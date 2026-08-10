using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ParkingSim.Core;
using ParkingSim.Core.V2;

namespace ParkingSim.Scenarios
{
    /// <summary>
    /// 무교란 안전 최대 밀도(N=15)에서 t=0 교란(도로 봉쇄 2셀 나무·운송 유닛 감소)을
    /// 스윕해 회복탄력성을 집계한다. 프로파일·입구 조건은 v2complexdensity와 동일.
    /// </summary>
    public static class V2DisturbanceSweepDemo
    {
        /// <summary>무교란 연속 8동 7분 안전 최대 N — v2complexdensity 산출</summary>
        private const int HeadlineVehicleCount = 15;

        private sealed class DisturbanceRow
        {
            public string Disturbance;
            public string Kind;
            public string BlockedCells;
            public int Robots;
            public ApartmentComplexDensityTrialV2 Trial;
        }

        public static void Run()
        {
            PhysicalTimeProfileV2 profile = PublishedParkingRobotTimingV2.Create(1.0);
            var options = new EmergencyAccessRouteGenerationOptionsV2
            {
                MaxRoutes = 4,
                MaxCenterlineAttempts = 16,
                MaxSearchExpansions = 100000,
            };
            ApartmentComplexScenarioV2 scenario =
                ApartmentComplexScenarioFactoryV2.BuildDensity(
                    HeadlineVehicleCount, profile.CreateOperationTiming());

            Console.WriteLine(
                $"=== 8동 단지 t=0 교란 스윕: N={HeadlineVehicleCount}, 서문+동문 ===");
            var rows = new List<DisturbanceRow>();

            Dictionary<int, ApartmentComplexDensityTrialV2> baseline =
                Solve8(scenario, options, profile, 4);
            foreach (ApartmentComplexDensityTrialV2 trial in baseline.Values)
                rows.Add(new DisturbanceRow
                {
                    Disturbance = "baseline",
                    Kind = "none",
                    BlockedCells = "",
                    Robots = 4,
                    Trial = trial,
                });
            PrintCondition("기준선(무교란)", baseline, baseline);

            int skipped = 0;
            foreach ((string name, (int X, int Y)[] cells) in BlockageAnchors())
            {
                var disturbance = new ComplexDisturbanceV2(name, cells);
                DisturbedComplexBuildResultV2 disturbed =
                    ApartmentComplexDisturbanceV2.Apply(scenario, disturbance);
                if (!disturbed.Success)
                {
                    Console.WriteLine($"{name}: 적용 불가 — {disturbed.FailReason}");
                    skipped++;
                    continue;
                }
                Dictionary<int, ApartmentComplexDensityTrialV2> trials =
                    Solve8(disturbed.Scenario, options, profile, 4);
                foreach (ApartmentComplexDensityTrialV2 trial in trials.Values)
                    rows.Add(new DisturbanceRow
                    {
                        Disturbance = name,
                        Kind = "road-blockage",
                        BlockedCells = string.Join(
                            ";", cells.Select(cell => cell.X + ":" + cell.Y)),
                        Robots = 4,
                        Trial = trial,
                    });
                PrintCondition(name, trials, baseline);
            }

            foreach (int robots in new[] { 3, 2, 1 })
            {
                Dictionary<int, ApartmentComplexDensityTrialV2> trials =
                    Solve8(scenario, options, profile, robots);
                foreach (ApartmentComplexDensityTrialV2 trial in trials.Values)
                    rows.Add(new DisturbanceRow
                    {
                        Disturbance = "unit-loss-" + robots,
                        Kind = "robot-loss",
                        BlockedCells = "",
                        Robots = robots,
                        Trial = trial,
                    });
                PrintCondition($"유닛 {robots}조", trials, baseline);
            }

            WriteRows(rows);
            WriteSummary(rows, baseline);
            PrintHeadline(rows, baseline, skipped);
        }

