using System;

namespace Ashfall.AI
{
    // 프레임 비용 측정값 (M3 설계 §1.8). 배치모드에서 Profiler 창 없이 테스트가 읽는다.
    // 평균 = 누적 합 / 횟수 (EMA 아님).
    public sealed class TickStats
    {
        private double totalMs;

        public double LastMs { get; private set; }
        public double AverageMs => Samples > 0 ? totalMs / Samples : 0.0;
        public double MaxMs { get; private set; }
        public int Samples { get; private set; }

        public void Add(double ms)
        {
            LastMs = ms;
            totalMs += ms;
            MaxMs = Math.Max(MaxMs, ms);
            Samples++;
        }

        public void Reset()
        {
            totalMs = 0.0;
            LastMs = 0.0;
            MaxMs = 0.0;
            Samples = 0;
        }
    }
}
