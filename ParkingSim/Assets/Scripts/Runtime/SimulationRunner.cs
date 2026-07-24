using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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

        private sealed class PreparedScenario
        {
            public int BuildingId;
            public int BlockingVehicleCount;
            public bool IncludeSecondaryEntrances;
            public ApartmentComplexScenarioV2 Complex;
            public ApartmentComplexPlanResultV2 ComplexPlan;
        }

        private EmergencyProblemV2 _problem;
        private PipelinedPlanResultV2 _plan;
        private ApartmentComplexScenarioV2 _complex;
        private ApartmentBuildingV2 _fireBuilding;
        private ApartmentComplexEntranceV2 _selectedEntrance;
        private PhysicalTimeProfileV2 _timeProfile;
        private EmergencyAccessRouteV2 _selectedRoute;
        private IReadOnlyList<EmergencyAccessRouteV2> _candidateRoutes;
        private readonly Dictionary<int, GameObject> _carViews = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, PipelinedMissionV2> _missions =
            new Dictionary<int, PipelinedMissionV2>();
        private GameObject[] _robotViews;
        private GameObject[] _robotServiceIndicators;
        private bool[] _robotUsesCustomView;
        private Camera[] _transportCameras;
        private int _selectedTransportCamera = -1;
        private Vector3 _presentationCameraFocus;
        private float _presentationCameraYaw;
        private float _presentationCameraPitch;
        private float _presentationCameraDistance;
        private bool _presentationCameraNavigationInitialized;
        private Vector3[] _transportCameraFocusOffsets;
        private float[] _transportCameraYaws;
        private float[] _transportCameraPitches;
        private float[] _transportCameraDistances;
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
        private int _fireBuildingId;
        private int _blockingVehicleCount;
        private int _requestedBlockingVehicleCount;
        private bool _includeSecondaryEntrances;
        private string _inputStatus;
        private SimulationVisualMode _visualMode =
            SimulationVisualMode.ThreeDimensional;
        private bool _paused;
        private float _displayTick;
        private float _time;
        private Task<PreparedScenario> _planningTask;
        private int _pendingBuildingId;
        private int _pendingBlockingVehicleCount;
        private bool _pendingIncludeSecondaryEntrances;
        private float _planningStartedAt;
        private static readonly Rect GuideBounds = new Rect(12f, 12f, 620f, 260f);
        private const float ControlPanelWidth = 286f;
        private const float ControlPanelHeight = 302f;

        private void Start()
        {
            _timeProfile = PublishedParkingRobotTimingV2.Create(1.0);
            _fireBuildingId = 104;
            _blockingVehicleCount =
                ApartmentComplexScenarioFactoryV2.MaximumBlockingVehicles;
            _requestedBlockingVehicleCount = _blockingVehicleCount;
            BeginPresetLoad(1, _fireBuildingId, _blockingVehicleCount);
        }

        private void BeginPresetLoad(
            int preset,
            int buildingId,
            int blockingVehicleCount)
        {
            if (_planningTask != null)
            {
                _inputStatus = "기존 경로 계산이 끝난 뒤 다시 선택해야 함";
                return;
            }
            bool includeSecondaryEntrances = preset != 0;
            OperationTimingV2 timing = _timeProfile.CreateOperationTiming();
            _pendingBuildingId = buildingId;
            _pendingBlockingVehicleCount = blockingVehicleCount;
            _pendingIncludeSecondaryEntrances = includeSecondaryEntrances;
            _planningStartedAt = Time.realtimeSinceStartup;
            _inputStatus =
                buildingId + "동 · " +
                (includeSecondaryEntrances ? "서문+동문" : "서문 단일") +
                " · 주차 N=" + blockingVehicleCount + " 경로 계산 중";
            _planningTask = Task.Run(() =>
            {
                ApartmentComplexScenarioV2 complex =
                    ApartmentComplexScenarioFactoryV2.BuildDensity(
                        blockingVehicleCount,
                        timing);
                ApartmentComplexPlanResultV2 complexPlan =
                    ApartmentComplexEmergencyPlannerV2.Solve(
                        complex,
                        new ApartmentFireIncidentV2(buildingId),
                        includeSecondaryEntrances,
                        activeRobotCount: 4,
                        generationOptions:
                            new EmergencyAccessRouteGenerationOptionsV2
                            {
                                MaxRoutes = 4,
                                MaxCenterlineAttempts = 16,
                                MaxSearchExpansions = 100000,
                            },
                        maxTick: 5000);
                return new PreparedScenario
                {
                    BuildingId = buildingId,
                    BlockingVehicleCount = blockingVehicleCount,
                    IncludeSecondaryEntrances = includeSecondaryEntrances,
                    Complex = complex,
                    ComplexPlan = complexPlan,
                };
            });
        }

        private void CompletePendingPlanning()
        {
            if (_planningTask == null || !_planningTask.IsCompleted) return;
            Task<PreparedScenario> completed = _planningTask;
            _planningTask = null;
            if (completed.IsFaulted || completed.IsCanceled)
            {
                string reason = completed.Exception == null
                    ? "계산이 취소됨"
                    : completed.Exception.GetBaseException().Message;
                _inputStatus = "경로 계산 실패: " + reason;
                Debug.LogError("[Model V2] " + _inputStatus);
                return;
            }
            ApplyPreparedScenario(completed.Result);
        }

        private void ApplyPreparedScenario(PreparedScenario prepared)
        {
            ApartmentComplexPlanResultV2 complexPlan = prepared.ComplexPlan;
            if (!complexPlan.Success)
            {
                _inputStatus =
                    prepared.BuildingId + "동 자동경로 실패: " +
                    complexPlan.FailReason;
                Debug.LogWarning("[Model V2] " + _inputStatus);
                return;
            }
            _fireBuildingId = prepared.BuildingId;
            _blockingVehicleCount = prepared.BlockingVehicleCount;
            _requestedBlockingVehicleCount = _blockingVehicleCount;
            _includeSecondaryEntrances = prepared.IncludeSecondaryEntrances;
            _complex = prepared.Complex;
            AutomaticEmergencyAccessPlanResultV2 automatic =
                complexPlan.Selected.AutomaticPlan;
            EmergencyScenarioBuildResultV2 built = automatic.Plan.Selected.Scenario;
            PipelinedPlanResultV2 plan = automatic.Plan.Selected.Plan;
            _selectedRoute = automatic.Plan.Selected.Route;
            _candidateRoutes = automatic.Generation.Routes;
            _selectedEntrance = complexPlan.Selected.Entrance;
            _fireBuilding = _complex.FindBuilding(_fireBuildingId);
            _fireCell = _fireBuilding.FireEngineZone.ApproachCell;
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
            _scenarioName =
                "8동 단지 " + _fireBuildingId + "동 화재 · " +
                (_includeSecondaryEntrances ? "서문+동문" : "서문 단일") +
                " · N=" + _blockingVehicleCount;
            _routeName = _selectedEntrance.Name + "/" + _selectedRoute.Name;
            _movedVehicleCount = built.SelectedVehicleCount;
            _additionalVehicleCount = _complex.BaseProblem.VehicleCount;
            _inputStatus =
                "화재 " + _fireBuildingId + "동 · 종점 " +
                _fireBuilding.FireEngineZone.Name + " · 후보 " +
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
            CompletePendingPlanning();
            int transportCamera;
            if (TransportCameraKeyPressed(out transportCamera))
                SelectTransportCamera(transportCamera);
            if (PauseTogglePressed()) _paused = !_paused;
            if (ReplayPressed())
            {
                _time = 0f;
                ApplyTick(0f);
            }
            Vector2 pointerPosition;
            if (PointerPressed(out pointerPosition) &&
                !IsPointerOverHud(pointerPosition) &&
                _planningTask == null)
            {
                int clickedBuildingId;
                string failure;
                if (TryResolveClickedBuilding(
                        pointerPosition, out clickedBuildingId, out failure))
                {
                    BeginPresetLoad(
                        _includeSecondaryEntrances ? 1 : 0,
                        clickedBuildingId,
                        _blockingVehicleCount);
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
            UpdateCameraNavigation();
        }

        private static bool TransportCameraKeyPressed(out int cameraIndex)
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.digit1Key.wasPressedThisFrame) cameraIndex = 0;
                else if (keyboard.digit2Key.wasPressedThisFrame) cameraIndex = 1;
                else if (keyboard.digit3Key.wasPressedThisFrame) cameraIndex = 2;
                else if (keyboard.digit4Key.wasPressedThisFrame) cameraIndex = 3;
                else
                {
                    cameraIndex = -1;
                    return false;
                }
                return true;
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Alpha1)) cameraIndex = 0;
            else if (Input.GetKeyDown(KeyCode.Alpha2)) cameraIndex = 1;
            else if (Input.GetKeyDown(KeyCode.Alpha3)) cameraIndex = 2;
            else if (Input.GetKeyDown(KeyCode.Alpha4)) cameraIndex = 3;
            else
            {
                cameraIndex = -1;
                return false;
            }
            return true;
