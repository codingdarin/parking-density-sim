using System.Collections.Generic;

namespace ParkingSim.Core.Pathfinding
{
    /// <summary>
    /// (x, y, t) 시공간 A*. 행동 = 4방향 이동 + 제자리 대기, 비용 = 1틱.
    /// 풋프린트 오프셋 배열로 빈 몸(1셀)과 적재(1×2 강체)를 같은 탐색으로 처리한다.
    /// canOccupy가 정적 지형·시간 종속 차량 차단·예약 테이블을 모두 판정해야 한다.
    /// </summary>
    public static class SpaceTimeAStar
    {
        public delegate bool CanOccupy(int x, int y, int t);

        private static readonly (int Dx, int Dy)[] Actions =
            { (0, 0), (1, 0), (-1, 0), (0, 1), (0, -1) }; // 대기 우선 → 결정적 순서

        /// <summary>
        /// startTick의 start에서 goal(앵커 기준, 도착 시각 무관)까지의 위치 열.
        /// 반환 리스트의 인덱스 i = startTick + i 틱의 앵커 위치. 도달 불가면 null.
        /// </summary>
        public static List<(int X, int Y)> FindPath(
            (int X, int Y) start, int startTick, (int X, int Y) goal,
            (int Dx, int Dy)[] footprint, CanOccupy canOccupy,
            int maxTick, int maxExpansions = 200000)
        {
            var open = new MinHeap();
            var gScore = new Dictionary<(int, int, int), int>();
            var cameFrom = new Dictionary<(int, int, int), (int, int, int)>();
            var closed = new HashSet<(int, int, int)>();

            var startState = (start.X, start.Y, startTick);
            gScore[startState] = 0;
            open.Push(Manhattan(start.X, start.Y, goal), 0, startState);
            int expansions = 0;

            while (open.Count > 0 && expansions++ < maxExpansions)
            {
                var current = open.Pop();
                var (x, y, t) = current;
                if (x == goal.X && y == goal.Y)
                    return Reconstruct(cameFrom, current, startTick);
                if (!closed.Add(current) || t >= maxTick) continue;

                foreach (var (dx, dy) in Actions)
                {
                    var next = (X: x + dx, Y: y + dy, T: t + 1);
                    bool ok = true;
                    foreach (var (fx, fy) in footprint)
                    {
                        if (!canOccupy(next.X + fx, next.Y + fy, next.T)) { ok = false; break; }
                    }
                    if (!ok) continue;

                    int tentative = gScore[(x, y, t)] + 1;
                    if (!gScore.TryGetValue(next, out int prev) || tentative < prev)
                    {
                        gScore[next] = tentative;
                        cameFrom[next] = (x, y, t);
                        int h = Manhattan(next.X, next.Y, goal);
                        open.Push(tentative + h, h, next);
                    }
                }
            }
            return null;
        }

        private static int Manhattan(int x, int y, (int X, int Y) goal)
        {
            int dx = x - goal.X, dy = y - goal.Y;
            return (dx < 0 ? -dx : dx) + (dy < 0 ? -dy : dy);
        }

        private static List<(int X, int Y)> Reconstruct(
            Dictionary<(int, int, int), (int, int, int)> cameFrom,
            (int X, int Y, int T) end, int startTick)
        {
            var states = new List<(int X, int Y, int T)> { end };
            while (cameFrom.TryGetValue(states[states.Count - 1], out var prev))
                states.Add(prev);
            states.Reverse();

            var path = new List<(int X, int Y)>(states.Count);
            foreach (var (x, y, _) in states)
                path.Add((x, y));
            return path;
        }

        /// <summary>(f, h, 삽입순) 비교 이진 힙 — 결정적 타이브레이크. (Unity netstandard2.1에는 PriorityQueue가 없음)</summary>
        private sealed class MinHeap
        {
            private readonly List<(int F, int H, int Seq, (int X, int Y, int T) State)> _items =
                new List<(int, int, int, (int, int, int))>();
            private int _seq;

            public int Count => _items.Count;

            public void Push(int f, int h, (int X, int Y, int T) state)
            {
                _items.Add((f, h, _seq++, state));
                int i = _items.Count - 1;
                while (i > 0)
                {
                    int parent = (i - 1) / 2;
                    if (Less(_items[i], _items[parent]))
                    {
                        (_items[i], _items[parent]) = (_items[parent], _items[i]);
                        i = parent;
                    }
                    else break;
                }
            }

            public (int X, int Y, int T) Pop()
            {
                var top = _items[0].State;
                var last = _items[_items.Count - 1];
                _items.RemoveAt(_items.Count - 1);
                if (_items.Count > 0)
                {
                    _items[0] = last;
                    int i = 0;
                    while (true)
                    {
                        int l = 2 * i + 1, r = 2 * i + 2, smallest = i;
                        if (l < _items.Count && Less(_items[l], _items[smallest])) smallest = l;
                        if (r < _items.Count && Less(_items[r], _items[smallest])) smallest = r;
                        if (smallest == i) break;
                        (_items[i], _items[smallest]) = (_items[smallest], _items[i]);
                        i = smallest;
                    }
                }
                return top;
            }

            private static bool Less(
                (int F, int H, int Seq, (int X, int Y, int T) State) a,
                (int F, int H, int Seq, (int X, int Y, int T) State) b)
            {
                if (a.F != b.F) return a.F < b.F;
                if (a.H != b.H) return a.H < b.H;
                return a.Seq < b.Seq;
            }
        }
    }
}
