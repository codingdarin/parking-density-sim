using System;
using ParkingSim.Core.V2;

namespace ParkingSim.Scenarios
{
    public static class V2ScaleDemo
    {
        public static void RunQualityGate()
        {
            PlannerQualityReportV2 report = PlannerQualityEvaluatorV2.EvaluateLineRolling();
            PrintQualityReport("rolling(창3)", report);
        }

        public static void RunPipelineQualityGate()
        {
            PlannerQualityReportV2 report = PlannerQualityEvaluatorV2.EvaluateLinePipelined();
            PrintQualityReport("pipelined-prioritized", report);
        }

        public static void RunPipelineDetail(int vehicles)
        {
            if (vehicles < 2) vehicles = 2;
            PipelinedPlanResultV2 result = PipelinedPrioritizedPlannerV2.Solve(
                V2ProblemFactory.LineProblem(vehicles));
            Console.WriteLine($"=== pipeline detail: 차량={vehicles}, 성공={result.Success}, " +
                              $"makespan={result.Ticks}, 유효={result.PhysicallyValid}, 확장={result.ExpandedStates} ===");
            foreach (PipelinedMissionV2 mission in result.Missions)
                Console.WriteLine($"r{mission.RobotIndex + 1} v{mission.VehicleIndex + 1} → s{mission.DestinationSlot}: " +
                                  $"start={mission.StartTick}, lift={mission.LiftTick}, drop={mission.DropTick}");
            for (int r = 0; r < 2; r++)
            {
                TimedRobotStateV2 end = result.RobotTimelines[r][result.RobotTimelines[r].Count - 1];
                Console.WriteLine($"r{r + 1} end=({end.X},{end.Y})@{end.Tick}");
            }
            if (!result.Success) Console.WriteLine("사유=" + result.FailReason);
        }

        public static void RunPipelineBlock()
        {
            EmergencyProblemV2 problem = V2MapCatalog.SmallParkingBlock.Build();
            ExactEmergencyResultV2 exact = ExactEmergencySolverV2.SolveWeighted(
                problem, 1, 1000000, 2);
            PipelinedPlanResultV2 candidate = PipelinedPrioritizedPlannerV2.Solve(
                V2MapCatalog.SmallParkingBlock.Build());
            double gap = exact.Success && candidate.Success
                ? 100.0 * (candidate.Ticks - exact.Ticks) / exact.Ticks
                : double.NaN;
            Console.WriteLine("=== small-parking-block: pipeline vs exact ===");
            Console.WriteLine($"exact={exact.Success}/{exact.Ticks}틱/{exact.ExpandedStates:N0}상태");
            Console.WriteLine($"pipeline={candidate.Success}/{candidate.Ticks}틱/" +
                              $"{candidate.ExpandedStates:N0}상태/유효={candidate.PhysicallyValid}/gap={gap:F1}%");
            foreach (PipelinedMissionV2 mission in candidate.Missions)
                Console.WriteLine($"r{mission.RobotIndex + 1} v{mission.VehicleIndex + 1} → " +
                                  $"s{mission.DestinationSlot}, lift={mission.LiftTick}, drop={mission.DropTick}");
            if (!candidate.Success) Console.WriteLine("사유=" + candidate.FailReason);
        }

        public static void RunPipelineApartment()
        {
            EmergencyProblemV2 map = V2MapCatalog.ApartmentAislePrototype.Build();
            var scenario = new EmergencyScenarioV2(
                "apartment-full-clearance",
                fireCell: (17, 5),
                requiredClearanceCells: map.CopyClearanceCells());
            EmergencyScenarioBuildResultV2 built = scenario.Build(map);
            Console.WriteLine("=== apartment-aisle-prototype first operational gate ===");
            Console.WriteLine($"scenario={built.Success}, selected={built.SelectedVehicleCount}, " +
                              $"fixed={map.FixedVehiclePoses.Count}, reason={built.FailReason ?? "valid"}");
            if (!built.Success) return;
            PipelinedPlanResultV2 candidate = PipelinedPrioritizedPlannerV2.Solve(built.Problem);
            Console.WriteLine($"pipeline={candidate.Success}, ticks={candidate.Ticks}, " +
                              $"expanded={candidate.ExpandedStates:N0}, valid={candidate.PhysicallyValid}, " +
                              $"reason={candidate.FailReason ?? "valid"}");
            foreach (PipelinedMissionV2 mission in candidate.Missions)
                Console.WriteLine($"r{mission.RobotIndex + 1} v{mission.VehicleIndex + 1} → " +
                                  $"s{mission.DestinationSlot}, lift={mission.LiftTick}, drop={mission.DropTick}");
        }

        public static void RunPipelineConstrainedApartment()
        {
            EmergencyProblemV2 map = V2MapCatalog.ApartmentConstrainedPrototype.Build();
            var scenario = new EmergencyScenarioV2(
                "apartment-constrained-clearance",
                fireCell: (19, 5),
                requiredClearanceCells: map.CopyClearanceCells());
            EmergencyScenarioBuildResultV2 built = scenario.Build(map);
            Console.WriteLine("=== apartment-constrained-prototype operational gate ===");
            Console.WriteLine($"scenario={built.Success}, selected={built.SelectedVehicleCount}, " +
                              $"fixed={map.FixedVehiclePoses.Count}, reason={built.FailReason ?? "valid"}");
            if (!built.Success) return;
            PipelinedPlanResultV2 candidate = PipelinedPrioritizedPlannerV2.Solve(built.Problem);
            Console.WriteLine($"pipeline={candidate.Success}, ticks={candidate.Ticks}, " +
                              $"expanded={candidate.ExpandedStates:N0}, valid={candidate.PhysicallyValid}, " +
                              $"reason={candidate.FailReason ?? "valid"}");
            foreach (PipelinedMissionV2 mission in candidate.Missions)
                Console.WriteLine($"r{mission.RobotIndex + 1} v{mission.VehicleIndex + 1} → " +
                                  $"s{mission.DestinationSlot}, lift={mission.LiftTick}, drop={mission.DropTick}");
        }

        private static void PrintQualityReport(string candidateName, PlannerQualityReportV2 report)
        {
            Console.WriteLine("=== Model V2 운영 후보 품질 게이트: " + candidateName + " vs 전역 exact ===");
            Console.WriteLine("차량 | exact | 후보 | 격차 | exact확장 | 후보확장 | 판정");
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
