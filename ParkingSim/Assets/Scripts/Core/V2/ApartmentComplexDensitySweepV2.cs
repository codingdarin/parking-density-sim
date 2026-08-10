using System;
using System.Linq;

namespace ParkingSim.Core.V2
{
    public enum ApartmentComplexDensityOutcomeV2
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

    public sealed class ApartmentComplexDensityTrialV2
    {
        public int BlockingVehicleCount { get; set; }
        public int BuildingId { get; set; }
        public bool IncludeSecondaryEntrances { get; set; }
        public ApartmentComplexDensityOutcomeV2 Outcome { get; set; }
        public bool PlanSuccess { get; set; }
        public bool WithinBudget { get; set; }
        public string SelectedEntrance { get; set; }
        public string SelectedRoute { get; set; }
        public int CandidateCount { get; set; }
        public int MovedVehicleCount { get; set; }
        public int Ticks { get; set; }
        public int ExpandedStates { get; set; }
        public double Seconds { get; set; }
        public string FailReason { get; set; }
    }

    /// <summary>
    /// 한 차량 밀도에서 한 화재동의 입구→전용구역 물리 개통시간을 평가한다.
    /// 시나리오·세션 생명주기와 파일 출력은 호출자가 관리한다.
    /// </summary>
    public static class ApartmentComplexDensitySweepV2
    {
        public static ApartmentComplexDensityTrialV2 Evaluate(
            ApartmentComplexScenarioV2 scenario,
            ApartmentComplexPlanningSessionV2 session,
            int buildingId,
            bool includeSecondaryEntrances,
            PhysicalTimeProfileV2 timeProfile,
            double budgetSeconds = TimeBudget.BaselineSeconds)
        {
            if (scenario == null) throw new ArgumentNullException(nameof(scenario));
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (timeProfile == null) throw new ArgumentNullException(nameof(timeProfile));
            if (budgetSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(budgetSeconds));

            ApartmentComplexPlanResultV2 result = session.Solve(
                new ApartmentFireIncidentV2(buildingId),
                includeSecondaryEntrances);
            var trial = new ApartmentComplexDensityTrialV2
            {
                BlockingVehicleCount = scenario.BlockingVehicleCount,
                BuildingId = buildingId,
                IncludeSecondaryEntrances = includeSecondaryEntrances,
                CandidateCount = result.Attempts.Sum(attempt =>
                    attempt.AutomaticPlan == null ||
                    attempt.AutomaticPlan.Generation == null ||
                    attempt.AutomaticPlan.Generation.Routes == null
                        ? 0
                        : attempt.AutomaticPlan.Generation.Routes.Count),
            };
            if (!result.Success)
            {
                trial.Outcome = MapFailure(result.Failure);
                trial.FailReason = result.FailReason;
                return trial;
            }

            EmergencyAccessCandidateResultV2 selected =
                result.Selected.AutomaticPlan.Plan.Selected;
            trial.PlanSuccess = true;
            trial.SelectedEntrance = result.Selected.Entrance.Name;
            trial.SelectedRoute = selected.Route.Name;
            trial.MovedVehicleCount = selected.Scenario.SelectedVehicleCount;
            trial.Ticks = selected.Plan.Ticks;
            trial.ExpandedStates = selected.Plan.ExpandedStates;
            trial.Seconds = timeProfile.PlanSeconds(selected.Plan.Ticks);
            trial.WithinBudget = trial.Seconds <= budgetSeconds;
            trial.Outcome = trial.WithinBudget
                ? ApartmentComplexDensityOutcomeV2.WithinBudget
                : ApartmentComplexDensityOutcomeV2.TimeBudgetExceeded;
            return trial;
        }

        private static ApartmentComplexDensityOutcomeV2 MapFailure(
            EmergencyAccessFailureV2 failure)
        {
            switch (failure)
            {
                case EmergencyAccessFailureV2.NoCenterline:
                    return ApartmentComplexDensityOutcomeV2.NoAccessRoute;
                case EmergencyAccessFailureV2.InsufficientWidth:
                    return ApartmentComplexDensityOutcomeV2.InsufficientWidth;
                case EmergencyAccessFailureV2.FixedObstruction:
                    return ApartmentComplexDensityOutcomeV2.FixedObstruction;
                case EmergencyAccessFailureV2.InsufficientStagingCapacity:
                    return ApartmentComplexDensityOutcomeV2.InsufficientStagingCapacity;
                case EmergencyAccessFailureV2.PhysicalPlanningFailed:
                    return ApartmentComplexDensityOutcomeV2.PhysicalPlanningFailed;
                case EmergencyAccessFailureV2.SearchLimitReached:
                    return ApartmentComplexDensityOutcomeV2.SearchLimitReached;
                default:
                    return ApartmentComplexDensityOutcomeV2.InvalidInput;
            }
        }
    }
}
