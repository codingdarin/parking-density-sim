using System;
using System.Diagnostics;
using ParkingSim.Core.V2;

namespace ParkingSim.Scenarios
{
    public static class V2CandidateSensitivityDemo
    {
        public static void Run()
        {
            EmergencyScenarioBuildResultV2 built =
                CorridorScenarioFactoryV2.BuildEmergency(occupiedLanes: 2, fireMeters: 40);
            Console.WriteLine("=== 운영 규모 고수준 후보 수 민감도 ===");
            Console.WriteLine("lane2·d40·차량20·활성4조");
            Console.WriteLine("candidates | success | ticks | expanded | elapsed_ms | reason");
            Console.WriteLine(new string('-', 76));
            foreach (int candidates in new[] { 1, 8, 32 })
            {
                var stopwatch = Stopwatch.StartNew();
                PipelinedPlanResultV2 result = PipelinedPrioritizedPlannerV2.Solve(
                    built.Problem,
                    activeRobotCount: 4,
                    maxHighLevelCandidates: candidates);
                stopwatch.Stop();
                Console.WriteLine(
                    $"{candidates,10} | {result.Success && result.PhysicallyValid,7} | " +
                    $"{result.Ticks,5} | {result.ExpandedStates,8:N0} | " +
                    $"{stopwatch.ElapsedMilliseconds,10:N0} | " +
                    (result.Success ? "valid" : result.FailReason));
            }
        }
    }
}
