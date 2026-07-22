using System.Globalization;
using System.Text;

namespace ParkingSim.Core.Metrics
{
    /// <summary>
    /// 지표 → CSV 문자열 서식만 담당 (파일 IO 없음 — 호스트가 기록). 모든 수치는
    /// InvariantCulture로 직렬화해 로케일 무관 재현성을 보장한다 (CLAUDE.md §3).
    /// </summary>
    public static class CsvFormat
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // ── 비상 실행 (D9 배치) ──
        public static string EmergencyHeader() =>
            "lanes,fire_m,pockets,robots,seed,beta_cells,success,within_budget," +
            "clear_tick,clear_s,clear_min,s_needed,attempts,main_drops,pocket_drops,collisions," +
            "makespan_tick,utilization,effective_p,envelope_s,deviation,fail_reason";

        public static string EmergencyRow(RunMetrics m)
        {
            var sb = new StringBuilder();
            Add(sb, m.OccupiedLanes);
            Add(sb, F(m.FireMeters, 1));
            Add(sb, m.PocketCount);
            Add(sb, m.RobotCount);
            Add(sb, m.Seed);
            Add(sb, m.BetaCells);
            Add(sb, m.Success ? 1 : 0);
            Add(sb, m.WithinBudget ? 1 : 0);
            Add(sb, m.ClearTick);
            Add(sb, F(m.ClearSeconds, 1));
            Add(sb, F(m.ClearMinutes, 2));
            Add(sb, m.SectionCarCount);
            Add(sb, m.Attempts);
            Add(sb, m.MainDrops);
            Add(sb, m.PocketDrops);
            Add(sb, m.Collisions);
            Add(sb, m.MakespanTicks);
            Add(sb, F(m.Utilization, 3));
            Add(sb, F(m.EffectiveP, 3));
            Add(sb, F(m.EnvelopeSeconds, 1));
            Add(sb, F(m.DeviationRatio, 3));
            sb.Append(Quote(m.FailReason));
            return sb.ToString();
        }

        // ── 평시 출차 ──
        public static string NormalHeader() =>
            "lanes,robots,seed,requested,completed,failed," +
            "avg_exit_tick,avg_exit_s,max_exit_tick,makespan_tick,utilization,throughput_per_min";

        public static string NormalRow(NormalOpsMetrics m)
        {
            var sb = new StringBuilder();
            Add(sb, m.OccupiedLanes);
            Add(sb, m.RobotCount);
            Add(sb, m.Seed);
            Add(sb, m.Requested);
            Add(sb, m.Completed);
            Add(sb, m.Failed);
            Add(sb, F(m.AvgExitTicks, 2));
            Add(sb, F(m.AvgExitSeconds, 1));
            Add(sb, m.MaxExitTicks);
            Add(sb, m.MakespanTicks);
            Add(sb, F(m.Utilization, 3));
            sb.Append(F(m.ThroughputPerMin, 2));
            return sb.ToString();
        }

        private static void Add(StringBuilder sb, int v) => sb.Append(v.ToString(Inv)).Append(',');
        private static void Add(StringBuilder sb, string v) => sb.Append(v).Append(',');
        private static string F(double v, int digits) => v.ToString("F" + digits, Inv);

        private static string Quote(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.IndexOf(',') < 0 && s.IndexOf('"') < 0) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
    }
}
