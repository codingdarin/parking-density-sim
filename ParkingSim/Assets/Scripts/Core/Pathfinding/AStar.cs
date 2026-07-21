using System.Collections.Generic;

namespace ParkingSim.Core.Pathfinding
{
    /// <summary>
    /// 4방향 격자 A* (정적 장애물). D3 시공간 A*의 기준 구현이자 단일 이동 계획용.
    /// 통과 가능 여부는 델리게이트로 주입 — 빈 몸(1셀)과 적재(1×2 풋프린트)를 같은 코드로 처리한다.
    /// 격자가 작으므로(수백 셀) 오픈 리스트는 선형 탐색 — 결정적 타이브레이크(h 낮은 쪽) 포함.
    /// </summary>
    public static class AStar
    {
        public delegate bool Passable(int x, int y);

        private static readonly (int Dx, int Dy)[] Dirs = { (1, 0), (-1, 0), (0, 1), (0, -1) };

        /// <summary>시작·목표 포함 경로. 도달 불가면 null.</summary>
        public static List<(int X, int Y)> FindPath(
            int width, int height, (int X, int Y) start, (int X, int Y) goal, Passable passable)
        {
            if (start.X == goal.X && start.Y == goal.Y)
                return new List<(int X, int Y)> { start };

            var gScore = new Dictionary<(int, int), int> { [start] = 0 };
            var cameFrom = new Dictionary<(int, int), (int, int)>();
            var open = new List<(int X, int Y)> { start };
            var closed = new HashSet<(int, int)>();

            while (open.Count > 0)
            {
                int best = 0, bestF = int.MaxValue, bestH = int.MaxValue;
                for (int i = 0; i < open.Count; i++)
                {
                    int h = Manhattan(open[i], goal);
                    int f = gScore[open[i]] + h;
                    if (f < bestF || (f == bestF && h < bestH))
                    {
                        bestF = f;
                        bestH = h;
                        best = i;
                    }
                }

                var current = open[best];
                open.RemoveAt(best);
                if (current.X == goal.X && current.Y == goal.Y)
                    return Reconstruct(cameFrom, current);
                closed.Add(current);

                foreach (var (dx, dy) in Dirs)
                {
                    var next = (X: current.X + dx, Y: current.Y + dy);
                    if (next.X < 0 || next.X >= width || next.Y < 0 || next.Y >= height) continue;
                    if (closed.Contains(next)) continue;
                    if (!passable(next.X, next.Y)) continue;

                    int tentative = gScore[current] + 1;
                    if (!gScore.TryGetValue(next, out int prev) || tentative < prev)
                    {
                        gScore[next] = tentative;
                        cameFrom[next] = current;
                        if (!open.Contains(next)) open.Add(next);
                    }
                }
            }
            return null;
        }

        private static int Manhattan((int X, int Y) a, (int X, int Y) b)
        {
            int dx = a.X - b.X, dy = a.Y - b.Y;
            return (dx < 0 ? -dx : dx) + (dy < 0 ? -dy : dy);
        }

        private static List<(int X, int Y)> Reconstruct(
            Dictionary<(int, int), (int, int)> cameFrom, (int X, int Y) end)
        {
            var path = new List<(int X, int Y)> { end };
            while (cameFrom.TryGetValue(path[path.Count - 1], out var prev))
                path.Add(prev);
            path.Reverse();
            return path;
        }
    }
}
