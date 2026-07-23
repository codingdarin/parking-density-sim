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
        private GameObject _visualRoot;
        private string _scenarioName;
        private string _routeName;
        private int _movedVehicleCount;
        private int _additionalVehicleCount;
        private (int X, int Y) _fireCell;
        private string _inputStatus;
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

            if (_visualRoot != null) Destroy(_visualRoot);
            _visualRoot = new GameObject("ModelV2-VisualRoot");
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

            BuildGrid();
            BuildRouteOverlays();
            BuildFireMarker();
            BuildFixedCars();
            BuildMovableCars();
            BuildRobots();
            SetupCamera();
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
            _time += Time.deltaTime;
            float cycle = _plan.Ticks + EndHoldTicks;
            float tick = (_time / SecondsPerTick) % cycle;
            ApplyTick(Mathf.Min(tick, _plan.Ticks));
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
                    RobotPosition(a), RobotPosition(b), fraction);
                SetColor(_robotViews[robot], RobotColor(robot, a.Carrying || b.Carrying));
            }

            for (int vehicle = 0; vehicle < _problem.VehicleCount; vehicle++)
            {
                VehicleVisualState a = VehicleAt(vehicle, aTick);
                VehicleVisualState b = VehicleAt(vehicle, bTick);
                GameObject view = _carViews[vehicle];
                view.transform.position = Vector3.Lerp(
                    VehiclePosition(a.Pose, a.Carried),
                    VehiclePosition(b.Pose, b.Carried), fraction);
                view.transform.rotation = Quaternion.Lerp(
                    VehicleRotation(a.Pose), VehicleRotation(b.Pose), fraction);
                SetColor(view, a.Carried || b.Carried
                    ? new Color(1f, 0.55f, 0.08f)
                    : MovableVehicleColor(vehicle));
            }
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

        private void BuildGrid()
        {
            for (int y = 0; y < _problem.Height; y++)
            {
                for (int x = 0; x < _problem.Width; x++)
                {
                    var tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    Track(tile);
                    tile.name = "Cell-" + x + "-" + y;
                    bool floor = _problem.IsFloor(x, y);
                    tile.transform.position = new Vector3(x, floor ? -0.08f : 0.04f, y);
                    tile.transform.localScale = new Vector3(0.94f, floor ? 0.12f : 0.35f, 0.94f);
                    SetColor(tile, CellColor(x, y));
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
                0.72f);
        }

        private void BuildRouteOverlay(
            EmergencyAccessRouteV2 route,
            string namePrefix,
            Color color,
            float y,
            float scale)
        {
            int index = 0;
            foreach (var cell in route.RequiredCells)
            {
                var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Track(marker);
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
            Track(fire);
            fire.name = "Fire-Origin";
            fire.transform.position = new Vector3(
                _problem.FireCell.Value.X, 0.05f, _problem.FireCell.Value.Y);
            fire.transform.localScale = new Vector3(0.28f, 0.05f, 0.28f);
            SetColor(fire, new Color(1f, 0.08f, 0.02f));
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
            var car = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Track(car);
            car.name = name;
            car.transform.localScale = new Vector3(1.82f, 0.42f, 0.82f);
            car.transform.position = VehiclePosition(pose, false);
            car.transform.rotation = VehicleRotation(pose);
            return car;
        }

        private void BuildRobots()
        {
            _robotViews = new GameObject[_plan.RobotTimelines.Length];
            for (int robot = 0; robot < _plan.RobotTimelines.Length; robot++)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Track(cube);
                cube.name = "TransportUnit-" + (robot + 1);
                cube.transform.localScale = new Vector3(0.72f, 0.18f, 0.72f);
                SetColor(cube, RobotColor(robot, false));
                _robotViews[robot] = cube;
            }
        }

        private void SetupCamera()
        {
            Camera[] cameras = Object.FindObjectsByType<Camera>();
            Camera camera = cameras.Length > 0 ? cameras[0] : null;
            for (int i = 1; i < cameras.Length; i++) cameras[i].gameObject.SetActive(false);
            if (camera == null)
            {
                var cameraObject = new GameObject("ModelV2-Camera") { tag = "MainCamera" };
                camera = cameraObject.AddComponent<Camera>();
                cameraObject.AddComponent<AudioListener>();
            }

            float aspect = camera.aspect > 0.01f ? camera.aspect : 16f / 9f;
            float size = Mathf.Max(_problem.Height / 2f, _problem.Width / (2f * aspect)) + 1f;
            camera.orthographic = true;
            camera.orthographicSize = size;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.06f, 0.07f, 0.09f);
            camera.transform.position = new Vector3(
                (_problem.Width - 1) / 2f, 35f, (_problem.Height - 1) / 2f);
            camera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            Debug.Log("[Model V2] camera=" + camera.name + ", orthoSize=" + size.ToString("0.0"));
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

        private static Vector3 RobotPosition(TimedRobotStateV2 robot)
        {
            return new Vector3(robot.X, 0.20f, robot.Y);
        }

        private static Vector3 VehiclePosition(VehiclePose pose, bool carried)
        {
            var second = pose.SecondCell;
            return new Vector3(
                (pose.X + second.X) / 2f,
                carried ? 0.52f : 0.30f,
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
            Material material = target.GetComponent<Renderer>().material;
            material.color = color;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        }

        private void Track(GameObject target)
        {
            target.transform.SetParent(_visualRoot.transform, worldPositionStays: true);
        }

        private void OnGUI()
        {
            if (_plan == null) return;
            double seconds = _timeProfile.PlanSeconds(_plan.Ticks);
            bool safe = seconds <= 420.0;
            GUI.Box(new Rect(12f, 12f, 500f, 166f), string.Empty);
            GUI.Label(new Rect(24f, 22f, 476f, 24f), "Model V2 — " + _scenarioName);
            GUI.Label(new Rect(24f, 46f, 476f, 24f),
                "경로 " + _routeName + " · 이동 " + _movedVehicleCount + "/" +
                _additionalVehicleCount + "대 · 운송유닛 " +
                _plan.RobotTimelines.Length + "조");
            GUI.Label(new Rect(24f, 70f, 476f, 24f),
                "확보 " + _plan.Ticks + "틱 / " + seconds.ToString("0.0") +
                "초(Stanley 1m/s 참조) · 7분 " + (safe ? "통과" : "실패") +
                " · 재생 t=" + _displayTick.ToString("0.0"));
            GUI.Label(new Rect(24f, 94f, 476f, 24f), _inputStatus);
            GUI.Label(new Rect(24f, 118f, 476f, 24f),
                "바닥 클릭: 화재 위치·자동 재계획  ·  청록: 선택  ·  파랑: 다른 후보");
            GUI.Label(new Rect(24f, 142f, 476f, 24f),
                "[1] 기본 화재 전면 재배치  [2] 현재 화재 자동 핵심경로");
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
