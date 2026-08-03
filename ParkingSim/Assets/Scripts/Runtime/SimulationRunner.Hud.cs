using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ParkingSim.Core;
using ParkingSim.Core.V2;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ParkingSim.Runtime
{
    partial class SimulationRunner
    {
        private void OnGUI()
        {
            DrawGuidePanel();
            DrawControlPanel();
            DrawReadinessPanel();
            if (_plan != null) DrawPlaybackBar();
            if (_planningTask != null) DrawPlanningOverlay();
        }

        private void DrawGuidePanel()
        {
            GUI.Box(GuideBounds, string.Empty);
            if (_plan == null)
            {
                GUI.Label(new Rect(24f, 22f, 596f, 24f),
                    "아파트 단지 소방 진입로 확보 시뮬레이션");
                GUI.Label(new Rect(24f, 50f, 596f, 24f),
                    "• 준비 상태: " +
                    (_inputStatus ?? "초기 대응 계획을 준비하고 있습니다."));
                GUI.Label(new Rect(24f, 82f, 596f, 24f),
                    "• 진행 안내: 계획 수립이 완료되면 자동으로 재생됩니다.");
                GUI.Label(new Rect(24f, 116f, 596f, 24f),
                    "• 화면 조작: WASD 이동 · 우클릭 드래그 회전 · 휠 확대/축소");
                return;
            }
            double seconds = _timeProfile.PlanSeconds(_plan.Ticks);
            float progress = _plan.Ticks <= 0
                ? 1f
                : Mathf.Clamp01(_displayTick / _plan.Ticks);
            GUI.Label(new Rect(24f, 22f, 596f, 24f),
                "아파트 단지 소방 진입로 확보 시뮬레이션");
            GUI.Label(new Rect(24f, 46f, 596f, 24f),
                "• 발생 상황: " + _fireBuildingId +
                "동 화재 · 소방차 출입구 " +
                EntranceDisplayName(_selectedEntrance) +
                " · 도로 주차 " + _additionalVehicleCount + "대");
            GUI.Label(new Rect(24f, 70f, 596f, 24f),
                "• 대응 결과: 차량 " + _movedVehicleCount +
                "대 이동 · 운송 장비 " + _plan.RobotTimelines.Length +
                "대 투입 · 진입로 확보 " + FormatDuration(seconds) +
                " · 재생 " + (progress * 100f).ToString("0") + "%");
            GUI.Label(new Rect(24f, 94f, 596f, 24f),
                SensitivityStatusText(seconds));
            GUI.Label(new Rect(24f, 118f, 596f, 24f), ServiceStatusText());
            GUI.Label(new Rect(24f, 142f, 596f, 24f),
                "• 계획 상태: 소방차 진입 구역까지의 접근로 선정 완료");
            GUI.Label(new Rect(24f, 166f, 596f, 24f),
                "• 화면 표시: 청록=선택 경로 · 파랑=대안 · 주황=이동 차량 · " +
                "노랑=확보 경계 · 적갈=도로 봉쇄" +
                (_blockagePlacementMode ? " (배치 모드: 도로 클릭)" : ""));
            string cameraLabel = _selectedTransportCamera >= 0
                ? "운송 장비 " + (_selectedTransportCamera + 1) + "번 추적 중"
                : _visualMode == SimulationVisualMode.Control
                    ? "관제 화면 표시 중"
                    : "3D 현장 화면 표시 중";
            GUI.Label(new Rect(24f, 190f, 596f, 24f),
                "• 카메라 선택: 숫자 1~4 운송 장비 추적 · " + cameraLabel);
            GUI.Label(new Rect(24f, 214f, 596f, 24f),
                "• 화면 조작: WASD 이동 · 우클릭 드래그 회전 · 휠 확대/축소 · Shift 빠른 이동");
            GUI.Label(new Rect(24f, 238f, 596f, 20f),
                "• 재생 조작: Space 일시정지/재생 · R 처음부터 · " +
                "하단 재생바 = 역재생·구간 이동");
        }

        private void DrawControlPanel()
        {
            Rect panel = ControlPanelBounds();
            GUI.Box(panel, string.Empty);
            float x = panel.x + 12f;
            float y = panel.y + 10f;
            GUI.Label(new Rect(x, y, 260f, 22f), "단지 시나리오");
            y += 24f;
            DrawScenarioButtons(
                x, y, _planningTask == null && _timeProfile != null);

            y += 42f;
            GUI.Label(new Rect(x, y, 260f, 22f), "화면 보기");
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
            GUI.Label(new Rect(x, y, 260f, 22f), "소방차 출입구");
            y += 24f;
            bool shownSecondary = _planningTask != null
                ? _pendingIncludeSecondaryEntrances
                : _includeSecondaryEntrances;
            bool canReplan = _planningTask == null && _timeProfile != null;
            if (DrawActionButton(
                    new Rect(x, y, 124f, 32f),
                    "서문만 사용",
                    !shownSecondary,
                    canReplan))
                BeginPresetLoad(0, _fireBuildingId, _blockingVehicleCount, _scenarioKind);
            if (DrawActionButton(
                    new Rect(x + 132f, y, 124f, 32f),
                    "서문·동문 비교",
                    shownSecondary,
                    canReplan))
                BeginPresetLoad(1, _fireBuildingId, _blockingVehicleCount, _scenarioKind);

            y += 42f;
            GUI.Label(new Rect(x, y, 260f, 22f), "도로 주차 차량");
            y += 22f;
            bool previousEnabled = GUI.enabled;
            GUI.enabled = canReplan;
            float selectedDensity = GUI.HorizontalSlider(
                new Rect(x, y + 4f, 196f, 20f),
                _requestedBlockingVehicleCount,
                0f,
                MaxVariableVehicles(_scenarioKind));
            _requestedBlockingVehicleCount = Mathf.Min(
                Mathf.RoundToInt(selectedDensity),
                MaxVariableVehicles(_scenarioKind));
            GUI.enabled = previousEnabled;
            GUI.Label(
                new Rect(x + 204f, y, 52f, 22f),
                _requestedBlockingVehicleCount + "대");
            y += 26f;
            bool densityApplied =
                _planningTask == null &&
                _requestedBlockingVehicleCount == _blockingVehicleCount;
            if (DrawActionButton(
                    new Rect(x, y, 256f, 30f),
                    densityApplied
                        ? "현재 주차 대수 적용됨"
                        : "선택한 주차 대수 적용",
                    densityApplied,
                    canReplan && !densityApplied))
                BeginPresetLoad(
                    _includeSecondaryEntrances ? 1 : 0,
                    _fireBuildingId,
                    _requestedBlockingVehicleCount,
                    _scenarioKind);

            y += 40f;
            GUI.Label(new Rect(x, y, 260f, 22f), "가용 운송 유닛 (충전·고장 이탈)");
            y += 24f;
            for (int units = 4; units >= 1; units--)
            {
                float buttonX = x + (4 - units) * 64f;
                if (DrawActionButton(
                        new Rect(buttonX, y, 58f, 32f),
                        units + "조",
                        _availableUnitCount == units,
                        canReplan && _availableUnitCount != units))
                {
                    _availableUnitCount = units;
                    BeginPresetLoad(
                        _includeSecondaryEntrances ? 1 : 0,
                        _fireBuildingId,
                        _blockingVehicleCount,
                        _scenarioKind);
                }
            }

            y += 42f;
            GUI.Label(new Rect(x, y, 260f, 22f), "도로 봉쇄 (쓰러진 나무)");
            y += 24f;
            if (DrawActionButton(
                    new Rect(x, y, 124f, 32f),
                    "봉쇄 배치 모드",
                    _blockagePlacementMode,
                    _complex != null))
                _blockagePlacementMode = !_blockagePlacementMode;
            if (DrawActionButton(
                    new Rect(x + 132f, y, 124f, 32f),
                    "전체 해제 (" + _blockageSegments.Count + ")",
                    false,
                    canReplan && _blockageSegments.Count > 0))
                ClearBlockages();

        }

        private static Rect PlaybackBarBounds()
        {
            // 상단: 안내 패널 오른쪽 ~ 조작 패널 왼쪽 사이
            float left = GuideBounds.xMax + 12f;
            float width = Mathf.Clamp(
                Screen.width - left - ControlPanelWidth - 36f,
                420f,
                760f);
            return new Rect(left, 12f, width, 64f);
        }

        /// <summary>하단 재생바 — 역재생/일시정지/재생 + 시점 이동 슬라이더.
        /// 재생이 무상태(틱 → 순수 렌더)라 임의 시점 점프·역재생이 안전하다.</summary>
        private void DrawPlaybackBar()
        {
            Rect bar = PlaybackBarBounds();
            GUI.Box(bar, string.Empty);
            GUI.Box(bar, string.Empty);
            float x = bar.x + 12f;
            float y = bar.y + 6f;
            bool reversePlaying = !_paused && _playbackDirection < 0f;
            bool forwardPlaying = !_paused && _playbackDirection > 0f;
            if (DrawActionButton(
                    new Rect(x, y, 88f, 26f),
                    "◀ 역재생",
                    reversePlaying,
                    !reversePlaying))
            {
                _playbackDirection = -1f;
                _paused = false;
            }
            if (DrawActionButton(
                    new Rect(x + 94f, y, 88f, 26f),
                    "∥ 정지",
                    _paused,
                    !_paused))
                _paused = true;
            if (DrawActionButton(
                    new Rect(x + 188f, y, 88f, 26f),
                    "▶ 재생",
                    forwardPlaying,
                    !forwardPlaying))
            {
                _playbackDirection = 1f;
                _paused = false;
            }
            bool doubleSpeed = _playbackSpeed > 1.5f;
            if (DrawActionButton(
                    new Rect(x + 282f, y, 88f, 26f),
                    "▶▶ 2배속",
                    doubleSpeed,
                    true))
                _playbackSpeed = doubleSpeed ? 1f : 2f;
            double planSeconds = _timeProfile.PlanSeconds(_plan.Ticks);
            double perTickSeconds = _plan.Ticks > 0
                ? planSeconds / _plan.Ticks
                : 0.0;
            float shownTick = Mathf.Min(_displayTick, _plan.Ticks);
            GUI.Label(
                new Rect(x + 382f, y + 3f, bar.width - 396f, 22f),
                FormatDuration(shownTick * perTickSeconds) + " / " +
                FormatDuration(planSeconds) +
                " (틱 " + Mathf.RoundToInt(shownTick) + "/" + _plan.Ticks + ")");
            // 커스텀 시크 바 — 어두운 트랙 + 시안 채움 + 핸들 (기본 슬라이더
            // 스킨은 회색이라 현재 지점이 안 보임). 클릭·드래그로 시점 이동.
            Rect track = new Rect(x, y + 34f, bar.width - 24f, 18f);
            float fraction = _plan.Ticks > 0
                ? Mathf.Clamp01(shownTick / _plan.Ticks)
                : 0f;
            float innerWidth = track.width - 4f;
            // GUI.Box 틴트는 스킨 텍스처에 곱해져 거의 검은색이 된다 —
            // 백색 텍스처 단색 렌더로 트랙/채움/핸들을 직접 그린다.
            GUI.DrawTexture(
                track, Texture2D.whiteTexture, ScaleMode.StretchToFill,
                false, 0f, new Color(0f, 0f, 0f, 0.62f), 0f, 5f);
            Rect fill = new Rect(
                track.x + 2f,
                track.y + 2f,
                Mathf.Max(3f, innerWidth * fraction),
                track.height - 4f);
            GUI.DrawTexture(
                fill, Texture2D.whiteTexture, ScaleMode.StretchToFill,
                false, 0f, new Color(0.10f, 0.92f, 1f, 0.95f), 0f, 4f);
            float handleX = track.x + 2f + innerWidth * fraction;
            Rect handle = new Rect(
                handleX - 4f, track.y - 3f, 8f, track.height + 6f);
            GUI.DrawTexture(
                handle, Texture2D.whiteTexture, ScaleMode.StretchToFill,
                false, 0f, Color.white, 0f, 3f);

            // 좌클릭 + 바에서 시작한 드래그만 시크로 인정 — 우클릭 카메라 회전이나
            // 다른 곳에서 시작한 드래그가 바를 스치며 몰래 시크되는 것을 차단
            Event current = Event.current;
            Rect hitArea = new Rect(
                track.x - 6f, track.y - 8f, track.width + 12f, track.height + 16f);
            bool seek = false;
            if (current.type == EventType.MouseDown &&
                current.button == 0 &&
                hitArea.Contains(current.mousePosition))
            {
                _seekDragging = true;
                seek = true;
            }
            else if (current.type == EventType.MouseDrag &&
                     current.button == 0 &&
                     _seekDragging)
            {
                seek = true;
            }
            else if (current.type == EventType.MouseUp && _seekDragging)
            {
                _seekDragging = false;
                current.Use();
            }
            if (seek)
            {
                float sought = Mathf.Clamp01(
                    (current.mousePosition.x - track.x - 2f) / innerWidth) *
                    _plan.Ticks;
                _time = sought * SecondsPerTick;
                ApplyTick(sought);
                current.Use();
            }
        }

        /// <summary>시크 드래그가 재생바에서 시작됐는지 — 스침 시크 방지</summary>
        private bool _seekDragging;

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
            // 초기(프리뷰) 계산은 화면 정중앙의 대형 진행 카드로, 이후 재계획은
            // 상단의 작은 바로 표시한다.
            bool initial = _plan == null;
            float width = initial
                ? Mathf.Min(820f, Screen.width - 40f)
                : 420f;
            float height = initial ? 168f : 70f;
            Rect overlay = new Rect(
                (Screen.width - width) / 2f,
                initial ? (Screen.height - height) / 2f : 92f,
                width,
                height);
            GUI.Box(overlay, string.Empty);
            GUI.Box(overlay, string.Empty);
            GUI.Box(overlay, string.Empty);
            float elapsed = Time.realtimeSinceStartup - _planningStartedAt;
            Rect track;
            if (initial)
            {
                var titleStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 32,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                };
                var subtitleStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 18,
                    alignment = TextAnchor.MiddleCenter,
                };
                GUI.Label(
                    new Rect(overlay.x, overlay.y + 16f, width, 42f),
                    "초기 대응 계획 계산 중",
                    titleStyle);
                GUI.Label(
                    new Rect(overlay.x, overlay.y + 62f, width, 26f),
                    "단지 상황 파악 완료 · " + elapsed.ToString("0.0") + "초 경과",
                    subtitleStyle);
                track = new Rect(
                    overlay.x + 24f,
                    overlay.y + 104f,
                    width - 48f,
                    36f);
            }
            else
            {
                GUI.Label(
                    new Rect(overlay.x + 14f, overlay.y + 8f, width - 28f, 22f),
                    _pendingBuildingId + "동 · " +
                    (_pendingIncludeSecondaryEntrances
                        ? "서문·동문 비교"
                        : "서문만 사용") +
                    " · 도로 주차 " + _pendingBlockingVehicleCount +
                    "대 경로 계산 중 · " + elapsed.ToString("0.0") + "초");
                track = new Rect(
                    overlay.x + 14f,
                    overlay.y + 39f,
                    width - 28f,
                    16f);
            }
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

        private static string EntranceDisplayName(
            ApartmentComplexEntranceV2 entrance)
        {
            if (entrance == null) return "확인 중";
            return entrance.IsPrimary ? "서문" : "동문";
        }

        private static string FormatDuration(double seconds)
        {
            if (seconds < 60.0) return seconds.ToString("0.0") + "초";
            int minutes = (int)(seconds / 60.0);
            double remainder = seconds - minutes * 60.0;
            return minutes + "분 " + remainder.ToString("0.0") + "초";
        }

        private static string SensitivityStatusText(double seconds)
        {
            return "• 시간 기준: 5분 " +
                   (seconds <= TimeBudget.FastArrivalSeconds ? "통과" : "초과") +
                   " · 7분(기준) " +
                   (seconds <= TimeBudget.BaselineSeconds ? "통과" : "초과") +
                   " · 9분 " +
                   (seconds <= TimeBudget.SlowArrivalSeconds ? "통과" : "초과");
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
                return "• 현재 작업: 차량 들어올림 · 운송 장비 " +
                       pickup + "대 · 진행 " +
                       (pickupProgress * 100f).ToString("0") + "%";
            if (release > 0)
                return "• 현재 작업: 차량 내려놓기 · 운송 장비 " +
                       release + "대 · 진행 " +
                       (releaseProgress * 100f).ToString("0") + "%";
            return _paused
                ? "• 현재 작업: 재생 일시정지"
                : "• 현재 작업: 차량 이동 또는 다음 작업 대기";
        }

    }
}
