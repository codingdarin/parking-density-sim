using System.Collections.Generic;

namespace ParkingSim.Core.Agents
{
    /// <summary>
    /// 계획된 로봇 1대의 틱별 타임라인. 인덱스 = 틱, 마지막 스텝 이후는 그 자리에 정지.
    /// </summary>
    public sealed class RobotTimeline
    {
        public int RobotId { get; }
        public int TargetCarId { get; }
        public int LiftTick { get; }
        public int DropTick { get; }
        public IReadOnlyList<(int X, int Y, bool Carrying)> Steps { get; }
        public int EndTick => Steps.Count - 1;

        public RobotTimeline(int robotId, int targetCarId, int liftTick, int dropTick,
            List<(int X, int Y, bool Carrying)> steps)
        {
            RobotId = robotId;
            TargetCarId = targetCarId;
            LiftTick = liftTick;
            DropTick = dropTick;
            Steps = steps;
        }

        public (int X, int Y, bool Carrying) At(int tick)
        {
            if (tick >= Steps.Count) return Steps[Steps.Count - 1];
            return Steps[tick];
        }

        /// <summary>제자리 대기 틱 수 (이동 없이 보낸 틱 — 혼잡 지표)</summary>
        public int WaitTicks
        {
            get
            {
                int waits = 0;
                for (int i = 1; i < Steps.Count; i++)
                    if (Steps[i].X == Steps[i - 1].X && Steps[i].Y == Steps[i - 1].Y &&
                        i != LiftTick && i != DropTick)
                        waits++;
                return waits;
            }
        }
    }
}
