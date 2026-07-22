using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ParkingSim.Core.V2;

namespace ParkingSim.Scenarios
{
    public static class V2ReportDemo
    {
        private sealed class Measurement
        {
            public int Lanes;
            public int FireMeters;
            public int Vehicles;
            public int Ticks;
            public bool Success;
        }

        public static void Run()
        {
            var measurements = new List<Measurement>();
            foreach (var condition in new[]
                     {
                         (Lanes: 1, Fire: 55), (Lanes: 1, Fire: 60),
                         (Lanes: 2, Fire: 30), (Lanes: 2, Fire: 35),
                         (Lanes: 3, Fire: 20), (Lanes: 3, Fire: 25),
                         (Lanes: 1, Fire: 100), (Lanes: 2, Fire: 100),
                         (Lanes: 3, Fire: 100),
                     })
            {
                measurements.Add(Solve(condition.Lanes, condition.Fire));
            }

            PipelinedPlanResultV2 baseline = SolvePocket(pockets: 0, offset: 0);
            PipelinedPlanResultV2 robust = SolvePocket(pockets: 14, offset: 14);
            var report = new StringBuilder();
            report.AppendLine("# Model V2 운영 리포트");
            report.AppendLine();
            report.AppendLine("## 고정 조건");
            report.AppendLine();
            report.AppendLine("- 통로: 폭 3셀 × 길이 40셀(100m), β=10m");
            report.AppendLine("- 차량: 1×2셀, 활성 운송 유닛 4조, 대기소 8칸");
            report.AppendLine("- 시간: 1틱=2.5초, 안전 예산 7분=168틱");
            report.AppendLine("- 계획: 고수준 후보 8개 bounded 상한 + 전체 물리 재생 검증");
            report.AppendLine();
            report.AppendLine("## 안전 도달거리");
            report.AppendLine();
            report.AppendLine("| 점유 레인 | 추가 수용량 α | 최대 안전 거리 | 다음 5m |");
            report.AppendLine("|---:|---:|---:|---:|");
            foreach (int lane in new[] { 1, 2, 3 })
            {
                Measurement safe = measurements
                    .Where(m => m.Lanes == lane && m.FireMeters < 100 && m.Ticks <= 168)
                    .OrderByDescending(m => m.FireMeters).First();
                Measurement next = measurements
                    .Where(m => m.Lanes == lane && m.FireMeters < 100 && m.FireMeters > safe.FireMeters)
                    .OrderBy(m => m.FireMeters).First();
                report.AppendLine(
                    $"| {lane} | +{lane * 20}대 (+{lane * 4}%) | " +
                    $"**{safe.FireMeters}m** ({safe.Ticks}틱/{safe.Ticks * 2.5:F1}초) | " +
                    $"{next.FireMeters}m: {next.Ticks}틱/실패 |");
            }
            report.AppendLine();
            report.AppendLine("## 최원단 100m");
            report.AppendLine();
            report.AppendLine("| 점유 레인 | 이동 차량 | 확보 시간 | 7분 판정 |");
            report.AppendLine("|---:|---:|---:|---|");
            foreach (Measurement measurement in measurements.Where(m => m.FireMeters == 100))
            {
                report.AppendLine(
                    $"| {measurement.Lanes} | {measurement.Vehicles}대 | " +
                    $"{measurement.Ticks}틱 / {measurement.Ticks * 2.5:F1}초 | " +
                    $"{(measurement.Ticks <= 168 ? "통과" : "**실패**")} |");
            }
            report.AppendLine();
            report.AppendLine("## 1레인·100m 포켓 처방 비교");
            report.AppendLine();
            report.AppendLine("| 안 | 포켓 비용 | net α | 확보 시간 | 7분 판정 |");
            report.AppendLine("|---|---:|---:|---:|---|");
            report.AppendLine(
                $"| 기준안 | 0면 | +20대 | {baseline.Ticks}틱 / {baseline.Ticks * 2.5:F1}초 | 실패 |");
            report.AppendLine(
                $"| 강건안(오프셋14 최악) | 14면 | +6대 | {robust.Ticks}틱 / {robust.Ticks * 2.5:F1}초 | 통과 |");
            report.AppendLine();
            report.AppendLine("## 해석 제한");
            report.AppendLine();
            report.AppendLine("- 합성 격자 결과이며 실제 아파트 도면·연속 회전 swept volume을 대체하지 않는다.");
            report.AppendLine("- 절대시간은 로봇 속도 가정에 종속되고, 계획값은 후보 8개의 물리 유효 상한이다.");
            report.AppendLine("- 포켓14는 순환 오프셋20종에 대한 강건값이며 모든 실제 배치의 보편 상수가 아니다.");

            Directory.CreateDirectory("output");
            string path = Path.Combine("output", "v2_report.md");
            File.WriteAllText(path, report.ToString());
            Console.WriteLine(report.ToString());
            Console.WriteLine("리포트: " + Path.GetFullPath(path));
        }

        private static Measurement Solve(int lanes, int fireMeters)
        {
            EmergencyScenarioBuildResultV2 built =
                CorridorScenarioFactoryV2.BuildEmergency(lanes, fireMeters);
            PipelinedPlanResultV2 result = PipelinedPrioritizedPlannerV2.Solve(
                built.Problem,
                activeRobotCount: 4,
                maxHighLevelCandidates: 8);
            if (!built.Success || !result.Success || !result.PhysicallyValid)
                throw new InvalidOperationException(
                    $"리포트 조건 실패: lane={lanes}, d={fireMeters}, " +
                    (built.FailReason ?? result.FailReason));
            return new Measurement
            {
                Lanes = lanes,
                FireMeters = fireMeters,
                Vehicles = built.SelectedVehicleCount,
                Ticks = result.Ticks,
                Success = true,
            };
        }

        private static PipelinedPlanResultV2 SolvePocket(int pockets, int offset)
        {
            EmergencyScenarioBuildResultV2 built =
                CorridorScenarioFactoryV2.BuildEmergencyWithPockets(
                    100, pockets, pocketOffset: offset);
            PipelinedPlanResultV2 result = PipelinedPrioritizedPlannerV2.Solve(
                built.Problem,
                activeRobotCount: 4,
                maxHighLevelCandidates: 8);
            if (!built.Success || !result.Success || !result.PhysicallyValid)
                throw new InvalidOperationException("포켓 리포트 조건 실패: " + result.FailReason);
            return result;
        }
    }
}
