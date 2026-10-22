using System;
using UnityEngine;

namespace Ashfall.Raid
{
    // 재 폭풍 시작 시각 (§8.3 "레이드 중 1회 랜덤", M6 Q23). 순수 함수.
    public static class AshStormSchedule
    {
        // 창 [min, max] 안에서 균등 무작위 (레이드 경과 초). 창이 뒤집혀 있으면 정렬하고, 음수는 0으로 올린다.
        // TODO(GDD-?) Q23: 창은 AshStormConfigSO.startWindowSec(180~1200초 임시값).
        public static float PickStartSec(Vector2 window, System.Random random)
        {
            if (random == null)
                throw new ArgumentNullException(nameof(random));
            float min = Mathf.Max(0f, Mathf.Min(window.x, window.y));
            float max = Mathf.Max(min, Mathf.Max(window.x, window.y));
            float value = min + (float)random.NextDouble() * (max - min);
            return Mathf.Clamp(value, min, max);
        }
    }
}
