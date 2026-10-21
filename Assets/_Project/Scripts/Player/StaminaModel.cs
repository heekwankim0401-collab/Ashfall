using UnityEngine;

namespace Ashfall.Player
{
    // §4.1 스태미나: 최대 100, 질주 15/s 소모, 재생 10/s (정지 20/s), 0이 되면 3초 질주 잠금.
    public sealed class StaminaModel
    {
        private readonly PlayerConfigSO config;
        private float lockRemaining;

        public float Current { get; private set; }
        public bool SprintLocked => lockRemaining > 0f;

        public StaminaModel(PlayerConfigSO config)
        {
            this.config = config;
            Current = config.staminaMax;
        }

        public void Tick(float dt, bool sprinting, bool moving)
        {
            if (lockRemaining > 0f)
                lockRemaining = Mathf.Max(0f, lockRemaining - dt);

            if (sprinting)
            {
                Spend(config.sprintStaminaPerSec * dt);
                return;
            }

            float regen = moving ? config.staminaRegenMovingPerSec : config.staminaRegenStationaryPerSec;
            Current = Mathf.Min(config.staminaMax, Current + regen * dt);
        }

        // 점프처럼 한 번에 드는 비용. 모자라면 쓰지 않고 false.
        public bool TrySpend(float amount)
        {
            if (Current < amount)
                return false;
            Spend(amount);
            return true;
        }

        private void Spend(float amount)
        {
            Current -= amount;
            if (Current <= 0f)
            {
                Current = 0f;
                lockRemaining = config.exhaustedSprintLockSec;
            }
        }
    }
}
