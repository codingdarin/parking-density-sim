using System.Collections.Generic;
using UnityEngine;
using ParkingSim.Core.Agents;
using ParkingSim.Core.Emergency;
using ParkingSim.Core.Grid;

namespace ParkingSim.Runtime
{
    /// <summary>
    /// D5 최소 통합 (가정 검증용) — "코드생성 전략으로 하루 통합"이 참인지 밟아보는 부트스트랩.
    /// 인스펙터 연결 0: Play 시 코드가 격자·차량·로봇·카메라를 전부 생성하고,
    /// 로봇 1대를 코어(EmergencyPlanner)의 RobotTimeline 출력대로 이동시킨다.
    /// 데모가 아니라 마찰(좌표계·카메라·보간·입력·URP 머티리얼) 조기 발견이 목적.
    ///
    /// 좌표 매핑: 격자 (x,y) → 월드 (x, 0, y). 카메라는 위에서 XZ 평면을 내려다봄(top-down).
    /// </summary>
    public sealed class SimulationRunner : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            var go = new GameObject("SimBootstrap");
            go.AddComponent<SimulationRunner>();
        }

        private const float SecondsPerTick = 0.25f; // 표시용 (실측 2.5초/틱을 10배속)

        private ParkingLot _lot;
        private List<RobotTimeline> _schedule;
        private Dictionary<int, GameObject> _carViews;
        private Dictionary<int, int> _liftTicks;
        private GameObject _agv;
        private int _endTick;
        private float _time;

        private void Start()
        {
            // 1) 코어: 소형 시나리오 계획 (로봇 1대가 전 미션 체이닝)
            _lot = ParkingLayoutBuilder.Build(new LayoutConfig { OccupiedLanes = 1 });
            var plan = EmergencyPlanner.Plan(_lot, new EmergencyConfig { FireMeters = 40, RobotCount = 1 });
            if (!plan.Success || plan.Schedules.Length == 0)
            {
                Debug.LogError($"[Sim] 계획 실패: {plan.FailReason}");
                enabled = false;
                return;
            }
            _schedule = plan.Schedules[0];
            _liftTicks = plan.CarLiftTicks;
            _endTick = plan.EndTick;

            BuildGround();
            BuildCars();
            BuildAgv();
            SetupCamera();

            Debug.Log($"[Sim] 시작 — 격자 {_lot.Grid.Width}x{_lot.Grid.Height}, 로봇1 미션 {_schedule.Count}개, 총 {_endTick}틱");
        }

        private void Update()
        {
            if (_schedule == null) return;

            _time += Time.deltaTime;
            float tf = _time / SecondsPerTick;
            int t = Mathf.FloorToInt(tf);
            float frac = tf - t;

            // 차량: 리프트 틱에 사라짐 (통로가 비워지는 것을 시각화)
            foreach (var kv in _liftTicks)
                if (_carViews.TryGetValue(kv.Key, out var cv) && cv.activeSelf && t >= kv.Value)
                    cv.SetActive(false);

            // 로봇: 코어 타임라인의 틱 간 위치를 선형 보간
            var a = PoseAt(t);
            var b = PoseAt(t + 1);
            var pa = new Vector3(a.X, 0.5f, a.Y);
            var pb = new Vector3(b.X, 0.5f, b.Y);
            _agv.transform.position = Vector3.Lerp(pa, pb, Mathf.Clamp01(frac));
            SetColor(_agv, a.Carrying ? new Color(0.9f, 0.5f, 0.1f) : new Color(0.1f, 0.6f, 0.9f));

            if (t > _endTick) _time = 0f; // 루프 재생
        }

        // ── 코어 타임라인 조회 (체이닝된 미션들에서 tick의 위치) ──
        private (int X, int Y, bool Carrying) PoseAt(int tick)
        {
            RobotTimeline seg = null;
            foreach (var s in _schedule)
            {
                if (s.StartTick <= tick) seg = s;
                else break;
            }
            if (seg == null) seg = _schedule.Count > 0 ? _schedule[0] : null;
            return seg?.At(tick) ?? (0, 0, false);
        }

        // ── 코드 생성 (프리미티브) ──
        private void BuildGround()
        {
            for (int y = 0; y < _lot.Grid.Height; y++)
                for (int x = 0; x < _lot.Grid.Width; x++)
                {
                    var tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    tile.transform.position = new Vector3(x, -0.05f, y);
                    tile.transform.localScale = new Vector3(0.95f, 0.1f, 0.95f);
                    SetColor(tile, CellColor(_lot.Grid.TypeAt(x, y)));
                }
        }

        private void BuildCars()
        {
            _carViews = new Dictionary<int, GameObject>();
            foreach (var car in _lot.Cars)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                var (x2, y2) = car.SecondCell;
                // 1x2 강체: 앵커와 둘째 셀의 중점, 방향에 맞춰 스케일
                cube.transform.position = new Vector3((car.X + x2) / 2f, 0.4f, (car.Y + y2) / 2f);
                cube.transform.localScale = car.Horizontal
                    ? new Vector3(1.9f, 0.8f, 0.9f)
                    : new Vector3(0.9f, 0.8f, 1.9f);
                SetColor(cube, car.InCorridor ? new Color(0.85f, 0.2f, 0.2f) : new Color(0.6f, 0.6f, 0.65f));
                _carViews[car.Id] = cube;
            }
        }

        private void BuildAgv()
        {
            _agv = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _agv.transform.localScale = new Vector3(0.8f, 1f, 0.8f);
            _agv.name = "AGV-1";
        }

        private void SetupCamera()
        {
            // 씬의 실제 렌더 카메라를 직접 재사용 (Camera.main은 태그 의존이라 Unity 6에서 빗나감).
            var cams = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
            Camera cam = cams.Length > 0 ? cams[0] : null;
            for (int i = 1; i < cams.Length; i++) cams[i].gameObject.SetActive(false); // 렌더 주체 단일화
            if (cam == null)
            {
                var camGo = new GameObject("SimCamera") { tag = "MainCamera" };
                cam = camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
            }

            float w = _lot.Grid.Width, h = _lot.Grid.Height;
            float aspect = cam.aspect > 0.01f ? cam.aspect : 16f / 9f;
            float size = Mathf.Max(h / 2f, w / (2f * aspect)) + 1f; // 폭·높이 둘 다 프레임에 들어오게
            cam.orthographic = true;
            cam.orthographicSize = size;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 200f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.12f, 0.12f, 0.14f);
            cam.transform.position = new Vector3(w / 2f, 50f, h / 2f);
            cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // 위에서 XZ 내려다봄
            Debug.Log($"[Sim] 카메라 '{cam.name}' (총 {cams.Length}대) size={size:0.0} aspect={aspect:0.00} pos={cam.transform.position}");
        }

        private static Color CellColor(CellType t)
        {
            switch (t)
            {
                case CellType.Corridor: return new Color(0.20f, 0.22f, 0.26f);
                case CellType.Road: return new Color(0.28f, 0.28f, 0.30f);
                case CellType.Staging: return new Color(0.20f, 0.35f, 0.25f);
                case CellType.Stall: return new Color(0.16f, 0.16f, 0.18f);
                default: return new Color(0.08f, 0.08f, 0.08f); // Outside
            }
        }

        /// <summary>Built-in(_Color)·URP/Lit(_BaseColor) 양쪽 대응 — Unity 6 RP 마찰 선제 방어.</summary>
        private static void SetColor(GameObject go, Color c)
        {
            var m = go.GetComponent<Renderer>().material;
            m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        }
    }
}
