using System;

namespace ParkingSim.Core.V2
{
    public sealed class CapacityTradeoffResultV2
    {
        public int GrossAdditionalCars { get; set; }
        public int DedicatedStagingSlots { get; set; }
        public int NetAlpha { get; set; }
        public bool Success { get; set; }
        public int ClearanceTicks { get; set; }
        public string FailReason { get; set; }
        public int ExpandedStates { get; set; }
    }

    public static class CapacityTradeoffV2
    {
        /// <summary>
        /// 동일 부지 회계. stagingOpportunityCostSlots=0이면 기존 비주차 포장,
        /// 주차 가능한 면을 전용했다면 그 수만큼 α에서 차감한다.
        /// </summary>
        public static CapacityTradeoffResultV2 EvaluateExact(
            EmergencyProblemV2 problem, int stagingOpportunityCostSlots,
            int maxExpansions = 1000000)
        {
            if (stagingOpportunityCostSlots < 0 ||
                stagingOpportunityCostSlots > problem.StagingCapacity)
                throw new ArgumentOutOfRangeException(nameof(stagingOpportunityCostSlots));

            var plan = ExactEmergencySolverV2.SolveWeighted(
                problem, heuristicWeight: 1, maxExpansions: maxExpansions);
            return new CapacityTradeoffResultV2
            {
                GrossAdditionalCars = problem.VehicleCount,
                DedicatedStagingSlots = stagingOpportunityCostSlots,
                NetAlpha = problem.VehicleCount - stagingOpportunityCostSlots,
                Success = plan.Success,
                ClearanceTicks = plan.Ticks,
                FailReason = plan.FailReason,
                ExpandedStates = plan.ExpandedStates,
            };
        }
    }
}
