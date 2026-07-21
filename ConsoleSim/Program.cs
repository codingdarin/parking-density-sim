using System;
using ParkingSim.Core.Grid;

namespace ParkingSim
{
    internal static class Program
    {
        private static void Main(string[] args)
        {
            int? lanes = ParseLanes(args);
            if (lanes.HasValue)
            {
                PrintLayout(lanes.Value);
            }
            else
            {
                for (int n = 0; n <= 3; n++)
                    PrintLayout(n);
            }
        }

        private static void PrintLayout(int occupiedLanes)
        {
            var config = new LayoutConfig { OccupiedLanes = occupiedLanes };
            var lot = ParkingLayoutBuilder.Build(config);

            Console.WriteLine($"=== 통로 점유 {occupiedLanes}레인 ===");
            Console.WriteLine(TextRenderer.Render(lot));
            Console.WriteLine(
                $"수용: 주차면 {lot.StallCarCount}대 + 통로 {lot.CorridorCarCount}대(α) = {lot.Cars.Count}대" +
                $" | S_필요(최원단 화재) = {lot.CorridorCarCount}대");
            Console.WriteLine();
        }

        /// <summary>인자: 없음(0~3 전부) 또는 "--lanes N" / "N"</summary>
        private static int? ParseLanes(string[] args)
        {
            if (args.Length == 0) return null;
            string value = args[args.Length - 1];
            if (int.TryParse(value, out int n)) return n;
            Console.WriteLine($"알 수 없는 인자: {string.Join(" ", args)} — 사용법: [--lanes] <0~3>");
            return null;
        }
    }
}
