using System;
using System.Collections.Generic;
using System.Linq;
using ParkingSim.Core.Agents;
using ParkingSim.Core.Grid;
using ParkingSim.Core.Pathfinding;

namespace ParkingSim.Core.Emergency
{
    public sealed class EmergencyConfig
    {
        public double FireMeters { get; set; }
        public int BetaCells { get; set; } = 4;      // β = 10m
        public int RobotCount { get; set; } = 4;     // 대기소 칸 수(= corridorStartX)로 상한 클램프
        public int MaxTick { get; set; } = 2000;
        public int DwellTicks { get; set; } = 12;    // 하차 유예 창 G (30초) — 체이닝 하차 직렬화 창
    }

    public sealed class EmergencyResult
    {
        public bool Success { get; set; }
        public string FailReason { get; set; }
        public List<RobotTimeline>[] Schedules { get; set; }
        public Dictionary<int, int> CarLiftTicks { get; set; }
        public (int X, int Y)[] Homes { get; set; }
        public int SectionEndX { get; set; }
        public int SectionCarCount { get; set; }
        /// <summary>계획 실패 후 재시도 횟수 — 혼잡·병렬성 붕괴의 독립 지표</summary>
        public int PlanFailures { get; set; }
        public int MainDrops { get; set; }
        public int PocketDrops { get; set; }

        public int EndTick => Schedules == null ? 0
            : Schedules.Max(s => s.Count == 0 ? 0 : s[s.Count - 1].EndTick);
    }

