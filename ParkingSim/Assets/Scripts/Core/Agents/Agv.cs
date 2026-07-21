using System;
using ParkingSim.Core.Grid;

namespace ParkingSim.Core.Agents
{
    /// <summary>
    /// AGV 1대. 빈 몸은 1셀, 적재 시 차량 풋프린트(1×2)를 그대로 차지하는 강체가 된다.
    /// 적재는 차량 앵커 셀 위에서만 가능 (앵커 일치 → 이동 계산 단순화).
    /// </summary>
    public sealed class Agv
    {
        public int Id { get; }
        public int X { get; private set; }
        public int Y { get; private set; }
        public int CarriedCarId { get; private set; }
        public bool CarriedHorizontal { get; private set; }
        public bool IsCarrying => CarriedCarId != 0;

        public Agv(int id, int x, int y)
        {
            Id = id;
            X = x;
            Y = y;
        }

        public void MoveTo(int x, int y)
        {
            X = x;
            Y = y;
        }

        public void PickUp(Car car)
        {
            if (IsCarrying)
                throw new InvalidOperationException($"AGV {Id}는 이미 적재 중");
            if (car.X != X || car.Y != Y)
                throw new InvalidOperationException(
                    $"AGV {Id} ({X},{Y})는 차량 {car.Id} 앵커 ({car.X},{car.Y}) 위에 있지 않음");
            CarriedCarId = car.Id;
            CarriedHorizontal = car.Horizontal;
        }

        public int Drop()
        {
            int id = CarriedCarId;
            CarriedCarId = 0;
            return id;
        }

        /// <summary>적재 중일 때 차지하는 두 번째 셀</summary>
        public (int X, int Y) SecondCell => CarriedHorizontal ? (X + 1, Y) : (X, Y + 1);
    }
}
