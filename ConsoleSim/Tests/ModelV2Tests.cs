using System;
using System.Collections.Generic;
using System.Linq;
using ParkingSim.Core.V2;

namespace ParkingSim.Tests
{
    public static class ModelV2Tests
    {
        public static int RunAll()
        {
            int passed = 0;
            passed += Run("① 유한 적치 — 차량 2대/슬롯 1면은 즉시 실패", TestFiniteCapacity);
            passed += Run("② 공동 정확해 — 로봇2대가 차량·목적지를 공동 선택", TestExactJointSolve);
            passed += Run("③ 차량 보존 — 하차 후에도 차량 2대가 슬롯 점유", TestVehicleConservation);
            passed += Run("④ 방향·회전 — 가로 차량을 세로 적치면에 실제 배치", TestRotationRequired);
            passed += Run("⑤ 정점/엣지 예약 — 정상 직후 추종 허용", TestFollowingAllowed);
            passed += Run("⑥ 정점/엣지 예약 — 반대방향 맞교환 차단", TestSwapBlocked);
            passed += Run("⑦ 공동 계획 효과 — 정확해에서 로봇2대가 1대보다 빠름", TestTwoRobotSpeedup);
            Console.WriteLine($"\nV2 타당성 게이트 {passed}/7 통과");
            return passed;
        }

        public static EmergencyProblemV2 TwoVehicleProblem(int stagingSlots = 2)
        {
            var slots = new List<ParkingSlotV2>
            {
                new ParkingSlotV2(0, SlotKind.Blocking,
                    new VehiclePose(4, 2, VehicleOrientation.Horizontal)),
                new ParkingSlotV2(1, SlotKind.Blocking,
                    new VehiclePose(6, 2, VehicleOrientation.Horizontal)),
                new ParkingSlotV2(2, SlotKind.Staging,
                    new VehiclePose(0, 0, VehicleOrientation.Vertical)),
            };
            if (stagingSlots >= 2)
                slots.Add(new ParkingSlotV2(3, SlotKind.Staging,
                    new VehiclePose(2, 0, VehicleOrientation.Vertical)));

            var clearance = new List<(int X, int Y)>();
            for (int x = 4; x < 8; x++) clearance.Add((x, 2));
            return new EmergencyProblemV2(
                width: 8,
                height: 5,
                floor: EmergencyProblemV2.FullFloor(8, 5),
                slots: slots,
                initialVehicleSlots: new[] { 0, 1 },
                robotStarts: new[] { (0, 4), (2, 4) },
                clearanceCells: clearance);
        }

        private static void TestFiniteCapacity()
        {
            var result = ExactEmergencySolverV2.Solve(TwoVehicleProblem(stagingSlots: 1));
            Assert(!result.Success, "용량 부족인데 성공 처리됨");
            Assert(result.FailReason.Contains("적치 용량 부족"), "용량 부족 원인이 명시되지 않음");
            Assert(result.ExpandedStates == 0, "명백한 용량 부족을 탐색 전에 차단하지 못함");
        }

        private static void TestExactJointSolve()
        {
            var result = ExactEmergencySolverV2.Solve(TwoVehicleProblem(), maxExpansions: 500000);
            Assert(result.Success, "정확해 탐색 실패: " + result.FailReason);
            Assert(result.Ticks > 0, "0틱 해는 불가능");
            Assert(result.JointActions.Any(a => a.Contains("r1:lift")), "로봇1이 차량을 맡지 않음");
            Assert(result.JointActions.Any(a => a.Contains("r2:lift")), "로봇2가 차량을 맡지 않음");
            Console.WriteLine($"   최소 makespan={result.Ticks}틱, 확장={result.ExpandedStates}상태");
        }

        private static void TestVehicleConservation()
        {
            var problem = TwoVehicleProblem();
            var result = ExactEmergencySolverV2.Solve(problem, maxExpansions: 500000);
            Assert(result.Success, result.FailReason);
            Assert(result.InitialVehicleCount == problem.VehicleCount, "초기 차량 수 불일치");
            Assert(result.FinalVehicleCount == problem.VehicleCount, "차량이 생성·소멸함");
            Assert(result.FinalVehicleSlots.Length == 2, "최종 차량 위치가 누락됨");
            Assert(result.FinalVehicleSlots.Distinct().Count() == 2, "두 차량이 같은 슬롯에 중복 적치됨");
            foreach (int slot in result.FinalVehicleSlots)
                Assert(problem.Slots[slot].Kind == SlotKind.Staging, "차량이 적치면 밖에 남음");
        }

        private static void TestRotationRequired()
        {
            var result = ExactEmergencySolverV2.Solve(TwoVehicleProblem(), maxExpansions: 500000);
            Assert(result.Success, result.FailReason);
            Assert(result.RotationActions >= 2, "가로 차량 2대를 세로 슬롯에 넣는데 회전이 누락됨");
        }

        private static void TestFollowingAllowed()
        {
            var rt = new ReservationTableV2();
            rt.ReserveMove((1, 0), (2, 0), departureTick: 0);
            Assert(rt.IsMoveFree((0, 0), (1, 0), departureTick: 0),
                "앞 로봇이 떠난 셀로 정상 추종하는 이동을 차단함");
        }

        private static void TestSwapBlocked()
        {
            var rt = new ReservationTableV2();
            rt.ReserveMove((1, 0), (2, 0), departureTick: 0);
            Assert(!rt.IsMoveFree((2, 0), (1, 0), departureTick: 0),
                "반대방향 엣지 맞교환을 허용함");
        }

        private static void TestTwoRobotSpeedup()
        {
            var problem = TwoVehicleProblem();
            var one = ExactEmergencySolverV2.Solve(problem, maxExpansions: 500000, activeRobotCount: 1);
            var two = ExactEmergencySolverV2.Solve(problem, maxExpansions: 500000, activeRobotCount: 2);
            Assert(one.Success && two.Success, "1대/2대 정확해 비교 중 탐색 실패");
            Assert(two.Ticks < one.Ticks,
                $"공동 계획이 빨라지지 않음: 1대={one.Ticks}, 2대={two.Ticks}");
            Console.WriteLine($"   정확해: 1대={one.Ticks}틱, 2대={two.Ticks}틱 ({(1.0 - (double)two.Ticks / one.Ticks):P1} 단축)");
        }

        private static int Run(string name, Action test)
        {
            try
            {
                test();
                Console.WriteLine("PASS  " + name);
                return 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL  " + name + " — " + ex.Message);
                return 0;
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
