namespace ParkingSim.Core.Grid
{
    public enum CellType : byte
    {
        Outside = 0,

        /// <summary>일반 주차면 (만차 전제)</summary>
        Stall,

        /// <summary>통로 레인 — 평시 점유 대상이자 비상시 확보 대상</summary>
        Corridor,

        /// <summary>진입구↔적치 구역 사이 도로 (d_s 구간)</summary>
        Road,

        /// <summary>임시 적치 구역 입구 — 차량이 도달하면 격자에서 빠지고 S 카운터로 집계되는 싱크</summary>
        Staging,
    }
}
