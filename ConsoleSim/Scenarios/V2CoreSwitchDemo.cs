using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ParkingSim.Core;
using ParkingSim.Core.Grid;
using ParkingSim.Core.V2;

namespace ParkingSim.Scenarios
{
    /// <summary>
    /// 핵심 스위치(기획정의서 §6) 최종 조립 — "몇 대를 더 받고, 몇 초를 잃는가"를
    /// 한 장으로 답한다. 레인×거리 실측 곡선에 봉투 이차 모델(계획서 §0-1)의
    /// p=4 상한 예측선을 병기해, 봉투로는 알 수 없는 간극(시뮬레이터의 존재 이유)을
    /// 정량화한다.
    /// </summary>
    public static class V2CoreSwitchDemo
    {
        private sealed class Row
        {
            public int Lanes;
            public int FireMeters;
            public int Vehicles;
            public bool Success;
            public int Ticks;
            public double Seconds;
            public double EnvelopeSeconds;
        }

        private sealed class PocketRow
        {
            public string Name;
            public int Ticks;
            public double Seconds;
            public bool Safe;
            public StagingLandAccountingResultV2 Accounting;
        }

        public static void Run()
        {
            Console.WriteLine("=== V2 핵심 스위치 조립 (α ↔ N 교환비) ===");
            var rows = new List<Row>();
            for (int lanes = 1; lanes <= 3; lanes++)
            {
                Console.Write("lane" + lanes + ": ");
                for (int fireMeters = 5; fireMeters <= 100; fireMeters += 5)
                {
                    EmergencyScenarioBuildResultV2 built =
                        CorridorScenarioFactoryV2.BuildEmergency(lanes, fireMeters);
                    PipelinedPlanResultV2 result = PipelinedPrioritizedPlannerV2.Solve(
                        built.Problem,
                        activeRobotCount: 4,
                        maxHighLevelCandidates: 8);
                    bool success = built.Success && result.Success && result.PhysicallyValid;
                    rows.Add(new Row
                    {
                        Lanes = lanes,
                        FireMeters = fireMeters,
                        Vehicles = built.SelectedVehicleCount,
                        Success = success,
                        Ticks = result.Ticks,
                        Seconds = result.Ticks * GridMap.SecondsPerCell,
                        EnvelopeSeconds = EnvelopeSecondsP4(lanes, fireMeters),
                    });
                    Console.Write(success ? "." : "X");
                }
                Console.WriteLine();
            }

            PocketRow baseline = SolvePocket("기준안(포켓0)", 0, 0);
            PocketRow robust = SolvePocket("강건안(포켓14·오프셋14 최악)", 14, 14);

            string curvePath = WriteCurveCsv(rows);
            string reportPath = WriteReport(rows, baseline, robust);
            Console.WriteLine("CSV: " + curvePath);
            Console.WriteLine("리포트: " + reportPath);
        }

        /// <summary>
        /// 봉투 이차 모델 (개발계획서 §0-1): N = 레인 × (d+β)/5m,
        /// T = N × ((d+β) + 2·d_s) ÷ p [초, 1m/s]. p=4 완전 병렬 상한이므로
        /// 시간의 하한 — 실측이 이보다 짧으면 모델 오류다.
        /// </summary>
        private static double EnvelopeSecondsP4(int lanes, int fireMeters)
        {
            const double betaMeters = 10.0;    // 부서공간 β = 4셀
            const double stagingMeters = 15.0; // 적치 거리 d_s
            const double parallelism = 4.0;    // p=4 상한
            double vehicles = lanes * (fireMeters + betaMeters) / 5.0;
            return vehicles * ((fireMeters + betaMeters) + 2.0 * stagingMeters) / parallelism;
        }

        private static PocketRow SolvePocket(string name, int pockets, int offset)
        {
            EmergencyScenarioBuildResultV2 built =
                CorridorScenarioFactoryV2.BuildEmergencyWithPockets(
                    100, pockets, pocketOffset: offset);
            PipelinedPlanResultV2 result = PipelinedPrioritizedPlannerV2.Solve(
                built.Problem,
                activeRobotCount: 4,
                maxHighLevelCandidates: 8);
            if (!built.Success || !result.Success || !result.PhysicallyValid)
                throw new InvalidOperationException(
                    name + " 계획 실패: " + (built.FailReason ?? result.FailReason));
            StagingLandProfileV2[] profiles = built.Problem.Slots
                .Where(slot => slot.Kind == SlotKind.Staging)
                .Select(slot => new StagingLandProfileV2(
                    slot.Id,
                    slot.Pose.Y == CorridorScenarioFactoryV2.CorridorBottomY + 3
                        ? StagingLandKindV2.ConvertedParkingSpace
                        : StagingLandKindV2.ExistingNonParkingPaved))
                .ToArray();
            StagingLandAccountingResultV2 accounting =
                CapacityTradeoffV2.EvaluateStagingLand(
                    built.Problem, result, grossAdditionalCars: 20, profiles);
            if (!accounting.NetAlphaClaimable)
                throw new InvalidOperationException(
                    name + " 토지 회계 실패: " + accounting.FailReason);
            return new PocketRow
            {
                Name = name,
                Ticks = result.Ticks,
                Seconds = result.Ticks * GridMap.SecondsPerCell,
                Safe = result.Ticks <= TimeBudget.BaselineTicks,
                Accounting = accounting,
            };
        }

