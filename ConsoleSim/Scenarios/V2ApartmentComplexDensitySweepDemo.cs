using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using ParkingSim.Core.V2;

namespace ParkingSim.Scenarios
{
    public static class V2ApartmentComplexDensitySweepDemo
    {
        public static void Run()
        {
            PhysicalTimeProfileV2 profile =
                PublishedParkingRobotTimingV2.Create(1.0);
            var options = new EmergencyAccessRouteGenerationOptionsV2
            {
                MaxRoutes = 4,
                MaxCenterlineAttempts = 16,
                MaxSearchExpansions = 100000,
            };
            ApartmentComplexScenarioV2 geometry =
                ApartmentComplexScenarioFactoryV2.BuildDensity(
                    0, profile.CreateOperationTiming());
            var routeCatalog = new ApartmentComplexRouteCatalogV2(
                geometry, options);
            var trials = new List<ApartmentComplexDensityTrialV2>();
            int physicalAttempts = 0;
            int physicalPlans = 0;
            int prunedPlans = 0;
            var stopwatch = Stopwatch.StartNew();

            Console.WriteLine(
                "=== 8동 단지 가변 차량 밀도 스윕: N=0~22, 서문+동문 ===");
            Console.WriteLine("N | 최악동 | 최악시간 | 이동 | 8동 전체");
            for (int count = 0;
                 count <= ApartmentComplexScenarioFactoryV2.MaximumBlockingVehicles;
                 count++)
            {
                ApartmentComplexScenarioV2 scenario =
                    ApartmentComplexScenarioFactoryV2.BuildDensity(
                        count, profile.CreateOperationTiming());
                var session = new ApartmentComplexPlanningSessionV2(
                    scenario,
                    activeRobotCount: 4,
                    generationOptions: options,
                    maxTick: 5000,
                    routeCatalog: routeCatalog,
                    enableLowerBoundPruning: true);
                var densityRows = new List<ApartmentComplexDensityTrialV2>();
                foreach (ApartmentBuildingV2 building in scenario.Buildings)
                {
                    ApartmentComplexDensityTrialV2 trial =
                        ApartmentComplexDensitySweepV2.Evaluate(
                            scenario,
                            session,
                            building.Id,
                            includeSecondaryEntrances: true,
                            timeProfile: profile);
                    densityRows.Add(trial);
                    trials.Add(trial);
                }
                physicalAttempts += session.PhysicalAttemptCount;
                physicalPlans += session.PhysicalPlanCount;
                prunedPlans += session.PhysicalPlanPrunedCount;

                ApartmentComplexDensityTrialV2 worst = densityRows
                    .Where(row => row.PlanSuccess)
                    .OrderByDescending(row => row.Seconds)
                    .ThenByDescending(row => row.MovedVehicleCount)
                    .ThenBy(row => row.BuildingId)
                    .FirstOrDefault();
                bool allPlan = densityRows.All(row => row.PlanSuccess);
                bool allWithin = allPlan && densityRows.All(row => row.WithinBudget);
                Console.WriteLine(
                    worst == null
                        ? $"{count,2} |      - | 계획 실패 |    - | 실패"
                        : $"{count,2} | {worst.BuildingId,6} | " +
                          $"{worst.Seconds,7:0.0}초 | " +
                          $"{worst.MovedVehicleCount,4} | " +
                          (allWithin
                              ? "통과"
                              : allPlan ? "시간 초과" : "계획 실패"));
            }
            stopwatch.Stop();

            int safeThrough = -1;
            foreach (IGrouping<int, ApartmentComplexDensityTrialV2> group in
                trials.GroupBy(row => row.BlockingVehicleCount)
                    .OrderBy(group => group.Key))
            {
                if (group.Key != safeThrough + 1 ||
                    !group.All(row => row.PlanSuccess && row.WithinBudget))
                    break;
                safeThrough = group.Key;
            }
            int? firstTimeExceeded = trials
                .Where(row =>
                    row.Outcome ==
                    ApartmentComplexDensityOutcomeV2.TimeBudgetExceeded)
                .Select(row => (int?)row.BlockingVehicleCount)
                .Min();
            int? firstPlanFailure = trials
                .Where(row => !row.PlanSuccess)
                .Select(row => (int?)row.BlockingVehicleCount)
                .Min();

            Console.WriteLine(
                $"연속 8동 7분 안전 최대 N={safeThrough}/" +
                $"{ApartmentComplexScenarioFactoryV2.MaximumBlockingVehicles}");
            Console.WriteLine(
                "첫 시간 초과 N=" +
                (firstTimeExceeded.HasValue
                    ? firstTimeExceeded.Value.ToString(CultureInfo.InvariantCulture)
                    : "없음") +
                ", 첫 계획 실패 N=" +
                (firstPlanFailure.HasValue
                    ? firstPlanFailure.Value.ToString(CultureInfo.InvariantCulture)
                    : "없음"));
            Console.WriteLine(
                $"후보 생성 {routeCatalog.GenerationCount}회, " +
                $"입구 물리 시도 {physicalAttempts}회, " +
                $"후보 물리계획 {physicalPlans}회, " +
                $"하한 가지치기 {prunedPlans}회, " +
                $"경과 {stopwatch.Elapsed.TotalSeconds:0.0}초");
            WriteCsv(trials);
        }

