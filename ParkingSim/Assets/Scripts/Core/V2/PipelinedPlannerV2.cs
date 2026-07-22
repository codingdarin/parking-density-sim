using System;
using System.Collections.Generic;
using System.Linq;

namespace ParkingSim.Core.V2
{
    public sealed class PipelinedMissionV2
    {
        public int RobotIndex { get; set; }
        public int VehicleIndex { get; set; }
        public int DestinationSlot { get; set; }
        public int StartTick { get; set; }
        public int LiftTick { get; set; }
        public int DropTick { get; set; }
    }

    public sealed class TimedRobotStateV2
    {
        public int Tick { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public bool Carrying { get; set; }
        public int VehicleIndex { get; set; }
        public VehicleOrientation Orientation { get; set; }
    }

    public sealed class PipelinedPlanResultV2
    {
        public bool Success { get; set; }
        public string FailReason { get; set; }
        public int Ticks { get; set; }
        public int ExpandedStates { get; set; }
        public bool PhysicallyValid { get; set; }
        public int[] FinalVehicleSlots { get; set; }
        public List<PipelinedMissionV2> Missions { get; } = new List<PipelinedMissionV2>();
        public List<TimedRobotStateV2>[] RobotTimelines { get; } =
            { new List<TimedRobotStateV2>(), new List<TimedRobotStateV2>() };
    }

    /// <summary>
    /// 차량-적치면 임무를 작은 시공간 A*로 순차 확정하되, 각 로봇의 전역 시간축은 겹쳐 둔다.
    /// 창 전체 완료 장벽 없이 먼저 빈 로봇이 다음 임무를 시작하는 운영 후보다.
    /// 완전 탐색은 아니며 우선순위에 따라 거짓 실패할 수 있다.
    /// </summary>
    public static class PipelinedPrioritizedPlannerV2
    {
        private sealed class VehicleSchedule
        {
            public VehiclePose Source;
            public int LiftTick = int.MaxValue;
            public VehiclePose Destination;
            public int DropTick = int.MaxValue;
            public bool Planned;
        }

        private readonly struct SearchState : IEquatable<SearchState>
        {
            public int X { get; }
            public int Y { get; }
            public VehicleOrientation Orientation { get; }
            public int Tick { get; }

            public SearchState(int x, int y, VehicleOrientation orientation, int tick)
            {
                X = x;
                Y = y;
                Orientation = orientation;
                Tick = tick;
            }

            public bool Equals(SearchState other) =>
                X == other.X && Y == other.Y && Orientation == other.Orientation && Tick == other.Tick;
            public override bool Equals(object obj) => obj is SearchState other && Equals(other);
            public override int GetHashCode() => (((X * 397) ^ Y) * 31 ^ (int)Orientation) * 397 ^ Tick;
        }

        private sealed class MissionDraft
        {
            public int Vehicle;
            public int Destination;
            public int LiftTick;
            public int DropTick;
            public int Expansions;
            public List<TimedRobotStateV2> States;
        }

        private sealed class TemporalReservations
        {
            private readonly HashSet<(int X, int Y, int T)> _vertices = new HashSet<(int, int, int)>();
            private readonly HashSet<(int Fx, int Fy, int Tx, int Ty, int T)> _edges =
                new HashSet<(int, int, int, int, int)>();
            private readonly Dictionary<(int X, int Y), int> _permanent =
                new Dictionary<(int, int), int>();

            public bool TransitionFree(
                IReadOnlyList<(int X, int Y)> from,
                IReadOnlyList<(int X, int Y)> to,
                int departureTick)
            {
                int arrival = departureTick + 1;
                foreach (var cell in to)
                {
                    if (_vertices.Contains((cell.X, cell.Y, arrival))) return false;
                    if (_permanent.TryGetValue(cell, out int start) && arrival >= start) return false;
                }
                int pairs = Math.Min(from.Count, to.Count);
                for (int i = 0; i < pairs; i++)
                    if (_edges.Contains((to[i].X, to[i].Y, from[i].X, from[i].Y, departureTick)))
                        return false;
                return true;
            }