        private static string WriteCurveCsv(List<Row> rows)
        {
            var csv = new List<string>
            {
                "lanes,fire_m,vehicles,success,ticks,seconds,envelope_p4_seconds," +
                "measured_over_envelope,within_5min,within_7min,within_9min",
            };
            foreach (Row row in rows)
            {
                double ratio = row.EnvelopeSeconds > 0
                    ? row.Seconds / row.EnvelopeSeconds
                    : 0.0;
                csv.Add(string.Join(",",
                    row.Lanes,
                    row.FireMeters,
                    row.Vehicles,
                    row.Success ? 1 : 0,
                    row.Ticks,
                    row.Seconds.ToString("F1", CultureInfo.InvariantCulture),
                    row.EnvelopeSeconds.ToString("F1", CultureInfo.InvariantCulture),
                    ratio.ToString("F3", CultureInfo.InvariantCulture),
                    Within(row, TimeBudget.FastArrivalSeconds),
                    Within(row, TimeBudget.BaselineSeconds),
                    Within(row, TimeBudget.SlowArrivalSeconds)));
            }
            string path = OutputDir.Resolve("v2_core_switch_curve.csv");
            File.WriteAllLines(path, csv);
            return path;
        }

        private static int Within(Row row, double budgetSeconds)
        {
            return row.Success && row.Seconds <= budgetSeconds ? 1 : 0;
        }

