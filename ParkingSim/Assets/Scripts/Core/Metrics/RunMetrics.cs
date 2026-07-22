namespace ParkingSim.Core.Metrics
{
    /// <summary>
    /// 비상 실행 1건의 입력 파라미터 + 산출 지표 평면 레코드.
    /// D9 배치 실험의 CSV 한 행에 대응한다 (측정정의서 §5 산출물).
    /// </summary>
    public sealed class RunMetrics
    {
        // 입력 (스윕 축)
        public int OccupiedLanes { get; set; }
        public double FireMeters { get; set; }
        public int PocketCount { get; set; }
        public int Seed { get; set; }
        public int RobotCount { get; set; }
        public int BetaCells { get; set; }

        // 산출 — 확보/판정
        public bool Success { get; set; }
        public string FailReason { get; set; } = "";
        public int ClearTick { get; set; }
        public double ClearSeconds { get; set; }
        public double ClearMinutes => ClearSeconds / 60.0;
        public bool WithinBudget { get; set; }

        // 산출 — 용량·재시도·검증
        public int SectionCarCount { get; set; } // = S_필요
        public int Attempts { get; set; }        // = PlanFailures (병렬성 붕괴 독립 지표)
        public int MainDrops { get; set; }
        public int PocketDrops { get; set; }
        public int Collisions { get; set; }

        // 산출 — 병렬성 실측 + 봉투 대비
        public int MakespanTicks { get; set; }
        public double Utilization { get; set; }   // 0~1: 로봇 평균 가동률
        public double EffectiveP { get; set; }    // 동시 가동 로봇 평균 (유효 병렬성 실측치)
        public double EnvelopeSeconds { get; set; } // 봉투 예측 (간섭 무시, p=RobotCount)
        public double DeviationRatio { get; set; }  // 실측 / 봉투 (>1 = 혼잡 손실)

        // 유휴 분해 (로봇-틱 비율) — 병렬성 붕괴의 주범 진단
        public double DriveWaitFrac { get; set; }   // 통로 혼잡
        public double DropWaitFrac { get; set; }    // 하차 병목 (유예 창 직렬화)
        public double IdleFrac { get; set; }        // 완전유휴 (미배정·완료 후)
    }
}
