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
    /// 단지 A 전용구역 배치 반사실 — 골목 전면(기준선) vs 간선 재지정.
    /// S4b가 특정한 골목 인출 직렬화 병목을 접근축 재지정으로 우회할 수 있는지
    /// 공통 밀도 구간 N=0~12에서 4동 전수 비교한다. 프로파일은 Stanley 1m/s.
    /// </summary>
    public static class V2SiteZoneCounterfactualDemo
    {
        private const int SharedMaxN = 12;

        public static void Run()
        {
            PhysicalTimeProfileV2 profile = PublishedParkingRobotTimingV2.Create(1.0);
            var options = new EmergencyAccessRouteGenerationOptionsV2
            {
                MaxRoutes = 4,
                MaxCenterlineAttempts = 16,
                MaxSearchExpansions = 100000,
            };
            Console.WriteLine(
                "=== 단지 A 전용구역 배치 반사실: 골목 vs 간선, N=0~" +
                SharedMaxN + " ===");
            var rows = new List<string>
            {
                "zone_placement,blocking_vehicles,building,plan_success,within_7min," +
                "seconds,ticks,moved_vehicles,entrance,route,outcome,fail_reason",
            };
            var summary = new List<string>
            {
                "zone_placement,blocking_vehicles,all_within_7min,worst_building," +
                "worst_seconds,max_moved_vehicles",
            };

            foreach (SiteZonePlacementV2 placement in new[]
                     {
                         SiteZonePlacementV2.AlleyFrontage,
                         SiteZonePlacementV2.ArterialFrontage,
                     })
            {
                string name = placement == SiteZonePlacementV2.AlleyFrontage
                    ? "alley-baseline"
                    : "arterial-relocated";
                Console.WriteLine($"\n[{name}] N | 최악동 | 최악시간 | 이동 | 판정");
                int safeStreak = -1;
                for (int count = 0; count <= SharedMaxN; count++)
                {
                    ApartmentComplexScenarioV2 scenario =
                        SiteABlockScenarioFactoryV2.BuildDensity(
                            count,
                            profile.CreateOperationTiming(),
                            SiteStagingLayoutV2.SouthWestOnly,
                            placement);
                    var session = new ApartmentComplexPlanningSessionV2(
                        scenario,
                        activeRobotCount: 4,
                        generationOptions: options,
                        maxTick: 5000,
                        enableLowerBoundPruning: true);
                    var trials = new List<ApartmentComplexDensityTrialV2>();
                    foreach (ApartmentBuildingV2 building in scenario.Buildings)
                    {
                        ApartmentComplexDensityTrialV2 trial =
                            ApartmentComplexDensitySweepV2.Evaluate(
                                scenario,
                                session,
                                building.Id,
                                includeSecondaryEntrances: true,
                                timeProfile: profile);
                        trials.Add(trial);
                        rows.Add(string.Join(",",
                            name,
                            count,
                            trial.BuildingId,
                            trial.PlanSuccess ? 1 : 0,
                            trial.WithinBudget ? 1 : 0,
                            trial.Seconds.ToString("F1", CultureInfo.InvariantCulture),
                            trial.Ticks,
                            trial.MovedVehicleCount,
                            trial.SelectedEntrance ?? "",
                            trial.SelectedRoute ?? "",
                            trial.Outcome,
                            (trial.FailReason ?? "").Replace(',', ';')));
                    }
                    bool allWithin = trials.All(trial =>
                        trial.PlanSuccess && trial.WithinBudget);
                    if (allWithin && safeStreak == count - 1) safeStreak = count;
                    ApartmentComplexDensityTrialV2 worst = trials
                        .Where(trial => trial.PlanSuccess)
                        .OrderByDescending(trial => trial.Seconds)
                        .ThenBy(trial => trial.BuildingId)
                        .FirstOrDefault();
                    int maxMoved = trials.Max(trial => trial.MovedVehicleCount);
                    Console.WriteLine(
                        worst == null
                            ? $"{count,2} |      - | 계획 실패 |    - | 실패"
                            : $"{count,2} | {worst.BuildingId,4}동 | " +
                              $"{worst.Seconds,7:0.0}초 | {maxMoved,3}대 | " +
                              (allWithin ? "안전" : "초과"));
                    summary.Add(string.Join(",",
                        name,
                        count,
                        allWithin ? 1 : 0,
                        worst == null ? "" : worst.BuildingId.ToString(),
                        worst == null
                            ? ""
                            : worst.Seconds.ToString(
                                "F1", CultureInfo.InvariantCulture),
                        maxMoved));
                }
                Console.WriteLine(
                    $"{name}: 연속 4동 7분 안전 최대 N = " +
                    (safeStreak < 0 ? "없음" : safeStreak.ToString()) +
                    $" / {SharedMaxN}면");
            }

            string path = OutputDir.Resolve("v2_site_zone.csv");
            File.WriteAllLines(path, rows);
            string summaryPath = OutputDir.Resolve("v2_site_zone_summary.csv");
            File.WriteAllLines(summaryPath, summary);
            Console.WriteLine("\nCSV: " + path);
            Console.WriteLine("CSV: " + summaryPath);
        }
    }
}
