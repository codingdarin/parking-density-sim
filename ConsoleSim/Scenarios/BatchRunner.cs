using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using ParkingSim.Core.Emergency;
using ParkingSim.Core.Grid;
using ParkingSim.Core.Metrics;

namespace ParkingSim.Scenarios
{
    /// <summary>
    /// D7: 비상 실행을 파라미터별로 돌려 RunMetrics를 수집하고 output/emergency_metrics.csv로 기록한다.
    /// - 인자 없음: 데모 스윕 (d∈{20,60,100} × 로봇∈{2,4}, 레인=1·포켓 on = 6점) — d축 선형/제곱 판별용.
    /// - 인자로 시나리오 CSV 경로: (lanes,fire,pockets,seed,robots) 행을 로드해 실행 (고정 시나리오 재현).
    /// 스윕 축·pockets-off 대비군 확장은 D9.
    /// </summary>
    public static class BatchRunner
    {
        private static readonly int[] DemoPockets = { 18, 28, 38 }; // 간격 25m

        private struct Scenario
        {
            public int Lanes;
            public double Fire;
            public bool Pockets;
            public int Seed;
            public int Robots;
        }

        public static void Run(string scenarioCsv)
        {
            var scenarios = scenarioCsv != null && File.Exists(scenarioCsv)
                ? LoadScenarios(scenarioCsv)
                : DemoScenarios();

            var rows = new List<RunMetrics>();
            foreach (var sc in scenarios)
                rows.Add(Execute(sc));

            Directory.CreateDirectory("output");
            var sb = new StringBuilder();
            sb.AppendLine(CsvFormat.EmergencyHeader());
            foreach (var m in rows)
                sb.AppendLine(CsvFormat.EmergencyRow(m));
            string path = Path.Combine("output", "emergency_metrics.csv");
            File.WriteAllText(path, sb.ToString());

            PrintTable(rows);
            Console.WriteLine($"\n→ {path} ({rows.Count}행)");
        }

        private static RunMetrics Execute(Scenario sc)
        {
            // ClearanceEvaluator가 grid를 변형하므로 실행마다 새 Build
            var lot = ParkingLayoutBuilder.Build(new LayoutConfig
            {
                OccupiedLanes = sc.Lanes,
                StagingPocketXs = sc.Pockets ? DemoPockets : new int[0],
            });
            var cfg = new EmergencyConfig { FireMeters = sc.Fire, RobotCount = sc.Robots };
            var plan = EmergencyPlanner.Plan(lot, cfg);
            var report = plan.Success ? ClearanceEvaluator.Evaluate(lot, plan) : null;
            return MetricsRecorder.FromEmergency(lot, cfg, plan, report, sc.Seed);
        }

        private static List<Scenario> DemoScenarios()
        {
            var list = new List<Scenario>();
            foreach (double d in new[] { 20.0, 60.0, 100.0 })
                foreach (int robots in new[] { 2, 4 })
                    list.Add(new Scenario { Lanes = 1, Fire = d, Pockets = true, Seed = 0, Robots = robots });
            return list;
        }

        private static List<Scenario> LoadScenarios(string path)
        {
            var list = new List<Scenario>();
            var inv = CultureInfo.InvariantCulture;
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("lanes")) continue;
                var f = line.Split(',');
                if (f.Length < 5) continue;
                list.Add(new Scenario
                {
                    Lanes = int.Parse(f[0], inv),
                    Fire = double.Parse(f[1], inv),
                    Pockets = f[2].Trim() == "1" || f[2].Trim().ToLowerInvariant() == "on",
                    Seed = int.Parse(f[3], inv),
                    Robots = int.Parse(f[4], inv),
                });
            }
            return list;
        }

        private static void PrintTable(List<RunMetrics> rows)
        {
            Console.WriteLine(
                "레인  화재   포켓  로봇 | 확보(분)  판정  S필요  재시도 | 유효p  가동률 | 봉투(분)  이탈배율");
            Console.WriteLine(new string('-', 88));
            foreach (var m in rows)
            {
                string verdict = !m.Success ? "실패" : (m.WithinBudget ? "✅" : "❌");
                string clear = m.Success ? $"{m.ClearMinutes,7:0.0}" : "   —  ";
                Console.WriteLine(
                    $"{m.OccupiedLanes,3}  {m.FireMeters,5:0}m  {(m.PocketCount > 0 ? "有" : "無"),3}  {m.RobotCount,3}대 |" +
                    $"{clear}  {verdict,-4} {m.SectionCarCount,4}대 {m.Attempts,5}회 |" +
                    $"{m.EffectiveP,6:0.00} {m.Utilization,6:0.0%} |{m.EnvelopeSeconds / 60,7:0.0}  {m.DeviationRatio,6:0.00}x");
            }
        }
    }
}
