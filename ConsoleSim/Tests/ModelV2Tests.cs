using System;
using System.Collections.Generic;
using System.Linq;
using ParkingSim.Core;
using ParkingSim.Core.V2;

namespace ParkingSim.Tests
{
    public static class ModelV2Tests
    {
        public const int ExpectedGateCount = 78;

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
            passed += Run("㊻ 적치 토지회계 — 실제사용과 상시전용 비용 분리", TestStagingLandAccounting);
            passed += Run("㊼ 포켓 토지회계 — 비주차6·주차전용14·net α6", TestPocketLandAccounting);
            passed += Run("㊽ 토지회계 방어 — 미확인·중복·외부슬롯·실패계획", TestStagingLandAccountingGuards);
            passed += Run("㊾ 현실시간 보정 — 공개사양 서비스틱·양자화", TestPublishedTimeCalibration);
            passed += Run("㊿ 현실시간 지상형 — 1·2·3m/s 자동경로 물리 재계획", TestCalibratedSurfaceAccess);
            passed += Run("51 현실시간 포켓14 — 서비스 하한으로 7분 실패", TestCalibratedPocketFailure);
            passed += Run("52 최종비교 — 동일맵 4정책·속도별 목적함수", TestFinalSurfacePolicyComparison);
            passed += Run("53 최종비교 회계 — 정책별 상시전용비용 동일", TestFinalSurfacePolicyAccounting);
            passed += Run("54 지상 밀도 팩토리 — 차량0~14·배치4·적치 분리", TestSurfaceDensityFactory);
            passed += Run("55 지상 밀도 평가 — 0틱 개방·현실시간 결정론", TestSurfaceDensityEvaluation);
            passed += Run("56 8동 단지 기하 — 건물·도로·전용구역 분리", TestApartmentComplexGeometry);
            passed += Run("57 8동 단지 화재 — 동별 전용구역 자동 개통", TestApartmentComplexAllBuildings);
            passed += Run("58 8동 단지 복수입구 — 최악시간 비악화·재현", TestApartmentComplexMultipleEntrances);
            passed += Run("59 8동 단지 실패 — 미등록 화재동 명시적 거부", TestApartmentComplexInvalidIncident);
            passed += Run("60 8동 밀도 팩토리 — 차량0~22·기하 보존", TestApartmentComplexDensityFactory);
            passed += Run("61 8동 후보 캐시 — 밀도 간 중심선 재사용", TestApartmentComplexRouteCatalogReuse);
            passed += Run("62 8동 하한 가지치기 — 선택 결과 동일", TestApartmentComplexPruningEquivalence);
            passed += Run("63 교란 검증 — 무효 봉쇄 거부·원본 불변", TestDisturbanceValidation);
            passed += Run("64 교란 무관 봉쇄 — 원거리 봉쇄에 결과 불변", TestDisturbanceIrrelevantBlockage);
            passed += Run("65 교란 경로 회피 — 선택 경로 봉쇄 시 우회 성공", TestDisturbanceReroute);
            passed += Run("66 교란 접근 불능 — 종점 협착을 실패 사유로 반환", TestDisturbanceInfeasible);
            passed += Run("67 교란 재현성 — 유닛 감소 포함 동일 결과", TestDisturbanceReproducibility);
            passed += Run("68 배터리 회계 — 소모·잔량 보존과 무발동", TestBatteryAccounting);
            passed += Run("69 핸드오버 추출 — 인도·잔여·적치 정합", TestHandoverStateExtraction);
            passed += Run("70 핸드오버 조립 — 잔여 계획 유효·시간 합성", TestHandoverComposition);
            passed += Run("71 핸드오버 교체 — 충전소 출발 유닛 합류", TestHandoverReplacement);
            passed += Run("72 핸드오버 재현성 — 동일 입력 동일 결과", TestHandoverReproducibility);
            passed += Run("76 핸드오버 정차 간섭 — 정차 유닛 셀 통행 불가", TestHandoverParkedInterference);
            passed += Run("77 단지A 간선 재지정 — 종축 전용구역·저이동 개통", TestSiteAArterialZone);
            passed += Run("78 병렬성 진단 — 로봇-틱 4분해 보존·재현", TestPlanUtilization);
            passed += Run("73 단지A 기하 — 만차 배경·전용구역·밀도 보존", TestSiteAGeometry);
            passed += Run("74 단지A 평가 — 배경 주차열 이동 필수·재현", TestSiteAEvaluation);
            passed += Run("75 단지A 적치 반사실 — 재배치·확장 기하와 재현", TestSiteAStagingCounterfactual);
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

        private static void TestStagingLandAccounting()
        {
            SurfaceApartmentScenarioV2 surface = SurfaceApartmentScenarioFactoryV2.Build();
            AutomaticEmergencyAccessPlanResultV2 automatic =
                EmergencyAccessRouteGeneratorV2.Solve(
                    surface.BaseProblem, (1, 5), (22, 5), activeRobotCount: 4);
            Assert(automatic.Success && automatic.Plan.Selected.Plan.Ticks == 36,
                automatic.FailReason);
            EmergencyProblemV2 problem = automatic.Plan.Selected.Scenario.Problem;
            PipelinedPlanResultV2 plan = automatic.Plan.Selected.Plan;

            ParkingSlotV2[] staging = problem.Slots
                .Where(slot => slot.Kind == SlotKind.Staging).ToArray();
            StagingLandAccountingResultV2 allParking =
                CapacityTradeoffV2.EvaluateStagingLand(
                    problem,
                    plan,
                    grossAdditionalCars: 5,
                    staging.Select(slot => new StagingLandProfileV2(
                        slot.Id, StagingLandKindV2.ConvertedParkingSpace)));
            StagingLandAccountingResultV2 allNonParking =
                CapacityTradeoffV2.EvaluateStagingLand(
                    problem,
                    plan,
                    grossAdditionalCars: 5,
                    staging.Select(slot => new StagingLandProfileV2(
                        slot.Id, StagingLandKindV2.ExistingNonParkingPaved)));
            StagingLandAccountingResultV2 mixed =
                CapacityTradeoffV2.EvaluateStagingLand(
                    problem,
                    plan,
                    grossAdditionalCars: 5,
                    staging.Select((slot, index) => new StagingLandProfileV2(
                        slot.Id,
                        index < 2
                            ? StagingLandKindV2.ConvertedParkingSpace
                            : StagingLandKindV2.ExistingNonParkingPaved)));

            Assert(allParking.NetAlphaClaimable &&
                   allParking.RequiredStagingSlots == 3 &&
                   allParking.UsedStagingSlots == 3 &&
                   allParking.DedicatedStagingSlots == 5 &&
                   allParking.UnusedDedicatedStagingSlots == 2,
                "지상형 실제사용3면·상시전용5면 분리가 잘못됨");
            Assert(allParking.ParkingOpportunityCostCars == 5 &&
                   allParking.VerifiedNetAlpha == 0,
                "전 적치면이 주차 가능 부지일 때 net α0이 아님");
            Assert(allNonParking.VerifiedNetAlpha == 5,
                "전 적치면이 기존 비주차 포장일 때 net α5가 아님");
            Assert(mixed.ConvertedParkingSlots == 2 &&
                   mixed.ExistingNonParkingPavedSlots == 3 &&
                   mixed.VerifiedNetAlpha == 3,
                "혼합 토지 2주차+3비주차 회계 오류");
        }

        private static void TestPocketLandAccounting()
        {
            EmergencyScenarioBuildResultV2 built =
                CorridorScenarioFactoryV2.BuildEmergencyWithPockets(
                    100, 14, pocketOffset: 14);
            Assert(built.Success && built.SelectedVehicleCount == 20, built.FailReason);
            PipelinedPlanResultV2 plan = PipelinedPrioritizedPlannerV2.Solve(
                built.Problem,
                activeRobotCount: 4,
                maxHighLevelCandidates: 8);
            Assert(plan.Success && plan.PhysicallyValid && plan.Ticks == 160,
                plan.FailReason);

            IEnumerable<StagingLandProfileV2> profiles = built.Problem.Slots
                .Where(slot => slot.Kind == SlotKind.Staging)
                .Select(slot => new StagingLandProfileV2(
                    slot.Id,
                    slot.Pose.Y == CorridorScenarioFactoryV2.CorridorBottomY + 3
                        ? StagingLandKindV2.ConvertedParkingSpace
                        : StagingLandKindV2.ExistingNonParkingPaved));
            StagingLandAccountingResultV2 accounting =
                CapacityTradeoffV2.EvaluateStagingLand(
                    built.Problem, plan, grossAdditionalCars: 20, profiles);
            Assert(accounting.NetAlphaClaimable, accounting.FailReason);
            Assert(accounting.DedicatedStagingSlots == 20 &&
                   accounting.UsedStagingSlots == 20 &&
                   accounting.ExistingNonParkingPavedSlots == 6 &&
                   accounting.ConvertedParkingSlots == 14 &&
                   accounting.ParkingOpportunityCostCars == 14 &&
                   accounting.VerifiedNetAlpha == 6,
                "포켓14 토지 회계가 6비주차+14주차전용=net6을 재현하지 못함");
        }

