using System.Collections.Generic;
using System.Text;
using ParkingSim.Core.Agents;

namespace ParkingSim.Core.Grid
{
    /// <summary>격자 상태를 콘솔용 텍스트로 렌더링 — D1~D4 디버깅의 주 시각화 수단.</summary>
    public static class TextRenderer
    {
        public static string Render(ParkingLot lot) => Render(lot, null);

        public static string Render(ParkingLot lot, IReadOnlyList<Agv> agvs)
        {
            var g = lot.Grid;
            var robotCells = RobotCells(agvs);
            var sb = new StringBuilder();

            // x 눈금 (10셀 = 25m 단위)
            for (int x = 0; x < g.Width; x++)
                sb.Append(x % 10 == 0 ? (char)('0' + x / 10 % 10) : ' ');
            sb.AppendLine();

            for (int y = 0; y < g.Height; y++)
            {
                for (int x = 0; x < g.Width; x++)
                {
                    if (robotCells.TryGetValue((x, y), out char rc))
                        sb.Append(rc);
                    else
                        sb.Append(CellChar(g, x, y));
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }

        public static string Legend =>
            "범례: c=주차면 차량  C=통로 차량  r=로봇(빈 몸)  R=로봇(적재, 2셀)  .=빈 통행 셀  S=적치 구역(싱크)  (공백)=외부";

        private static Dictionary<(int, int), char> RobotCells(IReadOnlyList<Agv> agvs)
        {
            var cells = new Dictionary<(int, int), char>();
            if (agvs == null) return cells;
            foreach (var agv in agvs)
            {
                if (agv.IsCarrying)
                {
                    cells[(agv.X, agv.Y)] = 'R';
                    cells[(agv.SecondCell.X, agv.SecondCell.Y)] = 'R';
                }
                else
                {
                    cells[(agv.X, agv.Y)] = 'r';
                }
            }
            return cells;
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
