using System;
using System.Diagnostics;
using System.Linq;
using ParkingSim.Core.V2;

namespace ParkingSim.Scenarios
{
    public static class V2RobotSweepDemo
    {
        public static void Run()
        {
            EmergencyProblemV2 problem = V2ProblemFactory.LineProblem(
                vehicleCount: 8, robotStationCount: 8);
            Console.WriteLine("=== V2 운송 유닛 수 일반화 게이트 ===");
            Console.WriteLine("차량8·직선 기하·대기소8칸 고정");
            Console.WriteLine("robots | success | ticks | expanded | elapsed_ms | missions/robot");
            Console.WriteLine(new string('-', 78));
            foreach (int robots in new[] { 1, 2, 4, 8 })
            {
                var stopwatch = Stopwatch.StartNew();
                PipelinedPlanResultV2 result = PipelinedPrioritizedPlannerV2.Solve(
                    problem, activeRobotCount: robots);
                stopwatch.Stop();
                string assignments = string.Join(",",
                    Enumerable.Range(0, robots).Select(robot =>
                        result.Missions.Count(mission => mission.RobotIndex == robot)));
                Console.WriteLine(
                    $"{robots,6} | {result.Success && result.PhysicallyValid,7} | " +
                    $"{result.Ticks,5} | {result.ExpandedStates,8:N0} | " +
                    $"{stopwatch.ElapsedMilliseconds,10:N0} | [{assignments}]" +
                    (result.Success ? string.Empty : " " + result.FailReason));
            }
        }
    }
}
