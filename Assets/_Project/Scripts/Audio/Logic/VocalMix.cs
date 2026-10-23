using Ashfall.Core;
using UnityEngine;

namespace Ashfall.Audio
{
    // 적 울음 음량·들리는 거리·클립 세트 (M7 설계 §1.2·§4.9, Q26 — "소리가 곧 정보", 기둥 3). 순수.
    // 게인 = 종류 음량 × (1 − d/vocalAudibleM). 들리는 거리 밖·NaN 거리·들리는 거리 ≤ 0이면 재생 없음.
    // 발소리(제곱 감쇠)보다 완만한 선형 감쇠 — 무리 접근을 멀리서도 알아듣게. TODO(GDD-?) Q26
    public static class VocalMix
    {
        public static bool Evaluate(float distanceM, EnemyVocalKind kind, AudioConfigSO cfg, out float gain)
        {
            gain = 0f;
            if (cfg == null || float.IsNaN(distanceM))
                return false;
            float audible = cfg.vocalAudibleM;
            float d = Mathf.Max(0f, distanceM);
            if (!(audible > 0f) || d > audible)
                return false;
            gain = Mathf.Clamp01(Volume(cfg, kind)) * (1f - d / audible);
            return true;
        }

        public static float Volume(AudioConfigSO cfg, EnemyVocalKind kind) =>
            kind == EnemyVocalKind.MutantAttack ? cfg.mutantAttackVolume : cfg.mutantAlertVolume;

        // 종류의 클립 배열 (없으면 null). 할당 없음.
        public static AudioClip[] Clips(AudioConfigSO cfg, EnemyVocalKind kind)
        {
            if (cfg == null)
                return null;
            return kind == EnemyVocalKind.MutantAttack ? cfg.mutantAttackClips : cfg.mutantAlertClips;
        }
    }
}
