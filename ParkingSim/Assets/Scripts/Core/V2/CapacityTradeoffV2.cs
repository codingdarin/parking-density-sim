using System;
using System.Collections.Generic;
using System.Linq;

namespace ParkingSim.Core.V2
{
    public enum StagingLandKindV2
    {
        ExistingNonParkingPaved,
        ConvertedParkingSpace,
        Unverified,
    }

    public sealed class StagingLandProfileV2
    {
        public int SlotId { get; }
        public StagingLandKindV2 LandKind { get; }

        public StagingLandProfileV2(int slotId, StagingLandKindV2 landKind)
        {
            SlotId = slotId;
            LandKind = landKind;
        }
    }

    public sealed class StagingLandAccountingResultV2
    {
        public bool PlanSuccess { get; set; }
        public bool LandClassificationComplete { get; set; }
        public bool NetAlphaClaimable { get; set; }
        public string FailReason { get; set; }
        public int GrossAdditionalCars { get; set; }
        public int RequiredStagingSlots { get; set; }
        public int DedicatedStagingSlots { get; set; }
        public int UsedStagingSlots { get; set; }
        public int UnusedDedicatedStagingSlots { get; set; }
        public int ExistingNonParkingPavedSlots { get; set; }
        public int ConvertedParkingSlots { get; set; }
        public int UnverifiedSlots { get; set; }
        public int ParkingOpportunityCostCars { get; set; }
        public int? VerifiedNetAlpha { get; set; }
        public int ClearanceTicks { get; set; }
    }

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
        /// 물리 계획에 쓰인 사건별 적치면과 레이아웃에 상시 전용된 전체 적치면을 분리해 회계한다.
        /// 순α는 실제 사용량이 아니라 전체 전용 적치면 중 주차 가능 부지의 기회비용을 gross α에서 뺀다.
        /// 모든 적치 슬롯의 토지 성격이 확인되고 물리 계획이 유효할 때만 순이득을 주장할 수 있다.
        /// </summary>
        public static StagingLandAccountingResultV2 EvaluateStagingLand(
            EmergencyProblemV2 scenarioProblem,
            PipelinedPlanResultV2 plan,
            int grossAdditionalCars,
            IEnumerable<StagingLandProfileV2> landProfiles)
        {
            if (scenarioProblem == null)
                throw new ArgumentNullException(nameof(scenarioProblem));
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (landProfiles == null) throw new ArgumentNullException(nameof(landProfiles));
            if (grossAdditionalCars < scenarioProblem.VehicleCount)
                throw new ArgumentOutOfRangeException(
                    nameof(grossAdditionalCars),
                    "gross α는 선택 경로의 이동 차량 수보다 작을 수 없음");

            ParkingSlotV2[] stagingSlots = scenarioProblem.Slots
                .Where(slot => slot.Kind == SlotKind.Staging)
                .ToArray();
            var stagingIds = new HashSet<int>(stagingSlots.Select(slot => slot.Id));
            StagingLandProfileV2[] profiles = landProfiles.ToArray();
            if (profiles.Any(profile => profile == null))
                throw new ArgumentException("토지 성격 항목은 null일 수 없음", nameof(landProfiles));
            if (profiles.Select(profile => profile.SlotId).Distinct().Count() != profiles.Length)
                throw new ArgumentException("같은 적치 슬롯의 토지 성격이 중복됨", nameof(landProfiles));
            foreach (StagingLandProfileV2 profile in profiles)
                if (!stagingIds.Contains(profile.SlotId))
                    throw new ArgumentException(
                        $"적치 레이아웃에 없는 슬롯 ID {profile.SlotId}",
                        nameof(landProfiles));

            var bySlotId = profiles.ToDictionary(profile => profile.SlotId);
            int existing = 0;
            int converted = 0;
            int unverified = 0;
            foreach (ParkingSlotV2 slot in stagingSlots)
            {
                StagingLandKindV2 kind = bySlotId.TryGetValue(
                    slot.Id, out StagingLandProfileV2 profile)
                    ? profile.LandKind
                    : StagingLandKindV2.Unverified;
                switch (kind)
                {
                    case StagingLandKindV2.ExistingNonParkingPaved:
                        existing++;
                        break;
                    case StagingLandKindV2.ConvertedParkingSpace:
                        converted++;
                        break;
                    default:
                        unverified++;
                        break;
                }
            }

            bool finalSlotsValid;
            int used = CountUsedStagingSlots(scenarioProblem, plan, out finalSlotsValid);
            bool planSuccess = plan.Success && plan.PhysicallyValid &&
                               finalSlotsValid &&
                               used == scenarioProblem.VehicleCount;
            bool classificationComplete = unverified == 0;
            bool claimable = planSuccess && classificationComplete &&
                             stagingSlots.Length >= scenarioProblem.VehicleCount;
            string failReason = null;
            if (!planSuccess)
                failReason = "물리 계획 실패: " + (plan.FailReason ?? "재생 검증 실패");
            else if (stagingSlots.Length < scenarioProblem.VehicleCount)
                failReason =
                    $"적치 용량 부족: 차량 {scenarioProblem.VehicleCount}대 > " +
                    $"슬롯 {stagingSlots.Length}면";
            else if (!classificationComplete)
                failReason = $"적치 토지 성격 미확인 {unverified}면 — 순이득 주장 불가";

            return new StagingLandAccountingResultV2
            {
                PlanSuccess = planSuccess,
                LandClassificationComplete = classificationComplete,
                NetAlphaClaimable = claimable,
                FailReason = failReason,
                GrossAdditionalCars = grossAdditionalCars,
                RequiredStagingSlots = scenarioProblem.VehicleCount,
                DedicatedStagingSlots = stagingSlots.Length,
                UsedStagingSlots = used,
                UnusedDedicatedStagingSlots = stagingSlots.Length - used,
                ExistingNonParkingPavedSlots = existing,
                ConvertedParkingSlots = converted,
                UnverifiedSlots = unverified,
                ParkingOpportunityCostCars = converted,
                VerifiedNetAlpha = claimable
                    ? grossAdditionalCars - converted
                    : (int?)null,
                ClearanceTicks = plan.Ticks,
            };
        }

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

        private static int CountUsedStagingSlots(
            EmergencyProblemV2 problem,
            PipelinedPlanResultV2 plan,
            out bool valid)
        {
            valid = plan.FinalVehicleSlots != null;
            if (!valid) return 0;
            var used = new HashSet<int>();
            foreach (int slotIndex in plan.FinalVehicleSlots)
            {
                if (slotIndex < 0 || slotIndex >= problem.Slots.Count ||
                    problem.Slots[slotIndex].Kind != SlotKind.Staging)
                {
                    valid = false;
                    return 0;
                }
                used.Add(slotIndex);
            }
            if (used.Count != plan.FinalVehicleSlots.Length) valid = false;
            return used.Count;
        }
    }
}
