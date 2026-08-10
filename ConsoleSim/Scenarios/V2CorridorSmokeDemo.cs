using System;
using System.Diagnostics;
using ParkingSim.Core.V2;

namespace ParkingSim.Scenarios
{
    public static class V2CorridorSmokeDemo
    {
        public static void Run()
        {
            Console.WriteLine("=== V2 운영 규모 6점 스모크 ===");
            Console.WriteLine("폭3×100m·beta10m·적치60면·대기소8칸·활성4조·후보8");
            Console.WriteLine("lanes | fire_m | vehicles | success | ticks | seconds | expanded | elapsed_ms | reason");
            Console.WriteLine(new string('-', 104));
            foreach (int fireMeters in new[] { 20, 40 })
            {
                foreach (int lanes in new[] { 1, 2, 3 })
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
                    Console.WriteLine(
                        $"{lanes,5} | {fireMeters,6} | {built.SelectedVehicleCount,8} | " +
                        $"{success,7} | {result.Ticks,5} | {result.Ticks * 2.5,7:F1} | " +
                        $"{result.ExpandedStates,8:N0} | {stopwatch.ElapsedMilliseconds,10:N0} | " +
                        (success ? "valid" : built.FailReason ?? result.FailReason));
                }
            }
        }
    }
}
