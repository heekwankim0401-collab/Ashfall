using Ashfall.Core;
using UnityEngine;

namespace Ashfall.Audio
{
    // 발소리 음량·들리는 거리 (§11, M6 설계 §1.4). 순수.
    // 게인 = 걸음 음량 × (1 − d/들리는 거리)² × (내 발소리면 playerSelfVolume). 재질 음량은 지면 판정 뒤에 곱한다(FindSurface().volume).
    public static class FootstepMix
    {
        // voice 우선순위 (작을수록 높음, 설계 §3.5 고정 — 총성 > 금속 붕괴 > 적 발소리 > 내 발소리)
        public const int PriorityGunshot = 0;
        public const int PriorityCollapse = 1;
        public const int PriorityEnemyFootstep = 2;
        public const int PriorityPlayerFootstep = 3;
        // M7 무기 클릭(헛격발·고장·해제, 플레이어 자신): 내 발소리보다 높게(M7 설계 §4.9) — 적 발소리와 같은 단계. TODO(GDD-?)
        public const int PriorityWeaponClick = PriorityEnemyFootstep;
        // M7 변이체 울음(EnemyVocalized): 적 발소리보다 높게(M7 설계 §4.9) — 금속 붕괴와 같은 단계, 총성보다는 낮다. TODO(GDD-?)
        public const int PriorityEnemyVocal = PriorityCollapse;

        // 들리면 true(그때만 지면 레이캐스트를 한다). 들리는 거리 ≤ 0·NaN 거리면 false.
        public static bool Evaluate(float distanceM, FootstepGait gait, SoundSource source, AudioConfigSO cfg, out float gain)
        {
            gain = 0f;
            if (cfg == null || float.IsNaN(distanceM))
                return false;
            float audible = cfg.gaitAudibleM.Get(gait);
            float d = Mathf.Max(0f, distanceM);
            if (!(audible > 0f) || d > audible)
                return false;

            float t = 1f - d / audible;
            gain = Mathf.Max(0f, cfg.gaitVolume.Get(gait)) * t * t;
            if (source == SoundSource.Player)
                gain *= Mathf.Max(0f, cfg.playerSelfVolume);
            return true;
        }

        public static int Priority(SoundSource source) =>
            source == SoundSource.Player ? PriorityPlayerFootstep : PriorityEnemyFootstep;

        // 재질 세트 (없으면 null). 할당 없음.
        public static SurfaceFootsteps FindSurface(AudioConfigSO cfg, SurfaceKind kind)
        {
            if (cfg == null || cfg.footsteps == null)
                return null;
            for (int i = 0; i < cfg.footsteps.Length; i++)
            {
                SurfaceFootsteps set = cfg.footsteps[i];
                if (set != null && set.surface == kind)
                    return set;
            }
            return null;
        }
    }
}
