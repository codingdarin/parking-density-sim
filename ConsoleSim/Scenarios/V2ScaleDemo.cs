using System;
using ParkingSim.Core.V2;

namespace ParkingSim.Scenarios
{
    public static class V2ScaleDemo
    {
        public static void RunQualityGate()
        {
            PlannerQualityReportV2 report = PlannerQualityEvaluatorV2.EvaluateLineRolling();
            Console.WriteLine("=== Model V2 운영 후보 품질 게이트: rolling(창3) vs 전역 exact ===");
            Console.WriteLine("차량 | exact | rolling | 격차 | exact확장 | rolling확장 | 판정");
            Console.WriteLine(new string('-', 78));
            foreach (PlannerQualityRowV2 row in report.Rows)
            {
                Console.WriteLine(
                    $" {row.VehicleCount,2}  | {FormatTicks(row.ExactSuccess, row.ExactTicks),5} |" +
                    $" {FormatTicks(row.CandidateSuccess, row.CandidateTicks),7} |" +
                    $" {(row.ExactSuccess && row.CandidateSuccess ? row.GapPercent.ToString("+0.0;-0.0;0.0") + "%" : "-"),6} |" +
                    $" {row.ExactExpandedStates,10:N0} | {row.CandidateExpandedStates,12:N0} | " +
                    (row.WithinTolerance ? "통과" : "실패: " + row.FailReason));
            }
            Console.WriteLine("최종 판정: " +
                              (report.AllWithinTolerance ? "운영 후보 채택 가능" : "채택 보류 — 후보 플래너 교정 필요"));
        }

        public static void RunRolling(int vehicles)
        {
            if (vehicles < 2) vehicles = 2;
            var rolling = RollingBatchPlannerV2.Solve(
                V2ProblemFactory.LineProblem(vehicles),
                batchSize: 3,
                maxExpansionsPerBatch: 1000000);
            Console.WriteLine("=== Model V2 rolling-horizon exact decomposition ===");
            Console.WriteLine(
                $"차량={vehicles}, 성공={rolling.Success}, makespan={rolling.TotalTicks}틱, " +
                $"배치={rolling.BatchCount}[{string.Join("+", rolling.BatchSizes)}], 확장={rolling.ExpandedStates:N0}, " +
                $"슬롯={rolling.FinalStagingSlotIds?.Length ?? 0}, 사유={rolling.FailReason ?? "유효해"}");
        }

        public static void Run(int maxVehicles)
        {
            if (maxVehicles < 2) maxVehicles = 2;
            if (maxVehicles > 6) maxVehicles = 6;
            Console.WriteLine("=== Model V2 정확 정보탐색 규모 경계 (w=1) ===");
            Console.WriteLine("차량 | 성공 | makespan | 확장상태 | 비고");
            Console.WriteLine(new string('-', 58));
            for (int n = 2; n <= maxVehicles; n++)
            {
                var result = ExactEmergencySolverV2.SolveWeighted(
                    V2ProblemFactory.LineProblem(n),
                    heuristicWeight: 1,
                    maxExpansions: 1000000,
                    activeRobotCount: 2);
                Console.WriteLine(
                    $" {n,2}  | {(result.Success ? "성공" : "실패"),4} |" +
                    $" {(result.Success ? result.Ticks.ToString() + "틱" : "-"),8} |" +
                    $" {result.ExpandedStates,8:N0} | {result.FailReason ?? "exact"}");
                if (!result.Success)
                {
                    var bounded = ExactEmergencySolverV2.SolveBounded10Percent(
                        V2ProblemFactory.LineProblem(n), maxExpansions: 1000000);
                    Console.WriteLine(
                        $" {n,2}b | {(bounded.Success ? "성공" : "실패"),4} |" +
                        $" {(bounded.Success ? bounded.Ticks.ToString() + "틱" : "-"),8} |" +
                        $" {bounded.ExpandedStates,8:N0} | bounded≤10%: {bounded.FailReason ?? "해 발견"}");
                    var rolling = RollingBatchPlannerV2.Solve(
                        V2ProblemFactory.LineProblem(n), batchSize: 4, maxExpansionsPerBatch: 1000000);
                    Console.WriteLine(
                        $" {n,2}r | {(rolling.Success ? "성공" : "실패"),4} |" +
                        $" {(rolling.Success ? rolling.TotalTicks.ToString() + "틱" : "-"),8} |" +
                        $" {rolling.ExpandedStates,8:N0} | rolling {rolling.BatchCount}배치: {rolling.FailReason ?? "유효해"}");
                    if (!rolling.Success) break;
                }
            }
        }

        private static string FormatTicks(bool success, int ticks)
        {
            return success ? ticks + "틱" : "실패";
        }
    }
}
