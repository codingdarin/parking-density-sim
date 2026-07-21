using System;
using System.Collections.Generic;
using ParkingSim.Core.Grid;
using ParkingSim.Core.Pathfinding;

namespace ParkingSim.Core.Agents
{
    /// <summary>운반 미션 정의 (로봇 1대 × 차량 1대).</summary>
    public readonly struct CarryMission
    {
        public int RobotId { get; }
        public (int X, int Y) Start { get; }
        public Car Target { get; }
        public (int X, int Y) DropAnchor { get; }
        public (int X, int Y)? Home { get; }

        public CarryMission(int robotId, (int X, int Y) start, Car target,
            (int X, int Y) dropAnchor, (int X, int Y)? home)
        {
            RobotId = robotId;
            Start = start;
            Target = target;
            DropAnchor = dropAnchor;
            Home = home;
        }
    }

    public sealed class FleetPlanResult
    {
        /// <summary>계획 순서대로의 타임라인</summary>
        public IReadOnlyList<RobotTimeline> Timelines { get; }
        /// <summary>사용한 시도 횟수 (1 = 기본 순서로 성공). 병렬성 붕괴의 독립 지표</summary>
        public int Attempts { get; }
        /// <summary>채택된 계획 순서 (missions 인덱스)</summary>
        public IReadOnlyList<int> OrderUsed { get; }
        public IReadOnlyDictionary<int, int> CarLiftTicks { get; }

        public FleetPlanResult(List<RobotTimeline> timelines, int attempts,
            int[] orderUsed, Dictionary<int, int> carLiftTicks)
        {
            Timelines = timelines;
            Attempts = attempts;
            OrderUsed = orderUsed;
            CarLiftTicks = carLiftTicks;
        }
    }

    /// <summary>
    /// 함대 계획: 우선순위 순차 계획을 결정론적 규칙으로 수행한다.
    /// 우선순위 배정이 해의 존재를 좌우하므로(D3 특이사항 1) 수동 조정 대신:
    /// 기본 순서(입력 순) → 실패 시 시드 기반 순열로 최대 K회 재시도 → 전부 실패 시 null.
    /// 재시도 횟수(Attempts)는 혼잡·병렬성 붕괴의 독립 지표로 기록된다.
    /// </summary>
    public static class MissionPlanner
    {
        public static FleetPlanResult TryPlanAll(
            GridMap grid, IReadOnlyList<CarryMission> missions,
            int seed, int maxAttempts, int maxTick)
        {
            var rng = new Random(seed);
            var order = new int[missions.Count];
            for (int i = 0; i < order.Length; i++) order[i] = i;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                var rt = new ReservationTable();
                var liftTicks = new Dictionary<int, int>();
                var timelines = new List<RobotTimeline>();
                bool ok = true;

                foreach (int idx in order)
                {
                    var m = missions[idx];
                    var tl = CooperativePlanner.PlanCarryMission(
                        grid, rt, liftTicks, m.RobotId, m.Start, m.Target, m.DropAnchor, m.Home, maxTick);
                    if (tl == null) { ok = false; break; }
                    timelines.Add(tl);
                }

                if (ok)
                    return new FleetPlanResult(timelines, attempt, (int[])order.Clone(), liftTicks);

                Shuffle(order, rng); // 다음 시도: 시드 순열 (재현 가능)
            }
            return null; // K회 모두 실패 → 호출부가 "확보 실패"로 기록
        }

        private static void Shuffle(int[] array, Random rng)
        {
            for (int i = array.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (array[i], array[j]) = (array[j], array[i]);
            }
        }
    }
}
