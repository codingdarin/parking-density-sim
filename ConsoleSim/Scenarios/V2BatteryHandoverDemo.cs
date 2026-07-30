using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ParkingSim.Core;
using ParkingSim.Core.V2;

namespace ParkingSim.Scenarios
{
    /// <summary>
    /// 8동 단지 N=15에서 배터리·핸드오버를 실측한다.
    /// ① 충전 정책 표 — 가용 유닛 수별 8동 전수 최악 개통시간(동시 충전 = 상시 가용 감소)
    /// ② 103동(유닛 민감 최악동) 핸드오버 — t=0 출동 불능형과 미션 경계 퇴역형을
    ///    교체 유닛 유무별로 잰다. 프로파일은 Stanley 1m/s, 만충은 합성 기준값.
    /// </summary>
    public static class V2BatteryHandoverDemo
    {
        private const int HeadlineVehicleCount = 15;
        private const int FireBuildingId = 103;
        /// <summary>만충 = 미보정 4시간 연속 임무(5,760틱), 예비 = 10분(240틱). 합성 기준값</summary>
        private const int CapacityTicks = 5760;
        private const int ReserveTicks = 240;
        private static readonly (int X, int Y) ChargeStation = (0, 0);

        public static void Run()
        {
            PhysicalTimeProfileV2 profile = PublishedParkingRobotTimingV2.Create(1.0);
            var options = new EmergencyAccessRouteGenerationOptionsV2
            {
                MaxRoutes = 4,
                MaxCenterlineAttempts = 16,
                MaxSearchExpansions = 100000,
            };
            ApartmentComplexScenarioV2 scenario =
                ApartmentComplexScenarioFactoryV2.BuildDensity(
                    HeadlineVehicleCount, profile.CreateOperationTiming());
            var battery = new BatteryModelV2(CapacityTicks, ReserveTicks);
            Console.WriteLine(
                $"=== 배터리·핸드오버 실측: N={HeadlineVehicleCount}, " +
                $"만충 {CapacityTicks}틱·예비 {ReserveTicks}틱 ===");

            WritePolicyTable(scenario, options, profile);
            var rows = new List<string>
            {
                "scenario,units,handover,retired_unit,sync_tick,delivered," +
                "total_ticks,total_seconds,delay_seconds,within_7min,residual_units",
            };

            // ── 103동 4조 자동 계획 (서문, 이동 4대) ──
            ApartmentComplexPlanResultV2 quad = Solve(scenario, options, 4);
            if (!quad.Success)
                throw new InvalidOperationException("4조 기준선 실패: " + quad.FailReason);
            EmergencyProblemV2 quadProblem =
                quad.Selected.AutomaticPlan.Plan.Selected.Scenario.Problem;
            PipelinedPlanResultV2 quadPlan =
                quad.Selected.AutomaticPlan.Plan.Selected.Plan;

            Report(rows, profile, "baseline-4units", 4,
                BatteryHandoverV2.Evaluate(
                    quadProblem, quadPlan, battery, FullCharges(quadPlan)),
                quadPlan);

            // t=0 출동 불능: 유닛 하나가 첫 미션조차 받지 못하는 전량
            int t0Robot = Enumerable.Range(0, quadPlan.RobotTimelines.Length)
                .First(robot => quadPlan.Missions.Any(m => m.RobotIndex == robot));
            int[] t0Charges = FullCharges(quadPlan);
            t0Charges[t0Robot] = BatteryHandoverV2.MissionCost(
                FirstMission(quadPlan, t0Robot)) + ReserveTicks - 1;
            Report(rows, profile, "t0-depleted-with-replacement", 4,
                BatteryHandoverV2.Evaluate(
                    quadProblem, quadPlan, battery, t0Charges,
                    replacementStart: ChargeStation),
                quadPlan);
            Report(rows, profile, "t0-depleted-no-replacement", 4,
                BatteryHandoverV2.Evaluate(quadProblem, quadPlan, battery, t0Charges),
                quadPlan);

            // ── 103동 2조 서문 계획 (유닛당 2미션) — 미션 경계 퇴역 ──
            ApartmentComplexPlanResultV2 duo = Solve(scenario, options, 2);
            ApartmentComplexAccessAttemptV2 west = duo.Attempts
                .FirstOrDefault(attempt =>
                    attempt.Entrance.Name == "west-primary" && attempt.Success);
            if (west == null)
            {
                Console.WriteLine("2조 서문 계획이 없어 미션 경계 시나리오 생략");
            }
            else
            {
                EmergencyProblemV2 duoProblem = west.AutomaticPlan.Plan.Selected
                    .Scenario.Problem;
                PipelinedPlanResultV2 duoPlan = west.AutomaticPlan.Plan.Selected.Plan;
                int midRobot = Enumerable.Range(0, duoPlan.RobotTimelines.Length)
                    .Where(robot =>
                        duoPlan.Missions.Count(m => m.RobotIndex == robot) >= 2)
                    .DefaultIfEmpty(-1)
                    .First();
                if (midRobot < 0)
                {
                    Console.WriteLine("2조 서문 계획에 2미션 유닛이 없어 생략");
                }
                else
                {
                    Report(rows, profile, "baseline-2units-west", 2,
                        BatteryHandoverV2.Evaluate(
                            duoProblem, duoPlan, battery, FullCharges(duoPlan)),
                        duoPlan);
                    int[] midCharges = FullCharges(duoPlan);
                    midCharges[midRobot] = BatteryHandoverV2.MissionCost(
                        FirstMission(duoPlan, midRobot)) + ReserveTicks;
                    Report(rows, profile, "midplan-retire-with-replacement", 2,
                        BatteryHandoverV2.Evaluate(
                            duoProblem, duoPlan, battery, midCharges,
                            replacementStart: ChargeStation),
                        duoPlan);
                    Report(rows, profile, "midplan-retire-no-replacement", 2,
                        BatteryHandoverV2.Evaluate(
                            duoProblem, duoPlan, battery, midCharges),
                        duoPlan);
                }
            }

            string handoverPath = OutputDir.Resolve("v2_battery_handover.csv");
            File.WriteAllLines(handoverPath, rows);
            Console.WriteLine("CSV: " + handoverPath);
        }

