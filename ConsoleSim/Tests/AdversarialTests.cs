using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ParkingSim.Core.Pathfinding;

namespace ParkingSim.Tests
{
    /// <summary>
    /// M1 통과 기준 — 적대적 테스트 5종.
    /// 시공간 A* + 예약 테이블이 그럴듯하게 돌아가지만 미묘하게 틀리는 대표 상황들.
    /// </summary>
    public static class AdversarialTests
    {
        /// <summary>통과한 테스트 수를 반환 (전부 통과 = 5)</summary>
        public static int RunAll()
        {
            var tests = new (string Name, Func<bool> Test)[]
            {
                ("① 좁은 통로 정면 조우 — 대피 베이로 교행", HeadOnNarrowCorridor),
                ("② 스왑 충돌 — 맞교환 거부 + 우회로 존재 시 해결", SwapConflict),
                ("③ 교차로 4대 동시 진입 — 중심 셀 경합 조정", FourWayIntersection),
                ("④ 목적지 점유 — 비켜줄 때까지 대기 / 영구 점유는 실패", GoalOccupied),
                ("⑤ 해 없음 — 무한 루프 없이 유한 시간 내 실패 종료", NoSolutionTerminates),
            };

            int passed = 0;
            foreach (var (name, test) in tests)
            {
                bool ok;
                try { ok = test(); }
                catch (Exception e)
                {
                    ok = false;
                    Console.WriteLine($"   예외: {e.Message}");
                }
                Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}");
                if (ok) passed++;
            }
            Console.WriteLine($"\n{passed}/{tests.Length} 통과");
            return passed;
        }

        /// <summary>
        /// 건전성 검사(뮤테이션 테스트): 예약 테이블을 끄고 같은 5종을 돌린다.
        /// 전부 실패해야 정상 — 하나라도 통과하면 그 테스트는 조정 메커니즘을 검증하지 않는 것.
        /// (⑤도 절반이 예약 기반 차단이라 실패해야 함)
        /// </summary>
        public static bool RunSanityCheck()
        {
            Console.WriteLine("=== 건전성 검사: 예약 테이블 비활성화 상태에서 동일 테스트 실행 ===");
            int passed;
            TestSupport.CoordinationDisabled = true;
            try { passed = RunAll(); }
            finally { TestSupport.CoordinationDisabled = false; }

            bool ok = passed == 0;
            Console.WriteLine(ok
                ? "→ 건전성 확인: 예약 없이는 5종 전부 실패. 테스트가 실제로 조정 메커니즘을 검증한다."
                : $"→ !! 경고: 예약 없이도 {passed}종 통과 — 해당 테스트는 아무것도 검증하지 않는다.");
            return ok;
        }

        /// <summary>폭 1 통로 양끝에서 마주 오는 두 로봇 — 후순위가 대피 베이(6,1)로 비켜야 함.</summary>
        private static bool HeadOnNarrowCorridor()
        {
            var cells = Enumerable.Range(0, 9).Select(x => (x, 0)).Append((6, 1));
            var g = TestSupport.SparseGrid(9, 2, cells);
            var rt = new ReservationTable();

            var a = TestSupport.PlanAndReserve(g, rt, (0, 0), (8, 0));
            var b = TestSupport.PlanAndReserve(g, rt, (8, 0), (0, 0));
            if (a == null || b == null) return false;

            var (vertex, swap) = TestSupport.CountConflicts(new[] { a, b });
            bool usedBay = b.Contains((6, 1));
            return vertex == 0 && swap == 0 && usedBay;
        }

        /// <summary>(a) 셀 2개뿐 → 맞교환밖에 없으므로 계획 거부(null).
        /// (b) 2×2 방 → 우회로가 있으면 스왑 없이 해결.</summary>
        private static bool SwapConflict()
        {
            // (a) 스왑 거부
            var g1 = TestSupport.SparseGrid(2, 1, new[] { (0, 0), (1, 0) });
            var rt1 = new ReservationTable();
            var a1 = TestSupport.PlanAndReserve(g1, rt1, (0, 0), (1, 0), maxTick: 50);
            var b1 = TestSupport.PlanAndReserve(g1, rt1, (1, 0), (0, 0), maxTick: 50);
            if (a1 == null || b1 != null) return false; // b1은 반드시 실패해야 함

            // (b) 우회로 존재 시 해결
            var g2 = TestSupport.RoomGrid(2, 2);
            var rt2 = new ReservationTable();
            var a2 = TestSupport.PlanAndReserve(g2, rt2, (0, 0), (1, 0));
            var b2 = TestSupport.PlanAndReserve(g2, rt2, (1, 0), (0, 0));
            if (a2 == null || b2 == null) return false;

            var (vertex, swap) = TestSupport.CountConflicts(new[] { a2, b2 });
            return vertex == 0 && swap == 0;
        }

