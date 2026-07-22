using System;
using System.Collections.Generic;
using System.Linq;

namespace ParkingSim.Core.V2
{
    public enum VehicleOrientation : byte
    {
        Horizontal,
        Vertical,
    }

    public enum SlotKind : byte
    {
        Blocking,
        Staging,
    }

    public sealed class OperationTimingV2
    {
        public int LiftServiceTicks { get; }
        public int DropServiceTicks { get; }
        /// <summary>제어/예약 안전여유. 물리 서비스시간과 분리하며 exact 물리 makespan에는 미반영.</summary>
        public int SafetyBufferTicks { get; }

        public OperationTimingV2(
            int liftServiceTicks = 1, int dropServiceTicks = 1, int safetyBufferTicks = 0)
        {
            if (liftServiceTicks < 1 || dropServiceTicks < 1 || safetyBufferTicks < 0)
                throw new ArgumentOutOfRangeException(nameof(liftServiceTicks));
            LiftServiceTicks = liftServiceTicks;
            DropServiceTicks = dropServiceTicks;
            SafetyBufferTicks = safetyBufferTicks;
        }
    }

    /// <summary>차량 앵커+방향. 차량은 항상 1×2셀을 점유하며 소멸하지 않는다.</summary>
    public readonly struct VehiclePose : IEquatable<VehiclePose>
    {
        public int X { get; }
        public int Y { get; }
        public VehicleOrientation Orientation { get; }

        public VehiclePose(int x, int y, VehicleOrientation orientation)
        {
            X = x;
            Y = y;
            Orientation = orientation;
        }

        public (int X, int Y) SecondCell => Orientation == VehicleOrientation.Horizontal
            ? (X + 1, Y)
            : (X, Y + 1);

        public VehiclePose Translate(int dx, int dy) => new VehiclePose(X + dx, Y + dy, Orientation);

        public VehiclePose Rotate() => new VehiclePose(
            X, Y,
            Orientation == VehicleOrientation.Horizontal
                ? VehicleOrientation.Vertical
                : VehicleOrientation.Horizontal);

        public bool Equals(VehiclePose other) =>
            X == other.X && Y == other.Y && Orientation == other.Orientation;

        public override bool Equals(object obj) => obj is VehiclePose other && Equals(other);
        public override int GetHashCode() => (X * 397) ^ (Y * 31) ^ (int)Orientation;
        public override string ToString() => $"({X},{Y},{Orientation})";
    }

    public sealed class ParkingSlotV2
    {
        public int Id { get; }
        public SlotKind Kind { get; }
        public VehiclePose Pose { get; }

        public ParkingSlotV2(int id, SlotKind kind, VehiclePose pose)
        {
            Id = id;
            Kind = kind;
            Pose = pose;
        }
    }

    /// <summary>
    /// 작은 정확해 오라클용 물리 문제. 차량은 초기 슬롯에 하나씩 존재하고,
    /// 적치 슬롯은 용량 1이다. floor=false 셀에는 빈 AGV도 진입할 수 없다.
    /// </summary>
    public sealed class EmergencyProblemV2
    {
        private readonly bool[,] _floor;
        private readonly HashSet<(int X, int Y)> _clearanceCells;

        public int Width { get; }
        public int Height { get; }
        public IReadOnlyList<ParkingSlotV2> Slots { get; }
        public IReadOnlyList<int> InitialVehicleSlots { get; }
        public IReadOnlyList<(int X, int Y)> RobotStarts { get; }
        public IReadOnlyList<VehiclePose> FixedVehiclePoses { get; }
        public OperationTimingV2 Timing { get; }
        public (int X, int Y)? FireCell { get; }
        public int VehicleCount => InitialVehicleSlots.Count;
        public int StagingCapacity => Slots.Count(s => s.Kind == SlotKind.Staging);