        private static void WritePolicyTable(
            ApartmentComplexScenarioV2 scenario,
            EmergencyAccessRouteGenerationOptionsV2 options,
            PhysicalTimeProfileV2 profile)
        {
            var csv = new List<string>
            {
                "available_units,charging_units,all_within_7min,worst_building," +
                "worst_seconds,max_unit_consumed_ticks,consumed_vs_capacity_percent",
            };
            Console.WriteLine(
                "\n[충전 정책 — 8동 전수] 가용 | 충전 | 8동 7분 | 최악동 | 최악시간 | 유닛 최대 소모");
            for (int units = 4; units >= 1; units--)
            {
                var session = new ApartmentComplexPlanningSessionV2(
                    scenario,
                    activeRobotCount: units,
                    generationOptions: options,
                    maxTick: 5000,
                    enableLowerBoundPruning: true);
                var trials = new List<ApartmentComplexDensityTrialV2>();
                foreach (ApartmentBuildingV2 building in scenario.Buildings)
                    trials.Add(ApartmentComplexDensitySweepV2.Evaluate(
                        scenario, session, building.Id,
                        includeSecondaryEntrances: true, timeProfile: profile));
                bool allWithin = trials.All(trial =>
                    trial.PlanSuccess && trial.WithinBudget);
                ApartmentComplexDensityTrialV2 worst = trials
                    .Where(trial => trial.PlanSuccess)
                    .OrderByDescending(trial => trial.Seconds)
                    .ThenBy(trial => trial.BuildingId)
                    .First();
                int maxConsumed = trials.Max(trial => trial.Ticks);
                Console.WriteLine(
                    $"{units}조 | {4 - units}대 | {(allWithin ? "통과" : "실패")} | " +
                    $"{worst.BuildingId}동 | {worst.Seconds,7:0.0}초 | " +
                    $"≤{maxConsumed}틱 (만충의 {100.0 * maxConsumed / CapacityTicks:0.0}%)");
                csv.Add(string.Join(",",
                    units,
                    4 - units,
                    allWithin ? 1 : 0,
                    worst.BuildingId,
                    worst.Seconds.ToString("F1", CultureInfo.InvariantCulture),
                    maxConsumed,
                    (100.0 * maxConsumed / CapacityTicks)
                        .ToString("F1", CultureInfo.InvariantCulture)));
            }
            string path = OutputDir.Resolve("v2_battery_policy.csv");
            File.WriteAllLines(path, csv);
            Console.WriteLine("CSV: " + path);
        }

