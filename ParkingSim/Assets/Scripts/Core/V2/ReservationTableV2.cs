using System.Collections.Generic;

namespace ParkingSim.Core.V2
{
    /// <summary>
    /// 표준 MAPF 예약 분리: 도착 시각의 정점 충돌과 반대방향 엣지 스왑만 차단한다.
    /// 앞 로봇이 떠난 셀로 같은 틱에 뒤 로봇이 추종하는 정상 이동은 허용한다.
    /// </summary>
    public sealed class ReservationTableV2
    {
        private readonly HashSet<(int X, int Y, int T)> _vertices =
            new HashSet<(int, int, int)>();
        private readonly HashSet<(int FromX, int FromY, int ToX, int ToY, int DepartureT)> _edges =
            new HashSet<(int, int, int, int, int)>();

        public void ReserveInitial(int x, int y, int tick)
        {
            _vertices.Add((x, y, tick));
        }

        public void ReserveMove(
            (int X, int Y) from, (int X, int Y) to, int departureTick)
        {
            _vertices.Add((from.X, from.Y, departureTick));
            _vertices.Add((to.X, to.Y, departureTick + 1));
            _edges.Add((from.X, from.Y, to.X, to.Y, departureTick));
        }

        public bool IsMoveFree(
            (int X, int Y) from, (int X, int Y) to, int departureTick)
        {
            int arrival = departureTick + 1;
            if (_vertices.Contains((to.X, to.Y, arrival))) return false;
            // 상대가 같은 틱에 to→from으로 움직이면 엣지 스왑.
            if (_edges.Contains((to.X, to.Y, from.X, from.Y, departureTick))) return false;
            return true;
        }
    }
}
