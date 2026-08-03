using System.Collections.Generic;
using ParkingSim.Core.V2;
using UnityEngine;

namespace ParkingSim.Runtime
{
    // 재생 경로 스무딩(R4-①) — 계획 셀 경로를 재생 시작 시 한 번 전처리해
    // 로봇별 틱 웨이포인트 폴리라인을 확정한다. 재생은 이 배열의 보간만
    // 샘플하므로 틱의 순수 함수: 시크·역재생에 프레임 상태가 남지 않는다.
    //
    // 1) 레인 스냅 — 하부 통과 히스테리시스(사용자 설계)의 틱 결정론 이식:
    //    전방(현재~+1셀)에 진행과 수직인 주차 차량이 나타나면 축간 레인에
    //    맞추고, 기준 부재는 전이 신호가 아니다(유지). 코너 해제, 적재 리셋.
    //    평행 통과는 바퀴 사이 = 셀 중앙선이라 보정 불요.
    // 2) 직선화 — 스냅·적재·대기가 없는 이동 구간만 LOS string-pulling.
    //    차량·비바닥·봉쇄 셀 통과 금지 + 원경로 대비 편차 상한으로
    //    계단(지그재그)만 펴지고 의도된 L코너는 유지된다.
    partial class SimulationRunner
    {
        /// <summary>로봇별 틱 웨이포인트(계획 평면 x,z) — 전처리 산출물</summary>
        private Vector2[][] _smoothedPaths;

        private const int MaxCutTicks = 12;
        private const float MaxCutDeviation = 0.75f;

        private void BuildSmoothedPaths()
        {
            int robots = _plan.RobotTimelines.Length;
            _smoothedPaths = new Vector2[robots][];
            for (int robot = 0; robot < robots; robot++)
                _smoothedPaths[robot] = BuildSmoothedPath(robot);
        }

        private Vector2[] BuildSmoothedPath(int robot)
        {
            List<TimedRobotStateV2> timeline = _plan.RobotTimelines[robot];
            int ticks = _plan.Ticks;
            var raw = new Vector2[ticks + 1];
            var carrying = new bool[ticks + 1];
            var cells = new (int X, int Y)[ticks + 1];
            for (int t = 0; t <= ticks; t++)
            {
                TimedRobotStateV2 state = StateAt(timeline, t);
                carrying[t] = state.Carrying;
                cells[t] = (state.X, state.Y);
                if (state.Carrying)
                {
                    VehiclePose pose = new VehiclePose(
                        state.X, state.Y, state.Orientation);
                    var second = pose.SecondCell;
                    raw[t] = new Vector2(
                        (pose.X + second.X) * 0.5f,
                        (pose.Y + second.Y) * 0.5f);
                }
                else
                {
                    raw[t] = new Vector2(state.X, state.Y);
                }
            }

            var result = (Vector2[])raw.Clone();
            var pinned = new bool[ticks + 1];
            ApplyLaneSnapping(cells, carrying, result, pinned);
            ApplyStringPulling(cells, carrying, raw, result, pinned);
            return result;
        }

        private void ApplyLaneSnapping(
            (int X, int Y)[] cells,
            bool[] carrying,
            Vector2[] result,
            bool[] pinned)
        {
            bool hasLane = false;
            bool laneIsX = false;
            float lane = 0f;
            for (int t = 0; t < cells.Length; t++)
            {
                if (carrying[t])
                {
                    hasLane = false;
                    pinned[t] = true;
                    continue;
                }
                bool alongX;
                int step;
                if (TryTravelDirection(cells, t, out alongX, out step))
                {
                    // 코너: 진행 축이 레인 축과 같아지면 레인이 무의미 — 해제
                    if (hasLane && laneIsX == alongX) hasLane = false;
                    for (int look = 0; look <= 1; look++)
                    {
                        (int X, int Y) cell = alongX
                            ? (cells[t].X + look * step, cells[t].Y)
                            : (cells[t].X, cells[t].Y + look * step);
                        VehiclePose pose;
                        if (!TryGetParkedPose(cell, t, out pose)) continue;
                        bool carHorizontal =
                            pose.Orientation == VehicleOrientation.Horizontal;
                        if (carHorizontal == alongX) continue;
                        var second = pose.SecondCell;
                        hasLane = true;
                        laneIsX = carHorizontal;
                        lane = carHorizontal
                            ? (pose.X + second.X) * 0.5f
                            : (pose.Y + second.Y) * 0.5f;
                        break;
                    }
                }
                if (!hasLane) continue;
                if (laneIsX) result[t].x = lane;
                else result[t].y = lane;
                pinned[t] = true;
            }
        }

        /// <summary>틱 t의 진행 방향 — 이후 첫 이동 우선, 없으면 직전 이동.
        /// 대기 중에도 다가올 축을 미리 반영한다(결정론적 선행).</summary>
        private static bool TryTravelDirection(
            (int X, int Y)[] cells,
            int t,
            out bool alongX,
            out int step)
        {
            for (int u = t; u < cells.Length - 1; u++)
            {
                if (DirectionOf(cells[u], cells[u + 1], out alongX, out step))
                    return true;
            }
            for (int u = t; u > 0; u--)
            {
                if (DirectionOf(cells[u - 1], cells[u], out alongX, out step))
                    return true;
            }
            alongX = false;
            step = 0;
            return false;
        }