            public void Reserve(IReadOnlyList<TimedRobotStateV2> states)
            {
                for (int i = 0; i < states.Count; i++)
                {
                    TimedRobotStateV2 state = states[i];
                    foreach (var cell in Cells(state)) _vertices.Add((cell.X, cell.Y, state.Tick));
                    if (i == 0) continue;
                    TimedRobotStateV2 previous = states[i - 1];
                    var from = Cells(previous);
                    var to = Cells(state);
                    int pairs = Math.Min(from.Count, to.Count);
                    for (int c = 0; c < pairs; c++)
                        _edges.Add((from[c].X, from[c].Y, to[c].X, to[c].Y, previous.Tick));
                }
            }

            public void ReservePermanent(TimedRobotStateV2 state)
            {
                foreach (var cell in Cells(state)) _permanent[cell] = state.Tick;
            }
        }

        public static PipelinedPlanResultV2 Solve(
            EmergencyProblemV2 problem,
            int maxTick = 2000,
            int maxExpansionsPerPath = 200000)
        {
            if (problem == null) throw new ArgumentNullException(nameof(problem));
            const int maxHighLevelCandidates = 1024;
            var vehicles = Enumerable.Range(0, problem.VehicleCount).ToArray();
            var staging = Enumerable.Range(0, problem.Slots.Count)
                .Where(s => problem.Slots[s].Kind == SlotKind.Staging).ToArray();
            var vehicleOrders = Permutations(vehicles, vehicles.Length, maxHighLevelCandidates);
            var destinationOrders = Permutations(staging, vehicles.Length, maxHighLevelCandidates);

            PipelinedPlanResultV2 best = null;
            PipelinedPlanResultV2 lastFailure = null;
            int totalExpanded = 0;
            int tried = 0;
            long product = (long)vehicleOrders.Count * destinationOrders.Count;
            if (product <= maxHighLevelCandidates)
            {
                foreach (int[] vehicleOrder in vehicleOrders)
                {
                    foreach (int[] destinationOrder in destinationOrders)
                    {
                        EvaluateConfiguration(vehicleOrder, destinationOrder);
                    }
                }
            }
            else
            {
                // 큰 문제는 첫 차량순서 하나에 목적지 조합이 편중되지 않도록 두 목록을 서로 다른 보폭으로 순회.
                for (int k = 0; k < maxHighLevelCandidates; k++)
                    EvaluateConfiguration(
                        vehicleOrders[k % vehicleOrders.Count],
                        destinationOrders[(k * 997) % destinationOrders.Count]);
            }

            PipelinedPlanResultV2 result = best ?? lastFailure ?? new PipelinedPlanResultV2
            {
                FailReason = "고수준 차량/목적지 후보가 없음",
            };
            result.ExpandedStates = totalExpanded;
            return result;

            void EvaluateConfiguration(int[] vehicleOrder, int[] destinationOrder)
            {
                tried++;
                PipelinedPlanResultV2 candidate = SolveConfigured(
                    problem, vehicleOrder, destinationOrder, maxTick, maxExpansionsPerPath);
                totalExpanded += candidate.ExpandedStates;
                if (!candidate.Success)
                {
                    lastFailure = candidate;
                    return;
                }
                if (best == null || candidate.Ticks < best.Ticks) best = candidate;
            }
        }

