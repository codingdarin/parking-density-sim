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
    partial class SimulationRunner
    {
        /// <summary>
        /// 재생 대상 단지 — 8동 합성 단지와 실제 단지 A 발췌 블록(골목/간선 전용구역).
        /// S4~S4c 서사의 화면 대비: 골목안 "9대 인출·770초" vs 간선안 "즉시 개통".
        /// </summary>
        private enum SiteScenarioKind
        {
            Complex,
            SiteAlley,
            SiteArterial,
        }

        private SiteScenarioKind _scenarioKind = SiteScenarioKind.Complex;

        private static string ScenarioDisplayName(SiteScenarioKind kind)
        {
            switch (kind)
            {
                case SiteScenarioKind.SiteAlley:
                    return "실제 단지 A · 골목 전용구역";
                case SiteScenarioKind.SiteArterial:
                    return "실제 단지 A · 간선 재지정";
                default:
                    return "8동 합성 단지";
            }
        }

        private static int MaxVariableVehicles(SiteScenarioKind kind)
        {
            switch (kind)
            {
                case SiteScenarioKind.SiteAlley:
                    return SiteABlockScenarioFactoryV2.MaximumVariableSlots(
                        SiteZonePlacementV2.AlleyFrontage);
                case SiteScenarioKind.SiteArterial:
                    return SiteABlockScenarioFactoryV2.MaximumVariableSlots(
                        SiteZonePlacementV2.ArterialFrontage);
                default:
                    return ApartmentComplexScenarioFactoryV2
                        .MaximumBlockingVehicles;
            }
        }

        private static int DefaultFireBuildingId(SiteScenarioKind kind)
        {
            return kind == SiteScenarioKind.Complex ? 104 : 2;
        }

        private static ApartmentComplexScenarioV2 BuildScenario(
            SiteScenarioKind kind,
            int blockingVehicleCount,
            OperationTimingV2 timing)
        {
            switch (kind)
            {
                case SiteScenarioKind.SiteAlley:
                    return SiteABlockScenarioFactoryV2.BuildDensity(
                        blockingVehicleCount, timing);
                case SiteScenarioKind.SiteArterial:
                    return SiteABlockScenarioFactoryV2.BuildDensity(
                        blockingVehicleCount,
                        timing,
                        SiteStagingLayoutV2.SouthWestOnly,
                        SiteZonePlacementV2.ArterialFrontage);
                default:
                    return ApartmentComplexScenarioFactoryV2.BuildDensity(
                        blockingVehicleCount, timing);
            }
        }

        private void SwitchScenario(SiteScenarioKind kind)
        {
            if (_planningTask != null)
            {
                _inputStatus = "경로 계산 중에는 단지를 바꿀 수 없음";
                return;
            }
            if (kind == _scenarioKind) return;
            // 봉쇄는 단지별 상태 — 격자·보호 셀이 다르므로 전환 시 초기화
            _blockageSegments.Clear();
            int maxVehicles = MaxVariableVehicles(kind);
            _requestedBlockingVehicleCount =
                Mathf.Min(_requestedBlockingVehicleCount, maxVehicles);
            BeginPresetLoad(
                _includeSecondaryEntrances ? 1 : 0,
                DefaultFireBuildingId(kind),
                Mathf.Min(_blockingVehicleCount, maxVehicles),
                kind);
        }

        /// <summary>조작 패널의 단지 선택 3버튼 — DrawControlPanel에서 호출</summary>
        private void DrawScenarioButtons(float x, float y, bool canReplan)
        {
            (SiteScenarioKind Kind, string Label)[] entries =
            {
                (SiteScenarioKind.Complex, "8동 합성"),
                (SiteScenarioKind.SiteAlley, "단지A 골목"),
                (SiteScenarioKind.SiteArterial, "단지A 간선"),
            };
            for (int index = 0; index < entries.Length; index++)
            {
                if (DrawActionButton(
                        new Rect(x + index * 86f, y, 82f, 32f),
                        entries[index].Label,
                        _scenarioKind == entries[index].Kind,
                        canReplan && _scenarioKind != entries[index].Kind))
                    SwitchScenario(entries[index].Kind);
            }
        }
    }
}
