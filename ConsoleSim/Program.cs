using System;
using ParkingSim.Core.Grid;
using ParkingSim.Scenarios;

namespace ParkingSim
{
    internal static class Program
    {
        private static void Main(string[] args)
        {
            string command = args.Length > 0 ? args[0] : "carry";
            int lanes = ParseLanes(args, defaultValue: command == "layout" ? -1 : 1);

            switch (command)
            {
                case "layout":
                    if (lanes >= 0) PrintLayout(lanes);
                    else for (int n = 0; n <= 3; n++) PrintLayout(n);
                    break;
                case "carry":
                    CarryDemo.Run(lanes);
                    break;
                case "multi":
                    MultiRobotDemo.Run(printEveryTick: Array.IndexOf(args, "--all") >= 0);
                    break;
                case "test":
                    Environment.ExitCode = Tests.AdversarialTests.RunAll() == 5 ? 0 : 1;
                    break;
                case "sanity":
                    Environment.ExitCode = Tests.AdversarialTests.RunSanityCheck() ? 0 : 1;
                    break;
                case "baseline":
                    Tests.AdversarialTests.RunNoCoordinationBaseline();
                    break;
                case "emergency":
                {
                    double fire = 40;
                    foreach (var a in args)
                        if (a.StartsWith("--fire=") && double.TryParse(a.Substring(7), out double f)) fire = f;
                    EmergencyDemo.Run(lanes, fire, usePockets: Array.IndexOf(args, "--pockets") >= 0);
                    break;
                }
                default:
                    Console.WriteLine("사용법: [layout|carry|multi|test|sanity|baseline] [점유 레인 0~3] [--all]");
                    break;
            }
        }

        private static void PrintLayout(int occupiedLanes)
        {
            var lot = ParkingLayoutBuilder.Build(new LayoutConfig { OccupiedLanes = occupiedLanes });
            Console.WriteLine($"=== 통로 점유 {occupiedLanes}레인 ===");
            Console.WriteLine(TextRenderer.Render(lot));
            Console.WriteLine(TextRenderer.Legend);
            Console.WriteLine(
                $"수용: 주차면 {lot.StallCarCount}대 + 통로 {lot.CorridorCarCount}대(α) = {lot.Cars.Count}대" +
                $" | S_필요(최원단 화재) = {lot.CorridorCarCount}대");
            Console.WriteLine();
        }

        private static int ParseLanes(string[] args, int defaultValue)
        {
            for (int i = args.Length - 1; i >= 0; i--)
                if (int.TryParse(args[i], out int n))
                    return n;
            return defaultValue;
        }
    }
}