        private static PipelinedPlanResultV2 SolveConfigured(
            EmergencyProblemV2 problem,
            IReadOnlyList<int> vehicleOrder,
            IReadOnlyList<int> destinationOrder,
            int maxTick,
            int maxExpansionsPerPath)
        {
            var result = new PipelinedPlanResultV2();
            if (problem.StagingCapacity < problem.VehicleCount)
            {
                result.FailReason = $"적치 용량 부족: 차량 {problem.VehicleCount}대 > 슬롯 {problem.StagingCapacity}면";
                return result;
            }

            var schedules = new VehicleSchedule[problem.VehicleCount];
            for (int v = 0; v < schedules.Length; v++)
                schedules[v] = new VehicleSchedule
                {
                    Source = problem.Slots[problem.InitialVehicleSlots[v]].Pose,
                };

            var reservations = new TemporalReservations();
            var robotStates = new TimedRobotStateV2[2];
            for (int r = 0; r < 2; r++)
            {
                robotStates[r] = NewState(0, problem.RobotStarts[r].X, problem.RobotStarts[r].Y,
                    false, -1, VehicleOrientation.Horizontal);
                result.RobotTimelines[r].Add(robotStates[r]);
                reservations.Reserve(new[] { robotStates[r] });
            }

            var remaining = new HashSet<int>(Enumerable.Range(0, problem.VehicleCount));
            var freeDestinations = new HashSet<int>(Enumerable.Range(0, problem.Slots.Count)
                .Where(s => problem.Slots[s].Kind == SlotKind.Staging));

            while (remaining.Count > 0)
            {
                int robot = robotStates[0].Tick <= robotStates[1].Tick ? 0 : 1;
                int missionIndex = result.Missions.Count;
                int forcedVehicle = vehicleOrder[missionIndex];
                int forcedDestination = destinationOrder[missionIndex];
                var options = new List<(int Vehicle, int Destination)>();
                if (remaining.Contains(forcedVehicle) && freeDestinations.Contains(forcedDestination))
                    options.Add((forcedVehicle, forcedDestination));

                MissionDraft selected = null;
                foreach (var option in options)
                {
                    MissionDraft draft = PlanMission(
                        problem, robotStates[robot], robot, option.Vehicle, option.Destination,
                        schedules, reservations, maxTick, maxExpansionsPerPath,
                        out int attemptedExpansions);
                    result.ExpandedStates += attemptedExpansions;
                    if (draft != null)
                    {
                        selected = draft;
                        break;
                    }
                }
                if (selected == null)
                {
                    result.FailReason = $"로봇{robot + 1} t={robotStates[robot].Tick}에서 남은 임무 경로 없음";
                    return result;
                }

                reservations.Reserve(selected.States);
                AppendWithoutDuplicate(result.RobotTimelines[robot], selected.States);
                robotStates[robot] = selected.States[selected.States.Count - 1];
                schedules[selected.Vehicle].LiftTick = selected.LiftTick;
                schedules[selected.Vehicle].DropTick = selected.DropTick;
                schedules[selected.Vehicle].Destination = problem.Slots[selected.Destination].Pose;
                schedules[selected.Vehicle].Planned = true;
                remaining.Remove(selected.Vehicle);
                freeDestinations.Remove(selected.Destination);
                result.Missions.Add(new PipelinedMissionV2
                {
                    RobotIndex = robot,
                    VehicleIndex = selected.Vehicle,
                    DestinationSlot = selected.Destination,
                    StartTick = selected.States[0].Tick,
                    LiftTick = selected.LiftTick,
                    DropTick = selected.DropTick,
                });
            }

            // exact 기준과 동일하게 두 로봇 모두 확보구간 밖으로 빠져나와야 완료다.
            foreach (int robot in new[] { 0, 1 }.OrderBy(r => robotStates[r].Tick))
            {
                if (problem.IsClearanceCell(robotStates[robot].X, robotStates[robot].Y))
                {
                    List<SearchState> exit = PlanNearestExit(
                        problem, robotStates[robot], reservations, maxTick, maxExpansionsPerPath,
                        out int expansions);
                    result.ExpandedStates += expansions;
                    if (exit == null)
                    {
                        result.FailReason = $"로봇{robot + 1} 확보구간 이탈 경로 없음";
                        return result;
                    }
                    var exitStates = exit.Select(s => NewState(
                        s.Tick, s.X, s.Y, false, -1, VehicleOrientation.Horizontal)).ToList();
                    reservations.Reserve(exitStates);
                    AppendWithoutDuplicate(result.RobotTimelines[robot], exitStates);
                    robotStates[robot] = exitStates[exitStates.Count - 1];
                }
                reservations.ReservePermanent(robotStates[robot]);
            }

            result.Ticks = Math.Max(robotStates[0].Tick, robotStates[1].Tick);
            result.FinalVehicleSlots = new int[problem.VehicleCount];
            foreach (PipelinedMissionV2 mission in result.Missions)
                result.FinalVehicleSlots[mission.VehicleIndex] = mission.DestinationSlot;
            result.PhysicallyValid = Validate(problem, result, schedules);
            result.Success = result.PhysicallyValid;
            if (!result.Success) result.FailReason = "생성 경로의 재생 물리 검증 실패";
            return result;
        }

