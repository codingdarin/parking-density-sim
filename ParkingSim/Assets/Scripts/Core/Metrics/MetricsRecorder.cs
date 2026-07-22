using System.Linq;
using ParkingSim.Core.Emergency;
using ParkingSim.Core.Grid;

namespace ParkingSim.Core.Metrics
{
    /// <summary>
    /// 비상 실행 결과(EmergencyResult + ClearanceReport)를 D9 산출물 지표로 환산한다.
    /// 순수 함수 — grid를 변형하지 않는다 (ClearanceEvaluator가 이미 grid를 소비하므로,
    /// 이 단계는 계획·리포트 객체만 읽는다).
    /// </summary>
    public static class MetricsRecorder
    {
        public static RunMetrics FromEmergency(
            ParkingLot lot, EmergencyConfig cfg, EmergencyResult plan, ClearanceReport report, int seed)
        {
            var m = new RunMetrics
            {
                OccupiedLanes = lot.Config.OccupiedLanes,
                FireMeters = cfg.FireMeters,
                PocketCount = lot.PocketXs.Count,
                Seed = seed,
                RobotCount = cfg.RobotCount,
                BetaCells = cfg.BetaCells,
                Success = plan.Success,
                FailReason = plan.FailReason ?? "",
                SectionCarCount = plan.SectionCarCount,
                Attempts = plan.PlanFailures,
                MainDrops = plan.MainDrops,
                PocketDrops = plan.PocketDrops,
            };

            // 봉투 예측 (간섭 무시): T = N × ((d+β) + 2·d_s) ÷ p, 1 m/s → 미터 = 초. p = RobotCount.
            int sectionLenCells = plan.SectionEndX - lot.CorridorStartX;
            double sectionLenM = sectionLenCells * GridMap.CellMeters;
            double dsM = lot.Config.StagingDistanceCells * GridMap.CellMeters;
            int p = cfg.RobotCount < 1 ? 1 : cfg.RobotCount;
            m.EnvelopeSeconds = plan.SectionCarCount * (sectionLenM + 2 * dsM) / p;

            if (!plan.Success || report == null)
                return m; // 실패 시 확보/병렬성 지표는 0 — 입력·필요량·재시도만 기록

            m.ClearTick = report.ClearTick;
            m.ClearSeconds = report.ClearSeconds;
            m.WithinBudget = report.WithinBudget;
            m.Collisions = report.Collisions;

            int makespan = plan.EndTick;
            m.MakespanTicks = makespan;

            // 가동률·유효 병렬성: 로봇별 활동 틱 = Σ_timelines (전이 수 − 대기). 대기는 적재/하차를 제외하므로
            // 활동 = 실이동 + 적재/하차 진행 틱. EffectiveP = 동시 가동 로봇 평균 (Σ활동 ÷ makespan).
            long totalWork = 0;
            foreach (var timelines in plan.Schedules)
                foreach (var tl in timelines)
                    totalWork += (tl.Steps.Count - 1) - tl.WaitTicks;

            if (makespan > 0)
            {
                m.EffectiveP = (double)totalWork / makespan;
                m.Utilization = m.EffectiveP / cfg.RobotCount;
            }
            m.DeviationRatio = m.EnvelopeSeconds > 0 ? m.ClearSeconds / m.EnvelopeSeconds : 0;

            return m;
        }
    }
}
