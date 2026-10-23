using System;
using Ashfall.Core;
using UnityEngine;

namespace Ashfall.Player
{
    // M7 §4.3 STAT-DEHY(수분)·STAT-HUNG(에너지). 순수 C# (M7 설계 §1.5·§3.6, 7C-2a).
    // 수분 100에서 −0.1/s(복부 파괴 중 × 2, Q34), 에너지 100에서 −0.07/s. 0에서 멈춘다.
    // 효과(수분 0 → 부위마다 HP 감소, 에너지 0 → 스태미나 재생 × 0.5)는 PlayerHealth·PlayerMotor가 이 상태를 읽어 적용한다.
    // 레이드마다 최대에서 시작한다(플레이어가 레이드 씬과 함께 새로 생긴다, 이월 없음 Q32).
    // RULE-05: 수치는 Player 안에만 — UI에는 IBodyStatusView.GetSurvivalTier(등급)만 나간다.
    public sealed class SurvivalModel
    {
        private readonly PlayerConfigSO config;
        private SurvivalTier hydrationTier;
        private SurvivalTier energyTier;

        // 수치 읽기는 Player 어셈블리·테스트용. UI·다른 기능에는 등급만 (RULE-05).
        public float Hydration { get; private set; }
        public float Energy { get; private set; }
        public float HydrationMax => config.hydrationMax;
        public float EnergyMax => config.energyMax;

        public bool IsDehydrated => Hydration <= 0f;
        public bool IsStarving => Energy <= 0f;

        // 수분·에너지 등급이 바뀔 때만 (방사능 등급은 BodyHealthModel이 선량으로 따로 본다)
        public event Action TierChanged;

        public SurvivalModel(PlayerConfigSO config)
        {
            this.config = config ? config : throw new ArgumentNullException(nameof(config));
            Hydration = Mathf.Max(0f, config.hydrationMax);
            Energy = Mathf.Max(0f, config.energyMax);
            hydrationTier = TierOf(Hydration, config.hydrationMax);
            energyTier = TierOf(Energy, config.energyMax);
        }

        // dt초 감소. dt가 0 이하·NaN·무한이면 무시.
        public void Tick(float dt, bool stomachDestroyed)
        {
            if (!(dt > 0f) || float.IsInfinity(dt))
                return;
            float hydrationRate = config.hydrationDecayPerSec * (stomachDestroyed ? config.stomachDestroyedHydrationMultiplier : 1f);
            Hydration = Decay(Hydration, hydrationRate * dt);
            Energy = Decay(Energy, config.energyDecayPerSec * dt);
            RefreshTiers();
        }

        // 물·음식 (Q42). 최대에서 자른다. 음수·NaN은 0으로 본다.
        public void Restore(float hydration, float energy)
        {
            if (hydration > 0f)
                Hydration = Mathf.Min(config.hydrationMax, Hydration + hydration);
            if (energy > 0f)
                Energy = Mathf.Min(config.energyMax, Energy + energy);
            RefreshTiers();
        }

        // 수분·에너지 등급. Radiation은 선량이 필요해 여기서는 Normal (BodyHealthModel.GetSurvivalTier가 RadiationTier로 판정).
        public SurvivalTier GetTier(SurvivalStat stat)
        {
            switch (stat)
            {
                case SurvivalStat.Hydration: return hydrationTier;
                case SurvivalStat.Energy: return energyTier;
                default: return SurvivalTier.Normal;
            }
        }

        // RULE-05 등급 경계 (TODO(GDD-?) Q43): 0 = Critical(탈수·허기), 최대 × survivalLowRatio 이하 = Low(목마름·배고픔).
        public SurvivalTier TierOf(float value, float max)
        {
            if (value <= 0f)
                return SurvivalTier.Critical;
            if (value <= max * config.survivalLowRatio)
                return SurvivalTier.Low;
            return SurvivalTier.Normal;
        }

        // 방사능 등급 (TODO(GDD-?) Q43): 선량 ≥ radiationSicknessDose = Critical(피폭, 최대 HP 감소 중), ≥ radiationWarnDose = Low(오염).
        public static SurvivalTier RadiationTier(float dose, PlayerConfigSO config)
        {
            if (config == null)
                return SurvivalTier.Normal;
            if (dose >= config.radiationSicknessDose)
                return SurvivalTier.Critical;
            if (dose >= config.radiationWarnDose)
                return SurvivalTier.Low;
            return SurvivalTier.Normal;
        }

        private static float Decay(float value, float amount) => amount > 0f ? Mathf.Max(0f, value - amount) : value;

        private void RefreshTiers()
        {
            SurvivalTier h = TierOf(Hydration, config.hydrationMax);
            SurvivalTier e = TierOf(Energy, config.energyMax);
            if (h == hydrationTier && e == energyTier)
                return;
            hydrationTier = h;
            energyTier = e;
            TierChanged?.Invoke();
        }
    }
}
