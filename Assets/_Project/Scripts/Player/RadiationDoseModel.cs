namespace Ashfall.Player
{
    // 방사능 누적량 (§4.3 STAT-RAD, §8.3 재 폭풍 +1/s, M6 Q27). 순수 C#.
    // M6은 누적만 한다 — 상한·효과(100 → 최대 HP −20%)·해독제·방독면·화면 표시·이벤트는 M7. TODO(GDD-?) Q27
    // 레이드마다 0에서 시작한다(플레이어가 레이드 씬과 함께 새로 생긴다, M5 Q18 체력 이월 없음).
    public sealed class RadiationDoseModel
    {
        public float Dose { get; private set; }

        // dt초 동안 ratePerSec(/s)만큼 쌓는다. dt·rate가 0 이하·NaN·무한이면 무시.
        public void Tick(float dt, float ratePerSec)
        {
            if (!(dt > 0f) || !(ratePerSec > 0f))
                return;
            float add = dt * ratePerSec;
            if (float.IsInfinity(add))
                return;
            Dose += add;
        }

        public void Reset() => Dose = 0f;
    }
}
