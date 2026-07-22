using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using ParkingSim.Core.V2;

namespace ParkingSim.Scenarios
{
    public static class V2FineGridDemo
    {
        private sealed class Row
        {
            public int Lanes;
            public int FireMeters;
            public int Vehicles;
            public int Ticks;
            public int Expanded;
            public long ElapsedMs;
            public bool Success;
        }

        public static void Run()
        {
            var rows = new List<Row>();
            var totalWatch = Stopwatch.StartNew();
            Console.WriteLine("=== V2 5m 전 거리 곡선 ===");
            for (int lanes = 1; lanes <= 3; lanes++)
            {
                Console.Write("lane" + lanes + ": ");
                for (int fireMeters = 5; fireMeters <= 100; fireMeters += 5)
                {
                    EmergencyScenarioBuildResultV2 built =
                        CorridorScenarioFactoryV2.BuildEmergency(lanes, fireMeters);
                    var stopwatch = Stopwatch.StartNew();
                    PipelinedPlanResultV2 result = PipelinedPrioritizedPlannerV2.Solve(
                        built.Problem,
                        activeRobotCount: 4,
                        maxHighLevelCandidates: 8);
                    stopwatch.Stop();
                    bool success = built.Success && result.Success && result.PhysicallyValid;
                    rows.Add(new Row
                    {
                        Lanes = lanes,
                        FireMeters = fireMeters,
                        Vehicles = built.SelectedVehicleCount,
                        Ticks = result.Ticks,
                        Expanded = result.ExpandedStates,
                        ElapsedMs = stopwatch.ElapsedMilliseconds,
                        Success = success,
                    });
                    Console.Write(success ? "." : "X");
                }
                Console.WriteLine();
            }
            totalWatch.Stop();

            Directory.CreateDirectory("output");
            string curvePath = Path.Combine("output", "v2_fine_distance_curve.csv");
            var csv = new List<string>
            {
                "lanes,fire_m,vehicles,success,ticks,seconds_at_2_5,expanded,elapsed_ms",
            };
            foreach (Row row in rows)
            {
                csv.Add(string.Join(",",
                    row.Lanes,
                    row.FireMeters,
                    row.Vehicles,
                    row.Success ? 1 : 0,
                    row.Ticks,
                    (row.Ticks * 2.5).ToString("F1", CultureInfo.InvariantCulture),
                    row.Expanded,
                    row.ElapsedMs));
            }
            File.WriteAllLines(curvePath, csv);

            Console.WriteLine("\n안전 도달거리(m):");
            Console.WriteLine("sec/tick | budget | lane1 | lane2 | lane3");
            Console.WriteLine(new string('-', 48));
            var sensitivityCsv = new List<string>
            {
                "seconds_per_tick,budget_minutes,lane1_m,lane2_m,lane3_m",
            };
            foreach (double secondsPerTick in new[] { 2.0, 2.5, 3.0 })
            {
                foreach (int budgetMinutes in new[] { 5, 7, 9 })
                {
                    double maxTicks = budgetMinutes * 60.0 / secondsPerTick;
                    int[] reaches = Enumerable.Range(1, 3)
                        .Select(lane => rows
                            .Where(row => row.Lanes == lane && row.Success && row.Ticks <= maxTicks)
                            .Select(row => row.FireMeters)
                            .DefaultIfEmpty(0)
                            .Max())
                        .ToArray();
                    Console.WriteLine(
                        $"{secondsPerTick,8:F1} | {budgetMinutes,6} | " +
                        $"{reaches[0],5} | {reaches[1],5} | {reaches[2],5}");
                    sensitivityCsv.Add(string.Join(",",
                        secondsPerTick.ToString("F1", CultureInfo.InvariantCulture),
                        budgetMinutes,
                        reaches[0], reaches[1], reaches[2]));
                }
            }
            string sensitivityPath = Path.Combine("output", "v2_time_sensitivity.csv");
            File.WriteAllLines(sensitivityPath, sensitivityCsv);
            Console.WriteLine($"총 {rows.Count}점, {totalWatch.ElapsedMilliseconds:N0}ms");
            Console.WriteLine("CSV: " + Path.GetFullPath(curvePath));
            Console.WriteLine("CSV: " + Path.GetFullPath(sensitivityPath));
        }
    }
}