        public EmergencyProblemV2(
            int width, int height, bool[,] floor,
            IEnumerable<ParkingSlotV2> slots,
            IEnumerable<int> initialVehicleSlots,
            IEnumerable<(int X, int Y)> robotStarts,
            IEnumerable<(int X, int Y)> clearanceCells,
            IEnumerable<VehiclePose> fixedVehiclePoses = null,
            OperationTimingV2 timing = null,
            (int X, int Y)? fireCell = null)
        {
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (floor == null || floor.GetLength(0) != width || floor.GetLength(1) != height)
                throw new ArgumentException("floor 크기가 문제 크기와 일치해야 함", nameof(floor));

            Width = width;
            Height = height;
            _floor = (bool[,])floor.Clone();
            Slots = slots.ToList();
            InitialVehicleSlots = initialVehicleSlots.ToList();
            RobotStarts = robotStarts.ToList();
            _clearanceCells = new HashSet<(int, int)>(clearanceCells);
            FixedVehiclePoses = fixedVehiclePoses == null
                ? new List<VehiclePose>()
                : fixedVehiclePoses.ToList();
            Timing = timing ?? new OperationTimingV2();
            FireCell = fireCell;

            if (RobotStarts.Count < 1)
                throw new ArgumentException("로봇 시작점이 하나 이상 필요함");
            if (RobotStarts.Distinct().Count() != RobotStarts.Count)
                throw new ArgumentException("로봇 시작점은 서로 달라야 함");
            if (InitialVehicleSlots.Distinct().Count() != InitialVehicleSlots.Count)
                throw new ArgumentException("초기 차량은 서로 다른 슬롯에 있어야 함");
            foreach (int slot in InitialVehicleSlots)
                if (slot < 0 || slot >= Slots.Count || Slots[slot].Kind != SlotKind.Blocking)
                    throw new ArgumentException("모든 초기 차량은 유효한 Blocking 슬롯에 있어야 함");
            foreach (var slot in Slots)
                if (!PoseFits(slot.Pose))
                    throw new ArgumentException($"슬롯 {slot.Id} 풋프린트가 floor 밖임");
            for (int i = 0; i < Slots.Count; i++)
                for (int j = i + 1; j < Slots.Count; j++)
                    if (PoseCells(Slots[i].Pose).Overlaps(PoseCells(Slots[j].Pose)))
                        throw new ArgumentException($"슬롯 {Slots[i].Id}와 {Slots[j].Id}의 풋프린트가 겹침");
            foreach (var start in RobotStarts)
                if (!IsFloor(start.X, start.Y))
                    throw new ArgumentException("로봇 시작점이 floor 밖임");
            if (FireCell.HasValue && !IsFloor(FireCell.Value.X, FireCell.Value.Y))
                throw new ArgumentException("화재 위치가 floor 밖임");
            foreach (var pose in FixedVehiclePoses)
                if (!PoseFits(pose))
                    throw new ArgumentException("고정 차량 풋프린트가 floor 밖임");

            var slotCells = new HashSet<(int X, int Y)>();
            foreach (var slot in Slots)
                foreach (var cell in PoseCells(slot.Pose)) slotCells.Add(cell);
            var fixedCells = new HashSet<(int X, int Y)>();
            foreach (var pose in FixedVehiclePoses)
            {
                foreach (var cell in PoseCells(pose))
                {
                    if (slotCells.Contains(cell))
                        throw new ArgumentException("고정 차량과 작업 슬롯의 풋프린트가 겹침");
                    if (!fixedCells.Add(cell))
                        throw new ArgumentException("고정 차량끼리 풋프린트가 겹침");
                }
            }

        }

        public bool IsFloor(int x, int y) =>
            x >= 0 && x < Width && y >= 0 && y < Height && _floor[x, y];

        public bool PoseFits(VehiclePose pose)
        {
            var second = pose.SecondCell;
            return IsFloor(pose.X, pose.Y) && IsFloor(second.X, second.Y);
        }

        public bool IsClearanceCell(int x, int y) => _clearanceCells.Contains((x, y));

        public bool[,] CopyFloor() => (bool[,])_floor.Clone();

        public (int X, int Y)[] CopyClearanceCells() => _clearanceCells.ToArray();

        private static HashSet<(int, int)> PoseCells(VehiclePose pose)
        {
            return new HashSet<(int, int)> { (pose.X, pose.Y), pose.SecondCell };
        }

        public static bool[,] FullFloor(int width, int height)
        {
            var floor = new bool[width, height];
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    floor[x, y] = true;
            return floor;
        }
    }
}
