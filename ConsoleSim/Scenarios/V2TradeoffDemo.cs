using System;
using ParkingSim.Core.V2;

namespace ParkingSim.Scenarios
{
    public static class V2TradeoffDemo
    {
        public static void Run()
        {
            Console.WriteLine("=== Model V2 소형 α↔N (동일 주차 블록, exact) ===");
            Console.WriteLine("추가차 | 적치면 | 확보 | N(틱) | 순α(주차면 전용) | 순α(비주차 포장) ");
            Console.WriteLine(new string('-', 78));
            foreach (var policy in new[] { (0, 0), (1, 1), (2, 2), (2, 1) })
            {
                var problem = V2ProblemFactory.ParkingBlockProblem(policy.Item1, policy.Item2);
                var parkingLand = CapacityTradeoffV2.EvaluateExact(problem, policy.Item2);
                int nonParkingAlpha = policy.Item1;
                Console.WriteLine(
                    $" {policy.Item1,4}  | {policy.Item2,4}  |" +
                    $" {(parkingLand.Success ? "성공" : "실패"),4} |" +
                    $" {(parkingLand.Success ? parkingLand.ClearanceTicks.ToString() : "-"),6} |" +
                    $" {parkingLand.NetAlpha,14} | {nonParkingAlpha,14} " +
                    $"{(parkingLand.FailReason == null ? "" : "— " + parkingLand.FailReason)}");
            }
        }
    }
}
