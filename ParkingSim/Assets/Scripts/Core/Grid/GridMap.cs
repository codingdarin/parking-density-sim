using System;

namespace ParkingSim.Core.Grid
{
    /// <summary>
    /// 격자 지도. 셀 타입(정적)과 차량 점유(동적)를 보관한다.
    /// 1셀 = 2.5m (측정정의서 §1). 시간·거리의 초/미터 환산은 표시 단계에서만 한다.
    /// </summary>
    public sealed class GridMap
    {
        public const double CellMeters = 2.5;
        public const double SecondsPerCell = 2.5; // 로봇 1 m/s

        public int Width { get; }
        public int Height { get; }

        private readonly CellType[] _types;
        private readonly int[] _carIds; // 0 = 빈 셀

        public GridMap(int width, int height)
        {
            if (width <= 0 || height <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), "격자 크기는 양수여야 함");
            Width = width;
            Height = height;
            _types = new CellType[width * height];
            _carIds = new int[width * height];
        }

        public bool InBounds(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;

        private int Idx(int x, int y)
        {
            if (!InBounds(x, y))
                throw new ArgumentOutOfRangeException($"({x},{y})는 격자 밖 (크기 {Width}×{Height})");
            return y * Width + x;
        }

        public CellType TypeAt(int x, int y) => _types[Idx(x, y)];
        public void SetType(int x, int y, CellType type) => _types[Idx(x, y)] = type;

        public int CarAt(int x, int y) => _carIds[Idx(x, y)];
        public bool IsOccupied(int x, int y) => _carIds[Idx(x, y)] != 0;

        public void PlaceCar(Car car)
        {
            var (x2, y2) = car.SecondCell;
            if (IsOccupied(car.X, car.Y) || IsOccupied(x2, y2))
                throw new InvalidOperationException($"셀 점유 충돌: car {car.Id} at ({car.X},{car.Y})");
            _carIds[Idx(car.X, car.Y)] = car.Id;
            _carIds[Idx(x2, y2)] = car.Id;
        }

        public void RemoveCar(Car car)
        {
            var (x2, y2) = car.SecondCell;
            _carIds[Idx(car.X, car.Y)] = 0;
            _carIds[Idx(x2, y2)] = 0;
        }
    }
}
