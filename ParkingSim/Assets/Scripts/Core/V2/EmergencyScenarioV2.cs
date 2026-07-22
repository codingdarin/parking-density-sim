using System;
using System.Collections.Generic;
using System.Linq;

namespace ParkingSim.Core.V2
{
    public sealed class EmergencyScenarioBuildResultV2
    {
        public bool Success { get; set; }
        public string FailReason { get; set; }
        public EmergencyProblemV2 Problem { get; set; }
        public int SelectedVehicleCount { get; set; }
    }

    /// <summary>
    /// 정적 주차 배치와 화재/확보구간을 분리한다.
    /// baseProblem의 Blocking 차량은 이동 가능한 후보이며, 확보구간과 겹치는 후보만 작업 차량이 된다.
    /// 선택되지 않은 후보는 해당 시나리오에서 고정 주차차량으로 남는다.
    /// </summary>
    public sealed class EmergencyScenarioV2
    {
        private readonly (int X, int Y)[] _requiredClearanceCells;

        public string Name { get; }
        public (int X, int Y) FireCell { get; }
        public IReadOnlyList<(int X, int Y)> RequiredClearanceCells => _requiredClearanceCells;

        public EmergencyScenarioV2(
            string name,
            (int X, int Y) fireCell,
            IEnumerable<(int X, int Y)> requiredClearanceCells)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("시나리오 이름이 필요함", nameof(name));
            if (requiredClearanceCells == null)
                throw new ArgumentNullException(nameof(requiredClearanceCells));
            Name = name;
            FireCell = fireCell;
            _requiredClearanceCells = requiredClearanceCells.Distinct().ToArray();
        }

        public EmergencyScenarioBuildResultV2 Build(EmergencyProblemV2 baseProblem)
        {
            if (baseProblem == null) throw new ArgumentNullException(nameof(baseProblem));
            var result = new EmergencyScenarioBuildResultV2();
            if (!baseProblem.IsFloor(FireCell.X, FireCell.Y))
            {
                result.FailReason = "화재 위치가 이동 가능 floor 밖임";
                return result;
            }
            foreach (var cell in _requiredClearanceCells)
            {
                if (!baseProblem.IsFloor(cell.X, cell.Y))
                {
                    result.FailReason = $"확보구간 셀 ({cell.X},{cell.Y})이 floor 밖임";
                    return result;
                }
            }

            var required = new HashSet<(int X, int Y)>(_requiredClearanceCells);
            foreach (VehiclePose fixedPose in baseProblem.FixedVehiclePoses)
            {
                if (Overlaps(fixedPose, required))
                {
                    result.FailReason = "고정 차량이 확보구간을 점유해 구조적으로 확보 불가";
                    return result;
                }
            }

            var selectedSources = new List<ParkingSlotV2>();
            var scenarioFixed = new List<VehiclePose>(baseProblem.FixedVehiclePoses);
            for (int vehicle = 0; vehicle < baseProblem.VehicleCount; vehicle++)
            {
                ParkingSlotV2 source = baseProblem.Slots[baseProblem.InitialVehicleSlots[vehicle]];
                if (Overlaps(source.Pose, required)) selectedSources.Add(source);
                else scenarioFixed.Add(source.Pose);
            }

            var slots = new List<ParkingSlotV2>();
            foreach (ParkingSlotV2 source in selectedSources)
                slots.Add(new ParkingSlotV2(source.Id, SlotKind.Blocking, source.Pose));
            foreach (ParkingSlotV2 staging in baseProblem.Slots.Where(s => s.Kind == SlotKind.Staging))
                slots.Add(new ParkingSlotV2(staging.Id, SlotKind.Staging, staging.Pose));

            result.Problem = new EmergencyProblemV2(
                baseProblem.Width,
                baseProblem.Height,
                baseProblem.CopyFloor(),
                slots,
                Enumerable.Range(0, selectedSources.Count),
                baseProblem.RobotStarts,
                _requiredClearanceCells,
                scenarioFixed,
                baseProblem.Timing,
                FireCell);
            result.SelectedVehicleCount = selectedSources.Count;
            result.Success = true;
            return result;
        }

        private static bool Overlaps(VehiclePose pose, ISet<(int X, int Y)> cells)
        {
            return cells.Contains((pose.X, pose.Y)) || cells.Contains(pose.SecondCell);
        }
    }
}
