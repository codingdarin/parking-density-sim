using System.Collections.Generic;
using System.Linq;
using ParkingSim.Core.Grid;
using ParkingSim.Core.Pathfinding;

namespace ParkingSim.Tests
{
    /// <summary>적대적 테스트용 소형 격자·계획·검증 유틸.</summary>
    public static class TestSupport
    {
        private static readonly (int Dx, int Dy)[] OneCell = { (0, 0) };

        /// <summary>지정 셀만 주행 가능(Road)한 격자</summary>
        public static GridMap SparseGrid(int width, int height, IEnumerable<(int X, int Y)> roadCells)
        {
            var g = new GridMap(width, height);
            foreach (var (x, y) in roadCells)
                g.SetType(x, y, CellType.Road);
            return g;
        }

        /// <summary>전체가 주행 가능한 열린 방</summary>
        public static GridMap RoomGrid(int width, int height)
        {
            var g = new GridMap(width, height);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    g.SetType(x, y, CellType.Road);
            return g;
        }

        /// <summary>1셀 로봇 계획 + 예약 기록. 실패 시 null (예약 미기록).</summary>
        public static List<(int X, int Y)> PlanAndReserve(
            GridMap g, ReservationTable rt, (int X, int Y) start, (int X, int Y) goal,
            int maxTick = 200, bool parkAtGoal = true, int maxExpansions = 200000)
        {
            var path = SpaceTimeAStar.FindPath(start, 0, goal, OneCell,
                (x, y, t) => Drivable(g, x, y) && rt.IsFree(x, y, t), maxTick, maxExpansions);
            if (path == null) return null;

            for (int t = 0; t < path.Count; t++)
                rt.ReserveStep(path[t].X, path[t].Y, t);
            if (parkAtGoal)
                rt.ReserveFrom(goal.X, goal.Y, path.Count - 1);
            return path;
        }

        public static bool Drivable(GridMap g, int x, int y)
        {
            if (!g.InBounds(x, y)) return false;
            var t = g.TypeAt(x, y);
            return t != CellType.Outside && t != CellType.Stall;
        }

        /// <summary>정점 충돌(같은 틱 같은 셀)과 스왑(자리 맞교환)을 명시적으로 계수.</summary>
        public static (int Vertex, int Swap) CountConflicts(IReadOnlyList<List<(int X, int Y)>> paths)
        {
            int end = paths.Max(p => p.Count);
            int vertex = 0, swap = 0;

            (int X, int Y) At(List<(int X, int Y)> p, int t) => t < p.Count ? p[t] : p[p.Count - 1];

            for (int t = 0; t < end; t++)
            {
                for (int i = 0; i < paths.Count; i++)
                {
                    for (int j = i + 1; j < paths.Count; j++)
                    {
                        var a = At(paths[i], t);
                        var b = At(paths[j], t);
                        if (a.X == b.X && a.Y == b.Y) vertex++;
                        if (t > 0)
                        {
                            var aPrev = At(paths[i], t - 1);
                            var bPrev = At(paths[j], t - 1);
                            bool moved = a.X != aPrev.X || a.Y != aPrev.Y;
                            if (moved && a.X == bPrev.X && a.Y == bPrev.Y &&
                                b.X == aPrev.X && b.Y == aPrev.Y)
                                swap++;
                        }
                    }
                }
            }
            return (vertex, swap);
        }
    }
}