#endif
            cameraIndex = -1;
            return false;
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

        private static Rect ControlPanelBounds()
        {
            return new Rect(
                Mathf.Max(12f, Screen.width - ControlPanelWidth - 12f),
                12f,
                ControlPanelWidth,
                ControlPanelHeight);
        }

        private static bool IsPointerOverHud(Vector2 screenPosition)
        {
            Vector2 guiPosition = new Vector2(
                screenPosition.x,
                Screen.height - screenPosition.y);
            return GuideBounds.Contains(guiPosition) ||
                   ControlPanelBounds().Contains(guiPosition);
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

        private bool TryResolveClickedBuilding(
            Vector2 screenPosition,
            out int buildingId,
            out string failure)
        {
            Camera camera = ActiveViewCamera();
            if (camera == null) camera = Object.FindAnyObjectByType<Camera>();
            if (camera == null)
            {
                buildingId = 0;
                failure = "활성 카메라가 없어 화재동을 선택할 수 없음";
                return false;
            }
            Ray ray = camera.ScreenPointToRay(screenPosition);
            RaycastHit hit;
            if (!Physics.Raycast(ray, out hit, 100f))
            {
                buildingId = 0;
                failure = "아파트동을 클릭해야 함";
                return false;
            }
            (int X, int Y) cell = (
                Mathf.RoundToInt(hit.point.x),
                Mathf.RoundToInt(hit.point.z));
            ApartmentBuildingV2 building = _complex == null
                ? null
                : _complex.Buildings.FirstOrDefault(candidate =>
                    candidate.FootprintCells.Contains(cell));
            if (building == null)
            {
                buildingId = 0;
                failure =
                    "선택 위치 (" + cell.X + "," + cell.Y +
                    ")에 등록된 아파트동이 없음";
                return false;
            }
            buildingId = building.Id;
            failure = null;
            return true;
        }

        private Camera ActiveViewCamera()
        {
            if (_selectedTransportCamera >= 0 &&
                _transportCameras != null &&
                _selectedTransportCamera < _transportCameras.Length)
                return _transportCameras[_selectedTransportCamera];
            return _presentationCamera != null
                ? _presentationCamera
                : Camera.main;
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
                    new Color(0.10f, 0.34f, 0.82f),
                    0.005f,
                    0.58f);
            }
            BuildRouteOverlay(
                _selectedRoute,
                "SelectedRoute-" + _selectedRoute.Name,
                new Color(0.02f, 0.92f, 0.94f),
                0.025f,
                0.90f,
                SimulationVisualLayer.Control);
            BuildThreeDimensionalRouteBoundary(_selectedRoute);
        }

        private void BuildApartmentContext()
        {
            if (_complex == null) return;
            foreach (ApartmentBuildingV2 apartment in _complex.Buildings)
            {
                int minX = apartment.FootprintCells.Min(cell => cell.X);
                int maxX = apartment.FootprintCells.Max(cell => cell.X);
                int minY = apartment.FootprintCells.Min(cell => cell.Y);
                int maxY = apartment.FootprintCells.Max(cell => cell.Y);
                float width = maxX - minX + 0.82f;
                float depth = maxY - minY + 0.82f;
                float height = 8.5f + ((apartment.Id - 101) % 4) * 0.9f;
                bool fireBuilding = apartment.Id == _fireBuildingId;
                BuildApartmentBuilding(
                    apartment.Id + "-Apartment",
                    new Vector3(
                        (minX + maxX) * 0.5f,
                        0f,
                        (minY + maxY) * 0.5f),
                    width,
                    depth,
                    height,
                    floors: 9 + ((apartment.Id - 101) % 4),
                    southColumns: 6,
                    westColumns: 5,
                    bodyColor: fireBuilding
                        ? new Color(0.56f, 0.50f, 0.48f)
                        : new Color(0.43f, 0.48f, 0.54f),
                    accentColor: fireBuilding
                        ? new Color(0.82f, 0.16f, 0.10f)
                        : new Color(0.12f, 0.46f, 0.68f),
                    variant: apartment.Id - 101);
            }
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
            Color accentColor,
            int variant)
        {
            GameObject building =
                SimulationVisualAssetFactory.TryCreateApartment(variant, name);
            if (building != null)
            {
                Track(building, SimulationVisualLayer.ThreeDimensional);
                FitVisualToBounds(
                    building,
                    origin,
                    new Vector3(width, height, depth));
                DisableColliders(building);
                BuildBuildingNumber(name, origin, width, height, depth);
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
            BuildBuildingNumber(name, origin, width, height, depth);
        }

        private static void FitVisualToBounds(
            GameObject visual,
            Vector3 targetBottomCenter,
            Vector3 targetSize)
        {
            Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                visual.transform.position = targetBottomCenter;
                return;
            }
            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
                bounds.Encapsulate(renderers[index].bounds);
            visual.transform.localScale = new Vector3(
                targetSize.x / Mathf.Max(0.001f, bounds.size.x),
                targetSize.y / Mathf.Max(0.001f, bounds.size.y),
                targetSize.z / Mathf.Max(0.001f, bounds.size.z));
            bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
                bounds.Encapsulate(renderers[index].bounds);
            visual.transform.position += new Vector3(
                targetBottomCenter.x - bounds.center.x,
                targetBottomCenter.y - bounds.min.y,
                targetBottomCenter.z - bounds.center.z);
        }

        private void BuildBuildingNumber(
            string name,
            Vector3 bottomCenter,
            float width,
            float height,
            float depth)
        {
            var label = new GameObject(name + "-Label");
            Track(label, SimulationVisualLayer.ThreeDimensional);
            label.transform.position = bottomCenter + new Vector3(
                0f, height * 0.55f, -depth * 0.51f);
            TextMesh text = label.AddComponent<TextMesh>();
            text.text = name.Substring(0, 3) + "동";
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.characterSize = Mathf.Max(0.18f, width * 0.035f);
            text.fontSize = 64;
            text.color = Color.white;
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
                marker.transform.localScale = new Vector3(scale, 0.018f, scale);
                SetColor(marker, color);
                Collider collider = marker.GetComponent<Collider>();
                if (collider != null) Destroy(collider);
            }
        }

        private void BuildThreeDimensionalRouteBoundary(
            EmergencyAccessRouteV2 route)
        {
            var cells = new HashSet<(int X, int Y)>(route.RequiredCells);
            Color lineColor = new Color(1f, 0.63f, 0.04f);
            int index = 0;
            foreach (var cell in route.RequiredCells)
            {
                if (!cells.Contains((cell.X - 1, cell.Y)))
                    BuildRoadBoundarySegment(
                        "FireLane-West-" + index,
                        new Vector3(cell.X - 0.47f, 0.015f, cell.Y),
                        new Vector3(0.055f, 0.014f, 0.94f),
                        lineColor);
                if (!cells.Contains((cell.X + 1, cell.Y)))
                    BuildRoadBoundarySegment(
                        "FireLane-East-" + index,
                        new Vector3(cell.X + 0.47f, 0.015f, cell.Y),
                        new Vector3(0.055f, 0.014f, 0.94f),
                        lineColor);
                if (!cells.Contains((cell.X, cell.Y - 1)))
                    BuildRoadBoundarySegment(
                        "FireLane-South-" + index,
                        new Vector3(cell.X, 0.015f, cell.Y - 0.47f),
                        new Vector3(0.94f, 0.014f, 0.055f),
                        lineColor);
                if (!cells.Contains((cell.X, cell.Y + 1)))
                    BuildRoadBoundarySegment(
                        "FireLane-North-" + index,
                        new Vector3(cell.X, 0.015f, cell.Y + 0.47f),
                        new Vector3(0.94f, 0.014f, 0.055f),
                        lineColor);
                index++;
            }
        }

        private void BuildRoadBoundarySegment(
            string name,
            Vector3 position,
            Vector3 scale,
            Color color)
        {
            var segment = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Track(segment, SimulationVisualLayer.ThreeDimensional);
            segment.name = name;
            segment.transform.position = position;
            segment.transform.localScale = scale;
            SetColor(segment, color);
            Collider collider = segment.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
        }

        private void BuildFireMarker()
        {
            if (_fireBuilding == null) return;
            bool northRow = _fireBuilding.Id <= 104;
            int facadeY = northRow
                ? _fireBuilding.FootprintCells.Max(cell => cell.Y)
                : _fireBuilding.FootprintCells.Min(cell => cell.Y);
            var fire = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Track(fire, SimulationVisualLayer.Shared);
            fire.name = "Building-Fire-" + _fireBuilding.Id;
            fire.transform.position = new Vector3(
                _fireBuilding.FireEngineZone.ApproachCell.X,
                4.8f,
                facadeY + (northRow ? 0.48f : -0.48f));
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
            if (_selectedEntrance == null) return;
            var gate = new GameObject("Emergency-Entrance");
            Track(gate, SimulationVisualLayer.Shared);
            gate.transform.position = new Vector3(
                _selectedEntrance.Cell.X,
                0f,
                _selectedEntrance.Cell.Y);
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
                BuildControlMovableVehicleMarker(vehicle, pose);
            }
        }

        private void BuildControlMovableVehicleMarker(
            int vehicle,
            VehiclePose pose)
        {
            var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Track(marker, SimulationVisualLayer.Control);
            marker.name = "MoveTarget-" + (vehicle + 1);
            var second = pose.SecondCell;
            marker.transform.position = new Vector3(
                (pose.X + second.X) / 2f,
                0.052f,
                (pose.Y + second.Y) / 2f);
            marker.transform.localScale =
                pose.Orientation == VehicleOrientation.Horizontal
                    ? new Vector3(2.18f, 0.018f, 1.08f)
                    : new Vector3(1.08f, 0.018f, 2.18f);
            SetColor(marker, new Color(1f, 0.43f, 0.04f));
            Collider collider = marker.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
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
            _transportCameras = new Camera[_plan.RobotTimelines.Length];
            _transportCameraFocusOffsets =
                new Vector3[_plan.RobotTimelines.Length];
            _transportCameraYaws = new float[_plan.RobotTimelines.Length];
            _transportCameraPitches = new float[_plan.RobotTimelines.Length];
            _transportCameraDistances = new float[_plan.RobotTimelines.Length];
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
                _transportCameras[robot] =
                    BuildTransportCamera(cube.transform, robot);
                _transportCameraFocusOffsets[robot] =
                    new Vector3(0.42f, 0.12f, 0f);
                _transportCameraYaws[robot] = 90f;
                _transportCameraPitches[robot] = 25f;
                _transportCameraDistances[robot] = 2.2f;
                ApplyTransportCameraPose(robot);
            }
        }

        private static Camera BuildTransportCamera(
            Transform transport,
            int robotIndex)
        {
            var cameraObject = new GameObject(
                "TransportUnit-Camera-" + (robotIndex + 1));
            cameraObject.transform.SetParent(transport, worldPositionStays: false);
            cameraObject.transform.localPosition = new Vector3(-1.55f, 1.05f, 0f);
            cameraObject.transform.localRotation = Quaternion.LookRotation(
                new Vector3(1.95f, -0.78f, 0f),
                Vector3.up);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.fieldOfView = 62f;
            camera.nearClipPlane = 0.08f;
            camera.farClipPlane = 120f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.025f, 0.04f, 0.07f);
            cameraObject.SetActive(false);
            return camera;
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
            _selectedTransportCamera = -1;
            _visualLayers.SetMode(_visualMode);
            _presentationCamera = _cameraController.Apply(
                _visualMode,
                _problem.Width,
                _problem.Height,
                _presentationCamera);
            _presentationCamera.enabled = true;
            if (_visualMode == SimulationVisualMode.ThreeDimensional)
            {
                EnsurePresentationCameraNavigation();
                ApplyPresentationCameraPose();
            }
            Debug.Log(
                "[Model V2] camera=" + _presentationCamera.name +
                (_visualMode == SimulationVisualMode.Control
                    ? ", control"
                    : ", three-dimensional"));
        }

        private void SelectTransportCamera(int cameraIndex)
        {
            if (_transportCameras == null ||
                cameraIndex < 0 ||
                cameraIndex >= _transportCameras.Length)
            {
                _inputStatus =
                    "운송유닛 " + (cameraIndex + 1) + " 카메라를 사용할 수 없음";
                return;
            }
            _visualMode = SimulationVisualMode.ThreeDimensional;
            if (_visualLayers != null) _visualLayers.SetMode(_visualMode);
            if (_presentationCamera != null)
                _presentationCamera.enabled = false;
            for (int index = 0; index < _transportCameras.Length; index++)
            {
                Camera camera = _transportCameras[index];
                if (camera == null) continue;
                bool selected = index == cameraIndex;
                camera.gameObject.SetActive(selected);
                camera.enabled = selected;
            }
            _selectedTransportCamera = cameraIndex;
            ApplyTransportCameraPose(cameraIndex);
            _inputStatus =
                "운송유닛 " + (cameraIndex + 1) +
                " 추적 카메라 · 관제/3D 버튼으로 전체 화면 복귀";
        }

        private void UpdateCameraNavigation()
        {
            bool tracking = _selectedTransportCamera >= 0;
            if (!tracking &&
                _visualMode != SimulationVisualMode.ThreeDimensional)
                return;

            Vector2 move;
            Vector2 orbit;
            float zoom;
            bool fast;
            ReadCameraNavigationInput(out move, out orbit, out zoom, out fast);
            float deltaTime = Time.unscaledDeltaTime;
            if (tracking)
            {
                int index = _selectedTransportCamera;
                if (_transportCameras == null ||
                    index >= _transportCameras.Length)
                    return;
                float distance = _transportCameraDistances[index];
                float speed =
                    Mathf.Max(0.55f, distance * 0.62f) *
                    deltaTime * (fast ? 2.5f : 1f);
                Quaternion heading = Quaternion.Euler(
                    0f, _transportCameraYaws[index], 0f);
                Vector3 offset = _transportCameraFocusOffsets[index];
                offset +=
                    (heading * Vector3.right * move.x +
                     heading * Vector3.forward * move.y) * speed;
                offset.x = Mathf.Clamp(offset.x, -5f, 5f);
                offset.z = Mathf.Clamp(offset.z, -5f, 5f);
                _transportCameraFocusOffsets[index] = offset;
                _transportCameraYaws[index] += orbit.x;
                _transportCameraPitches[index] = Mathf.Clamp(
                    _transportCameraPitches[index] - orbit.y,
                    8f,
                    78f);
                _transportCameraDistances[index] = Mathf.Clamp(
                    distance * Mathf.Exp(-zoom * 0.34f),
                    0.85f,
                    8f);
                ApplyTransportCameraPose(index);
                return;
            }

            EnsurePresentationCameraNavigation();
            float presentationSpeed =
                Mathf.Max(3f, _presentationCameraDistance * 0.34f) *
                deltaTime * (fast ? 2.5f : 1f);
            Quaternion presentationHeading =
                Quaternion.Euler(0f, _presentationCameraYaw, 0f);
            _presentationCameraFocus +=
                (presentationHeading * Vector3.right * move.x +
                 presentationHeading * Vector3.forward * move.y) *
                presentationSpeed;
            _presentationCameraYaw += orbit.x;
            _presentationCameraPitch = Mathf.Clamp(
                _presentationCameraPitch - orbit.y,
                12f,
                78f);
            _presentationCameraDistance = Mathf.Clamp(
                _presentationCameraDistance * Mathf.Exp(-zoom * 0.34f),
                12f,
                110f);
            ApplyPresentationCameraPose();
        }

        private void EnsurePresentationCameraNavigation()
        {
            if (_presentationCameraNavigationInitialized || _problem == null)
                return;
            _presentationCameraFocus = new Vector3(
                (_problem.Width - 1) / 2f,
                0f,
                (_problem.Height - 1) / 2f);
            _presentationCameraYaw = 0f;
            _presentationCameraPitch = 42f;
            _presentationCameraDistance =
                Mathf.Max(_problem.Width, _problem.Height) * 1.15f;
            _presentationCameraNavigationInitialized = true;
        }

        private void ApplyPresentationCameraPose()
        {
            if (_presentationCamera == null) return;
            Quaternion orbit = Quaternion.Euler(
                _presentationCameraPitch,
                _presentationCameraYaw,
                0f);
            Vector3 forward = orbit * Vector3.forward;
            _presentationCamera.transform.position =
                _presentationCameraFocus -
                forward * _presentationCameraDistance;
            _presentationCamera.transform.rotation =
                Quaternion.LookRotation(forward, Vector3.up);
        }

        private void ApplyTransportCameraPose(int index)
        {
            if (_transportCameras == null ||
                _transportCameraFocusOffsets == null ||
                index < 0 ||
                index >= _transportCameras.Length ||
                _transportCameras[index] == null)
                return;
            Quaternion orbit = Quaternion.Euler(
                _transportCameraPitches[index],
                _transportCameraYaws[index],
                0f);
            Vector3 forward = orbit * Vector3.forward;
            Transform cameraTransform = _transportCameras[index].transform;
            cameraTransform.localPosition =
                _transportCameraFocusOffsets[index] -
                forward * _transportCameraDistances[index];
            cameraTransform.localRotation =
                Quaternion.LookRotation(forward, Vector3.up);
        }

        private static void ReadCameraNavigationInput(
            out Vector2 move,
            out Vector2 orbit,
            out float zoom,
            out bool fast)
        {
            move = Vector2.zero;
            orbit = Vector2.zero;
            zoom = 0f;
            fast = false;
#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                move.x =
                    (keyboard.dKey.isPressed ? 1f : 0f) -
                    (keyboard.aKey.isPressed ? 1f : 0f);
                move.y =
                    (keyboard.wKey.isPressed ? 1f : 0f) -
                    (keyboard.sKey.isPressed ? 1f : 0f);
                fast =
                    keyboard.leftShiftKey.isPressed ||
                    keyboard.rightShiftKey.isPressed;
            }
            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                if (mouse.rightButton.isPressed)
                    orbit = mouse.delta.ReadValue() * 0.16f;
                zoom = mouse.scroll.ReadValue().y / 120f;
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            move.x =
                (Input.GetKey(KeyCode.D) ? 1f : 0f) -
                (Input.GetKey(KeyCode.A) ? 1f : 0f);
            move.y =
                (Input.GetKey(KeyCode.W) ? 1f : 0f) -
                (Input.GetKey(KeyCode.S) ? 1f : 0f);
            fast =
                Input.GetKey(KeyCode.LeftShift) ||
                Input.GetKey(KeyCode.RightShift);
            if (Input.GetMouseButton(1))
                orbit = new Vector2(
                    Input.GetAxis("Mouse X") * 3.2f,
                    Input.GetAxis("Mouse Y") * 3.2f);
            zoom = Input.GetAxis("Mouse ScrollWheel") * 10f;
