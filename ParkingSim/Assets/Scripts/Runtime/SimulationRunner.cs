using System.Collections.Generic;
using ParkingSim.Core.V2;
using UnityEngine;

namespace ParkingSim.Runtime
{
    /// <summary>
    /// Model V2 최소 통합 검증기.
    /// 인스펙터 연결 없이 실제 소형 주차 블록과 exact 상태 타임라인을 생성해 재생한다.
    /// 차량은 lift 뒤에도 사라지지 않고 AGV 위에서 이동한 뒤 유한 적치면을 계속 점유한다.
    /// </summary>
    public sealed class SimulationRunner : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Object.FindAnyObjectByType<SimulationRunner>() != null) return;
            var go = new GameObject("ModelV2-SimBootstrap");
            go.AddComponent<SimulationRunner>();
        }

        private const float SecondsPerTick = 0.35f;
        private const float EndHoldTicks = 4f;
        private static readonly AsciiMapV2 ActiveMap = V2MapCatalog.SmallParkingBlock;

        private EmergencyProblemV2 _problem;
        private ExactEmergencyResultV2 _plan;
        private readonly Dictionary<int, GameObject> _carViews = new Dictionary<int, GameObject>();
        private GameObject[] _robotViews;
        private float _time;

        private void Start()
        {
            _problem = ActiveMap.Build();
            _plan = ExactEmergencySolverV2.SolveWeighted(
                _problem,
                heuristicWeight: 1,
                maxExpansions: 1000000,
                activeRobotCount: 2,
                captureTimeline: true);

            if (!_plan.Success || _plan.Timeline.Count == 0)
            {
                Debug.LogError("[Model V2] exact 계획 실패: " + _plan.FailReason);
                enabled = false;
                return;
            }

            BuildGrid();
            BuildCars(_plan.Timeline[0]);
            BuildRobots();
            SetupCamera();
            ApplyFrame(_plan.Timeline[0], _plan.Timeline[0], 0f);

            Debug.Log(
                "[Model V2] map=" + ActiveMap.Name + ", 2로봇·차량보존 재생 시작 — " +
                _problem.Width + "x" + _problem.Height + ", " +
                _plan.Ticks + "틱, 회전 " + _plan.RotationActions + "회, 확장 " +
                _plan.ExpandedStates + "상태");
        }

        private void Update()
        {
            if (_plan == null || _plan.Timeline.Count == 0) return;

            _time += Time.deltaTime;
            float cycleTicks = _plan.Ticks + EndHoldTicks;
            float timelineTick = (_time / SecondsPerTick) % cycleTicks;
            if (timelineTick > _plan.Ticks) timelineTick = _plan.Ticks;

            int aIndex = Mathf.Clamp(Mathf.FloorToInt(timelineTick), 0, _plan.Timeline.Count - 1);
            int bIndex = Mathf.Min(aIndex + 1, _plan.Timeline.Count - 1);
            float fraction = bIndex == aIndex ? 0f : timelineTick - aIndex;
            ApplyFrame(_plan.Timeline[aIndex], _plan.Timeline[bIndex], fraction);
        }

        private void ApplyFrame(StateSnapshotV2 a, StateSnapshotV2 b, float fraction)
        {
            for (int i = 0; i < _robotViews.Length; i++)
            {
                RobotSnapshotV2 ra = a.Robots[i];
                RobotSnapshotV2 rb = b.Robots[i];
                _robotViews[i].transform.position = Vector3.Lerp(
                    RobotPosition(ra), RobotPosition(rb), fraction);
                bool carrying = ra.CarryVehicle >= 0 || rb.CarryVehicle >= 0;
                bool servicing = ra.ServiceRemaining > 0 || rb.ServiceRemaining > 0;
                SetColor(_robotViews[i], RobotColor(i, carrying, servicing));
            }

            for (int i = 0; i < a.Vehicles.Length; i++)
            {
                VehicleSnapshotV2 va = a.Vehicles[i];
                VehicleSnapshotV2 vb = FindVehicle(b, va.VehicleId);
                GameObject view = _carViews[va.VehicleId];
                view.transform.position = Vector3.Lerp(
                    VehiclePosition(va.Pose, va.Carried),
                    VehiclePosition(vb.Pose, vb.Carried),
                    fraction);
                view.transform.rotation = Quaternion.Lerp(
                    VehicleRotation(va.Pose), VehicleRotation(vb.Pose), fraction);
                SetColor(view, va.Carried || vb.Carried
                    ? new Color(1f, 0.55f, 0.08f)
                    : VehicleColor(va.VehicleId));
            }
        }

        private void BuildGrid()
        {
            for (int y = 0; y < _problem.Height; y++)
            {
                for (int x = 0; x < _problem.Width; x++)
                {
                    var tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    tile.name = "Cell-" + x + "-" + y;
                    tile.transform.position = new Vector3(x, _problem.IsFloor(x, y) ? -0.08f : 0.04f, y);
                    tile.transform.localScale = new Vector3(0.94f, _problem.IsFloor(x, y) ? 0.12f : 0.35f, 0.94f);
                    SetColor(tile, CellColor(x, y));
                }
            }
        }

        private void BuildCars(StateSnapshotV2 initial)
        {
            foreach (VehicleSnapshotV2 vehicle in initial.Vehicles)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = "Vehicle-" + (vehicle.VehicleId + 1);
                cube.transform.localScale = new Vector3(1.82f, 0.42f, 0.82f);
                SetColor(cube, VehicleColor(vehicle.VehicleId));
                _carViews.Add(vehicle.VehicleId, cube);
            }
        }

        private void BuildRobots()
        {
            _robotViews = new GameObject[2];
            for (int i = 0; i < _robotViews.Length; i++)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = "AGV-" + (i + 1);
                cube.transform.localScale = new Vector3(0.72f, 0.18f, 0.72f);
                SetColor(cube, RobotColor(i, false, false));
                _robotViews[i] = cube;
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
                (_problem.Width - 1) / 2f, 30f, (_problem.Height - 1) / 2f);
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

        private static VehicleSnapshotV2 FindVehicle(StateSnapshotV2 frame, int vehicleId)
        {
            foreach (VehicleSnapshotV2 vehicle in frame.Vehicles)
                if (vehicle.VehicleId == vehicleId) return vehicle;
            return frame.Vehicles[0];
        }

        private static Vector3 RobotPosition(RobotSnapshotV2 robot)
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

        private static Color VehicleColor(int vehicleId)
        {
            return vehicleId % 2 == 0
                ? new Color(0.90f, 0.22f, 0.20f)
                : new Color(0.72f, 0.18f, 0.72f);
        }

        private static Color RobotColor(int robotIndex, bool carrying, bool servicing)
        {
            if (servicing) return new Color(1f, 0.85f, 0.10f);
            if (carrying) return new Color(1f, 0.48f, 0.05f);
            return robotIndex == 0
                ? new Color(0.10f, 0.62f, 0.95f)
                : new Color(0.12f, 0.88f, 0.76f);
        }

        /// <summary>Built-in(_Color)과 URP/Lit(_BaseColor) 양쪽에서 보이도록 설정.</summary>
        private static void SetColor(GameObject target, Color color)
        {
            Material material = target.GetComponent<Renderer>().material;
            material.color = color;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        }
    }
}
