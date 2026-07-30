using System.Collections.Generic;
using System.Linq;

namespace ParkingSim.Core.V2
{
    public enum SurfaceVehiclePlacementV2
    {
        LowerFirst,
        UpperFirst,
        AlternatingEntranceFirst,
        AlternatingFireFirst,
    }

    public sealed class SurfaceApartmentScenarioV2
    {
        public EmergencyProblemV2 BaseProblem { get; set; }
        public IReadOnlyList<EmergencyAccessRouteV2> Routes { get; set; }
        public IReadOnlyList<(int X, int Y)> FullClearanceCells { get; set; }
        public int BlockingVehicleCount { get; set; }
        public int DedicatedStagingCapacity { get; set; }
        public SurfaceVehiclePlacementV2 Placement { get; set; }
    }

    /// <summary>법정 전용구역은 비워 두고 그곳까지의 내부 접근축 두 개를 비교하는 지상 단지 게이트.</summary>
    public static class SurfaceApartmentScenarioFactoryV2
    {
        public const int MaximumBlockingVehicles = 14;
        public const int MaximumDensityStagingCapacity = 14;

        public static SurfaceApartmentScenarioV2 Build(OperationTimingV2 timing = null)
        {
            return BuildScenario(
                new[]
                {
                    new VehiclePose(7, 5, VehicleOrientation.Horizontal),
                    new VehiclePose(12, 5, VehicleOrientation.Horizontal),
                    new VehiclePose(16, 5, VehicleOrientation.Horizontal),
                    new VehiclePose(7, 9, VehicleOrientation.Horizontal),
                    new VehiclePose(13, 9, VehicleOrientation.Horizontal),
                },
                new[]
                {
                    new VehiclePose(0, 0, VehicleOrientation.Horizontal),
                    new VehiclePose(3, 0, VehicleOrientation.Horizontal),
                    new VehiclePose(6, 0, VehicleOrientation.Horizontal),
                    new VehiclePose(9, 0, VehicleOrientation.Horizontal),
                    new VehiclePose(12, 0, VehicleOrientation.Horizontal),
                },
                SurfaceVehiclePlacementV2.AlternatingEntranceFirst,
                timing);
        }

        public static SurfaceApartmentScenarioV2 BuildDensity(
            int blockingVehicleCount,
            SurfaceVehiclePlacementV2 placement,
            int stagingCapacity,
            OperationTimingV2 timing = null)
        {
            if (blockingVehicleCount < 0 ||
                blockingVehicleCount > MaximumBlockingVehicles)
                throw new System.ArgumentOutOfRangeException(nameof(blockingVehicleCount));
            if (stagingCapacity < 1 ||
                stagingCapacity > MaximumDensityStagingCapacity)
                throw new System.ArgumentOutOfRangeException(nameof(stagingCapacity));

            VehiclePose[] blocking = DensityBlockingOrder(placement)
                .Take(blockingVehicleCount)
                .ToArray();
            VehiclePose[] staging = DensityStagingSlots()
                .Take(stagingCapacity)
                .ToArray();
            return BuildScenario(blocking, staging, placement, timing);
        }

        private static SurfaceApartmentScenarioV2 BuildScenario(
            IReadOnlyList<VehiclePose> blocking,
            IReadOnlyList<VehiclePose> staging,
            SurfaceVehiclePlacementV2 placement,
            OperationTimingV2 timing)
        {
            const int width = 24;
            const int height = 14;
            bool[,] floor = BuildFloor(width, height);
            var slots = new List<ParkingSlotV2>();
            foreach (VehiclePose pose in blocking)
                AddSlot(slots, SlotKind.Blocking, pose.X, pose.Y);
            foreach (VehiclePose pose in staging)
                AddSlot(slots, SlotKind.Staging, pose.X, pose.Y);

            var fixedVehicles = new List<VehiclePose>();
            foreach (int x in new[] { 5, 8, 11, 14, 17 })
                fixedVehicles.Add(new VehiclePose(x, 12, VehicleOrientation.Horizontal));

            (int X, int Y)[] lowerCells = RectangleCells(0, 22, 4, 6).ToArray();
            (int X, int Y)[] upperCells = RectangleCells(0, 22, 8, 10)
                .Concat(RectangleCells(0, 4, 4, 7))
                .Concat(RectangleCells(19, 22, 4, 7))
                .Distinct()
                .ToArray();
            var entrance = (X: 1, Y: 5);
            var fire = (X: 22, Y: 5);
            var problem = new EmergencyProblemV2(
                width,
                height,
                floor,
                slots,
                Enumerable.Range(0, blocking.Count),
                new[] { (0, 3), (1, 3), (2, 3), (3, 3) },
                System.Array.Empty<(int X, int Y)>(),
                fixedVehicles,
                timing,
                fire);
            return new SurfaceApartmentScenarioV2
            {
                BaseProblem = problem,
                Routes = new[]
                {
                    new EmergencyAccessRouteV2("lower-direct", entrance, fire, lowerCells),
                    new EmergencyAccessRouteV2("upper-detour", entrance, fire, upperCells),
                },
                FullClearanceCells = lowerCells.Concat(upperCells).Distinct().ToArray(),
                BlockingVehicleCount = blocking.Count,
                DedicatedStagingCapacity = staging.Count,
                Placement = placement,
            };
        }

