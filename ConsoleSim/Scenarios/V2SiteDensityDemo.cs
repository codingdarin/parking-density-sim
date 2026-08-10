using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ParkingSim.Core;
using ParkingSim.Core.V2;

namespace ParkingSim.Scenarios
{
    /// <summary>
    /// 실제 단지 A(익명화) 발췌 블록의 밀도·유닛 스윕.
    /// 이중주차 N=0~16 × 4개 동 × 서문+동문 전수, 이어서 연속 4동 7분 안전 최대 N에서
    /// 가용 유닛 4~1조. 프로파일·집계 관례는 v2complexdensity와 동일(Stanley 1m/s).
    /// </summary>
    public static class V2SiteDensityDemo
    {
        public static void Run()
        {
            PhysicalTimeProfileV2 profile = PublishedParkingRobotTimingV2.Create(1.0);
            var options = new EmergencyAccessRouteGenerationOptionsV2
            {
                MaxRoutes = 4,
                MaxCenterlineAttempts = 16,
                MaxSearchExpansions = 100000,
            };
            ApartmentComplexScenarioV2 geometry =
                SiteABlockScenarioFactoryV2.BuildDensity(
                    0, profile.CreateOperationTiming());
            var routeCatalog = new ApartmentComplexRouteCatalogV2(geometry, options);
            var trials = new List<ApartmentComplexDensityTrialV2>();

            Console.WriteLine(
                "=== 실제 단지 A 발췌 블록 밀도 스윕: 이중주차 N=0~16, 서문+동문 ===");
            Console.WriteLine("N | 최악동 | 최악시간 | 이동 | 4동 전체");
            int continuousSafeMax = -1;
            bool streak = true;
            for (int count = 0;
                 count <= SiteABlockScenarioFactoryV2.MaximumBlockingVehicles;
                 count++)
            {
                ApartmentComplexScenarioV2 scenario =
                    SiteABlockScenarioFactoryV2.BuildDensity(
                        count, profile.CreateOperationTiming());
                var session = new ApartmentComplexPlanningSessionV2(
                    scenario,
                    activeRobotCount: 4,
                    generationOptions: options,
                    maxTick: 5000,
                    routeCatalog: routeCatalog,
                    enableLowerBoundPruning: true);
                var rows = new List<ApartmentComplexDensityTrialV2>();
                foreach (ApartmentBuildingV2 building in scenario.Buildings)
                {
                    ApartmentComplexDensityTrialV2 trial =
                        ApartmentComplexDensitySweepV2.Evaluate(
                            scenario,
                            session,
                            building.Id,
                            includeSecondaryEntrances: true,
                            timeProfile: profile);
                    rows.Add(trial);
                    trials.Add(trial);
                }
                bool allPlan = rows.All(row => row.PlanSuccess);
                bool allWithin = allPlan && rows.All(row => row.WithinBudget);
                if (streak && allWithin) continuousSafeMax = count;
                else streak = false;
                ApartmentComplexDensityTrialV2 worst = rows
                    .Where(row => row.PlanSuccess)
                    .OrderByDescending(row => row.Seconds)
                    .ThenBy(row => row.BuildingId)
                    .FirstOrDefault();
                Console.WriteLine(
                    worst == null
                        ? $"{count,2} |    - | 계획 실패 |  - | 실패"
                        : $"{count,2} | {worst.BuildingId,4}동 | {worst.Seconds,7:0.0}초 | " +
                          $"{worst.MovedVehicleCount,2}대 | " +
                          (allWithin ? "안전" : allPlan ? "초과" : "불능"));
            }

            WriteRows(trials, "v2_site_density.csv");
            WriteSummary(trials, "v2_site_density_summary.csv");
            Console.WriteLine(
                $"\n연속 4동 7분 안전 최대 이중주차 N = " +
                (continuousSafeMax < 0 ? "없음" : continuousSafeMax.ToString()) +
                $" / {SiteABlockScenarioFactoryV2.MaximumBlockingVehicles}면");

            // ── 유닛 스윕: 안전 최대 N(없으면 0)에서 4~1조 ──
            int unitSweepN = Math.Max(0, continuousSafeMax);
            Console.WriteLine($"\n[가용 유닛 스윕 — N={unitSweepN}] 유닛 | 4동 7분 | 최악동 | 최악시간");
            var unitCsv = new List<string>
            {
                "available_units,blocking_vehicles,all_within_7min,worst_building,worst_seconds",
            };
            ApartmentComplexScenarioV2 unitScenario =
                SiteABlockScenarioFactoryV2.BuildDensity(
                    unitSweepN, profile.CreateOperationTiming());
            for (int units = 4; units >= 1; units--)
            {
                var session = new ApartmentComplexPlanningSessionV2(
                    unitScenario,
                    activeRobotCount: units,
                    generationOptions: options,
                    maxTick: 5000,
                    routeCatalog: routeCatalog,
                    enableLowerBoundPruning: true);
                var rows = new List<ApartmentComplexDensityTrialV2>();
                foreach (ApartmentBuildingV2 building in unitScenario.Buildings)
                    rows.Add(ApartmentComplexDensitySweepV2.Evaluate(
                        unitScenario, session, building.Id,
                        includeSecondaryEntrances: true, timeProfile: profile));
                bool allWithin = rows.All(row =>
                    row.PlanSuccess && row.WithinBudget);
                ApartmentComplexDensityTrialV2 worst = rows
                    .Where(row => row.PlanSuccess)
                    .OrderByDescending(row => row.Seconds)
                    .ThenBy(row => row.BuildingId)
                    .FirstOrDefault();
                Console.WriteLine(
                    $"{units}조 | {(allWithin ? "통과" : "실패")} | " +
                    (worst == null
                        ? "- | 계획 실패"
                        : $"{worst.BuildingId}동 | {worst.Seconds:0.0}초"));
                unitCsv.Add(string.Join(",",
                    units,
                    unitSweepN,
                    allWithin ? 1 : 0,
                    worst == null ? "" : worst.BuildingId.ToString(),
                    worst == null
                        ? ""
                        : worst.Seconds.ToString("F1", CultureInfo.InvariantCulture)));
            }
            string unitPath = OutputDir.Resolve("v2_site_units.csv");
            File.WriteAllLines(unitPath, unitCsv);
            Console.WriteLine("CSV: " + unitPath);
        }

