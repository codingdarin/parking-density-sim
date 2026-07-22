using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using ParkingSim.Core.V2;

namespace ParkingSim.Scenarios
{
    public static class V2CorridorGridDemo
    {
        public static void Run()
        {
            var csv = new List<string>
            {
                "lanes,fire_m,vehicles,robots,candidates,success,ticks,seconds,expanded,elapsed_ms,reason",
            };
            Console.WriteLine("=== V2 운영 규모 거리 격자 ===");
            Console.WriteLine("lanes | fire_m | vehicles | success | ticks | seconds | expanded | elapsed_ms");
            Console.WriteLine(new string('-', 88));
            foreach (int lanes in new[] { 1, 2, 3 })
            {
                foreach (int fireMeters in new[] { 20, 40, 60, 80, 100 })
                {
                    EmergencyScenarioBuildResultV2 built =
                        CorridorScenarioFactoryV2.BuildEmergency(lanes, fireMeters);
                    var stopwatch = Stopwatch.StartNew();
                    PipelinedPlanResultV2 result = built.Success
                        ? PipelinedPrioritizedPlannerV2.Solve(
                            built.Problem,
                            activeRobotCount: 4,
                            maxHighLevelCandidates: 8)
                        : new PipelinedPlanResultV2();
                    stopwatch.Stop();
                    bool success = built.Success && result.Success && result.PhysicallyValid;
                    string reason = success ? "valid" : built.FailReason ?? result.FailReason ?? "unknown";
                    double seconds = result.Ticks * 2.5;
                    Console.WriteLine(
                        $"{lanes,5} | {fireMeters,6} | {built.SelectedVehicleCount,8} | " +
                        $"{success,7} | {result.Ticks,5} | {seconds,7:F1} | " +
                        $"{result.ExpandedStates,8:N0} | {stopwatch.ElapsedMilliseconds,10:N0}");
                    csv.Add(string.Join(",",
                        lanes,
                        fireMeters,
                        built.SelectedVehicleCount,
                        4,
                        8,
                        success ? 1 : 0,
                        result.Ticks,
                        seconds.ToString("F1", CultureInfo.InvariantCulture),
                        result.ExpandedStates,
                        stopwatch.ElapsedMilliseconds,
                        reason.Replace(',', ';')));
                }
            }
            Directory.CreateDirectory("output");
            string path = Path.Combine("output", "v2_corridor_grid.csv");
            File.WriteAllLines(path, csv);
            Console.WriteLine("CSV: " + Path.GetFullPath(path));
        }
    }
}
