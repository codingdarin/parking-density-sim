using System;
using System.Linq;
using ParkingSim.Core.Emergency;
using ParkingSim.Core.Grid;

namespace ParkingSim.Scenarios
{
    /// <summary>
    /// D6: 화재 위치 → 확보 구간(+β) → 로컬 체이닝 재배치 → 확보 완료 판정을
    /// Core의 EmergencyPlanner + ClearanceEvaluator로 수행하고 결과를 출력한다.
    /// </summary>
    public static class EmergencyDemo
    {
        public static void Run(int occupiedLanes, double fireMeters, bool usePockets)
        {
            int[] pockets = usePockets ? new[] { 18, 28, 38 } : new int[0]; // 간격 25m
            var lot = ParkingLayoutBuilder.Build(new LayoutConfig
            {
                OccupiedLanes = occupiedLanes,
                StagingPocketXs = pockets,
            });

            var cfg = new EmergencyConfig { FireMeters = fireMeters };
            var plan = EmergencyPlanner.Plan(lot, cfg);

            Console.WriteLine(
                $"=== D6: 점유 {occupiedLanes}레인, 화재 {fireMeters}m, 확보 구간 x<{plan.SectionEndX}, " +
                $"대상 {plan.SectionCarCount}대, 포켓 {(usePockets ? "有" : "無")} ===");

            if (!plan.Success)
            {
                Console.WriteLine($"확보 실패: {plan.FailReason} | 계획 재시도 {plan.PlanFailures}회");
                return;
            }

            var report = ClearanceEvaluator.Evaluate(lot, plan);
            Console.WriteLine(
                $"확보 완료: t={report.ClearTick} ≈ {report.ClearSeconds:0}초 = {report.ClearSeconds / 60:0.0}분" +
                $" → 7분 판정: {(report.WithinBudget ? "✅ 충족" : "❌ 초과")}");
            Console.WriteLine(
                $"검증: 셀 충돌 {report.Collisions}건 | 계획 재시도 {plan.PlanFailures}회" +
                $" | 하차: 메인 {plan.MainDrops} / 포켓 {plan.PocketDrops} (S_필요 = {plan.SectionCarCount}대)");
            foreach (var sample in report.ConflictSamples)
                Console.WriteLine($"  !! {sample}");
            for (int r = 0; r < plan.Schedules.Length; r++)
            {
                var trips = plan.Schedules[r].Count(s => s.TargetCarId != 0);
                var waits = plan.Schedules[r].Sum(s => s.WaitTicks);
                Console.WriteLine($"  로봇 {r + 1}: {trips}대 운반, 대기 {waits}틱");
            }

            int n = plan.SectionCarCount;
            double envelope = n * ((plan.SectionEndX - lot.CorridorStartX) * GridMap.CellMeters + 30) / 4.0;
            Console.WriteLine($"봉투 예측(간섭 무시, 메인 적치 기준): ≈ {envelope:0}초");
        }
    }
}
