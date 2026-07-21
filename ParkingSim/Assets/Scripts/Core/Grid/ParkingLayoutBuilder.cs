using System;
using System.Collections.Generic;

namespace ParkingSim.Core.Grid
{
    /// <summary>생성된 주차장 인스턴스: 격자 + 차량 배치 + 기하 정보.</summary>
    public sealed class ParkingLot
    {
        public GridMap Grid { get; }
        public LayoutConfig Config { get; }
        public IReadOnlyList<Car> Cars { get; }

        /// <summary>통로 시작 x = 진입구 경계</summary>
        public int CorridorStartX { get; }

        /// <summary>통로 끝 x (exclusive)</summary>
        public int CorridorEndX { get; }

        /// <summary>통로 레인의 y 좌표 (위→아래)</summary>
        public IReadOnlyList<int> LaneYs { get; }

        public int StallCarCount { get; }
        public int CorridorCarCount { get; }

        /// <summary>통로변 적치 포켓의 x 위치들 (북측, 싱크 진입 셀은 통로에 인접한 y)</summary>
        public IReadOnlyList<int> PocketXs { get; }

        internal ParkingLot(
            GridMap grid, LayoutConfig config, List<Car> cars,
            int corridorStartX, int corridorEndX, int[] laneYs,
            int stallCarCount, int corridorCarCount, int[] pocketXs)
        {
            Grid = grid;
            Config = config;
            Cars = cars;
            CorridorStartX = corridorStartX;
            CorridorEndX = corridorEndX;
            LaneYs = laneYs;
            StallCarCount = stallCarCount;
            CorridorCarCount = corridorCarCount;
            PocketXs = pocketXs;
        }
    }

    /// <summary>
    /// 단일 통로 추상화 레이아웃 생성기.
    /// x축: [적치 싱크 2셀][도로 d_s][통로 40셀], y축: [북측 스톨 2][통로 레인 3][남측 스톨 2].
    /// 주차면은 만차로 채우고(만차 전제 — 측정정의서 §3), 통로는 점유 레인 수만큼 채운다.
    /// 점유 순서는 가장자리 레인부터, 가운데 레인 마지막 (2레인 점유 시 가운데가 통행로로 남도록).
    /// </summary>
    public static class ParkingLayoutBuilder
    {
        private const int StagingCells = 2;
        private const int StallDepth = 2;

        public static ParkingLot Build(LayoutConfig config)
        {
            if (config.OccupiedLanes < 0 || config.OccupiedLanes > config.CorridorLanes)
                throw new ArgumentOutOfRangeException(nameof(config),
                    $"점유 레인 수 {config.OccupiedLanes}는 0~{config.CorridorLanes} 범위여야 함");

            int corridorStartX = StagingCells + config.StagingDistanceCells;
            int corridorEndX = corridorStartX + config.CorridorLengthCells;
            int width = corridorEndX;
            int height = StallDepth + config.CorridorLanes + StallDepth;

            var grid = new GridMap(width, height);

            var laneYs = new int[config.CorridorLanes];
            for (int i = 0; i < config.CorridorLanes; i++)
                laneYs[i] = StallDepth + i;

            // 셀 타입 지정 (기본값 Outside)
            foreach (int y in laneYs)
            {
                for (int x = 0; x < StagingCells; x++)
                    grid.SetType(x, y, CellType.Staging);
                for (int x = StagingCells; x < corridorStartX; x++)
                    grid.SetType(x, y, CellType.Road);
                for (int x = corridorStartX; x < corridorEndX; x++)
                    grid.SetType(x, y, CellType.Corridor);
            }
            // 로봇 대기소(depot): 적치 블록 아래 4칸 — 운반 흐름(통로 행 y2~y4, 적치 앵커)과
            // 절대 겹치지 않는 홈. 대기 중인 로봇이 하차·운반 경로를 막는 문제를 구조적으로 차단
            int depotY = StallDepth + config.CorridorLanes;
            for (int x = 0; x < StagingCells; x++)
                for (int dy = 0; dy < StallDepth; dy++)
                    grid.SetType(x, depotY + dy, CellType.Road);

            var pocketSet = new HashSet<int>(config.StagingPocketXs ?? new int[0]);
            for (int x = corridorStartX; x < corridorEndX; x++)
            {
                for (int d = 0; d < StallDepth; d++)
                {
                    // 북측: 포켓 지정 x는 주차면 대신 적치 싱크
                    grid.SetType(x, d, pocketSet.Contains(x) ? CellType.Staging : CellType.Stall);
                    grid.SetType(x, StallDepth + config.CorridorLanes + d, CellType.Stall); // 남측
                }
            }

            // 차량 배치
            var cars = new List<Car>();
            int nextId = 1;

            // 주차면: 만차 (스톨 = 세로 1×2, 포켓 자리는 제외 — 포켓 1개 = 주차면 1면 희생)
            int southStallY = StallDepth + config.CorridorLanes;
            for (int x = corridorStartX; x < corridorEndX; x++)
            {
                if (!pocketSet.Contains(x))
                    AddCar(grid, cars, new Car(nextId++, x, 0, horizontal: false, inCorridor: false));
                AddCar(grid, cars, new Car(nextId++, x, southStallY, horizontal: false, inCorridor: false));
            }
            int stallCarCount = cars.Count;

            // 통로: 점유 레인 수만큼, 가장자리부터 (위, 아래, 가운데 순)
            foreach (int laneY in OccupancyOrder(laneYs, config.OccupiedLanes))
            {
                for (int x = corridorStartX; x + 1 < corridorEndX; x += 2)
                    AddCar(grid, cars, new Car(nextId++, x, laneY, horizontal: true, inCorridor: true));
            }
            int corridorCarCount = cars.Count - stallCarCount;

            var sortedPockets = new List<int>(pocketSet);
            sortedPockets.Sort();
            return new ParkingLot(grid, config, cars, corridorStartX, corridorEndX, laneYs,
                stallCarCount, corridorCarCount, sortedPockets.ToArray());
        }

        private static void AddCar(GridMap grid, List<Car> cars, Car car)
        {
            grid.PlaceCar(car);
            cars.Add(car);
        }

        private static IEnumerable<int> OccupancyOrder(int[] laneYs, int occupiedLanes)
        {
            var order = new List<int>();
            int top = 0, bottom = laneYs.Length - 1;
            while (top <= bottom)
            {
                order.Add(laneYs[top]);
                if (bottom != top) order.Add(laneYs[bottom]);
                top++;
                bottom--;
            }
            // 위 루프는 [위, 아래, 그다음 위, ...] 순 — 가운데가 항상 마지막
            return order.GetRange(0, occupiedLanes);
        }
    }
}
