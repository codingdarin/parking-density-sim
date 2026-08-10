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
            var liftTicks = new Dictionary<int, int>();
            const int maxTick = 600;

            // 미션은 자연 순서(1,2,3,4)로 정의 — 우선순위 배정은 MissionPlanner가 결정론적으로 처리:
            // 기본 순서 실패 시 시드 순열 재시도. (수동 순서 조정 금지 — D9 스윕에서 재현 불가)
            // 참고: 자연 순서는 로봇 4가 3레인 팔랑크스에 갇혀 실패함이 알려져 있음 → 재시도 검증 겸용.
            Car FindCar(int x, int y) => lot.Cars.First(c => c.X == x && c.Y == y && c.InCorridor);
            var missions = new List<CarryMission>
            {
                new CarryMission(1, (0, 2), FindCar(8, 2), (0, 2), (2, 4)),
                new CarryMission(2, (0, 4), FindCar(8, 4), (0, 4), (1, 4)),
                new CarryMission(3, (0, 3), FindCar(8, 3), (0, 3), (1, 3)),
                new CarryMission(4, (1, 3), FindCar(10, 2), (0, 2), null), // 하차 위치에 주차
            };

            var plan = MissionPlanner.TryPlanAll(g, missions, seed: 42, maxAttempts: 24, maxTick);
            if (plan == null)
            {
                Console.WriteLine("함대 계획 실패 (24회 순열 소진) — 확보 실패로 기록될 상황");
                return;
            }
            var timelines = plan.Timelines.ToList();
            foreach (var kv in plan.CarLiftTicks) liftTicks[kv.Key] = kv.Value;

            Console.WriteLine($"함대 계획: {plan.Attempts}회 시도, 채택 순서 = [{string.Join(",", plan.OrderUsed.Select(i => missions[i].RobotId))}]");
            foreach (var tl in timelines)
                Console.WriteLine(
                    $"로봇 {tl.RobotId}: 차량 {tl.TargetCarId}" +
                    $" | 적재 t={tl.LiftTick}, 하차 t={tl.DropTick}, 종료 t={tl.EndTick}, 대기 {tl.WaitTicks}틱");

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
