using System;
using System.Collections.Generic;
using System.Linq;

namespace ParkingSim.Core.V2
{
    public enum EmergencyAccessFailureV2
    {
        None,
        InvalidInput,
        NoCenterline,
        InsufficientWidth,
        FixedObstruction,
        InsufficientStagingCapacity,
        PhysicalPlanningFailed,
        SearchLimitReached,
    }

    public sealed class EmergencyAccessRouteGenerationOptionsV2
    {
        public int MaxRoutes { get; set; } = 6;
        public int MaxCenterlineAttempts { get; set; } = 32;
        public int MaxSearchExpansions { get; set; } = 20000;
        public int DiversificationPenalty { get; set; } = 8;
        public double DuplicateSimilarityThreshold { get; set; } = 0.85;

        internal string Validate()
        {
            if (MaxRoutes < 1 || MaxRoutes > 8)
                return "상위 후보 수는 1~8이어야 함";
            if (MaxCenterlineAttempts < 1)
                return "중심선 시도 상한은 1 이상이어야 함";
            if (MaxSearchExpansions < 1)
                return "중심선 탐색 상한은 1 이상이어야 함";
            if (DiversificationPenalty < 0)
                return "다양화 패널티는 0 이상이어야 함";
            if (DuplicateSimilarityThreshold <= 0 ||
                DuplicateSimilarityThreshold > 1)
                return "중복 임계값은 0 초과 1 이하여야 함";
            return null;
        }
    }

    public sealed class EmergencyAccessRouteGenerationResultV2
    {
        public bool Success { get; set; }
        public EmergencyAccessFailureV2 Failure { get; set; }
        public string FailReason { get; set; }
        public IReadOnlyList<EmergencyAccessRouteV2> Routes { get; set; }
        public int SearchExpansions { get; set; }
        public int CenterlinesFound { get; set; }
        public int WidthRejected { get; set; }
        public int FixedObstructionRejected { get; set; }
        public int DuplicateRejected { get; set; }
        public bool SearchLimitReached { get; set; }
    }

    public sealed class AutomaticEmergencyAccessPlanResultV2
    {
        public bool Success { get; set; }
        public EmergencyAccessFailureV2 Failure { get; set; }
        public string FailReason { get; set; }
        public EmergencyAccessRouteGenerationResultV2 Generation { get; set; }
        public EmergencyAccessPlanResultV2 Plan { get; set; }
    }

    /// <summary>
    /// 입구와 화재 위치 사이의 중심선을 제한 A*로 반복 생성하고, 각 중심선을
    /// 진행방향 직각으로 한 셀씩 확장해 폭 3셀 접근 후보를 만든다.
    /// 모든 단순 경로를 열거하지 않으며 시도·확장·반환 후보 수가 모두 유한하다.
    /// </summary>
    public static class EmergencyAccessRouteGeneratorV2
    {
        private sealed class RouteDraft
        {
            public (int X, int Y)[] Centerline;
            public (int X, int Y)[] RequiredCells;
        }

        private sealed class SearchNode
        {
            public (int X, int Y) Cell;
            public int Cost;
            public int Priority;
            public long Order;
        }

        private sealed class SearchHeap
        {
            private readonly List<SearchNode> _items = new List<SearchNode>();
            public int Count => _items.Count;

            public void Push(SearchNode node)
            {
                _items.Add(node);
                int index = _items.Count - 1;
                while (index > 0)
                {
                    int parent = (index - 1) / 2;
                    if (!Less(_items[index], _items[parent])) break;
                    SearchNode value = _items[index];
                    _items[index] = _items[parent];
                    _items[parent] = value;
                    index = parent;
                }
            }

            public SearchNode Pop()
            {
                SearchNode result = _items[0];
                int last = _items.Count - 1;
                _items[0] = _items[last];
                _items.RemoveAt(last);
                int index = 0;
                while (true)
                {
                    int left = index * 2 + 1;
                    if (left >= _items.Count) break;
                    int right = left + 1;
                    int best = right < _items.Count && Less(_items[right], _items[left])
                        ? right
                        : left;
                    if (!Less(_items[best], _items[index])) break;
                    SearchNode value = _items[index];
                    _items[index] = _items[best];
                    _items[best] = value;
                    index = best;
                }
                return result;
            }

