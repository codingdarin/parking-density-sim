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
                VehiclePose servicePose;
                if (TryGetRobotServicePose(robot, timelineTick, out servicePose))
                {
                    // 서비스 위치(1×2 pose 중심)로 하드 스냅하면 접근 셀(앵커)과
                    // 최대 반 셀 차이가 점프로 보인다. 취득 초반에는 차 밑으로
                    // 미끄러져 들어가고, 해제 말미에는 빠져나오도록 블렌딩한다.
                    float serviceProgress;
                    int servicePhase = ServicePhase(
                        robot, timelineTick, out serviceProgress);
                    float blend = 1f;
                    if (servicePhase == 1)
                        blend = Mathf.SmoothStep(
                            0f, 1f, Mathf.Clamp01(serviceProgress / 0.15f));
                    else if (servicePhase == 2)
                        blend = 1f - Mathf.SmoothStep(
                            0f,
                            1f,
                            Mathf.Clamp01((serviceProgress - 0.85f) / 0.15f));
                    Vector3 naturalPosition = ApplyUnderCarSwerve(
                        Vector3.Lerp(
                            RobotPosition(a, _robotUsesCustomView[robot]),
                            RobotPosition(b, _robotUsesCustomView[robot]),
                            fraction),
                        a,
                        b,
                        timelineTick,
                        robot);
                    _robotViews[robot].transform.position = Vector3.Lerp(
                        naturalPosition,
                        RobotPosition(servicePose, _robotUsesCustomView[robot]),
                        blend);
                    _robotViews[robot].transform.rotation =
                        SmoothRobotRotation(
                            _robotViews[robot].transform.rotation,
                            VehicleRotation(servicePose));
                }
                else
                {
                    _robotViews[robot].transform.position = ApplyUnderCarSwerve(
                        Vector3.Lerp(
                            RobotPosition(a, _robotUsesCustomView[robot]),
                            RobotPosition(b, _robotUsesCustomView[robot]),
                            fraction),
                        a,
                        b,
                        timelineTick,
                        robot);
                    _robotViews[robot].transform.rotation =
                        SmoothRobotRotation(
                            _robotViews[robot].transform.rotation,
                            RobotVisualTargetRotation(
                                a,
                                b,
                                fraction,
                                _robotViews[robot].transform.rotation));
                }
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
                position.y = ServiceVehicleHeight(
                    vehicle,
                    timelineTick,
                    customTransport);
                view.transform.position = position;
                view.transform.rotation = Quaternion.Lerp(
                    VehicleRotation(a.Pose), VehicleRotation(b.Pose), fraction);
                GameObject frame;
                if (_carTrackingFrames.TryGetValue(vehicle, out frame))
                {
                    // 플럼밥: 차량 상공에서 천천히 회전·부유.
                    // 전체 뷰에서는 크게, 유닛 추적 카메라에서는 원 크기 유지.
                    float markerScale =
                        _selectedTransportCamera >= 0 ? 1f : 1.6f;
                    frame.transform.localScale = Vector3.one * markerScale;
                    frame.transform.position = position + Vector3.up *
                        (1.05f + Mathf.Sin(Time.time * 2.4f + vehicle) * 0.05f);
                    frame.transform.rotation = Quaternion.Euler(
                        0f, Time.time * 110f + vehicle * 40f, 0f);
                    SetTrackingFrameColor(
                        frame,
                        a.Carried || b.Carried
                            ? new Color(0.08f, 0.92f, 1f)
                            : new Color(1f, 0.48f, 0.04f));
                }
            }
            ApplyRobotControlMarkers(timelineTick);
            ApplyServiceIndicators(timelineTick);
        }

        private void ApplyRobotControlMarkers(float tick)
        {
            if (_robotControlMarkers == null) return;
            int stateTick = Mathf.Clamp(
                Mathf.FloorToInt(tick),
                0,
                _plan.Ticks);
            for (int robot = 0;
                 robot < _robotControlMarkers.Length;
                 robot++)
            {
                GameObject marker = _robotControlMarkers[robot];
                if (marker == null || _robotViews[robot] == null) continue;
                Vector3 robotPosition = _robotViews[robot].transform.position;
                marker.transform.position = new Vector3(
                    robotPosition.x,
                    0.30f,
                    robotPosition.z);
                marker.transform.rotation = Quaternion.identity;
                float pulse =
                    1f + 0.07f * Mathf.Sin(
                        Time.unscaledTime * 3.2f + robot * 1.7f);
                marker.transform.localScale =
                    new Vector3(pulse, 1f, pulse);

                bool carrying = robot < _plan.RobotTimelines.Length &&
                    _plan.RobotTimelines[robot].Count > 0 &&
                    StateAt(_plan.RobotTimelines[robot], stateTick).Carrying;
                Color color = RobotColor(robot, carrying);
                SetTrackingFrameColor(marker, color);
                if (_robotControlLabels != null &&
                    robot < _robotControlLabels.Length &&
                    _robotControlLabels[robot] != null)
                {
                    _robotControlLabels[robot].transform.rotation =
                        _presentationCamera != null &&
                        _visualMode == SimulationVisualMode.Control
                            ? _presentationCamera.transform.rotation
                            : Quaternion.Euler(90f, 0f, 0f);
                    _robotControlLabels[robot].color = color;
                }
            }
        }

        private bool TryGetRobotServicePose(
            int robot,
            float tick,
            out VehiclePose pose)
        {
            foreach (PipelinedMissionV2 mission in _missions.Values)
            {
                if (mission.RobotIndex != robot) continue;
                float pickupStart =
                    mission.LiftTick - _problem.Timing.LiftServiceTicks;
                if (tick >= pickupStart && tick < mission.LiftTick)
                {
                    pose = _problem.Slots[
                        _problem.InitialVehicleSlots[mission.VehicleIndex]].Pose;
                    return true;
                }
                float releaseStart =
                    mission.DropTick - _problem.Timing.DropServiceTicks;
                if (tick >= releaseStart && tick < mission.DropTick)
                {
                    pose = _problem.Slots[mission.DestinationSlot].Pose;
                    return true;
                }
            }
            pose = default(VehiclePose);
            return false;
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

        private float ServiceVehicleHeight(
            int vehicle,
            float tick,
            bool customTransport)
        {
            PipelinedMissionV2 mission = _missions[vehicle];
            float parkedHeight = ParkedVehicleRootHeight;
            float carriedHeight = customTransport ? 0.44f : 0.52f;
            float pickupStart = mission.LiftTick - _problem.Timing.LiftServiceTicks;
            if (tick >= pickupStart && tick < mission.LiftTick)
            {
                float progress = Mathf.InverseLerp(pickupStart, mission.LiftTick, tick);
                return Mathf.SmoothStep(
                    parkedHeight,
                    carriedHeight,
                    progress);
            }
            float releaseStart = mission.DropTick - _problem.Timing.DropServiceTicks;
            if (tick >= releaseStart && tick < mission.DropTick)
            {
                float progress = Mathf.InverseLerp(releaseStart, mission.DropTick, tick);
                return Mathf.SmoothStep(
                    carriedHeight,
                    parkedHeight,
                    progress);
            }
            return tick >= mission.LiftTick && tick < mission.DropTick
                ? carriedHeight
                : parkedHeight;
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
                if (phase != 0)
                {
                    SetColor(indicator, phase == 1
                        ? new Color(1f, 0.72f, 0.08f)
                        : new Color(0.20f, 1f, 0.50f));
                    float pulse =
                        0.85f + 0.25f * Mathf.Sin(Time.unscaledTime * 8f);
                    float radius =
                        _robotUsesCustomView[robot] ? 0.06f : 0.30f;
                    float baseHeight =
                        _robotUsesCustomView[robot] ? 0.06f : 0.30f;
                    float progressHeight =
                        _robotUsesCustomView[robot] ? 0.10f : 0.50f;
                    indicator.transform.localScale = new Vector3(
                        radius * pulse,
                        baseHeight + progressHeight * progress,
                        radius * pulse);
                }
                ApplyLiftMechanism(robot, tick, phase, progress);
            }
        }

        private void ApplyLiftMechanism(
            int robot,
            float tick,
            int phase,
            float progress)
        {
            if (_robotLiftVisuals == null ||
                robot < 0 ||
                robot >= _robotLiftVisuals.Length)
                return;
            TransportLiftVisual liftVisual = _robotLiftVisuals[robot];
            if (liftVisual == null) return;

            int stateTick = Mathf.Clamp(
                Mathf.FloorToInt(tick),
                0,
                _plan.Ticks);
            bool carrying = robot < _plan.RobotTimelines.Length &&
                _plan.RobotTimelines[robot].Count > 0 &&
                StateAt(_plan.RobotTimelines[robot], stateTick).Carrying;
            // 취득: 부채 접힘은 0~85%로 길게(천천히), 상승은 기존 42~100% 유지 —
            // 접히면서 함께 들리는 동작. 해제는 역순 미러.
            float armAmount;
            float deckAmount;
            if (phase == 1)
            {
                armAmount = Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.Clamp01(progress / 0.85f));
                deckAmount = Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.Clamp01((progress - 0.42f) / 0.58f));
            }
            else if (phase == 2)
            {
                deckAmount = 1f - Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.Clamp01(progress / 0.58f));
                armAmount = 1f - Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.Clamp01((progress - 0.15f) / 0.85f));
            }
            else
            {
                armAmount = carrying ? 1f : 0f;
                deckAmount = carrying ? 1f : 0f;
            }

            // 모듈 전개: 유휴·빈 주행은 밀착(전장 ≤ 1셀 — 인접 대기 유닛과 비겹침),
            // 취득 서비스 전반부에 축거 간격으로 전개, 운반 중 유지, 해제 후 재밀착
            float spreadAmount;
            if (phase == 1)
                spreadAmount = Mathf.SmoothStep(
                    0f, 1f, Mathf.Clamp01(progress / 0.25f));
            else if (phase == 2)
                spreadAmount = 1f - Mathf.SmoothStep(
                    0f, 1f, Mathf.Clamp01((progress - 0.75f) / 0.25f));
            else
                spreadAmount = carrying ? 1f : 0f;
            if (liftVisual.AxleModules != null)
            {
                float offset = Mathf.Lerp(
                    IdleModuleOffsetX, DockedModuleOffsetX, spreadAmount);
                for (int index = 0; index < liftVisual.AxleModules.Length; index++)
                {
                    if (liftVisual.AxleModules[index] == null) continue;
                    float direction = index == 0 ? -1f : 1f;
                    Vector3 local = liftVisual.AxleModules[index].localPosition;
                    liftVisual.AxleModules[index].localPosition =
                        new Vector3(direction * offset, local.y, local.z);
                }
            }

            for (int index = 0; index < liftVisual.Decks.Length; index++)
            {
                liftVisual.Decks[index].localPosition =
                    liftVisual.DeckRestPositions[index] +
                    Vector3.up * (0.035f * deckAmount);
            }
            for (int index = 0; index < liftVisual.ArmPivots.Length; index++)
            {
                liftVisual.ArmPivots[index].localRotation =
                    Quaternion.Slerp(
                        liftVisual.ArmRestRotations[index],
                        liftVisual.ArmLiftRotations[index],
                        armAmount);
            }
        }

        /// <summary>
        /// 하부 통과 레인 유지 스워브 — 같은 방향 주차열에 들어설 때 한 번
        /// 축간 레인으로 옳기고, 열이 이어지는 동안 그 레인을 유지한다.
        /// 방향이 다른 구간을 만나면 그때 한 번 더 옳긴다(로봇별 오프셋
        /// 상태를 목표 레인으로 지수 수렴). 모델 좌표 불변 — 순수 시각 보정.
        /// </summary>
        private Vector3 ApplyUnderCarSwerve(
            Vector3 position,
            TimedRobotStateV2 a,
            TimedRobotStateV2 b,
            float tick,
            int robot)
        {
            Vector3 target = Vector3.zero;
            if (a.Carrying || b.Carrying)
            {
                // 적재 중 유닛+차량은 강체 — 잔여 오프셋을 즉시 소거
                if (_swerveOffsets != null && robot < _swerveOffsets.Length)
                    _swerveOffsets[robot] = Vector3.zero;
                return position;
            }
            {
                int travelX = Mathf.Abs(b.X - a.X);
                int travelZ = Mathf.Abs(b.Y - a.Y);
                if (travelX != 0 || travelZ != 0)
                {
                    bool travelAlongX = travelX >= travelZ;
                    int cellX = Mathf.RoundToInt(position.x);
                    int cellZ = Mathf.RoundToInt(position.z);
                    float bestDistance = float.MaxValue;
                    for (int step = -2; step <= 2; step++)
                    {
                        (int X, int Y) cell = travelAlongX
                            ? (cellX + step, cellZ)
                            : (cellX, cellZ + step);
                        VehiclePose pose;
                        if (!TryGetParkedPose(cell, tick, robot, out pose))
                            continue;
                        bool carHorizontal =
                            pose.Orientation == VehicleOrientation.Horizontal;
                        // 차 축과 평행 이동은 좌우 바퀴 사이 통로라 보정 불요
                        if (carHorizontal == travelAlongX) continue;
                        var second = pose.SecondCell;
                        if (carHorizontal)
                        {
                            float distance = Mathf.Abs(position.z - pose.Y);
                            if (distance <= 1.25f && distance < bestDistance)
                            {
                                bestDistance = distance;
                                target = new Vector3(
                                    (pose.X + second.X) * 0.5f - position.x,
                                    0f,
                                    0f);
                            }
                        }
                        else
                        {
                            float distance = Mathf.Abs(position.x - pose.X);
                            if (distance <= 1.25f && distance < bestDistance)
                            {
                                bestDistance = distance;
                                target = new Vector3(
                                    0f,
                                    0f,
                                    (pose.Y + second.Y) * 0.5f - position.z);
                            }
                        }
                    }
                }
            }
            if (_swerveOffsets == null || robot >= _swerveOffsets.Length)
                return position + target;
            _swerveOffsets[robot] = Vector3.Lerp(
                _swerveOffsets[robot],
                target,
                1f - Mathf.Exp(-7f * Time.deltaTime));
            return position + _swerveOffsets[robot];
        }

        /// <summary>해당 셀에 지금 서 있는 주차 차량 pose — 고정 차량 + 아직
        /// 들리지 않은 이동 대상 차량(자기 LiftTick 전까지)을 함께 본다.</summary>
        private bool TryGetParkedPose(
            (int X, int Y) cell, float tick, int robot, out VehiclePose pose)
        {
            if (_fixedPoseByCell.TryGetValue(cell, out pose)) return true;
            int vehicle;
            if (_movableVehicleByCell.TryGetValue(cell, out vehicle))
            {
                PipelinedMissionV2 mission;
                bool hasMission = _missions.TryGetValue(vehicle, out mission);
                // 자기 미션 대상 차량은 제외 — 도킹 블렌딩이 위치를 담당하므로
                // 스워브까지 끼어들면 리프팅 직전 이중 움직임이 생긴다.
                if (hasMission && mission.RobotIndex == robot)
                {
                    pose = default(VehiclePose);
                    return false;
                }
                if (!hasMission || tick < mission.LiftTick)
                {
                    pose = _problem.Slots[
                        _problem.InitialVehicleSlots[vehicle]].Pose;
                    return true;
                }
            }
            pose = default(VehiclePose);
            return false;
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