        private static void TestStagingLandAccountingGuards()
        {
            SurfaceApartmentScenarioV2 surface = SurfaceApartmentScenarioFactoryV2.Build();
            AutomaticEmergencyAccessPlanResultV2 automatic =
                EmergencyAccessRouteGeneratorV2.Solve(
                    surface.BaseProblem, (1, 5), (22, 5), activeRobotCount: 4);
            Assert(automatic.Success, automatic.FailReason);
            EmergencyProblemV2 problem = automatic.Plan.Selected.Scenario.Problem;
            PipelinedPlanResultV2 plan = automatic.Plan.Selected.Plan;
            ParkingSlotV2[] staging = problem.Slots
                .Where(slot => slot.Kind == SlotKind.Staging).ToArray();
            StagingLandProfileV2[] complete = staging
                .Select(slot => new StagingLandProfileV2(
                    slot.Id, StagingLandKindV2.ExistingNonParkingPaved))
                .ToArray();

            StagingLandAccountingResultV2 missing =
                CapacityTradeoffV2.EvaluateStagingLand(
                    problem, plan, 5, complete.Take(4));
            Assert(!missing.NetAlphaClaimable &&
                   missing.UnverifiedSlots == 1 &&
                   !missing.VerifiedNetAlpha.HasValue &&
                   missing.FailReason.Contains("미확인"),
                "미분류 적치면을 순이득 주장 불가로 반환하지 않음");

            bool duplicateRejected = ThrowsArgument(() =>
                CapacityTradeoffV2.EvaluateStagingLand(
                    problem, plan, 5, complete.Concat(new[] { complete[0] })));
            bool foreignRejected = ThrowsArgument(() =>
                CapacityTradeoffV2.EvaluateStagingLand(
                    problem,
                    plan,
                    5,
                    complete.Concat(new[]
                    {
                        new StagingLandProfileV2(
                            999999, StagingLandKindV2.ExistingNonParkingPaved),
                    })));
            Assert(duplicateRejected && foreignRejected,
                "중복 또는 레이아웃 외부 적치 슬롯 분류를 허용함");

            var failedPlan = new PipelinedPlanResultV2(4)
            {
                FailReason = "테스트 물리 계획 실패",
            };
            StagingLandAccountingResultV2 failed =
                CapacityTradeoffV2.EvaluateStagingLand(
                    problem, failedPlan, 5, complete);
            Assert(!failed.NetAlphaClaimable &&
                   failed.FailReason.Contains("물리 계획 실패") &&
                   !failed.VerifiedNetAlpha.HasValue,
                "실패 계획에서 순이득을 주장함");
        }

        private static void TestPublishedTimeCalibration()
        {
            PhysicalTimeProfileV2 one = PublishedParkingRobotTimingV2.Create(1.0);
            PhysicalTimeProfileV2 two = PublishedParkingRobotTimingV2.Create(2.0);
            PhysicalTimeProfileV2 three = PublishedParkingRobotTimingV2.Create(3.0);
            Assert(one.PickupServiceTicks == 36 && one.ReleaseServiceTicks == 24,
                "1m/s 공개 서비스틱 36/24 불일치");
            Assert(two.PickupServiceTicks == 72 && two.ReleaseServiceTicks == 48,
                "2m/s 공개 서비스틱 72/48 불일치");
            Assert(three.PickupServiceTicks == 108 && three.ReleaseServiceTicks == 72,
                "3m/s 공개 서비스틱 108/72 불일치");
            foreach (PhysicalTimeProfileV2 profile in new[] { one, two, three })
            {
                Assert(profile.QuantizedPickupSeconds >= profile.PickupServiceSeconds &&
                       profile.QuantizedPickupSeconds - profile.PickupServiceSeconds <
                       profile.MotionTickSeconds + 1e-9,
                    profile.Name + " pickup 양자화 오차가 이동틱1개 이상");
                Assert(profile.QuantizedReleaseSeconds >= profile.ReleaseServiceSeconds &&
                       profile.QuantizedReleaseSeconds - profile.ReleaseServiceSeconds <
                       profile.MotionTickSeconds + 1e-9,
                    profile.Name + " release 양자화 오차가 이동틱1개 이상");
                Assert(Math.Abs(
                           profile.ServiceOnlyLowerBoundSeconds(20, 4) - 750.0) < 1e-9,
                    profile.Name + " 차량20/4조 서비스 하한 750초 불일치");
            }
            Assert(ThrowsArgument(() => PublishedParkingRobotTimingV2.Create(3.1)),
                "공개 최대속도3m/s 초과를 허용함");
        }

        private static void TestCalibratedSurfaceAccess()
        {
            foreach (double speed in new[] { 1.0, 2.0, 3.0 })
            {
                PhysicalTimeProfileV2 profile =
                    PublishedParkingRobotTimingV2.Create(speed);
                SurfaceApartmentScenarioV2 surface =
                    SurfaceApartmentScenarioFactoryV2.Build(
                        profile.CreateOperationTiming());
                AutomaticEmergencyAccessPlanResultV2 result =
                    EmergencyAccessRouteGeneratorV2.Solve(
                        surface.BaseProblem,
                        (1, 5),
                        (22, 5),
                        activeRobotCount: 4,
                        maxTick: 5000);
                Assert(result.Success, profile.Name + ": " + result.FailReason);
                Assert(result.Plan.Selected.Scenario.SelectedVehicleCount == 3,
                    profile.Name + "에서 하부3대 경로 선택이 바뀜");
                EmergencyAccessCandidateResultV2 upper = result.Plan.Candidates
                    .Single(candidate =>
                        candidate.Success &&
                        candidate.Scenario.SelectedVehicleCount == 2);
                Assert(result.Plan.Selected.Plan.Ticks < upper.Plan.Ticks,
                    profile.Name + "에서 하부가 상부보다 느려짐");
                Console.WriteLine(
                    $"   {speed:F0}m/s: 하부 " +
                    $"{result.Plan.Selected.Plan.Ticks}틱/" +
                    $"{profile.PlanSeconds(result.Plan.Selected.Plan.Ticks):F1}초, " +
                    $"상부 {upper.Plan.Ticks}틱/" +
                    $"{profile.PlanSeconds(upper.Plan.Ticks):F1}초");
            }
        }

        private static void TestCalibratedPocketFailure()
        {
            foreach (double speed in new[] { 1.0, 2.0, 3.0 })
            {
                PhysicalTimeProfileV2 profile =
                    PublishedParkingRobotTimingV2.Create(speed);
                EmergencyScenarioBuildResultV2 built =
                    CorridorScenarioFactoryV2.BuildEmergencyWithPockets(
                        100,
                        14,
                        timing: profile.CreateOperationTiming(),
                        pocketOffset: 14);
                Assert(built.Success && built.SelectedVehicleCount == 20,
                    profile.Name + ": " + built.FailReason);
                PipelinedPlanResultV2 plan = PipelinedPrioritizedPlannerV2.Solve(
                    built.Problem,
                    activeRobotCount: 4,
                    maxHighLevelCandidates: 8,
                    maxTick: 5000);
                Assert(plan.Success && plan.PhysicallyValid,
                    profile.Name + ": " + plan.FailReason);
                double seconds = profile.PlanSeconds(plan.Ticks);
                double lowerBound = profile.ServiceOnlyLowerBoundSeconds(20, 4);
                Assert(seconds >= lowerBound && seconds > TimeBudget.BaselineSeconds,
                    profile.Name + " 현실시간이 서비스 하한 또는 7분보다 짧음");
                Console.WriteLine(
                    $"   {speed:F0}m/s: {plan.Ticks}틱/{seconds:F1}초, " +
                    $"서비스하한={lowerBound:F1}초, 7분 실패");
            }
        }

