using System;
using System.Collections.Generic;
using System.Linq;
using ParkingSim.Core.V2;

namespace ParkingSim.Tests
{
    public static class ModelV2Tests
    {
        public const int ExpectedGateCount = 45;

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
            passed += Run("⑰ ASCII 맵 — 기존 소형 블록 exact 결과 재현", TestAsciiMapReproducesBlock);
            passed += Run("⑱ 대표 기하 — L/T/아파트형 맵 불변조건 통과", TestMapCatalogGeometry);
            passed += Run("⑲ 맵 방어 — 경계 밖 차량 풋프린트 거부", TestInvalidMapRejected);
            passed += Run("⑳ 화재 시나리오 — 전체 확보구간 exact 19틱 재현", TestEmergencyScenarioFullClearance);
            passed += Run("㉑ 선택 이동 — 확보구간과 겹친 차량만 작업 대상으로 선정", TestEmergencyScenarioSelectsBlockers);
            passed += Run("㉒ 구조 실패 — 고정 차량이 확보구간을 막으면 탐색 전 거부", TestEmergencyScenarioRejectsFixedObstruction);
            passed += Run("㉓ 운영 후보 — 직선4대 exact 30틱·물리 유효 재현", TestPipelinedLineFour);
            passed += Run("㉔ 대표 기하 — 혼합방향 소형 블록 exact 19틱 재현", TestPipelinedParkingBlock);
            passed += Run("㉕ 아파트 프로토타입 — 고정차16 보존·20틱 기준", TestPipelinedApartmentPrototype);
            passed += Run("㉖ 강화 아파트형 — 기둥·고정차35·41틱 기준", TestPipelinedConstrainedApartment);
            passed += Run("㉗ 시드 맵 — 동일 시드 재현·다른 시드 변형", TestConstrainedApartmentSeedReproducibility);
            passed += Run("㉘ 정적 가지치기 — 시드2 해 보존·확장 상한", TestStaticReachabilityPruningRegression);
            passed += Run("㉙ 운송 유닛 일반화 — 1·2·4조 동적 타임라인", TestPipelinedRobotCountGeneralization);
            passed += Run("㉚ 운송 유닛 상한 — 8조가 차량8대에 각각 1임무", TestPipelinedEightRobots);
            passed += Run("㉛ 다차량 아파트형 — 차량8·고정차22·8조 46틱", TestApartmentSerialAisle);
            passed += Run("㉜ 운영 통로 팩토리 — 레인·거리별 차량 수와 유한 적치", TestCorridorScenarioFactory);
            passed += Run("㉝ 포켓 강건성 — 14개 오프셋20종 모두 7분 통과", TestPocketLayoutRobustness);
            passed += Run("㉞ 핵심경로 — 최소 개통시간 경로 선택·전면 재배치 단축", TestEmergencyAccessRouteSelection);
            passed += Run("㉟ 지상 아파트형 — 2경로 선택·법정 전용구역 비점유", TestSurfaceApartmentAccessSelection);
            passed += Run("㊱ 자동경로 — 지상형 수동36틱 재현·하부 선택", TestAutomaticSurfaceAccessSelection);
            passed += Run("㊲ 자동경로 실패 — 중심선 경로 없음", TestAutomaticNoCenterline);
            passed += Run("㊳ 자동경로 실패 — 중심선은 있으나 폭3 확보 불가", TestAutomaticInsufficientWidth);
            passed += Run("㊴ 자동경로 실패 — 고정 차량이 모든 폭3 후보 차단", TestAutomaticFixedObstruction);
            passed += Run("㊵ 자동경로 실패 — 유한 적치 용량 부족", TestAutomaticInsufficientCapacity);
            passed += Run("㊶ 자동경로 실패 — 물리 로봇 계획 실패", TestAutomaticPhysicalPlanningFailure);
            passed += Run("㊷ 자동경로 실패 — 중심선 탐색 상한 도달", TestAutomaticSearchLimit);
            passed += Run("㊸ 자동경로 0틱 — 이미 열린 경로", TestAutomaticAlreadyClear);
            passed += Run("㊹ 자동경로 중복 — 유사 후보 제거", TestAutomaticDuplicateRemoval);
            passed += Run("㊺ 자동경로 재현 — 동일 후보 순서·결과", TestAutomaticReproducibility);
            Console.WriteLine(
                $"\nV2 타당성 게이트 {passed}/{ExpectedGateCount} 통과");
            return passed;
        }

        public static EmergencyProblemV2 TwoVehicleProblem(int stagingSlots = 2)
        {
            return V2ProblemFactory.LineProblem(vehicleCount: 2, stagingSlots: stagingSlots);
        }

