namespace Ashfall.Core
{
    // 보폭 거리 기반 걸음 리듬 (M6 설계 §1.1·§3.6, Q12 — §11 발소리). Player(PlayerMotor)·AI(EnemyAgent) 공용, 순수.
    // 이동한 수평 거리를 누적해 보폭을 넘을 때마다 한 걸음. AI 청각용 소음 펄스(FootstepNoiseModel)와는 별개다.
    public sealed class StrideCadence
    {
        private float accumulatedM;
        private bool moving;

        // 지금 이동 중으로 보고 있는지 (마지막 Tick의 거리 > 0)
        public bool IsMoving => moving;

        // 다음 걸음까지 쌓인 거리 (m, 테스트·디버그용)
        public float AccumulatedM => accumulatedM;

        // 이번 프레임 수평 이동 거리와 보폭. 걸음이면 true.
        // - 멈춰 있다가(거리 ≤ 0) 움직이기 시작하면 누적을 보폭의 절반에서 시작한다(첫 걸음이 빨리 나게).
        // - 한 틱에 최대 1걸음. 긴 프레임으로 보폭을 두 번 이상 넘으면 남은 거리를 버린다(밀린 걸음을 다음 틱에 몰아 내지 않음).
        // - 보폭 ≤ 0이면 항상 false(멈춤으로 본다). 거리 NaN·음수는 멈춤.
        public bool Tick(float horizontalDistanceM, float strideLengthM)
        {
            if (!(strideLengthM > 0f) || !(horizontalDistanceM > 0f))
            {
                Reset();
                return false;
            }

            if (!moving)
            {
                moving = true;
                accumulatedM = strideLengthM * 0.5f;
            }

            accumulatedM += horizontalDistanceM;
            if (accumulatedM < strideLengthM)
                return false;

            accumulatedM -= strideLengthM;
            if (accumulatedM >= strideLengthM)
                accumulatedM = 0f;
            return true;
        }

        // 멈춤 상태로 (다음 이동의 첫 걸음은 다시 보폭 절반 뒤). 사망·NavMesh 밖·공중 등에서 부른다.
        public void Reset()
        {
            moving = false;
            accumulatedM = 0f;
        }
    }
}
