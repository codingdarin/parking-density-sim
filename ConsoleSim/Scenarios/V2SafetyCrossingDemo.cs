using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using ParkingSim.Core.V2;

namespace ParkingSim.Scenarios
{
    public static class V2SafetyCrossingDemo
    {
        public static void Run()
        {
            var conditions = new[]
            {
                (Lanes: 1, Fire: 45), (Lanes: 1, Fire: 50), (Lanes: 1, Fire: 55),
                (Lanes: 2, Fire: 25), (Lanes: 2, Fire: 30), (Lanes: 2, Fire: 35),
                (Lanes: 3, Fire: 25),
            };
            var csv = new List<string>
            {
                "lanes,fire_m,vehicles,success,ticks,seconds,safe_7min,expanded,elapsed_ms",
            };
            Console.WriteLine("=== V2 7분 교차점 5m 보강 ===");
            Console.WriteLine("lanes | fire_m | vehicles | ticks | seconds | safe | expanded | elapsed_ms");
            Console.WriteLine(new string('-', 84));
            foreach (var condition in conditions)
            {
                EmergencyScenarioBuildResultV2 built = CorridorScenarioFactoryV2.BuildEmergency(
                    condition.Lanes, condition.Fire);
                var stopwatch = Stopwatch.StartNew();
                PipelinedPlanResultV2 result = PipelinedPrioritizedPlannerV2.Solve(
                    built.Problem,
                    activeRobotCount: 4,
                    maxHighLevelCandidates: 8);
                stopwatch.Stop();
                bool success = built.Success && result.Success && result.PhysicallyValid;
                bool safe = success && result.Ticks <= 168;
                double seconds = result.Ticks * 2.5;
                Console.WriteLine(
                    $"{condition.Lanes,5} | {condition.Fire,6} | {built.SelectedVehicleCount,8} | " +
                    $"{result.Ticks,5} | {seconds,7:F1} | {safe,4} | " +
                    $"{result.ExpandedStates,8:N0} | {stopwatch.ElapsedMilliseconds,10:N0}");
                csv.Add(string.Join(",",
                    condition.Lanes,
                    condition.Fire,
                    built.SelectedVehicleCount,
                    success ? 1 : 0,
                    result.Ticks,
                    seconds.ToString("F1", CultureInfo.InvariantCulture),
                    safe ? 1 : 0,
                    result.ExpandedStates,
                    stopwatch.ElapsedMilliseconds));
            }
            Directory.CreateDirectory("output");
            string path = Path.Combine("output", "v2_safety_crossing.csv");
            File.WriteAllLines(path, csv);
            Console.WriteLine("CSV: " + Path.GetFullPath(path));
        }
    }
}