        private static void TestFinalSurfacePolicyComparison()
        {
            foreach (double speed in new[] { 1.0, 2.0, 3.0 })
            {
                SurfacePolicyComparisonResultV2 comparison =
                    SurfacePolicyComparisonV2.Run(
                        PublishedParkingRobotTimingV2.Create(speed));
                Assert(comparison.Success, comparison.FailReason);
                Assert(comparison.Policies.Count == 4,
                    speed + "m/s 최종 정책4종 누락");
                SurfacePolicyMeasurementV2 always = comparison.Policies
                    .Single(row => row.Policy == SurfaceEmergencyPolicyV2.AlwaysClear);
                SurfacePolicyMeasurementV2 full = comparison.Policies
                    .Single(row => row.Policy == SurfaceEmergencyPolicyV2.FullClearance);
                SurfacePolicyMeasurementV2 minimum = comparison.Policies
                    .Single(row => row.Policy ==
                                   SurfaceEmergencyPolicyV2.MinimumBlockingVehicles);
                SurfacePolicyMeasurementV2 fastest = comparison.Policies
                    .Single(row => row.Policy ==
                                   SurfaceEmergencyPolicyV2.FastestPhysicalOpening);
                Assert(always.GrossAdditionalCars == 0 &&
                       always.MovedVehicles == 0 &&
                       always.Ticks == 0,
                    "상시개방 기준이 α0·0대·0틱이 아님");
                Assert(full.GrossAdditionalCars == 5 && full.MovedVehicles == 5,
                    speed + "m/s 전면 정책 gross5·이동5 불일치");
                Assert(minimum.GrossAdditionalCars == 5 && minimum.MovedVehicles == 2,
                    speed + "m/s 최소차량 정책 gross5·이동2 불일치");
                Assert(fastest.GrossAdditionalCars == 5 && fastest.MovedVehicles == 3,
                    speed + "m/s 최소개통 정책 gross5·이동3 불일치");
                Assert(fastest.Seconds < minimum.Seconds &&
                       minimum.Seconds < full.Seconds,
                    speed + "m/s 정책 시간 순서가 하부<상부<전면이 아님");
                Assert(fastest.ReductionVsFullClearance >= 0.4,
                    speed + "m/s 자동 하부의 전면 대비 단축이 40% 미만");
                Console.WriteLine(
                    $"   {speed:F0}m/s: 전면={full.Seconds:F1}초/" +
                    $"{(full.WithinSevenMinutes ? "통과" : "실패")}, " +
                    $"상부={minimum.Seconds:F1}초, 하부={fastest.Seconds:F1}초, " +
                    $"단축={fastest.ReductionVsFullClearance:P1}");
            }
        }

        private static void TestFinalSurfacePolicyAccounting()
        {
            SurfacePolicyComparisonResultV2 comparison =
                SurfacePolicyComparisonV2.Run(
                    PublishedParkingRobotTimingV2.Create(1.0));
            Assert(comparison.Success, comparison.FailReason);
            foreach (SurfacePolicyMeasurementV2 row in comparison.Policies
                         .Where(row => row.Policy !=
                                       SurfaceEmergencyPolicyV2.AlwaysClear))
            {
                StagingLandAccountingResultV2 parking =
                    EvaluateSurfacePolicyLand(row, convertedCount: 5);
                StagingLandAccountingResultV2 mixed =
                    EvaluateSurfacePolicyLand(row, convertedCount: 2);
                StagingLandAccountingResultV2 nonParking =
                    EvaluateSurfacePolicyLand(row, convertedCount: 0);
                Assert(parking.NetAlphaClaimable &&
                       mixed.NetAlphaClaimable &&
                       nonParking.NetAlphaClaimable,
                    row.Policy + " 토지 회계 확정 실패");
                Assert(parking.DedicatedStagingSlots == 5 &&
                       mixed.DedicatedStagingSlots == 5 &&
                       nonParking.DedicatedStagingSlots == 5,
                    row.Policy + " 정책별 상시 전용면이 달라짐");
                Assert(parking.UsedStagingSlots == row.MovedVehicles &&
                       mixed.UsedStagingSlots == row.MovedVehicles,
                    row.Policy + " 사건 사용면과 이동차량 불일치");
                Assert(parking.VerifiedNetAlpha == 0 &&
                       mixed.VerifiedNetAlpha == 3 &&
                       nonParking.VerifiedNetAlpha == 5,
                    row.Policy + " 토지 민감도 net0/3/5 불일치");
            }
        }

        private static StagingLandAccountingResultV2 EvaluateSurfacePolicyLand(
            SurfacePolicyMeasurementV2 row,
            int convertedCount)
        {
            ParkingSlotV2[] staging = row.ScenarioProblem.Slots
                .Where(slot => slot.Kind == SlotKind.Staging).ToArray();
            return CapacityTradeoffV2.EvaluateStagingLand(
                row.ScenarioProblem,
                row.Plan,
                row.GrossAdditionalCars,
                staging.Select((slot, index) => new StagingLandProfileV2(
                    slot.Id,
                    index < convertedCount
                        ? StagingLandKindV2.ConvertedParkingSpace
                        : StagingLandKindV2.ExistingNonParkingPaved)));
        }

