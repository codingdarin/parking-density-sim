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
    }

    /// <summary>
    /// 계획 결과를 틱 단위로 재생하며 (1) 셀 충돌 검증 (2) 확보 완료 틱을 판정한다.
    /// 주의: lot.Grid를 변형(차량 제거)하므로 평가 후 lot은 재사용 불가 — 실행마다 새로 Build.
    /// </summary>
    public static class ClearanceEvaluator
    {
        public const int BudgetTicks = 168; // 7분 = 420초 / 2.5초

        public static ClearanceReport Evaluate(ParkingLot lot, EmergencyResult plan)
        {
            var g = lot.Grid;
            var report = new ClearanceReport { EndTick = plan.EndTick };
            var carById = lot.Cars.ToDictionary(c => c.Id);
            int robotCount = plan.Schedules.Length;
            int lastOccupied = -1;

            for (int t = 0; t <= report.EndTick; t++)
            {
                foreach (var kv in plan.CarLiftTicks)
                    if (kv.Value == t)
                        g.RemoveCar(carById[kv.Key]);

                var glyphs = new List<RobotGlyph>();
                for (int r = 0; r < robotCount; r++)
                {
                    var seg = SegmentAt(plan.Schedules[r], t);
                    var (x, y, carrying) = seg?.At(t) ?? (plan.Homes[r].X, plan.Homes[r].Y, false);
                    glyphs.Add(new RobotGlyph(r + 1, x, y, carrying));
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
