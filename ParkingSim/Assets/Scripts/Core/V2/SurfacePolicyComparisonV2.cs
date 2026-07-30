using System;
using System.Collections.Generic;
using System.Linq;

namespace ParkingSim.Core.V2
{
    public enum SurfaceEmergencyPolicyV2
    {
        AlwaysClear,
        FullClearance,
        MinimumBlockingVehicles,
        FastestPhysicalOpening,
    }

    public sealed class SurfacePolicyMeasurementV2
    {
        public SurfaceEmergencyPolicyV2 Policy { get; set; }
        public int GrossAdditionalCars { get; set; }
        public int MovedVehicles { get; set; }
        public int Ticks { get; set; }
        public double Seconds { get; set; }
        public bool WithinSevenMinutes { get; set; }
        public double? ReductionVsFullClearance { get; set; }
        public EmergencyProblemV2 ScenarioProblem { get; set; }
        public PipelinedPlanResultV2 Plan { get; set; }
    }

    public sealed class SurfacePolicyComparisonResultV2
    {
        public bool Success { get; set; }
        public string FailReason { get; set; }
        public PhysicalTimeProfileV2 TimeProfile { get; set; }
        public IReadOnlyList<SurfacePolicyMeasurementV2> Policies { get; set; }
    }

    /// <summary>
    /// 같은 지상형 맵·차량5대·적치5면에서 완료 정책만 바꿔 비교한다.
    /// 최소 방해차와 최소 물리 개통시간을 별도 정책으로 유지해 목적함수 차이를 드러낸다.
    /// </summary>
    public static class SurfacePolicyComparisonV2
    {
        public static SurfacePolicyComparisonResultV2 Run(
            PhysicalTimeProfileV2 timeProfile,
            int activeRobotCount = 4,
            int maxHighLevelCandidates = 8,
            int maxTick = 5000)
        {
            if (timeProfile == null) throw new ArgumentNullException(nameof(timeProfile));
            SurfaceApartmentScenarioV2 surface =
                SurfaceApartmentScenarioFactoryV2.Build(
                    timeProfile.CreateOperationTiming());
            AutomaticEmergencyAccessPlanResultV2 automatic =
                EmergencyAccessRouteGeneratorV2.Solve(
                    surface.BaseProblem,
                    (1, 5),
                    (22, 5),
                    activeRobotCount,
                    maxHighLevelCandidates: maxHighLevelCandidates,
                    maxTick: maxTick);
            if (!automatic.Success)
                return Failure(timeProfile, "자동 핵심경로 실패: " + automatic.FailReason);

            EmergencyAccessCandidateResultV2 minimumBlockers =
                automatic.Plan.Candidates
                    .Where(candidate => candidate.Success)
                    .OrderBy(candidate => candidate.Scenario.SelectedVehicleCount)
                    .ThenBy(candidate => candidate.Plan.Ticks)
                    .ThenBy(candidate => candidate.Route.Name, StringComparer.Ordinal)
                    .FirstOrDefault();
            if (minimumBlockers == null)
                return Failure(timeProfile, "최소 방해차 후보가 없음");

            EmergencyScenarioBuildResultV2 fullScenario =
                new EmergencyScenarioV2(
                    "surface-full-final-comparison",
                    (22, 5),
                    surface.FullClearanceCells).Build(surface.BaseProblem);
            if (!fullScenario.Success)
                return Failure(timeProfile, "전면 시나리오 실패: " + fullScenario.FailReason);
            PipelinedPlanResultV2 fullPlan =
                PipelinedPrioritizedPlannerV2.Solve(
                    fullScenario.Problem,
                    activeRobotCount: Math.Min(
                        activeRobotCount, fullScenario.SelectedVehicleCount),
                    maxHighLevelCandidates: maxHighLevelCandidates,
                    maxTick: maxTick);
            if (!fullPlan.Success || !fullPlan.PhysicallyValid)
                return Failure(timeProfile, "전면 물리 계획 실패: " + fullPlan.FailReason);

            var rows = new List<SurfacePolicyMeasurementV2>
            {
                new SurfacePolicyMeasurementV2
                {
                    Policy = SurfaceEmergencyPolicyV2.AlwaysClear,
                    GrossAdditionalCars = 0,
                    MovedVehicles = 0,
                    Ticks = 0,
                    Seconds = 0,
                    WithinSevenMinutes = true,
                },
                Measurement(
                    SurfaceEmergencyPolicyV2.FullClearance,
                    5,
                    fullScenario,
                    fullPlan,
                    timeProfile),
                Measurement(
                    SurfaceEmergencyPolicyV2.MinimumBlockingVehicles,
                    5,
                    minimumBlockers.Scenario,
                    minimumBlockers.Plan,
                    timeProfile),
                Measurement(
                    SurfaceEmergencyPolicyV2.FastestPhysicalOpening,
                    5,
                    automatic.Plan.Selected.Scenario,
                    automatic.Plan.Selected.Plan,
                    timeProfile),
            };
            double fullSeconds = rows
                .Single(row => row.Policy == SurfaceEmergencyPolicyV2.FullClearance)
                .Seconds;
            foreach (SurfacePolicyMeasurementV2 row in rows)
            {
                if (row.Policy == SurfaceEmergencyPolicyV2.AlwaysClear) continue;
                row.ReductionVsFullClearance =
                    fullSeconds <= 0 ? 0 : 1.0 - row.Seconds / fullSeconds;
            }

            return new SurfacePolicyComparisonResultV2
            {
                Success = true,
                TimeProfile = timeProfile,
                Policies = rows,
            };
        }

        private static SurfacePolicyMeasurementV2 Measurement(
            SurfaceEmergencyPolicyV2 policy,
            int grossAdditionalCars,
            EmergencyScenarioBuildResultV2 scenario,
            PipelinedPlanResultV2 plan,
            PhysicalTimeProfileV2 timeProfile)
        {
            double seconds = timeProfile.PlanSeconds(plan.Ticks);
            return new SurfacePolicyMeasurementV2
            {
                Policy = policy,
                GrossAdditionalCars = grossAdditionalCars,
                MovedVehicles = scenario.SelectedVehicleCount,
                Ticks = plan.Ticks,
                Seconds = seconds,
                WithinSevenMinutes = seconds <= 420.0,
                ScenarioProblem = scenario.Problem,
                Plan = plan,
            };
        }

        private static SurfacePolicyComparisonResultV2 Failure(
            PhysicalTimeProfileV2 profile,
            string reason)
        {
            return new SurfacePolicyComparisonResultV2
            {
                Success = false,
                FailReason = reason,
                TimeProfile = profile,
                Policies = Array.Empty<SurfacePolicyMeasurementV2>(),
            };
        }
    }
}
