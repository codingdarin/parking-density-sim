using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ParkingSim.Core.Metrics;

namespace ParkingSim.Scenarios
{
    /// <summary>
    /// D9 본 배치 — 목적별 슬라이스(OFAT). 전면 격자(160셀) 대신 통제변수를 고정한 34셀로,
    /// 각 슬라이스가 발표 슬라이드 1장·변수 1개에 대응한다. 유휴율(가동률·유효p)은 전 슬라이스 공통 기록.
    /// - 포켓 효과: 레인2·로봇4 고정, d × 포켓 on/off (T∝d² ↔ T∝d 대조)  [10셀]
    /// - 로봇 포화: 레인3·d100·포켓off·대기소8칸 고정, 로봇 스윕 (증차 무릎)  [4셀]
    /// - 안전 도달 거리: 로봇4·포켓on 고정, 레인 × d 격자 (7분 교차점)      [20셀]
    /// 7분 교차점 근처 촘촘한 보강은 1차 결과 확인 후.
    /// </summary>
    public static class D9SliceRunner
    {
        private static readonly double[] Ds = { 20, 40, 60, 80, 100 }; // 20m 간격 5점
        private static readonly int[] RobotKnee = { 1, 2, 4, 8 };       // 로그 스케일 포화

        public static void RunAll()
        {
            RunPocket();
            RunRobot();
            RunReach();
        }

        public static void Run(string slice)
        {
            switch (slice)
            {
                case "pocket": RunPocket(); break;
                case "pcount": RunPocketCount(); break;
                case "dwell": RunDwell(); break;
                case "robot": RunRobot(); break;
                case "reach": RunReach(); break;
                default: RunAll(); break;
            }
        }

        // 포켓 개수 스윕 — 레인1·d100·로봇4 고정, 포켓 {1,2,4,8}개 균등 분산.
        // 물음 하나: 하차대기가 포켓 수에 따라 계속 주는가, 어디서 포화하는가(= 유예 창 G의 하한).
        private static void RunPocketCount()
        {
            var rows = new List<RunMetrics>();
            foreach (int n in new[] { 1, 2, 4, 8 })
                rows.Add(BatchRunner.Execute(lanes: 1, fire: 100, pocketXs: EvenPockets(n), seed: 0, robots: 4));
            Emit("포켓 개수 스윕 (레인1·d100·로봇4 고정)", "d9_pocketcount", rows);
        }

        // 유예 창 G 민감도 — 레인1·d100·로봇4·포켓4 고정, G {6,12,18,24}.
        // 물음 하나: 하차대기 하한이 G에 비례하는가 (= 포켓과 직교하는 병목이 G 때문인가).
        private static void RunDwell()
        {
            var pk = EvenPockets(4);
            Console.WriteLine("\n=== 유예 창 G 민감도 (레인1·d100·로봇4·포켓4 고정) ===");
            Console.WriteLine("  G | 확보(분) 판정 | 주행D 하차D 유휴 | 유효p");
            Console.WriteLine(new string('-', 56));
            Directory.CreateDirectory("output");
            var sb = new StringBuilder();
            sb.AppendLine("dwell_g," + CsvFormat.EmergencyHeader());
            foreach (int g in new[] { 6, 12, 18, 24 })
            {
                var m = BatchRunner.Execute(lanes: 1, fire: 100, pocketXs: pk, seed: 0, robots: 4, dwell: g);
                string v = !m.Success ? "실패" : (m.WithinBudget ? "✅" : "❌");
                Console.WriteLine(
                    $"{g,3} | {m.ClearMinutes,7:0.0} {v,-3} |{m.DriveWaitFrac,5:0.0%}{m.DropWaitFrac,5:0.0%}{m.IdleFrac,5:0.0%} |{m.EffectiveP,6:0.00}");
                sb.AppendLine(g + "," + CsvFormat.EmergencyRow(m));
            }
            File.WriteAllText(Path.Combine("output", "d9_dwell.csv"), sb.ToString());
            Console.WriteLine("→ output/d9_dwell.csv");
        }

        /// <summary>통로 [8,48) 40셀에 n개 포켓을 균등 분산 (레이아웃 기본값: corridorStart=8, length=40).</summary>
        private static int[] EvenPockets(int n)
        {
            const int start = 8, length = 40;
            var xs = new int[n];
            for (int i = 0; i < n; i++)
                xs[i] = start + (int)((i + 0.5) * length / n);
            return xs;
        }

        // 포켓 효과 — 레인2·로봇4 고정, d × 포켓{off,on}
        private static void RunPocket()
        {
            var rows = new List<RunMetrics>();
            foreach (double d in Ds)
                foreach (bool pk in new[] { false, true })
                    rows.Add(BatchRunner.Execute(lanes: 2, fire: d, pockets: pk, seed: 0, robots: 4));
            Emit("포켓 효과 (레인2·로봇4 고정)", "d9_pocket", rows);
        }

        // 로봇 포화 — 레인3·d100·포켓off 고정, 로봇 스윕 (대기소는 항상 8칸, DepotCells.Take로 순수 로봇 효과)
        private static void RunRobot()
        {
            var rows = new List<RunMetrics>();
            foreach (int r in RobotKnee)
                rows.Add(BatchRunner.Execute(lanes: 3, fire: 100, pockets: false, seed: 0, robots: r));
            Emit("로봇 포화 (레인3·d100·포켓off 고정)", "d9_robot", rows);
        }

        // 안전 도달 거리 — 로봇4·포켓on 고정, 레인 × d 격자
        private static void RunReach()
        {
            var rows = new List<RunMetrics>();
            for (int lanes = 0; lanes <= 3; lanes++)
                foreach (double d in Ds)
                    rows.Add(BatchRunner.Execute(lanes, d, pockets: true, seed: 0, robots: 4));
            Emit("안전 도달 거리 (로봇4·포켓on 고정, 레인×d)", "d9_reach", rows);
        }

        private static void Emit(string title, string name, List<RunMetrics> rows)
        {
            Directory.CreateDirectory("output");
            var sb = new StringBuilder();
            sb.AppendLine(CsvFormat.EmergencyHeader());
            foreach (var m in rows)
                sb.AppendLine(CsvFormat.EmergencyRow(m));
            string path = Path.Combine("output", name + ".csv");
            File.WriteAllText(path, sb.ToString());

            Console.WriteLine($"\n=== {title} ===");
            BatchRunner.PrintTable(rows);
            Console.WriteLine($"→ {path} ({rows.Count}행)");
        }
    }
}
