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
    /// 대표 시나리오의 로봇-틱 4분해·유효 병렬성 계측 — S4b의 산술 추정(서비스
    /// 하한 기반 ≈1.8조)을 직접 계측으로 대체한다. 프로파일은 Stanley 1m/s.
    /// </summary>
    public static class V2PlanUtilizationDemo
    {
        public static void Run()
        {
            PhysicalTimeProfileV2 profile = PublishedParkingRobotTimingV2.Create(1.0);
            var options = new EmergencyAccessRouteGenerationOptionsV2
            {
                MaxRoutes = 4,
                MaxCenterlineAttempts = 16,
                MaxSearchExpansions = 100000,
            };
            var rows = new List<string>
            {
                "case,robot,move_ticks,service_ticks,wait_ticks,idle_ticks," +
                "makespan_ticks,effective_parallelism",
            };
            Console.WriteLine("=== 로봇-틱 4분해·유효 병렬성 계측 ===");

            Measure(rows, profile,
                "sitea-alley-n0-b2",
                SiteABlockScenarioFactoryV2.BuildDensity(
                    0, profile.CreateOperationTiming()),
                buildingId: 2, options);
            Measure(rows, profile,
                "complex-n15-b103",
                ApartmentComplexScenarioFactoryV2.BuildDensity(
                    15, profile.CreateOperationTiming()),
                buildingId: 103, options);
            Measure(rows, profile,
                "sitea-arterial-n12-b4",
                SiteABlockScenarioFactoryV2.BuildDensity(
                    12,
                    profile.CreateOperationTiming(),
                    SiteStagingLayoutV2.SouthWestOnly,
                    SiteZonePlacementV2.ArterialFrontage),
                buildingId: 4, options);

            string path = OutputDir.Resolve("v2_plan_utilization.csv");
            File.WriteAllLines(path, rows);
            Console.WriteLine("\nCSV: " + path);
        }

        private static void Measure(
            List<string> rows,
            PhysicalTimeProfileV2 profile,
            string name,
            ApartmentComplexScenarioV2 scenario,
            int buildingId,
            EmergencyAccessRouteGenerationOptionsV2 options)
        {
            var session = new ApartmentComplexPlanningSessionV2(
                scenario,
                activeRobotCount: 4,
                generationOptions: options,
                maxTick: 5000,
                enableLowerBoundPruning: true);
            ApartmentComplexPlanResultV2 solved = session.Solve(
                new ApartmentFireIncidentV2(buildingId),
                includeSecondaryEntrances: true);
            if (!solved.Success)
            {
                Console.WriteLine($"\n[{name}] 계획 실패 — {solved.FailReason}");
                return;
            }
            EmergencyProblemV2 problem =
                solved.Selected.AutomaticPlan.Plan.Selected.Scenario.Problem;
            PipelinedPlanResultV2 plan =
                solved.Selected.AutomaticPlan.Plan.Selected.Plan;
            PlanUtilizationReportV2 report =
                PlanUtilizationV2.Analyze(problem, plan);
            double seconds = profile.PlanSeconds(plan.Ticks);
            long serviceLowerBound = report.RobotCount == 0
                ? 0
                : (report.TotalServiceTicks + report.RobotCount - 1) /
                  report.RobotCount;

            Console.WriteLine(
                $"\n[{name}] {plan.Ticks}틱 / {seconds:0.0}초 · " +
                $"이동차량 {problem.VehicleCount}대 · 유닛 {report.RobotCount}조");
            Console.WriteLine("유닛 | 이동 | 서비스 | 대기 | 유휴");
            for (int robot = 0; robot < report.RobotCount; robot++)
            {
                Console.WriteLine(
                    $"  {robot}  | {report.MoveTicks[robot],4} | " +
                    $"{report.ServiceTicks[robot],5} | " +
                    $"{report.WaitTicks[robot],4} | {report.IdleTicks[robot],4}");
                rows.Add(string.Join(",",
                    name,
                    robot,
                    report.MoveTicks[robot],
                    report.ServiceTicks[robot],
                    report.WaitTicks[robot],
                    report.IdleTicks[robot],
                    report.Makespan,
                    ""));
            }
            double waitShare = report.Makespan == 0
                ? 0.0
                : report.TotalWaitTicks /
                  (double)((long)report.Makespan * report.RobotCount);
            Console.WriteLine(
                $"유효 병렬성 {report.EffectiveParallelism:0.00}조 / " +
                $"{report.RobotCount}조 · 대기 비중 {waitShare:P1} · " +
                $"서비스 병렬 하한 {serviceLowerBound}틱 (makespan의 " +
                $"{(report.Makespan == 0 ? 0 : 100.0 * serviceLowerBound / report.Makespan):0}%)");
            rows.Add(string.Join(",",
                name,
                "fleet",
                report.TotalMoveTicks,
                report.TotalServiceTicks,
                report.TotalWaitTicks,
                report.TotalIdleTicks,
                report.Makespan,
                report.EffectiveParallelism.ToString(
                    "F2", CultureInfo.InvariantCulture)));
        }
    }
}
