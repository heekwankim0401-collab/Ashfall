using Ashfall.Core;
using UnityEngine;

namespace Ashfall.AI
{
    // 인지 게이지 0~1 (M3 설계 §1.5). 수치는 전부 AiConfigSO (TODO(GDD-?)).
    // suspiciousAt 이상 → Suspicious, 1 → Alert (판정은 EnemyBrain).
    public sealed class AwarenessModel
    {
        private readonly AiConfigSO config;

        public AwarenessModel(AiConfigSO config)
        {
            this.config = config;
        }

        public float Value { get; private set; }
        public bool IsFull => Value >= 1f;

        // 보일 때: 가까울수록 빠르게 오른다. instantDetectRangeM 안이면 즉시 1.
        public void AddSight(float distanceM, float effectiveRangeM, float dt)
        {
            if (distanceM <= config.instantDetectRangeM)
            {
                Value = 1f;
                return;
            }
            float t = effectiveRangeM > 0f ? Mathf.Clamp01(distanceM / effectiveRangeM) : 1f;
            float rate = Mathf.Lerp(config.sightGainNearPerSec, config.sightGainFarPerSec, t);
            Value = Mathf.Min(1f, Value + rate * dt);
        }

        // 발소리·점프는 게이지에 더하고 총성은 바로 1.
        public void AddNoise(NoiseKind kind)
        {
            switch (kind)
            {
                case NoiseKind.Gunshot:
                    Value = 1f;
                    break;
                case NoiseKind.Jump:
                    Value = Mathf.Min(1f, Value + config.jumpAwarenessGain);
                    break;
                default:
                    Value = Mathf.Min(1f, Value + config.footstepAwarenessGain);
                    break;
            }
        }

        public void Decay(float dt) => Value = Mathf.Max(0f, Value - config.awarenessDecayPerSec * dt);
        public void SetFull() => Value = 1f;
        public void Clear() => Value = 0f;
    }
}
