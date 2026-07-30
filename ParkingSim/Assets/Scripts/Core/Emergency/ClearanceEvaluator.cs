using System.Collections.Generic;
using System.Linq;
using ParkingSim.Core.Agents;
using ParkingSim.Core.Grid;

namespace ParkingSim.Core.Emergency
{
    public sealed class ClearanceReport
    {
        /// <summary>확보 완료 틱 — 확보 구간 통로 셀에 차량·로봇이 모두 없는 최초 틱</summary>
        public int ClearTick { get; set; }
        public double ClearSeconds => ClearTick * GridMap.SecondsPerCell;
        public bool WithinBudget { get; set; }
        public int Collisions { get; set; }
        public List<string> ConflictSamples { get; } = new List<string>();
        public int EndTick { get; set; }

        // 로봇-틱 분해 (전 로봇 합) — "왜 노는가"를 가른다: 병렬성 붕괴의 주범이
        // 통로 혼잡(주행대기)인지 하차 병목(하차대기)인지 진단. (측정정의서 밖 진단 지표)
        public long ActiveTicks { get; set; }     // 이동 or 적재/하차 액션
        public long DriveWaitTicks { get; set; }  // 통로/도로에서 정지 대기 (혼잡)
        public long DropWaitTicks { get; set; }   // 하차 지점(메인 앵커·포켓)에서 정지 (유예 창 직렬화)
        public long IdleTicks { get; set; }       // 미션 전/후·미배정 (완전유휴)
        public long TotalRobotTicks => ActiveTicks + DriveWaitTicks + DropWaitTicks + IdleTicks;
    }

    /// <summary>
    /// 계획 결과를 틱 단위로 재생하며 (1) 셀 충돌 검증 (2) 확보 완료 틱을 판정한다.
    /// 주의: lot.Grid를 변형(차량 제거)하므로 평가 후 lot은 재사용 불가 — 실행마다 새로 Build.
    /// </summary>
    public static class ClearanceEvaluator
    {
        public const int BudgetTicks = TimeBudget.BaselineTicks; // 7분 = 420초 / 2.5초

        public static ClearanceReport Evaluate(ParkingLot lot, EmergencyResult plan)
        {
            var g = lot.Grid;
            var report = new ClearanceReport { EndTick = plan.EndTick };
            var carById = lot.Cars.ToDictionary(c => c.Id);
            int robotCount = plan.Schedules.Length;
            int lastOccupied = -1;

            int T = report.EndTick;
            var depot = new HashSet<(int, int)>(lot.DepotCells);

            // 로봇별 위치열 + 다음-이동-셀(예견) 사전계산 — 대기 원인 태깅에 필요.
            // 대기 로봇이 다음에 진입하려던 셀이 하차앵커면 그 대기는 하차 경합(스필오버 포함).
            var pos = new (int X, int Y, bool C)[robotCount][];
            var nextCell = new (int X, int Y)[robotCount][];
            var firstActive = new int[robotCount];
            var lastActive = new int[robotCount];
            for (int r = 0; r < robotCount; r++)
            {
                pos[r] = new (int, int, bool)[T + 1];
                for (int t = 0; t <= T; t++)
                {
                    var seg = SegmentAt(plan.Schedules[r], t);
                    pos[r][t] = seg?.At(t) ?? (plan.Homes[r].X, plan.Homes[r].Y, false);
                }
                nextCell[r] = new (int, int)[T + 1];
                var nd = (-1, -1);
                nextCell[r][T] = nd;
                for (int t = T - 1; t >= 0; t--)
                {
                    if (pos[r][t + 1].X != pos[r][t].X || pos[r][t + 1].Y != pos[r][t].Y)
                        nd = (pos[r][t + 1].X, pos[r][t + 1].Y);
                    nextCell[r][t] = nd;
                }
                var sch = plan.Schedules[r];
                firstActive[r] = sch.Count > 0 ? sch[0].StartTick : int.MaxValue;
                lastActive[r] = sch.Count > 0 ? sch[sch.Count - 1].EndTick : -1;
            }

            var prev = new (int X, int Y)[robotCount];
            for (int r = 0; r < robotCount; r++) prev[r] = plan.Homes[r];

            for (int t = 0; t <= T; t++)
            {
                foreach (var kv in plan.CarLiftTicks)
                    if (kv.Value == t)
                        g.RemoveCar(carById[kv.Key]);

                var glyphs = new List<RobotGlyph>();
                for (int r = 0; r < robotCount; r++)
                {
                    var (x, y, carrying) = pos[r][t];
                    glyphs.Add(new RobotGlyph(r + 1, x, y, carrying));

                    var seg = SegmentAt(plan.Schedules[r], t);
                    bool moved = x != prev[r].X || y != prev[r].Y;
                    bool action = seg != null && (t == seg.LiftTick || t == seg.DropTick);
                    if (moved || action) report.ActiveTicks++;
                    else if (t < firstActive[r] || t > lastActive[r] || depot.Contains((x, y)))
                        report.IdleTicks++;                                  // 완전유휴
                    else if (IsDropLocation(lot, x, y) ||
                             IsDropLocation(lot, nextCell[r][t].X, nextCell[r][t].Y))
                        report.DropWaitTicks++;   // 하차대기 — 앵커에서 정지 or 앵커 진입 대기(스필오버)
                    else report.DriveWaitTicks++; // 순수 주행대기 — 타 로봇 주행 경로에 막힘
                    prev[r] = (x, y);
                }

                Validate(g, glyphs, plan.CarLiftTicks, t, report);

                if (SectionOccupied(g, lot, plan.SectionEndX, glyphs))
                    lastOccupied = t;
            }

            report.ClearTick = lastOccupied + 1;
            report.WithinBudget = plan.Success && report.ClearTick <= BudgetTicks;
            return report;
        }

