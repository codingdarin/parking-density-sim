using System;
using System.IO;
using System.Text;
using ParkingSim.Core.Metrics;

namespace ParkingSim.Scenarios
{
    /// <summary>D7: 평시 출차 시뮬(최소) 실행·출력. 지표 = 평균 출차 시간·가동률·처리율.</summary>
    public static class NormalOpsDemo
    {
        public static void Run(int occupiedLanes, int robotCount, int seed, bool writeCsv)
        {
            // 통로 차량 절반가량을 도착창 안에 요청 (최소 데모)
            int requestCount = 8;
            int arrivalWindow = 40; // 40틱 = 100초 동안 요청 도착

            var m = NormalOpsSim.Run(occupiedLanes, robotCount, seed, requestCount, arrivalWindow);

            Console.WriteLine(
                $"=== 평시 출차: 점유 {m.OccupiedLanes}레인, 로봇 {m.RobotCount}대, 시드 {m.Seed} ===");
            Console.WriteLine(
                $"요청 {m.Requested}대 → 완료 {m.Completed} / 실패 {m.Failed}");
            Console.WriteLine(
                $"평균 출차: {m.AvgExitTicks:0.0}틱 ≈ {m.AvgExitSeconds:0}초 ({m.AvgExitSeconds / 60:0.0}분)" +
                $" | 최대 {m.MaxExitTicks}틱");
            Console.WriteLine(
                $"makespan {m.MakespanTicks}틱 | 가동률 {m.Utilization:0.0%} | 처리율 {m.ThroughputPerMin:0.0}대/분");

            if (writeCsv)
            {
                Directory.CreateDirectory("output");
                var sb = new StringBuilder();
                sb.AppendLine(CsvFormat.NormalHeader());
                sb.AppendLine(CsvFormat.NormalRow(m));
                string path = Path.Combine("output", "normal_metrics.csv");
                File.WriteAllText(path, sb.ToString());
                Console.WriteLine($"→ {path}");
            }
        }
    }
}
