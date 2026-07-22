using System;
using System.Collections.Generic;
using System.Linq;

namespace ParkingSim.Core.V2
{
    /// <summary>오라클·휴리스틱 회귀 비교용 결정론적 소형 문제군.</summary>
    public static class V2ProblemFactory
    {
        public static EmergencyProblemV2 LineProblem(int vehicleCount, int stagingSlots = -1)
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
                clearanceCells: clearance);
        }
    }
}
