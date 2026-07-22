using System;
using System.Collections.Generic;
using System.Linq;

namespace ParkingSim.Core.V2
{
    /// <summary>오라클·휴리스틱 회귀 비교용 결정론적 소형 문제군.</summary>
    public static class V2ProblemFactory
    {
        public static EmergencyProblemV2 LineProblem(
            int vehicleCount, int stagingSlots = -1, OperationTimingV2 timing = null)
        {
            if (vehicleCount < 1) throw new ArgumentOutOfRangeException(nameof(vehicleCount));
            if (stagingSlots < 0) stagingSlots = vehicleCount;
            if (stagingSlots > vehicleCount) throw new ArgumentOutOfRangeException(nameof(stagingSlots));

            int width = 4 + vehicleCount * 2;
            var slots = new List<ParkingSlotV2>();
            for (int i = 0; i < vehicleCount; i++)
                slots.Add(new ParkingSlotV2(i, SlotKind.Blocking,
                    new VehiclePose(4 + i * 2, 2, VehicleOrientation.Horizontal)));
            for (int i = 0; i < stagingSlots; i++)
                slots.Add(new ParkingSlotV2(vehicleCount + i, SlotKind.Staging,
                    new VehiclePose(i * 2, 0, VehicleOrientation.Vertical)));

            var clearance = new List<(int X, int Y)>();
            for (int x = 4; x < width; x++) clearance.Add((x, 2));
            return new EmergencyProblemV2(
                width: width,
                height: 5,
                floor: EmergencyProblemV2.FullFloor(width, 5),
                slots: slots,
                initialVehicleSlots: Enumerable.Range(0, vehicleCount),
                robotStarts: new[] { (0, 4), (2, 4) },
                clearanceCells: clearance,
                timing: timing);
        }

        /// <summary>
        /// 폭 3셀 통로(y=2..4), 북측 세로 적치 베이 2개, 나머지는 벽/주차면인 소형 블록.
        /// 방해 차량은 가로 1대+세로 1대로 방향을 섞는다.
        /// </summary>
        public static EmergencyProblemV2 ParkingBlockProblem(
            int blockingVehicles = 2, int stagingSlots = 2, OperationTimingV2 timing = null)
        {
            if (blockingVehicles < 0 || blockingVehicles > 2)
                throw new ArgumentOutOfRangeException(nameof(blockingVehicles));
            if (stagingSlots < 0 || stagingSlots > 2)
                throw new ArgumentOutOfRangeException(nameof(stagingSlots));
            const int width = 12, height = 6;
            var floor = new bool[width, height];
            for (int x = 0; x < width; x++)
                for (int y = 2; y <= 4; y++)
                    floor[x, y] = true; // 규정 통로 추상화: 폭 3셀
            foreach (int bayX in new[] { 0, 3 }.Take(stagingSlots))
                for (int y = 0; y <= 1; y++)
                    floor[bayX, y] = true; // 실제 세로 적치 베이

            var sourcePoses = new[]
            {
                new VehiclePose(6, 3, VehicleOrientation.Horizontal),
                new VehiclePose(9, 2, VehicleOrientation.Vertical),
            };
            var stagingPoses = new[]
            {
                new VehiclePose(0, 0, VehicleOrientation.Vertical),
                new VehiclePose(3, 0, VehicleOrientation.Vertical),
            };
            var slots = new List<ParkingSlotV2>();
            for (int i = 0; i < blockingVehicles; i++)
                slots.Add(new ParkingSlotV2(i, SlotKind.Blocking, sourcePoses[i]));
            for (int i = 0; i < stagingSlots; i++)
                slots.Add(new ParkingSlotV2(blockingVehicles + i, SlotKind.Staging, stagingPoses[i]));
            var clearance = new List<(int X, int Y)>();
            for (int x = 4; x < width; x++)
                for (int y = 2; y <= 4; y++)
                    clearance.Add((x, y));

            return new EmergencyProblemV2(
                width, height, floor, slots,
                initialVehicleSlots: Enumerable.Range(0, blockingVehicles),
                robotStarts: new[] { (0, 4), (3, 4) },
                clearanceCells: clearance,
                timing: timing);
        }
    }
}