            private static bool Less(SearchNode left, SearchNode right)
            {
                if (left.Priority != right.Priority)
                    return left.Priority < right.Priority;
                if (left.Cost != right.Cost)
                    return left.Cost < right.Cost;
                return left.Order < right.Order;
            }
        }

        public static EmergencyAccessRouteGenerationResultV2 Generate(
            EmergencyProblemV2 problem,
            (int X, int Y) entranceCell,
            (int X, int Y) fireCell,
            EmergencyAccessRouteGenerationOptionsV2 options = null)
        {
            var result = new EmergencyAccessRouteGenerationResultV2
            {
                Routes = Array.Empty<EmergencyAccessRouteV2>(),
            };
            if (problem == null)
                return Fail(result, EmergencyAccessFailureV2.InvalidInput, "기본 문제가 필요함");
            options = options ?? new EmergencyAccessRouteGenerationOptionsV2();
            string optionError = options.Validate();
            if (optionError != null)
                return Fail(result, EmergencyAccessFailureV2.InvalidInput, optionError);
            if (!problem.IsFloor(entranceCell.X, entranceCell.Y))
                return Fail(result, EmergencyAccessFailureV2.InvalidInput, "입구가 floor 밖임");
            if (!problem.IsFloor(fireCell.X, fireCell.Y))
                return Fail(result, EmergencyAccessFailureV2.InvalidInput, "화재 위치가 floor 밖임");

            var penalties = new Dictionary<(int X, int Y), int>();
            var drafts = new List<RouteDraft>();
            for (int attempt = 0; attempt < options.MaxCenterlineAttempts; attempt++)
            {
                int remaining = options.MaxSearchExpansions - result.SearchExpansions;
                if (remaining <= 0)
                {
                    result.SearchLimitReached = true;
                    break;
                }

                bool limitReached;
                int expansions;
                (int X, int Y)[] centerline = FindCenterline(
                    problem, entranceCell, fireCell, penalties, remaining,
                    out expansions, out limitReached);
                result.SearchExpansions += expansions;
                if (limitReached) result.SearchLimitReached = true;
                if (centerline == null)
                {
                    if (limitReached) break;
                    if (result.CenterlinesFound == 0)
                        return Fail(result, EmergencyAccessFailureV2.NoCenterline,
                            "입구와 화재 위치를 잇는 중심선 경로가 없음");
                    break;
                }

                result.CenterlinesFound++;
                AddPenalties(penalties, centerline, options.DiversificationPenalty);
                (int X, int Y)[] required = ExpandWidthThree(centerline);
                if (required.Any(cell => !problem.IsFloor(cell.X, cell.Y)))
                {
                    result.WidthRejected++;
                    continue;
                }
                if (OverlapsFixedVehicle(problem, required))
                {
                    result.FixedObstructionRejected++;
                    continue;
                }
                if (drafts.Any(existing => Similar(
                        existing.RequiredCells, required,
                        options.DuplicateSimilarityThreshold)))
                {
                    result.DuplicateRejected++;
                    continue;
                }
                drafts.Add(new RouteDraft
                {
                    Centerline = centerline,
                    RequiredCells = required,
                });
            }

            if (drafts.Count == 0)
            {
                if (result.SearchLimitReached)
                    return Fail(result, EmergencyAccessFailureV2.SearchLimitReached,
                        $"중심선 탐색 상한 {options.MaxSearchExpansions}회 도달");
                if (result.FixedObstructionRejected > 0 &&
                    result.FixedObstructionRejected + result.WidthRejected ==
                    result.CenterlinesFound)
                    return Fail(result, EmergencyAccessFailureV2.FixedObstruction,
                        "폭 3셀 후보가 고정 차량에 막혀 접근경로를 만들 수 없음");
                return Fail(result, EmergencyAccessFailureV2.InsufficientWidth,
                    "중심선은 있으나 폭 3셀 연속 확보구간을 만들 수 없음");
            }

            RouteDraft[] selected = drafts
                .OrderBy(draft => draft.Centerline.Length)
                .ThenBy(draft => draft.RequiredCells.Length)
                .ThenBy(draft => Signature(draft.Centerline), StringComparer.Ordinal)
                .Take(options.MaxRoutes)
                .ToArray();
            result.Routes = selected.Select((draft, index) =>
                new EmergencyAccessRouteV2(
                    "auto-route-" + (index + 1).ToString("D2"),
                    entranceCell,
                    fireCell,
                    draft.RequiredCells)).ToArray();
            result.Success = true;
            result.Failure = EmergencyAccessFailureV2.None;
            return result;
        }

