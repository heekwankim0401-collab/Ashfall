namespace Ashfall.Core
{
    // RPM에 맞춘 발사 간격. 자동은 누르고 있는 동안 연사, 반자동은 한 번 누를 때 한 발.
    public sealed class FireControl
    {
        private readonly float interval;
        private float nextShotTime = float.NegativeInfinity;
        private bool triggerWasHeld;

        public FireMode Mode { get; set; }

        public FireControl(int rpm, FireMode mode)
        {
            interval = 60f / rpm;
            Mode = mode;
        }

        public float IntervalSec => interval;

        // 이번 프레임에 한 발 쏠 수 있으면 true. 연사 중에는 간격이 누적 오차 없이 유지된다.
        public bool Tick(float now, bool triggerHeld)
        {
            bool pressedNow = triggerHeld && !triggerWasHeld;
            triggerWasHeld = triggerHeld;

            if (!triggerHeld || now < nextShotTime)
                return false;
            if (Mode == FireMode.Semi && !pressedNow)
                return false;

            // 직전 발사 직후 이어 쏘면 예정 시각 기준, 쉬었다 쏘면 지금 기준.
            nextShotTime = (now - nextShotTime < interval ? nextShotTime : now) + interval;
            return true;
        }
    }
}
