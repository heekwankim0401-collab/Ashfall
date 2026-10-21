namespace Ashfall.Player
{
    // §4.1 소음 반경을 AI 청각용 펄스로 바꾼다 (M3, SYS-PLR-01 → SYS-AI 청각).
    // 움직이는 동안(반경 > 0) noisePulseIntervalSec마다 한 번 true. 멈추면(반경 0) 누적을 버리고,
    // 다시 움직이기 시작한 첫 틱에는 곧바로 한 번 낸다(첫 걸음 소리).
    public sealed class FootstepNoiseModel
    {
        // 누적 float 오차 허용 (0.1 × 5 = 0.5 경계에서 한 틱 늦지 않게). 튜닝 수치 아님.
        private const float TimeEpsilon = 1e-4f;

        private readonly PlayerConfigSO config;
        private bool moving;
        private float elapsed;

        public FootstepNoiseModel(PlayerConfigSO config)
        {
            this.config = config;
        }

        // 매 프레임. radiusM = 이번 프레임 이동 소음 반경 (LocomotionModel.NoiseRadius). 펄스를 낼 때 true.
        public bool Tick(float dt, float radiusM)
        {
            if (radiusM <= 0f)
            {
                Reset();
                return false;
            }

            if (!moving)
            {
                moving = true;
                elapsed = 0f;
                return true;
            }

            elapsed += dt;
            float interval = config.noisePulseIntervalSec;
            if (elapsed + TimeEpsilon < interval)
                return false;

            // 긴 프레임이 와도 한 번만 낸다 (밀린 펄스를 몰아 내지 않는다).
            elapsed = interval > 0f ? (elapsed - interval) % interval : 0f;
            return true;
        }

        public void Reset()
        {
            moving = false;
            elapsed = 0f;
        }
    }
}
