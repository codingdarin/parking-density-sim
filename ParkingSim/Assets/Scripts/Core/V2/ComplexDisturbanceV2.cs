using System;
using System.Collections.Generic;
using System.Linq;

namespace ParkingSim.Core.V2
{
    /// <summary>
    /// 대응 개시 시점(FMS 트리거 t=0)에 이미 존재하는 단지 도로 교란.
    /// 쓰러진 나무·낙하물은 floor 셀 봉쇄, 비관리 차량은 이동 불가 고정 차량,
    /// 운송 유닛 고장은 가용 유닛 수 감소로 표현한다.
    /// 사건 도중(t>0) 발생 교란의 상태 추출 재계획은 모델 밖 —
    /// 집계에서는 보수 상한(경과 시각 + 전체 재계획 시간)만 다룬다.
    /// </summary>
    public sealed class ComplexDisturbanceV2
    {
        public string Name { get; }
        public IReadOnlyList<(int X, int Y)> BlockedCells { get; }
        public IReadOnlyList<VehiclePose> UnmanagedVehicles { get; }
        public int ActiveRobotCount { get; }

        public ComplexDisturbanceV2(
            string name,
            IEnumerable<(int X, int Y)> blockedCells = null,
            IEnumerable<VehiclePose> unmanagedVehicles = null,
            int activeRobotCount = 4)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("교란 이름이 필요함", nameof(name));
            if (activeRobotCount < 1)
                throw new ArgumentOutOfRangeException(nameof(activeRobotCount));
            Name = name;
            BlockedCells = (blockedCells ?? Enumerable.Empty<(int X, int Y)>())
                .Distinct().ToList();
            UnmanagedVehicles = (unmanagedVehicles ?? Enumerable.Empty<VehiclePose>())
                .ToList();
            ActiveRobotCount = activeRobotCount;
        }
    }

    public sealed class DisturbedComplexBuildResultV2
    {
        public bool Success { get; set; }
        public string FailReason { get; set; }
        public ApartmentComplexScenarioV2 Scenario { get; set; }
    }

    /// <summary>
    /// 교란을 적용한 단지 시나리오 사본을 만든다. 원본 시나리오는 불변.
    /// 로봇 시작점·작업 슬롯·단지 진입구·전용구역 접근셀의 봉쇄는 명시적으로 거부한다
    /// (그 상황들은 별도 대응 절차가 필요한 문제라 본 모델의 주장 범위 밖).
    /// 전용구역의 접근셀 외 셀 봉쇄는 허용 — 종점 주변 협착의 영향을 관찰 대상으로 남긴다.
    /// </summary>
    public static class ApartmentComplexDisturbanceV2
    {
        public static DisturbedComplexBuildResultV2 Apply(
            ApartmentComplexScenarioV2 source,
            ComplexDisturbanceV2 disturbance)
        {
            if (source == null || source.BaseProblem == null)
                throw new ArgumentNullException(nameof(source));
            if (disturbance == null)
                throw new ArgumentNullException(nameof(disturbance));

            EmergencyProblemV2 problem = source.BaseProblem;
            var result = new DisturbedComplexBuildResultV2();

            var protectedCells = new HashSet<(int X, int Y)>(problem.RobotStarts);
            foreach (ParkingSlotV2 slot in problem.Slots)
            {
                protectedCells.Add((slot.Pose.X, slot.Pose.Y));
                protectedCells.Add(slot.Pose.SecondCell);
            }
            if (source.Entrances != null)
                foreach (ApartmentComplexEntranceV2 entrance in source.Entrances)
                    protectedCells.Add(entrance.Cell);
            if (source.Buildings != null)
                foreach (ApartmentBuildingV2 building in source.Buildings)
                    protectedCells.Add(building.FireEngineZone.ApproachCell);

            foreach ((int x, int y) in disturbance.BlockedCells)
            {
                if (!problem.IsFloor(x, y))
                {
                    result.FailReason = $"봉쇄 셀 ({x},{y})가 floor 밖임";
                    return result;
                }
                if (protectedCells.Contains((x, y)))
                {
                    result.FailReason =
                        $"봉쇄 셀 ({x},{y})가 보호 셀(로봇 시작·슬롯·진입구·접근셀)과 겹침";
                    return result;
                }
            }

            bool[,] floor = problem.CopyFloor();
            foreach ((int x, int y) in disturbance.BlockedCells)
                floor[x, y] = false;

            EmergencyProblemV2 disturbed;
            try
            {
                disturbed = new EmergencyProblemV2(
                    problem.Width,
                    problem.Height,
                    floor,
                    problem.Slots,
                    problem.InitialVehicleSlots,
                    problem.RobotStarts,
                    problem.CopyClearanceCells(),
                    problem.FixedVehiclePoses.Concat(disturbance.UnmanagedVehicles),
                    problem.Timing,
                    problem.FireCell);
            }
            catch (ArgumentException error)
            {
                // 봉쇄로 기존 슬롯 풋프린트가 floor 밖이 되거나 비관리 차량이 무효 배치인 경우
                result.FailReason = "교란 적용 불가: " + error.Message;
                return result;
            }

            result.Success = true;
            result.Scenario = new ApartmentComplexScenarioV2
            {
                BaseProblem = disturbed,
                Buildings = source.Buildings,
                Entrances = source.Entrances,
                BlockingVehicleCount = source.BlockingVehicleCount,
            };
            return result;
        }
    }
}
