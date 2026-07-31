using System;
using System.Collections.Generic;
using System.Linq;

namespace ParkingSim.Core.V2
{
    /// <summary>
    /// 단지 A 적치 배치안 — S4b 반사실 실험용.
    /// SouthWestOnly = 실측 근사 기준선(남측 진입부 12면 편중).
    /// Redistributed = 총 면수 12를 유지한 채 남측 6 + 동측 6으로 분산(순수 배치 효과).
    /// Extended = 남측 12 + 동측 6 = 18면(배치 + 용량 효과).
    /// </summary>
    public enum SiteStagingLayoutV2 : byte
    {
        SouthWestOnly,
        Redistributed,
        Extended,
    }

    /// <summary>
    /// 실제 단지 A(익명화) 발췌 블록 — 1970년대 준공 강남 소재 대단지의 정형 판상 구간.
    /// 공개 자료 근거: 총 28동·지상 14층·4,424세대·대지 237,900㎡·주차 5,000대
    /// (세대당 1.13대)·지하주차장 부재·이중주차 일상·소방차 진입 지연 실사례 보도.
    ///
    /// 격자 근사(셀 2.5m, 보수 방향 명시):
    /// - 판상 동 4개(2행×2열)를 발췌. 동 길이는 실측 ~90m를 60m(24셀)로 절단.
    /// - 동 전면 회랑 = 직각주차열 2셀 + 도로 2셀(보도 6m급의 내림 5m) + 주차열 2셀.
    ///   도로 2셀(5m) &lt; 폭3(7.5m)이므로 소방 접근은 항상 연석 주차열의
    ///   관리 차량 이동을 요구한다 — "이중주차 제거만으로는 부족"이 기하에서 나온다.
    /// - 중앙 종축 4셀(10m), 서/동측 진입로 4셀, 남/북 외곽도로 3셀.
    /// - 배경 만차: 회랑 직각주차열 전량을 이동 가능(Blocking) 슬롯 + 상시 점유
    ///   (로봇 주차장 전환 가정). 가변 N = 이중주차(종축·진입로·골목 연석 평행 점유).
    /// - 적치 12면: 남측 진입부의 비주차 포장(진입광장) 가정 — 순이득 회계는 별도.
    /// </summary>
    public static class SiteABlockScenarioFactoryV2
    {
        public const int Width = 60;
        public const int Height = 22;
        public const int BuildingCount = 4;
        /// <summary>가변 이중주차 최대 대수 (결정론적 누적 순서)</summary>
        public const int MaximumBlockingVehicles = 16;

        // 행/열 좌표 (y0 남쪽): 남측도로 y0..2 / 1행 동 y3..7 / 회랑 y8..13
        // (주차열 y8..9, 도로 y10..11, 주차열 y12..13) / 2행 동 y14..18 / 북측도로 y19..21
        private const int Row1MinY = 3;
        private const int Row1MaxY = 7;
        private const int CorridorParkSouthY = 8;
        private const int CorridorRoadMinY = 10;
        private const int CorridorRoadMaxY = 11;
        private const int CorridorParkNorthY = 12;
        private const int Row2MinY = 14;
        private const int Row2MaxY = 18;
        // 열 좌표: 서측 진입로 x0..3 / A열 동 x4..27 / 중앙 종축 x28..31 /
        // B열 동 x32..55 / 동측 도로 x56..59
        private const int ColAMinX = 4;
        private const int ColAMaxX = 27;
        private const int CentralMinX = 28;
        private const int CentralMaxX = 31;
        private const int ColBMinX = 32;
        private const int ColBMaxX = 55;

        public static ApartmentComplexScenarioV2 Build(OperationTimingV2 timing = null)
        {
            return BuildDensity(MaximumBlockingVehicles, timing);
        }