        private static void WriteRows(
            List<ApartmentComplexDensityTrialV2> trials, string fileName)
        {
            var csv = new List<string>
            {
                "blocking_vehicles,building,plan_success,within_7min,seconds,ticks," +
                "moved_vehicles,entrance,route,candidates,outcome,fail_reason",
            };
            foreach (ApartmentComplexDensityTrialV2 trial in trials)
                csv.Add(string.Join(",",
                    trial.BlockingVehicleCount,
                    trial.BuildingId,
                    trial.PlanSuccess ? 1 : 0,
                    trial.WithinBudget ? 1 : 0,
                    trial.Seconds.ToString("F1", CultureInfo.InvariantCulture),
                    trial.Ticks,
                    trial.MovedVehicleCount,
                    trial.SelectedEntrance ?? "",
                    trial.SelectedRoute ?? "",
                    trial.CandidateCount,
                    trial.Outcome,
                    (trial.FailReason ?? "").Replace(',', ';')));
            string path = OutputDir.Resolve(fileName);
            File.WriteAllLines(path, csv);
            Console.WriteLine("CSV: " + path);
        }

        private static void WriteSummary(
            List<ApartmentComplexDensityTrialV2> trials, string fileName)
        {
            var csv = new List<string>
            {
                "blocking_vehicles,all_plan_success,all_within_7_minutes," +
                "worst_building,worst_seconds,max_moved_vehicles",
            };
            foreach (var group in trials
                         .GroupBy(trial => trial.BlockingVehicleCount)
                         .OrderBy(group => group.Key))
            {
                ApartmentComplexDensityTrialV2 worst = group
                    .Where(trial => trial.PlanSuccess)
                    .OrderByDescending(trial => trial.Seconds)
                    .ThenBy(trial => trial.BuildingId)
                    .FirstOrDefault();
                csv.Add(string.Join(",",
                    group.Key,
                    group.All(trial => trial.PlanSuccess),
                    group.All(trial => trial.PlanSuccess && trial.WithinBudget),
                    worst == null ? "" : worst.BuildingId.ToString(),
                    worst == null
                        ? ""
                        : worst.Seconds.ToString("F1", CultureInfo.InvariantCulture),
                    group.Max(trial => trial.MovedVehicleCount)));
            }
            string path = OutputDir.Resolve(fileName);
            File.WriteAllLines(path, csv);
            Console.WriteLine("CSV: " + path);
        }
    }
}
