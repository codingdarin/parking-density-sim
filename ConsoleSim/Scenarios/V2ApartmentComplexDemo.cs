using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ParkingSim.Core.V2;

namespace ParkingSim.Scenarios
{
    public static class V2ApartmentComplexDemo
    {
        private sealed class Row
        {
            public int BuildingId;
            public string EntranceMode;
            public string SelectedEntrance;
            public int CandidateCount;
            public int MovedVehicles;
            public int Ticks;
            public double Seconds;
            public bool WithinSevenMinutes;
            public string FailReason;
        }

        public static void Run()
        {
            PhysicalTimeProfileV2 profile =
                PublishedParkingRobotTimingV2.Create(1.0);
            ApartmentComplexScenarioV2 scenario =
                ApartmentComplexScenarioFactoryV2.Build(
                    profile.CreateOperationTiming());
            var options = new EmergencyAccessRouteGenerationOptionsV2
            {
                MaxRoutes = 4,
                MaxCenterlineAttempts = 16,
                MaxSearchExpansions = 100000,
            };
            var rows = new List<Row>();
            foreach (ApartmentBuildingV2 building in scenario.Buildings)
            {
                rows.Add(Evaluate(
                    scenario, building.Id, false, "서문 단일", profile, options));
                rows.Add(Evaluate(
                    scenario, building.Id, true, "서문+동문", profile, options));
            }

            Console.WriteLine("=== 2행×4동 아파트 단지 화재 접근 전수 평가 ===");
            Console.WriteLine(
                "화재동 | 진입조건 | 선택입구 | 후보 | 이동 | 확보시간 | 7분");
            foreach (Row row in rows)
            {
                Console.WriteLine(
                    $"{row.BuildingId,6} | {row.EntranceMode,-8} | " +
                    $"{(row.SelectedEntrance ?? "-"),-14} | " +
                    $"{row.CandidateCount,4} | {row.MovedVehicles,4} | " +
                    (row.FailReason == null
                        ? $"{row.Seconds,7:0.0}초 | " +
                          (row.WithinSevenMinutes ? "통과" : "실패")
                        : "실패: " + row.FailReason));
            }

            foreach (string mode in rows.Select(row => row.EntranceMode).Distinct())
            {
                Row worst = rows
                    .Where(row => row.EntranceMode == mode && row.FailReason == null)
                    .OrderByDescending(row => row.Seconds)
                    .ThenByDescending(row => row.MovedVehicles)
                    .ThenBy(row => row.BuildingId)
                    .FirstOrDefault();
                Console.WriteLine(
                    worst == null
                        ? mode + " 조건: 전 동 개통 실패"
                        : $"{mode} 최악: {worst.BuildingId}동 " +
                          $"{worst.Seconds:0.0}초/{worst.MovedVehicles}대, " +
                          $"7분 {(worst.WithinSevenMinutes ? "통과" : "실패")}");
            }
            WriteCsv(rows);
        }

        private static Row Evaluate(
            ApartmentComplexScenarioV2 scenario,
            int buildingId,
            bool includeSecondary,
            string mode,
            PhysicalTimeProfileV2 profile,
            EmergencyAccessRouteGenerationOptionsV2 options)
        {
            ApartmentComplexPlanResultV2 result =
                ApartmentComplexEmergencyPlannerV2.Solve(
                    scenario,
                    new ApartmentFireIncidentV2(buildingId),
                    includeSecondary,
                    activeRobotCount: 4,
                    generationOptions: options,
                    maxTick: 5000);
            var row = new Row
            {
                BuildingId = buildingId,
                EntranceMode = mode,
            };
            if (!result.Success)
            {
                row.FailReason = result.FailReason;
                row.CandidateCount = result.Attempts.Sum(attempt =>
                    attempt.AutomaticPlan.Generation == null
                        ? 0
                        : attempt.AutomaticPlan.Generation.Routes.Count);
                return row;
            }

            EmergencyAccessCandidateResultV2 selected =
                result.Selected.AutomaticPlan.Plan.Selected;
            row.SelectedEntrance = result.Selected.Entrance.Name;
            row.CandidateCount = result.Attempts.Sum(attempt =>
                attempt.AutomaticPlan.Generation.Routes.Count);
            row.MovedVehicles = selected.Scenario.SelectedVehicleCount;
            row.Ticks = selected.Plan.Ticks;
            row.Seconds = profile.PlanSeconds(row.Ticks);
            row.WithinSevenMinutes = row.Seconds <= 420.0;
            return row;
        }

        private static void WriteCsv(IEnumerable<Row> rows)
        {
            Directory.CreateDirectory("output");
            string path = Path.Combine("output", "v2_apartment_complex.csv");
            using (var writer = new StreamWriter(path, false))
            {
                writer.WriteLine(
                    "building_id,entrance_mode,selected_entrance,candidates," +
                    "moved_vehicles,ticks,seconds,within_7_minutes,fail_reason");
                foreach (Row row in rows)
                {
                    writer.WriteLine(string.Join(",", new[]
                    {
                        row.BuildingId.ToString(CultureInfo.InvariantCulture),
                        Escape(row.EntranceMode),
                        Escape(row.SelectedEntrance),
                        row.CandidateCount.ToString(CultureInfo.InvariantCulture),
                        row.MovedVehicles.ToString(CultureInfo.InvariantCulture),
                        row.Ticks.ToString(CultureInfo.InvariantCulture),
                        row.Seconds.ToString("0.0", CultureInfo.InvariantCulture),
                        row.WithinSevenMinutes ? "true" : "false",
                        Escape(row.FailReason),
                    }));
                }
            }
            Console.WriteLine("CSV: " + path);
        }

        private static string Escape(string value)
        {
            if (value == null) return string.Empty;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
