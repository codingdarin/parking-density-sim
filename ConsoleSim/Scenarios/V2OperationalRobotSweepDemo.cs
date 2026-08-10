using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using ParkingSim.Core;
using ParkingSim.Core.V2;

namespace ParkingSim.Scenarios
{
    public static class V2OperationalRobotSweepDemo
    {
        public static void Run()
        {
            var csv = new List<string>
            {
                "lanes,fire_m,vehicles,robots,success,ticks,seconds,safe_7min,expanded,reason",
            };
            Console.WriteLine("=== V2 운영 규모 로봇 대수 스윕 ===");
            Console.WriteLine("3레인·d20/d100·대기소8칸·후보8");
            Console.WriteLine("fire_m | robots | vehicles | success | ticks | seconds | safe | expanded | elapsed_ms");
            Console.WriteLine(new string('-', 96));
            foreach (int fireMeters in new[] { 20, 100 })
            {
                EmergencyScenarioBuildResultV2 built =
                    CorridorScenarioFactoryV2.BuildEmergency(3, fireMeters);
                foreach (int robots in new[] { 1, 2, 4, 8 })
                {
                    var stopwatch = Stopwatch.StartNew();
                    PipelinedPlanResultV2 result = PipelinedPrioritizedPlannerV2.Solve(
                        built.Problem,
                        maxTick: 6000,
                        activeRobotCount: robots,
                        maxHighLevelCandidates: 8);
                    stopwatch.Stop();
                    bool success = result.Success && result.PhysicallyValid;
                    bool safe = success && result.Ticks <= TimeBudget.BaselineTicks;
                    double seconds = result.Ticks * 2.5;
                    Console.WriteLine(
                        $"{fireMeters,6} | {robots,6} | {built.SelectedVehicleCount,8} | " +
                        $"{success,7} | {result.Ticks,5} | {seconds,7:F1} | {safe,4} | " +
                        $"{result.ExpandedStates,8:N0} | {stopwatch.ElapsedMilliseconds,10:N0}");
                    csv.Add(string.Join(",",
                        3,
                        fireMeters,
                        built.SelectedVehicleCount,
                        robots,
                        success ? 1 : 0,
                        result.Ticks,
                        seconds.ToString("F1", CultureInfo.InvariantCulture),
                        safe ? 1 : 0,
                        result.ExpandedStates,
                        (result.FailReason ?? "valid").Replace(',', ';')));
                }
            }
            string path = OutputDir.Resolve("v2_operational_robot_sweep.csv");
            File.WriteAllLines(path, csv);
            Console.WriteLine("CSV: " + Path.GetFullPath(path));
        }
    }
}
