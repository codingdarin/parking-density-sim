using System;
using System.Collections.Generic;

namespace ParkingSim.Core.V2
{
    public sealed class PlannerQualityRowV2
    {
        public int VehicleCount { get; set; }
        public bool ExactSuccess { get; set; }
        public bool CandidateSuccess { get; set; }
        public int ExactTicks { get; set; }
        public int CandidateTicks { get; set; }
        public int ExactExpandedStates { get; set; }
        public int CandidateExpandedStates { get; set; }
        public double GapPercent { get; set; }
        public bool WithinTolerance { get; set; }
        public string FailReason { get; set; }
    }

    public sealed class PlannerQualityReportV2
    {
        public double TolerancePercent { get; set; }
        public List<PlannerQualityRowV2> Rows { get; } = new List<PlannerQualityRowV2>();
        public bool AllWithinTolerance => Rows.TrueForAll(row => row.WithinTolerance);
    }

    /// <summary>
    /// 운영 후보 플래너를 소형 전역 exact 오라클과 동일 입력에서 비교한다.
    /// 계산량 감소가 아니라 성공 여부와 makespan 격차를 채택 기준으로 삼는다.
    /// </summary>
    public static class PlannerQualityEvaluatorV2
    {
        public static PlannerQualityReportV2 EvaluateLineRolling(
            int minVehicles = 2,
            int maxVehicles = 4,
            int rollingBatchSize = 3,
            double tolerancePercent = 10.0,
            int maxExpansions = 1000000)
        {
            if (minVehicles < 1 || maxVehicles < minVehicles)
                throw new ArgumentOutOfRangeException(nameof(minVehicles));
            if (tolerancePercent < 0) throw new ArgumentOutOfRangeException(nameof(tolerancePercent));

            var report = new PlannerQualityReportV2 { TolerancePercent = tolerancePercent };
            for (int vehicles = minVehicles; vehicles <= maxVehicles; vehicles++)
            {
                EmergencyProblemV2 exactProblem = V2ProblemFactory.LineProblem(vehicles);
                ExactEmergencyResultV2 exact = ExactEmergencySolverV2.SolveWeighted(
                    exactProblem,
                    heuristicWeight: 1,
                    maxExpansions: maxExpansions,
                    activeRobotCount: 2);
                RollingBatchResultV2 candidate = RollingBatchPlannerV2.Solve(
                    V2ProblemFactory.LineProblem(vehicles),
                    batchSize: rollingBatchSize,
                    maxExpansionsPerBatch: maxExpansions);

                var row = new PlannerQualityRowV2
                {
                    VehicleCount = vehicles,
                    ExactSuccess = exact.Success,
                    CandidateSuccess = candidate.Success,
                    ExactTicks = exact.Ticks,
                    CandidateTicks = candidate.TotalTicks,
                    ExactExpandedStates = exact.ExpandedStates,
                    CandidateExpandedStates = candidate.ExpandedStates,
                };
                if (!exact.Success)
                {
                    row.FailReason = "exact 오라클 실패: " + exact.FailReason;
                }
                else if (!candidate.Success)
                {
                    row.FailReason = "후보 플래너 실패: " + candidate.FailReason;
                }
                else
                {
                    row.GapPercent = exact.Ticks == 0
                        ? (candidate.TotalTicks == 0 ? 0 : double.PositiveInfinity)
                        : 100.0 * (candidate.TotalTicks - exact.Ticks) / exact.Ticks;
                    row.WithinTolerance = row.GapPercent <= tolerancePercent;
                    if (!row.WithinTolerance)
                        row.FailReason = $"makespan 격차 {row.GapPercent:F1}% > {tolerancePercent:F1}%";
                }
                report.Rows.Add(row);
            }
            return report;
        }
    }
}
