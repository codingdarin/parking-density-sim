using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ParkingSim.Core.V2;

namespace ParkingSim.Scenarios
{
    public static class V2SeedSweepDemo
    {
        public static void Run(int seedCount = 20)
        {
            var ticks = new List<int>();
            var expansions = new List<int>();
            var elapsedMs = new List<long>();
            int failures = 0;
            Console.WriteLine("=== 강화 아파트형 배치 시드 스윕 ===");
            Console.WriteLine("seed | success | ticks | expanded | elapsed_ms | reason");
            Console.WriteLine(new string('-', 72));
            for (int seed = 0; seed < seedCount; seed++)
            {
                EmergencyProblemV2 map = V2MapCatalog.ConstrainedApartmentVariant(seed).Build();
                var scenario = new EmergencyScenarioV2(
                    "seed-" + seed, (19, 5), map.CopyClearanceCells());
                EmergencyScenarioBuildResultV2 built = scenario.Build(map);
                var stopwatch = Stopwatch.StartNew();
                PipelinedPlanResultV2 plan = built.Success
                    ? PipelinedPrioritizedPlannerV2.Solve(built.Problem)
                    : new PipelinedPlanResultV2 { FailReason = built.FailReason };
                stopwatch.Stop();
                bool success = built.Success && plan.Success && plan.PhysicallyValid;
                if (success)
                {
                    ticks.Add(plan.Ticks);
                    expansions.Add(plan.ExpandedStates);
                    elapsedMs.Add(stopwatch.ElapsedMilliseconds);
                }
                else failures++;
                Console.WriteLine(
                    $"{seed,4} | {success,7} | {(success ? plan.Ticks.ToString() : "-"),5} | " +
                    $"{plan.ExpandedStates,8:N0} | {stopwatch.ElapsedMilliseconds,10:N0} | " +
                    (plan.FailReason ?? "valid"));
            }

            if (ticks.Count == 0)
            {
                Console.WriteLine($"요약: 성공0/{seedCount}, 실패={failures}, 시드 축 승격");
                return;
            }
            ticks.Sort();
            expansions.Sort();
            elapsedMs.Sort();
            double medianTicks = Median(ticks.Select(v => (double)v).ToList());
            double tickRatio = (double)ticks[ticks.Count - 1] / ticks[0];
            double expansionRatio = (double)expansions[expansions.Count - 1] / expansions[0];
            double elapsedRatio = elapsedMs[0] == 0
                ? double.PositiveInfinity
                : (double)elapsedMs[elapsedMs.Count - 1] / elapsedMs[0];
            bool promoteSeed = failures > 0 || tickRatio >= 1.25 || expansionRatio >= 3.0;
            Console.WriteLine(
                $"요약: 성공={ticks.Count}/{seedCount}, 실패={failures}, ticks={ticks[0]}/{medianTicks:F1}/{ticks[ticks.Count - 1]}" +
                $"(min/median/max), tick최악÷최선={tickRatio:F3}, " +
                $"확장비={expansionRatio:F2}, 시간비={elapsedRatio:F2}, " +
                $"판정={(promoteSeed ? "시드 축 승격" : "대표 시드5 가능")}");
        }

        private static double Median(IReadOnlyList<double> sorted)
        {
            int middle = sorted.Count / 2;
            return sorted.Count % 2 == 0
                ? (sorted[middle - 1] + sorted[middle]) / 2.0
                : sorted[middle];
        }
    }
}
