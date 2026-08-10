using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using ParkingSim.Core.V2;

namespace ParkingSim.Scenarios
{
    public static class V2SurfaceDensitySweepDemo
    {
        private sealed class MeasuredTrial
        {
            public string CapacityProfile;
            public SurfaceDensityTrialV2 Trial;
        }

        public static void Run()
        {
            PhysicalTimeProfileV2 timeProfile =
                PublishedParkingRobotTimingV2.Create(1.0);
            var capacities = new[]
            {
                (Name: "fixed-5", Slots: 5),
                (Name: "sufficient-14", Slots: 14),
            };
            SurfaceVehiclePlacementV2[] placements =
            {
                SurfaceVehiclePlacementV2.LowerFirst,
                SurfaceVehiclePlacementV2.UpperFirst,
                SurfaceVehiclePlacementV2.AlternatingEntranceFirst,
                SurfaceVehiclePlacementV2.AlternatingFireFirst,
            };
            (int X, int Y)[] fireCells =
            {
                (22, 5),
                (22, 7),
                (22, 9),
            };

            var rows = new List<MeasuredTrial>();
            var total = Stopwatch.StartNew();
            Console.WriteLine("=== 지상 아파트형 현실시간 차량 밀도 스윕 ===");
            Console.WriteLine(
                "접근축 가변차량 N=0~" +
                SurfaceApartmentScenarioFactoryV2.MaximumBlockingVehicles +
                ", 배치4, 화재3, 적치5/14, Stanley 1m/s");
            foreach (var capacity in capacities)
            {
                Console.WriteLine("\n[" + capacity.Name + "]");
                for (int vehicles = 0;
                     vehicles <= SurfaceApartmentScenarioFactoryV2.MaximumBlockingVehicles;
                     vehicles++)
                {
                    int safe = 0;
                    int timeFail = 0;
                    int capacityFail = 0;
                    int otherFail = 0;
                    foreach (SurfaceVehiclePlacementV2 placement in placements)
                    {
                        foreach ((int X, int Y) fire in fireCells)
                        {
                            SurfaceDensityTrialV2 trial = SurfaceDensitySweepV2.Evaluate(
                                vehicles,
                                placement,
                                capacity.Slots,
                                fire,
                                timeProfile,
                                activeRobotCount: 4,
                                budgetSeconds: Core.TimeBudget.BaselineSeconds,
                                maxHighLevelCandidates: 8,
                                maxTick: 5000);
                            rows.Add(new MeasuredTrial
                            {
                                CapacityProfile = capacity.Name,
                                Trial = trial,
                            });
                            if (trial.Outcome == SurfaceDensityOutcomeV2.WithinBudget)
                                safe++;
                            else if (trial.Outcome ==
                                     SurfaceDensityOutcomeV2.TimeBudgetExceeded)
                                timeFail++;
                            else if (trial.Outcome ==
                                     SurfaceDensityOutcomeV2.InsufficientStagingCapacity)
                                capacityFail++;
                            else
                                otherFail++;
                        }
                    }
                    Console.WriteLine(
                        "N=" + vehicles.ToString("D2") +
                        " safe=" + safe + "/12" +
                        " time=" + timeFail +
                        " capacity=" + capacityFail +
                        " other=" + otherFail);
                }
            }
            total.Stop();

            string detailPath = OutputDir.Resolve("v2_surface_density_sweep.csv");
            string summaryPath = OutputDir.Resolve("v2_surface_density_summary.csv");
            WriteDetails(detailPath, rows);
            WriteSummary(summaryPath, rows, capacities.Select(c => c.Name));

            Console.WriteLine("\n요약:");
            foreach (string profile in capacities.Select(c => c.Name))
                PrintProfileSummary(profile, rows);
            Console.WriteLine(
                "총 " + rows.Count + "점, " + total.ElapsedMilliseconds.ToString("N0") + "ms");
            Console.WriteLine("CSV: " + Path.GetFullPath(detailPath));
            Console.WriteLine("CSV: " + Path.GetFullPath(summaryPath));
        }

        private static void WriteDetails(
            string path,
            IEnumerable<MeasuredTrial> rows)
        {
            var csv = new List<string>
            {
                "capacity_profile,staging_capacity,blocking_vehicles,density_percent," +
                "placement,fire_x,fire_y,outcome,plan_success,candidates," +
                "min_required_vehicles,moved_vehicles," +
                "selected_route,ticks,seconds,safe_7min,expanded,reason",
            };
            foreach (MeasuredTrial measured in rows)
            {
                SurfaceDensityTrialV2 row = measured.Trial;
                csv.Add(string.Join(",",
                    measured.CapacityProfile,
                    row.StagingCapacity,
                    row.BlockingVehicleCount,
                    (100.0 * row.BlockingVehicleCount /
                     SurfaceApartmentScenarioFactoryV2.MaximumBlockingVehicles)
                        .ToString("F1", CultureInfo.InvariantCulture),
                    row.Placement,
                    row.FireCell.X,
                    row.FireCell.Y,
                    row.Outcome,
                    row.PlanSuccess ? 1 : 0,
                    row.CandidateCount,
                    row.MinimumRequiredVehicleCount,
                    row.MovedVehicleCount,
                    Escape(row.SelectedRoute),
                    row.Ticks,
                    row.Seconds.ToString("F1", CultureInfo.InvariantCulture),
                    row.WithinBudget ? 1 : 0,
                    row.ExpandedStates,
                    Escape(row.FailReason)));
            }
            File.WriteAllLines(path, csv);
        }

