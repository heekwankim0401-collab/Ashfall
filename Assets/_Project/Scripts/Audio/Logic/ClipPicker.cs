using System;

namespace Ashfall.Audio
{
    // 클립 고르기 (§11 발소리 변주): 직전과 다른 인덱스 + 피치 ± jitter. 순수, Random 주입(시드 결정적).
    // 세트(재질)마다 하나씩 둔다 — 직전 기억이 세트별이다.
    public sealed class ClipPicker
    {
        // 피치 무작위 폭 상한 (피치가 0 이하가 되지 않게)
        public const float MaxJitter = 0.9f;

        private readonly Random random;
        private int last = -1;

        public ClipPicker(Random random)
        {
            this.random = random ?? throw new ArgumentNullException(nameof(random));
        }

        public int Last => last;

        // 0개 → −1, 1개 → 0, 그 밖 → 직전과 다른 무작위 인덱스.
        public int Next(int clipCount)
        {
            if (clipCount <= 0)
                return -1;
            if (clipCount == 1)
            {
                last = 0;
                return 0;
            }
            int index;
            if (last < 0 || last >= clipCount)
            {
                index = random.Next(clipCount);
            }
            else
            {
                // 직전 칸을 뺀 clipCount−1개 중 하나
                index = random.Next(clipCount - 1);
                if (index >= last)
                    index++;
            }
            last = index;
            return index;
        }

        // 1 ± jitter 균등 무작위
        public float Pitch(float jitter)
        {
            if (!(jitter > 0f))
                return 1f;
            if (jitter > MaxJitter)
                jitter = MaxJitter;
            return 1f + (float)(random.NextDouble() * 2.0 - 1.0) * jitter;
        }
    }
}
