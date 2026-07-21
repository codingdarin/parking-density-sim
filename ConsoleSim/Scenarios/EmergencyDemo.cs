using System;
using System.Collections.Generic;
using System.Linq;
using ParkingSim.Core.Agents;
using ParkingSim.Core.Grid;
using ParkingSim.Core.Pathfinding;

namespace ParkingSim.Scenarios
{
    /// <summary>
    /// D6 예행: 화재 위치 입력 → 확보 구간 계산(+β) → 구간 내 통로 차량을
    /// "진입구 쪽 최근접 적치"로 재배치(미션 체이닝) → 전 로봇 후퇴 → 확보 완료 판정.
    /// 확보 완료 = 확보 구간의 통로 셀에 차량·로봇이 모두 없는 최초 틱 (측정정의서 §2).
    /// </summary>
    public static class EmergencyDemo
    {
        private const int BetaCells = 4;          // β = 10m
        private const int BudgetTicks = 168;      // 7분 = 420초 / 2.5초

        public static void Run(int occupiedLanes, double fireMeters, bool usePockets)
        {
            int[] pockets = usePockets ? new[] { 18, 28, 38 } : new int[0]; // 간격 25m
            var lot = ParkingLayoutBuilder.Build(new LayoutConfig
            {
                OccupiedLanes = occupiedLanes,
                StagingPocketXs = pockets,
            });
            var g = lot.Grid;

            int fireX = lot.CorridorStartX + (int)(fireMeters / GridMap.CellMeters);
            int sectionEnd = Math.Min(fireX + BetaCells, lot.CorridorEndX);
            var sectionCars = lot.Cars
                .Where(c => c.InCorridor && c.X < sectionEnd)
                .OrderBy(c => c.X).ThenBy(c => c.Y)
                .ToList();
            var usablePockets = lot.PocketXs.Where(px => px < fireX).ToList(); // 화재 너머 적치 사용 불가

            Console.WriteLine(
                $"=== D6 예행: 점유 {occupiedLanes}레인, 화재 {fireMeters}m(x={fireX}), " +
                $"확보 구간 x<{sectionEnd}, 대상 {sectionCars.Count}대, 사용 가능 포켓 {usablePockets.Count}개 ===");

            const int maxTick = 2000;
            var rt = new ReservationTable();
            var liftTicks = new Dictionary<int, int>();
            const int robotCount = 4;
            // 로봇별 고유 홈 = 대기소(depot, 적치 블록 아래) — 운반 흐름 밖이라 주차가 아무 경로도
            // 막지 않음. 매 미션 후 홈 복귀로 공유 하차 지점 주차 문제를 구조적으로 회피
            int depotY = 2 + lot.Config.CorridorLanes;
            var homes = new (int X, int Y)[] { (0, depotY), (1, depotY), (0, depotY + 1), (1, depotY + 1) };
            var avail = new int[robotCount];
            var schedules = new List<RobotTimeline>[robotCount];
            for (int r = 0; r < robotCount; r++)
            {
                schedules[r] = new List<RobotTimeline>();
                rt.ReserveFrom(homes[r].X, homes[r].Y, 0); // 출발 전 주차 상태
            }

            // 1) 배정 루프: 프런티어(서쪽)부터. 하차 후보는 거리순 폴백
            //    (포켓 앵커에 아직 안 치운 차가 있으면 그 포켓은 당장 막힘 → 메인으로 폴백)
            int delayRetries = 0, mainDrops = 0, pocketDrops = 0;
            foreach (var car in sectionCars)
            {
                bool assigned = false;
                foreach (var drop in DropCandidates(car, usablePockets, lot))
                {
                    foreach (int r in Enumerable.Range(0, robotCount).OrderBy(i => avail[i]).ThenBy(i => i))
                    {
                        foreach (int delay in new[] { 0, 5, 10, 20, 40 })
                        {
                            int startTick = avail[r] + delay;
                            rt.ReleasePermanent(homes[r].X, homes[r].Y);
                            var tl = CooperativePlanner.PlanCarryMission(
                                g, rt, liftTicks, r + 1, homes[r], car, drop, homes[r], maxTick, startTick);
                            if (tl == null)
                            {
                                rt.ReserveFrom(homes[r].X, homes[r].Y, avail[r]); // 주차 복원
                                delayRetries++;
                                continue;
                            }
                            for (int t = avail[r]; t < startTick; t++)
                                rt.ReserveStep(homes[r].X, homes[r].Y, t); // 유휴 구간 유한 예약
                            schedules[r].Add(tl);
                            avail[r] = tl.EndTick;
                            if (drop.X == 0) mainDrops++; else pocketDrops++;
                            assigned = true;
                            break;
                        }
                        if (assigned) break;
                    }
                    if (assigned) break;
                }
                if (!assigned)
                {
                    Console.WriteLine($"차량 {car.Id} 배정 실패 (전 하차 후보·로봇·지연 소진) → **확보 실패** 기록");
                    return;
                }
            }

            // 3) 재생 + 검증 + 확보 완료 판정
            int endTick = avail.Max();
            var carById = lot.Cars.ToDictionary(c => c.Id);
            int lastOccupied = -1, collisions = 0;
            for (int t = 0; t <= endTick; t++)
            {
                foreach (var kv in liftTicks)
                    if (kv.Value == t)
                        g.RemoveCar(carById[kv.Key]);

                var glyphs = new List<RobotGlyph>();
                for (int r = 0; r < robotCount; r++)
                {
                    var seg = SegmentAt(schedules[r], t);
                    var (x, y, carrying) = seg?.At(t) ?? (homes[r].X, homes[r].Y, false);
                    glyphs.Add(new RobotGlyph(r + 1, x, y, carrying));
                }

                collisions += Validate(g, glyphs, liftTicks, t);

                if (SectionOccupied(g, lot, sectionEnd, glyphs))
                    lastOccupied = t;
            }
            int clearTick = lastOccupied + 1;
            double clearSeconds = clearTick * GridMap.SecondsPerCell;

            // 4) 리포트
            Console.WriteLine($"확보 완료: t={clearTick} ≈ {clearSeconds:0}초 = {clearSeconds / 60:0.0}분" +
                              $" → 골든타임 7분(420초) 판정: {(clearTick <= BudgetTicks ? "✅ 충족" : "❌ 초과")}");
            Console.WriteLine($"검증: 셀 충돌 {collisions}건 | 지연 재시도 {delayRetries}회" +
                              $" | 하차 분포: 메인 {mainDrops}대 / 포켓 {pocketDrops}대 (S_필요 = {sectionCars.Count}대)");
            for (int r = 0; r < robotCount; r++)
            {
                var trips = schedules[r].Count(s => s.TargetCarId != 0);
                var waits = schedules[r].Sum(s => s.WaitTicks);
                Console.WriteLine($"  로봇 {r + 1}: {trips}대 운반, 대기 {waits}틱");
            }
            int n = sectionCars.Count;
            double envelope = occupiedLanes > 0
                ? n * ((sectionEnd - lot.CorridorStartX) * GridMap.CellMeters + 30) / 4.0
                : 0;
            Console.WriteLine($"봉투 예측(간섭 무시, 메인 적치 기준): ≈ {envelope:0}초 — 실측과의 차이가 혼잡 비용");
        }

