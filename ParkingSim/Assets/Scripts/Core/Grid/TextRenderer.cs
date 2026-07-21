using System.Text;

namespace ParkingSim.Core.Grid
{
    /// <summary>격자 상태를 콘솔용 텍스트로 렌더링 — D1~D4 디버깅의 주 시각화 수단.</summary>
    public static class TextRenderer
    {
        public static string Render(ParkingLot lot)
        {
            var g = lot.Grid;
            var sb = new StringBuilder();

            // x 눈금 (10셀 = 25m 단위)
            sb.Append(' ', 0);
            for (int x = 0; x < g.Width; x++)
                sb.Append(x % 10 == 0 ? (char)('0' + x / 10 % 10) : ' ');
            sb.AppendLine();

            for (int y = 0; y < g.Height; y++)
            {
                for (int x = 0; x < g.Width; x++)
                    sb.Append(CellChar(g, x, y));
                sb.AppendLine();
            }

            // 진입구 표시
            sb.Append(' ', lot.CorridorStartX);
            sb.Append('^');
            sb.AppendLine($" 진입구 x={lot.CorridorStartX} (통로 {lot.CorridorEndX - lot.CorridorStartX}셀 = {(lot.CorridorEndX - lot.CorridorStartX) * GridMap.CellMeters}m)");
            sb.AppendLine("범례: c=주차면 차량  C=통로 차량  .=빈 통행 셀  S=적치 구역(싱크)  (공백)=외부");
            return sb.ToString();
        }

        private static char CellChar(GridMap g, int x, int y)
        {
            bool occupied = g.IsOccupied(x, y);
            switch (g.TypeAt(x, y))
            {
                case CellType.Stall: return occupied ? 'c' : '_';
                case CellType.Corridor: return occupied ? 'C' : '.';
                case CellType.Road: return occupied ? 'C' : '.';
                case CellType.Staging: return 'S';
                default: return ' ';
            }
        }
    }
}
