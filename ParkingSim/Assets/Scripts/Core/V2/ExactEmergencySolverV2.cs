using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ParkingSim.Core.V2
{
    public sealed class ExactEmergencyResultV2
    {
        public bool Success { get; set; }
        public string FailReason { get; set; }
        public int Ticks { get; set; }
        public int ExpandedStates { get; set; }
        public int InitialVehicleCount { get; set; }
        public int FinalVehicleCount { get; set; }
        public int ActiveRobotCount { get; set; }
        public int[] FinalVehicleSlots { get; set; }
        public (int X, int Y)[] FinalRobotPositions { get; set; }
        public List<string> JointActions { get; } = new List<string>();
        public int RotationActions => JointActions.Sum(a => CountToken(a, "rotate"));

        private static int CountToken(string text, string token)
        {
            int count = 0, at = 0;
            while ((at = text.IndexOf(token, at, StringComparison.Ordinal)) >= 0)
            {
                count++;
                at += token.Length;
            }
            return count;
        }
    }

    /// <summary>
    /// 로봇 2대의 위치·적재 차량·차량 슬롯을 하나의 공동 상태로 BFS한다.
    /// 차량 선택/로봇 배정/적치 목적지/이동·회전을 미리 고정하지 않으므로,
    /// 주어진 작은 격자와 1틱 행동 모델 안에서 최초 해는 최소 makespan의 정확해다.
    /// </summary>
    public static class ExactEmergencySolverV2
    {
        private enum ActionKind : byte { Wait, Move, Rotate, Lift, Drop, ContinueService }
        private enum ServiceKind : byte { None, Lift, Drop }

        private readonly struct ActionV2
        {
            public ActionKind Kind { get; }
            public int A { get; }
            public int B { get; }
            public string Label { get; }

            public ActionV2(ActionKind kind, int a, int b, string label)
            {
                Kind = kind;
                A = a;
                B = b;
                Label = label;
            }
        }

        private struct RobotState
        {
            public int X;
            public int Y;
            public VehicleOrientation Orientation;
            public int CarryVehicle;
            public ServiceKind Service;
            public int ServiceRemaining;
            public int PendingVehicle;
            public int PendingSlot;
        }

        private sealed class State
        {
            public RobotState[] Robots;
            /// <summary>0 이상=주차 슬롯 인덱스, -1=로봇0 적재, -2=로봇1 적재.</summary>
            public int[] VehicleSlots;

            public State Clone()
            {
                return new State
                {
                    Robots = (RobotState[])Robots.Clone(),
                    VehicleSlots = (int[])VehicleSlots.Clone(),
                };
            }
        }

        private sealed class Parent
        {
            public string PreviousKey;
            public string JointAction;
        }

        public static ExactEmergencyResultV2 Solve(
            EmergencyProblemV2 problem, int maxExpansions = 500000, int activeRobotCount = 2)
        {
            if (activeRobotCount < 1 || activeRobotCount > 2)
                throw new ArgumentOutOfRangeException(nameof(activeRobotCount));
            var result = new ExactEmergencyResultV2
            {
                InitialVehicleCount = problem.VehicleCount,
                ActiveRobotCount = activeRobotCount,
            };
            if (problem.StagingCapacity < problem.VehicleCount)
            {
                result.Success = false;
                result.FailReason = $"적치 용량 부족: 차량 {problem.VehicleCount}대 > 슬롯 {problem.StagingCapacity}면";
                result.FinalVehicleCount = problem.VehicleCount;
                return result;
            }

            var start = CreateStart(problem);
            string startKey = Key(start);
            var queue = new Queue<(State State, int Depth)>();
            var seen = new HashSet<string> { startKey };
            var parents = new Dictionary<string, Parent>();
            queue.Enqueue((start, 0));

            while (queue.Count > 0 && result.ExpandedStates < maxExpansions)
            {
                var item = queue.Dequeue();
                var current = item.State;
                int depth = item.Depth;
                result.ExpandedStates++;

                if (IsGoal(problem, current))
                {
                    string goalKey = Key(current);
                    result.Success = true;
                    result.Ticks = depth;
                    result.FinalVehicleSlots = (int[])current.VehicleSlots.Clone();
                    result.FinalRobotPositions = current.Robots.Select(r => (r.X, r.Y)).ToArray();
                    result.FinalVehicleCount = current.VehicleSlots.Length;
                    Reconstruct(startKey, goalKey, parents, result.JointActions);
                    return result;
                }

                var a0s = Actions(problem, current, 0, activeRobotCount);
                var a1s = Actions(problem, current, 1, activeRobotCount);
                foreach (var a0 in a0s)
                {
                    foreach (var a1 in a1s)
                    {
                        var next = ApplyJoint(problem, current, a0, a1);
                        if (next == null) continue;
                        string key = Key(next);
                        if (!seen.Add(key)) continue;
                        parents[key] = new Parent
                        {
                            PreviousKey = Key(current),
                            JointAction = $"r1:{a0.Label} | r2:{a1.Label}",
                        };
                        queue.Enqueue((next, depth + 1));
                    }
                }
            }

            result.Success = false;
            result.FailReason = result.ExpandedStates >= maxExpansions
                ? $"정확해 탐색 상한 {maxExpansions} 상태 초과"
                : "해 없음";
            result.FinalVehicleCount = problem.VehicleCount;
            return result;
        }

        /// <summary>
        /// 동일한 물리 상태·충돌 규칙을 쓰되 남은 작업거리 휴리스틱을 가중한 근사 탐색.
        /// 작은 문제에서는 Solve(BFS 정확해)와 최적성 격차를 측정하고,
        /// 격차 기준을 통과한 뒤에만 더 큰 문제에 사용한다.
        /// </summary>
        public static ExactEmergencyResultV2 SolveWeighted(
            EmergencyProblemV2 problem, int heuristicWeight = 1,
            int maxExpansions = 200000, int activeRobotCount = 2)
        {
            if (heuristicWeight < 1) throw new ArgumentOutOfRangeException(nameof(heuristicWeight));
            return SolveWeightedRatio(
                problem, heuristicNumerator: heuristicWeight, heuristicDenominator: 1,
                maxExpansions: maxExpansions, activeRobotCount: activeRobotCount);
        }

        /// <summary>
        /// admissible h에 대해 w=1.1(10g+11h) bounded weighted A*.
        /// 해를 반환하면 이 행동 모델의 최적 makespan 대비 10% 이내 상한을 갖는다.
        /// </summary>
        public static ExactEmergencyResultV2 SolveBounded10Percent(
            EmergencyProblemV2 problem, int maxExpansions = 1000000, int activeRobotCount = 2)
        {
            return SolveWeightedRatio(
                problem, heuristicNumerator: 11, heuristicDenominator: 10,
                maxExpansions: maxExpansions, activeRobotCount: activeRobotCount);
        }

        private static ExactEmergencyResultV2 SolveWeightedRatio(
            EmergencyProblemV2 problem, int heuristicNumerator, int heuristicDenominator,
            int maxExpansions, int activeRobotCount)
        {
            if (heuristicNumerator < heuristicDenominator || heuristicDenominator < 1)
                throw new ArgumentOutOfRangeException(nameof(heuristicNumerator));
            if (activeRobotCount < 1 || activeRobotCount > 2)
                throw new ArgumentOutOfRangeException(nameof(activeRobotCount));

            var result = new ExactEmergencyResultV2
            {
                InitialVehicleCount = problem.VehicleCount,
                ActiveRobotCount = activeRobotCount,
            };
            if (problem.StagingCapacity < problem.VehicleCount)
            {
                result.Success = false;
                result.FailReason = $"적치 용량 부족: 차량 {problem.VehicleCount}대 > 슬롯 {problem.StagingCapacity}면";
                result.FinalVehicleCount = problem.VehicleCount;
                return result;
            }

            var start = CreateStart(problem);
            string startKey = Key(start);
            var open = new SearchHeap();
            var bestDepth = new Dictionary<string, int> { [startKey] = 0 };
            var parents = new Dictionary<string, Parent>();
            open.Push(
                (long)heuristicNumerator * Heuristic(problem, start, activeRobotCount), 0, start);

            while (open.Count > 0 && result.ExpandedStates < maxExpansions)
            {
                var node = open.Pop();
                var current = node.State;
                int depth = node.Depth;
                string currentKey = Key(current);
                if (!bestDepth.TryGetValue(currentKey, out int known) || known != depth) continue;
                result.ExpandedStates++;

                if (IsGoal(problem, current))
                {
                    result.Success = true;
                    result.Ticks = depth;
                    result.FinalVehicleSlots = (int[])current.VehicleSlots.Clone();
                    result.FinalRobotPositions = current.Robots.Select(r => (r.X, r.Y)).ToArray();
                    result.FinalVehicleCount = current.VehicleSlots.Length;
                    Reconstruct(startKey, currentKey, parents, result.JointActions);
                    return result;
                }

                var a0s = Actions(problem, current, 0, activeRobotCount);
                var a1s = Actions(problem, current, 1, activeRobotCount);
                foreach (var a0 in a0s)
                {
                    foreach (var a1 in a1s)
                    {
                        var next = ApplyJoint(problem, current, a0, a1);
                        if (next == null) continue;
                        int nextDepth = depth + 1;
                        string key = Key(next);
                        if (bestDepth.TryGetValue(key, out int previous) && previous <= nextDepth) continue;
                        bestDepth[key] = nextDepth;
                        parents[key] = new Parent
                        {
                            PreviousKey = currentKey,
                            JointAction = $"r1:{a0.Label} | r2:{a1.Label}",
                        };
                        int h = Heuristic(problem, next, activeRobotCount);
                        long score = (long)heuristicDenominator * nextDepth +
                                     (long)heuristicNumerator * h;
                        open.Push(score, nextDepth, next);
                    }
                }
            }

            result.Success = false;
            result.FailReason = result.ExpandedStates >= maxExpansions
                ? $"가중 탐색 상한 {maxExpansions} 상태 초과"
                : "해 없음";
            result.FinalVehicleCount = problem.VehicleCount;
            return result;
        }

        private static int Heuristic(EmergencyProblemV2 problem, State state, int activeRobotCount)
        {
            int mandatoryActions = 0;
            int longestSingleTask = 0;
            for (int v = 0; v < state.VehicleSlots.Length; v++)
            {
                int location = state.VehicleSlots[v];
                if (location >= 0 && problem.Slots[location].Kind == SlotKind.Staging) continue;

                int servicingRobot = -1;
                for (int r = 0; r < state.Robots.Length; r++)
                    if (state.Robots[r].Service != ServiceKind.None &&
                        state.Robots[r].PendingVehicle == v)
                        servicingRobot = r;
                if (servicingRobot >= 0)
                {
                    var sr = state.Robots[servicingRobot];
                    if (sr.Service == ServiceKind.Drop)
                    {
                        mandatoryActions += sr.ServiceRemaining;
                        longestSingleTask = Math.Max(longestSingleTask, sr.ServiceRemaining);
                    }
                    else
                    {
                        int task = sr.ServiceRemaining + problem.Timing.DropServiceTicks +
                                   MinStagingDistance(problem,
                                       new VehiclePose(sr.X, sr.Y, problem.Slots[sr.PendingSlot].Pose.Orientation),
                                       state);
                        mandatoryActions += sr.ServiceRemaining + problem.Timing.DropServiceTicks;
                        longestSingleTask = Math.Max(longestSingleTask, task);
                    }
                    continue;
                }

                if (location < 0)
                {
                    int r = -1 - location;
                    var robot = state.Robots[r];
                    mandatoryActions += problem.Timing.DropServiceTicks;
                    int task = problem.Timing.DropServiceTicks + MinStagingDistance(
                        problem, new VehiclePose(robot.X, robot.Y, robot.Orientation), state);
                    longestSingleTask = Math.Max(longestSingleTask, task);
                }
                else
                {
                    var source = problem.Slots[location].Pose;
                    mandatoryActions += problem.Timing.LiftServiceTicks + problem.Timing.DropServiceTicks;
                    int task = problem.Timing.LiftServiceTicks + problem.Timing.DropServiceTicks +
                               MinStagingDistance(problem, source, state);
                    longestSingleTask = Math.Max(longestSingleTask, task);
                }
            }
            // 두 하한의 max: 필수 액션 총량/R, 차량 하나의 최소 운반거리.
            // 로봇 접근거리·슬롯 경합·타 차량 우회는 전부 무시하므로 admissible.
            int actionLowerBound = (mandatoryActions + activeRobotCount - 1) / activeRobotCount;
            return Math.Max(actionLowerBound, longestSingleTask);
        }

        private static int MinStagingDistance(
            EmergencyProblemV2 problem, VehiclePose pose, State state)
        {
            int best = int.MaxValue;
            for (int s = 0; s < problem.Slots.Count; s++)
            {
                var slot = problem.Slots[s];
                if (slot.Kind != SlotKind.Staging || SlotOccupied(state, s)) continue;
                int d = Math.Abs(pose.X - slot.Pose.X) + Math.Abs(pose.Y - slot.Pose.Y);
                if (pose.Orientation != slot.Pose.Orientation) d++;
                best = Math.Min(best, d);
            }
            return best == int.MaxValue ? 0 : best;
        }

        private static State CreateStart(EmergencyProblemV2 problem)
        {
            var robots = new RobotState[2];
            for (int r = 0; r < 2; r++)
            {
                robots[r] = new RobotState
                {
                    X = problem.RobotStarts[r].X,
                    Y = problem.RobotStarts[r].Y,
                    Orientation = VehicleOrientation.Horizontal,
                    CarryVehicle = -1,
                    Service = ServiceKind.None,
                    ServiceRemaining = 0,
                    PendingVehicle = -1,
                    PendingSlot = -1,
                };
            }
            return new State
            {
                Robots = robots,
                VehicleSlots = problem.InitialVehicleSlots.ToArray(),
            };
        }

        private static List<ActionV2> Actions(
            EmergencyProblemV2 problem, State state, int robotIndex, int activeRobotCount)
        {
            var robot = state.Robots[robotIndex];
            var actions = new List<ActionV2> { new ActionV2(ActionKind.Wait, 0, 0, "wait") };
            if (robotIndex >= activeRobotCount) return actions;
            if (robot.Service != ServiceKind.None)
                return new List<ActionV2>
                {
                    new ActionV2(ActionKind.ContinueService, 0, 0, "service")
                };
            if (robot.CarryVehicle < 0)
            {
                foreach (var d in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    if (problem.IsFloor(robot.X + d.Item1, robot.Y + d.Item2))
                        actions.Add(new ActionV2(ActionKind.Move, d.Item1, d.Item2, $"move({d.Item1},{d.Item2})"));

                for (int v = 0; v < state.VehicleSlots.Length; v++)
                {
                    int slotIndex = state.VehicleSlots[v];
                    if (slotIndex < 0 || VehicleClaimed(state, v)) continue;
                    var pose = problem.Slots[slotIndex].Pose;
                    if (pose.X == robot.X && pose.Y == robot.Y)
                        actions.Add(new ActionV2(ActionKind.Lift, v, slotIndex, $"lift(v{v + 1})"));
                }
            }
            else
            {
                var pose = new VehiclePose(robot.X, robot.Y, robot.Orientation);
                foreach (var d in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    if (problem.PoseFits(pose.Translate(d.Item1, d.Item2)))
                        actions.Add(new ActionV2(ActionKind.Move, d.Item1, d.Item2, $"carry({d.Item1},{d.Item2})"));
                if (problem.PoseFits(pose.Rotate()))
                    actions.Add(new ActionV2(ActionKind.Rotate, 0, 0, "rotate"));

                for (int s = 0; s < problem.Slots.Count; s++)
                {
                    if (problem.Slots[s].Kind != SlotKind.Staging ||
                        SlotOccupied(state, s) || SlotClaimed(state, s)) continue;
                    if (pose.Equals(problem.Slots[s].Pose))
                        actions.Add(new ActionV2(ActionKind.Drop, s, 0, $"drop(s{s})"));
                }
            }
            return actions;
        }

        private static State ApplyJoint(
            EmergencyProblemV2 problem, State current, ActionV2 a0, ActionV2 a1)
        {
            // 같은 차량 동시 리프트·같은 슬롯 동시 하차는 불가.
            if (a0.Kind == ActionKind.Lift && a1.Kind == ActionKind.Lift && a0.A == a1.A) return null;
            if (a0.Kind == ActionKind.Drop && a1.Kind == ActionKind.Drop && a0.A == a1.A) return null;

            var next = current.Clone();
            if (!ApplyOne(problem, next, 0, a0)) return null;
            if (!ApplyOne(problem, next, 1, a1)) return null;
            if (!ValidState(problem, next)) return null;
            if (EdgeSwap(current, next)) return null;
            return next;
        }

        private static bool ApplyOne(EmergencyProblemV2 problem, State state, int robotIndex, ActionV2 action)
        {
            var robot = state.Robots[robotIndex];
            switch (action.Kind)
            {
                case ActionKind.Wait:
                    break;
                case ActionKind.Move:
                    robot.X += action.A;
                    robot.Y += action.B;
                    break;
                case ActionKind.Rotate:
                    if (robot.CarryVehicle < 0) return false;
                    robot.Orientation = robot.Orientation == VehicleOrientation.Horizontal
                        ? VehicleOrientation.Vertical
                        : VehicleOrientation.Horizontal;
                    break;
                case ActionKind.Lift:
                    if (robot.CarryVehicle >= 0 || state.VehicleSlots[action.A] != action.B) return false;
                    robot.Service = ServiceKind.Lift;
                    robot.ServiceRemaining = problem.Timing.LiftServiceTicks - 1;
                    robot.PendingVehicle = action.A;
                    robot.PendingSlot = action.B;
                    if (robot.ServiceRemaining == 0)
                        CompleteService(problem, state, robotIndex, ref robot);
                    break;
                case ActionKind.Drop:
                    if (robot.CarryVehicle < 0 || SlotOccupied(state, action.A)) return false;
                    robot.Service = ServiceKind.Drop;
                    robot.ServiceRemaining = problem.Timing.DropServiceTicks - 1;
                    robot.PendingVehicle = robot.CarryVehicle;
                    robot.PendingSlot = action.A;
                    if (robot.ServiceRemaining == 0)
                        CompleteService(problem, state, robotIndex, ref robot);
                    break;
                case ActionKind.ContinueService:
                    if (robot.Service == ServiceKind.None || robot.ServiceRemaining <= 0) return false;
                    robot.ServiceRemaining--;
                    if (robot.ServiceRemaining == 0)
                        CompleteService(problem, state, robotIndex, ref robot);
                    break;
                default:
                    return false;
            }
            state.Robots[robotIndex] = robot;
            return true;
        }

        private static void CompleteService(
            EmergencyProblemV2 problem, State state, int robotIndex, ref RobotState robot)
        {
            if (robot.Service == ServiceKind.Lift)
            {
                robot.CarryVehicle = robot.PendingVehicle;
                robot.Orientation = problem.Slots[robot.PendingSlot].Pose.Orientation;
                state.VehicleSlots[robot.PendingVehicle] = -1 - robotIndex;
            }
            else if (robot.Service == ServiceKind.Drop)
            {
                state.VehicleSlots[robot.PendingVehicle] = robot.PendingSlot;
                robot.CarryVehicle = -1;
                robot.Orientation = VehicleOrientation.Horizontal;
            }
            robot.Service = ServiceKind.None;
            robot.PendingVehicle = -1;
            robot.PendingSlot = -1;
        }

        private static bool ValidState(EmergencyProblemV2 problem, State state)
        {
            // 적재 차량 방향은 리프트 직후 원래 슬롯 방향이어야 한다.
            for (int r = 0; r < 2; r++)
            {
                var robot = state.Robots[r];
                if (robot.Service == ServiceKind.Lift && robot.CarryVehicle >= 0) return false;
                if (robot.Service == ServiceKind.Drop && robot.CarryVehicle < 0) return false;
                if (robot.CarryVehicle >= 0 && state.VehicleSlots[robot.CarryVehicle] != -1 - r)
                    return false;
                if (robot.CarryVehicle < 0)
                {
                    if (!problem.IsFloor(robot.X, robot.Y)) return false;
                }
                else if (!problem.PoseFits(new VehiclePose(robot.X, robot.Y, robot.Orientation)))
                    return false;
            }

            var parkedCells = new HashSet<(int, int)>();
            foreach (var pose in problem.FixedVehiclePoses)
            {
                parkedCells.Add((pose.X, pose.Y));
                parkedCells.Add(pose.SecondCell);
            }
            var occupiedSlots = new HashSet<int>();
            for (int v = 0; v < state.VehicleSlots.Length; v++)
            {
                int slotIndex = state.VehicleSlots[v];
                if (slotIndex < 0) continue;
                if (!occupiedSlots.Add(slotIndex)) return false;
                var pose = problem.Slots[slotIndex].Pose;
                parkedCells.Add((pose.X, pose.Y));
                parkedCells.Add(pose.SecondCell);
            }

            var r0 = RobotCells(state.Robots[0]);
            var r1 = RobotCells(state.Robots[1]);
            if (r0.Overlaps(r1)) return false;
            for (int r = 0; r < 2; r++)
            {
                var robot = state.Robots[r];
                if (robot.CarryVehicle < 0) continue; // 빈 AGV는 주차 차량 하부 진입 가능
                if (RobotCells(robot).Overlaps(parkedCells)) return false;
            }
            return true;
        }

        private static bool EdgeSwap(State current, State next)
        {
            var c0 = RobotCells(current.Robots[0]);
            var c1 = RobotCells(current.Robots[1]);
            var n0 = RobotCells(next.Robots[0]);
            var n1 = RobotCells(next.Robots[1]);
            return n0.Overlaps(c1) && n1.Overlaps(c0);
        }

        private static HashSet<(int, int)> RobotCells(RobotState robot)
        {
            var cells = new HashSet<(int, int)> { (robot.X, robot.Y) };
            if (robot.CarryVehicle >= 0)
            {
                var second = new VehiclePose(robot.X, robot.Y, robot.Orientation).SecondCell;
                cells.Add(second);
            }
            return cells;
        }

        private static bool IsGoal(EmergencyProblemV2 problem, State state)
        {
            for (int v = 0; v < state.VehicleSlots.Length; v++)
            {
                int slot = state.VehicleSlots[v];
                if (slot < 0 || problem.Slots[slot].Kind != SlotKind.Staging) return false;
            }
            foreach (var robot in state.Robots)
                foreach (var cell in RobotCells(robot))
                    if (problem.IsClearanceCell(cell.Item1, cell.Item2)) return false;
            return true;
        }

        private static bool SlotOccupied(State state, int slotIndex)
        {
            foreach (int slot in state.VehicleSlots)
                if (slot == slotIndex) return true;
            return false;
        }

        private static bool VehicleClaimed(State state, int vehicle)
        {
            foreach (var robot in state.Robots)
                if (robot.Service == ServiceKind.Lift && robot.PendingVehicle == vehicle) return true;
            return false;
        }

        private static bool SlotClaimed(State state, int slot)
        {
            foreach (var robot in state.Robots)
                if (robot.Service == ServiceKind.Drop && robot.PendingSlot == slot) return true;
            return false;
        }

        private static string Key(State state)
        {
            var sb = new StringBuilder();
            for (int r = 0; r < 2; r++)
            {
                var robot = state.Robots[r];
                sb.Append(robot.X).Append(',').Append(robot.Y).Append(',')
                    .Append(robot.CarryVehicle >= 0 ? (int)robot.Orientation : 0).Append(',')
                    .Append(robot.CarryVehicle).Append(',')
                    .Append((int)robot.Service).Append(',').Append(robot.ServiceRemaining).Append(',')
                    .Append(robot.PendingVehicle).Append(',').Append(robot.PendingSlot).Append('|');
            }
            foreach (int slot in state.VehicleSlots) sb.Append(slot).Append(',');
            return sb.ToString();
        }

        private static void Reconstruct(
            string startKey, string goalKey, Dictionary<string, Parent> parents, List<string> output)
        {
            var reversed = new List<string>();
            string key = goalKey;
            while (key != startKey && parents.TryGetValue(key, out var parent))
            {
                reversed.Add(parent.JointAction);
                key = parent.PreviousKey;
            }
            reversed.Reverse();
            output.AddRange(reversed);
        }

        private sealed class SearchHeap
        {
            public readonly struct Node
            {
                public long Score { get; }
                public int Depth { get; }
                public int Sequence { get; }
                public State State { get; }

                public Node(long score, int depth, int sequence, State state)
                {
                    Score = score;
                    Depth = depth;
                    Sequence = sequence;
                    State = state;
                }
            }

            private readonly List<Node> _items = new List<Node>();
            private int _sequence;
            public int Count => _items.Count;

            public void Push(long score, int depth, State state)
            {
                _items.Add(new Node(score, depth, _sequence++, state));
                int i = _items.Count - 1;
                while (i > 0)
                {
                    int p = (i - 1) / 2;
                    if (!Less(_items[i], _items[p])) break;
                    (_items[i], _items[p]) = (_items[p], _items[i]);
                    i = p;
                }
            }

            public Node Pop()
            {
                var top = _items[0];
                var last = _items[_items.Count - 1];
                _items.RemoveAt(_items.Count - 1);
                if (_items.Count == 0) return top;
                _items[0] = last;
                int i = 0;
                while (true)
                {
                    int left = i * 2 + 1, right = left + 1, smallest = i;
                    if (left < _items.Count && Less(_items[left], _items[smallest])) smallest = left;
                    if (right < _items.Count && Less(_items[right], _items[smallest])) smallest = right;
                    if (smallest == i) break;
                    (_items[i], _items[smallest]) = (_items[smallest], _items[i]);
                    i = smallest;
                }
                return top;
            }

            private static bool Less(Node a, Node b)
            {
                if (a.Score != b.Score) return a.Score < b.Score;
                if (a.Depth != b.Depth) return a.Depth < b.Depth;
                return a.Sequence < b.Sequence;
            }
        }
    }
}
