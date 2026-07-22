using System;
using System.Collections.Generic;
using System.Linq;
using ParkingSim.Core.Agents;
using ParkingSim.Core.Grid;
using ParkingSim.Core.Pathfinding;

namespace ParkingSim.Core.Metrics
{
    public sealed class NormalOpsMetrics
    {
        public int OccupiedLanes { get; set; }
        public int RobotCount { get; set; }
        public int Seed { get; set; }
        public int Requested { get; set; }
        public int Completed { get; set; }
        public int Failed { get; set; }
        public double AvgExitTicks { get; set; }
        public double AvgExitSeconds => AvgExitTicks * GridMap.SecondsPerCell;
        public int MaxExitTicks { get; set; }
        public int MakespanTicks { get; set; }
        public double Utilization { get; set; }
        public double ThroughputPerMin { get; set; }
    }

    /// <summary>
    /// 평시 출차 시뮬 (최소 구현). 수요 = 통로 수평 차량의 인출 요청 (PlanCarryMission의 1×2
    /// 수평 강체와 정합). 시드 주입 System.Random으로 요청 시각을 발생시키고, 가용 최소 로봇부터
    /// 서쪽 출구 앵커(0, laneY)로 인출·홈 복귀시킨다. 지표: 평균 출차 시간·가동률·유효 병렬성.
    /// 재현성: 같은 (레인·로봇·시드·요청수·도착창) → 같은 결과 (CLAUDE.md §3).
    /// </summary>
    public static class NormalOpsSim
    {
        public static NormalOpsMetrics Run(
            int occupiedLanes, int robotCount, int seed, int requestCount, int arrivalWindowTicks,
            int maxTick = 4000)
        {
            var lot = ParkingLayoutBuilder.Build(new LayoutConfig { OccupiedLanes = occupiedLanes });
            var g = lot.Grid;
            var rng = new Random(seed);

            robotCount = Math.Max(1, Math.Min(robotCount, lot.DepotCells.Count));
            var homes = lot.DepotCells.Take(robotCount).ToArray();

            // 요청 대상: 통로 차량 중 requestCount대 (0 이하·초과면 전량), 시드 셔플로 선택
            var corridorCars = lot.Cars.Where(c => c.InCorridor).ToList();
            Shuffle(corridorCars, rng);
            int n = requestCount <= 0 || requestCount > corridorCars.Count ? corridorCars.Count : requestCount;
            var requests = corridorCars.Take(n)
                .Select(car => (Car: car, Tick: rng.Next(0, Math.Max(1, arrivalWindowTicks))))
                .OrderBy(r => r.Tick).ThenBy(r => r.Car.Id)
                .ToList();

            var rt = new ReservationTable();
            var liftTicks = new Dictionary<int, int>();
            var schedules = new List<RobotTimeline>[robotCount];
            var avail = new int[robotCount];
            for (int r = 0; r < robotCount; r++)
            {
                schedules[r] = new List<RobotTimeline>();
                rt.ReserveFrom(homes[r].X, homes[r].Y, 0);
            }

            var exitTicks = new List<int>();
            int failed = 0;

            foreach (var (car, reqTick) in requests)
            {
                int robot = Enumerable.Range(0, robotCount)
                    .OrderBy(i => Math.Max(reqTick, avail[i])).ThenBy(i => i).First();
                int startTick = Math.Max(reqTick, avail[robot]);
                var drop = (X: 0, Y: car.Y); // 서쪽 출구 앵커 (해당 레인)

                rt.ReleasePermanent(homes[robot].X, homes[robot].Y);
                var tl = CooperativePlanner.PlanCarryMission(
                    g, rt, liftTicks, robot + 1, homes[robot], car, drop,
                    home: homes[robot], maxTick, startTick, parkAtEnd: true);
                if (tl == null)
                {
                    rt.ReserveFrom(homes[robot].X, homes[robot].Y, startTick);
                    failed++;
                    continue;
                }

                schedules[robot].Add(tl);
                avail[robot] = tl.EndTick;
                exitTicks.Add(tl.DropTick - reqTick);
            }

            int makespan = avail.DefaultIfEmpty(0).Max();
            long totalWork = 0;
            foreach (var timelines in schedules)
                foreach (var t in timelines)
                    totalWork += (t.Steps.Count - 1) - t.WaitTicks;

            var m = new NormalOpsMetrics
            {
                OccupiedLanes = occupiedLanes,
                RobotCount = robotCount,
                Seed = seed,
                Requested = requests.Count,
                Completed = exitTicks.Count,
                Failed = failed,
                MaxExitTicks = exitTicks.DefaultIfEmpty(0).Max(),
                AvgExitTicks = exitTicks.Count > 0 ? exitTicks.Average() : 0,
                MakespanTicks = makespan,
                Utilization = makespan > 0 ? (double)totalWork / (robotCount * makespan) : 0,
            };
            double minutes = makespan * GridMap.SecondsPerCell / 60.0;
            m.ThroughputPerMin = minutes > 0 ? m.Completed / minutes : 0;
            return m;
        }

        private static void Shuffle<T>(IList<T> list, Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
