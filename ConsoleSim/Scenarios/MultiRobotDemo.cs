using System;
using System.Collections.Generic;
using System.Linq;
using ParkingSim.Core.Agents;
using ParkingSim.Core.Grid;
using ParkingSim.Core.Pathfinding;

namespace ParkingSim.Scenarios
{
    /// <summary>
    /// D3 데모: 로봇 4대가 3레인 완전 점유 통로의 프런티어 차량 4대를 동시에 적치 구역으로 옮긴다.
    /// 우선순위 순차 계획(시공간 A* + 예약 테이블) → 재생하며 매 틱 충돌 검증.
    /// </summary>
    public static class MultiRobotDemo
    {
        public static void Run(bool printEveryTick)
        {
            var lot = ParkingLayoutBuilder.Build(new LayoutConfig { OccupiedLanes = 3 });
            var g = lot.Grid;
            var reservations = new ReservationTable();
            var liftTicks = new Dictionary<int, int>();
            const int maxTick = 600;

            // 우선순위 순: (시작, 대상 차량 앵커, 하차 앵커, 홈)
            // 순서 주의 — 우선순위 배정이 해의 존재를 좌우한다 (Prioritized Planning의 속성):
            // 로봇 4는 로봇 1이 (8,2)를 들어올린 뒤에만 (10,2)에 접근 가능하므로 로봇 1 다음,
            // 그러나 2·3보다는 먼저 계획해야 세 대가 레인을 쓸기 전에 슬롯을 확보한다
            // (마지막 순번이면 3레인 팔랑크스 앞에 갇혀 프런티어에서 퇴로가 없음 → 계획 실패).
            var missions = new (int Robot, (int X, int Y) Start, (int X, int Y) Target, (int X, int Y) Drop, (int X, int Y)? Home)[]
            {
                (1, (0, 2), (8, 2), (0, 2), (2, 4)),
                (4, (1, 3), (10, 2), (0, 2), null), // 하차 위치에 주차 (로봇 1이 비운 뒤)
                (2, (0, 4), (8, 4), (0, 4), (1, 4)),
                (3, (0, 3), (8, 3), (0, 3), (1, 3)),
            };

            var timelines = new List<RobotTimeline>();
            foreach (var m in missions)
            {
                var target = lot.Cars.First(c => c.X == m.Target.X && c.Y == m.Target.Y && c.InCorridor);
                var tl = CooperativePlanner.PlanCarryMission(
                    g, reservations, liftTicks, m.Robot, m.Start, target, m.Drop, m.Home, maxTick);
                if (tl == null)
                {
                    Console.WriteLine($"로봇 {m.Robot} 계획 실패 — 중단");
                    return;
                }
                timelines.Add(tl);
                Console.WriteLine(
                    $"로봇 {m.Robot}: 차량 {target.Id} @({target.X},{target.Y}) → ({m.Drop.X},{m.Drop.Y})" +
                    $" | 적재 t={tl.LiftTick}, 하차 t={tl.DropTick}, 종료 t={tl.EndTick}, 대기 {tl.WaitTicks}틱");
            }

            int endTick = timelines.Max(t => t.EndTick);
            Console.WriteLine($"\n계획 완료 — 전체 종료 t={endTick} ≈ {endTick * GridMap.SecondsPerCell:0}초");
            Console.WriteLine(TextRenderer.Legend + "\n");

            // 재생 + 매 틱 충돌 검증
            var carById = lot.Cars.ToDictionary(c => c.Id);
            int collisions = 0;
            for (int t = 0; t <= endTick; t++)
            {
                foreach (var kv in liftTicks)
                    if (kv.Value == t)
                        g.RemoveCar(carById[kv.Key]);

                var glyphs = new List<RobotGlyph>();
                var targetCarIds = new List<int>();
                foreach (var tl in timelines)
                {
                    var (x, y, carrying) = tl.At(t);
                    glyphs.Add(new RobotGlyph(tl.RobotId, x, y, carrying));
                    targetCarIds.Add(tl.TargetCarId);
                }

                collisions += Validate(g, glyphs, targetCarIds, t);

                if (printEveryTick || t % 5 == 0 || t == endTick)
                {
                    Console.WriteLine($"t={t}");
                    Console.WriteLine(TextRenderer.RenderGlyphs(lot, glyphs));
                }
            }

            int staged = timelines.Count;
            Console.WriteLine(collisions == 0
                ? $"검증 통과: {endTick + 1}틱 × 로봇 4대, 셀 충돌 0건 | 적치 S={staged}대"
                : $"!! 충돌 {collisions}건 발견");
        }

        /// <summary>로봇↔차량, 로봇↔로봇 셀 중복 검사. 충돌 수 반환.
        /// 예외: 자기 대상 차량과의 겹침은 도킹(적재 직전 밑으로 진입)이므로 허용.</summary>
        private static int Validate(GridMap g, List<RobotGlyph> glyphs, List<int> targetCarIds, int tick)
        {
            int collisions = 0;
            var occupied = new Dictionary<(int, int), int>();
            for (int i = 0; i < glyphs.Count; i++)
            {
                var r = glyphs[i];
                var cells = r.Carrying
                    ? new[] { (r.X, r.Y), r.SecondCell }
                    : new[] { (r.X, r.Y) };
                foreach (var cell in cells)
                {
                    int carId = g.CarAt(cell.Item1, cell.Item2);
                    if (carId != 0 && carId != targetCarIds[i])
                    {
                        Console.WriteLine($"!! t={tick}: 로봇 {r.Id} ↔ 차량 {carId} 충돌 @{cell}");
                        collisions++;
                    }
                    if (occupied.TryGetValue(cell, out int other))
                    {
                        Console.WriteLine($"!! t={tick}: 로봇 {r.Id} ↔ 로봇 {other} 충돌 @{cell}");
                        collisions++;
                    }
                    else
                    {
                        occupied[cell] = r.Id;
                    }
                }
            }
            return collisions;
        }
    }
}
