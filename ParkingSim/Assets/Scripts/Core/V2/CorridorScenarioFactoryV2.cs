using System;
using System.Collections.Generic;
using System.Linq;

namespace ParkingSim.Core.V2
{
    /// <summary>
    /// 측정정의서의 폭3×길이40셀 통로를 V2 유한 차량/적치 모델로 생성한다.
    /// 통로 왼쪽의 적치실은 진입구 밖 비주차 포장공간을 추상화한다.
    /// </summary>
    public static class CorridorScenarioFactoryV2
    {
        public const int CorridorCells = 40;
        public const int CorridorEntranceX = 12;
        public const int CorridorBottomY = 20;
        public const int BetaCells = 4;
        public const int StagingCapacity = 60;

        public static EmergencyProblemV2 BuildBase(
            int occupiedLanes,
            int robotStationCount = 8,
            OperationTimingV2 timing = null)
        {
            if (occupiedLanes < 1 || occupiedLanes > 3)
                throw new ArgumentOutOfRangeException(nameof(occupiedLanes));
            if (robotStationCount < 1 || robotStationCount > 8)
                throw new ArgumentOutOfRangeException(nameof(robotStationCount));

            const int width = CorridorEntranceX + CorridorCells;
            const int height = 45;
            var floor = new bool[width, height];
            for (int x = 0; x <= CorridorEntranceX; x++)
                for (int y = 0; y < height; y++) floor[x, y] = true;
            for (int x = CorridorEntranceX; x < width; x++)
                for (int y = CorridorBottomY; y < CorridorBottomY + 3; y++) floor[x, y] = true;

            var slots = new List<ParkingSlotV2>();
            for (int x = CorridorEntranceX; x < width; x += 2)
                for (int lane = 0; lane < occupiedLanes; lane++)
                    slots.Add(new ParkingSlotV2(
                        slots.Count,
                        SlotKind.Blocking,
                        new VehiclePose(x, CorridorBottomY + lane, VehicleOrientation.Horizontal)));
            int blockingCount = slots.Count;

            var stagingPoses = new List<VehiclePose>();
            foreach (int x in new[] { 9, 6, 3, 0 })
                for (int y = 0; y <= 42; y += 3)
                    stagingPoses.Add(new VehiclePose(x, y, VehicleOrientation.Vertical));
            foreach (VehiclePose pose in stagingPoses
                         .OrderBy(pose => Math.Abs(pose.X - CorridorEntranceX) +
                                          Math.Abs(pose.Y - (CorridorBottomY + 1))))
            {
                slots.Add(new ParkingSlotV2(slots.Count, SlotKind.Staging, pose));
            }

            var fixedVehicles = new List<VehiclePose>();
            for (int x = CorridorEntranceX; x < width; x += 2)
            {
                floor[x, CorridorBottomY - 2] = true;
                floor[x, CorridorBottomY - 1] = true;
                floor[x, CorridorBottomY + 3] = true;
                floor[x, CorridorBottomY + 4] = true;
                fixedVehicles.Add(new VehiclePose(
                    x, CorridorBottomY - 2, VehicleOrientation.Vertical));
                fixedVehicles.Add(new VehiclePose(
                    x, CorridorBottomY + 3, VehicleOrientation.Vertical));
            }

            int[] startYs = { 19, 20, 21, 22, 23, 18, 24, 17 };
            var starts = Enumerable.Range(0, robotStationCount)
                .Select(index => (10, startYs[index]));
            return new EmergencyProblemV2(
                width,
                height,
                floor,
                slots,
                Enumerable.Range(0, blockingCount),
                starts,
                new (int X, int Y)[0],
                fixedVehicles,
                timing);
        }

        public static EmergencyScenarioBuildResultV2 BuildEmergency(
            int occupiedLanes,
            int fireMeters,
            int robotStationCount = 8,
            OperationTimingV2 timing = null)
        {
            if (fireMeters < 5 || fireMeters > 100 || fireMeters % 5 != 0)
                throw new ArgumentOutOfRangeException(nameof(fireMeters));
            EmergencyProblemV2 baseProblem = BuildBase(
                occupiedLanes, robotStationCount, timing);
            int fireCells = fireMeters / 5 * 2;
            int clearanceCells = Math.Min(CorridorCells, fireCells + BetaCells);
            var required = new List<(int X, int Y)>();
            for (int x = CorridorEntranceX; x < CorridorEntranceX + clearanceCells; x++)
                for (int lane = 0; lane < 3; lane++)
                    required.Add((x, CorridorBottomY + lane));
            int fireX = Math.Min(
                CorridorEntranceX + CorridorCells - 1,
                CorridorEntranceX + fireCells - 1);
            return new EmergencyScenarioV2(
                $"corridor-l{occupiedLanes}-d{fireMeters}",
                (fireX, CorridorBottomY + 1),
                required).Build(baseProblem);
        }
    }
}
