using System;
using UnityEngine;
using Random = System.Random;

namespace Ashfall.Audio
{
    // 무작위 간격 반복 (금속 붕괴 40~120초, §11 환경음). 순수.
    // 간격은 [min, max] 균등(뒤집혀 있으면 정렬, 하한 MinIntervalSec). 한 틱에 최대 1번 울린다.
    public sealed class IntervalScheduler
    {
        // 0 간격으로 매 틱 울리는 것을 막는 하한 (초)
        public const float MinIntervalSec = 0.01f;

        private readonly float min;
        private readonly float max;
        private readonly Random random;

        public IntervalScheduler(Vector2 rangeSec, Random random)
        {
            this.random = random ?? throw new ArgumentNullException(nameof(random));
            float a = float.IsNaN(rangeSec.x) ? MinIntervalSec : rangeSec.x;
            float b = float.IsNaN(rangeSec.y) ? MinIntervalSec : rangeSec.y;
            min = Mathf.Max(MinIntervalSec, Mathf.Min(a, b));
            max = Mathf.Max(min, Mathf.Max(a, b));
            Remaining = Pick();
        }

        public float MinSec => min;
        public float MaxSec => max;
        // 다음 울림까지 남은 초
        public float Remaining { get; private set; }
        // 마지막으로 뽑은 간격
        public float LastInterval { get; private set; }

        public bool Tick(float dt)
        {
            if (!(dt > 0f))
                return false;
            Remaining -= dt;
            if (Remaining > 0f)
                return false;
            Remaining = Pick();
            return true;
        }

        // 지금 울린 것으로 치고 다음 간격을 새로 뽑는다(디버그 "지금" 뒤 바로 또 울리지 않게).
        public void Restart() => Remaining = Pick();

        private float Pick()
        {
            LastInterval = min + (float)random.NextDouble() * (max - min);
            return LastInterval;
        }
    }
}