        private static RobotTimeline SegmentAt(List<RobotTimeline> segments, int tick)
        {
            RobotTimeline current = null;
            foreach (var s in segments)
                if (s.StartTick <= tick) current = s;
                else break;
            return current ?? (segments.Count > 0 ? segments[0] : null);
        }

        /// <summary>하차 지점 판정: 메인 적치 앵커(x=0, 레인 행) 또는 통로변 포켓 하차 셀(포켓 x, 최상단 레인).
        /// 이 위치에서의 정지는 하차 직렬화(유예 창) 대기로 계수.</summary>
        private static bool IsDropLocation(ParkingLot lot, int x, int y)
        {
            if (x == 0 && lot.LaneYs.Contains(y)) return true;
            return y == lot.LaneYs[0] && lot.PocketXs.Contains(x);
        }

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

        private static void Validate(
            GridMap g, List<RobotGlyph> glyphs, Dictionary<int, int> liftTicks, int tick, ClearanceReport report)
        {
            var occupied = new Dictionary<(int, int), int>();
            foreach (var r in glyphs)
            {
                var cells = r.Carrying ? new[] { (r.X, r.Y), r.SecondCell } : new[] { (r.X, r.Y) };
                foreach (var cell in cells)
                {
                    int carId = g.CarAt(cell.Item1, cell.Item2);
                    // 도킹 예외: 적재 직전(리프트 1틱 전)부터 자기 대상 차량과의 겹침 허용
                    bool docking = carId != 0 && liftTicks.TryGetValue(carId, out int lift) && tick >= lift - 1;
                    if (carId != 0 && !docking)
                        Record(report, $"t={tick}: 로봇 {r.Id} ↔ 차량 {carId} @{cell}");
                    if (occupied.TryGetValue(cell, out int other))
                        Record(report, $"t={tick}: 로봇 {r.Id} ↔ 로봇 {other} @{cell}");
                    else
                        occupied[cell] = r.Id;
                }
            }
        }

        private static void Record(ClearanceReport report, string message)
        {
            report.Collisions++;
            if (report.ConflictSamples.Count < 10)
                report.ConflictSamples.Add(message);
        }
    }
}