        public static AutomaticEmergencyAccessPlanResultV2 Solve(
            EmergencyProblemV2 problem,
            (int X, int Y) entranceCell,
            (int X, int Y) fireCell,
            int activeRobotCount,
            EmergencyAccessRouteGenerationOptionsV2 generationOptions = null,
            int maxHighLevelCandidates = 8,
            int maxTick = 2000,
            int maxExpansionsPerPath = 200000)
        {
            EmergencyAccessRouteGenerationResultV2 generation = Generate(
                problem, entranceCell, fireCell, generationOptions);
            var result = new AutomaticEmergencyAccessPlanResultV2
            {
                Generation = generation,
            };
            if (!generation.Success)
            {
                result.Failure = generation.Failure;
                result.FailReason = generation.FailReason;
                return result;
            }

            EmergencyAccessPlanResultV2 plan = EmergencyAccessPlannerV2.Solve(
                problem,
                generation.Routes,
                activeRobotCount,
                maxHighLevelCandidates,
                maxTick,
                maxExpansionsPerPath);
            result.Plan = plan;
            if (plan.Success)
            {
                result.Success = true;
                result.Failure = EmergencyAccessFailureV2.None;
                return result;
            }

            bool allCapacityFailures = plan.Candidates.Count > 0 &&
                plan.Candidates.All(candidate =>
                    candidate.Scenario != null &&
                    candidate.Scenario.Success &&
                    candidate.Plan != null &&
                    !candidate.Plan.Success &&
                    candidate.Plan.FailReason != null &&
                    candidate.Plan.FailReason.Contains("적치 용량 부족"));
            bool anySearchLimit = plan.Candidates.Any(candidate =>
                candidate.Plan != null &&
                !candidate.Plan.Success &&
                candidate.Plan.FailReason != null &&
                candidate.Plan.FailReason.Contains("상한"));
            result.Failure = allCapacityFailures
                ? EmergencyAccessFailureV2.InsufficientStagingCapacity
                : anySearchLimit
                    ? EmergencyAccessFailureV2.SearchLimitReached
                    : EmergencyAccessFailureV2.PhysicalPlanningFailed;
            result.FailReason = allCapacityFailures
                ? "모든 자동 접근 후보의 적치 용량이 부족함"
                : anySearchLimit
                    ? "자동 접근 후보의 물리 계획 탐색 상한 도달"
                    : "자동 접근 후보의 물리 로봇 계획이 모두 실패함";
            return result;
        }

        private static EmergencyAccessRouteGenerationResultV2 Fail(
            EmergencyAccessRouteGenerationResultV2 result,
            EmergencyAccessFailureV2 failure,
            string reason)
        {
            result.Success = false;
            result.Failure = failure;
            result.FailReason = reason;
            return result;
        }

        private static (int X, int Y)[] FindCenterline(
            EmergencyProblemV2 problem,
            (int X, int Y) start,
            (int X, int Y) target,
            IReadOnlyDictionary<(int X, int Y), int> penalties,
            int maxExpansions,
            out int expansions,
            out bool limitReached)
        {
            var open = new SearchHeap();
            var cost = new Dictionary<(int X, int Y), int> { [start] = 0 };
            var parent = new Dictionary<(int X, int Y), (int X, int Y)>();
            long order = 0;
            open.Push(new SearchNode
            {
                Cell = start,
                Cost = 0,
                Priority = Manhattan(start, target),
                Order = order++,
            });
            expansions = 0;
            limitReached = false;
            while (open.Count > 0)
            {
                if (expansions >= maxExpansions)
                {
                    limitReached = true;
                    return null;
                }
                SearchNode current = open.Pop();
                if (!cost.TryGetValue(current.Cell, out int known) || known != current.Cost)
                    continue;
                expansions++;
                if (current.Cell == target)
                    return Reconstruct(parent, start, target);

                foreach (var next in Neighbors(current.Cell))
                {
                    if (!problem.IsFloor(next.X, next.Y)) continue;
                    int penalty = penalties.TryGetValue(next, out int value) ? value : 0;
                    int nextCost = current.Cost + 1 + penalty;
                    if (cost.TryGetValue(next, out int oldCost) && oldCost <= nextCost)
                        continue;
                    cost[next] = nextCost;
                    parent[next] = current.Cell;
                    open.Push(new SearchNode
                    {
                        Cell = next,
                        Cost = nextCost,
                        Priority = nextCost + Manhattan(next, target),
                        Order = order++,
                    });
                }
            }
            return null;
        }

