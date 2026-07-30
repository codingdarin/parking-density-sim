using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using ParkingSim.Core;
using ParkingSim.Core.V2;

namespace ParkingSim.Scenarios
{
    public static class V2PocketSweepDemo
    {
        public static void Run()
        {
            var csv = new List<string>
            {
                "lanes,fire_m,vehicles,robots,pockets,net_alpha,success,ticks,seconds,safe_7min,expanded,reason",
            };
            Console.WriteLine("=== V2 분산 포켓 수 스윕 ===");
            Console.WriteLine("1레인·d100·차량20·활성4조·총적치20면·후보8");
            Console.WriteLine("pockets | net_alpha | success | ticks | seconds | safe | expanded | elapsed_ms");
            Console.WriteLine(new string('-', 88));
            foreach (int pockets in new[] { 0, 1, 2, 4, 8, 9, 10, 11, 12, 16, 20 })
            {
                EmergencyScenarioBuildResultV2 built =
                    CorridorScenarioFactoryV2.BuildEmergencyWithPockets(100, pockets);
                var stopwatch = Stopwatch.StartNew();
                PipelinedPlanResultV2 result = PipelinedPrioritizedPlannerV2.Solve(
                    built.Problem,
                    activeRobotCount: 4,
                    maxHighLevelCandidates: 8);
                stopwatch.Stop();
                bool success = built.Success && result.Success && result.PhysicallyValid;
                bool safe = success && result.Ticks <= TimeBudget.BaselineTicks;
                double seconds = result.Ticks * 2.5;
                int netAlpha = 20 - pockets;
                Console.WriteLine(
                    $"{pockets,7} | {netAlpha,9} | {success,7} | {result.Ticks,5} | " +
                    $"{seconds,7:F1} | {safe,4} | {result.ExpandedStates,8:N0} | " +
                    $"{stopwatch.ElapsedMilliseconds,10:N0}");
                csv.Add(string.Join(",",
                    1,
                    100,
                    built.SelectedVehicleCount,
                    4,
                    pockets,
                    netAlpha,
                    success ? 1 : 0,
                    result.Ticks,
                    seconds.ToString("F1", CultureInfo.InvariantCulture),
                    safe ? 1 : 0,
                    result.ExpandedStates,
                    (result.FailReason ?? "valid").Replace(',', ';')));
            }
            string path = OutputDir.Resolve("v2_pocket_sweep.csv");
            File.WriteAllLines(path, csv);
            Console.WriteLine("CSV: " + Path.GetFullPath(path));
        }
    }
}
