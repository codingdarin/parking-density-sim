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
    /// 8동 단지 배치 강건성 — 헤드라인 임계(N=15) 주변 N=13~17을 시드 배치
    /// 12종 + 누적 기준선으로 전수 평가해 통과율·강건 임계를 분포로 낸다.
    /// 후보 카탈로그는 배치 무관(중심선·폭3)이라 전 실행 공유. Stanley 1m/s.
    /// </summary>
    public static class V2ComplexRobustnessDemo
    {
        private const int MinN = 13;
        private const int MaxN = 17;
        private const int SeedCount = 12;

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
                ApartmentComplexScenarioFactoryV2.BuildDensity(
                    0, profile.CreateOperationTiming());
            var catalog = new ApartmentComplexRouteCatalogV2(geometry, options);

            var placements = new List<int> { -1 };
            placements.AddRange(Enumerable.Range(1, SeedCount));

            var rows = new List<string>
            {
                "placement,blocking_vehicles,building,plan_success,within_7min," +
                "seconds,ticks,moved_vehicles,entrance,outcome",
            };
            var summary = new List<string>
            {
                "placement,blocking_vehicles,all_within_7min,worst_building," +
                "worst_seconds",
            };
            Console.WriteLine(
                $"=== 8동 단지 배치 강건성: N={MinN}~{MaxN}, " +
                $"시드 {SeedCount}종 + 누적 기준선 ===");

            var passBySeedAndN = new Dictionary<(int Seed, int N), bool>();
            var worstBySeedAndN = new Dictionary<(int Seed, int N), double>();
            foreach (int seed in placements)
            {
                string label = seed < 0 ? "cumulative" : "seed" + seed;
                for (int count = MinN; count <= MaxN; count++)
                {
                    ApartmentComplexScenarioV2 scenario =
                        ApartmentComplexScenarioFactoryV2.BuildDensity(
                            count, profile.CreateOperationTiming(), seed);
                    var session = new ApartmentComplexPlanningSessionV2(
                        scenario,
                        activeRobotCount: 4,
                        generationOptions: options,
                        maxTick: 5000,
                        routeCatalog: catalog,
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
                            label,
                            count,
                            trial.BuildingId,
                            trial.PlanSuccess ? 1 : 0,
                            trial.WithinBudget ? 1 : 0,
                            trial.Seconds.ToString("F1", CultureInfo.InvariantCulture),
                            trial.Ticks,
                            trial.MovedVehicleCount,
                            trial.SelectedEntrance ?? "",
                            trial.Outcome));
                    }
                    bool allWithin = trials.All(trial =>
                        trial.PlanSuccess && trial.WithinBudget);
                    double worstSeconds = trials
                        .Where(trial => trial.PlanSuccess)
                        .Select(trial => trial.Seconds)
                        .DefaultIfEmpty(double.NaN)
                        .Max();
                    ApartmentComplexDensityTrialV2 worst = trials
                        .Where(trial => trial.PlanSuccess)
                        .OrderByDescending(trial => trial.Seconds)
                        .ThenBy(trial => trial.BuildingId)
                        .FirstOrDefault();
                    passBySeedAndN[(seed, count)] = allWithin;
                    worstBySeedAndN[(seed, count)] = worstSeconds;
                    summary.Add(string.Join(",",
                        label,
                        count,
                        allWithin ? 1 : 0,
                        worst == null ? "" : worst.BuildingId.ToString(),
                        double.IsNaN(worstSeconds)
                            ? ""
                            : worstSeconds.ToString(
                                "F1", CultureInfo.InvariantCulture)));
                }
                string verdicts = string.Join(" ", Enumerable
                    .Range(MinN, MaxN - MinN + 1)
                    .Select(count => count +
                        (passBySeedAndN[(seed, count)] ? "○" : "×")));
                Console.WriteLine($"{label,-10} | {verdicts}");
            }

            Console.WriteLine("\nN | 시드 통과율 | 최악초 min/중앙/max | 누적 기준");
            int robustThreshold = MinN - 1;
            bool streak = true;
            for (int count = MinN; count <= MaxN; count++)
            {
                var seedRuns = Enumerable.Range(1, SeedCount)
                    .Select(seed => (Pass: passBySeedAndN[(seed, count)],
                        Worst: worstBySeedAndN[(seed, count)]))
                    .ToList();
                int passCount = seedRuns.Count(run => run.Pass);
                var worsts = seedRuns
                    .Where(run => !double.IsNaN(run.Worst))
                    .Select(run => run.Worst)
                    .OrderBy(value => value)
                    .ToList();
                double median = worsts.Count == 0
                    ? double.NaN
                    : worsts[worsts.Count / 2];
                if (streak && passCount == SeedCount) robustThreshold = count;
                else streak = false;
                Console.WriteLine(
                    $"{count,2} | {passCount,2}/{SeedCount} | " +
                    (worsts.Count == 0
                        ? "-"
                        : $"{worsts.First():0.0}/{median:0.0}/{worsts.Last():0.0}초") +
                    $" | {(passBySeedAndN[(-1, count)] ? "통과" : "실패")}");
            }
            Console.WriteLine(
                "\n강건 임계(창 내 전 시드 통과 최대 N) = " +
                (robustThreshold < MinN
                    ? MinN + " 미만(창 밖)"
                    : robustThreshold.ToString()) +
                " · 누적 기준선 임계 15와 비교");

            string path = OutputDir.Resolve("v2_complex_robustness.csv");
            File.WriteAllLines(path, rows);
            string summaryPath =
                OutputDir.Resolve("v2_complex_robustness_summary.csv");
            File.WriteAllLines(summaryPath, summary);
            Console.WriteLine("CSV: " + path);
            Console.WriteLine("CSV: " + summaryPath);
        }
    }
}
