namespace ParkingSim.Core.Agents
{
    /// <summary>렌더링·검증용 로봇 스냅샷 (틱 하나의 상태).</summary>
    public readonly struct RobotGlyph
    {
        public int Id { get; }
        public int X { get; }
        public int Y { get; }
        public bool Carrying { get; }
        public bool Horizontal { get; }

        public RobotGlyph(int id, int x, int y, bool carrying, bool horizontal = true)
        {
            Id = id;
            X = x;
            Y = y;
            Carrying = carrying;
            Horizontal = horizontal;
        }

        public (int X, int Y) SecondCell => Horizontal ? (X + 1, Y) : (X, Y + 1);
    }
}
