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
        /// t=0 도로 교란(쓰러진 나무) 배치 — S1 `ComplexDisturbanceV2`의 화면 조작.
        /// 배치 모드에서 도로 셀을 클릭하면 세로 2셀 나무를 놓고(재클릭 = 해제),
        /// 즉시 재계획해 자동 우회를 재생한다. 보호 셀은 클릭 시점에 거부한다.
        /// </summary>
        private bool _blockagePlacementMode;
        private readonly List<(int X, int Y)[]> _blockageSegments =
            new List<(int X, int Y)[]>();

        private IReadOnlyList<(int X, int Y)> BlockedCellsSnapshot()
        {
            return _blockageSegments.SelectMany(segment => segment).ToList();
        }

        private string BlockedCellsSignature()
        {
            return string.Join(";", _blockageSegments
                .SelectMany(segment => segment)
                .OrderBy(cell => cell.X).ThenBy(cell => cell.Y)
                .Select(cell => cell.X + ":" + cell.Y));
        }

        /// <summary>지면(y=0) 평면 교차로 클릭 셀을 해석한다 — 콜라이더 불요.</summary>
        private bool TryResolveClickedRoadCell(
            Vector2 screenPosition, out (int X, int Y) cell, out string failure)
        {
            cell = default;
            Camera camera = ActiveViewCamera();
            if (camera == null) camera = Object.FindAnyObjectByType<Camera>();
            if (camera == null)
            {
                failure = "활성 카메라가 없어 봉쇄를 배치할 수 없음";
                return false;
            }
            Ray ray = camera.ScreenPointToRay(screenPosition);
            if (Mathf.Abs(ray.direction.y) < 1e-4f)
            {
                failure = "지면과 평행한 시선이라 셀을 특정할 수 없음";
                return false;
            }
            float distance = -ray.origin.y / ray.direction.y;
            if (distance <= 0f)
            {
                failure = "지면 위 지점을 클릭해야 함";
                return false;
            }
            Vector3 point = ray.GetPoint(distance);
            cell = (Mathf.RoundToInt(point.x), Mathf.RoundToInt(point.z));
            if (_complex == null ||
                !_complex.BaseProblem.IsFloor(cell.X, cell.Y))
            {
                failure = "(" + cell.X + "," + cell.Y + ")는 도로 셀이 아님";
                return false;
            }
            failure = null;
            return true;
        }

        private void HandleBlockageClick((int X, int Y) cell)
        {
            if (_planningTask != null)
            {
                _inputStatus = "경로 계산 중에는 봉쇄를 바꿀 수 없음";
                return;
            }
            int existing = _blockageSegments.FindIndex(segment =>
                segment.Contains(cell));
            if (existing >= 0)
            {
                _blockageSegments.RemoveAt(existing);
                _inputStatus = "봉쇄 해제 — 잔여 " + _blockageSegments.Count + "건";
                BeginPresetLoad(
                    _includeSecondaryEntrances ? 1 : 0,
                    _fireBuildingId,
                    _blockingVehicleCount);
                return;
            }

            // 쓰러진 나무 = 세로 2셀 (위 셀 우선, 막히면 아래 셀 폴백, 둘 다 불가면 1셀)
            (int X, int Y)[] segment;
            if (_complex.BaseProblem.IsFloor(cell.X, cell.Y + 1))
                segment = new[] { cell, (cell.X, cell.Y + 1) };
            else if (_complex.BaseProblem.IsFloor(cell.X, cell.Y - 1))
                segment = new[] { (cell.X, cell.Y - 1), cell };
            else
                segment = new[] { cell };

            // 클릭 시점 즉시 검증 — 보호 셀·기존 봉쇄와의 충돌은 배치 자체를 거부
            var candidate = new List<(int X, int Y)>(BlockedCellsSnapshot());
            candidate.AddRange(segment);
            DisturbedComplexBuildResultV2 probe =
                ApartmentComplexDisturbanceV2.Apply(
                    _complex,
                    new ComplexDisturbanceV2("배치 검증", candidate));
            if (!probe.Success)
            {
                _inputStatus = "봉쇄 배치 불가: " + probe.FailReason;
                return;
            }
            _blockageSegments.Add(segment);
            _inputStatus = "봉쇄 배치 — 총 " + _blockageSegments.Count + "건, 재계획 중";
            BeginPresetLoad(
                _includeSecondaryEntrances ? 1 : 0,
                _fireBuildingId,
                _blockingVehicleCount);
        }

        private void ClearBlockages()
        {
            if (_blockageSegments.Count == 0) return;
            _blockageSegments.Clear();
            _inputStatus = "봉쇄 전체 해제, 재계획 중";
            BeginPresetLoad(
                _includeSecondaryEntrances ? 1 : 0,
                _fireBuildingId,
                _blockingVehicleCount);
        }

        /// <summary>쓰러진 나무 마커 — 관제/3D 공용(Shared).</summary>
        private void BuildBlockageMarkers()
        {
            for (int index = 0; index < _blockageSegments.Count; index++)
            {
                (int X, int Y)[] segment = _blockageSegments[index];
                float centerX = (float)segment.Average(cell => cell.X);
                float centerZ = (float)segment.Average(cell => cell.Y);
                var root = new GameObject("Shared-Blockage-" + index);
                Track(root, SimulationVisualLayer.Shared);
                root.transform.position = new Vector3(centerX, 0f, centerZ);

                // 경고면 — 봉쇄 셀 전체를 덮는 적갈색 바닥 표시
                CreateSitePrimitive(
                    PrimitiveType.Cube,
                    "Blockage-Warning-" + index,
                    new Vector3(0f, 0.03f, 0f),
                    new Vector3(0.96f, 0.05f, segment.Length - 0.04f),
                    _siteFireZoneMaterial,
                    root.transform,
                    true);
                // 눕힌 줄기 — 도로를 가로지르는 나무 몸통
                GameObject trunk = CreateSitePrimitive(
                    PrimitiveType.Cylinder,
                    "Blockage-Trunk-" + index,
                    new Vector3(0f, 0.19f, 0f),
                    new Vector3(0.24f, 0.5f * segment.Length + 0.15f, 0.24f),
                    _siteWoodMaterial,
                    root.transform,
                    true);
                trunk.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                // 수관 — 줄기 끝의 잎 뭉치
                CreateSitePrimitive(
                    PrimitiveType.Sphere,
                    "Blockage-Crown-" + index,
                    new Vector3(0f, 0.34f, 0.5f * segment.Length + 0.28f),
                    new Vector3(0.95f, 0.7f, 0.95f),
                    _siteFoliageMaterial,
                    root.transform,
                    true);
            }
        }
    }
}
