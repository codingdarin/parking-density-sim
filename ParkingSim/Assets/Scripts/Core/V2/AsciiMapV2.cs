using System;
using System.Collections.Generic;
using System.Linq;

namespace ParkingSim.Core.V2
{
    /// <summary>
    /// 위에서 아래 순서의 ASCII 행으로 V2 맵을 정의한다.
    /// # 벽, . 바닥, ! 확보구간, 1/2 로봇 시작점,
    /// &gt;/^ 가로/세로 방해차 슬롯, =/| 가로/세로 적치 슬롯,
    /// -/I 가로/세로 고정 차량 앵커다. 모든 차량 기호는 1×2 pose의 첫 셀이다.
    /// </summary>
    public sealed class AsciiMapV2
    {
        private readonly string[] _rowsTopDown;

        public string Name { get; }
        public int Width => _rowsTopDown[0].Length;
        public int Height => _rowsTopDown.Length;
        public IReadOnlyList<string> RowsTopDown => _rowsTopDown;

        public AsciiMapV2(string name, params string[] rowsTopDown)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("맵 이름이 필요함", nameof(name));
            if (rowsTopDown == null || rowsTopDown.Length == 0)
                throw new ArgumentException("ASCII 맵 행이 필요함", nameof(rowsTopDown));
            if (rowsTopDown.Any(row => row == null || row.Length != rowsTopDown[0].Length))
                throw new ArgumentException("모든 ASCII 맵 행의 폭이 같아야 함", nameof(rowsTopDown));
            Name = name;
            _rowsTopDown = (string[])rowsTopDown.Clone();
        }

        public EmergencyProblemV2 Build(OperationTimingV2 timing = null)
        {
            var floor = new bool[Width, Height];
            var blocking = new List<VehiclePose>();
            var staging = new List<VehiclePose>();
            var fixedVehicles = new List<VehiclePose>();
            var starts = new (int X, int Y)?[2];
            var clearance = new HashSet<(int X, int Y)>();

            for (int row = 0; row < Height; row++)
            {
                int y = Height - 1 - row;
                for (int x = 0; x < Width; x++)
                {
                    char symbol = _rowsTopDown[row][x];
                    if (!IsSupported(symbol))
                        throw new FormatException($"{Name}: 지원하지 않는 기호 '{symbol}' ({x},{y})");
                    floor[x, y] = symbol != '#';
                    switch (symbol)
                    {
                        case '!': clearance.Add((x, y)); break;
                        case '1': SetRobotStart(starts, 0, x, y); break;
                        case '2': SetRobotStart(starts, 1, x, y); break;
                        case '>': AddBlocking(blocking, clearance, x, y, VehicleOrientation.Horizontal); break;
                        case '^': AddBlocking(blocking, clearance, x, y, VehicleOrientation.Vertical); break;
                        case '=': staging.Add(new VehiclePose(x, y, VehicleOrientation.Horizontal)); break;
                        case '|': staging.Add(new VehiclePose(x, y, VehicleOrientation.Vertical)); break;
                        case '-': fixedVehicles.Add(new VehiclePose(x, y, VehicleOrientation.Horizontal)); break;
                        case 'I': fixedVehicles.Add(new VehiclePose(x, y, VehicleOrientation.Vertical)); break;
                    }
                }
            }

            if (!starts[0].HasValue || !starts[1].HasValue)
                throw new FormatException(Name + ": 로봇 시작점 1과 2가 각각 하나씩 필요함");

            var slots = new List<ParkingSlotV2>();
            foreach (VehiclePose pose in blocking)
                slots.Add(new ParkingSlotV2(slots.Count, SlotKind.Blocking, pose));
            foreach (VehiclePose pose in staging)
                slots.Add(new ParkingSlotV2(slots.Count, SlotKind.Staging, pose));

            return new EmergencyProblemV2(
                Width,
                Height,
                floor,
                slots,
                Enumerable.Range(0, blocking.Count),
                new[] { starts[0].Value, starts[1].Value },
                clearance,
                fixedVehicles,
                timing);
        }

        private static void AddBlocking(
            ICollection<VehiclePose> blocking,
            ISet<(int X, int Y)> clearance,
            int x,
            int y,
            VehicleOrientation orientation)
        {
            var pose = new VehiclePose(x, y, orientation);
            blocking.Add(pose);
            clearance.Add((pose.X, pose.Y));
            clearance.Add(pose.SecondCell);
        }

        private static void SetRobotStart((int X, int Y)?[] starts, int index, int x, int y)
        {
            if (starts[index].HasValue)
                throw new FormatException("로봇 시작점 " + (index + 1) + "이 중복됨");
            starts[index] = (x, y);
        }

        private static bool IsSupported(char symbol)
        {
            return symbol == '#' || symbol == '.' || symbol == '!' ||
                   symbol == '1' || symbol == '2' ||
                   symbol == '>' || symbol == '^' || symbol == '=' || symbol == '|' ||
                   symbol == '-' || symbol == 'I';
        }
    }

    public static class V2MapCatalog
    {
        public static readonly AsciiMapV2 SmallParkingBlock = new AsciiMapV2(
            "small-parking-block",
            "############",
            "1..2!!!!!!!!",
            "....!!>!!!!!",
            "....!!!!!^!!",
            ".##.########",
            "|##|########");

        public static readonly AsciiMapV2 LTurn = new AsciiMapV2(
            "l-turn",
            "1..####",
            "2..####",
            "...####",
            "...####",
            ".......",
            ".......",
            ".......");

        public static readonly AsciiMapV2 TJunction = new AsciiMapV2(
            "t-junction",
            ".........",
            ".........",
            ".........",
            "###1.2###",
            "###...###",
            "###...###",
            "###...###");

        public static readonly AsciiMapV2 ApartmentAislePrototype = new AsciiMapV2(
            "apartment-aisle-prototype",
            "##################",
            "..................",
            "I.I.I.I.I.I.I.I.I.",
            "1..2!!!!!!!!!!!!!!",
            "....!!>!!!!!!!!!!!",
            "....!!!!!^!!!!!!!!",
            "..................",
            "..................",
            "|..|I.I.I.I.I.I.I.");

        public static readonly AsciiMapV2 ApartmentConstrainedPrototype = new AsciiMapV2(
            "apartment-constrained-prototype",
            "####################",
            "####################",
            "....................",
            "IIIIIIIIIIIIIIIIIIII",
            ".....#......#.......",
            "1..2......!!!!>!!!!!",
            "........#.!!!!!#!!^!",
            "....................",
            ".....IIIIIIIIIIIIIII",
            "....################",
            "|..|################");
    }
}
