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
        /// <summary>
        /// 관제보드 — 방재 담당자의 상시 질문 "지금 불나면 몇 분 만에 열리나".
        /// 현재 조건(밀도·진입구·가용 유닛)으로 전 동의 예상 개통시간을
        /// 백그라운드에서 재계산해 상시 표시한다. 조건이 바뀌면 자동 재계산.
        /// </summary>
        private sealed class ReadinessBoard
        {
            public string Signature;
            public string Failure;
            public List<ApartmentComplexDensityTrialV2> Rows;
        }

        private const float ReadinessPanelWidth = 286f;
        private const float ReadinessPanelHeight = 308f;

        /// <summary>비상 대응 가용 운송 유닛 수 (충전·고장 이탈 반영, 조작 패널에서 선택)</summary>
        private int _availableUnitCount = 4;
        private Task<ReadinessBoard> _readinessTask;
        private ReadinessBoard _readinessBoard;
        /// <summary>조건 서명 → 완료 보드 캐시. 계산이 결정론(같은 조건 → 같은 결과)이라
        /// 시나리오·밀도를 되돌릴 때 재계산 없이 즉시 표시한다. 실패 결과도 캐시해
        /// 같은 조건을 끝없이 다시 계산하는 루프를 막는다. 조건 조합 수가 유한해
        /// 캐시 크기는 실질적으로 제한된다.</summary>
        private readonly Dictionary<string, ReadinessBoard> _readinessCache =
            new Dictionary<string, ReadinessBoard>();
        /// <summary>계산 중 동별 점진 표시용 — 태스크 스레드가 잠금 하에 행을 추가한다.</summary>
        private readonly List<ApartmentComplexDensityTrialV2> _readinessPartial =
            new List<ApartmentComplexDensityTrialV2>();
        private string _readinessTaskSignature;

        private static Rect ReadinessPanelBounds()
        {
            return new Rect(
                Mathf.Max(12f, Screen.width - ReadinessPanelWidth - 12f),
                Mathf.Min(
                    Screen.height - ReadinessPanelHeight - 12f,
                    12f + ControlPanelHeight + 10f),
                ReadinessPanelWidth,
                ReadinessPanelHeight);
        }

        private string ReadinessSignature()
        {
            return _scenarioKind + "|" + _blockingVehicleCount + "|" +
                   _includeSecondaryEntrances + "|" + _availableUnitCount + "|" +
                   BlockedCellsSignature();
        }

        /// <summary>Update에서 매 프레임 호출 — 완료 회수·캐시 조회·재계산 트리거를 겸한다.</summary>
        private void UpdateReadinessBoard()
        {
            if (_readinessTask != null)
            {
                if (!_readinessTask.IsCompleted) return;
                Task<ReadinessBoard> completed = _readinessTask;
                _readinessTask = null;
                if (completed.IsFaulted || completed.IsCanceled)
                {
                    string reason = completed.Exception == null
                        ? "취소됨"
                        : completed.Exception.GetBaseException().Message;
                    Debug.LogWarning("[Model V2] 관제보드 계산 실패: " + reason);
                    var failed = new ReadinessBoard
                    {
                        Signature = _readinessTaskSignature,
                        Failure = "계산 실패: " + reason,
                    };
                    _readinessCache[failed.Signature] = failed;
                    _readinessBoard = failed;
                }
                else
                {
                    _readinessBoard = completed.Result;
                    _readinessCache[completed.Result.Signature] = completed.Result;
                }
            }
            if (_timeProfile == null) return;
            if (ReadinessBoardCurrent()) return;
            ReadinessBoard cached;
            if (_readinessCache.TryGetValue(ReadinessSignature(), out cached))
            {
                _readinessBoard = cached;
                return;
            }
            StartReadinessTask();
        }

        private bool ReadinessBoardCurrent()
        {
            return _readinessBoard != null &&
                   _readinessBoard.Signature == ReadinessSignature();
        }

        private void StartReadinessTask()
        {
            int blockingVehicleCount = _blockingVehicleCount;
            bool includeSecondaryEntrances = _includeSecondaryEntrances;
            int availableUnitCount = _availableUnitCount;
            SiteScenarioKind kind = _scenarioKind;
            IReadOnlyList<(int X, int Y)> blockedCells = BlockedCellsSnapshot();
            string signature = ReadinessSignature();
            PhysicalTimeProfileV2 profile = _timeProfile;
            lock (_readinessPartial) _readinessPartial.Clear();
            _readinessTaskSignature = signature;
            _readinessTask = Task.Run(() =>
            {
                ApartmentComplexScenarioV2 complex = BuildScenario(
                    kind, blockingVehicleCount, profile.CreateOperationTiming());
                if (blockedCells.Count > 0)
                {
                    DisturbedComplexBuildResultV2 disturbed =
                        ApartmentComplexDisturbanceV2.Apply(
                            complex,
                            new ComplexDisturbanceV2("관제보드 봉쇄", blockedCells));
                    if (!disturbed.Success)
                        return new ReadinessBoard
                        {
                            Signature = signature,
                            Failure = "교란 적용 불가: " + disturbed.FailReason,
                        };
                    complex = disturbed.Scenario;
                }
                var session = new ApartmentComplexPlanningSessionV2(
                    complex,
                    activeRobotCount: availableUnitCount,
                    generationOptions: new EmergencyAccessRouteGenerationOptionsV2
                    {
                        MaxRoutes = 4,
                        MaxCenterlineAttempts = 16,
                        MaxSearchExpansions = 100000,
                    },
                    maxTick: 5000,
                    enableLowerBoundPruning: true);
                var rows = new List<ApartmentComplexDensityTrialV2>();
                foreach (ApartmentBuildingV2 building in complex.Buildings)
                {
                    ApartmentComplexDensityTrialV2 row =
                        ApartmentComplexDensitySweepV2.Evaluate(
                            complex,
                            session,
                            building.Id,
                            includeSecondaryEntrances,
                            profile);
                    rows.Add(row);
                    lock (_readinessPartial) _readinessPartial.Add(row);
                }
                return new ReadinessBoard
                {
                    Signature = signature,
                    Rows = rows,
                };
            });
        }

        private void DrawReadinessPanel()
        {
            Rect panel = ReadinessPanelBounds();
            GUI.Box(panel, string.Empty);
            float x = panel.x + 12f;
            float y = panel.y + 10f;
            GUI.Label(new Rect(x, y, 260f, 22f),
                "관제보드 — " + ScenarioDisplayName(_scenarioKind));
            y += 22f;
            GUI.Label(new Rect(x, y, 260f, 20f),
                "가용 " + _availableUnitCount + "/4조 · 도로 주차 " +
                _blockingVehicleCount + "대 · " +
                (_includeSecondaryEntrances ? "서문+동문" : "서문 단일") +
                (_blockageSegments.Count > 0
                    ? " · 봉쇄 " + _blockageSegments.Count + "건"
                    : ""));
            y += 22f;
            bool stale = !ReadinessBoardCurrent();
            List<ApartmentComplexDensityTrialV2> partial = null;
            if (stale && _readinessTask != null &&
                _readinessTaskSignature == ReadinessSignature())
                lock (_readinessPartial)
                    partial = new List<ApartmentComplexDensityTrialV2>(
                        _readinessPartial);
            if (stale && partial != null)
            {
                // 계산 중 — 완료된 동부터 점진 표시
                GUI.Label(new Rect(x, y, 260f, 20f),
                    "동별 계산 중… (완료 " + partial.Count + "동)");
                y += 20f;
                int partialSafe;
                DrawReadinessRows(partial, x, y, out partialSafe);
                return;
            }
            if (_readinessBoard == null)
            {
                GUI.Label(new Rect(x, y, 260f, 20f), "동별 대응력 계산 중…");
                return;
            }
            if (stale)
            {
                GUI.Label(new Rect(x, y, 260f, 20f),
                    "조건 변경 반영 재계산 중… (아래는 직전 값)");
                y += 20f;
            }
            if (_readinessBoard.Rows == null)
            {
                GUI.Label(new Rect(x, y, 260f, 40f), _readinessBoard.Failure);
                return;
            }
            int safeCount;
            y = DrawReadinessRows(_readinessBoard.Rows, x, y, out safeCount);
            y += 4f;
            GUI.Label(new Rect(x, y, 260f, 20f),
                _readinessBoard.Rows.Count + "동 중 " + safeCount + "동 7분 이내" +
                (safeCount == _readinessBoard.Rows.Count
                    ? " — 전 동 대응 가능"
                    : " — 취약 동 존재"));
            y += 20f;
            GUI.Label(new Rect(x, y, 260f, 20f),
                _availableUnitCount >= 4
                    ? "예비 유닛 없음 — 1대 이탈 시 재확인 필요"
                    : "유닛 이탈 상태 — 충전·고장 복귀 필요");
        }

        private float DrawReadinessRows(
            List<ApartmentComplexDensityTrialV2> rows,
            float x,
            float y,
            out int safeCount)
        {
            Color previousColor = GUI.color;
            safeCount = 0;
            foreach (ApartmentComplexDensityTrialV2 row in rows)
            {
                string text;
                Color color;
                if (!row.PlanSuccess)
                {
                    text = row.BuildingId + "동  개통 불가 (" + row.Outcome + ")";
                    color = new Color(1f, 0.36f, 0.30f, 1f);
                }
                else
                {
                    bool fast = row.Seconds <= TimeBudget.FastArrivalSeconds;
                    bool within = row.WithinBudget;
                    text = row.BuildingId + "동  " +
                           FormatDuration(row.Seconds) +
                           (within ? fast ? "  (5분 이내)" : "  (7분 이내)" : "  7분 초과");
                    color = within
                        ? fast
                            ? new Color(0.35f, 1f, 0.55f, 1f)
                            : new Color(0.55f, 0.95f, 1f, 1f)
                        : new Color(1f, 0.62f, 0.25f, 1f);
                    if (within) safeCount++;
                }
                GUI.color = color;
                GUI.Label(new Rect(x, y, 260f, 20f), text);
                y += 20f;
            }
            GUI.color = previousColor;
            return y;
        }
    }
}
