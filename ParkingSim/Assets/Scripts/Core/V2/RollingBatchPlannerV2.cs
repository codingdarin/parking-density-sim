using System;
using System.Collections.Generic;
using System.Linq;

namespace ParkingSim.Core.V2
{
    public sealed class RollingBatchResultV2
    {
        public bool Success { get; set; }
        public string FailReason { get; set; }
        public int TotalTicks { get; set; }
        public int BatchCount { get; set; }
        public int ExpandedStates { get; set; }
        public int VehicleCount { get; set; }
        public int[] FinalStagingSlotIds { get; set; }
        public (int X, int Y)[] FinalRobotPositions { get; set; }
        public List<int> BatchSizes { get; } = new List<int>();
    }

    /// <summary>
    /// 최대 4대씩 공동 정확해를 푸는 rolling-horizon 분해.
    /// 창 밖 차량과 이미 적치한 차량은 고정 1×2 장애물로 유지하고,
    /// 로봇 최종 위치·사용한 슬롯을 다음 창으로 전달한다.
    /// </summary>
    public static class RollingBatchPlannerV2
    {
        public static RollingBatchResultV2 Solve(
            EmergencyProblemV2 fullProblem, int batchSize = 3, int maxExpansionsPerBatch = 1000000)
        {
            if (batchSize < 1 || batchSize > 4)
                throw new ArgumentOutOfRangeException(nameof(batchSize), "오라클 검증 범위는 창당 1~4대");

            var result = new RollingBatchResultV2 { VehicleCount = fullProblem.VehicleCount };
            if (fullProblem.StagingCapacity < fullProblem.VehicleCount)
            {
                result.FailReason = $"적치 용량 부족: 차량 {fullProblem.VehicleCount}대 > 슬롯 {fullProblem.StagingCapacity}면";
                return result;
            }

            var pending = Enumerable.Range(0, fullProblem.VehicleCount)
                .OrderByDescending(v => fullProblem.Slots[fullProblem.InitialVehicleSlots[v]].Pose.X)
                .ToList();
            var freeStaging = Enumerable.Range(0, fullProblem.Slots.Count)
                .Where(s => fullProblem.Slots[s].Kind == SlotKind.Staging)
                .OrderBy(s => fullProblem.Slots[s].Pose.X)
                .ToList();
            var occupiedStaging = new Dictionary<int, int>(); // vehicle → full slot index
            var robotStarts = fullProblem.RobotStarts.ToArray();

            while (pending.Count > 0)
            {
                int attemptSize = Math.Min(batchSize, pending.Count);
                List<int> batchVehicles = null;
                EmergencyProblemV2 subProblem = null;
                ExactEmergencyResultV2 batch = null;
                string lastFailure = null;

                while (attemptSize >= 1)
                {
                    batchVehicles = pending.Take(attemptSize).ToList();
                    var batchDestinations = freeStaging.Take(attemptSize).ToList();
                    subProblem = BuildSubProblem(
                        fullProblem, pending, batchVehicles, batchDestinations,
                        occupiedStaging.Values, robotStarts);
                    batch = ExactEmergencySolverV2.SolveWeighted(
                        subProblem, heuristicWeight: 1,
                        maxExpansions: maxExpansionsPerBatch, activeRobotCount: 2);
                    result.ExpandedStates += batch.ExpandedStates;
                    if (batch.Success) break;
                    lastFailure = $"창 {attemptSize}대: {batch.FailReason}";
                    attemptSize--;
                }

                if (batch == null || !batch.Success)
                {
                    result.FailReason = $"배치 {result.BatchCount + 1} 실패: {lastFailure}";
                    return result;
                }

                for (int i = 0; i < batchVehicles.Count; i++)
                {
                    int subSlot = batch.FinalVehicleSlots[i];
                    int fullSlotId = subProblem.Slots[subSlot].Id;
                    int fullSlotIndex = Enumerable.Range(0, fullProblem.Slots.Count)
                        .First(s => fullProblem.Slots[s].Id == fullSlotId);
                    occupiedStaging[batchVehicles[i]] = fullSlotIndex;
                    freeStaging.Remove(fullSlotIndex);
                    pending.Remove(batchVehicles[i]);
                }
                robotStarts = batch.FinalRobotPositions;
                result.FinalRobotPositions = robotStarts;
                result.TotalTicks += batch.Ticks;
                result.BatchCount++;
                result.BatchSizes.Add(batchVehicles.Count);
            }

            result.Success = true;
            result.FinalStagingSlotIds = Enumerable.Range(0, fullProblem.VehicleCount)
                .Select(v => fullProblem.Slots[occupiedStaging[v]].Id)
                .ToArray();
            return result;
        }

        private static EmergencyProblemV2 BuildSubProblem(
            EmergencyProblemV2 fullProblem,
            List<int> pending,
            List<int> batchVehicles,
            List<int> batchDestinations,
            IEnumerable<int> occupiedStaging,
            (int X, int Y)[] robotStarts)
        {
            var batchVehicleSet = new HashSet<int>(batchVehicles);
            var subSlots = new List<ParkingSlotV2>();
            foreach (int v in batchVehicles)
            {
                int fullSource = fullProblem.InitialVehicleSlots[v];
                var slot = fullProblem.Slots[fullSource];
                subSlots.Add(new ParkingSlotV2(fullSource, SlotKind.Blocking, slot.Pose));
            }
            foreach (int fullDest in batchDestinations)
            {
                var slot = fullProblem.Slots[fullDest];
                subSlots.Add(new ParkingSlotV2(fullDest, SlotKind.Staging, slot.Pose));
            }

            var fixedVehicles = new List<VehiclePose>(fullProblem.FixedVehiclePoses);
            foreach (int v in pending)
                if (!batchVehicleSet.Contains(v))
                    fixedVehicles.Add(fullProblem.Slots[fullProblem.InitialVehicleSlots[v]].Pose);
            foreach (int fullSlot in occupiedStaging)
                fixedVehicles.Add(fullProblem.Slots[fullSlot].Pose);

            return new EmergencyProblemV2(
                fullProblem.Width,
                fullProblem.Height,
                fullProblem.CopyFloor(),
                subSlots,
                Enumerable.Range(0, batchVehicles.Count),
                robotStarts,
                clearanceCells: new (int X, int Y)[0],
                fixedVehiclePoses: fixedVehicles);
        }
    }
}
