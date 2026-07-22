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
        private enum ActionKind : byte { Wait, Move, Rotate, Lift, Drop }

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
            if (robot.CarryVehicle < 0)
            {
                foreach (var d in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    if (problem.IsFloor(robot.X + d.Item1, robot.Y + d.Item2))
                        actions.Add(new ActionV2(ActionKind.Move, d.Item1, d.Item2, $"move({d.Item1},{d.Item2})"));

                for (int v = 0; v < state.VehicleSlots.Length; v++)
                {
                    int slotIndex = state.VehicleSlots[v];
                    if (slotIndex < 0) continue;
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
                    if (problem.Slots[s].Kind != SlotKind.Staging || SlotOccupied(state, s)) continue;
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
                    robot.CarryVehicle = action.A;
                    robot.Orientation = problem.Slots[action.B].Pose.Orientation;
                    state.VehicleSlots[action.A] = -1 - robotIndex;
                    break;
                case ActionKind.Drop:
                    if (robot.CarryVehicle < 0 || SlotOccupied(state, action.A)) return false;
                    state.VehicleSlots[robot.CarryVehicle] = action.A;
                    robot.CarryVehicle = -1;
                    robot.Orientation = VehicleOrientation.Horizontal;
                    break;
                default:
                    return false;
            }
            state.Robots[robotIndex] = robot;
            return true;
        }

        private static bool ValidState(EmergencyProblemV2 problem, State state)
        {
            // 적재 차량 방향은 리프트 직후 원래 슬롯 방향이어야 한다.
            for (int r = 0; r < 2; r++)
            {
                var robot = state.Robots[r];
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

        private static string Key(State state)
        {
            var sb = new StringBuilder();
            for (int r = 0; r < 2; r++)
            {
                var robot = state.Robots[r];
                sb.Append(robot.X).Append(',').Append(robot.Y).Append(',')
                    .Append(robot.CarryVehicle >= 0 ? (int)robot.Orientation : 0).Append(',')
                    .Append(robot.CarryVehicle).Append('|');
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
    }
}
