using System.Collections.Generic;
using System.Linq;
using ParkingSim.Core.V2;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ParkingSim.Runtime
{
    /// <summary>
    /// Model V2 운영 후보 Unity 재생기.
    /// 운영 규모 통로와 화재 시나리오를 코드로 생성하고, pipeline 결과를 그대로 재생한다.
    /// </summary>
    public sealed class SimulationRunner : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Object.FindAnyObjectByType<SimulationRunner>() != null) return;
            var gameObject = new GameObject("ModelV2-SimBootstrap");
            gameObject.AddComponent<SimulationRunner>();
        }

        private const float SecondsPerTick = 0.32f;
        private const float EndHoldTicks = 5f;

        private EmergencyProblemV2 _problem;
        private PipelinedPlanResultV2 _plan;
        private SurfaceApartmentScenarioV2 _surface;
        private PhysicalTimeProfileV2 _timeProfile;
        private EmergencyAccessRouteV2 _selectedRoute;
        private IReadOnlyList<EmergencyAccessRouteV2> _candidateRoutes;
        private readonly Dictionary<int, GameObject> _carViews = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, PipelinedMissionV2> _missions =
            new Dictionary<int, PipelinedMissionV2>();
        private GameObject[] _robotViews;
        private GameObject[] _robotServiceIndicators;
        private bool[] _robotUsesCustomView;
        private SimulationVisualLayers _visualLayers;
        private readonly SimulationCameraController _cameraController =
            new SimulationCameraController();
        private GameObject _fireMarker;
        private Camera _presentationCamera;
        private string _scenarioName;
        private string _routeName;
        private int _movedVehicleCount;
        private int _additionalVehicleCount;
        private (int X, int Y) _fireCell;
        private string _inputStatus;
        private SimulationVisualMode _visualMode =
            SimulationVisualMode.ThreeDimensional;
        private bool _paused;
        private float _displayTick;
        private float _time;

        private void Start()
        {
            _timeProfile = PublishedParkingRobotTimingV2.Create(1.0);
            _fireCell = (22, 5);
            LoadPreset(1);
        }

        private void LoadPreset(int preset)
        {
            _surface = SurfaceApartmentScenarioFactoryV2.Build(
                _timeProfile.CreateOperationTiming());
            EmergencyScenarioBuildResultV2 built;
            PipelinedPlanResultV2 plan;
            string scenarioName;
            string routeName;
            if (preset == 0)
            {
                _fireCell = (22, 5);
                scenarioName = "전면 재배치 기준선";
                routeName = "두 접근로 전체";
                built = new EmergencyScenarioV2(
                    "unity-surface-full", _fireCell, _surface.FullClearanceCells)
                    .Build(_surface.BaseProblem);
                plan = built.Success
                    ? PipelinedPrioritizedPlannerV2.Solve(
                        built.Problem,
                        activeRobotCount: 4,
                        maxHighLevelCandidates: 8,
                        maxTick: 5000)
                    : null;
                _selectedRoute = new EmergencyAccessRouteV2(
                    "full-clearance", (1, 5), _fireCell, _surface.FullClearanceCells);
                _candidateRoutes = new[] { _selectedRoute };
            }
            else
            {
                scenarioName = "클릭 화재 자동 핵심경로";
                AutomaticEmergencyAccessPlanResultV2 automatic =
                    EmergencyAccessRouteGeneratorV2.Solve(
                        _surface.BaseProblem,
                        (1, 5),
                        _fireCell,
                        activeRobotCount: 4,
                        maxHighLevelCandidates: 8,
                        maxTick: 5000);
                if (!automatic.Success)
                {
                    string attemptedFire =
                        "(" + _fireCell.X + "," + _fireCell.Y + ")";
                    if (_problem != null && _problem.FireCell.HasValue)
                        _fireCell = _problem.FireCell.Value;
                    _inputStatus =
                        "화재 " + attemptedFire + " 자동경로 실패: " +
                        automatic.FailReason;
                    Debug.LogWarning("[Model V2] " + _inputStatus);
                    return;
                }
                built = automatic.Plan.Selected.Scenario;
                plan = automatic.Plan.Selected.Plan;
                _selectedRoute = automatic.Plan.Selected.Route;
                _candidateRoutes = automatic.Generation.Routes;
                routeName = _selectedRoute.Name;
            }
            if (!built.Success)
            {
                Debug.LogError("[Model V2] 시나리오 생성 실패: " + built.FailReason);
                return;
            }

            if (plan == null || !plan.Success || !plan.PhysicallyValid)
            {
                Debug.LogError("[Model V2] pipeline 계획 실패: " +
                               (plan == null ? "계획 없음" : plan.FailReason));
                return;
            }

            if (_visualLayers != null && _visualLayers.Root != null)
                Destroy(_visualLayers.Root);
            _visualLayers = new SimulationVisualLayers();
            _carViews.Clear();
            _missions.Clear();
            _problem = built.Problem;
            _plan = plan;
            _scenarioName = scenarioName;
            _routeName = routeName;
            _movedVehicleCount = built.SelectedVehicleCount;
            _additionalVehicleCount = _surface.BaseProblem.VehicleCount;
            _inputStatus =
                "화재 (" + _fireCell.X + "," + _fireCell.Y + ") · 후보 " +
                _candidateRoutes.Count + "개";
            _time = 0f;
            foreach (PipelinedMissionV2 mission in _plan.Missions)
                _missions.Add(mission.VehicleIndex, mission);

            BuildControlGrid();
            BuildPresentationGround();
            BuildApartmentContext();
            BuildRouteOverlays();
            BuildFireMarker();
            BuildEntranceMarker();
            BuildFixedCars();
            BuildMovableCars();
            BuildRobots();
            SetupLighting();
            ApplyVisualMode();
            ApplyTick(0f);

            Debug.Log(
                "[Model V2] scenario=" + _scenarioName +
                ", pipeline 재생 시작 — " + _problem.Width + "x" + _problem.Height +
                ", 이동차량=" + _problem.VehicleCount + ", 고정차량=" +
                _problem.FixedVehiclePoses.Count + ", 선택경로=" + _routeName +
                ", 가변주차=" + _additionalVehicleCount + ", " +
                _plan.RobotTimelines.Length + "조, " + _plan.Ticks + "틱/" +
                _timeProfile.PlanSeconds(_plan.Ticks).ToString("0.0") +
                "초(Stanley 1m/s 참조), 확장 " +
                _plan.ExpandedStates + "상태");
        }

        private void Update()
        {
            if (PresetKeyPressed(1)) LoadPreset(0);
            if (PresetKeyPressed(2)) LoadPreset(1);
            if (VisualModeTogglePressed())
            {
                _visualMode = _visualMode == SimulationVisualMode.Control
                    ? SimulationVisualMode.ThreeDimensional
                    : SimulationVisualMode.Control;
                ApplyVisualMode();
            }
            if (PauseTogglePressed()) _paused = !_paused;
            if (ReplayPressed())
            {
                _time = 0f;
                ApplyTick(0f);
            }
            Vector2 pointerPosition;
            if (PointerPressed(out pointerPosition))
            {
                (int X, int Y) clickedCell;
                string failure;
                if (TryResolveClickedCell(pointerPosition, out clickedCell, out failure))
                {
                    _fireCell = clickedCell;
                    LoadPreset(1);
                }
                else
                {
                    _inputStatus = failure;
                    Debug.LogWarning("[Model V2] " + failure);
                }
            }
            if (_plan == null) return;
            if (!_paused) _time += Time.deltaTime;
            float cycle = _plan.Ticks + EndHoldTicks;
            float tick = (_time / SecondsPerTick) % cycle;
            ApplyTick(Mathf.Min(tick, _plan.Ticks));
            AnimateFireMarker();
        }

        private static bool PresetKeyPressed(int preset)
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return false;
            return preset == 1
                ? keyboard.digit1Key.wasPressedThisFrame
                : keyboard.digit2Key.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(preset == 1 ? KeyCode.Alpha1 : KeyCode.Alpha2);