        /// <summary>5×5 열린 방, 4대가 서로 반대편으로 — 최단 경로가 전부 중심 (2,2)를 같은 틱에 지남.</summary>
        private static bool FourWayIntersection()
        {
            var g = TestSupport.RoomGrid(5, 5);
            var rt = new ReservationTable();

            var starts = new (int X, int Y)[] { (0, 2), (4, 2), (2, 0), (2, 4) };
            var goals = new (int X, int Y)[] { (4, 2), (0, 2), (2, 4), (2, 0) };
            var paths = new List<List<(int X, int Y)>>();
            for (int i = 0; i < 4; i++)
            {
                var p = TestSupport.PlanAndReserve(g, rt, starts[i], goals[i]);
                if (p == null) return false;
                paths.Add(p);
            }

            var (vertex, swap) = TestSupport.CountConflicts(paths);
            int makespan = paths.Max(p => p.Count) - 1;
            return vertex == 0 && swap == 0 && makespan <= 15;
        }

        /// <summary>(a) 목적지를 다른 로봇이 t=6까지 점유 후 비킴 → 대기 후 t=7 도착.
        /// (b) 영구 점유 → 계획 실패(null)로 정상 종료.</summary>
        private static bool GoalOccupied()
        {
            // (a) 일시 점유: 스크립트된 로봇 B가 (4,0)에 t=5까지 머문 뒤 (4,1)로 비킴
            var cells = Enumerable.Range(0, 5).Select(x => (x, 0)).Append((4, 1));
            var g = TestSupport.SparseGrid(5, 2, cells);
            var rt = new ReservationTable();
            for (int t = 0; t <= 5; t++) rt.ReserveStep(4, 0, t);
            rt.ReserveStep(4, 1, 6);
            rt.ReserveFrom(4, 1, 6);

            var a = TestSupport.PlanAndReserve(g, rt, (0, 0), (4, 0));
            if (a == null || a.Count - 1 != 7) return false; // 최단 4틱 + 대기 3틱

            // (b) 영구 점유
            var g2 = TestSupport.SparseGrid(5, 1, Enumerable.Range(0, 5).Select(x => (x, 0)));
            var rt2 = new ReservationTable();
            rt2.ReserveFrom(4, 0, 0);
            var b = TestSupport.PlanAndReserve(g2, rt2, (0, 0), (4, 0), maxTick: 100);
            return b == null;
        }

        /// <summary>(a) 물리적 단절 → 즉시 실패. (b) 거대 탐색 공간 + 도달 불가 목표 →
        /// 확장 상한이 탐색을 끊어 유한 시간 내 실패 종료 (D9 고밀도 배치가 반드시 타는 경로).</summary>
        private static bool NoSolutionTerminates()
        {
            var sw = Stopwatch.StartNew();

            // (a) 벽으로 단절된 통로
            var cells = Enumerable.Range(0, 7).Where(x => x != 3).Select(x => (x, 0));
            var g1 = TestSupport.SparseGrid(7, 1, cells);
            var a = TestSupport.PlanAndReserve(g1, new ReservationTable(), (0, 0), (6, 0), maxTick: 2000);
            if (a != null) return false;

            // (b) 20×20 열린 방, 목표 영구 점유, maxTick 5000 → 확장 상한(10만)이 종료 보장
            var g2 = TestSupport.RoomGrid(20, 20);
            var rt = new ReservationTable();
            rt.ReserveFrom(19, 19, 0);
            var b = TestSupport.PlanAndReserve(g2, rt, (0, 0), (19, 19),
                maxTick: 5000, maxExpansions: 100000);
            sw.Stop();

            Console.WriteLine($"   (⑤ 소요 {sw.ElapsedMilliseconds}ms)");
            return b == null && sw.ElapsedMilliseconds < 5000;
        }

        /// <summary>
        /// 비교 데모(테스트 아님): 같은 교차로 시나리오를 예약 테이블 공유 없이
        /// (각자 빈 테이블 = 무조정 독립 계획) 돌리면 충돌이 발생함을 보인다.
        /// "중앙 조정 없이는 안전 주행이 성립하지 않는다"의 콘솔 근거 — 계획서 P1 항목.
        /// </summary>
        public static void RunNoCoordinationBaseline()
        {
            var g = TestSupport.RoomGrid(5, 5);
            var starts = new (int X, int Y)[] { (0, 2), (4, 2), (2, 0), (2, 4) };
            var goals = new (int X, int Y)[] { (4, 2), (0, 2), (2, 4), (2, 0) };

            var paths = new List<List<(int X, int Y)>>();
            foreach (var i in Enumerable.Range(0, 4))
                paths.Add(TestSupport.PlanAndReserve(g, new ReservationTable(), starts[i], goals[i]));

            var (vertex, swap) = TestSupport.CountConflicts(paths);
            Console.WriteLine("=== 무조정 독립 계획 베이스라인 (교차로 4대, 예약 공유 없음) ===");
            Console.WriteLine($"정점 충돌 {vertex}건, 스왑 {swap}건 발생");
            Console.WriteLine("→ 동일 시나리오가 예약 테이블 공유 시 충돌 0건 (테스트 ③).");
            Console.WriteLine("  중앙 조정 없는 독립 계획으로는 안전 주행이 성립하지 않는다.");
        }
    }
}
