using System;
using System.Collections.Generic;
using System.Linq;

namespace ParkingSim.Core.V2
{
    /// <summary>
    /// 계획의 로봇-틱 분해 결과. V1 `ClearanceReport`의 유휴 3분해에 해당하는
    /// V2 진단 지표 — 유효 병렬성이 낮은 원인이 대기(간섭)인지 유휴(작업 부족)인지 가른다.
    /// </summary>
    public sealed class PlanUtilizationReportV2
    {
        public int RobotCount { get; set; }
        public int Makespan { get; set; }
        public int[] MoveTicks { get; set; }
        public int[] ServiceTicks { get; set; }
        public int[] WaitTicks { get; set; }
        public int[] IdleTicks { get; set; }
        public long TotalMoveTicks { get; set; }
        public long TotalServiceTicks { get; set; }
        public long TotalWaitTicks { get; set; }
        public long TotalIdleTicks { get; set; }
        /// <summary>유효 병렬성 = (이동+서비스 틱 합) ÷ makespan — 동시 활동한 평균 조 수</summary>
        public double EffectiveParallelism { get; set; }
    }

    /// <summary>
    /// 확정된 계획의 타임라인·미션을 재생해 로봇-틱을 이동/서비스/대기/유휴로
    /// 분해한다. 대기 = 미션 구간 안의 정지(간섭·순서 대기), 유휴 = 미션 구간 밖.
    /// 대기의 원인 귀속(선행 로봇/적치 경합)은 다루지 않는다.
    /// </summary>
    public static class PlanUtilizationV2
    {
        public static PlanUtilizationReportV2 Analyze(
            EmergencyProblemV2 problem,
            PipelinedPlanResultV2 plan)
        {
            if (problem == null) throw new ArgumentNullException(nameof(problem));
            if (plan == null || !plan.Success || !plan.PhysicallyValid)
                throw new ArgumentException("유효한 계획이 필요함", nameof(plan));

            int robotCount = plan.RobotTimelines.Length;
            int makespan = plan.Ticks;
            var report = new PlanUtilizationReportV2
            {
                RobotCount = robotCount,
                Makespan = makespan,
                MoveTicks = new int[robotCount],
                ServiceTicks = new int[robotCount],
                WaitTicks = new int[robotCount],
                IdleTicks = new int[robotCount],
            };
            if (makespan <= 0)
            {
                report.EffectiveParallelism = 0.0;
                return report;
            }

            for (int robot = 0; robot < robotCount; robot++)
            {
                List<PipelinedMissionV2> missions = plan.Missions
                    .Where(mission => mission.RobotIndex == robot)
                    .ToList();
                int firstStart = missions.Count == 0
                    ? int.MaxValue
                    : missions.Min(mission => mission.StartTick);
                int lastDrop = missions.Count == 0
                    ? -1
                    : missions.Max(mission => mission.DropTick);

                for (int tick = 0; tick < makespan; tick++)
                {
                    (int X, int Y) now = PositionAt(problem, plan, robot, tick);
                    (int X, int Y) next = PositionAt(problem, plan, robot, tick + 1);
                    if (now != next)
                    {
                        report.MoveTicks[robot]++;
                    }
                    else if (InServiceWindow(problem, missions, tick))
                    {
                        report.ServiceTicks[robot]++;
                    }
                    else if (tick >= firstStart && tick < lastDrop)
                    {
                        report.WaitTicks[robot]++;
                    }
                    else
                    {
                        report.IdleTicks[robot]++;
                    }
                }
            }

            report.TotalMoveTicks = report.MoveTicks.Sum(ticks => (long)ticks);
            report.TotalServiceTicks =
                report.ServiceTicks.Sum(ticks => (long)ticks);
            report.TotalWaitTicks = report.WaitTicks.Sum(ticks => (long)ticks);
            report.TotalIdleTicks = report.IdleTicks.Sum(ticks => (long)ticks);
            report.EffectiveParallelism =
                (report.TotalMoveTicks + report.TotalServiceTicks) /
                (double)makespan;
            return report;
        }

        /// <summary>틱 t가 자기 미션의 취득/해제 서비스 창 안인가
        /// (창: [LiftTick-서비스틱, LiftTick), [DropTick-서비스틱, DropTick))</summary>
        private static bool InServiceWindow(
            EmergencyProblemV2 problem,
            IReadOnlyList<PipelinedMissionV2> missions,
            int tick)
        {
            int lift = problem.Timing.LiftServiceTicks;
            int drop = problem.Timing.DropServiceTicks;
            foreach (PipelinedMissionV2 mission in missions)
            {
                if (tick >= mission.LiftTick - lift && tick < mission.LiftTick)
                    return true;
                if (tick >= mission.DropTick - drop && tick < mission.DropTick)
                    return true;
            }
            return false;
        }

        private static (int X, int Y) PositionAt(
            EmergencyProblemV2 problem,
            PipelinedPlanResultV2 plan,
            int robot,
            int tick)
        {
            List<TimedRobotStateV2> timeline = plan.RobotTimelines[robot];
            for (int index = timeline.Count - 1; index >= 0; index--)
                if (timeline[index].Tick <= tick)
                    return (timeline[index].X, timeline[index].Y);
            return problem.RobotStarts[robot];
        }
    }
}