        private static void WriteCsv(
            IReadOnlyList<ApartmentComplexDensityTrialV2> trials)
        {
            Directory.CreateDirectory("output");
            string detailPath =
                Path.Combine("output", "v2_apartment_complex_density.csv");
            using (var writer = new StreamWriter(detailPath, false))
            {
                writer.WriteLine(
                    "blocking_vehicles,building_id,selected_entrance," +
                    "selected_route,candidates,moved_vehicles,ticks," +
                    "expanded_states,seconds,plan_success,within_7_minutes," +
                    "outcome,fail_reason");
                foreach (ApartmentComplexDensityTrialV2 row in trials)
                {
                    writer.WriteLine(string.Join(",", new[]
                    {
                        Number(row.BlockingVehicleCount),
                        Number(row.BuildingId),
                        Escape(row.SelectedEntrance),
                        Escape(row.SelectedRoute),
                        Number(row.CandidateCount),
                        Number(row.MovedVehicleCount),
                        Number(row.Ticks),
                        Number(row.ExpandedStates),
                        row.Seconds.ToString("0.0", CultureInfo.InvariantCulture),
                        row.PlanSuccess ? "true" : "false",
                        row.WithinBudget ? "true" : "false",
                        row.Outcome.ToString(),
                        Escape(row.FailReason),
                    }));
                }
            }

            string summaryPath =
                Path.Combine("output", "v2_apartment_complex_density_summary.csv");
            using (var writer = new StreamWriter(summaryPath, false))
            {
                writer.WriteLine(
                    "blocking_vehicles,all_plan_success,all_within_7_minutes," +
                    "worst_building,worst_seconds,max_moved_vehicles");
                foreach (IGrouping<int, ApartmentComplexDensityTrialV2> group in
                    trials.GroupBy(row => row.BlockingVehicleCount)
                        .OrderBy(group => group.Key))
                {
                    ApartmentComplexDensityTrialV2 worst = group
                        .Where(row => row.PlanSuccess)
                        .OrderByDescending(row => row.Seconds)
                        .ThenBy(row => row.BuildingId)
                        .FirstOrDefault();
                    writer.WriteLine(string.Join(",", new[]
                    {
                        Number(group.Key),
                        group.All(row => row.PlanSuccess) ? "true" : "false",
                        group.All(row => row.PlanSuccess && row.WithinBudget)
                            ? "true"
                            : "false",
                        worst == null ? string.Empty : Number(worst.BuildingId),
                        worst == null
                            ? string.Empty
                            : worst.Seconds.ToString(
                                "0.0", CultureInfo.InvariantCulture),
                        Number(group.Max(row => row.MovedVehicleCount)),
                    }));
                }
            }
            Console.WriteLine("CSV: " + detailPath);
            Console.WriteLine("CSV: " + summaryPath);
        }

        private static string Number(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static string Escape(string value)
        {
            if (value == null) return string.Empty;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
