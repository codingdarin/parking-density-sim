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
                case "v2test":
                    Environment.ExitCode = Tests.ModelV2Tests.RunAll() == 33 ? 0 : 1;
                    break;
                case "v2scale":
                    Scenarios.V2ScaleDemo.Run(lanes > 1 ? lanes : 4);
                    break;
                case "v2rolling":
                    Scenarios.V2ScaleDemo.RunRolling(lanes > 1 ? lanes : 5);
                    break;
                case "v2quality":
                    Scenarios.V2ScaleDemo.RunQualityGate();
                    break;
                case "v2pipeline":
                    Scenarios.V2ScaleDemo.RunPipelineQualityGate();
                    break;
                case "v2pdetail":
                    Scenarios.V2ScaleDemo.RunPipelineDetail(lanes > 1 ? lanes : 2);
                    break;
                case "v2pblock":
                    Scenarios.V2ScaleDemo.RunPipelineBlock();
                    break;
                case "v2papartment":
                    Scenarios.V2ScaleDemo.RunPipelineApartment();
                    break;
                case "v2pconstrained":
                    Scenarios.V2ScaleDemo.RunPipelineConstrainedApartment();
                    break;
                case "v2pseeds":
                    Scenarios.V2SeedSweepDemo.Run(20);
                    break;
                case "v2robots":
                    Scenarios.V2RobotSweepDemo.Run();
                    break;
                case "v2arobots":
                    Scenarios.V2ApartmentRobotSweepDemo.Run();
                    break;
                case "v2corridor":
                    Scenarios.V2CorridorSmokeDemo.Run();
                    break;
                case "v2caps":
                    Scenarios.V2CandidateSensitivityDemo.Run();
                    break;
                case "v2grid":
                    Scenarios.V2CorridorGridDemo.Run();
                    break;
                case "v2crossing":
                    Scenarios.V2SafetyCrossingDemo.Run();
                    break;
                case "v2orobots":
                    Scenarios.V2OperationalRobotSweepDemo.Run();
                    break;
                case "v2pockets":
                    Scenarios.V2PocketSweepDemo.Run();
                    break;
                case "v2pocketlayouts":
                    Scenarios.V2PocketLayoutSensitivityDemo.Run();
                    break;
                case "v2tradeoff":
                    Scenarios.V2TradeoffDemo.Run();
                    break;
                case "baseline":
                    Tests.AdversarialTests.RunNoCoordinationBaseline();
                    break;
                case "batch":
                {
                    string scenarioCsv = null;
                    foreach (var a in args)
                        if (a.EndsWith(".csv")) scenarioCsv = a;
                    Scenarios.BatchRunner.Run(scenarioCsv);
                    break;
                }
                case "d9":
                {
                    string slice = args.Length > 1 ? args[1] : "all";
                    Scenarios.D9SliceRunner.Run(slice);
                    break;
                }
                case "normal":
                {
                    int seed = 42, robots = 4;
                    foreach (var a in args)
                    {
                        if (a.StartsWith("--seed=") && int.TryParse(a.Substring(7), out int s)) seed = s;
                        if (a.StartsWith("--robots=") && int.TryParse(a.Substring(9), out int rc)) robots = rc;
                    }
                    Scenarios.NormalOpsDemo.Run(lanes, robots, seed, writeCsv: Array.IndexOf(args, "--csv") >= 0);
                    break;
                }
                case "emergency":
                {
                    double fire = 40;
                    foreach (var a in args)
                        if (a.StartsWith("--fire=") && double.TryParse(a.Substring(7), out double f)) fire = f;
                    EmergencyDemo.Run(lanes, fire, usePockets: Array.IndexOf(args, "--pockets") >= 0);
                    break;
                }
                default:
                    Console.WriteLine("사용법: [layout|carry|multi|test|sanity|v2test|v2scale|v2rolling|v2quality|v2pipeline|v2pdetail|v2pblock|v2papartment|v2pconstrained|v2pseeds|v2robots|v2arobots|v2corridor|v2caps|v2grid|v2crossing|v2orobots|v2pockets|v2pocketlayouts|v2tradeoff|baseline|emergency|batch|d9|normal] [숫자] [--all]");
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
