namespace Ashfall.Player
{
    // 방사능 누적량 (§4.3 STAT-RAD, §8.3 재 폭풍 +1/s, M6 Q27). 순수 C#.
    // M6은 누적만 했다. M7(7C-2a): 률 합(폭풍 × 방독면 + 구역, Q38)은 PlayerHealth, 효과(선량 ≥ 100 → 모든 부위 최대 HP × 0.8, Q35)는
    // BodyHealthModel.SyncRadiation, 해독제(선량 0, Q36)는 MedicalUseModel이 Reset으로 처리한다. 상한 없음 — 100 뒤에도 쌓이되 추가 효과 없음(Q35).
    // 레이드마다 0에서 시작한다(플레이어가 레이드 씬과 함께 새로 생긴다, M5 Q18 체력 이월 없음, M7 Q32).
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

        // M7 방사선 피폭 판정 (§4.3 누적 100 = PlayerConfigSO.radiationSicknessDose)
        public bool IsSick(float threshold) => Dose >= threshold;
    }
}