        private static string WriteReport(List<Row> rows, PocketRow baseline, PocketRow robust)
        {
            var report = new StringBuilder();
            report.AppendLine("# 핵심 스위치 — 몇 대를 더 받고, 몇 초를 잃는가");
            report.AppendLine();
            report.AppendLine(
                "기획정의서 §6 핵심 스위치의 실측 조립이다. 폭 3셀 × 100m 통로, " +
                "운송 유닛 4조, **미보정 기준선(이동 1틱=2.5초, pickup/drop 각 1틱)** — " +
                "현실 성능값이 아니며, Stanley 공개사양 보정은 `v2_report.md` 민감도를 참조한다.");
            report.AppendLine();
            report.AppendLine("## 표 1 — 교환비 (최악 = 최원단 100m 화재)");
            report.AppendLine();
            report.AppendLine("| 정책 | 수용 대수 (500면 기준) | 상시 통로 | 최악 확보 시간 | 7분 판정 |");
            report.AppendLine("|---|---:|---|---:|---|");
            report.AppendLine("| 현행 기준(상시 비움) | 500대 | 항상 개방 | 0초 | 통과 |");
            foreach (int lanes in new[] { 1, 2, 3 })
            {
                Row worst = rows.Single(r => r.Lanes == lanes && r.FireMeters == 100);
                report.AppendLine(
                    $"| {lanes}레인 점유 | {500 + lanes * 20}대 (+{lanes * 20}대, +{lanes * 4}%) | " +
                    $"비상시 복구 | {worst.Ticks}틱 / {worst.Seconds:F1}초 ({worst.Seconds / 60.0:F1}분) | " +
                    $"{Verdict(worst, TimeBudget.BaselineSeconds)} |");
            }
            report.AppendLine();
            report.AppendLine(
                "어떤 점유 수준도 최원단 화재의 7분 확보를 만족하지 못한다 — " +
                "점유의 성립 조건은 아래 표 3(분산 포켓 처방)이다.");
            report.AppendLine();
            report.AppendLine("## 표 2 — 안전 도달 거리: 실측 vs 봉투 상한");
            report.AppendLine();
            report.AppendLine(
                "봉투 이차 모델(계획서 §0-1, β=10m, d_s=15m)의 p=4 완전 병렬 상한과 " +
                "실측(운송 유닛 4조, 간섭 포함)의 간극. 이 간극이 봉투 계산으로 대체할 수 " +
                "없는 시뮬레이션 고유의 산출물이다.");
            report.AppendLine();
            report.AppendLine("| 점유 레인 | 예산 | 실측 안전 거리 | 봉투 p=4 상한 | 간극 |");
            report.AppendLine("|---:|---:|---:|---:|---:|");
            foreach (int lanes in new[] { 1, 2, 3 })
            {
                foreach (var budget in new[]
                         {
                             (Minutes: 5, Seconds: TimeBudget.FastArrivalSeconds),
                             (Minutes: 7, Seconds: TimeBudget.BaselineSeconds),
                             (Minutes: 9, Seconds: TimeBudget.SlowArrivalSeconds),
                         })
                {
                    int measured = Reach(rows, lanes, budget.Seconds, useEnvelope: false);
                    int envelope = Reach(rows, lanes, budget.Seconds, useEnvelope: true);
                    report.AppendLine(
                        $"| {lanes} | {budget.Minutes}분 | " +
                        $"{(budget.Minutes == 7 ? "**" : "")}{measured}m" +
                        $"{(budget.Minutes == 7 ? "**" : "")} | {envelope}m | " +
                        $"-{envelope - measured}m |");
                }
            }
            report.AppendLine();
            report.AppendLine("## 표 3 — 순이득 처방 (1레인·최원단 100m)");
            report.AppendLine();
            report.AppendLine(
                "| 안 | 기존 비주차 포장 | 주차면 전용 | net α | 확보 시간 | 7분 판정 |");
            report.AppendLine("|---|---:|---:|---:|---:|---|");
            foreach (PocketRow pocket in new[] { baseline, robust })
            {
                report.AppendLine(
                    $"| {pocket.Name} | {pocket.Accounting.ExistingNonParkingPavedSlots}면 | " +
                    $"{pocket.Accounting.ConvertedParkingSlots}면 | " +
                    $"+{pocket.Accounting.VerifiedNetAlpha}대 | " +
                    $"{pocket.Ticks}틱 / {pocket.Seconds:F1}초 | " +
                    $"{(pocket.Safe ? "통과" : "**실패**")} |");
            }
            report.AppendLine();
            report.AppendLine("## 헤드라인");
            report.AppendLine();
            Row lane1Worst = rows.Single(r => r.Lanes == 1 && r.FireMeters == 100);
            int lane1Measured = Reach(rows, 1, TimeBudget.BaselineSeconds, useEnvelope: false);
            report.AppendLine(
                $"> 통로 1레인 점유는 +20대(+4%)를 얻는 대신 최악 화재의 접근 복구에 " +
                $"{lane1Worst.Seconds / 60.0:F1}분이 걸려 7분을 넘긴다. 전 구간 7분 확보는 " +
                $"분산 포켓 처방(강건안)으로만 복원되며, 그 순이득은 포켓 부지의 토지 성격에 " +
                $"따라 +{robust.Accounting.VerifiedNetAlpha}대까지 줄어든다. 점유 없는 기준 " +
                $"레이아웃에서도 7분 안전 도달 거리는 봉투 상한보다 짧다 " +
                $"(1레인 {lane1Measured}m vs 봉투 " +
                $"{Reach(rows, 1, TimeBudget.BaselineSeconds, useEnvelope: true)}m) — " +
                $"간섭·병렬성 붕괴는 시뮬레이션으로만 정량화된다.");
            report.AppendLine();
            report.AppendLine("## 해석 제한");
            report.AppendLine();
            report.AppendLine(
                "- 미보정 기준선 결과다. Stanley 공개사양(취득90초·해제60초) 적용 시 " +
                "서비스 시간이 지배해 결론이 달라진다 (`v2_report.md` §공개사양 현실 시간 민감도).");
            report.AppendLine("- 봉투 상한은 계획서 §0-1의 이차 모델이며 V2 기하와 d_s 등이 정확히 일치하지 않는다 — 참조선이지 동일 조건 대조군이 아니다.");
            report.AppendLine("- 합성 통로 결과이며 실제 도면·연속 주행을 대체하지 않는다.");
            report.AppendLine("- 데이터: `v2_core_switch_curve.csv` (레인×5m 60점, 5/7/9분 판정과 실측/봉투 비율 포함).");

            string path = OutputDir.Resolve("v2_core_switch.md");
            File.WriteAllText(path, report.ToString());
            Console.WriteLine();
            Console.WriteLine(report.ToString());
            return path;
        }

        private static string Verdict(Row row, double budgetSeconds)
        {
            return row.Success && row.Seconds <= budgetSeconds ? "통과" : "**실패**";
        }

        private static int Reach(
            List<Row> rows, int lanes, double budgetSeconds, bool useEnvelope)
        {
            return rows
                .Where(r => r.Lanes == lanes &&
                            (useEnvelope
                                ? r.EnvelopeSeconds <= budgetSeconds
                                : r.Success && r.Seconds <= budgetSeconds))
                .Select(r => r.FireMeters)
                .DefaultIfEmpty(0)
                .Max();
        }
    }
}
