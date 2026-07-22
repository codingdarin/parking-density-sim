using System;
using System.Collections.Generic;
using System.Linq;

namespace ParkingSim.Core.V2
{
    public sealed class EmergencyAccessRouteV2
    {
        private readonly (int X, int Y)[] _requiredCells;

        public string Name { get; }
        public (int X, int Y) EntranceCell { get; }
        public (int X, int Y) FireCell { get; }
        public IReadOnlyList<(int X, int Y)> RequiredCells => _requiredCells;

        public EmergencyAccessRouteV2(
            string name,
            (int X, int Y) entranceCell,
            (int X, int Y) fireCell,
            IEnumerable<(int X, int Y)> requiredCells)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("경로 이름이 필요함", nameof(name));
            if (requiredCells == null) throw new ArgumentNullException(nameof(requiredCells));
            Name = name;
            EntranceCell = entranceCell;
            FireCell = fireCell;
            _requiredCells = requiredCells.Distinct().ToArray();
            if (_requiredCells.Length == 0)
                throw new ArgumentException("경로 확보 셀이 필요함", nameof(requiredCells));
            var cells = new HashSet<(int X, int Y)>(_requiredCells);
            if (!cells.Contains(EntranceCell) || !cells.Contains(FireCell))
                throw new ArgumentException("입구와 화재 지점은 확보 셀에 포함돼야 함");
            if (!Connected(cells, EntranceCell, FireCell))
                throw new ArgumentException("입구와 화재 지점이 확보 셀로 연결되지 않음");
        }

        private static bool Connected(
            ISet<(int X, int Y)> cells,
            (int X, int Y) start,
            (int X, int Y) target)
        {
            var queue = new Queue<(int X, int Y)>();
            var visited = new HashSet<(int X, int Y)> { start };
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (current == target) return true;
                foreach (var next in new[]
                {
                    (current.X + 1, current.Y), (current.X - 1, current.Y),
                    (current.X, current.Y + 1), (current.X, current.Y - 1),
                })
                {
                    if (cells.Contains(next) && visited.Add(next)) queue.Enqueue(next);
                }
            }
            return false;
        }
    }

    public sealed class EmergencyAccessCandidateResultV2
    {
        public EmergencyAccessRouteV2 Route { get; set; }
        public EmergencyScenarioBuildResultV2 Scenario { get; set; }
        public PipelinedPlanResultV2 Plan { get; set; }
        public bool Success => Scenario != null && Scenario.Success &&
                               Plan != null && Plan.Success && Plan.PhysicallyValid;
    }

    public sealed class EmergencyAccessPlanResultV2
    {
        public bool Success { get; set; }
        public string FailReason { get; set; }
        public EmergencyAccessCandidateResultV2 Selected { get; set; }
        public IReadOnlyList<EmergencyAccessCandidateResultV2> Candidates { get; set; }
    }

    /// <summary>
    /// 미리 정의된 소방차 폭 확보 후보를 각각 물리 계획한 뒤 최초 개통시간이 가장 짧은 경로를 고른다.
    /// 차량 수만 세지 않고 유한 적치·접근기하·시공간 충돌까지 포함한 실제 makespan으로 선택한다.
    /// </summary>
    public static class EmergencyAccessPlannerV2
    {
        public static EmergencyAccessPlanResultV2 Solve(
            EmergencyProblemV2 baseProblem,
            IEnumerable<EmergencyAccessRouteV2> routes,
            int activeRobotCount,
            int maxHighLevelCandidates = 8)
        {
            if (baseProblem == null) throw new ArgumentNullException(nameof(baseProblem));
            if (routes == null) throw new ArgumentNullException(nameof(routes));
            var candidates = new List<EmergencyAccessCandidateResultV2>();
            foreach (EmergencyAccessRouteV2 route in routes)
            {
                var scenario = new EmergencyScenarioV2(
                    "access-route-" + route.Name, route.FireCell, route.RequiredCells).Build(baseProblem);
                PipelinedPlanResultV2 plan = null;
                if (scenario.Success)
                {
                    int requiredVehicles = scenario.SelectedVehicleCount;
                    if (requiredVehicles == 0)
                    {
                        plan = new PipelinedPlanResultV2
                        {
                            Success = true,
                            PhysicallyValid = true,
                            Ticks = 0,
                            FinalVehicleSlots = Array.Empty<int>(),
                        };
                    }
                    else
                    {
                        plan = PipelinedPrioritizedPlannerV2.Solve(
                            scenario.Problem,
                            activeRobotCount: Math.Min(activeRobotCount, requiredVehicles),
                            maxHighLevelCandidates: maxHighLevelCandidates);
                    }
                }
                candidates.Add(new EmergencyAccessCandidateResultV2
                {
                    Route = route,
                    Scenario = scenario,
                    Plan = plan,
                });
            }

            EmergencyAccessCandidateResultV2 selected = candidates
                .Where(candidate => candidate.Success)
                .OrderBy(candidate => candidate.Plan.Ticks)
                .ThenBy(candidate => candidate.Scenario.SelectedVehicleCount)
                .ThenBy(candidate => candidate.Route.RequiredCells.Count)
                .ThenBy(candidate => candidate.Route.Name, StringComparer.Ordinal)
                .FirstOrDefault();
            return new EmergencyAccessPlanResultV2
            {
                Success = selected != null,
                FailReason = selected == null ? "물리적으로 개통 가능한 접근경로가 없음" : null,
                Selected = selected,
                Candidates = candidates,
            };
        }
    }
}
