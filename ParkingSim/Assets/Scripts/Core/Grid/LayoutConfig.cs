namespace ParkingSim.Core.Grid
{
    /// <summary>레이아웃 파라미터. 기본값은 측정정의서 §1의 모델 상수.</summary>
    public sealed class LayoutConfig
    {
        /// <summary>통로 길이(셀). 40셀 = 100m</summary>
        public int CorridorLengthCells { get; set; } = 40;

        /// <summary>통로 폭 = 레인 수. 3레인 = 7.5m</summary>
        public int CorridorLanes { get; set; } = 3;

        /// <summary>진입구↔적치 구역 거리 d_s(셀). 6셀 = 15m</summary>
        public int StagingDistanceCells { get; set; } = 6;

        /// <summary>통로 점유 레인 수 (0~3)</summary>
        public int OccupiedLanes { get; set; }

        /// <summary>적치 용량 S(대). 기본은 충분히 크게 — 필요량은 산출물로 기록 (측정정의서 §3)</summary>
        public int StagingCapacityCars { get; set; } = 999;
    }
}
