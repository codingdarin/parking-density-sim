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
                    Vector3 naturalPosition = Vector3.Lerp(
                        RobotPosition(a, _robotUsesCustomView[robot]),
                        RobotPosition(b, _robotUsesCustomView[robot]),
                        fraction);
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
                    _robotViews[robot].transform.position = Vector3.Lerp(
                        RobotPosition(a, _robotUsesCustomView[robot]),
                        RobotPosition(b, _robotUsesCustomView[robot]),
                        fraction);
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

                bool carrying = StateAt(
                    _plan.RobotTimelines[robot],
                    stateTick).Carrying;
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
            bool carrying = StateAt(
                _plan.RobotTimelines[robot],
                stateTick).Carrying;
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