        private static ApartmentComplexPlanResultV2 Solve(
            ApartmentComplexScenarioV2 scenario,
            EmergencyAccessRouteGenerationOptionsV2 options,
            int units)
        {
            var session = new ApartmentComplexPlanningSessionV2(
                scenario,
                activeRobotCount: units,
                generationOptions: options,
                maxTick: 5000,
                enableLowerBoundPruning: true);
            return session.Solve(
                new ApartmentFireIncidentV2(FireBuildingId),
                includeSecondaryEntrances: true);
        }

        private static int[] FullCharges(PipelinedPlanResultV2 plan)
        {
            return Enumerable.Repeat(CapacityTicks, plan.RobotTimelines.Length)
                .ToArray();
        }

        private static PipelinedMissionV2 FirstMission(
            PipelinedPlanResultV2 plan, int robot)
        {
            return plan.Missions
                .Where(m => m.RobotIndex == robot)
                .OrderBy(m => m.StartTick)
                .First();
        }

        private static void Report(
            List<string> rows,
            PhysicalTimeProfileV2 profile,
            string name,
            int units,
            BatteryHandoverResultV2 result,
            PipelinedPlanResultV2 basePlan)
        {
            if (!result.Success)
            {
                Console.WriteLine($"\n[{name}] 실패 — {result.FailReason}");
                rows.Add($"{name},{units},{(result.HandoverOccurred ? 1 : 0)},,,,,,,0,");
                return;
            }
            double seconds = profile.PlanSeconds(result.TotalTicks);
            double delay = profile.PlanSeconds(Math.Abs(result.DelayTicks)) *
                Math.Sign(result.DelayTicks);
            bool within = seconds <= TimeBudget.BaselineSeconds;
            int residualUnits = result.ResidualProblem == null
                ? basePlan.RobotTimelines.Length
                : result.ResidualProblem.RobotStarts.Count;
            Console.WriteLine(
                $"\n[{name}] 핸드오버 {(result.HandoverOccurred ? "발동" : "없음")}" +
                (result.HandoverOccurred
                    ? $" | 퇴역 유닛{result.RetiredRobot} " +
                      $"(결정 {result.RetireDecisionTick}틱) | 동기 {result.SyncTick}틱 | " +
                      $"인도 {result.DeliveredVehicles.Count}대 | 잔여 유닛 {residualUnits}조"
                    : "") +
                $"\n  총 {result.TotalTicks}틱 / {seconds:0.0}초 | " +
                $"지연 {delay:+0.0;-0.0;+0.0}초 | 7분 {(within ? "통과" : "실패")}");
            rows.Add(string.Join(",",
                name,
                units,
                result.HandoverOccurred ? 1 : 0,
                result.HandoverOccurred ? result.RetiredRobot.ToString() : "",
                result.SyncTick,
                result.DeliveredVehicles.Count,
                result.TotalTicks,
                seconds.ToString("F1", CultureInfo.InvariantCulture),
                delay.ToString("F1", CultureInfo.InvariantCulture),
                within ? 1 : 0,
                residualUnits));
        }
    }
}