        private static MissionDraft PlanMission(
            EmergencyProblemV2 problem,
            TimedRobotStateV2 robot,
            int robotIndex,
            int vehicle,
            int destination,
            VehicleSchedule[] schedules,
            TemporalReservations reservations,
            int maxTick,
            int maxExpansions,
            out int attemptedExpansions)
        {
            attemptedExpansions = 0;
            VehiclePose source = schedules[vehicle].Source;
            VehiclePose target = problem.Slots[destination].Pose;
            if (!StaticLoadedReachable(problem, source, target, vehicle, schedules)) return null;
            int approachExpansions;
            List<SearchState> approach = Search(
                problem,
                new SearchState(robot.X, robot.Y, VehicleOrientation.Horizontal, robot.Tick),
                new VehiclePose(source.X, source.Y, VehicleOrientation.Horizontal),
                loaded: false,
                ignoreVehicle: vehicle,
                schedules,
                reservations,
                maxTick,
                maxExpansions,
                out approachExpansions);
            attemptedExpansions += approachExpansions;
            if (approach == null) return null;

            var states = approach.Select(s => NewState(
                s.Tick, s.X, s.Y, false, -1, VehicleOrientation.Horizontal)).ToList();
            int liftTick = approach[approach.Count - 1].Tick + problem.Timing.LiftServiceTicks;
            for (int tick = approach[approach.Count - 1].Tick + 1; tick <= liftTick; tick++)
            {
                bool carrying = tick == liftTick;
                var next = NewState(tick, source.X, source.Y, carrying,
                    carrying ? vehicle : -1, source.Orientation);
                if (!reservations.TransitionFree(Cells(states[states.Count - 1]), Cells(next), tick - 1))
                    return null;
                if (carrying && !LoadedPoseFree(problem, source, tick, vehicle, schedules)) return null;
                states.Add(next);
            }

            int carryExpansions;
            List<SearchState> carry = Search(
                problem,
                new SearchState(source.X, source.Y, source.Orientation, liftTick),
                target,
                loaded: true,
                ignoreVehicle: vehicle,
                schedules,
                reservations,
                maxTick,
                maxExpansions,
                out carryExpansions);
            attemptedExpansions += carryExpansions;
            if (carry == null) return null;
            for (int i = 1; i < carry.Count; i++)
                states.Add(NewState(carry[i].Tick, carry[i].X, carry[i].Y,
                    true, vehicle, carry[i].Orientation));

            int arrival = carry[carry.Count - 1].Tick;
            int dropTick = arrival + problem.Timing.DropServiceTicks;
            for (int tick = arrival + 1; tick <= dropTick; tick++)
            {
                bool carrying = tick < dropTick;
                var next = NewState(tick, target.X, target.Y, carrying,
                    carrying ? vehicle : -1, target.Orientation);
                if (!reservations.TransitionFree(Cells(states[states.Count - 1]), Cells(next), tick - 1))
                    return null;
                states.Add(next);
            }

            return new MissionDraft
            {
                Vehicle = vehicle,
                Destination = destination,
                LiftTick = liftTick,
                DropTick = dropTick,
                Expansions = approachExpansions + carryExpansions,
                States = states,
            };
        }

