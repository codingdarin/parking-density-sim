using System;
using System.Linq;

namespace ParkingSim.Core.V2
{
    public enum SurfaceDensityOutcomeV2
    {
        WithinBudget,
        TimeBudgetExceeded,
        NoAccessRoute,
        InsufficientWidth,
        FixedObstruction,
        InsufficientStagingCapacity,
        PhysicalPlanningFailed,
        SearchLimitReached,
        InvalidInput,
    }

    public sealed class SurfaceDensityTrialV2
    {
        public int BlockingVehicleCount { get; set; }
        public int StagingCapacity { get; set; }
        public SurfaceVehiclePlacementV2 Placement { get; set; }
        public (int X, int Y) FireCell { get; set; }
        public SurfaceDensityOutcomeV2 Outcome { get; set; }
        public bool PlanSuccess { get; set; }
        public bool WithinBudget { get; set; }
        public int CandidateCount { get; set; }
        public int MinimumRequiredVehicleCount { get; set; }
        public int MovedVehicleCount { get; set; }
        public string SelectedRoute { get; set; }
        public int Ticks { get; set; }
        public int ExpandedStates { get; set; }
        public double Seconds { get; set; }
        public string FailReason { get; set; }
    }

    /// <summary>
    /// 지상 아파트형의 접근축 가변 주차 수·배치·화재 위치 한 점을 현실시간으로 평가한다.
    /// 파일 출력과 집계는 ConsoleSim 계층이 담당한다.
    /// </summary>
    public static class SurfaceDensitySweepV2
    {
        public static SurfaceDensityTrialV2 Evaluate(
            int blockingVehicleCount,
            SurfaceVehiclePlacementV2 placement,
            int stagingCapacity,
            (int X, int Y) fireCell,
            PhysicalTimeProfileV2 timeProfile,
            int activeRobotCount = 4,
            double budgetSeconds = TimeBudget.BaselineSeconds,
            int maxHighLevelCandidates = 8,
            int maxTick = 5000,
            int maxExpansionsPerPath = 200000)
        {
            if (timeProfile == null) throw new ArgumentNullException(nameof(timeProfile));
            if (budgetSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(budgetSeconds));

            SurfaceApartmentScenarioV2 surface =
                SurfaceApartmentScenarioFactoryV2.BuildDensity(
                    blockingVehicleCount,
                    placement,
                    stagingCapacity,
                    timeProfile.CreateOperationTiming());
            AutomaticEmergencyAccessPlanResultV2 automatic =
                EmergencyAccessRouteGeneratorV2.Solve(
                    surface.BaseProblem,
                    (1, 5),
                    fireCell,
                    activeRobotCount,
                    maxHighLevelCandidates: maxHighLevelCandidates,
                    maxTick: maxTick,
                    maxExpansionsPerPath: maxExpansionsPerPath);
            var trial = new SurfaceDensityTrialV2
            {
                BlockingVehicleCount = blockingVehicleCount,
                StagingCapacity = stagingCapacity,
                Placement = placement,
                FireCell = fireCell,
                CandidateCount = automatic.Generation == null ||
                                 automatic.Generation.Routes == null
                    ? 0
                    : automatic.Generation.Routes.Count,
            };
            if (!automatic.Success)
            {
                trial.MinimumRequiredVehicleCount =
                    automatic.Plan == null || automatic.Plan.Candidates == null
                        ? 0
                        : automatic.Plan.Candidates
                            .Where(candidate =>
                                candidate.Scenario != null &&
                                candidate.Scenario.Success)
                            .Select(candidate =>
                                candidate.Scenario.SelectedVehicleCount)
                            .DefaultIfEmpty(0)
                            .Min();
                trial.Outcome = MapFailure(automatic.Failure);
                trial.FailReason = automatic.FailReason;
                return trial;
            }

            EmergencyAccessCandidateResultV2 selected = automatic.Plan.Selected;
            trial.PlanSuccess = true;
            trial.MovedVehicleCount = selected.Scenario.SelectedVehicleCount;
            trial.MinimumRequiredVehicleCount = trial.MovedVehicleCount;
            trial.SelectedRoute = selected.Route.Name;
            trial.Ticks = selected.Plan.Ticks;
            trial.ExpandedStates = selected.Plan.ExpandedStates;
            trial.Seconds = timeProfile.PlanSeconds(selected.Plan.Ticks);
            trial.WithinBudget = trial.Seconds <= budgetSeconds;
            trial.Outcome = trial.WithinBudget
                ? SurfaceDensityOutcomeV2.WithinBudget
                : SurfaceDensityOutcomeV2.TimeBudgetExceeded;
            return trial;
        }

        private static SurfaceDensityOutcomeV2 MapFailure(
            EmergencyAccessFailureV2 failure)
        {
            switch (failure)
            {
                case EmergencyAccessFailureV2.NoCenterline:
                    return SurfaceDensityOutcomeV2.NoAccessRoute;
                case EmergencyAccessFailureV2.InsufficientWidth:
                    return SurfaceDensityOutcomeV2.InsufficientWidth;
                case EmergencyAccessFailureV2.FixedObstruction:
                    return SurfaceDensityOutcomeV2.FixedObstruction;
                case EmergencyAccessFailureV2.InsufficientStagingCapacity:
                    return SurfaceDensityOutcomeV2.InsufficientStagingCapacity;
                case EmergencyAccessFailureV2.PhysicalPlanningFailed:
                    return SurfaceDensityOutcomeV2.PhysicalPlanningFailed;
                case EmergencyAccessFailureV2.SearchLimitReached:
                    return SurfaceDensityOutcomeV2.SearchLimitReached;
                default:
                    return SurfaceDensityOutcomeV2.InvalidInput;
            }
        }
    }
}
