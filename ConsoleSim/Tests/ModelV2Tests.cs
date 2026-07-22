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
            passed += Run("⑧ 휴리스틱 최적성 — 정확해 격차 10% 이내·확장 감소", TestWeightedOptimalityGap);
            passed += Run("⑨ 규모 경계 — 차량3대도 정확 정보탐색으로 해결", TestThreeVehicleExact);
            passed += Run("⑩ 10% bounded 탐색 — 소형 실제 격차·상한 검증", TestBoundedSearch);
            passed += Run("⑪ rolling 분해 — 한 창에서는 전역 exact와 동일", TestRollingMatchesExact);
            passed += Run("⑫ rolling 확장 — 차량6대·유한 슬롯6면 보존", TestRollingSixVehicles);
            passed += Run("⑬ 시간 의미 분리 — 물리 하차시간≠예약 안전버퍼", TestTimingSeparation);
            passed += Run("⑭ 실제 블록 — 3셀 통로·벽·가로/세로 혼합 차량", TestParkingBlockGeometry);
            passed += Run("⑮ α↔N 회계 — 적치 전용비용 차감·용량부족 실패", TestCapacityTradeoff);
            passed += Run("⑯ 상태 타임라인 — 매 틱 차량·로봇 pose 보존", TestTimelineCapture);
            Console.WriteLine($"\nV2 타당성 게이트 {passed}/16 통과");
            return passed;
        }

        public static EmergencyProblemV2 TwoVehicleProblem(int stagingSlots = 2)
        {
            return V2ProblemFactory.LineProblem(vehicleCount: 2, stagingSlots: stagingSlots);
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

        private static void TestWeightedOptimalityGap()
        {
            var problem = TwoVehicleProblem();
            var exact = ExactEmergencySolverV2.Solve(problem, maxExpansions: 500000);
            Assert(exact.Success, "정확해 탐색 실패");

            ExactEmergencyResultV2 selected = null;
            int selectedWeight = 0;
            foreach (int weight in new[] { 1, 2, 3 })
            {
                var candidate = ExactEmergencySolverV2.SolveWeighted(
                    problem, heuristicWeight: weight, maxExpansions: 200000);
                Assert(candidate.Success, $"가중치 {weight} 탐색 실패");
                double candidateGap = (double)candidate.Ticks / exact.Ticks - 1.0;
                Console.WriteLine(
                    $"   w={weight}: {candidate.Ticks}틱({candidateGap:P1}), 확장={candidate.ExpandedStates}");
                if (candidateGap <= 0.10 &&
                    (selected == null || candidate.ExpandedStates < selected.ExpandedStates))
                {
                    selected = candidate;
                    selectedWeight = weight;
                }
            }

            Assert(selected != null, "10% 최적성 기준을 만족하는 휴리스틱 가중치가 없음");
            Assert(selected.ExpandedStates < exact.ExpandedStates,
                $"휴리스틱이 탐색을 줄이지 못함: exact={exact.ExpandedStates}, selected={selected.ExpandedStates}");
            Assert(selectedWeight == 1,
                $"기본 가중치 재검토 필요: 선택값={selectedWeight}");
            double gap = (double)selected.Ticks / exact.Ticks - 1.0;
            Console.WriteLine(
                $"   선택 w={selectedWeight}: gap={gap:P1}, 확장 {exact.ExpandedStates}→{selected.ExpandedStates}");
        }

        private static void TestThreeVehicleExact()
        {
            var problem = V2ProblemFactory.LineProblem(vehicleCount: 3, stagingSlots: 3);
            var result = ExactEmergencySolverV2.SolveWeighted(
                problem, heuristicWeight: 1, maxExpansions: 1000000);
            Assert(result.Success, result.FailReason);
            Assert(result.FinalVehicleCount == 3, "차량3대 보존 실패");
            Assert(result.FinalVehicleSlots.Distinct().Count() == 3, "차량3대 슬롯 중복");
            Assert(result.RotationActions >= 3, "세로 적치에 필요한 회전 누락");
            Console.WriteLine(
                $"   차량3대: makespan={result.Ticks}틱, 확장={result.ExpandedStates}상태");
        }

        private static void TestBoundedSearch()
        {
            foreach (int n in new[] { 2, 3 })
            {
                var problem = V2ProblemFactory.LineProblem(n);
                var exact = ExactEmergencySolverV2.SolveWeighted(
                    problem, heuristicWeight: 1, maxExpansions: 1000000);
                var bounded = ExactEmergencySolverV2.SolveBounded10Percent(
                    problem, maxExpansions: 1000000);
                Assert(exact.Success && bounded.Success, $"차량{n}대 bounded 비교 실패");
                double gap = (double)bounded.Ticks / exact.Ticks - 1.0;
                Assert(gap <= 0.10 + 1e-9,
                    $"차량{n}대 실제 격차 {gap:P1}가 10%를 초과");
                Console.WriteLine(
                    $"   차량{n}: exact={exact.Ticks}/{exact.ExpandedStates:N0}, " +
                    $"bounded={bounded.Ticks}/{bounded.ExpandedStates:N0}, gap={gap:P1}");
            }
        }

        private static void TestRollingMatchesExact()
        {
            var problem = V2ProblemFactory.LineProblem(2);
            var exact = ExactEmergencySolverV2.SolveWeighted(
                problem, heuristicWeight: 1, maxExpansions: 500000);
            var rolling = RollingBatchPlannerV2.Solve(
                problem, batchSize: 4, maxExpansionsPerBatch: 500000);
            Assert(exact.Success && rolling.Success, "exact/rolling 비교 실패");
            Assert(rolling.TotalTicks == exact.Ticks,
                $"한 창인데 exact와 다름: exact={exact.Ticks}, rolling={rolling.TotalTicks}");
            Assert(rolling.FinalStagingSlotIds.Distinct().Count() == 2,
                "rolling 결과가 슬롯을 중복 사용");
        }

        private static void TestRollingSixVehicles()
        {
            var rolling = RollingBatchPlannerV2.Solve(
                V2ProblemFactory.LineProblem(6),
                batchSize: 3,
                maxExpansionsPerBatch: 1000000);
            Assert(rolling.Success, rolling.FailReason);
            Assert(rolling.VehicleCount == 6, "rolling 차량 수 불일치");
            Assert(rolling.FinalStagingSlotIds.Length == 6, "최종 적치 차량 누락");
            Assert(rolling.FinalStagingSlotIds.Distinct().Count() == 6, "최종 적치 슬롯 중복");
            Assert(rolling.BatchSizes.SequenceEqual(new[] { 3, 3 }), "예상 3+3 창 분해가 아님");
            Console.WriteLine(
                $"   차량6대: {rolling.TotalTicks}틱, {string.Join("+", rolling.BatchSizes)}, " +
                $"확장={rolling.ExpandedStates:N0}");
        }

        private static void TestTimingSeparation()
        {
            var baseline = ExactEmergencySolverV2.SolveWeighted(
                V2ProblemFactory.LineProblem(2,
                    timing: new OperationTimingV2(1, 1, 0)),
                heuristicWeight: 1, maxExpansions: 500000);
            var slowerDrop = ExactEmergencySolverV2.SolveWeighted(
                V2ProblemFactory.LineProblem(2,
                    timing: new OperationTimingV2(1, 3, 0)),
                heuristicWeight: 1, maxExpansions: 500000);
            var safetyOnly = ExactEmergencySolverV2.SolveWeighted(
                V2ProblemFactory.LineProblem(2,
                    timing: new OperationTimingV2(1, 1, 12)),
                heuristicWeight: 1, maxExpansions: 500000);
            Assert(baseline.Success && slowerDrop.Success && safetyOnly.Success, "시간 분리 탐색 실패");
            Assert(slowerDrop.Ticks > baseline.Ticks,
                "물리 하차시간 증가가 makespan에 반영되지 않음");
            Assert(safetyOnly.Ticks == baseline.Ticks,
                "예약 안전버퍼가 물리 exact makespan을 오염시킴");
            Console.WriteLine(
                $"   baseline={baseline.Ticks}, drop1→3={slowerDrop.Ticks}, " +
                $"safety0→12={safetyOnly.Ticks}(불변)");
        }

        private static void TestParkingBlockGeometry()
        {
            var problem = V2ProblemFactory.ParkingBlockProblem();
            Assert(!problem.IsFloor(1, 0), "주차면/벽이 열린 FullFloor로 남음");
            Assert(problem.IsFloor(0, 0) && problem.IsFloor(0, 1), "세로 적치 베이가 닫힘");
            Assert(problem.IsFloor(5, 2) && problem.IsFloor(5, 3) && problem.IsFloor(5, 4),
                "폭 3셀 통로가 연속하지 않음");

            var result = ExactEmergencySolverV2.SolveWeighted(
                problem, heuristicWeight: 1, maxExpansions: 1000000);
            Assert(result.Success, result.FailReason);
            Assert(result.FinalVehicleCount == 2, "혼합 방향 차량 보존 실패");
            Assert(result.FinalVehicleSlots.Distinct().Count() == 2, "실제 베이 중복 적치");
            Assert(result.RotationActions >= 1, "가로 방해 차량의 세로 베이 회전 누락");
            Console.WriteLine(
                $"   parking-block: {result.Ticks}틱, 회전={result.RotationActions}, " +
                $"확장={result.ExpandedStates:N0}");
        }

        private static void TestCapacityTradeoff()
        {
            var baseline = CapacityTradeoffV2.EvaluateExact(
                V2ProblemFactory.ParkingBlockProblem(0, 0), 0);
            var oneParkingLand = CapacityTradeoffV2.EvaluateExact(
                V2ProblemFactory.ParkingBlockProblem(1, 1), 1);
            var oneNonParking = CapacityTradeoffV2.EvaluateExact(
                V2ProblemFactory.ParkingBlockProblem(1, 1), 0);
            var twoParkingLand = CapacityTradeoffV2.EvaluateExact(
                V2ProblemFactory.ParkingBlockProblem(2, 2), 2);
            var insufficient = CapacityTradeoffV2.EvaluateExact(
                V2ProblemFactory.ParkingBlockProblem(2, 1), 1);

            Assert(baseline.Success && baseline.ClearanceTicks == 0 && baseline.NetAlpha == 0,
                "기준안이 0틱/α0이 아님");
            Assert(oneParkingLand.Success && oneParkingLand.NetAlpha == 0,
                "주차면 1개 전용 비용이 α에서 차감되지 않음");
            Assert(oneNonParking.Success && oneNonParking.NetAlpha == 1,
                "비주차 포장 적치의 순α 계산 오류");
            Assert(twoParkingLand.Success && twoParkingLand.NetAlpha == 0,
                "추가2/전용2 동일부지 회계 오류");
            Assert(!insufficient.Success && insufficient.FailReason.Contains("적치 용량 부족"),
                "차량2/적치1 정책이 실패하지 않음");
        }

        private static void TestTimelineCapture()
        {
            var problem = V2ProblemFactory.ParkingBlockProblem();
            var result = ExactEmergencySolverV2.SolveWeighted(
                problem, heuristicWeight: 1, maxExpansions: 1000000,
                captureTimeline: true);
            Assert(result.Success, result.FailReason);
            Assert(result.Timeline.Count == result.Ticks + 1,
                "타임라인 길이가 0틱 초기상태+행동틱과 불일치");
            foreach (var frame in result.Timeline)
            {
                Assert(frame.Robots.Length == 2, $"t={frame.Tick} 로봇 누락");
                Assert(frame.Vehicles.Length == 2, $"t={frame.Tick} 차량 생성·소멸");
                Assert(frame.Vehicles.Select(v => v.VehicleId).Distinct().Count() == 2,
                    $"t={frame.Tick} 차량 ID 중복");
            }
            var final = result.Timeline[result.Timeline.Count - 1];
            Assert(final.Vehicles.All(v => !v.Carried && v.SlotIndex >= 0),
                "최종 프레임에 적재 중/무슬롯 차량이 남음");
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
