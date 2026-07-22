using System.Collections.Generic;
using System.Linq;

namespace ParkingSim.Core.V2
{
    public sealed class SurfaceApartmentScenarioV2
    {
        public EmergencyProblemV2 BaseProblem { get; set; }
        public IReadOnlyList<EmergencyAccessRouteV2> Routes { get; set; }
        public IReadOnlyList<(int X, int Y)> FullClearanceCells { get; set; }
    }

    /// <summary>법정 전용구역은 비워 두고 그곳까지의 내부 접근축 두 개를 비교하는 지상 단지 게이트.</summary>
    public static class SurfaceApartmentScenarioFactoryV2
    {
        public static SurfaceApartmentScenarioV2 Build(OperationTimingV2 timing = null)
        {
            const int width = 24, height = 14;
            var floor = new bool[width, height];
            Fill(floor, 0, 23, 4, 6);   // 하부 직선로
            Fill(floor, 0, 23, 8, 10);  // 상부 우회로
            Fill(floor, 0, 4, 0, 10);   // 단지 입구 연결부
            Fill(floor, 19, 23, 4, 10); // 화재동 전개공간 연결부
            Fill(floor, 0, 18, 0, 2);   // 유한 적치 구역
            Fill(floor, 5, 18, 11, 13); // 일반 주차열

            var slots = new List<ParkingSlotV2>();
            AddSlot(slots, SlotKind.Blocking, 7, 5);
            AddSlot(slots, SlotKind.Blocking, 12, 5);
            AddSlot(slots, SlotKind.Blocking, 16, 5);
            AddSlot(slots, SlotKind.Blocking, 7, 9);
            AddSlot(slots, SlotKind.Blocking, 13, 9);
            foreach (int x in new[] { 0, 3, 6, 9, 12 })
                AddSlot(slots, SlotKind.Staging, x, 0);

            var fixedVehicles = new List<VehiclePose>();
            foreach (int x in new[] { 5, 8, 11, 14, 17 })
                fixedVehicles.Add(new VehiclePose(x, 12, VehicleOrientation.Horizontal));

            var lowerCells = RectangleCells(0, 22, 4, 6).ToArray();
            var upperCells = RectangleCells(0, 22, 8, 10)
                .Concat(RectangleCells(0, 4, 4, 7))
                .Concat(RectangleCells(19, 22, 4, 7))
                .Distinct().ToArray();
            var entrance = (X: 1, Y: 5);
            var fire = (X: 22, Y: 5);
            var problem = new EmergencyProblemV2(
                width, height, floor, slots, Enumerable.Range(0, 5),
                new[] { (0, 3), (1, 3), (2, 3), (3, 3) },
                System.Array.Empty<(int X, int Y)>(), fixedVehicles, timing, fire);
            return new SurfaceApartmentScenarioV2
            {
                BaseProblem = problem,
                Routes = new[]
                {
                    new EmergencyAccessRouteV2("lower-direct", entrance, fire, lowerCells),
                    new EmergencyAccessRouteV2("upper-detour", entrance, fire, upperCells),
                },
                FullClearanceCells = lowerCells.Concat(upperCells).Distinct().ToArray(),
            };
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
