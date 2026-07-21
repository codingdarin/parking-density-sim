using System.Collections.Generic;
using ParkingSim.Core.Grid;
using ParkingSim.Core.Pathfinding;

namespace ParkingSim.Core.Agents
{
    /// <summary>
    /// 우선순위 순차 계획(Cooperative A*): 로봇 하나의 운반 미션 전체
    /// (접근 → 적재 1틱 → 운반 → 하차 1틱 → 복귀)를 시공간 A*로 계획한다.
    ///
    /// 규약:
    /// - 모든 계획은 시뮬레이션 실행 전에 수행 — grid는 초기 배치 상태여야 하고,
    ///   시간 종속 차단은 carLiftTicks(차량 id → 격자에서 사라지는 틱)로 표현한다.
    /// - 자기 예약은 미션 전체를 계획한 뒤 일괄 기록 — 단일 몸은 시점이 달라 자기충돌이
    ///   없으므로, 단계 경계에서 자기 예약이 자기 대기를 막는 문제를 회피한다.
    /// </summary>
    public static class CooperativePlanner
    {
        private static readonly (int Dx, int Dy)[] OneCell = { (0, 0) };
        private static readonly (int Dx, int Dy)[] TwoCellHorizontal = { (0, 0), (1, 0) };

        /// <summary>빈 몸 재배치 (미션 간 이동·후퇴). 도착 후 영구 주차. 실패 시 null.</summary>
        public static RobotTimeline PlanRelocation(
            GridMap grid, ReservationTable reservations, Dictionary<int, int> carLiftTicks,
            int robotId, (int X, int Y) start, (int X, int Y) goal, int maxTick, int startTick)
        {
            bool CellOk(int x, int y, int t)
            {
                if (!grid.InBounds(x, y)) return false;
                var type = grid.TypeAt(x, y);
                if (type == CellType.Outside || type == CellType.Stall) return false;
                int carId = grid.CarAt(x, y);
                if (carId != 0 && !(carLiftTicks.TryGetValue(carId, out int lift) && t >= lift))
                    return false;
                return reservations.IsFree(x, y, t);
            }

            var path = SpaceTimeAStar.FindPath(start, startTick, goal, OneCell,
                (x, y, t) => CellOk(x, y, t), maxTick);
            if (path == null) return null;

            var steps = new List<(int X, int Y, bool Carrying)>();
            foreach (var (x, y) in path)
                steps.Add((x, y, false));
            for (int i = 0; i < steps.Count; i++)
                reservations.ReserveStep(steps[i].X, steps[i].Y, startTick + i);
            reservations.ReserveFrom(goal.X, goal.Y, startTick + steps.Count - 1);

            return new RobotTimeline(robotId, 0, startTick, -1, -1, steps);
        }

        /// <summary>미션 계획 실패 시 null (예약·liftTicks 미변경). startTick = 미션 시작 전역 틱 (체이닝용).</summary>
        public static RobotTimeline PlanCarryMission(
            GridMap grid, ReservationTable reservations, Dictionary<int, int> carLiftTicks,
            int robotId, (int X, int Y) start, Car target,
            (int X, int Y) dropAnchor, (int X, int Y)? home, int maxTick, int startTick = 0)
        {
            var steps = new List<(int X, int Y, bool Carrying)>();

            bool CellOk(int x, int y, int t, int dockCarId)
            {
                if (!grid.InBounds(x, y)) return false;
                var type = grid.TypeAt(x, y);
                if (type == CellType.Outside || type == CellType.Stall) return false;
                int carId = grid.CarAt(x, y);
                if (carId != 0 && carId != dockCarId)
                {
                    // 이미 계획된 다른 미션이 이 차량을 들어올린 뒤라면 통과 가능
                    if (!(carLiftTicks.TryGetValue(carId, out int lift) && t >= lift))
                        return false;
                }
                return reservations.IsFree(x, y, t);
            }

            // 1) 접근 (빈 몸 1셀, 대상 차량 셀만 도킹 진입 허용)
            var approach = SpaceTimeAStar.FindPath(
                start, startTick, (target.X, target.Y), OneCell,
                (x, y, t) => CellOk(x, y, t, target.Id), maxTick);
            if (approach == null) return null;
            foreach (var (x, y) in approach)
                steps.Add((x, y, false));

            // 2) 적재 1틱 — 이 틱부터 차량은 격자에서 사라지고 로봇 풋프린트가 된다
            int liftTick = startTick + steps.Count;
            steps.Add((target.X, target.Y, true));
            carLiftTicks[target.Id] = liftTick;

            // 3) 운반 (1×2 강체)
            var carry = SpaceTimeAStar.FindPath(
                (target.X, target.Y), liftTick, dropAnchor, TwoCellHorizontal,
                (x, y, t) => CellOk(x, y, t, 0), maxTick);
            if (carry == null) { carLiftTicks.Remove(target.Id); return null; }
            for (int i = 1; i < carry.Count; i++)
                steps.Add((carry[i].X, carry[i].Y, true));

            // 4) 하차 1틱 — 싱크: 차량 소멸, 로봇은 1셀로
            int dropTick = startTick + steps.Count;
            steps.Add((dropAnchor.X, dropAnchor.Y, false));

            // 5) 복귀 (홈 지정 시)
            if (home.HasValue && (home.Value.X != dropAnchor.X || home.Value.Y != dropAnchor.Y))
            {
                var back = SpaceTimeAStar.FindPath(
                    dropAnchor, dropTick, home.Value, OneCell,
                    (x, y, t) => CellOk(x, y, t, 0), maxTick);
                if (back == null) { carLiftTicks.Remove(target.Id); return null; }
                for (int i = 1; i < back.Count; i++)
                    steps.Add((back[i].X, back[i].Y, false));
            }

            // 6) 예약 일괄 기록 (자기 미션 확정 후) — 전역 틱 기준
            for (int i = 0; i < steps.Count; i++)
            {
                int t = startTick + i;
                var (x, y, carrying) = steps[i];
                reservations.ReserveStep(x, y, t);
                if (carrying) reservations.ReserveStep(x + 1, y, t);
                // 하차 틱은 내려놓는 차량 폭까지 보수적으로 예약
                if (t == dropTick) reservations.ReserveStep(x + 1, y, t);
            }
            var last = steps[steps.Count - 1];
            reservations.ReserveFrom(last.X, last.Y, startTick + steps.Count - 1);

            return new RobotTimeline(robotId, target.Id, startTick, liftTick, dropTick, steps);
        }
    }
}