        private static List<SearchState> Search(
            EmergencyProblemV2 problem,
            SearchState start,
            VehiclePose goal,
            bool loaded,
            int ignoreVehicle,
            VehicleSchedule[] schedules,
            TemporalReservations reservations,
            int maxTick,
            int maxExpansions,
            out int expansions)
        {
            var open = new SearchHeap();
            var parents = new Dictionary<SearchState, SearchState>();
            var seen = new HashSet<SearchState> { start };
            open.Push(Heuristic(start, goal, loaded), start);
            expansions = 0;

            while (open.Count > 0 && expansions < maxExpansions)
            {
                SearchState current = open.Pop();
                expansions++;
                if (current.X == goal.X && current.Y == goal.Y &&
                    (!loaded || current.Orientation == goal.Orientation))
                    return Reconstruct(parents, current);
                if (current.Tick >= maxTick) continue;

                foreach (SearchState next in NextStates(current, loaded))
                {
                    var nextCells = Cells(next, loaded);
                    if (!reservations.TransitionFree(Cells(current, loaded), nextCells, current.Tick)) continue;
                    if (loaded)
                    {
                        var pose = new VehiclePose(next.X, next.Y, next.Orientation);
                        if (!problem.PoseFits(pose) ||
                            !LoadedPoseFree(problem, pose, next.Tick, ignoreVehicle, schedules)) continue;
                    }
                    else if (!problem.IsFloor(next.X, next.Y)) continue;
                    if (!seen.Add(next)) continue;
                    parents[next] = current;
                    int g = next.Tick - start.Tick;
                    open.Push(g + Heuristic(next, goal, loaded), next);
                }
            }
            return null;
        }

        private static List<SearchState> PlanNearestExit(
            EmergencyProblemV2 problem,
            TimedRobotStateV2 start,
            TemporalReservations reservations,
            int maxTick,
            int maxExpansions,
            out int expansions)
        {
            var candidates = new List<(int X, int Y)>();
            for (int x = 0; x < problem.Width; x++)
                for (int y = 0; y < problem.Height; y++)
                    if (problem.IsFloor(x, y) && !problem.IsClearanceCell(x, y)) candidates.Add((x, y));
            candidates = candidates.OrderBy(c => Math.Abs(c.X - start.X) + Math.Abs(c.Y - start.Y)).ToList();
            expansions = 0;
            foreach (var cell in candidates)
            {
                List<SearchState> path = Search(
                    problem,
                    new SearchState(start.X, start.Y, VehicleOrientation.Horizontal, start.Tick),
                    new VehiclePose(cell.X, cell.Y, VehicleOrientation.Horizontal),
                    false, -1, new VehicleSchedule[0], reservations,
                    maxTick, maxExpansions, out int used);
                expansions += used;
                if (path != null) return path;
            }
            return null;
        }