        private static bool DirectionOf(
            (int X, int Y) from,
            (int X, int Y) to,
            out bool alongX,
            out int step)
        {
            int dx = to.X - from.X;
            int dy = to.Y - from.Y;
            alongX = false;
            step = 0;
            if (dx == 0 && dy == 0) return false;
            alongX = Mathf.Abs(dx) >= Mathf.Abs(dy);
            step = alongX ? (dx >= 0 ? 1 : -1) : (dy >= 0 ? 1 : -1);
            return true;
        }

        private void ApplyStringPulling(
            (int X, int Y)[] cells,
            bool[] carrying,
            Vector2[] raw,
            Vector2[] result,
            bool[] pinned)
        {
            int last = cells.Length - 1;
            int start = 0;
            while (start < last)
            {
                // 런: 핀 없음 + 매 틱 이동(대기 없음) 최장 구간
                if (pinned[start] || carrying[start] ||
                    cells[start] == cells[start + 1])
                {
                    start++;
                    continue;
                }
                int end = start;
                while (end < last &&
                       !pinned[end + 1] && !carrying[end + 1] &&
                       cells[end] != cells[end + 1])
                    end++;
                int anchor = start;
                while (anchor < end)
                {
                    int target = Mathf.Min(anchor + MaxCutTicks, end);
                    while (target > anchor + 1 &&
                           !CutAllowed(raw, result, anchor, target))
                        target--;
                    if (target > anchor + 1)
                    {
                        for (int t = anchor + 1; t < target; t++)
                            result[t] = Vector2.Lerp(
                                result[anchor],
                                result[target],
                                (float)(t - anchor) / (target - anchor));
                    }
                    anchor = target;
                }
                start = end + 1;
            }
        }

        /// <summary>anchor→target 직선 대체 가능 판정 — 표본마다 원경로
        /// 편차 상한(계단만 허용, L코너 차단)과 셀 통행(바닥·차량·봉쇄) 확인.</summary>
        private bool CutAllowed(Vector2[] raw, Vector2[] result, int anchor, int target)
        {
            Vector2 from = result[anchor];
            Vector2 to = result[target];
            int samples = Mathf.Max(
                2, Mathf.CeilToInt(Vector2.Distance(from, to) * 4f));
            for (int index = 1; index < samples; index++)
            {
                float s = (float)index / samples;
                Vector2 point = Vector2.Lerp(from, to, s);
                float tickAt = Mathf.Lerp(anchor, target, s);
                int rawTick = Mathf.RoundToInt(tickAt);
                if (Vector2.Distance(point, raw[rawTick]) > MaxCutDeviation)
                    return false;
                (int X, int Y) cell =
                    (Mathf.RoundToInt(point.x), Mathf.RoundToInt(point.y));
                if (cell.X < 0 || cell.X >= _problem.Width ||
                    cell.Y < 0 || cell.Y >= _problem.Height)
                    return false;
                if (!_problem.IsFloor(cell.X, cell.Y)) return false;
                VehiclePose pose;
                if (TryGetParkedPose(cell, tickAt, out pose)) return false;
                if (CellHasDroppedVehicle(cell, tickAt)) return false;
            }
            return true;
        }

        /// <summary>해당 셀에 지금 서 있는 주차 차량 pose — 고정 차량 + 아직
        /// 들리지 않은 이동 대상 차량(LiftTick 전까지, 자기 대상 포함).</summary>
        private bool TryGetParkedPose(
            (int X, int Y) cell, float tick, out VehiclePose pose)
        {
            if (_fixedPoseByCell.TryGetValue(cell, out pose)) return true;
            int vehicle;
            if (_movableVehicleByCell.TryGetValue(cell, out vehicle))
            {
                PipelinedMissionV2 mission;
                if (!_missions.TryGetValue(vehicle, out mission) ||
                    tick < mission.LiftTick)
                {
                    pose = _problem.Slots[
                        _problem.InitialVehicleSlots[vehicle]].Pose;
                    return true;
                }
            }
            pose = default(VehiclePose);
            return false;
        }

        /// <summary>하차 완료된 차량이 이 시점에 목적 슬롯에서 점유하는 셀인지</summary>
        private bool CellHasDroppedVehicle((int X, int Y) cell, float tick)
        {
            foreach (PipelinedMissionV2 mission in _missions.Values)
            {
                if (tick < mission.DropTick) continue;
                VehiclePose pose = _problem.Slots[mission.DestinationSlot].Pose;
                if ((pose.X, pose.Y) == cell || pose.SecondCell == cell)
                    return true;
            }
            return false;
        }

        /// <summary>스무딩 폴리라인 샘플(계획 평면) — 전처리 전이면 원상태 폴백</summary>
        private Vector2 SampleSmoothedPlanar(int robot, float tick)
        {
            Vector2[] path =
                _smoothedPaths != null && robot < _smoothedPaths.Length
                    ? _smoothedPaths[robot]
                    : null;
            if (path == null || path.Length == 0)
            {
                TimedRobotStateV2 state = StateAt(
                    _plan.RobotTimelines[robot],
                    Mathf.Clamp(Mathf.FloorToInt(tick), 0, _plan.Ticks));
                return new Vector2(state.X, state.Y);
            }
            int a = Mathf.Clamp(Mathf.FloorToInt(tick), 0, path.Length - 1);
            int b = Mathf.Min(a + 1, path.Length - 1);
            return Vector2.Lerp(path[a], path[b], Mathf.Clamp01(tick - a));
        }

        private Vector3 SmoothedRobotPosition(
            int robot, float tick, bool customTransport)
        {
            Vector2 planar = SampleSmoothedPlanar(robot, tick);
            return new Vector3(
                planar.x, customTransport ? 0f : 0.20f, planar.y);
        }
    }
}
