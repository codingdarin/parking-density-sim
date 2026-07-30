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
                "• 화면 표시: 청록=선택 경로 · 파랑=대안 · 주황=이동 차량 · 노랑=확보 경계");
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
                "• 재생 조작: Space 일시정지/재생 · R 처음부터");
        }

        private void DrawControlPanel()
        {
            Rect panel = ControlPanelBounds();
            GUI.Box(panel, string.Empty);
            float x = panel.x + 12f;
            float y = panel.y + 10f;
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
                BeginPresetLoad(0, _fireBuildingId, _blockingVehicleCount);
            if (DrawActionButton(
                    new Rect(x + 132f, y, 124f, 32f),
                    "서문·동문 비교",
                    shownSecondary,
                    canReplan))
                BeginPresetLoad(1, _fireBuildingId, _blockingVehicleCount);

            y += 42f;
            GUI.Label(new Rect(x, y, 260f, 22f), "도로 주차 차량");
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
                    _requestedBlockingVehicleCount);

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
                        _blockingVehicleCount);
                }
            }

            y += 42f;
            GUI.Label(new Rect(x, y, 260f, 22f), "재생 제어");
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
                    ? "서문·동문 비교"
                    : "서문만 사용") +
                " · 도로 주차 " + _pendingBlockingVehicleCount + "대";
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