#endif
            if (move.sqrMagnitude > 1f) move.Normalize();
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
            DrawGuidePanel();
            DrawControlPanel();
            if (_planningTask != null) DrawPlanningOverlay();
        }

        private void DrawGuidePanel()
        {
            GUI.Box(GuideBounds, string.Empty);
            if (_plan == null)
            {
                GUI.Label(new Rect(24f, 22f, 596f, 24f),
                    "Model V2 — 아파트 단지 비상 진입 시뮬레이션");
                GUI.Label(new Rect(24f, 50f, 596f, 24f),
                    _inputStatus ?? "초기 경로를 준비하는 중");
                GUI.Label(new Rect(24f, 82f, 596f, 24f),
                    "경로 계산이 끝나면 시뮬레이션이 자동으로 시작됩니다.");
                GUI.Label(new Rect(24f, 116f, 596f, 24f),
                    "[WASD] 기준점 이동  [우클릭 드래그] 회전  [휠] 빠른 줌");
                return;
            }
            double seconds = _timeProfile.PlanSeconds(_plan.Ticks);
            GUI.Label(new Rect(24f, 22f, 596f, 24f), "Model V2 — " + _scenarioName);
            GUI.Label(new Rect(24f, 46f, 596f, 24f),
                "경로 " + _routeName + " · 이동 " + _movedVehicleCount + "/" +
                _additionalVehicleCount + "대 · 운송유닛 " +
                _plan.RobotTimelines.Length + "조");
            GUI.Label(new Rect(24f, 70f, 596f, 24f),
                "확보 " + _plan.Ticks + "틱 / " + seconds.ToString("0.0") +
                "초(Stanley 1m/s 참조)" +
                " · 재생 t=" + _displayTick.ToString("0.0"));
            GUI.Label(new Rect(24f, 94f, 596f, 24f),
                SensitivityStatusText(seconds));
            GUI.Label(new Rect(24f, 118f, 596f, 24f), ServiceStatusText());
            GUI.Label(new Rect(24f, 142f, 596f, 24f), _inputStatus);
            GUI.Label(new Rect(24f, 166f, 596f, 24f),
                "관제 청록=선택 · 파랑=대안 · 주황=이동차량 / 3D 노랑=확보경계");
            string cameraLabel = _selectedTransportCamera >= 0
                ? "현재 유닛 " + (_selectedTransportCamera + 1)
                : _visualMode == SimulationVisualMode.Control
                    ? "현재 관제모드"
                    : "현재 3D모드";
            GUI.Label(new Rect(24f, 190f, 596f, 24f),
                "[1~4] 운송유닛 추적 카메라  ·  " + cameraLabel);
            GUI.Label(new Rect(24f, 214f, 596f, 24f),
                "[WASD] 기준점 이동  [우클릭 드래그] 회전  [휠] 빠른 줌  [Shift] 가속");
            GUI.Label(new Rect(24f, 238f, 596f, 20f),
                "[Space] 일시정지/재생  [R] 처음부터");
        }

        private void DrawControlPanel()
        {
            Rect panel = ControlPanelBounds();
            GUI.Box(panel, string.Empty);
            float x = panel.x + 12f;
            float y = panel.y + 10f;
            GUI.Label(new Rect(x, y, 260f, 22f), "화면");
            y += 24f;
            if (DrawActionButton(
                    new Rect(x, y, 124f, 32f),
                    "관제모드",
                    _visualMode == SimulationVisualMode.Control,
                    true))
            {
                _visualMode = SimulationVisualMode.Control;
                ApplyVisualMode();
            }
            if (DrawActionButton(
                    new Rect(x + 132f, y, 124f, 32f),
                    "3D모드",
                    _visualMode == SimulationVisualMode.ThreeDimensional,
                    true))
            {
                _visualMode = SimulationVisualMode.ThreeDimensional;
                ApplyVisualMode();
            }

            y += 42f;
            GUI.Label(new Rect(x, y, 260f, 22f), "소방차 진입 조건");
            y += 24f;
            bool shownSecondary = _planningTask != null
                ? _pendingIncludeSecondaryEntrances
                : _includeSecondaryEntrances;
            bool canReplan = _planningTask == null && _timeProfile != null;
            if (DrawActionButton(
                    new Rect(x, y, 124f, 32f),
                    "서문 단일",
                    !shownSecondary,
                    canReplan))
                BeginPresetLoad(0, _fireBuildingId, _blockingVehicleCount);
            if (DrawActionButton(
                    new Rect(x + 132f, y, 124f, 32f),
                    "서문+동문",
                    shownSecondary,
                    canReplan))
                BeginPresetLoad(1, _fireBuildingId, _blockingVehicleCount);

            y += 42f;
            GUI.Label(new Rect(x, y, 260f, 22f), "진입로 가변주차 밀도");
            y += 22f;
            bool previousEnabled = GUI.enabled;
            GUI.enabled = canReplan;
            float selectedDensity = GUI.HorizontalSlider(
                new Rect(x, y + 4f, 196f, 20f),
                _requestedBlockingVehicleCount,
                0f,
                ApartmentComplexScenarioFactoryV2.MaximumBlockingVehicles);
            _requestedBlockingVehicleCount = Mathf.RoundToInt(selectedDensity);
            GUI.enabled = previousEnabled;
            GUI.Label(
                new Rect(x + 204f, y, 52f, 22f),
                _requestedBlockingVehicleCount + "/22대");
            y += 26f;
            bool densityApplied =
                _planningTask == null &&
                _requestedBlockingVehicleCount == _blockingVehicleCount;
            if (DrawActionButton(
                    new Rect(x, y, 256f, 30f),
                    densityApplied
                        ? "현재 밀도 적용됨"
                        : "선택 밀도 적용",
                    densityApplied,
                    canReplan && !densityApplied))
                BeginPresetLoad(
                    _includeSecondaryEntrances ? 1 : 0,
                    _fireBuildingId,
                    _requestedBlockingVehicleCount);

            y += 40f;
            GUI.Label(new Rect(x, y, 260f, 22f), "재생");
            y += 24f;
            bool canPlayback = _plan != null;
            if (DrawActionButton(
                    new Rect(x, y, 124f, 32f),
                    _paused ? "재생" : "일시정지",
                    _paused,
                    canPlayback))
                _paused = !_paused;
            if (DrawActionButton(
                    new Rect(x + 132f, y, 124f, 32f),
                    "처음부터",
                    false,
                    canPlayback))
            {
                _time = 0f;
                ApplyTick(0f);
            }
        }

        private static bool DrawActionButton(
            Rect rect,
            string label,
            bool selected,
            bool enabled)
        {
            Color previousColor = GUI.color;
            bool previousEnabled = GUI.enabled;
            if (selected)
            {
                GUI.color = new Color(0.10f, 0.92f, 1f, 1f);
                GUI.Box(new Rect(
                    rect.x - 3f,
                    rect.y - 3f,
                    rect.width + 6f,
                    rect.height + 6f), string.Empty);
            }
            GUI.color = previousColor;
            GUI.enabled = enabled;
            bool clicked = GUI.Button(rect, selected ? "● " + label : label);
            GUI.enabled = previousEnabled;
            return clicked;
        }

        private void DrawPlanningOverlay()
        {
            const float width = 420f;
            const float height = 70f;
            Rect overlay = new Rect(
                (Screen.width - width) / 2f,
                18f,
                width,
                height);
            GUI.Box(overlay, string.Empty);
            float elapsed = Time.realtimeSinceStartup - _planningStartedAt;
            string target =
                _pendingBuildingId + "동 · " +
                (_pendingIncludeSecondaryEntrances
                    ? "서문+동문 비교"
                    : "서문 단일") +
                " · N=" + _pendingBlockingVehicleCount;
            GUI.Label(
                new Rect(overlay.x + 14f, overlay.y + 8f, width - 28f, 22f),
                target + " 경로 계산 중 · " + elapsed.ToString("0.0") + "초");
            Rect track = new Rect(
                overlay.x + 14f,
                overlay.y + 39f,
                width - 28f,
                16f);
            GUI.Box(track, string.Empty);
            const float segmentWidth = 92f;
            float travel = Mathf.Max(0f, track.width - segmentWidth - 4f);
            float offset = Mathf.PingPong(
                Time.realtimeSinceStartup * 155f,
                travel);
            Color previousColor = GUI.color;
            GUI.color = new Color(0.10f, 0.92f, 1f, 1f);
            GUI.Box(
                new Rect(
                    track.x + 2f + offset,
                    track.y + 2f,
                    segmentWidth,
                    track.height - 4f),
                string.Empty);
            GUI.color = previousColor;
        }

        private static string SensitivityStatusText(double seconds)
        {
            return "도착시간 민감도: 5분 " +
                   (seconds <= 300.0 ? "통과" : "초과") +
                   " · 7분(기준) " +
                   (seconds <= 420.0 ? "통과" : "초과") +
                   " · 9분 " +
                   (seconds <= 540.0 ? "통과" : "초과");
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