        private static bool[,] BuildFloor(int width, int height)
        {
            var floor = new bool[width, height];
            Fill(floor, 0, 23, 4, 6);   // 하부 직선로
            Fill(floor, 0, 23, 8, 10);  // 상부 우회로
            Fill(floor, 0, 4, 0, 10);   // 단지 입구 연결부
            Fill(floor, 19, 23, 4, 10); // 화재동 전개공간 연결부
            Fill(floor, 0, 18, 0, 2);   // 유한 적치 구역
            Fill(floor, 5, 18, 11, 13); // 일반 주차열
            return floor;
        }

        private static IEnumerable<VehiclePose> DensityBlockingOrder(
            SurfaceVehiclePlacementV2 placement)
        {
            VehiclePose[] lower = DensityLane(5).ToArray();
            VehiclePose[] upper = DensityLane(9).ToArray();
            switch (placement)
            {
                case SurfaceVehiclePlacementV2.LowerFirst:
                    return lower.Concat(upper);
                case SurfaceVehiclePlacementV2.UpperFirst:
                    return upper.Concat(lower);
                case SurfaceVehiclePlacementV2.AlternatingEntranceFirst:
                    return Interleave(lower, upper);
                case SurfaceVehiclePlacementV2.AlternatingFireFirst:
                    return Interleave(lower.Reverse(), upper.Reverse());
                default:
                    throw new System.ArgumentOutOfRangeException(nameof(placement));
            }
        }

        private static IEnumerable<VehiclePose> DensityLane(int y)
        {
            foreach (int x in new[] { 5, 7, 9, 11, 13, 15, 17 })
                yield return new VehiclePose(x, y, VehicleOrientation.Horizontal);
        }

        private static IEnumerable<VehiclePose> DensityStagingSlots()
        {
            foreach (int x in new[] { 0, 3, 6, 9, 12 })
                yield return new VehiclePose(x, 0, VehicleOrientation.Horizontal);
            for (int x = 0; x <= 16; x += 2)
                yield return new VehiclePose(x, 2, VehicleOrientation.Horizontal);
        }

        private static IEnumerable<VehiclePose> Interleave(
            IEnumerable<VehiclePose> first,
            IEnumerable<VehiclePose> second)
        {
            VehiclePose[] left = first.ToArray();
            VehiclePose[] right = second.ToArray();
            for (int i = 0; i < left.Length; i++)
            {
                yield return left[i];
                yield return right[i];
            }
        }

        private static void AddSlot(
            ICollection<ParkingSlotV2> slots, SlotKind kind, int x, int y)
        {
            slots.Add(new ParkingSlotV2(
                slots.Count, kind, new VehiclePose(x, y, VehicleOrientation.Horizontal)));
        }

        private static void Fill(bool[,] floor, int minX, int maxX, int minY, int maxY)
        {
            for (int x = minX; x <= maxX; x++)
                for (int y = minY; y <= maxY; y++) floor[x, y] = true;
        }

        private static IEnumerable<(int X, int Y)> RectangleCells(
            int minX, int maxX, int minY, int maxY)
        {
            for (int x = minX; x <= maxX; x++)
                for (int y = minY; y <= maxY; y++) yield return (x, y);
        }
    }
}
