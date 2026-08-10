using System.Collections.Generic;

namespace ParkingSim.Core.Pathfinding
{
    /// <summary>
    /// 시공간 예약 테이블. 이동 주체는 각 위치를 [t, t+1] 두 틱에 걸쳐 예약한다 —
    /// 다음 틱까지 겹쳐 잡음으로써 스왑(자리 교환)과 직후 추종을 정점 예약만으로 차단한다.
    /// (보수적: 앞차가 떠나는 셀에 같은 틱에 진입하는 것도 금지됨)
    /// </summary>
    public sealed class ReservationTable
    {
        private readonly HashSet<(int X, int Y, int T)> _cells = new HashSet<(int, int, int)>();
        private readonly Dictionary<(int X, int Y), int> _permanentFrom = new Dictionary<(int, int), int>();

        public bool IsFree(int x, int y, int t)
        {
            if (_permanentFrom.TryGetValue((x, y), out int from) && t >= from) return false;
            return !_cells.Contains((x, y, t));
        }

        /// <summary>t와 t+1 두 틱 예약 (이동·정지 공통)</summary>
        public void ReserveStep(int x, int y, int t)
        {
            _cells.Add((x, y, t));
            _cells.Add((x, y, t + 1));
        }

        /// <summary>t 이후 영구 예약 (임무 종료 후 주차)</summary>
        public void ReserveFrom(int x, int y, int t)
        {
            if (_permanentFrom.TryGetValue((x, y), out int cur) && cur <= t) return;
            _permanentFrom[(x, y)] = t;
        }

        /// <summary>
        /// 영구 예약 해제 — 주차 중인 로봇이 다음 미션으로 재출발할 때 사용 (미션 체이닝).
        /// 해제 후 유휴 구간은 호출부가 ReserveStep으로 유한 예약해야 한다.
        /// (해제 전에 계획된 타 미션은 이 셀을 회피했으므로 보수적이지만 여전히 유효)
        /// </summary>
        public void ReleasePermanent(int x, int y) => _permanentFrom.Remove((x, y));
    }
}