        private static Dictionary<int, ApartmentComplexDensityTrialV2> Solve8(
            ApartmentComplexScenarioV2 scenario,
            EmergencyAccessRouteGenerationOptionsV2 options,
            PhysicalTimeProfileV2 profile,
            int robots)
        {
            var session = new ApartmentComplexPlanningSessionV2(
                scenario,
                activeRobotCount: robots,
                generationOptions: options,
                maxTick: 5000,
                enableLowerBoundPruning: true);
            var trials = new Dictionary<int, ApartmentComplexDensityTrialV2>();
            foreach (ApartmentBuildingV2 building in scenario.Buildings)
                trials[building.Id] = ApartmentComplexDensitySweepV2.Evaluate(
                    scenario,
                    session,
                    building.Id,
                    includeSecondaryEntrances: true,
                    timeProfile: profile);
            return trials;
        }

        /// <summary>
        /// 도로 봉쇄 후보(쓰러진 나무 = 세로 2셀). 중앙로(폭5)·종축(폭3)·북측·남측 도로의
        /// 대표 지점. 슬롯·보호 셀과 겹치는 후보는 Apply가 거부하고 스윕은 건너뛴다.
        /// </summary>
        private static IEnumerable<(string Name, (int X, int Y)[] Cells)> BlockageAnchors()
        {
            foreach (int x in new[] { 6, 9, 14, 19, 23, 27, 32, 36, 41, 46, 50, 53 })
                yield return ($"tree-central-x{x}", new[] { (x, 17), (x, 18) });
            foreach (int centerX in new[] { 3, 16, 29, 42, 55 })
                foreach (int y in new[] { 9, 27 })
                    yield return (
                        $"tree-column-x{centerX}-y{y}",
                        new[] { (centerX, y), (centerX, y + 1) });
            foreach (int x in new[] { 8, 34 })
                yield return ($"tree-north-x{x}", new[] { (x, 2), (x, 3) });
            foreach (int x in new[] { 18, 47 })
                yield return ($"tree-south-x{x}", new[] { (x, 36), (x, 37) });
        }

        private static void PrintCondition(
            string name,
            Dictionary<int, ApartmentComplexDensityTrialV2> trials,
            Dictionary<int, ApartmentComplexDensityTrialV2> baseline)
        {
            bool allPlan = trials.Values.All(trial => trial.PlanSuccess);
            bool allWithin = allPlan &&
                trials.Values.All(trial => trial.WithinBudget);
            ApartmentComplexDensityTrialV2 worst = trials.Values
                .Where(trial => trial.PlanSuccess)
                .OrderByDescending(trial => trial.Seconds)
                .ThenBy(trial => trial.BuildingId)
                .FirstOrDefault();
            int rerouted = trials.Count(pair =>
                pair.Value.PlanSuccess && baseline[pair.Key].PlanSuccess &&
                (pair.Value.SelectedEntrance != baseline[pair.Key].SelectedEntrance ||
                 pair.Value.SelectedRoute != baseline[pair.Key].SelectedRoute));
            string failed = string.Join(",", trials
                .Where(pair => !pair.Value.PlanSuccess)
                .Select(pair => pair.Key.ToString()));
            Console.WriteLine(
                $"{name,-22} | {(allWithin ? "안전" : allPlan ? "초과" : "불능")} | " +
                $"최악 {(worst == null ? "-" : worst.BuildingId + "동 " + worst.Seconds.ToString("0.0") + "초")} | " +
                $"전환 {rerouted}동" +
                (failed.Length > 0 ? $" | 실패동 {failed}" : ""));
        }

        private static void WriteRows(List<DisturbanceRow> rows)
        {
            var csv = new List<string>
            {
                "disturbance,kind,blocked_cells,robots,building,plan_success," +
                "within_7min,seconds,ticks,moved_vehicles,entrance,route," +
                "candidates,outcome,fail_reason",
            };
            foreach (DisturbanceRow row in rows)
            {
                ApartmentComplexDensityTrialV2 trial = row.Trial;
                csv.Add(string.Join(",",
                    row.Disturbance,
                    row.Kind,
                    row.BlockedCells,
                    row.Robots,
                    trial.BuildingId,
                    trial.PlanSuccess ? 1 : 0,
                    trial.WithinBudget ? 1 : 0,
                    trial.Seconds.ToString("F1", CultureInfo.InvariantCulture),
                    trial.Ticks,
                    trial.MovedVehicleCount,
                    trial.SelectedEntrance ?? "",
                    trial.SelectedRoute ?? "",
                    trial.CandidateCount,
                    trial.Outcome,
                    (trial.FailReason ?? "").Replace(',', ';')));
            }
            string path = OutputDir.Resolve("v2_disturbance_sweep.csv");
            File.WriteAllLines(path, csv);
            Console.WriteLine("CSV: " + path);
        }