        private static bool ThrowsArgument(Action action)
        {
            try
            {
                action();
                return false;
            }
            catch (ArgumentException)
            {
                return true;
            }
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
                Assert(result.Success && result.PhysicallyValid &&
                       result.Ticks <= TimeBudget.BaselineTicks,
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

        private static void TestSurfaceDensityFactory()
        {
            foreach (SurfaceVehiclePlacementV2 placement in
                     Enum.GetValues(typeof(SurfaceVehiclePlacementV2)))
            {
                SurfaceApartmentScenarioV2 empty =
                    SurfaceApartmentScenarioFactoryV2.BuildDensity(
                        0, placement, 5);
                SurfaceApartmentScenarioV2 full =
                    SurfaceApartmentScenarioFactoryV2.BuildDensity(
                        SurfaceApartmentScenarioFactoryV2.MaximumBlockingVehicles,
                        placement,
                        SurfaceApartmentScenarioFactoryV2.MaximumDensityStagingCapacity);
                Assert(empty.BaseProblem.VehicleCount == 0 &&
                       empty.BaseProblem.StagingCapacity == 5,
                    "빈 밀도 조건의 차량·적치 수 불일치");
                Assert(full.BaseProblem.VehicleCount == 14 &&
                       full.BaseProblem.StagingCapacity == 14,
                    "최대 밀도 조건의 차량·적치 수 불일치");
                Assert(full.BlockingVehicleCount == 14 &&
                       full.DedicatedStagingCapacity == 14 &&
                       full.Placement == placement,
                    "밀도 시나리오 메타데이터 불일치");
            }

            SurfaceApartmentScenarioV2 alternating =
                SurfaceApartmentScenarioFactoryV2.BuildDensity(
                    4,
                    SurfaceVehiclePlacementV2.AlternatingEntranceFirst,
                    5);
            VehiclePose[] poses = alternating.BaseProblem.InitialVehicleSlots
                .Select(slot => alternating.BaseProblem.Slots[slot].Pose)
                .ToArray();
            Assert(poses.SequenceEqual(new[]
                {
                    new VehiclePose(5, 5, VehicleOrientation.Horizontal),
                    new VehiclePose(5, 9, VehicleOrientation.Horizontal),
                    new VehiclePose(7, 5, VehicleOrientation.Horizontal),
                    new VehiclePose(7, 9, VehicleOrientation.Horizontal),
                }),
                "입구부터 교대 배치의 결정론적 prefix 불일치");
        }

        private static void TestSurfaceDensityEvaluation()
        {
            PhysicalTimeProfileV2 profile =
                PublishedParkingRobotTimingV2.Create(1.0);
            SurfaceDensityTrialV2 empty = SurfaceDensitySweepV2.Evaluate(
                0,
                SurfaceVehiclePlacementV2.AlternatingEntranceFirst,
                5,
                (22, 5),
                profile);
            Assert(empty.PlanSuccess &&
                   empty.Outcome == SurfaceDensityOutcomeV2.WithinBudget &&
                   empty.MovedVehicleCount == 0 &&
                   empty.Ticks == 0 &&
                   Math.Abs(empty.Seconds) < 1e-9,
                "이미 열린 지상 밀도 조건을 0대·0틱 성공으로 평가하지 않음");

            SurfaceDensityTrialV2 first = SurfaceDensitySweepV2.Evaluate(
                4,
                SurfaceVehiclePlacementV2.AlternatingEntranceFirst,
                14,
                (22, 7),
                profile);
            SurfaceDensityTrialV2 repeated = SurfaceDensitySweepV2.Evaluate(
                4,
                SurfaceVehiclePlacementV2.AlternatingEntranceFirst,
                14,
                (22, 7),
                profile);
            Assert(first.PlanSuccess && repeated.PlanSuccess,
                first.FailReason ?? repeated.FailReason);
            Assert(first.Outcome == repeated.Outcome &&
                   first.SelectedRoute == repeated.SelectedRoute &&
                   first.MovedVehicleCount == repeated.MovedVehicleCount &&
                   first.Ticks == repeated.Ticks &&
                   Math.Abs(first.Seconds - repeated.Seconds) < 1e-9,
                "동일 지상 밀도 입력의 현실시간 결과가 재현되지 않음");
        }

        private static void TestApartmentComplexGeometry()
        {
            ApartmentComplexScenarioV2 scenario =
                ApartmentComplexScenarioFactoryV2.Build();
            Assert(scenario.Buildings.Count == 8 &&
                   scenario.Buildings.Select(building => building.Id)
                       .SequenceEqual(Enumerable.Range(101, 8)),
                "2행×4동 ID 구성이 101~108동이 아님");
            Assert(scenario.Entrances.Count == 2 &&
                   scenario.Entrances.Count(entrance => entrance.IsPrimary) == 1,
                "서측 주진입구·동측 보조진입구 구성이 아님");
            Assert(scenario.BaseProblem.VehicleCount == 22 &&
                   scenario.BaseProblem.StagingCapacity == 12,
                "단지 접근축 차량·적치 용량 불일치");

            var allZoneCells = new HashSet<(int X, int Y)>();
            foreach (ApartmentBuildingV2 building in scenario.Buildings)
            {
                Assert(building.FootprintCells.All(cell =>
                        !scenario.BaseProblem.IsFloor(cell.X, cell.Y)),
                    building.Id + "동 풋프린트가 주행 floor와 겹침");
                Assert(building.FireEngineZone.Cells.All(cell =>
                        scenario.BaseProblem.IsFloor(cell.X, cell.Y)),
                    building.Id + "동 전용구역이 주행 floor 밖임");
                Assert(building.FireEngineZone.Cells.All(allZoneCells.Add),
                    building.Id + "동 전용구역이 다른 동과 겹침");
                Assert(building.FireEngineZone.Cells.Contains(
                        building.FireEngineZone.ApproachCell),
                    building.Id + "동 전용구역에 접근 종점이 없음");
                foreach (int slotId in scenario.BaseProblem.InitialVehicleSlots)
                {
                    VehiclePose pose = scenario.BaseProblem.Slots[slotId].Pose;
                    Assert(!building.FireEngineZone.Cells.Contains((pose.X, pose.Y)) &&
                           !building.FireEngineZone.Cells.Contains(pose.SecondCell),
                        building.Id + "동 전용구역에 가변 차량이 배치됨");
                }
            }
        }

        private static void TestApartmentComplexAllBuildings()
        {
            ApartmentComplexScenarioV2 scenario =
                ApartmentComplexScenarioFactoryV2.Build();
            EmergencyAccessRouteGenerationOptionsV2 options = ComplexOptions();
            foreach (ApartmentBuildingV2 building in scenario.Buildings)
            {
                ApartmentComplexPlanResultV2 result =
                    ApartmentComplexEmergencyPlannerV2.Solve(
                        scenario,
                        new ApartmentFireIncidentV2(building.Id),
                        includeSecondaryEntrances: false,
                        activeRobotCount: 4,
                        generationOptions: options,
                        maxTick: 800);
                Assert(result.Success, building.Id + "동 개통 실패: " + result.FailReason);
                Assert(result.TargetZone.BuildingId == building.Id &&
                       result.Selected.Entrance.IsPrimary,
                    building.Id + "동 사건의 전용구역·진입구 매핑 오류");
                EmergencyAccessRouteV2 route =
                    result.Selected.AutomaticPlan.Plan.Selected.Route;
                Assert(route.EntranceCell == result.Selected.Entrance.Cell &&
                       route.FireCell == building.FireEngineZone.ApproachCell,
                    building.Id + "동 경로가 입구→전용구역을 잇지 않음");
                Assert(result.Selected.AutomaticPlan.Generation.Routes.Count >= 2,
                    building.Id + "동에 서로 다른 자동 후보가 2개 미만임");
            }
        }

        private static void TestApartmentComplexMultipleEntrances()
        {
            ApartmentComplexScenarioV2 scenario =
                ApartmentComplexScenarioFactoryV2.Build();
            EmergencyAccessRouteGenerationOptionsV2 options = ComplexOptions();
            var incident = new ApartmentFireIncidentV2(104);
            ApartmentComplexPlanResultV2 primary =
                ApartmentComplexEmergencyPlannerV2.Solve(
                    scenario, incident, false, 4, options, maxTick: 800);
            ApartmentComplexPlanResultV2 dual =
                ApartmentComplexEmergencyPlannerV2.Solve(
                    scenario, incident, true, 4, options, maxTick: 800);
            ApartmentComplexPlanResultV2 repeated =
                ApartmentComplexEmergencyPlannerV2.Solve(
                    scenario, incident, true, 4, options, maxTick: 800);
            Assert(primary.Success && dual.Success && repeated.Success,
                primary.FailReason ?? dual.FailReason ?? repeated.FailReason);
            int primaryTicks =
                primary.Selected.AutomaticPlan.Plan.Selected.Plan.Ticks;
            int dualTicks = dual.Selected.AutomaticPlan.Plan.Selected.Plan.Ticks;
            Assert(dualTicks <= primaryTicks,
                $"복수 진입구가 단일 진입구보다 느림: {primaryTicks}→{dualTicks}");
            Assert(dual.Attempts.Count == 2 &&
                   dual.Attempts.Any(attempt =>
                       attempt.Entrance.Name == "east-secondary" &&
                       attempt.Success),
                "동측 보조진입구 후보가 물리 비교에 포함되지 않음");
            Assert(dual.Selected.Entrance.Name == repeated.Selected.Entrance.Name &&
                   dual.Selected.AutomaticPlan.Plan.Selected.Route.Name ==
                   repeated.Selected.AutomaticPlan.Plan.Selected.Route.Name &&
                   dualTicks ==
                   repeated.Selected.AutomaticPlan.Plan.Selected.Plan.Ticks,
                "동일 8동 사건의 진입구·경로·시간이 재현되지 않음");
        }

        private static void TestApartmentComplexInvalidIncident()
        {
            ApartmentComplexScenarioV2 scenario =
                ApartmentComplexScenarioFactoryV2.Build();
            ApartmentComplexPlanResultV2 result =
                ApartmentComplexEmergencyPlannerV2.Solve(
                    scenario,
                    new ApartmentFireIncidentV2(999),
                    includeSecondaryEntrances: true);
            Assert(!result.Success &&
                   result.Failure == EmergencyAccessFailureV2.InvalidInput &&
                   result.FailReason != null &&
                   result.FailReason.Contains("전용구역"),
                "미등록 화재동을 명시적 입력 실패로 반환하지 않음");
        }

        private static void TestApartmentComplexDensityFactory()
        {
            ApartmentComplexScenarioV2 empty =
                ApartmentComplexScenarioFactoryV2.BuildDensity(0);
            ApartmentComplexScenarioV2 full =
                ApartmentComplexScenarioFactoryV2.BuildDensity(
                    ApartmentComplexScenarioFactoryV2.MaximumBlockingVehicles);
            Assert(empty.BaseProblem.VehicleCount == 0 &&
                   full.BaseProblem.VehicleCount == 22 &&
                   empty.BaseProblem.Slots.Count == full.BaseProblem.Slots.Count &&
                   empty.Buildings.Count == full.Buildings.Count &&
                   empty.Entrances.Count == full.Entrances.Count,
                "밀도 단계가 차량 수 외 단지 메타데이터를 바꿈");
            ParkingSlotV2[] blockingSlots = full.BaseProblem.Slots
                .Where(slot => slot.Kind == SlotKind.Blocking)
                .ToArray();
            Assert(blockingSlots.Count(slot =>
                       slot.Pose.Orientation ==
                       VehicleOrientation.Vertical) == 6 &&
                   blockingSlots.Where(slot =>
                           slot.Pose.Orientation ==
                           VehicleOrientation.Vertical)
                       .All(slot =>
                           new[] { 15, 28, 41 }.Contains(slot.Pose.X) &&
                           (slot.Pose.Y == 10 || slot.Pose.Y == 27)) &&
                   blockingSlots.Count(slot =>
                       slot.Pose.Orientation ==
                       VehicleOrientation.Horizontal) == 16,
                "연결도로6면 세로·가로도로16면 가로 주차 구성이 아님");
            for (int x = 0; x < empty.BaseProblem.Width; x++)
                for (int y = 0; y < empty.BaseProblem.Height; y++)
                    Assert(
                        empty.BaseProblem.IsFloor(x, y) ==
                        full.BaseProblem.IsFloor(x, y),
                        "밀도 단계가 단지 floor를 바꿈");
            bool rejected = false;
            try
            {
                ApartmentComplexScenarioFactoryV2.BuildDensity(23);
            }
            catch (ArgumentOutOfRangeException)
            {
                rejected = true;
            }
            Assert(rejected, "차량 밀도 상한 초과를 거부하지 않음");
        }

        private static void TestApartmentComplexRouteCatalogReuse()
        {
            EmergencyAccessRouteGenerationOptionsV2 options = ComplexOptions();
            ApartmentComplexScenarioV2 empty =
                ApartmentComplexScenarioFactoryV2.BuildDensity(0);
            var catalog = new ApartmentComplexRouteCatalogV2(empty, options);
            var emptySession = new ApartmentComplexPlanningSessionV2(
                empty,
                generationOptions: options,
                maxTick: 800,
                routeCatalog: catalog);
            ApartmentComplexPlanResultV2 primaryResult = emptySession.Solve(
                new ApartmentFireIncidentV2(104),
                includeSecondaryEntrances: false);
            ApartmentComplexPlanResultV2 emptyResult = emptySession.Solve(
                new ApartmentFireIncidentV2(104),
                includeSecondaryEntrances: true);
            int generated = catalog.GenerationCount;

            ApartmentComplexScenarioV2 full =
                ApartmentComplexScenarioFactoryV2.BuildDensity(22);
            var fullSession = new ApartmentComplexPlanningSessionV2(
                full,
                generationOptions: options,
                maxTick: 800,
                routeCatalog: catalog);
            ApartmentComplexPlanResultV2 fullResult = fullSession.Solve(
                new ApartmentFireIncidentV2(104),
                includeSecondaryEntrances: true);
            Assert(primaryResult.Success && emptyResult.Success && fullResult.Success,
                primaryResult.FailReason ??
                emptyResult.FailReason ??
                fullResult.FailReason);
            Assert(generated == 2 && catalog.GenerationCount == generated,
                "동일 기하의 밀도 단계에서 입구 후보를 다시 생성함");
            Assert(emptySession.PhysicalAttemptCount == 2 &&
                   emptySession.AttemptCacheHitCount == 1,
                "서문 단일 결과를 복수 진입구 평가에서 재사용하지 않음");
        }

        private static void TestApartmentComplexPruningEquivalence()
        {
            ApartmentComplexScenarioV2 scenario =
                ApartmentComplexScenarioFactoryV2.Build();
            EmergencyAccessRouteGenerationOptionsV2 options = ComplexOptions();
            ApartmentComplexPlanResultV2 baseline =
                ApartmentComplexEmergencyPlannerV2.Solve(
                    scenario,
                    new ApartmentFireIncidentV2(104),
                    includeSecondaryEntrances: true,
                    activeRobotCount: 4,
                    generationOptions: options,
                    maxTick: 800);
            var session = new ApartmentComplexPlanningSessionV2(
                scenario,
                activeRobotCount: 4,
                generationOptions: options,
                maxTick: 800,
                enableLowerBoundPruning: true);
            ApartmentComplexPlanResultV2 optimized = session.Solve(
                new ApartmentFireIncidentV2(104),
                includeSecondaryEntrances: true);
            Assert(baseline.Success && optimized.Success,
                baseline.FailReason ?? optimized.FailReason);
            EmergencyAccessCandidateResultV2 left =
                baseline.Selected.AutomaticPlan.Plan.Selected;
            EmergencyAccessCandidateResultV2 right =
                optimized.Selected.AutomaticPlan.Plan.Selected;
            Assert(
                baseline.Selected.Entrance.Name ==
                optimized.Selected.Entrance.Name &&
                left.Route.Name == right.Route.Name &&
                left.Scenario.SelectedVehicleCount ==
                right.Scenario.SelectedVehicleCount &&
                left.Plan.Ticks == right.Plan.Ticks,
                "하한 가지치기가 선택 입구·경로·차량·시간을 바꿈");
            Assert(optimized.Attempts
                    .SelectMany(attempt =>
                        attempt.AutomaticPlan.Plan.Candidates)
                    .Where(candidate => candidate.Success)
                    .All(candidate =>
                        candidate.PhysicalLowerBoundTicks <=
                        candidate.Plan.Ticks),
                "물리시간 하한이 실제 성공 계획보다 큼");
        }

        private static void TestDisturbanceValidation()
        {
            ApartmentComplexScenarioV2 scenario =
                ApartmentComplexScenarioFactoryV2.BuildDensity(6);
            foreach (var (cell, label) in new[]
                     {
                         ((7, 10), "건물 풋프린트(floor 밖)"),
                         ((12, 17), "작업 슬롯"),
                         ((0, 1), "로봇 시작점"),
                         ((3, 18), "서문 진입구"),
                         ((9, 3), "전용구역 접근셀"),
                     })
            {
                DisturbedComplexBuildResultV2 rejected =
                    ApartmentComplexDisturbanceV2.Apply(
                        scenario,
                        new ComplexDisturbanceV2("무효-" + label, new[] { cell }));
                Assert(!rejected.Success && rejected.FailReason != null,
                    label + " 봉쇄가 거부되지 않음");
            }
            DisturbedComplexBuildResultV2 overlapped =
                ApartmentComplexDisturbanceV2.Apply(
                    scenario,
                    new ComplexDisturbanceV2(
                        "무효-비관리차량",
                        unmanagedVehicles: new[]
                        {
                            new VehiclePose(12, 17, VehicleOrientation.Horizontal),
                        }));
            Assert(!overlapped.Success && overlapped.FailReason != null,
                "슬롯과 겹치는 비관리 차량이 거부되지 않음");
            DisturbedComplexBuildResultV2 valid =
                ApartmentComplexDisturbanceV2.Apply(
                    scenario,
                    new ComplexDisturbanceV2("유효", new[] { (7, 17), (7, 18) }));
            Assert(valid.Success &&
                   !valid.Scenario.BaseProblem.IsFloor(7, 17) &&
                   !valid.Scenario.BaseProblem.IsFloor(7, 18) &&
                   scenario.BaseProblem.IsFloor(7, 17) &&
                   scenario.BaseProblem.IsFloor(7, 18),
                "유효 봉쇄가 사본에만 적용되고 원본은 불변이어야 함");
        }

        private static void TestDisturbanceIrrelevantBlockage()
        {
            ApartmentComplexScenarioV2 scenario =
                ApartmentComplexScenarioFactoryV2.BuildDensity(6);
            EmergencyAccessRouteGenerationOptionsV2 options = ComplexOptions();
            var incident = new ApartmentFireIncidentV2(101);
            ApartmentComplexPlanResultV2 baseline =
                ApartmentComplexEmergencyPlannerV2.Solve(
                    scenario, incident, false, 4, options, maxTick: 800);
            DisturbedComplexBuildResultV2 disturbed =
                ApartmentComplexDisturbanceV2.Apply(
                    scenario,
                    new ComplexDisturbanceV2(
                        "동측 원거리 봉쇄", new[] { (55, 8), (55, 9) }));
            Assert(disturbed.Success, disturbed.FailReason);
            ApartmentComplexPlanResultV2 result =
                ApartmentComplexEmergencyPlannerV2.Solve(
                    disturbed.Scenario, incident, false, 4, options, maxTick: 800);
            Assert(baseline.Success && result.Success,
                baseline.FailReason ?? result.FailReason);
            Assert(result.Selected.Entrance.Name ==
                       baseline.Selected.Entrance.Name &&
                   result.Selected.AutomaticPlan.Plan.Selected.Route.Name ==
                       baseline.Selected.AutomaticPlan.Plan.Selected.Route.Name &&
                   result.Selected.AutomaticPlan.Plan.Selected.Plan.Ticks ==
                       baseline.Selected.AutomaticPlan.Plan.Selected.Plan.Ticks,
                "101동 서측 대응과 무관한 동측 봉쇄가 결과를 바꿈");
        }

        private static void TestDisturbanceReroute()
        {
            ApartmentComplexScenarioV2 scenario =
                ApartmentComplexScenarioFactoryV2.BuildDensity(6);
            EmergencyAccessRouteGenerationOptionsV2 options = ComplexOptions();
            var incident = new ApartmentFireIncidentV2(101);
            ApartmentComplexPlanResultV2 baseline =
                ApartmentComplexEmergencyPlannerV2.Solve(
                    scenario, incident, true, 4, options, maxTick: 800);
            Assert(baseline.Success, baseline.FailReason);
            EmergencyAccessRouteV2 baselineRoute =
                baseline.Selected.AutomaticPlan.Plan.Selected.Route;
            (int X, int Y) blocked = default;
            bool found = false;
            foreach ((int x, int y) in baselineRoute.RequiredCells)
            {
                DisturbedComplexBuildResultV2 probe =
                    ApartmentComplexDisturbanceV2.Apply(
                        scenario,
                        new ComplexDisturbanceV2("탐침", new[] { (x, y) }));
                if (!probe.Success) continue;
                blocked = (x, y);
                found = true;
                break;
            }
            Assert(found, "선택 경로에 봉쇄 가능한 셀이 없음");
            DisturbedComplexBuildResultV2 disturbed =
                ApartmentComplexDisturbanceV2.Apply(
                    scenario,
                    new ComplexDisturbanceV2("선택 경로 봉쇄", new[] { blocked }));
            ApartmentComplexPlanResultV2 rerouted =
                ApartmentComplexEmergencyPlannerV2.Solve(
                    disturbed.Scenario, incident, true, 4, options, maxTick: 800);
            Assert(rerouted.Success,
                "선택 경로 봉쇄 후 우회 개통 실패: " + rerouted.FailReason);
            Assert(!rerouted.Selected.AutomaticPlan.Plan.Selected.Route
                    .RequiredCells.Contains(blocked),
                "우회 경로가 봉쇄 셀을 다시 포함함");
        }

        private static void TestDisturbanceInfeasible()
        {
            ApartmentComplexScenarioV2 scenario =
                ApartmentComplexScenarioFactoryV2.BuildDensity(6);
            DisturbedComplexBuildResultV2 disturbed =
                ApartmentComplexDisturbanceV2.Apply(
                    scenario,
                    new ComplexDisturbanceV2(
                        "101동 종점 협착",
                        new[]
                        {
                            (8, 36), (8, 37), (8, 38),
                            (10, 36), (10, 37), (10, 38),
                        }));
            Assert(disturbed.Success, disturbed.FailReason);
            ApartmentComplexPlanResultV2 result =
                ApartmentComplexEmergencyPlannerV2.Solve(
                    disturbed.Scenario,
                    new ApartmentFireIncidentV2(101),
                    includeSecondaryEntrances: true,
                    activeRobotCount: 4,
                    generationOptions: ComplexOptions(),
                    maxTick: 800);
            Assert(!result.Success &&
                   result.Failure != EmergencyAccessFailureV2.None &&
                   result.FailReason != null,
                "전용구역 접근 협착이 예외 없는 실패 사유로 반환되어야 함");
        }

        private static void TestDisturbanceReproducibility()
        {
            ApartmentComplexScenarioV2 scenario =
                ApartmentComplexScenarioFactoryV2.BuildDensity(6);
            EmergencyAccessRouteGenerationOptionsV2 options = ComplexOptions();
            var disturbance = new ComplexDisturbanceV2(
                "중앙 종축 봉쇄·유닛 2조", new[] { (16, 9), (16, 10) },
                activeRobotCount: 2);
            var incident = new ApartmentFireIncidentV2(103);
            ApartmentComplexPlanResultV2[] runs = new ApartmentComplexPlanResultV2[2];
            for (int attempt = 0; attempt < runs.Length; attempt++)
            {
                DisturbedComplexBuildResultV2 disturbed =
                    ApartmentComplexDisturbanceV2.Apply(scenario, disturbance);
                Assert(disturbed.Success, disturbed.FailReason);
                runs[attempt] = ApartmentComplexEmergencyPlannerV2.Solve(
                    disturbed.Scenario,
                    incident,
                    includeSecondaryEntrances: true,
                    activeRobotCount: disturbance.ActiveRobotCount,
                    generationOptions: options,
                    maxTick: 800);
                Assert(runs[attempt].Success,
                    "유닛 2조 교란 대응 실패: " + runs[attempt].FailReason);
            }
            Assert(runs[0].Selected.Entrance.Name == runs[1].Selected.Entrance.Name &&
                   runs[0].Selected.AutomaticPlan.Plan.Selected.Route.Name ==
                       runs[1].Selected.AutomaticPlan.Plan.Selected.Route.Name &&
                   runs[0].Selected.AutomaticPlan.Plan.Selected.Plan.Ticks ==
                       runs[1].Selected.AutomaticPlan.Plan.Selected.Plan.Ticks,
                "같은 교란·유닛 수의 결과가 재현되지 않음");
        }

        private static (EmergencyProblemV2 Problem, PipelinedPlanResultV2 Plan)
            BatteryGateFixture()
        {
            EmergencyScenarioBuildResultV2 built =
                CorridorScenarioFactoryV2.BuildEmergency(1, 30);
            Assert(built.Success, "배터리 픽스처 시나리오 실패: " + built.FailReason);
            PipelinedPlanResultV2 plan = PipelinedPrioritizedPlannerV2.Solve(
                built.Problem, activeRobotCount: 2, maxHighLevelCandidates: 8);
            Assert(plan.Success && plan.PhysicallyValid,
                "배터리 픽스처 계획 실패: " + plan.FailReason);
            Assert(plan.Missions.Count(m => m.RobotIndex == 0) >= 2,
                "픽스처 유닛0의 미션이 2개 미만이라 경계 퇴역을 시험할 수 없음");
            return (built.Problem, plan);
        }

        private static int[] LowChargeForRobotZero(
            PipelinedPlanResultV2 plan, BatteryModelV2 battery)
        {
            PipelinedMissionV2 first = plan.Missions
                .Where(m => m.RobotIndex == 0)
                .OrderBy(m => m.StartTick)
                .First();
            return new[]
            {
                BatteryHandoverV2.MissionCost(first) + battery.ReserveTicks,
                battery.CapacityTicks,
            };
        }

        private static void TestBatteryAccounting()
        {
            (EmergencyProblemV2 problem, PipelinedPlanResultV2 plan) =
                BatteryGateFixture();
            var battery = new BatteryModelV2(10000, 100);
            BatteryHandoverResultV2 result = BatteryHandoverV2.Evaluate(
                problem, plan, battery, new[] { 10000, 10000 });
            Assert(result.Success && !result.HandoverOccurred &&
                   result.TotalTicks == plan.Ticks && result.DelayTicks == 0,
                "만충 함대에서 핸드오버가 발동하거나 시간이 변함");
            for (int robot = 0; robot < 2; robot++)
            {
                int expected = plan.Missions
                    .Where(m => m.RobotIndex == robot)
                    .Sum(BatteryHandoverV2.MissionCost);
                Assert(result.ConsumedTicks[robot] == expected &&
                       result.RemainingTicks[robot] == 10000 - expected,
                    $"유닛{robot} 소모·잔량 회계 불일치");
            }
        }

        private static void TestHandoverStateExtraction()
        {
            (EmergencyProblemV2 problem, PipelinedPlanResultV2 plan) =
                BatteryGateFixture();
            var battery = new BatteryModelV2(10000, 100);
            BatteryHandoverResultV2 result = BatteryHandoverV2.Evaluate(
                problem, plan, battery, LowChargeForRobotZero(plan, battery));
            Assert(result.HandoverOccurred && result.RetiredRobot == 0,
                "유닛0 미션 경계 퇴역이 발동하지 않음");
            Assert(result.DeliveredVehicles.Count >= 1 &&
                   result.SyncTick >= result.RetireDecisionTick,
                "인도 완료분 또는 동기 시점이 비정상");
            EmergencyProblemV2 residual = result.ResidualProblem;
            Assert(residual != null, "잔여 문제가 없음: " + result.FailReason);
            Assert(residual.VehicleCount ==
                   problem.VehicleCount - result.DeliveredVehicles.Count,
                "인도 + 잔여 차량 수가 전체와 다름");
            Assert(residual.StagingCapacity ==
                   problem.StagingCapacity - result.DeliveredVehicles.Count,
                "잔여 적치 용량이 인도 수만큼 줄지 않음");
            Assert(residual.FixedVehiclePoses.Count ==
                   problem.FixedVehiclePoses.Count + result.DeliveredVehicles.Count -
                   result.ConvertedStagingPoseCount,
                "인도 완료 차량의 고정/봉쇄 전환 회계가 어긋남");
            Assert(residual.RobotStarts.Count == 1 &&
                   residual.RobotStarts.Distinct().Count() == 1,
                "생존 유닛 시작점 구성이 비정상");

            // t=0 출동 불능 경계: 유닛0이 첫 미션조차 못 받는 전량 —
            // 그 미션의 차량이 "인도 완료"로 잘못 집계되면 안 된다.
            PipelinedMissionV2 first = plan.Missions
                .Where(m => m.RobotIndex == 0)
                .OrderBy(m => m.StartTick)
                .First();
            int[] depleted =
            {
                BatteryHandoverV2.MissionCost(first) + battery.ReserveTicks - 1,
                battery.CapacityTicks,
            };
            BatteryHandoverResultV2 immediate = BatteryHandoverV2.Evaluate(
                problem, plan, battery, depleted);
            Assert(immediate.HandoverOccurred &&
                   immediate.RetiredRobot == 0 &&
                   immediate.RetireDecisionTick == 0 &&
                   immediate.ConsumedTicks[0] == 0 &&
                   !immediate.DeliveredVehicles.Contains(first.VehicleIndex),
                "t=0 출동 불능 유닛의 미수행 미션이 인도분으로 새어 들어감");
        }

        private static void TestHandoverComposition()
        {
            (EmergencyProblemV2 problem, PipelinedPlanResultV2 plan) =
                BatteryGateFixture();
            var battery = new BatteryModelV2(10000, 100);
            BatteryHandoverResultV2 result = BatteryHandoverV2.Evaluate(
                problem, plan, battery, LowChargeForRobotZero(plan, battery));
            Assert(result.Success,
                "핸드오버 잔여 재계획 실패: " + result.FailReason);
            Assert(result.ResidualPlan.Success && result.ResidualPlan.PhysicallyValid,
                "잔여 계획이 물리 검증을 통과하지 못함");
            Assert(result.TotalTicks ==
                   result.SyncTick + result.ResidualPlan.Ticks &&
                   result.DelayTicks == result.TotalTicks - plan.Ticks,
                "핸드오버 시간 합성 공식이 어긋남");
        }

        private static void TestHandoverReplacement()
        {
            (EmergencyProblemV2 problem, PipelinedPlanResultV2 plan) =
                BatteryGateFixture();
            var battery = new BatteryModelV2(10000, 100);
            BatteryHandoverResultV2 result = BatteryHandoverV2.Evaluate(
                problem, plan, battery, LowChargeForRobotZero(plan, battery),
                replacementStart: (11, 21));
            Assert(result.Success && result.HandoverOccurred,
                "교체 유닛 핸드오버 실패: " + result.FailReason);
            Assert(result.ResidualProblem.RobotStarts.Count == 2 &&
                   result.ResidualProblem.RobotStarts.Contains((11, 21)),
                "충전소 출발 교체 유닛이 잔여 문제에 합류하지 않음");
        }

        private static void TestHandoverReproducibility()
        {
            (EmergencyProblemV2 problem, PipelinedPlanResultV2 plan) =
                BatteryGateFixture();
            var battery = new BatteryModelV2(10000, 100);
            int[] charges = LowChargeForRobotZero(plan, battery);
            BatteryHandoverResultV2 first = BatteryHandoverV2.Evaluate(
                problem, plan, battery, charges, replacementStart: (11, 21));
            BatteryHandoverResultV2 second = BatteryHandoverV2.Evaluate(
                problem, plan, battery, charges, replacementStart: (11, 21));
            Assert(first.Success && second.Success,
                first.FailReason ?? second.FailReason);
            Assert(first.RetiredRobot == second.RetiredRobot &&
                   first.SyncTick == second.SyncTick &&
                   first.TotalTicks == second.TotalTicks &&
                   first.ResidualPlan.Ticks == second.ResidualPlan.Ticks,
                "같은 입력의 핸드오버 결과가 재현되지 않음");
        }

        private static void TestSiteAGeometry()
        {
            ApartmentComplexScenarioV2 empty =
                SiteABlockScenarioFactoryV2.BuildDensity(0);
            ApartmentComplexScenarioV2 full =
                SiteABlockScenarioFactoryV2.BuildDensity(
                    SiteABlockScenarioFactoryV2.MaximumBlockingVehicles);
            Assert(empty.Buildings.Count == SiteABlockScenarioFactoryV2.BuildingCount &&
                   empty.BaseProblem.Slots.Count == full.BaseProblem.Slots.Count,
                "단지A 동 수 또는 슬롯 구성이 밀도에 따라 변함");
            int background = empty.BaseProblem.VehicleCount;
            Assert(background > 0 &&
                   full.BaseProblem.VehicleCount ==
                   background + SiteABlockScenarioFactoryV2.MaximumBlockingVehicles,
                "만차 배경 + 가변 N 초기 점유 구성이 어긋남");
            Assert(empty.BaseProblem.StagingCapacity == 12,
                "적치 12면 구성이 어긋남");
            var slotCells = new HashSet<(int X, int Y)>();
            foreach (ParkingSlotV2 slot in full.BaseProblem.Slots)
            {
                slotCells.Add((slot.Pose.X, slot.Pose.Y));
                slotCells.Add(slot.Pose.SecondCell);
            }
            foreach (ApartmentBuildingV2 building in full.Buildings)
            {
                foreach ((int x, int y) in building.FireEngineZone.Cells)
                {
                    Assert(full.BaseProblem.IsFloor(x, y),
                        "단지A 전용구역 셀이 floor 밖임");
                    Assert(!slotCells.Contains((x, y)),
                        "단지A 전용구역 셀을 주차 슬롯이 점유함");
                }
                foreach ((int x, int y) in building.FootprintCells)
                    Assert(!full.BaseProblem.IsFloor(x, y),
                        "단지A 건물 풋프린트가 주행 가능 floor임");
            }
            foreach (ApartmentComplexEntranceV2 entrance in full.Entrances)
                Assert(full.BaseProblem.IsFloor(entrance.Cell.X, entrance.Cell.Y),
                    "단지A 진입구가 floor 밖임");
        }

        private static void TestSiteAEvaluation()
        {
            ApartmentComplexScenarioV2 scenario =
                SiteABlockScenarioFactoryV2.BuildDensity(0);
            EmergencyAccessRouteGenerationOptionsV2 options = ComplexOptions();
            ApartmentComplexPlanResultV2[] runs = new ApartmentComplexPlanResultV2[2];
            for (int attempt = 0; attempt < runs.Length; attempt++)
            {
                runs[attempt] = ApartmentComplexEmergencyPlannerV2.Solve(
                    scenario,
                    new ApartmentFireIncidentV2(1),
                    includeSecondaryEntrances: false,
                    activeRobotCount: 4,
                    generationOptions: options,
                    maxTick: 2000);
                Assert(runs[attempt].Success,
                    "단지A 1동 개통 실패: " + runs[attempt].FailReason);
            }
            EmergencyAccessCandidateResultV2 selected =
                runs[0].Selected.AutomaticPlan.Plan.Selected;
            // 골목 도로 2셀(5m) < 폭3(7.5m) — 이중주차 0대여도 연석 주차열의
            // 관리 차량 이동 없이는 개통이 불가능해야 한다 (단지A 기하의 핵심).
            Assert(selected.Scenario.SelectedVehicleCount >= 1 &&
                   selected.Plan.Ticks > 0,
                "이중주차 0대에서 배경 주차열 이동 없이 개통됨 — 기하 의도와 다름");
            Assert(runs[0].Selected.AutomaticPlan.Plan.Selected.Plan.Ticks ==
                   runs[1].Selected.AutomaticPlan.Plan.Selected.Plan.Ticks &&
                   runs[0].Selected.AutomaticPlan.Plan.Selected.Route.Name ==
                   runs[1].Selected.AutomaticPlan.Plan.Selected.Route.Name,
                "단지A 평가가 재현되지 않음");
        }

        private static void TestSiteAStagingCounterfactual()
        {
            ApartmentComplexScenarioV2 baseline =
                SiteABlockScenarioFactoryV2.BuildDensity(0);
            ApartmentComplexScenarioV2 redistributed =
                SiteABlockScenarioFactoryV2.BuildDensity(
                    0, stagingLayout: SiteStagingLayoutV2.Redistributed);
            ApartmentComplexScenarioV2 extended =
                SiteABlockScenarioFactoryV2.BuildDensity(
                    0, stagingLayout: SiteStagingLayoutV2.Extended);
            Assert(redistributed.BaseProblem.StagingCapacity == 12 &&
                   extended.BaseProblem.StagingCapacity == 18,
                "반사실 적치 면수(재배치 12·확장 18)가 어긋남");
            Assert(redistributed.BaseProblem.Slots
                       .Count(s => s.Kind == SlotKind.Staging && s.Pose.X == 59) == 6 &&
                   extended.BaseProblem.Slots
                       .Count(s => s.Kind == SlotKind.Staging && s.Pose.X == 59) == 6,
                "동측 연석 적치 6면이 배치되지 않음");
            // 반사실은 적치만 바꾼다 — 차량·건물·진입구 기하는 기준선과 동일해야 함
            Assert(redistributed.BaseProblem.VehicleCount ==
                       baseline.BaseProblem.VehicleCount &&
                   redistributed.BaseProblem.Slots.Count(s =>
                       s.Kind == SlotKind.Blocking) ==
                   baseline.BaseProblem.Slots.Count(s =>
                       s.Kind == SlotKind.Blocking),
                "반사실이 적치 외의 구성(차량·주차 슬롯)을 바꿈");

            EmergencyAccessRouteGenerationOptionsV2 options = ComplexOptions();
            ApartmentComplexPlanResultV2[] runs = new ApartmentComplexPlanResultV2[2];
            for (int attempt = 0; attempt < runs.Length; attempt++)
            {
                ApartmentComplexScenarioV2 scenario =
                    SiteABlockScenarioFactoryV2.BuildDensity(
                        0, stagingLayout: SiteStagingLayoutV2.Redistributed);
                runs[attempt] = ApartmentComplexEmergencyPlannerV2.Solve(
                    scenario,
                    new ApartmentFireIncidentV2(2),
                    includeSecondaryEntrances: true,
                    activeRobotCount: 4,
                    generationOptions: options,
                    maxTick: 2000);
                Assert(runs[attempt].Success,
                    "재배치안 2동 개통 실패: " + runs[attempt].FailReason);
            }
            Assert(runs[0].Selected.AutomaticPlan.Plan.Selected.Plan.Ticks ==
                   runs[1].Selected.AutomaticPlan.Plan.Selected.Plan.Ticks,
                "재배치안 결과가 재현되지 않음");
        }

        private static void TestHandoverParkedInterference()
        {
            (EmergencyProblemV2 problem, PipelinedPlanResultV2 plan) =
                BatteryGateFixture();
            var battery = new BatteryModelV2(10000, 100);
            BatteryHandoverResultV2 result = BatteryHandoverV2.Evaluate(
                problem, plan, battery, LowChargeForRobotZero(plan, battery));
            Assert(result.Success && result.HandoverOccurred,
                "정차 간섭 게이트 픽스처 실패: " + result.FailReason);
            // 유닛0은 첫 미션을 완결하고 인도 차량 밑에 정차 — 그 pose는
            // 고정 차량이 아니라 전체 봉쇄로 전환되어야 한다.
            Assert(result.ConvertedStagingPoseCount >= 1,
                "인도 차량 밑 정차 유닛의 pose가 봉쇄로 전환되지 않음");
            Assert(result.ParkedUnitCells.Count >= 2,
                "정차 봉쇄 셀 기록이 비어 있음");
            foreach ((int x, int y) in result.ParkedUnitCells)
            {
                Assert(problem.IsFloor(x, y),
                    "원 문제에서 floor였던 셀만 정차 봉쇄 대상이어야 함");
                Assert(!result.ResidualProblem.IsFloor(x, y),
                    $"정차 유닛 셀 ({x},{y})가 잔여 문제에서 여전히 통행 가능함");
            }
            Assert(result.ResidualPlan.Success && result.ResidualPlan.PhysicallyValid,
                "정차 봉쇄 반영 후 잔여 계획 실패: " + result.ResidualPlan.FailReason);
        }

        private static void TestSiteAArterialZone()
        {
            Assert(SiteABlockScenarioFactoryV2.MaximumVariableSlots(
                       SiteZonePlacementV2.ArterialFrontage) == 12 &&
                   SiteABlockScenarioFactoryV2.MaximumVariableSlots(
                       SiteZonePlacementV2.AlleyFrontage) == 16,
                "배치안별 가변 최대 면수가 어긋남");
            ApartmentComplexScenarioV2 arterial =
                SiteABlockScenarioFactoryV2.BuildDensity(
                    0,
                    null,
                    SiteStagingLayoutV2.SouthWestOnly,
                    SiteZonePlacementV2.ArterialFrontage);
            var slotCells = new HashSet<(int X, int Y)>();
            foreach (ParkingSlotV2 slot in arterial.BaseProblem.Slots)
            {
                slotCells.Add((slot.Pose.X, slot.Pose.Y));
                slotCells.Add(slot.Pose.SecondCell);
            }
            foreach (ApartmentBuildingV2 building in arterial.Buildings)
                foreach ((int x, int y) in building.FireEngineZone.Cells)
                {
                    Assert(x >= 28 && x <= 31,
                        "재지정 전용구역이 중앙 종축 밖임");
                    Assert(arterial.BaseProblem.IsFloor(x, y),
                        "재지정 전용구역 셀이 floor 밖임");
                    Assert(!slotCells.Contains((x, y)),
                        "재지정 전용구역 셀을 주차 슬롯이 점유함");
                }

            EmergencyAccessRouteGenerationOptionsV2 options = ComplexOptions();
            ApartmentComplexPlanResultV2 repeat = null;
            foreach (ApartmentBuildingV2 building in arterial.Buildings)
            {
                ApartmentComplexPlanResultV2 result =
                    ApartmentComplexEmergencyPlannerV2.Solve(
                        arterial,
                        new ApartmentFireIncidentV2(building.Id),
                        includeSecondaryEntrances: true,
                        activeRobotCount: 4,
                        generationOptions: options,
                        maxTick: 2000);
                Assert(result.Success,
                    building.Id + "동 간선 재지정 개통 실패: " + result.FailReason);
                int moved = result.Selected.AutomaticPlan.Plan.Selected
                    .Scenario.SelectedVehicleCount;
                Assert(moved <= 4,
                    building.Id + "동 간선 재지정에서 골목 연석열이 이동됨: " +
                    moved + "대");
                if (building.Id == 1) repeat = result;
            }
            ApartmentComplexPlanResultV2 again =
                ApartmentComplexEmergencyPlannerV2.Solve(
                    arterial,
                    new ApartmentFireIncidentV2(1),
                    includeSecondaryEntrances: true,
                    activeRobotCount: 4,
                    generationOptions: options,
                    maxTick: 2000);
            Assert(again.Success && repeat != null &&
                   again.Selected.AutomaticPlan.Plan.Selected.Plan.Ticks ==
                   repeat.Selected.AutomaticPlan.Plan.Selected.Plan.Ticks,
                "간선 재지정 결과가 재현되지 않음");
        }

        private static void TestPlanUtilization()
        {
            (EmergencyProblemV2 problem, PipelinedPlanResultV2 plan) =
                BatteryGateFixture();
            PlanUtilizationReportV2 first =
                PlanUtilizationV2.Analyze(problem, plan);
            PlanUtilizationReportV2 second =
                PlanUtilizationV2.Analyze(problem, plan);
            int lift = problem.Timing.LiftServiceTicks;
            int drop = problem.Timing.DropServiceTicks;
            for (int robot = 0; robot < first.RobotCount; robot++)
            {
                Assert(first.MoveTicks[robot] + first.ServiceTicks[robot] +
                       first.WaitTicks[robot] + first.IdleTicks[robot] ==
                       first.Makespan,
                    $"유닛{robot} 로봇-틱 4분해 합이 makespan과 다름");
                int missions = plan.Missions.Count(m => m.RobotIndex == robot);
                Assert(first.ServiceTicks[robot] == missions * (lift + drop),
                    $"유닛{robot} 서비스틱이 미션 수 × 서비스틱과 다름");
                Assert(first.MoveTicks[robot] == second.MoveTicks[robot] &&
                       first.WaitTicks[robot] == second.WaitTicks[robot] &&
                       first.IdleTicks[robot] == second.IdleTicks[robot],
                    "로봇-틱 분해가 재현되지 않음");
            }
            Assert(first.EffectiveParallelism > 0.0 &&
                   first.EffectiveParallelism <= first.RobotCount + 1e-9,
                "유효 병렬성이 (0, 조수] 범위를 벗어남");
        }

        private static EmergencyAccessRouteGenerationOptionsV2 ComplexOptions()
        {
            return new EmergencyAccessRouteGenerationOptionsV2
            {
                MaxRoutes = 4,
                MaxCenterlineAttempts = 16,
                MaxSearchExpansions = 100000,
            };
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
