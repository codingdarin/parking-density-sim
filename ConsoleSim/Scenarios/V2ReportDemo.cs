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

        private sealed class PocketMeasurement
        {
            public PipelinedPlanResultV2 Plan;
            public StagingLandAccountingResultV2 Accounting;
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

            PocketMeasurement baseline = SolvePocket(pockets: 0, offset: 0);
            PocketMeasurement robust = SolvePocket(pockets: 14, offset: 14);
            StagingLandAccountingResultV2[] surfaceAccounting =
                SolveSurfaceLandAccounting();
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
            report.AppendLine(
                "| 안 | 기존 비주차 포장 | 주차면 전용 | 상시 전용/사건 사용 | net α | 확보 시간 | 7분 판정 |");
            report.AppendLine("|---|---:|---:|---:|---:|---:|---|");
            report.AppendLine(
                $"| 기준안 | {baseline.Accounting.ExistingNonParkingPavedSlots}면 | " +
                $"{baseline.Accounting.ConvertedParkingSlots}면 | " +
                $"{baseline.Accounting.DedicatedStagingSlots}/" +
                $"{baseline.Accounting.UsedStagingSlots}면 | " +
                $"+{baseline.Accounting.VerifiedNetAlpha}대 | " +
                $"{baseline.Plan.Ticks}틱 / {baseline.Plan.Ticks * 2.5:F1}초 | 실패 |");
            report.AppendLine(
                $"| 강건안(오프셋14 최악) | " +
                $"{robust.Accounting.ExistingNonParkingPavedSlots}면 | " +
                $"{robust.Accounting.ConvertedParkingSlots}면 | " +
                $"{robust.Accounting.DedicatedStagingSlots}/" +
                $"{robust.Accounting.UsedStagingSlots}면 | " +
                $"+{robust.Accounting.VerifiedNetAlpha}대 | " +
                $"{robust.Plan.Ticks}틱 / {robust.Plan.Ticks * 2.5:F1}초 | 통과 |");
            report.AppendLine();
            report.AppendLine(
                "순α는 사건 때 사용한 면수가 아니라 설계상 상시 전용한 전체 적치면의 " +
                "주차 기회비용을 차감한다.");
            report.AppendLine();
            report.AppendLine("## 지상형 자동 핵심경로 토지 성격 민감도");
            report.AppendLine();
            report.AppendLine(
                "자동 선택은 하부 3대/36틱이며 적치5면 중 사건 사용은3면이다.");
            report.AppendLine();
            report.AppendLine("| 적치5면 성격 | 주차 기회비용 | net α | 회계 확정 |");
            report.AppendLine("|---|---:|---:|---|");
            report.AppendLine(
                $"| 전부 주차 가능 부지 | 5면 | " +
                $"{FormatNet(surfaceAccounting[0])} | " +
                $"{ClaimLabel(surfaceAccounting[0])} |");
            report.AppendLine(
                $"| 3면 비주차 포장 + 2면 주차 가능 부지 | 2면 | " +
                $"{FormatNet(surfaceAccounting[1])} | " +
                $"{ClaimLabel(surfaceAccounting[1])} |");
            report.AppendLine(
                $"| 전부 기존 비주차 포장 | 0면 | " +
                $"{FormatNet(surfaceAccounting[2])} | " +
                $"{ClaimLabel(surfaceAccounting[2])} |");
            report.AppendLine(
                "| 1면 이상 미확인 | — | 확정 불가 | **불가** |");
            report.AppendLine();
            report.AppendLine("## 해석 제한");
            report.AppendLine();
            report.AppendLine("- 합성 격자 결과이며 실제 아파트 도면·연속 회전 swept volume을 대체하지 않는다.");
            report.AppendLine("- 절대시간은 로봇 속도 가정에 종속되고, 계획값은 후보 8개의 물리 유효 상한이다.");
            report.AppendLine("- 포켓14는 순환 오프셋20종에 대한 강건값이며 모든 실제 배치의 보편 상수가 아니다.");
            report.AppendLine("- 기존 비주차 포장 여부는 실제 지적·도면·운영 용도 조사로 확인해야 하며, 미확인 부지는 순이득에서 0비용으로 간주하지 않는다.");

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

        private static PocketMeasurement SolvePocket(int pockets, int offset)
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
                    "포켓 토지 회계 실패: " + accounting.FailReason);
            return new PocketMeasurement
            {
                Plan = result,
                Accounting = accounting,
            };
        }

        private static StagingLandAccountingResultV2[] SolveSurfaceLandAccounting()
        {
            SurfaceApartmentScenarioV2 surface = SurfaceApartmentScenarioFactoryV2.Build();
            AutomaticEmergencyAccessPlanResultV2 automatic =
                EmergencyAccessRouteGeneratorV2.Solve(
                    surface.BaseProblem, (1, 5), (22, 5), activeRobotCount: 4);
            if (!automatic.Success || automatic.Plan.Selected.Plan.Ticks != 36)
                throw new InvalidOperationException(
                    "지상형 자동경로 회계 기준 실패: " + automatic.FailReason);
            EmergencyProblemV2 problem = automatic.Plan.Selected.Scenario.Problem;
            PipelinedPlanResultV2 plan = automatic.Plan.Selected.Plan;
            ParkingSlotV2[] staging = problem.Slots
                .Where(slot => slot.Kind == SlotKind.Staging).ToArray();
            return new[]
            {
                CapacityTradeoffV2.EvaluateStagingLand(
                    problem,
                    plan,
                    5,
                    staging.Select(slot => new StagingLandProfileV2(
                        slot.Id, StagingLandKindV2.ConvertedParkingSpace))),
                CapacityTradeoffV2.EvaluateStagingLand(
                    problem,
                    plan,
                    5,
                    staging.Select((slot, index) => new StagingLandProfileV2(
                        slot.Id,
                        index < 2
                            ? StagingLandKindV2.ConvertedParkingSpace
                            : StagingLandKindV2.ExistingNonParkingPaved))),
                CapacityTradeoffV2.EvaluateStagingLand(
                    problem,
                    plan,
                    5,
                    staging.Select(slot => new StagingLandProfileV2(
                        slot.Id, StagingLandKindV2.ExistingNonParkingPaved))),
            };
        }

        private static string FormatNet(StagingLandAccountingResultV2 accounting)
        {
            return accounting.VerifiedNetAlpha.HasValue
                ? "+" + accounting.VerifiedNetAlpha.Value + "대"
                : "확정 불가";
        }

        private static string ClaimLabel(StagingLandAccountingResultV2 accounting)
        {
            return accounting.NetAlphaClaimable ? "확정" : "**불가**";
        }
    }
}