    /// <summary>
    /// D6 본 구현 — 로컬 체이닝 디스패처 (이벤트 구동).
    ///
    /// 설계 결정 (예행에서 도출):
    /// - 미션 완료 시점 계획: 가용 시각이 가장 빠른 로봇부터 다음 미션을 계획·확정.
    ///   오프라인 계획 + 결정론적 재생이므로 지연 전파는 구조적으로 없음.
    /// - 로컬 체이닝: 다음 미션은 하차 지점에서 즉시(avail 틱) 시작 — 홈 복귀 사이클이
    ///   포켓 이득을 상쇄하는 문제(예행 발견)를 해소. 대기는 A*의 in-plan wait가 담당.
    /// - 체이닝 중 주차 금지(parkAtEnd=false): 공유 셀 영구 예약은 기계획 미래 스텝과
    ///   충돌 검사가 안 되므로, 영구 주차는 흐름 밖 전용 대기소(depot)에서만.
    /// - 고착(비홈 위치에서 아무 계획도 불가) = 확보 실패로 정직하게 기록.
    /// </summary>
    public static class EmergencyPlanner
    {
        public static EmergencyResult Plan(ParkingLot lot, EmergencyConfig cfg)
        {
            var g = lot.Grid;
            int fireX = lot.CorridorStartX + (int)(cfg.FireMeters / GridMap.CellMeters);
            int sectionEnd = Math.Min(fireX + cfg.BetaCells, lot.CorridorEndX);
            var pending = lot.Cars
                .Where(c => c.InCorridor && c.X < sectionEnd)
                .OrderBy(c => c.X).ThenBy(c => c.Y)
                .ToList();
            var pockets = lot.PocketXs.Where(px => px < fireX).ToList(); // 화재 너머 적치 사용 불가

            int robotCount = Math.Max(1, Math.Min(cfg.RobotCount, lot.DepotCells.Count));
            var homes = lot.DepotCells.Take(robotCount).ToArray();

            int dwellTicks = cfg.DwellTicks; // 하차 유예 창 G — 다음 출발이 이 안에서 이뤄짐
            var rt = new ReservationTable();
            var liftTicks = new Dictionary<int, int>();
            var pos = ((int X, int Y)[])homes.Clone();
            var avail = new int[robotCount];
            var holdUntil = new int[robotCount]; // 하차 유예 창 만료 틱 (홈 주차 시 -1)
            var stuck = new bool[robotCount];
            var schedules = new List<RobotTimeline>[robotCount];
            for (int r = 0; r < robotCount; r++)
            {
                schedules[r] = new List<RobotTimeline>();
                rt.ReserveFrom(homes[r].X, homes[r].Y, 0);
            }

            var result = new EmergencyResult
            {
                Schedules = schedules,
                CarLiftTicks = liftTicks,
                Homes = homes,
                SectionEndX = sectionEnd,
                SectionCarCount = pending.Count,
            };

            while (pending.Count > 0)
            {
                int robot = Enumerable.Range(0, robotCount)
                    .Where(i => !stuck[i])
                    .OrderBy(i => avail[i]).ThenBy(i => i)
                    .DefaultIfEmpty(-1).First();
                if (robot < 0)
                {
                    result.Success = false;
                    result.FailReason = $"전 로봇 고착 — 잔여 {pending.Count}대 (확보 실패)";
                    return result;
                }

                bool assigned = false;
                for (int ci = 0; ci < pending.Count && !assigned; ci++)
                {
                    var car = pending[ci];
                    foreach (var drop in DropCandidates(car, pockets, lot))
                    {
                        bool parkedAtHome = pos[robot].X == homes[robot].X && pos[robot].Y == homes[robot].Y;
                        if (parkedAtHome) rt.ReleasePermanent(homes[robot].X, homes[robot].Y);
                        var hold = parkedAtHome
                            ? (((int X, int Y), int)?)null
                            : (pos[robot], holdUntil[robot]);

                        var tl = CooperativePlanner.PlanCarryMission(
                            g, rt, liftTicks, robot + 1, pos[robot], car, drop,
                            home: null, cfg.MaxTick, avail[robot],
                            parkAtEnd: false, dwellTicks: dwellTicks, selfHold: hold);
                        if (tl == null)
                        {
                            if (parkedAtHome) rt.ReserveFrom(homes[robot].X, homes[robot].Y, avail[robot]);
                            result.PlanFailures++;
                            continue;
                        }

                        schedules[robot].Add(tl);
                        pos[robot] = drop;
                        avail[robot] = tl.EndTick;
                        holdUntil[robot] = tl.EndTick + dwellTicks;
                        if (drop.X == 0) result.MainDrops++; else result.PocketDrops++;
                        pending.RemoveAt(ci);
                        for (int k = 0; k < robotCount; k++) stuck[k] = false; // 상황 변화 → 고착 해제
                        assigned = true;
                        break;
                    }
                }

                if (!assigned)
                {
                    // 이 로봇은 현시점 어떤 차·하차 후보로도 계획 불가 → 홈 복귀 후 재시도
                    if (pos[robot].X != homes[robot].X || pos[robot].Y != homes[robot].Y)
                    {
                        var back = CooperativePlanner.PlanRelocation(
                            g, rt, liftTicks, robot + 1, pos[robot], homes[robot], cfg.MaxTick, avail[robot],
                            selfHold: (pos[robot], holdUntil[robot]));
                        if (back != null)
                        {
                            schedules[robot].Add(back);
                            pos[robot] = homes[robot];
                            avail[robot] = back.EndTick;
                            continue;
                        }
                        // 비홈 위치 고착: 물리적으로 서 있을 곳이 보장 안 됨 → 정직한 실패
                        result.Success = false;
                        result.FailReason = $"로봇 {robot + 1} 고착 (비홈 위치, 이동 불가) — 확보 실패";
                        return result;
                    }
                    stuck[robot] = true;
                }
            }

            // 최종 후퇴: 대기소 밖에 있는 로봇을 자기 대기소로 (영구 주차는 대기소에서만)
            for (int r = 0; r < robotCount; r++)
            {
                if (pos[r].X == homes[r].X && pos[r].Y == homes[r].Y) continue;
                var back = CooperativePlanner.PlanRelocation(
                    g, rt, liftTicks, r + 1, pos[r], homes[r], cfg.MaxTick, avail[r],
                    selfHold: (pos[r], holdUntil[r]));
                if (back == null)
                {
                    result.Success = false;
                    result.FailReason = $"로봇 {r + 1} 최종 후퇴 실패 — 확보 실패";
                    return result;
                }
                schedules[r].Add(back);
                pos[r] = homes[r];
                avail[r] = back.EndTick;
            }

            result.Success = true;
            return result;
        }

        /// <summary>하차 후보를 이동 거리순으로 — 메인(서쪽 끝) + 화재 진입구 쪽 포켓.
        /// 최근접이 막혀 있으면(포켓 앵커 위 미제거 차량 등) 다음 후보로 폴백.</summary>
        private static IEnumerable<(int X, int Y)> DropCandidates(Car car, List<int> pockets, ParkingLot lot)
        {
            var candidates = new List<(int Dist, int X, int Y)>
            {
                (car.X, 0, car.Y), // 메인: 자기 레인의 적치 앵커
            };
            foreach (int px in pockets)
                candidates.Add((Math.Abs(car.X - px) + Math.Abs(car.Y - lot.LaneYs[0]), px, lot.LaneYs[0]));
            return candidates.OrderBy(c => c.Dist).ThenBy(c => c.X).Select(c => (c.X, c.Y));
        }
    }
}
