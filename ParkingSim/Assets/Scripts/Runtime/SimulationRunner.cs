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
    public sealed partial class SimulationRunner : MonoBehaviour
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
        private const float RoadSurfaceHeight = -0.02f;
        private const float ParkedVehicleRootHeight = 0.30f;
        private const float CameraWheelZoomExponent = 2.0f;
        private const float CustomVehicleBottomOffset =
            RoadSurfaceHeight - ParkedVehicleRootHeight;

        private sealed class PreparedScenario
        {
            public int BuildingId;
            public int BlockingVehicleCount;
            public bool IncludeSecondaryEntrances;
            public ApartmentComplexScenarioV2 Complex;
            public ApartmentComplexPlanResultV2 ComplexPlan;
            public string DisturbanceFailure;
            public SiteScenarioKind Kind;
        }

        private sealed class TransportLiftVisual
        {
            public Transform[] Decks;
            public Vector3[] DeckRestPositions;
            public Transform[] ArmPivots;
            public Quaternion[] ArmRestRotations;
            public Quaternion[] ArmLiftRotations;
            /// <summary>[0]=후방, [1]=전방 축거 모듈 — 유휴 밀착·도킹 전개 애니메이션용</summary>
            public Transform[] AxleModules;
        }

        /// <summary>유휴·빈 주행 시 모듈 중심 간격 — 전장이 1셀(2.5m)을 넘지 않게 밀착</summary>
        private const float IdleModuleOffsetX = 0.22f;
        /// <summary>차량 축거 도킹 시 모듈 중심 간격 (기존 상시 값)</summary>
        private const float DockedModuleOffsetX = 0.54f;

        private EmergencyProblemV2 _problem;
        private PipelinedPlanResultV2 _plan;
        private ApartmentComplexScenarioV2 _complex;
        private ApartmentBuildingV2 _fireBuilding;
        private ApartmentComplexEntranceV2 _selectedEntrance;
        private PhysicalTimeProfileV2 _timeProfile;
        private EmergencyAccessRouteV2 _selectedRoute;
        private IReadOnlyList<EmergencyAccessRouteV2> _candidateRoutes;
        private readonly Dictionary<int, GameObject> _carViews = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, GameObject> _carTrackingFrames =
            new Dictionary<int, GameObject>();
        private readonly Dictionary<int, PipelinedMissionV2> _missions =
            new Dictionary<int, PipelinedMissionV2>();
        private readonly List<TextMesh> _threeDimensionalLabels =
            new List<TextMesh>();
        private GameObject[] _robotViews;
        private GameObject[] _robotControlMarkers;
        private TextMesh[] _robotControlLabels;
        private GameObject[] _robotServiceIndicators;
        private TransportLiftVisual[] _robotLiftVisuals;
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
        private bool _fireUsesCustomView;
        private Camera _presentationCamera;
        private Material _siteAsphaltMaterial;
        private Material _siteGrassMaterial;
        private Material _siteConcreteMaterial;
        private Material _siteMarkingMaterial;
        private Material _siteFireZoneMaterial;
        private Material _siteGlassMaterial;
        private Material _siteMetalMaterial;
        private Material _siteWoodMaterial;
        private Material _siteFoliageMaterial;
        private Mesh _siteRoadMesh;
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
        /// <summary>재생 방향 — +1 정방향, -1 역재생</summary>
        private float _playbackDirection = 1f;
        /// <summary>재생 배속 — 1 또는 2</summary>
        private float _playbackSpeed = 1f;
        private float _displayTick;
        private float _time;
        private Task<PreparedScenario> _planningTask;
        private int _pendingBuildingId;
        private int _pendingBlockingVehicleCount;
        private bool _pendingIncludeSecondaryEntrances;
        private float _planningStartedAt;
        private static readonly Rect GuideBounds = new Rect(12f, 12f, 620f, 260f);
        private const float ControlPanelWidth = 286f;
        private const float ControlPanelHeight = 434f;

        private void Start()
        {
            _timeProfile = PublishedParkingRobotTimingV2.Create(1.0);
            _fireBuildingId = 104;
            _blockingVehicleCount =
                ApartmentComplexScenarioFactoryV2.MaximumBlockingVehicles;
            _requestedBlockingVehicleCount = _blockingVehicleCount;
            BeginPresetLoad(1, _fireBuildingId, _blockingVehicleCount, _scenarioKind);
            // 첫 계획이 계산되는 동안 스카이박스 대신 단지 현황을 먼저 보여준다.
            // 환경 지오메트리는 계산이 필요 없어 즉시 생성 가능하다.
            BuildScenarioPreview(
                BuildScenario(
                    _scenarioKind,
                    _blockingVehicleCount,
                    _timeProfile.CreateOperationTiming()));
        }

        /// <summary>
        /// 계획 없는 단지 프리뷰 — 도로·건물·주차 차량만 정적으로 생성한다.
        /// 계획이 완료되면 ApplyPreparedScenario가 전체를 재구축하며 대체한다.
        /// </summary>
        private void BuildScenarioPreview(ApartmentComplexScenarioV2 complex)
        {
            _complex = complex;
            _problem = complex.BaseProblem;
            _plan = null;
            _fireBuilding = null;
            _selectedRoute = null;
            _candidateRoutes = null;
            _selectedEntrance = null;
            if (_visualLayers != null && _visualLayers.Root != null)
                Destroy(_visualLayers.Root);
            _visualLayers = new SimulationVisualLayers();
            _carViews.Clear();
            _carTrackingFrames.Clear();
            _missions.Clear();
            _threeDimensionalLabels.Clear();
            BuildPresentationGround();
            BuildSlotAnalysisOverlay();
            BuildApartmentContext();
            BuildFixedCars();
            BuildMovableCars();
            BuildBlockageMarkers();
            SetupLighting();
            ApplyVisualMode();
        }

        private void BeginPresetLoad(
            int preset,
            int buildingId,
            int blockingVehicleCount,
            SiteScenarioKind kind)
        {
            if (_planningTask != null)
            {
                _inputStatus = "기존 경로 계산이 끝난 뒤 다시 선택해야 함";
                return;
            }
            bool includeSecondaryEntrances = preset != 0;
            int availableUnitCount = _availableUnitCount;
            IReadOnlyList<(int X, int Y)> blockedCells = BlockedCellsSnapshot();
            OperationTimingV2 timing = _timeProfile.CreateOperationTiming();
            _pendingBuildingId = buildingId;
            _pendingBlockingVehicleCount = blockingVehicleCount;
            _pendingIncludeSecondaryEntrances = includeSecondaryEntrances;
            _planningStartedAt = Time.realtimeSinceStartup;
            _inputStatus =
                ScenarioDisplayName(kind) + " · " +
                buildingId + "동 화재 · " +
                (includeSecondaryEntrances ? "서문·동문 비교" : "서문만 사용") +
                " · 도로 주차 " + blockingVehicleCount +
                "대 대응 계획 수립 중";
            _planningTask = Task.Run(() =>
            {
                ApartmentComplexScenarioV2 complex =
                    BuildScenario(kind, blockingVehicleCount, timing);
                if (blockedCells.Count > 0)
                {
                    DisturbedComplexBuildResultV2 disturbed =
                        ApartmentComplexDisturbanceV2.Apply(
                            complex,
                            new ComplexDisturbanceV2("화면 봉쇄", blockedCells));
                    if (!disturbed.Success)
                        return new PreparedScenario
                        {
                            BuildingId = buildingId,
                            BlockingVehicleCount = blockingVehicleCount,
                            IncludeSecondaryEntrances = includeSecondaryEntrances,
                            DisturbanceFailure = disturbed.FailReason,
                            Kind = kind,
                        };
                    complex = disturbed.Scenario;
                }
                ApartmentComplexPlanResultV2 complexPlan =
                    ApartmentComplexEmergencyPlannerV2.Solve(
                        complex,
                        new ApartmentFireIncidentV2(buildingId),
                        includeSecondaryEntrances,
                        activeRobotCount: availableUnitCount,
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
                    Kind = kind,
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
            if (prepared.DisturbanceFailure != null)
            {
                _inputStatus = "봉쇄 적용 실패: " + prepared.DisturbanceFailure;
                Debug.LogWarning("[Model V2] " + _inputStatus);
                return;
            }
            ApartmentComplexPlanResultV2 complexPlan = prepared.ComplexPlan;
            if (!complexPlan.Success)
            {
                _inputStatus =
                    prepared.BuildingId + "동 자동경로 실패: " +
                    complexPlan.FailReason;
                Debug.LogWarning("[Model V2] " + _inputStatus);
                return;
            }
            _scenarioKind = prepared.Kind;
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
            _carTrackingFrames.Clear();
            _missions.Clear();
            _threeDimensionalLabels.Clear();
            _problem = built.Problem;
            _plan = plan;
            _scenarioName =
                ScenarioDisplayName(prepared.Kind) + " " +
                _fireBuildingId + "동 화재 · " +
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

            BuildPresentationGround();
            BuildSlotAnalysisOverlay();
            BuildApartmentContext();
            BuildRouteOverlays();
            BuildFireMarker();
            BuildEntranceMarker();
            BuildBlockageMarkers();
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
            UpdateReadinessBoard();
            int transportCamera;
            if (TransportCameraKeyPressed(out transportCamera))
                SelectTransportCamera(transportCamera);
            if (PauseTogglePressed()) _paused = !_paused;
            if (ModeTogglePressed())
            {
                _visualMode = _visualMode == SimulationVisualMode.Control
                    ? SimulationVisualMode.ThreeDimensional
                    : SimulationVisualMode.Control;
                ApplyVisualMode();
            }
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
                if (_blockagePlacementMode)
                {
                    (int X, int Y) roadCell;
                    string blockFailure;
                    if (TryResolveClickedRoadCell(
                            pointerPosition, out roadCell, out blockFailure))
                        HandleBlockageClick(roadCell);
                    else
                        _inputStatus = blockFailure;
                }
                else
                {
                    int clickedBuildingId;
                    string failure;
                    if (TryResolveClickedBuilding(
                            pointerPosition, out clickedBuildingId, out failure))
                    {
                        BeginPresetLoad(
                            _includeSecondaryEntrances ? 1 : 0,
                            clickedBuildingId,
                            _blockingVehicleCount,
                            _scenarioKind);
                    }
                    else
                    {
                        _inputStatus = failure;
                        Debug.LogWarning("[Model V2] " + failure);
                    }
                }
            }
            if (_plan == null)
            {
                // 프리뷰 중에도 카메라 조작·라벨 빌보드는 살아 있어야 한다
                UpdateCameraNavigation();
                ApplyThreeDimensionalLabelFacing();
                return;
            }
            // 재생은 무상태(틱 → 순수 렌더)라 역재생·임의 시점 점프가 안전하다.
            float cycleSeconds = (_plan.Ticks + EndHoldTicks) * SecondsPerTick;
            if (!_paused)
                _time = Mathf.Repeat(
                    _time + Time.deltaTime * _playbackDirection * _playbackSpeed,
                    cycleSeconds);
            float tick = _time / SecondsPerTick;
            ApplyTick(Mathf.Min(tick, _plan.Ticks));
            AnimateFireMarker();
            UpdateCameraNavigation();
            ApplyThreeDimensionalLabelFacing();
        }

    }
}