        private static void WriteSummary(
            List<DisturbanceRow> rows,
            Dictionary<int, ApartmentComplexDensityTrialV2> baseline)
        {
            var csv = new List<string>
            {
                "disturbance,kind,robots,all_plan_success,all_within_7min," +
                "worst_building,worst_seconds,rerouted_buildings,failed_buildings",
            };
            foreach (var group in rows.GroupBy(row => row.Disturbance))
            {
                List<ApartmentComplexDensityTrialV2> trials =
                    group.Select(row => row.Trial).ToList();
                ApartmentComplexDensityTrialV2 worst = trials
                    .Where(trial => trial.PlanSuccess)
                    .OrderByDescending(trial => trial.Seconds)
                    .ThenBy(trial => trial.BuildingId)
                    .FirstOrDefault();
                int rerouted = trials.Count(trial =>
                    trial.PlanSuccess && baseline[trial.BuildingId].PlanSuccess &&
                    (trial.SelectedEntrance !=
                         baseline[trial.BuildingId].SelectedEntrance ||
                     trial.SelectedRoute != baseline[trial.BuildingId].SelectedRoute));
                csv.Add(string.Join(",",
                    group.Key,
                    group.First().Kind,
                    group.First().Robots,
                    trials.All(trial => trial.PlanSuccess),
                    trials.All(trial => trial.PlanSuccess && trial.WithinBudget),
                    worst == null ? "" : worst.BuildingId.ToString(),
                    worst == null
                        ? ""
                        : worst.Seconds.ToString("F1", CultureInfo.InvariantCulture),
                    rerouted,
                    trials.Count(trial => !trial.PlanSuccess)));
            }
            string path = OutputDir.Resolve("v2_disturbance_summary.csv");
            File.WriteAllLines(path, csv);
            Console.WriteLine("CSV: " + path);
        }

        private static void PrintHeadline(
            List<DisturbanceRow> rows,
            Dictionary<int, ApartmentComplexDensityTrialV2> baseline,
            int skipped)
        {
            List<IGrouping<string, DisturbanceRow>> blockages = rows
                .Where(row => row.Kind == "road-blockage")
                .GroupBy(row => row.Disturbance)
                .ToList();
            int safe = blockages.Count(group => group.All(row =>
                row.Trial.PlanSuccess && row.Trial.WithinBudget));
            int exceeded = blockages.Count(group =>
                group.All(row => row.Trial.PlanSuccess) &&
                group.Any(row => !row.Trial.WithinBudget));
            int infeasible = blockages.Count(group =>
                group.Any(row => !row.Trial.PlanSuccess));
            double baselineWorst = baseline.Values.Max(trial => trial.Seconds);
            double blockedWorst = blockages
                .SelectMany(group => group)
                .Where(row => row.Trial.PlanSuccess)
                .Select(row => row.Trial.Seconds)
                .DefaultIfEmpty(0.0)
                .Max();

            Console.WriteLine();
            Console.WriteLine($"봉쇄 {blockages.Count}개 평가 (적용 불가 {skipped}개 제외): " +
                $"8동 안전 유지 {safe} · 시간 초과 {exceeded} · 접근 불능 {infeasible}");
            Console.WriteLine(
                $"무교란 최악 {baselineWorst:0.0}초 → 봉쇄 하 최악(성공 동) {blockedWorst:0.0}초 " +
                $"(예산 {TimeBudget.BaselineSeconds:0}초)");
            double reserve = TimeBudget.BaselineSeconds - blockedWorst;
            Console.WriteLine(
                "보수 상한 관점: 사건 도중 교란이 t초에 발생해도 전체 재계획으로 7분을 지키려면 " +
                (reserve > 0
                    ? $"t ≤ {reserve:0.0}초 (상한이며, 완료분 인정 재계획은 S2에서 정밀화)"
                    : "여유가 없음 — t=0 교란만으로 이미 예산 초과"));
        }
    }
}
