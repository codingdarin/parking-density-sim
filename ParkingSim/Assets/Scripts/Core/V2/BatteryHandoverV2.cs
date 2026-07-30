using System;
using System.Collections.Generic;
using System.Linq;

namespace ParkingSim.Core.V2
{
    /// <summary>
    /// 운송 유닛 배터리 회계 모델. 소모는 미션 구간(StartTick~DropTick) 틱당 1,
    /// 유휴 0의 단순형이며 상태별 차등 소모는 민감도 축으로 미룬다.
    /// 용량·예비는 이동틱 단위 — 초 환산은 표시 단계에서 프로파일로 한다.
    /// </summary>
    public sealed class BatteryModelV2
    {
        public int CapacityTicks { get; }
        /// <summary>예비 임계 — 복귀 주행·안전 여유. 잔량이 (다음 미션 비용 + 예비)
        /// 미만이면 그 미션을 받지 않고 퇴역한다.</summary>
        public int ReserveTicks { get; }

        public BatteryModelV2(int capacityTicks, int reserveTicks)
        {
            if (capacityTicks <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacityTicks));
            if (reserveTicks < 0 || reserveTicks >= capacityTicks)
                throw new ArgumentOutOfRangeException(nameof(reserveTicks));
            CapacityTicks = capacityTicks;
            ReserveTicks = reserveTicks;
        }
    }

    public sealed class BatteryHandoverResultV2
    {
        public bool Success { get; set; }
        public string FailReason { get; set; }
        public bool HandoverOccurred { get; set; }
        public int RetiredRobot { get; set; } = -1;
        /// <summary>퇴역 결정 시각 = 퇴역 유닛의 직전 미션 완료 틱 (첫 미션 전이면 0)</summary>
        public int RetireDecisionTick { get; set; }
        /// <summary>동기 시점 — 결정 시각까지 시작된 모든 미션의 완료 틱</summary>
        public int SyncTick { get; set; }
        public IReadOnlyList<int> DeliveredVehicles { get; set; }
        public EmergencyProblemV2 ResidualProblem { get; set; }
        public PipelinedPlanResultV2 ResidualPlan { get; set; }
        /// <summary>핸드오버 없으면 원 계획 makespan, 있으면 SyncTick + 잔여 makespan</summary>
        public int TotalTicks { get; set; }
        public int DelayTicks { get; set; }
        public int[] ConsumedTicks { get; set; }
        public int[] RemainingTicks { get; set; }
    }

    /// <summary>
    /// 확정된 계획 위에 배터리 회계를 적용하고, 임계 도달 유닛의 미션 경계 퇴역과
    /// 잔여 작업 재계획(핸드오버)을 조립한다. 단일 핸드오버만 다루며(복수 동시
    /// 저전량은 한계), 퇴역 유닛의 충전소 복귀 주행과 교체 유닛의 배터리는
    /// 모델링하지 않는다(교체 유닛 만충 가정).
    /// </summary>
    public static class BatteryHandoverV2
    {
        public static BatteryHandoverResultV2 Evaluate(
            EmergencyProblemV2 problem,
            PipelinedPlanResultV2 plan,
            BatteryModelV2 battery,
            IReadOnlyList<int> initialChargeTicks,
            (int X, int Y)? replacementStart = null,
            int maxTick = 5000,
            int maxHighLevelCandidates = 8)
        {
            if (problem == null) throw new ArgumentNullException(nameof(problem));
            if (plan == null || !plan.Success || !plan.PhysicallyValid)
                throw new ArgumentException("유효한 원 계획이 필요함", nameof(plan));
            if (battery == null) throw new ArgumentNullException(nameof(battery));
            int robotCount = plan.RobotTimelines.Length;
            if (initialChargeTicks == null || initialChargeTicks.Count != robotCount)
                throw new ArgumentException(
                    "초기 전량은 계획의 유닛 수와 같아야 함", nameof(initialChargeTicks));
            foreach (int charge in initialChargeTicks)
                if (charge < 0 || charge > battery.CapacityTicks)
                    throw new ArgumentException("초기 전량이 용량 범위 밖임");

            var result = new BatteryHandoverResultV2
            {
                ConsumedTicks = new int[robotCount],
                RemainingTicks = initialChargeTicks.ToArray(),
                DeliveredVehicles = Array.Empty<int>(),
            };

            // 퇴역 스캔: 전 미션을 시작 시각 순으로 보며, 각 유닛의 잔량이
            // (미션 비용 + 예비) 미만이 되는 최초 미션에서 핸드오버가 발생한다.
            List<PipelinedMissionV2> ordered = plan.Missions
                .OrderBy(mission => mission.StartTick)
                .ThenBy(mission => mission.RobotIndex)
                .ToList();
            int retiredRobot = -1;
            int retireTick = 0;
            var lastDropByRobot = new int[robotCount];
            foreach (PipelinedMissionV2 mission in ordered)
            {
                int cost = MissionCost(mission);
                int robot = mission.RobotIndex;
                if (result.RemainingTicks[robot] < cost + battery.ReserveTicks)
                {
                    retiredRobot = robot;
                    retireTick = lastDropByRobot[robot];
                    break;
                }
                result.RemainingTicks[robot] -= cost;
                result.ConsumedTicks[robot] += cost;
                lastDropByRobot[robot] = mission.DropTick;
            }

            if (retiredRobot < 0)
            {
                result.Success = true;
                result.TotalTicks = plan.Ticks;
                result.DelayTicks = 0;
                return result;
            }

            // 미션 경계 의미론: 결정 시각까지 시작된 미션은 완결, 신규 배차만 중단.
            // 퇴역 유닛은 결정 시각까지 "완료한" 미션만 인정 — 결정과 같은 틱에
            // 시작 예정이던 미션(특히 t=0 출동 불능)은 잔여로 넘어가야 한다.
            // 회계를 완결분 기준으로 다시 계산한다 (위 스캔은 계획 전체 기준이었음).
            List<PipelinedMissionV2> halted = plan.Missions
                .Where(mission => mission.RobotIndex == retiredRobot
                    ? mission.DropTick <= retireTick
                    : mission.StartTick <= retireTick)
                .ToList();
            Array.Clear(result.ConsumedTicks, 0, robotCount);
            for (int robot = 0; robot < robotCount; robot++)
                result.RemainingTicks[robot] = initialChargeTicks[robot];
            foreach (PipelinedMissionV2 mission in halted)
            {
                int cost = MissionCost(mission);
                result.ConsumedTicks[mission.RobotIndex] += cost;
                result.RemainingTicks[mission.RobotIndex] -= cost;
            }
            int syncTick = retireTick;
            foreach (PipelinedMissionV2 mission in halted)
                if (mission.DropTick > syncTick) syncTick = mission.DropTick;

            result.HandoverOccurred = true;
            result.RetiredRobot = retiredRobot;
            result.RetireDecisionTick = retireTick;
            result.SyncTick = syncTick;
            result.DeliveredVehicles =
                halted.Select(mission => mission.VehicleIndex).ToList();

            BuildResidual(
                problem, plan, result, replacementStart,
                maxTick, maxHighLevelCandidates);
            return result;
        }

        /// <summary>미션 소모 틱 = 시작~하차 시간 간격</summary>
        public static int MissionCost(PipelinedMissionV2 mission)
        {
            return mission.DropTick - mission.StartTick;
        }

        private static void BuildResidual(
            EmergencyProblemV2 problem,
            PipelinedPlanResultV2 plan,
            BatteryHandoverResultV2 result,
            (int X, int Y)? replacementStart,
            int maxTick,
            int maxHighLevelCandidates)
        {
            int robotCount = plan.RobotTimelines.Length;
            var delivered = new HashSet<int>(result.DeliveredVehicles);
            List<PipelinedMissionV2> halted = plan.Missions
                .Where(mission => delivered.Contains(mission.VehicleIndex))
                .ToList();
            var usedStaging = new HashSet<int>(
                halted.Select(mission => mission.DestinationSlot));

            // 슬롯 재구성: 잔여 차량의 출발 슬롯 + 미사용 적치 슬롯 (인덱스 재매핑)
            var slots = new List<ParkingSlotV2>();
            int remaining = 0;
            for (int vehicle = 0; vehicle < problem.VehicleCount; vehicle++)
            {
                if (delivered.Contains(vehicle)) continue;
                ParkingSlotV2 source =
                    problem.Slots[problem.InitialVehicleSlots[vehicle]];
                slots.Add(new ParkingSlotV2(slots.Count, SlotKind.Blocking, source.Pose));
                remaining++;
            }
            for (int index = 0; index < problem.Slots.Count; index++)
            {
                if (problem.Slots[index].Kind != SlotKind.Staging) continue;
                if (usedStaging.Contains(index)) continue;
                slots.Add(new ParkingSlotV2(
                    slots.Count, SlotKind.Staging, problem.Slots[index].Pose));
            }

            // 인도 완료 차량 = 적치 pose의 고정 차량
            var fixedPoses = new List<VehiclePose>(problem.FixedVehiclePoses);
            foreach (int stagingIndex in usedStaging)
                fixedPoses.Add(problem.Slots[stagingIndex].Pose);
            var fixedCells = new HashSet<(int X, int Y)>();
            foreach (VehiclePose pose in fixedPoses)
            {
                fixedCells.Add((pose.X, pose.Y));
                fixedCells.Add(pose.SecondCell);
            }

            // 유닛 정지 위치 = 자기 "마지막 완결 미션"의 하차 시점 위치.
            // 원 계획의 t_sync 시점 위치를 쓰면 취소된 미션의 주행이 섞이므로 부정확.
            var starts = new List<(int X, int Y)>();
            for (int robot = 0; robot < robotCount; robot++)
            {
                if (robot == result.RetiredRobot) continue;
                starts.Add(RestPosition(plan, problem, robot, halted));
            }
            if (replacementStart.HasValue) starts.Add(replacementStart.Value);
            if (starts.Count == 0)
            {
                result.FailReason = "핸드오버 후 가용 유닛이 없음";
                return;
            }
            if (starts.Distinct().Count() != starts.Count)
            {
                result.FailReason = "교체 유닛 시작점이 생존 유닛 위치와 겹침";
                return;
            }
            // 재배치 규칙 9: 필요한 조만 활성화. 잔여 차량보다 많은 유닛을 세우면
            // 유휴 활성 유닛이 인도 차량 밑 정지 위치에서 잔여 동선과 충돌한다.
            // 비활성 유닛은 도크/차량 하부 정차로 간주하며 그 간섭은 모델 밖(한계).
            if (starts.Count > remaining)
                starts.RemoveRange(remaining, starts.Count - remaining);

            // 퇴역 유닛 정지 위치: ① 인도 차량(고정 pose) 밑이면 추가 조치 불요(저상형)
            // ② 자기 차고(시작점)면 도크 정차로 간주해 통행 방해 없음(시작점은 상호
            // 배타 도크) ③ 그 외 노상이면 해당 셀을 봉쇄해 잔여 계획이 통과하지 못하게 한다.
            bool[,] floor = problem.CopyFloor();
            (int X, int Y) rest =
                RestPosition(plan, problem, result.RetiredRobot, halted);
            bool atHomeDock = rest == problem.RobotStarts[result.RetiredRobot];
            if (!fixedCells.Contains(rest) && !atHomeDock)
            {
                if (starts.Contains(rest))
                {
                    result.FailReason = "퇴역 유닛 정지 셀이 가용 유닛 시작점과 겹침";
                    return;
                }
                floor[rest.X, rest.Y] = false;
            }

            EmergencyProblemV2 residual;
            try
            {
                residual = new EmergencyProblemV2(
                    problem.Width,
                    problem.Height,
                    floor,
                    slots,
                    Enumerable.Range(0, remaining),
                    starts,
                    problem.CopyClearanceCells(),
                    fixedPoses,
                    problem.Timing,
                    problem.FireCell);
            }
            catch (ArgumentException error)
            {
                result.FailReason = "잔여 문제 구성 실패: " + error.Message;
                return;
            }

            result.ResidualProblem = residual;
            PipelinedPlanResultV2 residualPlan = PipelinedPrioritizedPlannerV2.Solve(
                residual,
                maxTick: maxTick,
                activeRobotCount: starts.Count,
                maxHighLevelCandidates: maxHighLevelCandidates);
            result.ResidualPlan = residualPlan;
            if (!residualPlan.Success || !residualPlan.PhysicallyValid)
            {
                result.FailReason = "잔여 재계획 실패: " + residualPlan.FailReason;
                return;
            }

            result.Success = true;
            result.TotalTicks = result.SyncTick + residualPlan.Ticks;
            result.DelayTicks = result.TotalTicks - plan.Ticks;
        }

        /// <summary>유닛 정지 위치 — 완결(halted) 미션 중 자기 마지막 하차 시점의
        /// 타임라인 위치. 완결 미션이 없으면 원 시작점.</summary>
        private static (int X, int Y) RestPosition(
            PipelinedPlanResultV2 plan,
            EmergencyProblemV2 problem,
            int robot,
            IReadOnlyList<PipelinedMissionV2> halted)
        {
            int lastDrop = -1;
            foreach (PipelinedMissionV2 mission in halted)
                if (mission.RobotIndex == robot && mission.DropTick > lastDrop)
                    lastDrop = mission.DropTick;
            if (lastDrop < 0) return problem.RobotStarts[robot];
            List<TimedRobotStateV2> timeline = plan.RobotTimelines[robot];
            for (int index = timeline.Count - 1; index >= 0; index--)
                if (timeline[index].Tick <= lastDrop)
                    return (timeline[index].X, timeline[index].Y);
            return problem.RobotStarts[robot];
        }
    }
}