        public static int RunEmergencyAccessGate()
        {
            int passed = Run("핵심경로 단독 게이트", TestEmergencyAccessRouteSelection);
            passed += Run("지상 아파트형 단독 게이트", TestSurfaceApartmentAccessSelection);
            return passed;
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

        private static void TestAsciiMapReproducesBlock()
        {
            var problem = V2MapCatalog.SmallParkingBlock.Build();
            var result = ExactEmergencySolverV2.SolveWeighted(
                problem, heuristicWeight: 1, maxExpansions: 1000000);
            Assert(problem.Width == 12 && problem.Height == 6, "소형 블록 크기 불일치");
            Assert(problem.VehicleCount == 2 && problem.StagingCapacity == 2,
                "ASCII 슬롯/차량 파싱 오류");
            Assert(result.Success && result.Ticks == 19,
                "기존 소형 블록 exact 19틱을 재현하지 못함");
        }

        private static void TestMapCatalogGeometry()
        {
            var lTurn = V2MapCatalog.LTurn.Build();
            var tJunction = V2MapCatalog.TJunction.Build();
            var apartment = V2MapCatalog.ApartmentAislePrototype.Build();

            Assert(lTurn.Width == 7 && lTurn.Height == 7 && lTurn.IsFloor(6, 0),
                "L자 통로 파싱 오류");
            Assert(tJunction.Width == 9 && tJunction.Height == 7 &&
                   tJunction.IsFloor(0, 6) && !tJunction.IsFloor(0, 0),
                "T자 통로 파싱 오류");
            Assert(apartment.Width == 18 && apartment.Height == 9,
                "아파트형 프로토타입 크기 불일치");
            Assert(apartment.VehicleCount == 2 && apartment.StagingCapacity == 2,
                "아파트형 작업 차량/적치면 파싱 오류");
            Assert(apartment.FixedVehiclePoses.Count == 16,
                "아파트형 고정 주차차량 수 불일치");
        }

        private static void TestInvalidMapRejected()
        {
            bool rejected = false;
            try
            {
                new AsciiMapV2("invalid-footprint", "12", ".>").Build();
            }
            catch (ArgumentException)
            {
                rejected = true;
            }
            Assert(rejected, "맵 경계 밖 1×2 차량을 허용함");
        }

        private static void TestEmergencyScenarioFullClearance()
        {
            var map = V2MapCatalog.SmallParkingBlock.Build();
            var scenario = new EmergencyScenarioV2(
                "full-clearance",
                fireCell: (11, 4),
                requiredClearanceCells: map.CopyClearanceCells());
            var built = scenario.Build(map);
            Assert(built.Success && built.SelectedVehicleCount == 2, built.FailReason);
            Assert(built.Problem.FireCell.HasValue && built.Problem.FireCell.Value == (11, 4),
                "화재 위치 메타데이터가 문제로 전달되지 않음");
            var exact = ExactEmergencySolverV2.SolveWeighted(
                built.Problem, heuristicWeight: 1, maxExpansions: 1000000);
            Assert(exact.Success && exact.Ticks == 19,
                "시나리오 분리 후 기존 전체 확보 19틱을 재현하지 못함");
        }

        private static void TestEmergencyScenarioSelectsBlockers()
        {
            var map = V2MapCatalog.SmallParkingBlock.Build();
            var scenario = new EmergencyScenarioV2(
                "horizontal-only",
                fireCell: (7, 4),
                requiredClearanceCells: new[] { (6, 3), (7, 3) });
            var built = scenario.Build(map);
            Assert(built.Success, built.FailReason);
            Assert(built.SelectedVehicleCount == 1 && built.Problem.VehicleCount == 1,
                "확보구간 밖 차량까지 작업 대상으로 선택함");
            Assert(built.Problem.FixedVehiclePoses.Count == 1,
                "선택되지 않은 이동 후보가 고정 주차차량으로 보존되지 않음");
        }

        private static void TestEmergencyScenarioRejectsFixedObstruction()
        {
            var map = V2MapCatalog.ApartmentAislePrototype.Build();
            var scenario = new EmergencyScenarioV2(
                "fixed-obstruction",
                fireCell: (4, 5),
                requiredClearanceCells: new[] { (0, 6) });
            var built = scenario.Build(map);
            Assert(!built.Success && built.Problem == null,
                "고정 차량이 확보구간을 막는데 문제를 생성함");
            Assert(built.FailReason.Contains("고정 차량"),
                "구조 실패 원인을 고정 차량으로 보고하지 않음");
        }

        private static void TestPipelinedLineFour()
        {
            PipelinedPlanResultV2 result = PipelinedPrioritizedPlannerV2.Solve(
                V2ProblemFactory.LineProblem(4));
            Assert(result.Success && result.PhysicallyValid, result.FailReason);
            Assert(result.Ticks == 30, "직선4대 exact 기준 30틱 불일치");
            Assert(result.Missions.Count == 4, "차량4대 미션 누락");
            Assert(result.FinalVehicleSlots.Distinct().Count() == 4,
                "최종 유한 적치면 중복");
        }

        private static void TestPipelinedParkingBlock()
        {
            PipelinedPlanResultV2 result = PipelinedPrioritizedPlannerV2.Solve(
                V2MapCatalog.SmallParkingBlock.Build());
            Assert(result.Success && result.PhysicallyValid, result.FailReason);
            Assert(result.Ticks == 19, "소형 혼합방향 블록 exact 기준 19틱 불일치");
            Assert(result.FinalVehicleSlots.Distinct().Count() == 2,
                "소형 블록 적치면 중복");
        }

        private static void TestPipelinedApartmentPrototype()
        {
            EmergencyProblemV2 map = V2MapCatalog.ApartmentAislePrototype.Build();
            var scenario = new EmergencyScenarioV2(
                "apartment-regression", (17, 5), map.CopyClearanceCells());
            EmergencyScenarioBuildResultV2 built = scenario.Build(map);
            Assert(built.Success && built.SelectedVehicleCount == 2, built.FailReason);
            PipelinedPlanResultV2 result = PipelinedPrioritizedPlannerV2.Solve(built.Problem);
            Assert(result.Success && result.PhysicallyValid, result.FailReason);
            Assert(result.Ticks == 20, "아파트 프로토타입 기준 20틱 불일치");
            Assert(map.FixedVehiclePoses.Count == 16, "고정차량 16대 보존 실패");
        }

        private static void TestPipelinedConstrainedApartment()
        {
            EmergencyProblemV2 map = V2MapCatalog.ApartmentConstrainedPrototype.Build();
            var scenario = new EmergencyScenarioV2(
                "constrained-regression", (19, 5), map.CopyClearanceCells());
            EmergencyScenarioBuildResultV2 built = scenario.Build(map);
            Assert(built.Success && built.SelectedVehicleCount == 2, built.FailReason);
            PipelinedPlanResultV2 result = PipelinedPrioritizedPlannerV2.Solve(built.Problem);
            Assert(result.Success && result.PhysicallyValid, result.FailReason);
            Assert(result.Ticks == 41, "강화 아파트형 기준 41틱 불일치");
            Assert(map.FixedVehiclePoses.Count == 35, "고정차량 35대 보존 실패");
        }

        private static void TestConstrainedApartmentSeedReproducibility()
        {
            AsciiMapV2 first = V2MapCatalog.ConstrainedApartmentVariant(7);
            AsciiMapV2 repeated = V2MapCatalog.ConstrainedApartmentVariant(7);
            AsciiMapV2 different = V2MapCatalog.ConstrainedApartmentVariant(8);
            Assert(first.RowsTopDown.SequenceEqual(repeated.RowsTopDown),
                "동일 시드가 같은 ASCII 배치를 재현하지 못함");
            Assert(!first.RowsTopDown.SequenceEqual(different.RowsTopDown),
                "다른 시드가 동일한 ASCII 배치를 생성함");

            EmergencyProblemV2 problem = first.Build();
            Assert(problem.Width == 20 && problem.Height == 11,
                "시드 맵 크기 20×11 불일치");
            Assert(problem.FixedVehiclePoses.Count == 35 &&
                   problem.VehicleCount == 2 && problem.StagingCapacity == 2,
                "시드 맵의 고정차량·이동차량·적치면 수 불일치");
        }

        private static void TestStaticReachabilityPruningRegression()
        {
            EmergencyProblemV2 map = V2MapCatalog.ConstrainedApartmentVariant(2).Build();
            var scenario = new EmergencyScenarioV2(
                "static-pruning-regression", (19, 5), map.CopyClearanceCells());
            EmergencyScenarioBuildResultV2 built = scenario.Build(map);
            Assert(built.Success, built.FailReason);

            PipelinedPlanResultV2 result = PipelinedPrioritizedPlannerV2.Solve(built.Problem);
            Assert(result.Success && result.PhysicallyValid, result.FailReason);
            Assert(result.Ticks == 39, "정적 가지치기 후 시드2 makespan 39틱 불일치");
            Assert(result.ExpandedStates < 1000,
                "정적 불가능 조합이 시간축 탐색으로 다시 누출됨: " + result.ExpandedStates);
        }

        private static void TestPipelinedRobotCountGeneralization()
        {
            EmergencyProblemV2 problem = V2ProblemFactory.LineProblem(
                vehicleCount: 4, robotStationCount: 4);
            foreach (int robots in new[] { 1, 2, 4 })
            {
                PipelinedPlanResultV2 result = PipelinedPrioritizedPlannerV2.Solve(
                    problem, activeRobotCount: robots);
                Assert(result.Success && result.PhysicallyValid,
                    $"운송 유닛 {robots}조 계획 실패: {result.FailReason}");
                Assert(result.RobotTimelines.Length == robots,
                    $"활성 {robots}조와 타임라인 수 불일치");
                Assert(result.Missions.Count == 4 &&
                       result.Missions.All(mission => mission.RobotIndex < robots),
                    $"운송 유닛 {robots}조 임무 배정 불일치");
            }
        }

        private static void TestPipelinedEightRobots()
        {
            EmergencyProblemV2 problem = V2ProblemFactory.LineProblem(
                vehicleCount: 8, robotStationCount: 8);
            PipelinedPlanResultV2 result = PipelinedPrioritizedPlannerV2.Solve(
                problem, activeRobotCount: 8);
            Assert(result.Success && result.PhysicallyValid, result.FailReason);
            Assert(result.Ticks == 22 && result.RobotTimelines.Length == 8,
                "운송 유닛8조 직선 게이트 22틱 불일치");
            Assert(Enumerable.Range(0, 8).All(robot =>
                    result.Missions.Count(mission => mission.RobotIndex == robot) == 1),
                "차량8대가 운송 유닛8조에 하나씩 배정되지 않음");
        }

        private static void TestApartmentSerialAisle()
        {
            EmergencyProblemV2 map = V2MapCatalog.ApartmentSerialAisle.Build();
            var scenario = new EmergencyScenarioV2(
                "serial-aisle-regression", (27, 7), map.CopyClearanceCells());
            EmergencyScenarioBuildResultV2 built = scenario.Build(map);
            Assert(built.Success && built.SelectedVehicleCount == 8, built.FailReason);
            Assert(map.FixedVehiclePoses.Count == 22 && map.StagingCapacity == 8 &&
                   map.RobotStarts.Count == 8,
                "다차량 아파트형 고정차·적치면·대기소 수 불일치");
            PipelinedPlanResultV2 result = PipelinedPrioritizedPlannerV2.Solve(
                built.Problem, activeRobotCount: 8);
            Assert(result.Success && result.PhysicallyValid && result.Ticks == 46,
                "다차량 아파트형 8조 기준 46틱 불일치: " + result.FailReason);
        }

        private static void TestCorridorScenarioFactory()
        {
            EmergencyScenarioBuildResultV2 oneLane =
                CorridorScenarioFactoryV2.BuildEmergency(1, 20);
            EmergencyScenarioBuildResultV2 threeLanes =
                CorridorScenarioFactoryV2.BuildEmergency(3, 40);
            Assert(oneLane.Success && oneLane.SelectedVehicleCount == 6,
                "1레인·d20 선택 차량 수 6대 불일치");
            Assert(threeLanes.Success && threeLanes.SelectedVehicleCount == 30,
                "3레인·d40 선택 차량 수 30대 불일치");
            Assert(oneLane.Problem.StagingCapacity == 60 &&
                   oneLane.Problem.RobotStarts.Count == 8,
                "운영 통로 유한 적치60면·대기소8칸 불일치");
            PipelinedPlanResultV2 result = PipelinedPrioritizedPlannerV2.Solve(
                oneLane.Problem,
                activeRobotCount: 4,
                maxHighLevelCandidates: 8);
            Assert(result.Success && result.PhysicallyValid && result.Ticks == 43,
                "운영 통로 1레인·d20 기준 43틱 불일치: " + result.FailReason);
        }

        private static void TestPocketLayoutRobustness()
        {
            for (int offset = 0; offset < 20; offset++)
            {
                EmergencyScenarioBuildResultV2 built =
                    CorridorScenarioFactoryV2.BuildEmergencyWithPockets(
                        100, 14, pocketOffset: offset);
                Assert(built.Success && built.SelectedVehicleCount == 20,
                    $"포켓14 오프셋{offset} 시나리오 생성 실패: {built.FailReason}");
                Assert(built.Problem.StagingCapacity == 20,
                    $"포켓14 오프셋{offset} 총 적치면20 불일치");
                PipelinedPlanResultV2 result = PipelinedPrioritizedPlannerV2.Solve(
                    built.Problem,
                    activeRobotCount: 4,
                    maxHighLevelCandidates: 8);
                Assert(result.Success && result.PhysicallyValid && result.Ticks <= 168,
                    $"포켓14 오프셋{offset} 7분 실패: {result.Ticks}틱 {result.FailReason}");
            }
        }

        private static void TestEmergencyAccessRouteSelection()
        {
            EmergencyProblemV2 problem = EmergencyAccessTestProblem();
            EmergencyAccessRouteV2 lower = new EmergencyAccessRouteV2(
                "lower-one-car", (1, 4), (12, 4), LowerAccessCells());
            EmergencyAccessRouteV2 upper = new EmergencyAccessRouteV2(
                "upper-two-cars", (1, 4), (12, 4), UpperAccessCells());
            EmergencyAccessPlanResultV2 selected = EmergencyAccessPlannerV2.Solve(
                problem, new[] { lower, upper }, activeRobotCount: 2);
            Assert(selected.Success, selected.FailReason);
            EmergencyAccessCandidateResultV2 lowerResult = selected.Candidates
                .Single(candidate => candidate.Route.Name == lower.Name);
            EmergencyAccessCandidateResultV2 upperResult = selected.Candidates
                .Single(candidate => candidate.Route.Name == upper.Name);
            Assert(lowerResult.Success && upperResult.Success,
                "두 접근경로가 모두 물리 계획 가능해야 함: lower=" +
                CandidateFailure(lowerResult) + ", upper=" + CandidateFailure(upperResult));
            int expectedTicks = Math.Min(lowerResult.Plan.Ticks, upperResult.Plan.Ticks);
            Assert(selected.Selected.Plan.Ticks == expectedTicks,
                "선택기가 최소 개통시간이 아닌 경로를 선택함");

            var fullCells = LowerAccessCells().Concat(UpperAccessCells()).Distinct().ToArray();
            EmergencyScenarioBuildResultV2 full = new EmergencyScenarioV2(
                "full-clearance-baseline", (12, 4), fullCells).Build(problem);
            Assert(full.Success && full.SelectedVehicleCount == 3, full.FailReason);
            PipelinedPlanResultV2 fullPlan = PipelinedPrioritizedPlannerV2.Solve(
                full.Problem, activeRobotCount: 2, maxHighLevelCandidates: 8);
            Assert(fullPlan.Success && fullPlan.PhysicallyValid, fullPlan.FailReason);
            Assert(selected.Selected.Plan.Ticks * 10 <= fullPlan.Ticks * 7,
                $"핵심경로 시간 단축이 30% 미만: {selected.Selected.Plan.Ticks}/{fullPlan.Ticks}틱");
            Console.WriteLine(
                $"   하부={lowerResult.Plan.Ticks}틱/1대, 상부={upperResult.Plan.Ticks}틱/2대, " +
                $"선택={selected.Selected.Route.Name}, 전면={fullPlan.Ticks}틱/3대");
        }

        private static void TestSurfaceApartmentAccessSelection()
        {
            SurfaceApartmentScenarioV2 scenario = SurfaceApartmentScenarioFactoryV2.Build();
            Assert(scenario.BaseProblem.FixedVehiclePoses.Count == 5 &&
                   scenario.BaseProblem.VehicleCount == 5 &&
                   scenario.BaseProblem.StagingCapacity == 5,
                "지상 아파트형 차량·적치 수 불일치");
            Assert(!scenario.BaseProblem.Slots.Any(slot =>
                    slot.Kind == SlotKind.Blocking && slot.Pose.X >= 19),
                "화재동 법정 전용구역 연결부에 차량이 배치됨");
            EmergencyAccessPlanResultV2 selected = EmergencyAccessPlannerV2.Solve(
                scenario.BaseProblem, scenario.Routes, activeRobotCount: 4);
            Assert(selected.Success, selected.FailReason);
            Assert(selected.Candidates.All(candidate => candidate.Success),
                "지상 아파트형 접근 후보 중 물리 계획 실패");
            EmergencyAccessCandidateResultV2 lower = selected.Candidates
                .Single(candidate => candidate.Route.Name == "lower-direct");
            EmergencyAccessCandidateResultV2 upper = selected.Candidates
                .Single(candidate => candidate.Route.Name == "upper-detour");
            Assert(lower.Scenario.SelectedVehicleCount == 3 &&
                   upper.Scenario.SelectedVehicleCount == 2,
                "지상 아파트형 후보별 방해차 수 불일치");

            EmergencyScenarioBuildResultV2 full = new EmergencyScenarioV2(
                "surface-full-baseline", (22, 5), scenario.FullClearanceCells)
                .Build(scenario.BaseProblem);
            Assert(full.Success && full.SelectedVehicleCount == 5, full.FailReason);
            PipelinedPlanResultV2 fullPlan = PipelinedPrioritizedPlannerV2.Solve(
                full.Problem, activeRobotCount: 4, maxHighLevelCandidates: 8);
            Assert(fullPlan.Success && fullPlan.PhysicallyValid, fullPlan.FailReason);
            Assert(selected.Selected.Plan.Ticks * 10 <= fullPlan.Ticks * 7,
                $"지상 핵심경로 단축이 30% 미만: {selected.Selected.Plan.Ticks}/{fullPlan.Ticks}틱");
            Console.WriteLine(
                $"   하부={lower.Plan.Ticks}틱/3대, 상부={upper.Plan.Ticks}틱/2대, " +
                $"선택={selected.Selected.Route.Name}, 전면={fullPlan.Ticks}틱/5대");
        }

        private static void TestAutomaticSurfaceAccessSelection()
        {
            SurfaceApartmentScenarioV2 scenario = SurfaceApartmentScenarioFactoryV2.Build();
            AutomaticEmergencyAccessPlanResultV2 automatic =
                EmergencyAccessRouteGeneratorV2.Solve(
                    scenario.BaseProblem, (1, 5), (22, 5), activeRobotCount: 4);
            Assert(automatic.Success, automatic.FailReason);
            Assert(automatic.Generation.Routes.Count >= 2,
                "지상형 맵에서 서로 다른 자동 후보 2개 이상을 생성하지 못함");
            EmergencyAccessCandidateResultV2 lower = automatic.Plan.Candidates
                .SingleOrDefault(candidate =>
                    candidate.Success &&
                    candidate.Scenario.SelectedVehicleCount == 3 &&
                    candidate.Plan.Ticks == 36);
            EmergencyAccessCandidateResultV2 upper = automatic.Plan.Candidates
                .SingleOrDefault(candidate =>
                    candidate.Success &&
                    candidate.Scenario.SelectedVehicleCount == 2 &&
                    candidate.Plan.Ticks == 39);
            Assert(lower != null, "자동 후보가 수동 하부 기준 3대/36틱을 재현하지 못함");
            Assert(upper != null, "자동 후보가 수동 상부 기준 2대/39틱을 재현하지 못함");
            Assert(automatic.Plan.Selected == lower,
                "자동 선택기가 차량이 적은 상부보다 빠른 하부를 선택하지 않음");

            EmergencyAccessPlanResultV2 manual = EmergencyAccessPlannerV2.Solve(
                scenario.BaseProblem, scenario.Routes, activeRobotCount: 4);
            Assert(manual.Success && manual.Selected.Plan.Ticks == 36,
                "기존 수동 후보 36틱 기준이 깨짐");
            Assert(automatic.Plan.Selected.Plan.Ticks == manual.Selected.Plan.Ticks,
                "자동 최선과 기존 수동 최선의 개통시간이 다름");
            Console.WriteLine(
                $"   자동후보={automatic.Generation.Routes.Count}, " +
                $"하부={lower.Plan.Ticks}틱/3대, 상부={upper.Plan.Ticks}틱/2대, " +
                $"수동격차={automatic.Plan.Selected.Plan.Ticks - manual.Selected.Plan.Ticks}틱");
        }

        private static void TestAutomaticNoCenterline()
        {
            var floor = new bool[9, 5];
            FillFloor(floor, 0, 2, 1, 3);
            FillFloor(floor, 6, 8, 1, 3);
            EmergencyProblemV2 problem = EmptyAccessProblem(
                floor, new[] { (0, 1) });
            EmergencyAccessRouteGenerationResultV2 result =
                EmergencyAccessRouteGeneratorV2.Generate(
                    problem, (1, 2), (7, 2));
            Assert(!result.Success &&
                   result.Failure == EmergencyAccessFailureV2.NoCenterline,
                "단절 맵을 중심선 없음으로 반환하지 않음: " + result.FailReason);
        }

        private static void TestAutomaticInsufficientWidth()
        {
            var floor = new bool[7, 3];
            for (int x = 0; x < 7; x++) floor[x, 1] = true;
            EmergencyProblemV2 problem = EmptyAccessProblem(
                floor, new[] { (0, 1) });
            var options = new EmergencyAccessRouteGenerationOptionsV2
            {
                MaxCenterlineAttempts = 4,
            };
            EmergencyAccessRouteGenerationResultV2 result =
                EmergencyAccessRouteGeneratorV2.Generate(
                    problem, (0, 1), (6, 1), options);
            Assert(!result.Success &&
                   result.Failure == EmergencyAccessFailureV2.InsufficientWidth,
                "1셀 통로를 폭3 부족으로 반환하지 않음: " + result.FailReason);
            Assert(result.CenterlinesFound > 0 && result.WidthRejected > 0,
                "중심선 존재와 폭 거부 진단값이 기록되지 않음");
        }

        private static void TestAutomaticFixedObstruction()
        {
            bool[,] floor = EmergencyProblemV2.FullFloor(7, 3);
            EmergencyProblemV2 problem = EmptyAccessProblem(
                floor,
                new[] { (0, 1) },
                new[] { new VehiclePose(3, 0, VehicleOrientation.Vertical) });
            var options = new EmergencyAccessRouteGenerationOptionsV2
            {
                MaxCenterlineAttempts = 8,
            };
            EmergencyAccessRouteGenerationResultV2 result =
                EmergencyAccessRouteGeneratorV2.Generate(
                    problem, (0, 1), (6, 1), options);
            Assert(!result.Success &&
                   result.Failure == EmergencyAccessFailureV2.FixedObstruction,
                "고정 차량 전면 차단을 구조 실패로 반환하지 않음: " + result.FailReason);
            Assert(result.FixedObstructionRejected > 0,
                "고정 차량으로 거부된 후보 수가 기록되지 않음");
        }

        private static void TestAutomaticInsufficientCapacity()
        {
            SurfaceApartmentScenarioV2 source = SurfaceApartmentScenarioFactoryV2.Build();
            List<ParkingSlotV2> slots = source.BaseProblem.Slots
                .Where(slot => slot.Kind == SlotKind.Blocking)
                .Concat(source.BaseProblem.Slots
                    .Where(slot => slot.Kind == SlotKind.Staging)
                    .Take(1))
                .ToList();
            EmergencyProblemV2 problem = new EmergencyProblemV2(
                source.BaseProblem.Width,
                source.BaseProblem.Height,
                source.BaseProblem.CopyFloor(),
                slots,
                Enumerable.Range(0, 5),
                source.BaseProblem.RobotStarts,
                Array.Empty<(int X, int Y)>(),
                source.BaseProblem.FixedVehiclePoses,
                source.BaseProblem.Timing,
                source.BaseProblem.FireCell);
            AutomaticEmergencyAccessPlanResultV2 result =
                EmergencyAccessRouteGeneratorV2.Solve(
                    problem, (1, 5), (22, 5), activeRobotCount: 4);
            Assert(!result.Success &&
                   result.Failure == EmergencyAccessFailureV2.InsufficientStagingCapacity,
                "적치1면으로 모든 후보가 막힌 상황을 용량 부족으로 반환하지 않음: " +
                result.FailReason);
        }

        private static void TestAutomaticPhysicalPlanningFailure()
        {
            SurfaceApartmentScenarioV2 scenario = SurfaceApartmentScenarioFactoryV2.Build();
            AutomaticEmergencyAccessPlanResultV2 result =
                EmergencyAccessRouteGeneratorV2.Solve(
                    scenario.BaseProblem,
                    (1, 5),
                    (22, 5),
                    activeRobotCount: 4,
                    maxTick: 1);
            Assert(!result.Success &&
                   result.Failure == EmergencyAccessFailureV2.PhysicalPlanningFailed,
                "물리 계획 실패를 명시적 결과로 반환하지 않음: " + result.FailReason);
            Assert(result.Plan != null && result.Plan.Candidates.All(candidate =>
                    candidate.Plan != null && !candidate.Plan.Success),
                "후보별 물리 계획 실패가 보존되지 않음");
        }

        private static void TestAutomaticSearchLimit()
        {
            SurfaceApartmentScenarioV2 scenario = SurfaceApartmentScenarioFactoryV2.Build();
            var options = new EmergencyAccessRouteGenerationOptionsV2
            {
                MaxSearchExpansions = 1,
            };
            EmergencyAccessRouteGenerationResultV2 result =
                EmergencyAccessRouteGeneratorV2.Generate(
                    scenario.BaseProblem, (1, 5), (22, 5), options);
            Assert(!result.Success &&
                   result.Failure == EmergencyAccessFailureV2.SearchLimitReached,
                "탐색 상한 도달을 명시적 결과로 반환하지 않음: " + result.FailReason);
            Assert(result.SearchLimitReached && result.SearchExpansions == 1,
                "탐색 상한 진단값 불일치");
        }

        private static void TestAutomaticAlreadyClear()
        {
            EmergencyProblemV2 problem = EmptyAccessProblem(
                EmergencyProblemV2.FullFloor(8, 5),
                new[] { (0, 0), (1, 0) });
            AutomaticEmergencyAccessPlanResultV2 result =
                EmergencyAccessRouteGeneratorV2.Solve(
                    problem, (1, 2), (6, 2), activeRobotCount: 2);
            Assert(result.Success, result.FailReason);
            Assert(result.Plan.Selected.Scenario.SelectedVehicleCount == 0 &&
                   result.Plan.Selected.Plan.Ticks == 0,
                "이미 열린 경로를 0대·0틱으로 반환하지 않음");
        }

        private static void TestAutomaticDuplicateRemoval()
        {
            EmergencyProblemV2 problem = EmptyAccessProblem(
                EmergencyProblemV2.FullFloor(7, 3),
                new[] { (0, 1) });
            var options = new EmergencyAccessRouteGenerationOptionsV2
            {
                MaxCenterlineAttempts = 4,
                DiversificationPenalty = 0,
            };
            EmergencyAccessRouteGenerationResultV2 result =
                EmergencyAccessRouteGeneratorV2.Generate(
                    problem, (0, 1), (6, 1), options);
            Assert(result.Success, result.FailReason);
            Assert(result.Routes.Count == 1 && result.DuplicateRejected == 3,
                $"동일 중심선 중복 제거 불일치: 후보{result.Routes.Count}, " +
                $"중복{result.DuplicateRejected}");
        }

        private static void TestAutomaticReproducibility()
        {
            SurfaceApartmentScenarioV2 scenario = SurfaceApartmentScenarioFactoryV2.Build();
            AutomaticEmergencyAccessPlanResultV2 first =
                EmergencyAccessRouteGeneratorV2.Solve(
                    scenario.BaseProblem, (1, 5), (22, 5), activeRobotCount: 4);
            AutomaticEmergencyAccessPlanResultV2 repeated =
                EmergencyAccessRouteGeneratorV2.Solve(
                    scenario.BaseProblem, (1, 5), (22, 5), activeRobotCount: 4);
            Assert(first.Success && repeated.Success,
                first.FailReason ?? repeated.FailReason);
            Assert(first.Generation.Routes.Count == repeated.Generation.Routes.Count,
                "동일 입력의 자동 후보 수가 달라짐");
            for (int index = 0; index < first.Generation.Routes.Count; index++)
            {
                EmergencyAccessRouteV2 left = first.Generation.Routes[index];
                EmergencyAccessRouteV2 right = repeated.Generation.Routes[index];
                Assert(left.Name == right.Name &&
                       left.RequiredCells.SequenceEqual(right.RequiredCells),
                    $"동일 입력의 후보 {index} 이름·순서·셀 집합이 달라짐");
            }
            Assert(first.Plan.Selected.Route.Name == repeated.Plan.Selected.Route.Name &&
                   first.Plan.Selected.Plan.Ticks == repeated.Plan.Selected.Plan.Ticks &&
                   first.Plan.Selected.Scenario.SelectedVehicleCount ==
                   repeated.Plan.Selected.Scenario.SelectedVehicleCount,
                "동일 입력의 최종 자동 선택 결과가 달라짐");
        }

        private static EmergencyProblemV2 EmptyAccessProblem(
            bool[,] floor,
            IEnumerable<(int X, int Y)> robotStarts,
            IEnumerable<VehiclePose> fixedVehicles = null)
        {
            return new EmergencyProblemV2(
                floor.GetLength(0),
                floor.GetLength(1),
                floor,
                Array.Empty<ParkingSlotV2>(),
                Array.Empty<int>(),
                robotStarts,
                Array.Empty<(int X, int Y)>(),
                fixedVehicles ?? Array.Empty<VehiclePose>());
        }

        private static void FillFloor(
            bool[,] floor,
            int minX,
            int maxX,
            int minY,
            int maxY)
        {
            for (int x = minX; x <= maxX; x++)
                for (int y = minY; y <= maxY; y++)
                    floor[x, y] = true;
        }

        private static EmergencyProblemV2 EmergencyAccessTestProblem()
        {
            var slots = new List<ParkingSlotV2>
            {
                new ParkingSlotV2(0, SlotKind.Blocking,
                    new VehiclePose(6, 3, VehicleOrientation.Horizontal)),
                new ParkingSlotV2(1, SlotKind.Blocking,
                    new VehiclePose(5, 6, VehicleOrientation.Horizontal)),
                new ParkingSlotV2(2, SlotKind.Blocking,
                    new VehiclePose(9, 6, VehicleOrientation.Horizontal)),
                new ParkingSlotV2(3, SlotKind.Staging,
                    new VehiclePose(2, 0, VehicleOrientation.Horizontal)),
                new ParkingSlotV2(4, SlotKind.Staging,
                    new VehiclePose(5, 0, VehicleOrientation.Horizontal)),
                new ParkingSlotV2(5, SlotKind.Staging,
                    new VehiclePose(8, 0, VehicleOrientation.Horizontal)),
            };
            return new EmergencyProblemV2(
                14, 10, EmergencyProblemV2.FullFloor(14, 10),
                slots, new[] { 0, 1, 2 }, new[] { (0, 1), (1, 1) },
                Array.Empty<(int X, int Y)>());
        }

        private static string CandidateFailure(EmergencyAccessCandidateResultV2 candidate)
        {
            if (candidate.Scenario == null) return "scenario-null";
            if (!candidate.Scenario.Success) return candidate.Scenario.FailReason;
            if (candidate.Plan == null) return "plan-null";
            return candidate.Plan.Success
                ? candidate.Plan.Ticks + " ticks"
                : candidate.Plan.FailReason;
        }

        private static IEnumerable<(int X, int Y)> LowerAccessCells()
        {
            for (int x = 1; x <= 12; x++)
                for (int y = 2; y <= 4; y++) yield return (x, y);
        }

        private static IEnumerable<(int X, int Y)> UpperAccessCells()
        {
            for (int x = 1; x <= 12; x++)
                for (int y = 5; y <= 7; y++) yield return (x, y);
            for (int x = 1; x <= 3; x++)
                for (int y = 2; y <= 4; y++) yield return (x, y);
            for (int x = 11; x <= 12; x++)
                for (int y = 2; y <= 4; y++) yield return (x, y);
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
