using System;

namespace Ashfall.Audio
{
    // 가이거 계수기 클릭 (§11 "방사능 구역에서 가이거", M6 Q22: 재 폭풍 방사능률에만 반응). 순수.
    // 초당 클릭 = 방사능률 × clicksPerRad (상한 maxClicksPerSec). 간격은 지수 분포(푸아송 과정).
    // 비율이 바뀌어도 맞도록 "기대 클릭 수" 단위의 남은 양을 쓴다(지수(1) 표본에서 rate·dt씩 뺀다).
    public sealed class GeigerClickModel
    {
        // 한 틱에 낼 수 있는 최대 클릭 수 (긴 프레임 보호)
        public const int MaxClicksPerTick = 64;

        private readonly float clicksPerRad;
        private readonly float maxClicksPerSec;
        private readonly Random random;
        private double remaining = -1.0;   // 다음 클릭까지 남은 기대 클릭 수 (<0 = 아직 뽑지 않음)

        public GeigerClickModel(float clicksPerRad, float maxClicksPerSec, Random random)
        {
            this.clicksPerRad = clicksPerRad;
            this.maxClicksPerSec = maxClicksPerSec;
            this.random = random ?? throw new ArgumentNullException(nameof(random));
        }

        // 현재 초당 클릭 (0 이상)
        public float Rate(float radiationPerSec)
        {
            if (!(radiationPerSec > 0f) || !(clicksPerRad > 0f) || float.IsInfinity(radiationPerSec))
                return 0f;
            float rate = radiationPerSec * clicksPerRad;
            if (maxClicksPerSec > 0f && rate > maxClicksPerSec)
                rate = maxClicksPerSec;
            return rate;
        }

        // 이번 틱에 낼 클릭 수. 방사능 0·dt 0이면 0.
        public int Tick(float dt, float radiationPerSec)
        {
            float rate = Rate(radiationPerSec);
            if (rate <= 0f || !(dt > 0f))
                return 0;
            if (remaining < 0.0)
                remaining = DrawUnitExponential();

            remaining -= rate * (double)dt;
            int clicks = 0;
            while (remaining <= 0.0 && clicks < MaxClicksPerTick)
            {
                clicks++;
                remaining += DrawUnitExponential();
            }
            if (remaining <= 0.0)
                remaining = DrawUnitExponential();
            return clicks;
        }

        public void Reset() => remaining = -1.0;

        private double DrawUnitExponential() => -Math.Log(1.0 - random.NextDouble());
    }
}
