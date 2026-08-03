using System;
using System.Collections.Generic;
using System.Linq;

namespace ParkingSim.Core.V2
{
    public enum ApartmentFacadeV2 : byte
    {
        CentralRoad,
    }

    public sealed class ApartmentFireIncidentV2
    {
        public int BuildingId { get; }
        public ApartmentFacadeV2 Facade { get; }

        public ApartmentFireIncidentV2(
            int buildingId,
            ApartmentFacadeV2 facade = ApartmentFacadeV2.CentralRoad)
        {
            BuildingId = buildingId;
            Facade = facade;
        }
    }

    public sealed class ApartmentComplexEntranceV2
    {
        public string Name { get; }
        public (int X, int Y) Cell { get; }
        public bool IsPrimary { get; }

        public ApartmentComplexEntranceV2(
            string name, (int X, int Y) cell, bool isPrimary)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("진입구 이름이 필요함", nameof(name));
            Name = name;
            Cell = cell;
            IsPrimary = isPrimary;
        }
    }

    public sealed class FireEngineZoneV2
    {
        private readonly (int X, int Y)[] _cells;

        public string Name { get; }
        public int BuildingId { get; }
        public ApartmentFacadeV2 Facade { get; }
        public (int X, int Y) ApproachCell { get; }
        public IReadOnlyList<(int X, int Y)> Cells => _cells;

        public FireEngineZoneV2(
            string name,
            int buildingId,
            ApartmentFacadeV2 facade,
            (int X, int Y) approachCell,
            IEnumerable<(int X, int Y)> cells)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("전용구역 이름이 필요함", nameof(name));
            if (cells == null) throw new ArgumentNullException(nameof(cells));
            Name = name;
            BuildingId = buildingId;
            Facade = facade;
            ApproachCell = approachCell;
            _cells = cells.Distinct().ToArray();
            if (_cells.Length == 0 || !_cells.Contains(approachCell))
                throw new ArgumentException("전용구역은 접근 셀을 포함해야 함", nameof(cells));
        }
    }

    public sealed class ApartmentBuildingV2
    {
        private readonly (int X, int Y)[] _footprintCells;

        public int Id { get; }
        public IReadOnlyList<(int X, int Y)> FootprintCells => _footprintCells;
        public FireEngineZoneV2 FireEngineZone { get; }

        public ApartmentBuildingV2(
            int id,
            IEnumerable<(int X, int Y)> footprintCells,
            FireEngineZoneV2 fireEngineZone)
        {
            if (footprintCells == null)
                throw new ArgumentNullException(nameof(footprintCells));
            Id = id;
            _footprintCells = footprintCells.Distinct().ToArray();
            FireEngineZone = fireEngineZone ??
                throw new ArgumentNullException(nameof(fireEngineZone));
            if (_footprintCells.Length == 0)
                throw new ArgumentException("건물 풋프린트가 필요함", nameof(footprintCells));
            if (FireEngineZone.BuildingId != id)
                throw new ArgumentException("건물과 전용구역의 동 ID가 다름");
        }
    }

    public sealed class ApartmentComplexScenarioV2
    {
        public EmergencyProblemV2 BaseProblem { get; set; }
        public IReadOnlyList<ApartmentBuildingV2> Buildings { get; set; }
        public IReadOnlyList<ApartmentComplexEntranceV2> Entrances { get; set; }
        public int BlockingVehicleCount { get; set; }

        public ApartmentBuildingV2 FindBuilding(int buildingId)
        {
            return Buildings.FirstOrDefault(building => building.Id == buildingId);
        }
    }

    public sealed class ApartmentComplexAccessAttemptV2
    {
        public ApartmentComplexEntranceV2 Entrance { get; set; }
        public AutomaticEmergencyAccessPlanResultV2 AutomaticPlan { get; set; }
        public bool Success => AutomaticPlan != null && AutomaticPlan.Success;
    }

    public sealed class ApartmentComplexPlanResultV2
    {
        public bool Success { get; set; }
        public EmergencyAccessFailureV2 Failure { get; set; }
        public string FailReason { get; set; }
        public ApartmentFireIncidentV2 Incident { get; set; }
        public FireEngineZoneV2 TargetZone { get; set; }
        public ApartmentComplexAccessAttemptV2 Selected { get; set; }
        public IReadOnlyList<ApartmentComplexAccessAttemptV2> Attempts { get; set; }
    }

    /// <summary>
    /// 2행×4동 합성 단지. 건물 내부 화재와 소방차 도착 종점을 분리하고,
    /// 경로 생성기에는 단지 진입구와 해당 동 전용구역 접근 셀만 전달한다.
    /// </summary>
    public static class ApartmentComplexScenarioFactoryV2
    {
        public const int Width = 59;
        public const int Height = 41;
        public const int BuildingCount = 8;
        public const int MaximumBlockingVehicles = 22;

        public static ApartmentComplexScenarioV2 Build(OperationTimingV2 timing = null)
        {
            return BuildDensity(MaximumBlockingVehicles, timing);
        }

        /// <summary>
        /// placementSeed &lt; 0: 팩토리 고정 누적 순서(합성 기준선).
        /// placementSeed ≥ 0: 시드 주입 순열로 가변 22면 중 N면을 점유 —
        /// 배치 강건성 축(측정정의서 시드 배치 집계).
        /// </summary>
        public static ApartmentComplexScenarioV2 BuildDensity(
            int blockingVehicleCount,
            OperationTimingV2 timing = null,
            int placementSeed = -1)
        {
            if (blockingVehicleCount < 0 ||
                blockingVehicleCount > MaximumBlockingVehicles)
                throw new ArgumentOutOfRangeException(nameof(blockingVehicleCount));
            bool[,] floor = new bool[Width, Height];
            Fill(floor, 0, 41, 0, 1);
            Fill(floor, 0, Width - 1, 2, 4);
            Fill(floor, 0, Width - 1, 16, 20);
            Fill(floor, 0, Width - 1, 36, 38);
            foreach (int centerX in new[] { 3, 16, 29, 42, 55 })
                Fill(floor, centerX - 1, centerX + 1, 0, 38);

            var buildings = new List<ApartmentBuildingV2>();
            int[] centers = { 9, 22, 35, 48 };
            for (int column = 0; column < centers.Length; column++)
            {
                int minX = 5 + column * 13;
                int maxX = 14 + column * 13;
                buildings.Add(CreateBuilding(
                    101 + column, minX, maxX, 21, 34,
                    centers[column], 36, 38, 37));
                buildings.Add(CreateBuilding(
                    105 + column, minX, maxX, 6, 15,
                    centers[column], 2, 4, 3));
            }
            buildings = buildings.OrderBy(building => building.Id).ToList();

            var slots = new List<ParkingSlotV2>();
            foreach (int y in new[] { 17, 19 })
                foreach (int x in new[] { 12, 25, 38, 51 })
                    AddSlot(slots, SlotKind.Blocking, x, y);
            foreach (int x in new[] { 12, 25, 38, 51 })
                AddSlot(slots, SlotKind.Blocking, x, 3);
            foreach (int x in new[] { 12, 25, 38, 51 })
                AddSlot(slots, SlotKind.Blocking, x, 37);
            foreach (int x in new[] { 15, 28, 41 })
                AddSlot(
                    slots,
                    SlotKind.Blocking,
                    x,
                    10,
                    VehicleOrientation.Vertical);
            foreach (int x in new[] { 15, 28, 41 })
                AddSlot(
                    slots,
                    SlotKind.Blocking,
                    x,
                    27,
                    VehicleOrientation.Vertical);
            foreach (int x in Enumerable.Range(0, 12).Select(index => 6 + index * 3))
                AddSlot(slots, SlotKind.Staging, x, 0);

            IEnumerable<int> occupiedSlots;
            if (placementSeed < 0)
            {
                occupiedSlots = Enumerable.Range(0, blockingVehicleCount);
            }
            else
            {
                int[] order = Enumerable
                    .Range(0, MaximumBlockingVehicles).ToArray();
                var random = new Random(placementSeed);
                for (int index = order.Length - 1; index > 0; index--)
                {
                    int swap = random.Next(index + 1);
                    (order[index], order[swap]) = (order[swap], order[index]);
                }
                occupiedSlots = order
                    .Take(blockingVehicleCount)
                    .OrderBy(slotIndex => slotIndex)
                    .ToArray();
            }

            var problem = new EmergencyProblemV2(
                Width,
                Height,
                floor,
                slots,
                occupiedSlots,
                new[] { (0, 1), (1, 1), (2, 1), (3, 1) },
                Array.Empty<(int X, int Y)>(),
                Array.Empty<VehiclePose>(),
                timing);
            return new ApartmentComplexScenarioV2
            {
                BaseProblem = problem,
                Buildings = buildings,
                BlockingVehicleCount = blockingVehicleCount,
                Entrances = new[]
                {
                    new ApartmentComplexEntranceV2("west-primary", (3, 18), true),
                    new ApartmentComplexEntranceV2("east-secondary", (55, 18), false),
                },
            };
        }

        private static ApartmentBuildingV2 CreateBuilding(
            int id,
            int minX,
            int maxX,
            int minY,
            int maxY,
            int zoneCenterX,
            int zoneMinY,
            int zoneMaxY,
            int approachY)
        {
            (int X, int Y)[] footprint =
                RectangleCells(minX, maxX, minY, maxY).ToArray();
            (int X, int Y)[] zoneCells =
                RectangleCells(zoneCenterX - 2, zoneCenterX + 2, zoneMinY, zoneMaxY)
                    .ToArray();
            var zone = new FireEngineZoneV2(
                "zone-" + id,
                id,
                ApartmentFacadeV2.CentralRoad,
                (zoneCenterX, approachY),
                zoneCells);
            return new ApartmentBuildingV2(id, footprint, zone);
        }

        private static void AddSlot(
            ICollection<ParkingSlotV2> slots,
            SlotKind kind,
            int x,
            int y,
            VehicleOrientation orientation =
                VehicleOrientation.Horizontal)
        {
            slots.Add(new ParkingSlotV2(
                slots.Count,
                kind,
                new VehiclePose(x, y, orientation)));
        }

        private static void Fill(
            bool[,] floor,
            int minX,
            int maxX,
            int minY,
            int maxY)
        {
            for (int x = minX; x <= maxX; x++)
                for (int y = minY; y <= maxY; y++)
                    floor[x, y] = true;
        }

        private static IEnumerable<(int X, int Y)> RectangleCells(
            int minX,
            int maxX,
            int minY,
            int maxY)
        {
            for (int x = minX; x <= maxX; x++)
                for (int y = minY; y <= maxY; y++)
                    yield return (x, y);
        }
    }

    /// <summary>
    /// 차량 점유와 무관한 중심선·폭3 후보를 같은 도로 기하의 여러 평가에서 재사용한다.
    /// floor 또는 고정 차량 구성이 다르면 재사용을 거부한다.
    /// </summary>
    public sealed class ApartmentComplexRouteCatalogV2
    {
        private readonly bool[,] _floor;
        private readonly VehiclePose[] _fixedVehicles;
        private readonly EmergencyAccessRouteGenerationOptionsV2 _options;
        private readonly Dictionary<(int BuildingId, string EntranceName),
            EmergencyAccessRouteGenerationResultV2> _generations =
            new Dictionary<(int, string), EmergencyAccessRouteGenerationResultV2>();

        public int GenerationCount { get; private set; }

        public ApartmentComplexRouteCatalogV2(
            ApartmentComplexScenarioV2 geometrySource,
            EmergencyAccessRouteGenerationOptionsV2 options = null)
        {
            if (geometrySource == null || geometrySource.BaseProblem == null)
                throw new ArgumentNullException(nameof(geometrySource));
            _floor = geometrySource.BaseProblem.CopyFloor();
            _fixedVehicles = geometrySource.BaseProblem.FixedVehiclePoses
                .OrderBy(pose => pose.X)
                .ThenBy(pose => pose.Y)
                .ThenBy(pose => pose.Orientation)
                .ToArray();
            _options = options ?? new EmergencyAccessRouteGenerationOptionsV2();
            string error = _options.Validate();
            if (error != null) throw new ArgumentException(error, nameof(options));
        }

        internal EmergencyAccessRouteGenerationResultV2 GetOrGenerate(
            ApartmentComplexScenarioV2 scenario,
            ApartmentBuildingV2 building,
            ApartmentComplexEntranceV2 entrance)
        {
            EnsureCompatible(scenario);
            var key = (building.Id, entrance.Name);
            if (_generations.TryGetValue(key, out
                    EmergencyAccessRouteGenerationResultV2 generation))
                return generation;
            generation = EmergencyAccessRouteGeneratorV2.Generate(
                scenario.BaseProblem,
                entrance.Cell,
                building.FireEngineZone.ApproachCell,
                _options);
            _generations.Add(key, generation);
            GenerationCount++;
            return generation;
        }

        private void EnsureCompatible(ApartmentComplexScenarioV2 scenario)
        {
            if (scenario == null || scenario.BaseProblem == null ||
                scenario.BaseProblem.Width != _floor.GetLength(0) ||
                scenario.BaseProblem.Height != _floor.GetLength(1))
                throw new ArgumentException("후보 카탈로그와 단지 크기가 다름", nameof(scenario));
            for (int x = 0; x < _floor.GetLength(0); x++)
                for (int y = 0; y < _floor.GetLength(1); y++)
                    if (scenario.BaseProblem.IsFloor(x, y) != _floor[x, y])
                        throw new ArgumentException(
                            "후보 카탈로그와 단지 floor가 다름", nameof(scenario));
            VehiclePose[] fixedVehicles = scenario.BaseProblem.FixedVehiclePoses
                .OrderBy(pose => pose.X)
                .ThenBy(pose => pose.Y)
                .ThenBy(pose => pose.Orientation)
                .ToArray();
            if (!_fixedVehicles.SequenceEqual(fixedVehicles))
                throw new ArgumentException(
                    "후보 카탈로그와 고정 차량 구성이 다름", nameof(scenario));
        }
    }

    /// <summary>
    /// 한 차량 배치에서 동×입구 물리 시도를 한 번만 계산한다.
    /// 서문 단일과 서문+동문 집계를 함께 구할 때 서문 결과를 재사용한다.
    /// </summary>
    public sealed class ApartmentComplexPlanningSessionV2
    {
        private readonly ApartmentComplexScenarioV2 _scenario;
        private readonly ApartmentComplexRouteCatalogV2 _routeCatalog;
        private readonly int _activeRobotCount;
        private readonly int _maxHighLevelCandidates;
        private readonly int _maxTick;
        private readonly int _maxExpansionsPerPath;
        private readonly bool _enableLowerBoundPruning;
        private readonly Dictionary<(int BuildingId, string EntranceName),
            ApartmentComplexAccessAttemptV2> _attempts =
            new Dictionary<(int, string), ApartmentComplexAccessAttemptV2>();

        public int PhysicalAttemptCount { get; private set; }
        public int AttemptCacheHitCount { get; private set; }
        public int PhysicalPlanCount { get; private set; }
        public int PhysicalPlanPrunedCount { get; private set; }
        public int RouteGenerationCount => _routeCatalog.GenerationCount;

        public ApartmentComplexPlanningSessionV2(
            ApartmentComplexScenarioV2 scenario,
            int activeRobotCount = 4,
            EmergencyAccessRouteGenerationOptionsV2 generationOptions = null,
            int maxHighLevelCandidates = 8,
            int maxTick = 2000,
            int maxExpansionsPerPath = 200000,
            ApartmentComplexRouteCatalogV2 routeCatalog = null,
            bool enableLowerBoundPruning = false)
        {
            if (scenario == null || scenario.BaseProblem == null)
                throw new ArgumentNullException(nameof(scenario));
            _scenario = scenario;
            _activeRobotCount = activeRobotCount;
            _maxHighLevelCandidates = maxHighLevelCandidates;
            _maxTick = maxTick;
            _maxExpansionsPerPath = maxExpansionsPerPath;
            _enableLowerBoundPruning = enableLowerBoundPruning;
            _routeCatalog = routeCatalog ??
                new ApartmentComplexRouteCatalogV2(scenario, generationOptions);
        }

        public ApartmentComplexPlanResultV2 Solve(
            ApartmentFireIncidentV2 incident,
            bool includeSecondaryEntrances)
        {
            var result = new ApartmentComplexPlanResultV2
            {
                Incident = incident,
                Attempts = Array.Empty<ApartmentComplexAccessAttemptV2>(),
            };
            if (incident == null)
            {
                result.Failure = EmergencyAccessFailureV2.InvalidInput;
                result.FailReason = "화재 사건이 필요함";
                return result;
            }

            ApartmentBuildingV2 building = _scenario.FindBuilding(incident.BuildingId);
            if (building == null || building.FireEngineZone.Facade != incident.Facade)
            {
                result.Failure = EmergencyAccessFailureV2.InvalidInput;
                result.FailReason = "화재동 또는 접근면에 대응하는 전용구역이 없음";
                return result;
            }
            result.TargetZone = building.FireEngineZone;

            ApartmentComplexEntranceV2[] entrances = _scenario.Entrances
                .Where(entrance => entrance.IsPrimary || includeSecondaryEntrances)
                .OrderBy(entrance => entrance.Name, StringComparer.Ordinal)
                .ToArray();
            var attempts = new List<ApartmentComplexAccessAttemptV2>();
            foreach (ApartmentComplexEntranceV2 entrance in entrances)
                attempts.Add(GetOrSolve(building, entrance));
            result.Attempts = attempts;

            ApartmentComplexAccessAttemptV2 selected = attempts
                .Where(attempt => attempt.Success)
                .OrderBy(attempt => attempt.AutomaticPlan.Plan.Selected.Plan.Ticks)
                .ThenBy(attempt =>
                    attempt.AutomaticPlan.Plan.Selected.Scenario.SelectedVehicleCount)
                .ThenBy(attempt =>
                    attempt.AutomaticPlan.Plan.Selected.Route.RequiredCells.Count)
                .ThenBy(attempt => attempt.Entrance.Name, StringComparer.Ordinal)
                .ThenBy(attempt =>
                    attempt.AutomaticPlan.Plan.Selected.Route.Name,
                    StringComparer.Ordinal)
                .FirstOrDefault();
            if (selected != null)
            {
                result.Success = true;
                result.Failure = EmergencyAccessFailureV2.None;
                result.Selected = selected;
                return result;
            }

            result.Failure = attempts.Count == 0
                ? EmergencyAccessFailureV2.InvalidInput
                : attempts.Select(attempt => attempt.AutomaticPlan.Failure)
                    .OrderBy(failure => (int)failure)
                    .First();
            result.FailReason = attempts.Count == 0
                ? "사용 가능한 단지 진입구가 없음"
                : "모든 단지 진입구에서 전용구역 개통 계획에 실패함";
            return result;
        }

        private ApartmentComplexAccessAttemptV2 GetOrSolve(
            ApartmentBuildingV2 building,
            ApartmentComplexEntranceV2 entrance)
        {
            var key = (building.Id, entrance.Name);
            if (_attempts.TryGetValue(key, out ApartmentComplexAccessAttemptV2 cached))
            {
                AttemptCacheHitCount++;
                return cached;
            }
            EmergencyAccessRouteGenerationResultV2 generation =
                _routeCatalog.GetOrGenerate(_scenario, building, entrance);
            AutomaticEmergencyAccessPlanResultV2 automatic =
                EmergencyAccessRouteGeneratorV2.SolveGenerated(
                    _scenario.BaseProblem,
                    generation,
                    _activeRobotCount,
                    _maxHighLevelCandidates,
                    _maxTick,
                    _maxExpansionsPerPath,
                    _enableLowerBoundPruning);
            var attempt = new ApartmentComplexAccessAttemptV2
            {
                Entrance = entrance,
                AutomaticPlan = automatic,
            };
            _attempts.Add(key, attempt);
            PhysicalAttemptCount++;
            if (automatic.Plan != null)
            {
                PhysicalPlanCount += automatic.Plan.PhysicalPlansRun;
                PhysicalPlanPrunedCount += automatic.Plan.PhysicalPlansPruned;
            }
            return attempt;
        }
    }

    public static class ApartmentComplexEmergencyPlannerV2
    {
        public static ApartmentComplexPlanResultV2 Solve(
            ApartmentComplexScenarioV2 scenario,
            ApartmentFireIncidentV2 incident,
            bool includeSecondaryEntrances,
            int activeRobotCount = 4,
            EmergencyAccessRouteGenerationOptionsV2 generationOptions = null,
            int maxTick = 2000,
            int maxExpansionsPerPath = 200000,
            ApartmentComplexRouteCatalogV2 routeCatalog = null)
        {
            if (scenario == null || scenario.BaseProblem == null)
            {
                var result = new ApartmentComplexPlanResultV2
                {
                    Incident = incident,
                    Attempts = Array.Empty<ApartmentComplexAccessAttemptV2>(),
                };
                result.Failure = EmergencyAccessFailureV2.InvalidInput;
                result.FailReason = "단지 시나리오와 화재 사건이 필요함";
                return result;
            }
            var session = new ApartmentComplexPlanningSessionV2(
                scenario,
                activeRobotCount,
                generationOptions,
                maxTick: maxTick,
                maxExpansionsPerPath: maxExpansionsPerPath,
                routeCatalog: routeCatalog);
            return session.Solve(incident, includeSecondaryEntrances);
        }
    }
}
