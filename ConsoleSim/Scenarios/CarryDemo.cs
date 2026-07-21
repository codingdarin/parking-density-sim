using System;
using System.Collections.Generic;
using System.Linq;
using ParkingSim.Core.Agents;
using ParkingSim.Core.Grid;
using ParkingSim.Core.Pathfinding;

namespace ParkingSim.Scenarios
{
    /// <summary>
    /// D2 데모: 로봇 1대가 통로 최서단 차량을 적치 구역까지 운반하는 전 과정을 t별로 출력.
    /// 사이클: 접근(빈 몸 1셀) → 적재(1틱) → 운반(1×2 강체) → 하차(1틱, 싱크 집계).
    /// </summary>
    public static class CarryDemo
    {
        public static void Run(int occupiedLanes)
        {
            var lot = ParkingLayoutBuilder.Build(new LayoutConfig { OccupiedLanes = occupiedLanes });
            if (lot.CorridorCarCount == 0)
            {
                Console.WriteLine("통로 차량이 없음 — 점유 레인 1 이상으로 실행");
                return;
            }

            var g = lot.Grid;
            var agv = new Agv(1, 1, lot.LaneYs[lot.LaneYs.Count / 2]); // 적치 구역 가운데 레인에서 출발
            var agvs = new List<Agv> { agv };
            var target = lot.Cars.Where(c => c.InCorridor).OrderBy(c => c.X).ThenBy(c => c.Y).First();

            Console.WriteLine($"=== D2 운반 사이클 (점유 {occupiedLanes}레인, 대상: 차량 {target.Id} @({target.X},{target.Y})) ===");
            Console.WriteLine(TextRenderer.Legend);
            int tick = 0;
            Frame(tick, "시작", lot, agvs);

            // 1) 접근 — 대상 차량의 앵커 셀로 (대상 차량 셀만 진입 허용)
            bool ApproachPassable(int x, int y)
            {
                var t = g.TypeAt(x, y);
                if (t == CellType.Outside || t == CellType.Stall) return false;
                int car = g.CarAt(x, y);
                return car == 0 || car == target.Id;
            }
            var approach = AStar.FindPath(g.Width, g.Height, (agv.X, agv.Y), (target.X, target.Y), ApproachPassable);
            if (approach == null)
            {
                Console.WriteLine("접근 경로 없음");
                return;
            }
            foreach (var (x, y) in approach.Skip(1))
            {
                agv.MoveTo(x, y);
                Frame(++tick, "접근", lot, agvs);
            }

            // 2) 적재 (1틱) — 차량이 격자에서 로봇 위로
            g.RemoveCar(target);
            agv.PickUp(target);
            Frame(++tick, "적재", lot, agvs);

            // 3) 운반 — 1×2 강체, 목적지 풋프린트 전체가 비어 있어야 이동
            bool CarryPassable(int x, int y)
            {
                var second = agv.CarriedHorizontal ? (X: x + 1, Y: y) : (X: x, Y: y + 1);
                return Free(x, y) && Free(second.X, second.Y);

                bool Free(int cx, int cy)
                {
                    if (!g.InBounds(cx, cy)) return false;
                    var t = g.TypeAt(cx, cy);
                    if (t == CellType.Outside || t == CellType.Stall) return false;
                    return g.CarAt(cx, cy) == 0;
                }
            }
            var goal = (X: 0, Y: agv.Y); // 같은 레인의 적치 입구 (앵커 x=0 → (0,y),(1,y) 모두 적치 셀)
            var carry = AStar.FindPath(g.Width, g.Height, (agv.X, agv.Y), goal, CarryPassable);
            if (carry == null)
            {
                Console.WriteLine("운반 경로 없음");
                return;
            }
            foreach (var (x, y) in carry.Skip(1))
            {
                agv.MoveTo(x, y);
                Frame(++tick, "운반", lot, agvs);
            }

            // 4) 하차 (1틱) — 싱크: 차량 소멸, S 집계
            int droppedId = agv.Drop();
            Frame(++tick, $"하차 — 차량 {droppedId} 적치 (S=1)", lot, agvs);

            Console.WriteLine(
                $"총 {tick}틱 ≈ {tick * GridMap.SecondsPerCell:0}초" +
                $" (봉투 모델: 왕복 = 차량 위치 + 2×d_s + 적재/하차 오버헤드)");
        }

        private static void Frame(int tick, string caption, ParkingLot lot, List<Agv> agvs)
        {
            Console.WriteLine($"t={tick} ({caption})");
            Console.WriteLine(TextRenderer.Render(lot, agvs));
        }
    }
}