        /// <summary>하차 후보를 이동 거리순으로 반환 — 메인(서쪽 끝) + 화재 진입구 쪽 포켓.
        /// 최근접이 막혀 있으면(예: 포켓 앵커 위에 아직 안 치운 차) 다음 후보로 폴백된다.</summary>
        private static IEnumerable<(int X, int Y)> DropCandidates(Car car, List<int> pockets, ParkingLot lot)
        {
            var candidates = new List<(int Dist, int X, int Y)>
            {
                (car.X, 0, car.Y), // 메인: 자기 레인의 적치 앵커
            };
            foreach (int px in pockets)
            {
                // 포켓 하차 앵커 = 포켓 아래 통로 셀 (싱크가 흡수 — 수직 회전은 추상화)
                candidates.Add((Math.Abs(car.X - px) + Math.Abs(car.Y - lot.LaneYs[0]), px, lot.LaneYs[0]));
            }
            return candidates.OrderBy(c => c.Dist).ThenBy(c => c.X).Select(c => (c.X, c.Y));
        }

        private static RobotTimeline SegmentAt(List<RobotTimeline> segments, int tick)
        {
            RobotTimeline current = null;
            foreach (var s in segments)
                if (s.StartTick <= tick) current = s;
                else break;
            return current ?? (segments.Count > 0 ? segments[0] : null);
        }

        /// <summary>확보 구간(통로 레인 × [시작, sectionEnd))에 차량 또는 로봇이 있는가</summary>
        private static bool SectionOccupied(GridMap g, ParkingLot lot, int sectionEnd, List<RobotGlyph> glyphs)
        {
            foreach (int y in lot.LaneYs)
                for (int x = lot.CorridorStartX; x < sectionEnd; x++)
                    if (g.IsOccupied(x, y))
                        return true;
            foreach (var r in glyphs)
            {
                var cells = r.Carrying ? new[] { (r.X, r.Y), r.SecondCell } : new[] { (r.X, r.Y) };
                foreach (var (cx, cy) in cells)
                    if (cx >= lot.CorridorStartX && cx < sectionEnd && lot.LaneYs.Contains(cy))
                        return true;
            }
            return false;
        }

        private static int Validate(GridMap g, List<RobotGlyph> glyphs, Dictionary<int, int> liftTicks, int tick)
        {
            int collisions = 0;
            var occupied = new Dictionary<(int, int), int>();
            foreach (var r in glyphs)
            {
                var cells = r.Carrying ? new[] { (r.X, r.Y), r.SecondCell } : new[] { (r.X, r.Y) };
                foreach (var cell in cells)
                {
                    int carId = g.CarAt(cell.Item1, cell.Item2);
                    // 도킹 예외: 적재 직전(리프트 1틱 전부터) 자기 대상 차량과의 겹침 허용
                    bool docking = carId != 0 && liftTicks.TryGetValue(carId, out int lift) && tick >= lift - 1;
                    if (carId != 0 && !docking)
                    {
                        Console.WriteLine($"!! t={tick}: 로봇 {r.Id} ↔ 차량 {carId} 충돌 @{cell}");
                        collisions++;
                    }
                    if (occupied.TryGetValue(cell, out int other))
                    {
                        Console.WriteLine($"!! t={tick}: 로봇 {r.Id} ↔ 로봇 {other} 충돌 @{cell}");
                        collisions++;
                    }
                    else
                    {
                        occupied[cell] = r.Id;
                    }
                }
            }
            return collisions;
        }
    }
}