        public static ApartmentComplexScenarioV2 BuildDensity(
            int blockingVehicleCount,
            OperationTimingV2 timing = null,
            SiteStagingLayoutV2 stagingLayout = SiteStagingLayoutV2.SouthWestOnly)
        {
            if (blockingVehicleCount < 0 ||
                blockingVehicleCount > MaximumBlockingVehicles)
                throw new ArgumentOutOfRangeException(nameof(blockingVehicleCount));

            bool[,] floor = new bool[Width, Height];
            Fill(floor, 0, Width - 1, 0, 2);                       // 남측 외곽도로
            Fill(floor, 0, Width - 1, 19, 21);                     // 북측 외곽도로
            Fill(floor, 0, Width - 1, CorridorParkSouthY, CorridorParkNorthY + 1); // 회랑 전폭
            Fill(floor, 0, 3, 0, 21);                              // 서측 진입로
            Fill(floor, CentralMinX, CentralMaxX, 0, 21);          // 중앙 종축
            Fill(floor, 56, 59, 0, 21);                            // 동측 도로

            // 동 4개 — 전용구역은 각 동 전면 회랑 도로(폭5×2셀), 항상 비점유
            var buildings = new List<ApartmentBuildingV2>
            {
                CreateBuilding(1, ColAMinX, ColAMaxX, Row1MinY, Row1MaxY, 15, CorridorRoadMinY),
                CreateBuilding(2, ColBMinX, ColBMaxX, Row1MinY, Row1MaxY, 43, CorridorRoadMinY),
                CreateBuilding(3, ColAMinX, ColAMaxX, Row2MinY, Row2MaxY, 8, CorridorRoadMaxY),
                CreateBuilding(4, ColBMinX, ColBMaxX, Row2MinY, Row2MaxY, 36, CorridorRoadMaxY),
            };

            var slots = new List<ParkingSlotV2>();
            // ── 가변 이중주차 16면 (슬롯 0..15, 누적 순서 = 목록 순서) ──
            // 중앙 종축 연석(x29) 평행 4 → 골목 연석(y10/y11) 평행 8 → 서측 진입로(x1) 4
            foreach (int y in new[] { 4, 6, 15, 17 })
                AddSlot(slots, SlotKind.Blocking, 29, y, VehicleOrientation.Vertical);
            foreach (int x in new[] { 20, 24, 48, 52 })
                AddSlot(slots, SlotKind.Blocking, x, CorridorRoadMinY);
            foreach (int x in new[] { 19, 23, 47, 51 })
                AddSlot(slots, SlotKind.Blocking, x, CorridorRoadMaxY);
            foreach (int y in new[] { 4, 6, 15, 17 })
                AddSlot(slots, SlotKind.Blocking, 1, y, VehicleOrientation.Vertical);
            int variableSlotCount = slots.Count;

            // ── 배경 만차: 회랑 직각주차열 (2셀 간격, 전용구역 전면 주차금지 준수) ──
            var zoneFrontage = new HashSet<int>();
            foreach (ApartmentBuildingV2 building in buildings)
                foreach ((int zx, int _) in building.FireEngineZone.Cells)
                    zoneFrontage.Add(zx);
            foreach (int x in PerpendicularParkingXs())
            {
                if (zoneFrontage.Contains(x)) continue; // 전용구역 전면 주차금지
                AddSlot(slots, SlotKind.Blocking, x, CorridorParkSouthY,
                    VehicleOrientation.Vertical);
                AddSlot(slots, SlotKind.Blocking, x, CorridorParkNorthY,
                    VehicleOrientation.Vertical);
            }
            int backgroundSlotCount = slots.Count - variableSlotCount;

            // ── 적치면: 배치안별 구성 (남측 y0 연석 = 진입광장 비주차 포장,
            //    동측 x59 연석 = 반사실 가정의 동측 유휴 포장) ──
            int southStagingCount =
                stagingLayout == SiteStagingLayoutV2.Redistributed ? 6 : 12;
            foreach (int x in Enumerable.Range(0, southStagingCount)
                         .Select(index => 4 + index * 2))
                AddSlot(slots, SlotKind.Staging, x, 0);
            if (stagingLayout != SiteStagingLayoutV2.SouthWestOnly)
                foreach (int y in new[] { 3, 5, 7, 14, 16, 18 })
                    AddSlot(slots, SlotKind.Staging, 59, y,
                        VehicleOrientation.Vertical);

            // 초기 점유 = 가변 N + 배경 만차 전량
            IEnumerable<int> occupied = Enumerable.Range(0, blockingVehicleCount)
                .Concat(Enumerable.Range(variableSlotCount, backgroundSlotCount));

            var problem = new EmergencyProblemV2(
                Width,
                Height,
                floor,
                slots,
                occupied,
                new[] { (0, 0), (1, 0), (2, 0), (3, 0) },
                Array.Empty<(int X, int Y)>(),
                Array.Empty<VehiclePose>(),
                timing);
            return new ApartmentComplexScenarioV2
            {
                BaseProblem = problem,
                Buildings = buildings,
                BlockingVehicleCount = blockingVehicleCount,
                Entrances = new[]
                {
                    new ApartmentComplexEntranceV2("west-primary", (1, 10), true),
                    new ApartmentComplexEntranceV2("east-secondary", (58, 11), false),
                },
            };
        }

        /// <summary>회랑 직각주차열 x 좌표 — 동 전면 범위에서 2셀 간격</summary>
        private static IEnumerable<int> PerpendicularParkingXs()
        {
            for (int x = ColAMinX; x <= ColAMaxX; x += 2) yield return x;
            for (int x = ColBMinX; x <= ColBMaxX; x += 2) yield return x;
        }

        private static ApartmentBuildingV2 CreateBuilding(
            int id,
            int minX,
            int maxX,
            int minY,
            int maxY,
            int zoneCenterX,
            int approachY)
        {
            var footprint = new List<(int X, int Y)>();
            for (int x = minX; x <= maxX; x++)
                for (int y = minY; y <= maxY; y++)
                    footprint.Add((x, y));
            var zoneCells = new List<(int X, int Y)>();
            for (int x = zoneCenterX - 2; x <= zoneCenterX + 2; x++)
                for (int y = CorridorRoadMinY; y <= CorridorRoadMaxY; y++)
                    zoneCells.Add((x, y));
            var zone = new FireEngineZoneV2(
                "site-a-zone-" + id,
                id,
                ApartmentFacadeV2.CentralRoad,
                (zoneCenterX, approachY),
                zoneCells);
            return new ApartmentBuildingV2(id, footprint, zone);
        }

        private static void AddSlot(
            ICollection<ParkingSlotV2> slots,
            SlotKind kind,
            int x,
            int y,
            VehicleOrientation orientation = VehicleOrientation.Horizontal)
        {
            slots.Add(new ParkingSlotV2(
                slots.Count, kind, new VehiclePose(x, y, orientation)));
        }

        private static void Fill(
            bool[,] floor, int minX, int maxX, int minY, int maxY)
        {
            for (int x = minX; x <= maxX; x++)
                for (int y = minY; y <= maxY; y++)
                    floor[x, y] = true;
        }
    }
}