        private static IEnumerable<(int X, int Y)> Neighbors((int X, int Y) cell)
        {
            yield return (cell.X + 1, cell.Y);
            yield return (cell.X, cell.Y + 1);
            yield return (cell.X, cell.Y - 1);
            yield return (cell.X - 1, cell.Y);
        }

        private static (int X, int Y)[] Reconstruct(
            IReadOnlyDictionary<(int X, int Y), (int X, int Y)> parent,
            (int X, int Y) start,
            (int X, int Y) target)
        {
            var cells = new List<(int X, int Y)> { target };
            (int X, int Y) current = target;
            while (current != start)
            {
                current = parent[current];
                cells.Add(current);
            }
            cells.Reverse();
            return cells.ToArray();
        }

        private static void AddPenalties(
            IDictionary<(int X, int Y), int> penalties,
            IReadOnlyList<(int X, int Y)> centerline,
            int amount)
        {
            for (int i = 1; i + 1 < centerline.Count; i++)
            {
                (int X, int Y) cell = centerline[i];
                penalties[cell] = penalties.TryGetValue(cell, out int value)
                    ? value + amount
                    : amount;
            }
        }

        private static (int X, int Y)[] ExpandWidthThree(
            IReadOnlyList<(int X, int Y)> centerline)
        {
            var required = new HashSet<(int X, int Y)>();
            for (int index = 0; index < centerline.Count; index++)
            {
                (int X, int Y) cell = centerline[index];
                bool horizontal = false;
                bool vertical = false;
                if (index > 0)
                {
                    horizontal |= centerline[index - 1].Y == cell.Y;
                    vertical |= centerline[index - 1].X == cell.X;
                }
                if (index + 1 < centerline.Count)
                {
                    horizontal |= centerline[index + 1].Y == cell.Y;
                    vertical |= centerline[index + 1].X == cell.X;
                }
                if (horizontal)
                    for (int offset = -1; offset <= 1; offset++)
                        required.Add((cell.X, cell.Y + offset));
                if (vertical)
                    for (int offset = -1; offset <= 1; offset++)
                        required.Add((cell.X + offset, cell.Y));
                if (!horizontal && !vertical) required.Add(cell);
            }
            return required.OrderBy(cell => cell.X).ThenBy(cell => cell.Y).ToArray();
        }

        private static bool OverlapsFixedVehicle(
            EmergencyProblemV2 problem,
            IEnumerable<(int X, int Y)> requiredCells)
        {
            var required = new HashSet<(int X, int Y)>(requiredCells);
            foreach (VehiclePose pose in problem.FixedVehiclePoses)
                if (required.Contains((pose.X, pose.Y)) ||
                    required.Contains(pose.SecondCell))
                    return true;
            return false;
        }

        private static bool Similar(
            IEnumerable<(int X, int Y)> leftCells,
            IEnumerable<(int X, int Y)> rightCells,
            double threshold)
        {
            var left = new HashSet<(int X, int Y)>(leftCells);
            var right = new HashSet<(int X, int Y)>(rightCells);
            int intersection = left.Count(cell => right.Contains(cell));
            int union = left.Count + right.Count - intersection;
            return union > 0 && (double)intersection / union >= threshold;
        }

        private static int Manhattan((int X, int Y) left, (int X, int Y) right)
        {
            return Math.Abs(left.X - right.X) + Math.Abs(left.Y - right.Y);
        }

        private static string Signature(IEnumerable<(int X, int Y)> cells)
        {
            return string.Join(";", cells.Select(cell => cell.X + "," + cell.Y));
        }
    }
}
