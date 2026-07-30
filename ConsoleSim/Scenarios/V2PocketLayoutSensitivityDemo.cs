using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using ParkingSim.Core;
using ParkingSim.Core.V2;

namespace ParkingSim.Scenarios
{
    public static class V2PocketLayoutSensitivityDemo
    {
        public static void Run()
        {
            var csv = new List<string>
            {
                "pockets,offset,success,ticks,seconds,safe_7min,expanded",
            };
            Console.WriteLine("=== V2 포켓 위치 민감도 ===");
            Console.WriteLine("1레인·d100·활성4조·포켓10~16·순환오프셋20개");
            Console.WriteLine("pockets | success | safe | ticks_min/median/max | elapsed_ms");
            Console.WriteLine(new string('-', 78));
            foreach (int pockets in new[] { 10, 11, 12, 13, 14, 15, 16 })
            {
                var ticks = new List<int>();
                int successes = 0;
                int safeCount = 0;
                var groupWatch = Stopwatch.StartNew();
                for (int offset = 0; offset < 20; offset++)
                {
                    EmergencyScenarioBuildResultV2 built =
                        CorridorScenarioFactoryV2.BuildEmergencyWithPockets(
                            100, pockets, pocketOffset: offset);
                    var stopwatch = Stopwatch.StartNew();
                    PipelinedPlanResultV2 result = PipelinedPrioritizedPlannerV2.Solve(
                        built.Problem,
                        activeRobotCount: 4,
                        maxHighLevelCandidates: 8);
                    stopwatch.Stop();
                    bool success = built.Success && result.Success && result.PhysicallyValid;
                    bool safe = success && result.Ticks <= TimeBudget.BaselineTicks;
                    if (success)
                    {
                        successes++;
                        ticks.Add(result.Ticks);
                    }
                    if (safe) safeCount++;
                    csv.Add(string.Join(",",
                        pockets,
                        offset,
                        success ? 1 : 0,
                        result.Ticks,
                        (result.Ticks * 2.5).ToString("F1", CultureInfo.InvariantCulture),
                        safe ? 1 : 0,
                        result.ExpandedStates));
                }
                groupWatch.Stop();
                ticks.Sort();
                double median = ticks.Count % 2 == 0
                    ? (ticks[ticks.Count / 2 - 1] + ticks[ticks.Count / 2]) / 2.0
                    : ticks[ticks.Count / 2];
                Console.WriteLine(
                    $"{pockets,7} | {successes,7}/20 | {safeCount,4}/20 | " +
                    $"{ticks.First(),4}/{median,5:F1}/{ticks.Last(),4} | {groupWatch.ElapsedMilliseconds,10:N0}");
            }
            string path = OutputDir.Resolve("v2_pocket_layout_sensitivity.csv");
            File.WriteAllLines(path, csv);
            Console.WriteLine("CSV: " + Path.GetFullPath(path));
        }
    }
}
