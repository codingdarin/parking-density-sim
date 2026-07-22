using System;
using System.Collections.Generic;
using System.Linq;

namespace ParkingSim.Core.V2
{
    /// <summary>
    /// 위에서 아래 순서의 ASCII 행으로 V2 맵을 정의한다.
    /// # 벽, . 바닥, ! 확보구간, 1~8 로봇 시작점,
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
            var starts = new (int X, int Y)?[8];
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
                        case '1': case '2': case '3': case '4':
                        case '5': case '6': case '7': case '8':
                            SetRobotStart(starts, symbol - '1', x, y);
                            break;
                        case '>': AddBlocking(blocking, clearance, x, y, VehicleOrientation.Horizontal); break;
                        case '^': AddBlocking(blocking, clearance, x, y, VehicleOrientation.Vertical); break;
                        case '=': staging.Add(new VehiclePose(x, y, VehicleOrientation.Horizontal)); break;
                        case '|': staging.Add(new VehiclePose(x, y, VehicleOrientation.Vertical)); break;
                        case '-': fixedVehicles.Add(new VehiclePose(x, y, VehicleOrientation.Horizontal)); break;
                        case 'I': fixedVehicles.Add(new VehiclePose(x, y, VehicleOrientation.Vertical)); break;
                    }
                }
            }

            int robotCount = 0;
            while (robotCount < starts.Length && starts[robotCount].HasValue) robotCount++;
            if (robotCount == 0)
                throw new FormatException(Name + ": 로봇 시작점 1이 필요함");
            for (int i = robotCount; i < starts.Length; i++)
                if (starts[i].HasValue)
                    throw new FormatException(Name + ": 로봇 시작점 번호는 1부터 연속이어야 함");

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
                starts.Take(robotCount).Select(start => start.Value),
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
                   (symbol >= '1' && symbol <= '8') ||
                   symbol == '>' || symbol == '^' || symbol == '=' || symbol == '|' ||
                   symbol == '-' || symbol == 'I';
        }
    }

    public static class V2MapCatalog
    {
        public static AsciiMapV2 ConstrainedApartmentVariant(int seed)
        {
            const int width = 20, height = 11;
            var cells = new char[width, height];
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++) cells[x, y] = '#';

            for (int x = 0; x < width; x++)
            {
                for (int y = 2; y <= 8; y++) cells[x, y] = '.';
                cells[x, 7] = 'I';
            }
            for (int x = 0; x <= 4; x++)
                for (int y = 0; y <= 3; y++) cells[x, y] = '.';
            for (int x = 5; x < width; x++) cells[x, 2] = 'I';
            cells[0, 0] = '|';
            cells[3, 0] = '|';
            cells[0, 5] = '1';
            cells[3, 5] = '2';

            var random = new Random(seed);
            var topColumns = new HashSet<int>();
            while (topColumns.Count < 2) topColumns.Add(random.Next(5, 16));
            var bottomColumns = new HashSet<int>();
            while (bottomColumns.Count < 2)
            {
                int x = random.Next(5, 16);
                if (!topColumns.Contains(x)) bottomColumns.Add(x);
            }
            foreach (int x in topColumns) cells[x, 6] = '#';
            foreach (int x in bottomColumns) cells[x, 4] = '#';

            for (int x = 10; x < width; x++)
                for (int y = 4; y <= 6; y++)
                    if (cells[x, y] != '#') cells[x, y] = '!';

            int horizontalX = random.Next(12, 17);
            int verticalX;
            do verticalX = random.Next(17, 20);
            while (verticalX == horizontalX || verticalX == horizontalX + 1);
            cells[horizontalX, 5] = '>';
            cells[verticalX, 4] = '^';

            var rows = new string[height];
            for (int row = 0; row < height; row++)
            {
                int y = height - 1 - row;
                var chars = new char[width];
                for (int x = 0; x < width; x++) chars[x] = cells[x, y];
                rows[row] = new string(chars);
            }
            return new AsciiMapV2("apartment-constrained-seed-" + seed, rows);
        }

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