        private static bool LoadedPoseFree(
            EmergencyProblemV2 problem,
            VehiclePose pose,
            int tick,
            int ignoreVehicle,
            VehicleSchedule[] schedules)
        {
            foreach (VehiclePose fixedPose in problem.FixedVehiclePoses)
                if (Overlaps(pose, fixedPose)) return false;
            for (int v = 0; v < schedules.Length; v++)
            {
                if (v == ignoreVehicle) continue;
                VehicleSchedule schedule = schedules[v];
                if (tick < schedule.LiftTick)
                {
                    if (Overlaps(pose, schedule.Source)) return false;
                }
                else if (schedule.Planned && tick >= schedule.DropTick && Overlaps(pose, schedule.Destination))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool StaticLoadedReachable(
            EmergencyProblemV2 problem,
            VehiclePose start,
            VehiclePose goal,
            int ignoreVehicle,
            VehicleSchedule[] schedules)
        {
            var blocked = new HashSet<(int X, int Y)>();
            foreach (VehiclePose fixedPose in problem.FixedVehiclePoses)
            {
                blocked.Add((fixedPose.X, fixedPose.Y));
                blocked.Add(fixedPose.SecondCell);
            }
            for (int vehicle = 0; vehicle < schedules.Length; vehicle++)
            {
                if (vehicle == ignoreVehicle || schedules[vehicle].Planned) continue;
                VehiclePose pose = schedules[vehicle].Source;
                blocked.Add((pose.X, pose.Y));
                blocked.Add(pose.SecondCell);
            }

            var queue = new Queue<VehiclePose>();
            var seen = new HashSet<VehiclePose> { start };
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                VehiclePose current = queue.Dequeue();
                if (current.Equals(goal)) return true;
                foreach (VehiclePose next in StaticNeighbors(current))
                {
                    if (!problem.PoseFits(next) || PoseOverlaps(next, blocked) || !seen.Add(next)) continue;
                    queue.Enqueue(next);
                }
            }
            return false;
        }

        private static IEnumerable<VehiclePose> StaticNeighbors(VehiclePose pose)
        {
            yield return pose.Translate(1, 0);
            yield return pose.Translate(-1, 0);
            yield return pose.Translate(0, 1);
            yield return pose.Translate(0, -1);
            yield return pose.Rotate();
        }

        private static bool PoseOverlaps(VehiclePose pose, ISet<(int X, int Y)> blocked)
        {
            return blocked.Contains((pose.X, pose.Y)) || blocked.Contains(pose.SecondCell);
        }

        private static IEnumerable<SearchState> NextStates(SearchState current, bool loaded)
        {
            int tick = current.Tick + 1;
            yield return new SearchState(current.X, current.Y, current.Orientation, tick);
            yield return new SearchState(current.X + 1, current.Y, current.Orientation, tick);
            yield return new SearchState(current.X - 1, current.Y, current.Orientation, tick);
            yield return new SearchState(current.X, current.Y + 1, current.Orientation, tick);
            yield return new SearchState(current.X, current.Y - 1, current.Orientation, tick);
            if (loaded)
                yield return new SearchState(current.X, current.Y,
                    current.Orientation == VehicleOrientation.Horizontal
                        ? VehicleOrientation.Vertical
                        : VehicleOrientation.Horizontal, tick);
        }

        private static int Heuristic(SearchState state, VehiclePose goal, bool loaded)
        {
            int value = Math.Abs(state.X - goal.X) + Math.Abs(state.Y - goal.Y);
            if (loaded && state.Orientation != goal.Orientation) value++;
            return value;
        }

        private static int Estimate(
            TimedRobotStateV2 robot,
            VehiclePose source,
            VehiclePose destination,
            OperationTimingV2 timing)
        {
            int approach = Math.Abs(robot.X - source.X) + Math.Abs(robot.Y - source.Y);
            int carry = Math.Abs(source.X - destination.X) + Math.Abs(source.Y - destination.Y);
            if (source.Orientation != destination.Orientation) carry++;
            return approach + carry + timing.LiftServiceTicks + timing.DropServiceTicks;
        }

        private static List<int[]> Permutations(int[] items, int length, int limit)
        {
            var output = new List<int[]>();
            var current = new int[length];
            var used = new bool[items.Length];
            BuildPermutations(items, length, limit, 0, current, used, output);
            return output;
        }

        private static void BuildPermutations(
            int[] items,
            int length,
            int limit,
            int depth,
            int[] current,
            bool[] used,
            List<int[]> output)
        {
            if (output.Count >= limit) return;
            if (depth == length)
            {
                output.Add((int[])current.Clone());
                return;
            }
            for (int i = 0; i < items.Length && output.Count < limit; i++)
            {
                if (used[i]) continue;
                used[i] = true;
                current[depth] = items[i];
                BuildPermutations(items, length, limit, depth + 1, current, used, output);
                used[i] = false;
            }
        }

        private static List<SearchState> Reconstruct(
            Dictionary<SearchState, SearchState> parents,
            SearchState end)
        {
            var path = new List<SearchState> { end };
            while (parents.TryGetValue(path[path.Count - 1], out SearchState previous)) path.Add(previous);
            path.Reverse();
            return path;
        }

        private static bool Validate(
            EmergencyProblemV2 problem,
            PipelinedPlanResultV2 result,
            VehicleSchedule[] schedules)
        {
            if (result.FinalVehicleSlots == null ||
                result.FinalVehicleSlots.Distinct().Count() != problem.VehicleCount) return false;
            for (int v = 0; v < schedules.Length; v++)
                if (!schedules[v].Planned || schedules[v].DropTick > result.Ticks) return false;

            for (int tick = 0; tick <= result.Ticks; tick++)
            {
                TimedRobotStateV2 r0 = StateAt(result.RobotTimelines[0], tick);
                TimedRobotStateV2 r1 = StateAt(result.RobotTimelines[1], tick);
                if (Cells(r0).Intersect(Cells(r1)).Any()) return false;
                foreach (TimedRobotStateV2 robot in new[] { r0, r1 })
                {
                    if (!robot.Carrying) continue;
                    VehiclePose pose = new VehiclePose(robot.X, robot.Y, robot.Orientation);
                    if (!problem.PoseFits(pose) ||
                        !LoadedPoseFree(problem, pose, tick, robot.VehicleIndex, schedules)) return false;
                }
                if (tick == 0) continue;
                TimedRobotStateV2 p0 = StateAt(result.RobotTimelines[0], tick - 1);
                TimedRobotStateV2 p1 = StateAt(result.RobotTimelines[1], tick - 1);
                if (Cells(r0).Intersect(Cells(p1)).Any() && Cells(r1).Intersect(Cells(p0)).Any()) return false;
            }
            TimedRobotStateV2 end0 = StateAt(result.RobotTimelines[0], result.Ticks);
            TimedRobotStateV2 end1 = StateAt(result.RobotTimelines[1], result.Ticks);
            return !problem.IsClearanceCell(end0.X, end0.Y) &&
                   !problem.IsClearanceCell(end1.X, end1.Y);
        }

        private static TimedRobotStateV2 StateAt(List<TimedRobotStateV2> timeline, int tick)
        {
            for (int i = timeline.Count - 1; i >= 0; i--)
                if (timeline[i].Tick <= tick) return timeline[i];
            return timeline[0];
        }

        private static bool Overlaps(VehiclePose a, VehiclePose b)
        {
            var ac = new[] { (a.X, a.Y), a.SecondCell };
            var bc = new[] { (b.X, b.Y), b.SecondCell };
            return ac.Intersect(bc).Any();
        }

        private static List<(int X, int Y)> Cells(SearchState state, bool loaded)
        {
            var cells = new List<(int, int)> { (state.X, state.Y) };
            if (loaded) cells.Add(new VehiclePose(state.X, state.Y, state.Orientation).SecondCell);
            return cells;
        }

        private static List<(int X, int Y)> Cells(TimedRobotStateV2 state)
        {
            var cells = new List<(int, int)> { (state.X, state.Y) };
            if (state.Carrying)
                cells.Add(new VehiclePose(state.X, state.Y, state.Orientation).SecondCell);
            return cells;
        }

        private static TimedRobotStateV2 NewState(
            int tick, int x, int y, bool carrying, int vehicle, VehicleOrientation orientation)
        {
            return new TimedRobotStateV2
            {
                Tick = tick,
                X = x,
                Y = y,
                Carrying = carrying,
                VehicleIndex = vehicle,
                Orientation = orientation,
            };
        }

        private static void AppendWithoutDuplicate(
            List<TimedRobotStateV2> target,
            List<TimedRobotStateV2> source)
        {
            int start = target.Count > 0 && source.Count > 0 &&
                        target[target.Count - 1].Tick == source[0].Tick ? 1 : 0;
            for (int i = start; i < source.Count; i++) target.Add(source[i]);
        }

        private sealed class SearchHeap
        {
            private readonly List<(int Score, int Seq, SearchState State)> _items =
                new List<(int, int, SearchState)>();
            private int _sequence;
            public int Count => _items.Count;

            public void Push(int score, SearchState state)
            {
                _items.Add((score, _sequence++, state));
                int i = _items.Count - 1;
                while (i > 0)
                {
                    int p = (i - 1) / 2;
                    if (Less(_items[i], _items[p]))
                    {
                        (_items[i], _items[p]) = (_items[p], _items[i]);
                        i = p;
                    }
                    else break;
                }
            }

            public SearchState Pop()
            {
                SearchState top = _items[0].State;
                var last = _items[_items.Count - 1];
                _items.RemoveAt(_items.Count - 1);
                if (_items.Count > 0)
                {
                    _items[0] = last;
                    int i = 0;
                    while (true)
                    {
                        int left = i * 2 + 1, right = left + 1, best = i;
                        if (left < _items.Count && Less(_items[left], _items[best])) best = left;
                        if (right < _items.Count && Less(_items[right], _items[best])) best = right;
                        if (best == i) break;
                        (_items[i], _items[best]) = (_items[best], _items[i]);
                        i = best;
                    }
                }
                return top;
            }

            private static bool Less(
                (int Score, int Seq, SearchState State) a,
                (int Score, int Seq, SearchState State) b) =>
                a.Score != b.Score ? a.Score < b.Score : a.Seq < b.Seq;
        }
    }
}
