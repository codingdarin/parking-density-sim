using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ParkingSim.Core.V2;

namespace ParkingSim.Scenarios
{
    public static class V2FinalComparisonDemo
    {
        public static void Run()
        {
            var lines = new List<string>
            {
                "speed_mps,policy,gross_alpha,moved_vehicles,ticks,seconds," +
                "within_7min,reduction_vs_full,staging_dedicated,staging_used," +
                "net_alpha_all_parking,net_alpha_mixed,net_alpha_all_nonparking",
            };
            Console.WriteLine("=== 지상형 동일맵 최종 정책 비교 ===");
            Console.WriteLine(
                "속도 | 정책 | gross | 이동 | 시간 | 7분 | 전면대비 | " +
                "netα(전부주차/혼합/전부비주차)");
            Console.WriteLine(new string('-', 104));
            foreach (double speed in new[] { 1.0, 2.0, 3.0 })
            {
                SurfacePolicyComparisonResultV2 comparison =
                    SurfacePolicyComparisonV2.Run(
                        PublishedParkingRobotTimingV2.Create(speed));
                if (!comparison.Success)
                    throw new InvalidOperationException(comparison.FailReason);
                foreach (SurfacePolicyMeasurementV2 row in comparison.Policies)
                {
                    int dedicated;
                    int used;
                    int netParking;
                    int netMixed;
                    int netNonParking;
                    Account(
                        row,
                        out dedicated,
                        out used,
                        out netParking,
                        out netMixed,
                        out netNonParking);
                    string reduction = row.ReductionVsFullClearance.HasValue
                        ? row.ReductionVsFullClearance.Value.ToString(
                            "P1", CultureInfo.InvariantCulture)
                        : "-";
                    Console.WriteLine(
                        $"{speed,4:F0} | {PolicyName(row.Policy),12} | " +
                        $"{row.GrossAdditionalCars,5} | {row.MovedVehicles,4} | " +
                        $"{row.Seconds,6:F1}초 | " +
                        $"{(row.WithinSevenMinutes ? "통과" : "실패"),4} | " +
                        $"{reduction,8} | {netParking}/{netMixed}/{netNonParking}");
                    lines.Add(string.Join(",",
                        speed.ToString("F1", CultureInfo.InvariantCulture),
                        row.Policy,
                        row.GrossAdditionalCars,
                        row.MovedVehicles,
                        row.Ticks,
                        row.Seconds.ToString("F1", CultureInfo.InvariantCulture),
                        row.WithinSevenMinutes ? 1 : 0,
                        row.ReductionVsFullClearance.HasValue
                            ? row.ReductionVsFullClearance.Value.ToString(
                                "F4", CultureInfo.InvariantCulture)
                            : "",
                        dedicated,
                        used,
                        netParking,
                        netMixed,
                        netNonParking));
                }
            }
            Directory.CreateDirectory("output");
            string path = Path.Combine("output", "v2_final_comparison.csv");
            File.WriteAllLines(path, lines);
            Console.WriteLine("CSV: " + Path.GetFullPath(path));
        }

        private static void Account(
            SurfacePolicyMeasurementV2 row,
            out int dedicated,
            out int used,
            out int netParking,
            out int netMixed,
            out int netNonParking)
        {
            if (row.Policy == SurfaceEmergencyPolicyV2.AlwaysClear)
            {
                dedicated = 0;
                used = 0;
                netParking = 0;
                netMixed = 0;
                netNonParking = 0;
                return;
            }
            ParkingSlotV2[] staging = row.ScenarioProblem.Slots
                .Where(slot => slot.Kind == SlotKind.Staging).ToArray();
            StagingLandAccountingResultV2 allParking = Evaluate(
                row, staging, convertedCount: 5);
            StagingLandAccountingResultV2 mixed = Evaluate(
                row, staging, convertedCount: 2);
            StagingLandAccountingResultV2 allNonParking = Evaluate(
                row, staging, convertedCount: 0);
            if (!allParking.NetAlphaClaimable ||
                !mixed.NetAlphaClaimable ||
                !allNonParking.NetAlphaClaimable)
                throw new InvalidOperationException("최종 비교 토지 회계 실패");
            dedicated = mixed.DedicatedStagingSlots;
            used = mixed.UsedStagingSlots;
            netParking = allParking.VerifiedNetAlpha.Value;
            netMixed = mixed.VerifiedNetAlpha.Value;
            netNonParking = allNonParking.VerifiedNetAlpha.Value;
        }

        private static StagingLandAccountingResultV2 Evaluate(
            SurfacePolicyMeasurementV2 row,
            IReadOnlyList<ParkingSlotV2> staging,
            int convertedCount)
        {
            return CapacityTradeoffV2.EvaluateStagingLand(
                row.ScenarioProblem,
                row.Plan,
                row.GrossAdditionalCars,
                staging.Select((slot, index) => new StagingLandProfileV2(
                    slot.Id,
                    index < convertedCount
                        ? StagingLandKindV2.ConvertedParkingSpace
                        : StagingLandKindV2.ExistingNonParkingPaved)));
        }

        private static string PolicyName(SurfaceEmergencyPolicyV2 policy)
        {
            switch (policy)
            {
                case SurfaceEmergencyPolicyV2.AlwaysClear:
                    return "상시개방";
                case SurfaceEmergencyPolicyV2.FullClearance:
                    return "전면재배치";
                case SurfaceEmergencyPolicyV2.MinimumBlockingVehicles:
                    return "최소차량상부";
                default:
                    return "최소개통하부";
            }
        }
    }
}
