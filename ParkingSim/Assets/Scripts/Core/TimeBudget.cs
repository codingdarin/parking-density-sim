using ParkingSim.Core.Grid;

namespace ParkingSim.Core
{
    /// <summary>
    /// 5/7/9분 도착 시나리오의 시간 예산 단일 상수원 — 측정정의서 §2.
    /// 7분(420초)은 소방청 도착률 지표에 맞춘 대표 평가값이며,
    /// 법정 완료기준이나 화재의 물리 골든타임으로 표현하지 않는다.
    /// </summary>
    public static class TimeBudget
    {
        public const double FastArrivalSeconds = 300.0;
        public const double BaselineSeconds = 420.0;
        public const double SlowArrivalSeconds = 540.0;

        /// <summary>미보정 기준선(이동 1틱 = GridMap.SecondsPerCell)의 7분 예산 틱 = 168</summary>
        public const int BaselineTicks = (int)(BaselineSeconds / GridMap.SecondsPerCell);
    }
}