        private static void WriteSummary(
            string path,
            IReadOnlyCollection<MeasuredTrial> rows,
            IEnumerable<string> profiles)
        {
            var csv = new List<string>
            {
                "capacity_profile,blocking_vehicles,trials,all_safe_7min," +
                "safe_count,time_fail_count,capacity_fail_count,other_fail_count," +
                "min_moved,max_moved,worst_seconds",
            };
            foreach (string profile in profiles)
            {
                for (int vehicles = 0;
                     vehicles <= SurfaceApartmentScenarioFactoryV2.MaximumBlockingVehicles;
                     vehicles++)
                {
                    MeasuredTrial[] group = rows
                        .Where(row => row.CapacityProfile == profile &&
                                      row.Trial.BlockingVehicleCount == vehicles)
                        .ToArray();
                    SurfaceDensityTrialV2[] trials =
                        group.Select(row => row.Trial).ToArray();
                    int safe = trials.Count(row =>
                        row.Outcome == SurfaceDensityOutcomeV2.WithinBudget);
                    int time = trials.Count(row =>
                        row.Outcome == SurfaceDensityOutcomeV2.TimeBudgetExceeded);
                    int capacity = trials.Count(row =>
                        row.Outcome ==
                        SurfaceDensityOutcomeV2.InsufficientStagingCapacity);
                    int other = trials.Length - safe - time - capacity;
                    int[] moved = trials
                        .Where(row => row.PlanSuccess)
                        .Select(row => row.MovedVehicleCount)
                        .ToArray();
                    double worst = trials
                        .Where(row => row.PlanSuccess)
                        .Select(row => row.Seconds)
                        .DefaultIfEmpty(0.0)
                        .Max();
                    csv.Add(string.Join(",",
                        profile,
                        vehicles,
                        trials.Length,
                        safe == trials.Length ? 1 : 0,
                        safe,
                        time,
                        capacity,
                        other,
                        moved.Length == 0 ? 0 : moved.Min(),
                        moved.Length == 0 ? 0 : moved.Max(),
                        worst.ToString("F1", CultureInfo.InvariantCulture)));
                }
            }
            File.WriteAllLines(path, csv);
        }

        private static void PrintProfileSummary(
            string profile,
            IReadOnlyCollection<MeasuredTrial> rows)
        {
            var allSafeByCount = new Dictionary<int, bool>();
            for (int vehicles = 0;
                 vehicles <= SurfaceApartmentScenarioFactoryV2.MaximumBlockingVehicles;
                 vehicles++)
            {
                SurfaceDensityTrialV2[] group = rows
                    .Where(row => row.CapacityProfile == profile &&
                                  row.Trial.BlockingVehicleCount == vehicles)
                    .Select(row => row.Trial)
                    .ToArray();
                allSafeByCount[vehicles] =
                    group.Length > 0 &&
                    group.All(row => row.Outcome == SurfaceDensityOutcomeV2.WithinBudget);
            }

            int safePrefix = -1;
            for (int vehicles = 0;
                 vehicles <= SurfaceApartmentScenarioFactoryV2.MaximumBlockingVehicles;
                 vehicles++)
            {
                if (!allSafeByCount[vehicles]) break;
                safePrefix = vehicles;
            }
            int isolatedMaximum = allSafeByCount
                .Where(pair => pair.Value)
                .Select(pair => pair.Key)
                .DefaultIfEmpty(-1)
                .Max();
            int firstTimeFailure = rows
                .Where(row => row.CapacityProfile == profile &&
                              row.Trial.Outcome ==
                              SurfaceDensityOutcomeV2.TimeBudgetExceeded)
                .Select(row => row.Trial.BlockingVehicleCount)
                .DefaultIfEmpty(-1)
                .Min();
            int firstCapacityFailure = rows
                .Where(row => row.CapacityProfile == profile &&
                              row.Trial.Outcome ==
                              SurfaceDensityOutcomeV2.InsufficientStagingCapacity)
                .Select(row => row.Trial.BlockingVehicleCount)
                .DefaultIfEmpty(-1)
                .Min();
            Console.WriteLine(
                profile +
                ": 연속 전조건 안전 N≤" + safePrefix +
                ", 고립 최대 안전 N=" + isolatedMaximum +
                ", 첫 시간실패 N=" + firstTimeFailure +
                ", 첫 적치실패 N=" + firstCapacityFailure);
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
