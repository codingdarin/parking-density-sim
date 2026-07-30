using System;
using System.Diagnostics;
using System.Linq;
using ParkingSim.Core.V2;

namespace ParkingSim.Scenarios
{
    public static class V2ApartmentRobotSweepDemo
    {
        public static void Run()
        {
            EmergencyProblemV2 map = V2MapCatalog.ApartmentSerialAisle.Build();
            var scenario = new EmergencyScenarioV2(
                "apartment-serial-full-clearance", (27, 7), map.CopyClearanceCells());
            EmergencyScenarioBuildResultV2 built = scenario.Build(map);
            Console.WriteLine("=== 다차량 강화 아파트형 운송 유닛 스윕 ===");
            Console.WriteLine(
                $"map={map.Width}x{map.Height}, selected={built.SelectedVehicleCount}, " +
                $"fixed={map.FixedVehiclePoses.Count}, staging={map.StagingCapacity}, stations={map.RobotStarts.Count}");
            if (!built.Success)
            {
                Console.WriteLine("시나리오 실패: " + built.FailReason);
                return;
            }
            Console.WriteLine("robots | success | ticks | expanded | elapsed_ms | missions/robot | reason");
            Console.WriteLine(new string('-', 92));
            foreach (int robots in new[] { 1, 2, 4, 8 })
            {
                var stopwatch = Stopwatch.StartNew();
                PipelinedPlanResultV2 result = PipelinedPrioritizedPlannerV2.Solve(
                    built.Problem, activeRobotCount: robots);
                stopwatch.Stop();
                string assignments = string.Join(",",
                    Enumerable.Range(0, robots).Select(robot =>
                        result.Missions.Count(mission => mission.RobotIndex == robot)));
                Console.WriteLine(
                    $"{robots,6} | {result.Success && result.PhysicallyValid,7} | " +
                    $"{result.Ticks,5} | {result.ExpandedStates,8:N0} | " +
                    $"{stopwatch.ElapsedMilliseconds,10:N0} | [{assignments}] | " +
                    (result.Success ? "valid" : result.FailReason));
            }
        }
    }
}
