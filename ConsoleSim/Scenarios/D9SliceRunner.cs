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
                case "robot": RunRobot(); break;
                case "reach": RunReach(); break;
                default: RunAll(); break;
            }
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
