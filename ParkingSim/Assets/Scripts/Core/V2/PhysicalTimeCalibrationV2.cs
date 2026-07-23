using System;

namespace ParkingSim.Core.V2
{
    /// <summary>
    /// 격자 이동과 차량 취득/해제 서비스시간을 분리한 물리 시간 프로파일.
    /// 서비스시간은 이동틱 단위로 올림해 시공간 예약 플래너에 다시 투입한다.
    /// </summary>
    public sealed class PhysicalTimeProfileV2
    {
        public string Name { get; }
        public double CellMeters { get; }
        public double TravelSpeedMetersPerSecond { get; }
        public double PickupServiceSeconds { get; }
        public double ReleaseServiceSeconds { get; }
        public string SourceLabel { get; }
        public string SourceUrl { get; }

        public double MotionTickSeconds => CellMeters / TravelSpeedMetersPerSecond;
        public int PickupServiceTicks => ServiceTicks(PickupServiceSeconds);
        public int ReleaseServiceTicks => ServiceTicks(ReleaseServiceSeconds);
        public double QuantizedPickupSeconds => PickupServiceTicks * MotionTickSeconds;
        public double QuantizedReleaseSeconds => ReleaseServiceTicks * MotionTickSeconds;

        public PhysicalTimeProfileV2(
            string name,
            double cellMeters,
            double travelSpeedMetersPerSecond,
            double pickupServiceSeconds,
            double releaseServiceSeconds,
            string sourceLabel,
            string sourceUrl)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("시간 프로파일 이름이 필요함", nameof(name));
            if (cellMeters <= 0)
                throw new ArgumentOutOfRangeException(nameof(cellMeters));
            if (travelSpeedMetersPerSecond <= 0)
                throw new ArgumentOutOfRangeException(nameof(travelSpeedMetersPerSecond));
            if (pickupServiceSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(pickupServiceSeconds));
            if (releaseServiceSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(releaseServiceSeconds));
            Name = name;
            CellMeters = cellMeters;
            TravelSpeedMetersPerSecond = travelSpeedMetersPerSecond;
            PickupServiceSeconds = pickupServiceSeconds;
            ReleaseServiceSeconds = releaseServiceSeconds;
            SourceLabel = sourceLabel;
            SourceUrl = sourceUrl;
        }

        public OperationTimingV2 CreateOperationTiming(int safetyBufferTicks = 0)
        {
            return new OperationTimingV2(
                PickupServiceTicks,
                ReleaseServiceTicks,
                safetyBufferTicks);
        }

        public double PlanSeconds(int ticks)
        {
            if (ticks < 0) throw new ArgumentOutOfRangeException(nameof(ticks));
            return ticks * MotionTickSeconds;
        }

        public double ServiceOnlyLowerBoundSeconds(
            int vehicleCount,
            int activeRobotCount)
        {
            if (vehicleCount < 0)
                throw new ArgumentOutOfRangeException(nameof(vehicleCount));
            if (activeRobotCount < 1)
                throw new ArgumentOutOfRangeException(nameof(activeRobotCount));
            int batches = (vehicleCount + activeRobotCount - 1) / activeRobotCount;
            return batches * (PickupServiceSeconds + ReleaseServiceSeconds);
        }

        private int ServiceTicks(double seconds)
        {
            return (int)Math.Ceiling(seconds / MotionTickSeconds - 1e-9);
        }
    }

    /// <summary>
    /// Stanley Robotics 공개 기술사양을 사용한 참조 프로파일.
    /// 최대속도3m/s, 차량 취득90초, 해제60초. 최대속도는 실제 평균속도가 아닌 상한이다.
    /// </summary>
    public static class PublishedParkingRobotTimingV2
    {
        public const double CellMeters = 2.5;
        public const double MaximumTravelSpeedMetersPerSecond = 3.0;
        public const double PickupServiceSeconds = 90.0;
        public const double ReleaseServiceSeconds = 60.0;
        public const string SourceLabel =
            "Stanley Robotics Robot Technical Specifications";
        public const string SourceUrl =
            "https://www.stanley-robotics.com/wp-content/uploads/2022/07/" +
            "stanley-robotics-robot-spec.pdf";

        public static PhysicalTimeProfileV2 Create(double travelSpeedMetersPerSecond)
        {
            if (travelSpeedMetersPerSecond > MaximumTravelSpeedMetersPerSecond)
                throw new ArgumentOutOfRangeException(
                    nameof(travelSpeedMetersPerSecond),
                    "공개 최대속도 3m/s를 초과할 수 없음");
            return new PhysicalTimeProfileV2(
                "stanley-published-" +
                travelSpeedMetersPerSecond.ToString("0.###") + "mps",
                CellMeters,
                travelSpeedMetersPerSecond,
                PickupServiceSeconds,
                ReleaseServiceSeconds,
                SourceLabel,
                SourceUrl);
        }
    }
}
