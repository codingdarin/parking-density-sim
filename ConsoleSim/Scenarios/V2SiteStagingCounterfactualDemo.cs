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
    /// 단지 A 적치 배치 반사실 실험 (S4b) — 기준선(남서 편중 12면) vs
    /// 재배치안(총 12면 유지, 남 6+동 6) vs 확장안(남 12+동 6).
    /// 재배치안이 개선되면 효과는 용량이 아니라 "배치"에서 온다는 것이 분리 증명된다.
    /// 프로파일·집계 관례는 v2site와 동일(Stanley 1m/s, 서문+동문).
    /// </summary>
    public static class V2SiteStagingCounterfactualDemo
    {
        private sealed class LayoutResult
        {
            public SiteStagingLayoutV2 Layout;
            public string Name;
            public List<ApartmentComplexDensityTrialV2> Trials =
                new List<ApartmentComplexDensityTrialV2>();
            public int ContinuousSafeMax = -1;
        }

        public static void Run()
        {
            PhysicalTimeProfileV2 profile = PublishedParkingRobotTimingV2.Create(1.0);
            var options = new EmergencyAccessRouteGenerationOptionsV2
            {
                MaxRoutes = 4,
                MaxCenterlineAttempts = 16,
                MaxSearchExpansions = 100000,
            };
            // floor·고정차량은 배치안과 무관 — 후보 카탈로그를 전 조건에서 공유
            ApartmentComplexScenarioV2 geometry =
                SiteABlockScenarioFactoryV2.BuildDensity(
                    0, profile.CreateOperationTiming());
            var routeCatalog = new ApartmentComplexRouteCatalogV2(geometry, options);

            var layouts = new[]
            {
                new LayoutResult
                {
                    Layout = SiteStagingLayoutV2.SouthWestOnly,
                    Name = "baseline-southwest12",
                },
                new LayoutResult
                {
                    Layout = SiteStagingLayoutV2.Redistributed,
                    Name = "redistributed-6+6",
                },
                new LayoutResult
                {
                    Layout = SiteStagingLayoutV2.Extended,
                    Name = "extended-12+6",
                },
            };

            Console.WriteLine(
                "=== 단지 A 적치 반사실: 3안 × 이중주차 N=0~16 × 4동, 서문+동문 ===");
            foreach (LayoutResult layout in layouts)
            {
                Console.WriteLine($"\n[{layout.Name}] N | 최악동 | 최악시간 | 이동 | 판정");
                bool streak = true;
                for (int count = 0;
                     count <= SiteABlockScenarioFactoryV2.MaximumBlockingVehicles;
                     count++)
                {
                    ApartmentComplexScenarioV2 scenario =
                        SiteABlockScenarioFactoryV2.BuildDensity(
                            count,
                            profile.CreateOperationTiming(),
                            layout.Layout);
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
                        layout.Trials.Add(trial);
                    }
                    bool allPlan = rows.All(row => row.PlanSuccess);
                    bool allWithin = allPlan && rows.All(row => row.WithinBudget);
                    if (streak && allWithin) layout.ContinuousSafeMax = count;
                    else streak = false;
                    ApartmentComplexDensityTrialV2 worst = rows
                        .Where(row => row.PlanSuccess)
                        .OrderByDescending(row => row.Seconds)
                        .ThenBy(row => row.BuildingId)
                        .FirstOrDefault();
                    Console.WriteLine(
                        worst == null
                            ? $"{count,2} |    - | 계획 실패 |  - | 실패"
                            : $"{count,2} | {worst.BuildingId,4}동 | " +
                              $"{worst.Seconds,7:0.0}초 | {worst.MovedVehicleCount,2}대 | " +
                              (allWithin ? "안전" : allPlan ? "초과" : "불능"));
                }
            }

            WriteRows(layouts);
            WriteSummary(layouts);
            PrintHeadline(layouts);
        }

        private static void WriteRows(LayoutResult[] layouts)
        {
            var csv = new List<string>
            {
                "staging_layout,blocking_vehicles,building,plan_success,within_7min," +
                "seconds,ticks,moved_vehicles,entrance,route,outcome",
            };
            foreach (LayoutResult layout in layouts)
                foreach (ApartmentComplexDensityTrialV2 trial in layout.Trials)
                    csv.Add(string.Join(",",
                        layout.Name,
                        trial.BlockingVehicleCount,
                        trial.BuildingId,
                        trial.PlanSuccess ? 1 : 0,
                        trial.WithinBudget ? 1 : 0,
                        trial.Seconds.ToString("F1", CultureInfo.InvariantCulture),
                        trial.Ticks,
                        trial.MovedVehicleCount,
                        trial.SelectedEntrance ?? "",
                        trial.SelectedRoute ?? "",
                        trial.Outcome));
            string path = OutputDir.Resolve("v2_site_staging.csv");
            File.WriteAllLines(path, csv);
            Console.WriteLine("\nCSV: " + path);
        }

        private static void WriteSummary(LayoutResult[] layouts)
        {
            var csv = new List<string>
            {
                "staging_layout,continuous_safe_max_n,n0_worst_building," +
                "n0_worst_seconds,n0_building2_seconds,nmax_worst_seconds",
            };
            foreach (LayoutResult layout in layouts)
            {
                List<ApartmentComplexDensityTrialV2> n0 = layout.Trials
                    .Where(trial => trial.BlockingVehicleCount == 0).ToList();
                ApartmentComplexDensityTrialV2 n0Worst = n0
                    .Where(trial => trial.PlanSuccess)
                    .OrderByDescending(trial => trial.Seconds).First();
                double nMaxWorst = layout.Trials
                    .Where(trial => trial.BlockingVehicleCount ==
                        SiteABlockScenarioFactoryV2.MaximumBlockingVehicles &&
                        trial.PlanSuccess)
                    .Max(trial => trial.Seconds);
                csv.Add(string.Join(",",
                    layout.Name,
                    layout.ContinuousSafeMax < 0
                        ? "none"
                        : layout.ContinuousSafeMax.ToString(),
                    n0Worst.BuildingId,
                    n0Worst.Seconds.ToString("F1", CultureInfo.InvariantCulture),
                    n0.First(trial => trial.BuildingId == 2).Seconds
                        .ToString("F1", CultureInfo.InvariantCulture),
                    nMaxWorst.ToString("F1", CultureInfo.InvariantCulture)));
            }
            string path = OutputDir.Resolve("v2_site_staging_summary.csv");
            File.WriteAllLines(path, csv);
            Console.WriteLine("CSV: " + path);
        }

        private static void PrintHeadline(LayoutResult[] layouts)
        {
            double Building2AtN0(LayoutResult layout)
            {
                return layout.Trials.First(trial =>
                    trial.BlockingVehicleCount == 0 && trial.BuildingId == 2).Seconds;
            }
            double baseline = Building2AtN0(layouts[0]);
            double redistributed = Building2AtN0(layouts[1]);
            Console.WriteLine(
                $"\n원단 2동(N=0): 기준선 {baseline:0.0}초 → " +
                $"재배치안(면수 동일) {redistributed:0.0}초 " +
                $"({(1.0 - redistributed / baseline) * 100.0:0.0}% 단축) → " +
                $"확장안 {Building2AtN0(layouts[2]):0.0}초");
            foreach (LayoutResult layout in layouts)
                Console.WriteLine(
                    $"{layout.Name}: 연속 4동 안전 N = " +
                    (layout.ContinuousSafeMax < 0
                        ? "없음"
                        : layout.ContinuousSafeMax +
                          "/" + SiteABlockScenarioFactoryV2.MaximumBlockingVehicles));
        }
    }
}
