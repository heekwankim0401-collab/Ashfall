using Ashfall.Core;
using Ashfall.Player;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // M7 §4.3 STAT-DEHY·STAT-HUNG (SurvivalModel, M7 설계 §1.5·§3.6, Q32~Q34·Q43)
    public class SurvivalModelTests
    {
        private PlayerConfigSO config;
        private SurvivalModel model;

        // §3.6 값을 명시한다 (필드 기본값에 기대지 않게)
        internal static PlayerConfigSO Config()
        {
            PlayerConfigSO c = TestData.Player();
            c.hydrationMax = 100f; c.hydrationDecayPerSec = 0.1f; c.dehydratedHpPerSec = 0.5f;
            c.stomachDestroyedHydrationMultiplier = 2f;
            c.energyMax = 100f; c.energyDecayPerSec = 0.07f; c.starvingStaminaRegenMultiplier = 0.5f;
            c.radiationSicknessDose = 100f; c.radiationSicknessMaxHpMultiplier = 0.8f; c.radiationWarnDose = 50f;
            c.survivalLowRatio = 0.25f;
            return c;
        }

        [SetUp]
        public void SetUp()
        {
            config = Config();
            model = new SurvivalModel(config);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(config);

        [Test]
        public void StartsAtMax_Normal()
        {
            Assert.AreEqual(100f, model.Hydration);
            Assert.AreEqual(100f, model.Energy);
            Assert.AreEqual(SurvivalTier.Normal, model.GetTier(SurvivalStat.Hydration));
            Assert.AreEqual(SurvivalTier.Normal, model.GetTier(SurvivalStat.Energy));
            Assert.AreEqual(SurvivalTier.Normal, model.GetTier(SurvivalStat.Radiation), "방사능은 선량 판정(BodyHealthModel)");
            Assert.IsFalse(model.IsDehydrated);
            Assert.IsFalse(model.IsStarving);
        }

        [Test]
        public void DecayRates_Hydration01_Energy007()
        {
            for (int i = 0; i < 600; i++)
                model.Tick(1f / 60f, false);
            Assert.AreEqual(99f, model.Hydration, 0.01f, "−0.1/s × 10초 (프레임 누적 float 오차 허용)");
            Assert.AreEqual(99.3f, model.Energy, 0.01f, "−0.07/s × 10초");
        }

        [Test]
        public void StomachDestroyed_DoublesHydrationDecay_EnergyUnchanged()
        {
            model.Tick(10f, true);
            Assert.AreEqual(98f, model.Hydration, 1e-3f, "× 2 (Q34)");
            Assert.AreEqual(99.3f, model.Energy, 1e-3f, "에너지는 그대로");
        }

        [Test]
        public void ReachesZero_ThenStops()
        {
            model.Tick(999f, false);
            Assert.IsFalse(model.IsDehydrated);
            model.Tick(1f, false);
            Assert.AreEqual(0f, model.Hydration, 1e-3f, "1000초에 0");
            model.Tick(500f, false);
            Assert.AreEqual(0f, model.Hydration, "0 아래로 가지 않는다");
            Assert.AreEqual(0f, model.Energy, "약 1429초에 0");
            Assert.IsTrue(model.IsDehydrated);
            Assert.IsTrue(model.IsStarving);
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void InvalidDt_Ignored(float dt)
        {
            model.Tick(dt, false);
            Assert.AreEqual(100f, model.Hydration);
            Assert.AreEqual(100f, model.Energy);
        }

        [Test]
        public void TierBoundaries_LowAt25Percent_CriticalAtZero()
        {
            model.Tick(740f, false);   // 수분 26
            Assert.AreEqual(SurvivalTier.Normal, model.GetTier(SurvivalStat.Hydration));
            model.Tick(20f, false);    // 수분 24 ≤ 최대 × 0.25 → Low
            Assert.AreEqual(SurvivalTier.Low, model.GetTier(SurvivalStat.Hydration), "25% 이하 = Low");
            model.Tick(239f, false);   // 0.1
            Assert.AreEqual(SurvivalTier.Low, model.GetTier(SurvivalStat.Hydration));
            model.Tick(2f, false);
            Assert.AreEqual(SurvivalTier.Critical, model.GetTier(SurvivalStat.Hydration), "0 = Critical");
        }

        [Test]
        public void TierOf_Boundaries()
        {
            Assert.AreEqual(SurvivalTier.Normal, model.TierOf(25.01f, 100f));
            Assert.AreEqual(SurvivalTier.Low, model.TierOf(25f, 100f));
            Assert.AreEqual(SurvivalTier.Low, model.TierOf(0.01f, 100f));
            Assert.AreEqual(SurvivalTier.Critical, model.TierOf(0f, 100f));
        }

        [Test]
        public void RadiationTier_WarnAt50_CriticalAt100()
        {
            Assert.AreEqual(SurvivalTier.Normal, SurvivalModel.RadiationTier(0f, config));
            Assert.AreEqual(SurvivalTier.Normal, SurvivalModel.RadiationTier(49.99f, config));
            Assert.AreEqual(SurvivalTier.Low, SurvivalModel.RadiationTier(50f, config), "방사능 오염 (Q43)");
            Assert.AreEqual(SurvivalTier.Low, SurvivalModel.RadiationTier(99.99f, config));
            Assert.AreEqual(SurvivalTier.Critical, SurvivalModel.RadiationTier(100f, config), "방사선 피폭");
            Assert.AreEqual(SurvivalTier.Critical, SurvivalModel.RadiationTier(250f, config));
        }

        [Test]
        public void TierChanged_OnlyWhenTierChanges()
        {
            int count = 0;
            model.TierChanged += () => count++;
            model.Tick(100f, false);
            Assert.AreEqual(0, count, "Normal 유지");
            model.Tick(660f, false);   // 수분 24 → Low
            Assert.AreEqual(1, count);
            model.Tick(1f, false);
            Assert.AreEqual(1, count, "Low 유지 — 틱마다 내지 않는다");
            model.Restore(50f, 0f);
            Assert.AreEqual(2, count, "Restore로 Normal 복귀");
        }

        [Test]
        public void Restore_CappedAtMax()
        {
            model.Tick(300f, false);   // 수분 70, 에너지 79
            model.Restore(50f, 60f);
            Assert.AreEqual(100f, model.Hydration, "최대에서 자른다");
            Assert.AreEqual(100f, model.Energy);
        }

        [Test]
        public void Restore_FromZero_AndIgnoresNegative()
        {
            model.Tick(2000f, false);
            model.Restore(50f, 0f);
            Assert.AreEqual(50f, model.Hydration, 1e-4f);
            Assert.AreEqual(0f, model.Energy);
            Assert.IsFalse(model.IsDehydrated);
            model.Restore(-30f, float.NaN);
            Assert.AreEqual(50f, model.Hydration, 1e-4f, "음수 무시");
            Assert.AreEqual(0f, model.Energy, "NaN 무시");
        }
    }

    // §4.1 + M7 §4.3 STAT-HUNG 스태미나 재생 배율 (StaminaModel.Tick regenMultiplier, PlayerMotor.StaminaRegenMultiplier)
    // 설계 §6의 "StaminaModelTests 확장" — 기존 스태미나 테스트(PlayerMovementTests)는 손대지 않고 여기 둔다.
    public class StaminaRegenMultiplierTests
    {
        private PlayerConfigSO config;

        [SetUp]
        public void SetUp() => config = SurvivalModelTests.Config();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(config);

        private StaminaModel Drained()
        {
            var stamina = new StaminaModel(config);
            Assert.IsTrue(stamina.TrySpend(80f));
            return stamina;
        }

        [Test]
        public void DefaultMultiplier_IsOne()
        {
            StaminaModel stamina = Drained();
            stamina.Tick(1f, false, false);
            Assert.AreEqual(40f, stamina.Current, 1e-4f, "정지 20/s 그대로");
        }

        [Test]
        public void HalfMultiplier_HalvesRegen_StationaryAndMoving()
        {
            StaminaModel stamina = Drained();
            stamina.Tick(1f, false, false, config.starvingStaminaRegenMultiplier);
            Assert.AreEqual(30f, stamina.Current, 1e-4f, "정지 20/s × 0.5");
            stamina.Tick(1f, false, true, config.starvingStaminaRegenMultiplier);
            Assert.AreEqual(35f, stamina.Current, 1e-4f, "이동 10/s × 0.5");
        }

        [Test]
        public void Multiplier_DoesNotAffectSprintDrain()
        {
            var stamina = new StaminaModel(config);
            stamina.Tick(1f, true, true, 0.5f);
            Assert.AreEqual(85f, stamina.Current, 1e-4f, "질주 15/s 그대로");
        }

        [TestCase(-1f)]
        [TestCase(float.NaN)]
        public void InvalidMultiplier_NoRegen(float multiplier)
        {
            StaminaModel stamina = Drained();
            stamina.Tick(1f, false, false, multiplier);
            Assert.AreEqual(20f, stamina.Current, 1e-4f);
        }
    }

    // 리뷰 7C 6: 생존 설정 범위 가드 (인스펙터 [Min]/[Range] — 직렬화 값은 바꾸지 않는다)
    public class SurvivalConfigRangeTests
    {
        [TestCase(nameof(PlayerConfigSO.hydrationMax))]
        [TestCase(nameof(PlayerConfigSO.hydrationDecayPerSec))]
        [TestCase(nameof(PlayerConfigSO.dehydratedHpPerSec))]
        [TestCase(nameof(PlayerConfigSO.stomachDestroyedHydrationMultiplier))]
        [TestCase(nameof(PlayerConfigSO.energyMax))]
        [TestCase(nameof(PlayerConfigSO.energyDecayPerSec))]
        [TestCase(nameof(PlayerConfigSO.radiationSicknessDose))]
        [TestCase(nameof(PlayerConfigSO.radiationWarnDose))]
        public void Field_HasMinZero(string field)
        {
            var min = typeof(PlayerConfigSO).GetField(field).GetCustomAttributes(typeof(UnityEngine.MinAttribute), false);
            Assert.AreEqual(1, min.Length, field);
            Assert.AreEqual(0f, ((UnityEngine.MinAttribute)min[0]).min);
        }

        [TestCase(nameof(PlayerConfigSO.starvingStaminaRegenMultiplier))]
        [TestCase(nameof(PlayerConfigSO.radiationSicknessMaxHpMultiplier))]
        [TestCase(nameof(PlayerConfigSO.survivalLowRatio))]
        public void Field_HasRangeZeroToOne(string field)
        {
            var range = typeof(PlayerConfigSO).GetField(field).GetCustomAttributes(typeof(UnityEngine.RangeAttribute), false);
            Assert.AreEqual(1, range.Length, field);
            Assert.AreEqual(0f, ((UnityEngine.RangeAttribute)range[0]).min);
            Assert.AreEqual(1f, ((UnityEngine.RangeAttribute)range[0]).max);
        }
    }
}
