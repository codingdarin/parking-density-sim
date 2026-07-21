namespace ParkingSim.Core.Grid
{
    /// <summary>
    /// 차량 1대 = 1×2셀 강체. (X,Y)가 앵커이며 Horizontal이면 (X+1,Y), 아니면 (X,Y+1)까지 차지한다.
    /// 통로 위 차량은 통로와 평행(Horizontal), 주차면 차량은 수직(스톨 깊이 방향).
    /// </summary>
    public readonly struct Car
    {
        public int Id { get; }
        public int X { get; }
        public int Y { get; }
        public bool Horizontal { get; }
        public bool InCorridor { get; }

        public Car(int id, int x, int y, bool horizontal, bool inCorridor)
        {
            Id = id;
            X = x;
            Y = y;
            Horizontal = horizontal;
            InCorridor = inCorridor;
        }

        public (int X, int Y) SecondCell => Horizontal ? (X + 1, Y) : (X, Y + 1);
    }
}