#else
            return false;
#endif
        }

        private static bool PointerPressed(out Vector2 position)
        {
#if ENABLE_INPUT_SYSTEM
            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                position = mouse.position.ReadValue();
                return true;
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetMouseButtonDown(0))
            {
                position = Input.mousePosition;
                return true;
            }
#endif
            position = Vector2.zero;
            return false;
        }

        private static bool VisualModeTogglePressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null &&
                   (Keyboard.current.tabKey.wasPressedThisFrame ||
                    Keyboard.current.cKey.wasPressedThisFrame);
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.Tab) || Input.GetKeyDown(KeyCode.C);
#else
            return false;
#endif
        }

        private static bool PauseTogglePressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.Space);
#else
            return false;
#endif
        }

        private static bool ReplayPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.R);
#else
            return false;
#endif
        }

        private bool TryResolveClickedCell(
            Vector2 screenPosition,
            out (int X, int Y) cell,
            out string failure)
        {
            Camera camera = Camera.main;
            if (camera == null) camera = Object.FindAnyObjectByType<Camera>();
            if (camera == null)
            {
                cell = default;
                failure = "활성 카메라가 없어 화재 위치를 선택할 수 없음";
                return false;
            }
            Ray ray = camera.ScreenPointToRay(screenPosition);
            RaycastHit hit;
            if (!Physics.Raycast(ray, out hit, 100f))
            {
                cell = default;
                failure = "주차장 격자를 클릭해야 함";
                return false;
            }
            cell = (
                Mathf.RoundToInt(hit.point.x),
                Mathf.RoundToInt(hit.point.z));
            if (_surface == null ||
                !_surface.BaseProblem.IsFloor(cell.X, cell.Y))
            {
                failure =
                    "선택 셀 (" + cell.X + "," + cell.Y + ")은 이동 가능 floor 밖임";
                return false;
            }
            failure = null;
            return true;
        }

        private void ApplyTick(float timelineTick)
        {
            _displayTick = timelineTick;
            int aTick = Mathf.Clamp(Mathf.FloorToInt(timelineTick), 0, _plan.Ticks);
            int bTick = Mathf.Min(aTick + 1, _plan.Ticks);
            float fraction = bTick == aTick ? 0f : timelineTick - aTick;

            for (int robot = 0; robot < _plan.RobotTimelines.Length; robot++)
            {
                TimedRobotStateV2 a = StateAt(_plan.RobotTimelines[robot], aTick);
                TimedRobotStateV2 b = StateAt(_plan.RobotTimelines[robot], bTick);
                _robotViews[robot].transform.position = Vector3.Lerp(
                    RobotPosition(a, _robotUsesCustomView[robot]),
                    RobotPosition(b, _robotUsesCustomView[robot]),
                    fraction);
                _robotViews[robot].transform.rotation = Quaternion.Lerp(
                    RobotRotation(a), RobotRotation(b), fraction);
                SetColor(_robotViews[robot], RobotColor(robot, a.Carrying || b.Carrying));
            }

            for (int vehicle = 0; vehicle < _problem.VehicleCount; vehicle++)
            {
                VehicleVisualState a = VehicleAt(vehicle, aTick);
                VehicleVisualState b = VehicleAt(vehicle, bTick);
                GameObject view = _carViews[vehicle];
                bool customTransport =
                    _robotUsesCustomView[_missions[vehicle].RobotIndex];
                Vector3 position = Vector3.Lerp(
                    VehiclePosition(a.Pose, a.Carried, customTransport),
                    VehiclePosition(b.Pose, b.Carried, customTransport),
                    fraction);
                position.y += ServiceHeightOffset(vehicle, timelineTick);
                view.transform.position = position;
                view.transform.rotation = Quaternion.Lerp(
                    VehicleRotation(a.Pose), VehicleRotation(b.Pose), fraction);
                SetColor(view, a.Carried || b.Carried
                    ? new Color(1f, 0.55f, 0.08f)
                    : MovableVehicleColor(vehicle));
            }
            ApplyServiceIndicators(timelineTick);
        }

        private VehicleVisualState VehicleAt(int vehicle, int tick)
        {
            PipelinedMissionV2 mission = _missions[vehicle];
            if (tick < mission.LiftTick)
                return new VehicleVisualState(
                    _problem.Slots[_problem.InitialVehicleSlots[vehicle]].Pose, false);
            if (tick < mission.DropTick)
            {
                TimedRobotStateV2 robot = StateAt(
                    _plan.RobotTimelines[mission.RobotIndex], tick);
                return new VehicleVisualState(
                    new VehiclePose(robot.X, robot.Y, robot.Orientation), true);
            }
            return new VehicleVisualState(_problem.Slots[mission.DestinationSlot].Pose, false);
        }

        private float ServiceHeightOffset(int vehicle, float tick)
        {
            PipelinedMissionV2 mission = _missions[vehicle];
            float liftHeight = _robotUsesCustomView[mission.RobotIndex]
                ? 0.04f
                : 0.18f;
            float pickupStart = mission.LiftTick - _problem.Timing.LiftServiceTicks;
            if (tick >= pickupStart && tick < mission.LiftTick)
            {
                float progress = Mathf.InverseLerp(pickupStart, mission.LiftTick, tick);
                return Mathf.SmoothStep(0f, liftHeight, progress);
            }
            float releaseStart = mission.DropTick - _problem.Timing.DropServiceTicks;
            if (tick >= releaseStart && tick < mission.DropTick)
            {
                float progress = Mathf.InverseLerp(releaseStart, mission.DropTick, tick);
                return Mathf.SmoothStep(0f, -liftHeight, progress);
            }
            return 0f;
        }

        private void ApplyServiceIndicators(float tick)
        {
            if (_robotServiceIndicators == null) return;
            for (int robot = 0; robot < _robotServiceIndicators.Length; robot++)
            {
                float progress;
                int phase = ServicePhase(robot, tick, out progress);
                GameObject indicator = _robotServiceIndicators[robot];
                indicator.SetActive(phase != 0);
                if (phase == 0) continue;
                SetColor(indicator, phase == 1
                    ? new Color(1f, 0.72f, 0.08f)
                    : new Color(0.20f, 1f, 0.50f));
                float pulse = 0.85f + 0.25f * Mathf.Sin(Time.unscaledTime * 8f);
                float radius = _robotUsesCustomView[robot] ? 0.06f : 0.30f;
                float baseHeight = _robotUsesCustomView[robot] ? 0.06f : 0.30f;
                float progressHeight = _robotUsesCustomView[robot] ? 0.10f : 0.50f;
                indicator.transform.localScale = new Vector3(
                    radius * pulse,
                    baseHeight + progressHeight * progress,
                    radius * pulse);
            }
        }

        private int ServicePhase(int robot, float tick, out float progress)
        {
            foreach (PipelinedMissionV2 mission in _missions.Values)
            {
                if (mission.RobotIndex != robot) continue;
                float pickupStart = mission.LiftTick - _problem.Timing.LiftServiceTicks;
                if (tick >= pickupStart && tick < mission.LiftTick)
                {
                    progress = Mathf.InverseLerp(pickupStart, mission.LiftTick, tick);
                    return 1;
                }
                float releaseStart = mission.DropTick - _problem.Timing.DropServiceTicks;
                if (tick >= releaseStart && tick < mission.DropTick)
                {
                    progress = Mathf.InverseLerp(releaseStart, mission.DropTick, tick);
                    return 2;
                }
            }
            progress = 0f;
            return 0;
        }

        private void BuildControlGrid()
        {
            for (int y = 0; y < _problem.Height; y++)
            {
                for (int x = 0; x < _problem.Width; x++)
                {
                    var tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    Track(tile, SimulationVisualLayer.Control);
                    tile.name = "ControlCell-" + x + "-" + y;
                    bool floor = _problem.IsFloor(x, y);
                    float height = floor
                        ? 0.12f
                        : 0.90f + ((x * 7 + y * 3) % 4) * 0.16f;
                    tile.transform.position = new Vector3(
                        x, floor ? -0.08f : height / 2f - 0.08f, y);
                    tile.transform.localScale = new Vector3(0.94f, height, 0.94f);
                    SetColor(tile, CellColor(x, y));
                }
            }
        }

        private void BuildPresentationGround()
        {
            for (int y = 0; y < _problem.Height; y++)
            {
                for (int x = 0; x < _problem.Width; x++)
                {
                    var tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    Track(tile, SimulationVisualLayer.ThreeDimensional);
                    tile.name = "PresentationCell-" + x + "-" + y;
                    bool floor = _problem.IsFloor(x, y);
                    float height = floor
                        ? 0.10f
                        : 0.62f + ((x * 7 + y * 3) % 3) * 0.10f;
                    tile.transform.position = new Vector3(
                        x, floor ? -0.09f : height / 2f - 0.09f, y);
                    tile.transform.localScale = new Vector3(0.98f, height, 0.98f);
                    SetColor(
                        tile,
                        floor
                            ? new Color(0.22f, 0.24f, 0.25f)
                            : new Color(0.35f, 0.38f, 0.40f));
                }
            }
        }

        private void BuildRouteOverlays()
        {
            if (_candidateRoutes == null || _selectedRoute == null) return;
            foreach (EmergencyAccessRouteV2 route in _candidateRoutes)
            {
                if (route.Name == _selectedRoute.Name) continue;
                BuildRouteOverlay(
                    route,
                    "CandidateRoute-" + route.Name,
                    new Color(0.12f, 0.38f, 0.78f),
                    0.08f,
                    0.54f);
            }
            BuildRouteOverlay(
                _selectedRoute,
                "SelectedRoute-" + _selectedRoute.Name,
                new Color(0.08f, 0.88f, 0.92f),
                0.12f,
                0.72f,
                SimulationVisualLayer.Control);
            BuildRouteOverlay(
                _selectedRoute,
                "ThreeDimensionalSelectedRoute-" + _selectedRoute.Name,
                new Color(0.08f, 0.88f, 0.92f),
                0.11f,
                0.46f,
                SimulationVisualLayer.ThreeDimensional);
        }

        private void BuildApartmentContext()
        {
            BuildApartmentBuilding(
                "Fire-Apartment",
                new Vector3(25.2f, 0f, 7f),
                width: 3.2f,
                depth: 8.4f,
                height: 6.8f,
                floors: 6,
                southColumns: 2,
                westColumns: 5,
                bodyColor: new Color(0.50f, 0.54f, 0.58f),
                accentColor: new Color(0.72f, 0.20f, 0.16f));
            BuildApartmentBuilding(
                "Background-Apartment",
                new Vector3(11.5f, 0f, 15.4f),
                width: 10.4f,
                depth: 2.8f,
                height: 5.6f,
                floors: 5,
                southColumns: 7,
                westColumns: 2,
                bodyColor: new Color(0.43f, 0.48f, 0.54f),
                accentColor: new Color(0.12f, 0.46f, 0.68f));
        }

        private void BuildApartmentBuilding(
            string name,
            Vector3 origin,
            float width,
            float depth,
            float height,
            int floors,
            int southColumns,
            int westColumns,
            Color bodyColor,
            Color accentColor)
        {
            GameObject building = SimulationVisualAssetFactory.TryCreate(
                SimulationVisualAssetFactory.ApartmentResourcePath,
                name);
            if (building != null)
            {
                Track(building, SimulationVisualLayer.ThreeDimensional);
                building.transform.position = origin;
                building.transform.localScale = new Vector3(width, height, depth);
                DisableColliders(building);
                return;
            }
            building = new GameObject(name);
            Track(building, SimulationVisualLayer.ThreeDimensional);
            building.transform.position = origin;
            CreateVisualChild(
                PrimitiveType.Cube,
                building.transform,
                name + "-Body",
                new Vector3(0f, height / 2f, 0f),
                new Vector3(width, height, depth),
                bodyColor);
            CreateVisualChild(
                PrimitiveType.Cube,
                building.transform,
                name + "-Roof",
                new Vector3(0f, height + 0.16f, 0f),
                new Vector3(width + 0.24f, 0.28f, depth + 0.24f),
                new Color(0.18f, 0.21f, 0.25f));
            CreateVisualChild(
                PrimitiveType.Cube,
                building.transform,
                name + "-Core",
                new Vector3(0f, height + 0.56f, 0f),
                new Vector3(
                    Mathf.Min(1.2f, width * 0.36f),
                    0.80f,
                    Mathf.Min(1.5f, depth * 0.34f)),
                accentColor);

            float floorHeight = height / floors;
            Color windowColor = new Color(0.12f, 0.30f, 0.43f);
            for (int floor = 0; floor < floors; floor++)
            {
                float y = floorHeight * (floor + 0.58f);
                for (int column = 0; column < southColumns; column++)
                {
                    float x = ColumnPosition(width, southColumns, column);
                    CreateVisualChild(
                        PrimitiveType.Cube,
                        building.transform,
                        name + "-SouthWindow-" + floor + "-" + column,
                        new Vector3(x, y, -depth / 2f - 0.025f),
                        new Vector3(
                            Mathf.Min(0.72f, width / (southColumns + 1) * 0.58f),
                            floorHeight * 0.42f,
                            0.07f),
                        windowColor);
                }
                for (int column = 0; column < westColumns; column++)
                {
                    float z = ColumnPosition(depth, westColumns, column);
                    CreateVisualChild(
                        PrimitiveType.Cube,
                        building.transform,
                        name + "-WestWindow-" + floor + "-" + column,
                        new Vector3(-width / 2f - 0.025f, y, z),
                        new Vector3(
                            0.07f,
                            floorHeight * 0.42f,
                            Mathf.Min(0.72f, depth / (westColumns + 1) * 0.58f)),
                        windowColor);
                }
                CreateVisualChild(
                    PrimitiveType.Cube,
                    building.transform,
                    name + "-FloorBand-" + floor,
                    new Vector3(0f, floorHeight * (floor + 1f), -depth / 2f - 0.06f),
                    new Vector3(width + 0.14f, 0.07f, 0.15f),
                    accentColor);
            }

            CreateVisualChild(
                PrimitiveType.Cube,
                building.transform,
                name + "-Entrance",
                new Vector3(-width / 2f - 0.04f, 0.62f, 0f),
                new Vector3(0.10f, 1.24f, Mathf.Min(1.1f, depth * 0.3f)),
                new Color(0.08f, 0.12f, 0.16f));
        }

        private static float ColumnPosition(float span, int columns, int index)
        {
            return -span / 2f + span * (index + 1f) / (columns + 1f);
        }

        private void BuildRouteOverlay(
            EmergencyAccessRouteV2 route,
            string namePrefix,
            Color color,
            float y,
            float scale,
            SimulationVisualLayer layer = SimulationVisualLayer.Control)
        {
            int index = 0;
            foreach (var cell in route.RequiredCells)
            {
                var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Track(marker, layer);
                marker.name = namePrefix + "-" + index++;
                marker.transform.position = new Vector3(cell.X, y, cell.Y);
                marker.transform.localScale = new Vector3(scale, 0.04f, scale);
                SetColor(marker, color);
                Collider collider = marker.GetComponent<Collider>();
                if (collider != null) Destroy(collider);
            }
        }

        private void BuildFireMarker()
        {
            if (!_problem.FireCell.HasValue) return;
            var fire = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Track(fire, SimulationVisualLayer.Shared);
            fire.name = "Fire-Origin";
            fire.transform.position = new Vector3(
                _problem.FireCell.Value.X, 0.16f, _problem.FireCell.Value.Y);
            fire.transform.localScale = new Vector3(0.34f, 0.14f, 0.34f);
            SetColor(fire, new Color(1f, 0.08f, 0.02f));
            GameObject flame = CreateChildPrimitive(
                PrimitiveType.Sphere,
                fire.transform,
                "Fire-Flame",
                new Vector3(0f, 1.45f, 0f),
                new Vector3(0.62f, 1.8f, 0.62f),
                new Color(1f, 0.45f, 0.02f));
            Collider flameCollider = flame.GetComponent<Collider>();
            if (flameCollider != null) Destroy(flameCollider);
            _fireMarker = fire;
        }

        private void BuildEntranceMarker()
        {
            var gate = new GameObject("Emergency-Entrance");
            Track(gate, SimulationVisualLayer.Shared);
            gate.transform.position = new Vector3(0.15f, 0f, 5f);
            CreateChildPrimitive(
                PrimitiveType.Cube, gate.transform, "Entrance-Left",
                new Vector3(0f, 0.52f, -1f),
                new Vector3(0.18f, 1.05f, 0.18f), Color.white);
            CreateChildPrimitive(
                PrimitiveType.Cube, gate.transform, "Entrance-Right",
                new Vector3(0f, 0.52f, 1f),
                new Vector3(0.18f, 1.05f, 0.18f), Color.white);
            CreateChildPrimitive(
                PrimitiveType.Cube, gate.transform, "Entrance-Header",
                new Vector3(0f, 1.02f, 0f),
                new Vector3(0.18f, 0.14f, 2.18f), new Color(0.12f, 0.82f, 1f));
        }

        private void BuildFixedCars()
        {
            for (int i = 0; i < _problem.FixedVehiclePoses.Count; i++)
            {
                VehiclePose pose = _problem.FixedVehiclePoses[i];
                GameObject car = CreateCar("FixedVehicle-" + (i + 1), pose);
                SetColor(car, new Color(0.42f, 0.44f, 0.48f));
            }
        }

        private void BuildMovableCars()
        {
            for (int vehicle = 0; vehicle < _problem.VehicleCount; vehicle++)
            {
                VehiclePose pose = _problem.Slots[_problem.InitialVehicleSlots[vehicle]].Pose;
                GameObject car = CreateCar("MovableVehicle-" + (vehicle + 1), pose);
                SetColor(car, MovableVehicleColor(vehicle));
                _carViews.Add(vehicle, car);
            }
        }

        private GameObject CreateCar(string name, VehiclePose pose)
        {
            GameObject car = SimulationVisualAssetFactory.TryCreate(
                SimulationVisualAssetFactory.CarResourcePath,
                name);
            bool primitiveFallback = car == null;
            if (primitiveFallback)
                car = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Track(car, SimulationVisualLayer.Shared);
            car.name = name;
            car.transform.localScale = new Vector3(1.82f, 0.42f, 0.82f);
            car.transform.position = VehiclePosition(pose, false, false);
            car.transform.rotation = VehicleRotation(pose);
            if (!primitiveFallback) return car;
            CreateChildPrimitive(
                PrimitiveType.Cube, car.transform, name + "-Cabin",
                new Vector3(0.02f, 0.72f, 0f),
                new Vector3(0.48f, 0.70f, 0.74f),
                new Color(0.12f, 0.22f, 0.30f));
            CreateChildPrimitive(
                PrimitiveType.Cube, car.transform, name + "-Front",
                new Vector3(0.43f, 0.05f, 0f),
                new Vector3(0.06f, 0.54f, 0.76f),
                new Color(0.92f, 0.92f, 0.78f));
            return car;
        }

        private void BuildRobots()
        {
            _robotViews = new GameObject[_plan.RobotTimelines.Length];
            _robotServiceIndicators = new GameObject[_plan.RobotTimelines.Length];
            _robotUsesCustomView = new bool[_plan.RobotTimelines.Length];
            for (int robot = 0; robot < _plan.RobotTimelines.Length; robot++)
            {
                GameObject cube = SimulationVisualAssetFactory.TryCreate(
                    SimulationVisualAssetFactory.TransportUnitResourcePath,
                    "TransportUnit-" + (robot + 1));
                bool primitiveFallback = cube == null;
                if (primitiveFallback)
                    cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Track(cube, SimulationVisualLayer.Shared);
                cube.name = "TransportUnit-" + (robot + 1);
                if (primitiveFallback)
                {
                    cube.transform.localScale = new Vector3(0.72f, 0.18f, 0.72f);
                    SetColor(cube, RobotColor(robot, false));
                }
                else
                {
                    cube.transform.localScale = Vector3.one;
                }
                _robotViews[robot] = cube;
                _robotUsesCustomView[robot] = !primitiveFallback;
                if (primitiveFallback)
                {
                    CreateChildPrimitive(
                        PrimitiveType.Cube, cube.transform, "Platform-" + (robot + 1),
                        new Vector3(0f, 0.72f, 0f),
                        new Vector3(1.12f, 0.26f, 1.12f),
                        new Color(0.10f, 0.12f, 0.15f));
                }
                GameObject indicator = CreateChildPrimitive(
                    PrimitiveType.Sphere, cube.transform, "ServiceLight-" + (robot + 1),
                    primitiveFallback
                        ? new Vector3(0f, 1.55f, 0f)
                        : new Vector3(0f, 0.16f, 0f),
                    primitiveFallback
                        ? new Vector3(0.30f, 0.55f, 0.30f)
                        : new Vector3(0.12f, 0.18f, 0.12f),
                    new Color(1f, 0.72f, 0.08f));
                Collider collider = indicator.GetComponent<Collider>();
                if (collider != null) Destroy(collider);
                indicator.SetActive(false);
                _robotServiceIndicators[robot] = indicator;
            }
        }

        private void SetupLighting()
        {
            Light[] lights = Object.FindObjectsByType<Light>();
            for (int i = 0; i < lights.Length; i++) lights[i].gameObject.SetActive(false);
            var lightObject = new GameObject("ModelV2-KeyLight");
            Track(lightObject, SimulationVisualLayer.Shared);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.94f, 0.84f);
            light.intensity = 1.25f;
            light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
            RenderSettings.ambientLight = new Color(0.28f, 0.32f, 0.40f);
        }

        private void ApplyVisualMode()
        {
            if (_visualLayers == null || _problem == null) return;
            _visualLayers.SetMode(_visualMode);
            _presentationCamera = _cameraController.Apply(
                _visualMode,
                _problem.Width,
                _problem.Height,
                _presentationCamera);
            Debug.Log(
                "[Model V2] camera=" + _presentationCamera.name +
                (_visualMode == SimulationVisualMode.Control
                    ? ", control"
                    : ", three-dimensional"));
        }

        private void AnimateFireMarker()
        {
            if (_fireMarker == null) return;
            float pulse = 1f + 0.12f * Mathf.Sin(Time.unscaledTime * 5f);
            _fireMarker.transform.localScale = new Vector3(
                0.34f * pulse,
                0.14f * (1f + 0.08f * Mathf.Sin(Time.unscaledTime * 7f)),
                0.34f * pulse);
        }

        private Color CellColor(int x, int y)
        {
            if (!_problem.IsFloor(x, y)) return new Color(0.09f, 0.10f, 0.12f);
            if (IsSlotCell(x, y, SlotKind.Staging)) return new Color(0.12f, 0.42f, 0.24f);
            if (IsSlotCell(x, y, SlotKind.Blocking)) return new Color(0.48f, 0.16f, 0.16f);
            if (_problem.IsClearanceCell(x, y)) return new Color(0.42f, 0.30f, 0.10f);
            return new Color(0.22f, 0.25f, 0.30f);
        }

        private bool IsSlotCell(int x, int y, SlotKind kind)
        {
            foreach (ParkingSlotV2 slot in _problem.Slots)
            {
                if (slot.Kind != kind) continue;
                var second = slot.Pose.SecondCell;
                if ((slot.Pose.X == x && slot.Pose.Y == y) ||
                    (second.X == x && second.Y == y)) return true;
            }
            return false;
        }

        private static TimedRobotStateV2 StateAt(List<TimedRobotStateV2> timeline, int tick)
        {
            for (int i = timeline.Count - 1; i >= 0; i--)
                if (timeline[i].Tick <= tick) return timeline[i];
            return timeline[0];
        }

        private static Vector3 RobotPosition(
            TimedRobotStateV2 robot,
            bool customTransport)
        {
            return new Vector3(robot.X, customTransport ? 0f : 0.20f, robot.Y);
        }

        private static Quaternion RobotRotation(TimedRobotStateV2 robot)
        {
            return robot.Orientation == VehicleOrientation.Horizontal
                ? Quaternion.identity
                : Quaternion.Euler(0f, 90f, 0f);
        }

        private static Vector3 VehiclePosition(
            VehiclePose pose,
            bool carried,
            bool customTransport)
        {
            var second = pose.SecondCell;
            float height = carried
                ? customTransport ? 0.34f : 0.52f
                : 0.30f;
            return new Vector3(
                (pose.X + second.X) / 2f,
                height,
                (pose.Y + second.Y) / 2f);
        }

        private static Quaternion VehicleRotation(VehiclePose pose)
        {
            return pose.Orientation == VehicleOrientation.Horizontal
                ? Quaternion.identity
                : Quaternion.Euler(0f, 90f, 0f);
        }

        private static Color MovableVehicleColor(int vehicle)
        {
            return vehicle % 2 == 0
                ? new Color(0.90f, 0.22f, 0.20f)
                : new Color(0.72f, 0.18f, 0.72f);
        }

        private static Color RobotColor(int robot, bool carrying)
        {
            if (carrying) return new Color(1f, 0.48f, 0.05f);
            return Color.HSVToRGB((robot * 0.137f + 0.52f) % 1f, 0.78f, 0.95f);
        }

        private static void SetColor(GameObject target, Color color)
        {
            Renderer renderer = target.GetComponent<Renderer>();
            if (renderer == null) return;
            Material material = renderer.material;
            material.color = color;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        }

        private static GameObject CreateChildPrimitive(
            PrimitiveType type,
            Transform parent,
            string name,
            Vector3 localPosition,
            Vector3 localScale,
            Color color)
        {
            GameObject child = GameObject.CreatePrimitive(type);
            child.name = name;
            child.transform.SetParent(parent, worldPositionStays: false);
            child.transform.localPosition = localPosition;
            child.transform.localRotation = Quaternion.identity;
            child.transform.localScale = localScale;
            SetColor(child, color);
            return child;
        }

        private static GameObject CreateVisualChild(
            PrimitiveType type,
            Transform parent,
            string name,
            Vector3 localPosition,
            Vector3 localScale,
            Color color)
        {
            GameObject child = CreateChildPrimitive(
                type, parent, name, localPosition, localScale, color);
            Collider collider = child.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
                Destroy(collider);
            }
            return child;
        }

        private static void DisableColliders(GameObject root)
        {
            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
                colliders[i].enabled = false;
        }

        private void Track(GameObject target, SimulationVisualLayer layer)
        {
            _visualLayers.Track(target, layer);
        }

        private void OnGUI()
        {
            if (_plan == null) return;
            double seconds = _timeProfile.PlanSeconds(_plan.Ticks);
            bool safe = seconds <= 420.0;
            GUI.Box(new Rect(12f, 12f, 540f, 214f), string.Empty);
            GUI.Label(new Rect(24f, 22f, 476f, 24f), "Model V2 — " + _scenarioName);
            GUI.Label(new Rect(24f, 46f, 516f, 24f),
                "경로 " + _routeName + " · 이동 " + _movedVehicleCount + "/" +
                _additionalVehicleCount + "대 · 운송유닛 " +
                _plan.RobotTimelines.Length + "조");
            GUI.Label(new Rect(24f, 70f, 516f, 24f),
                "확보 " + _plan.Ticks + "틱 / " + seconds.ToString("0.0") +
                "초(Stanley 1m/s 참조) · 7분 " + (safe ? "통과" : "실패") +
                " · 재생 t=" + _displayTick.ToString("0.0"));
            GUI.Label(new Rect(24f, 94f, 516f, 24f), ServiceStatusText());
            GUI.Label(new Rect(24f, 118f, 516f, 24f), _inputStatus);
            GUI.Label(new Rect(24f, 142f, 516f, 24f),
                "바닥 클릭: 화재 위치·자동 재계획  ·  청록: 선택  ·  파랑: 다른 후보");
            GUI.Label(new Rect(24f, 166f, 516f, 24f),
                "[1] 기본 화재 전면 재배치  [2] 현재 화재 자동 핵심경로");
            GUI.Label(new Rect(24f, 190f, 516f, 24f),
                "[Tab/C] " +
                (_visualMode == SimulationVisualMode.Control ? "3D모드" : "관제모드") +
                "  [Space] " + (_paused ? "재생" : "일시정지") +
                "  [R] 처음부터");
        }

        private string ServiceStatusText()
        {
            int pickup = 0;
            int release = 0;
            float pickupProgress = 0f;
            float releaseProgress = 0f;
            for (int robot = 0; robot < _plan.RobotTimelines.Length; robot++)
            {
                float progress;
                int phase = ServicePhase(robot, _displayTick, out progress);
                if (phase == 1)
                {
                    pickup++;
                    pickupProgress = Mathf.Max(pickupProgress, progress);
                }
                else if (phase == 2)
                {
                    release++;
                    releaseProgress = Mathf.Max(releaseProgress, progress);
                }
            }
            if (pickup > 0)
                return "서비스: 차량 취득 " + pickup + "조 · " +
                       (pickupProgress * 100f).ToString("0") + "% · 노랑 상태등";
            if (release > 0)
                return "서비스: 차량 해제 " + release + "조 · " +
                       (releaseProgress * 100f).ToString("0") + "% · 초록 상태등";
            return _paused ? "서비스: 재생 일시정지" : "서비스: 이동/대기";
        }

        private readonly struct VehicleVisualState
        {
            public VehiclePose Pose { get; }
            public bool Carried { get; }

            public VehicleVisualState(VehiclePose pose, bool carried)
            {
                Pose = pose;
                Carried = carried;
            }
        }
    }
}
