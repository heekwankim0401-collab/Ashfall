using System;
using Ashfall.Core;
using Ashfall.Player;
using NUnit.Framework;
using Object = UnityEngine.Object;

namespace Ashfall.Tests.EditMode
{
    // M7 §4.3 수분 0 전신 피해(Q33)·방사선 피폭 최대 HP ×0.8(Q35)·전신 등급 위임 (BodyHealthModel, M7 설계 §4.5)
    public class BodyHealthSystemicTests
    {
        private PlayerConfigSO config;
        private BodyHealthModel body;
        private SurvivalModel survival;
        private RadiationDoseModel radiation;
        private int statusChanged;

        private static readonly BodyPart[] Parts = (BodyPart[])Enum.GetValues(typeof(BodyPart));

        [SetUp]
        public void SetUp()
        {
            config = SurvivalModelTests.Config();
            body = new BodyHealthModel(config, new FixedRandom(0.99));
            survival = new SurvivalModel(config);
            radiation = new RadiationDoseModel();
            body.AttachSurvival(survival, radiation);
            statusChanged = 0;
            body.StatusChanged += () => statusChanged++;
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(config);

        [Test]
        public void SystemicDamage_EveryIntactPart_PerPartRate()
        {
            for (int i = 0; i < 60; i++)
                body.ApplySystemicDamage(config.dehydratedHpPerSec / 60f);
            foreach (BodyPart part in Parts)
            {
                // 리뷰 7C 1 결정 (b): 머리는 탈수 피해를 받지 않는다 (Q33 "흉부 80 → 160초")
                float expected = part == BodyPart.Head ? config.MaxHp(part) : config.MaxHp(part) - 0.5f;
                Assert.AreEqual(expected, body.GetHp(part), 1e-3f, $"{part}: 머리 제외 부위마다 0.5/s (합계 나눔 아님)");
            }
        }

        [Test]
        public void SystemicDamage_SkipsDestroyedParts_NoRedistribution()
        {
            body.ApplyDamage(BodyPart.LeftArm, 1000f);   // 파괴 (분산 없음 — 이미 0이 아니었다)
            float thorax = body.GetHp(BodyPart.Thorax);
            float stomach = body.GetHp(BodyPart.Stomach);
            body.ApplySystemicDamage(1f);
            Assert.AreEqual(0f, body.GetHp(BodyPart.LeftArm));
            Assert.AreEqual(thorax - 1f, body.GetHp(BodyPart.Thorax), 1e-4f, "파괴 부위 몫이 흉부로 가지 않는다");
            Assert.AreEqual(stomach - 1f, body.GetHp(BodyPart.Stomach), 1e-4f);
        }

        [Test]
        public void SystemicDamage_ThoraxZero_Dies()
        {
            BodyPart? cause = null;
            body.Died += p => cause = p;
            body.ApplyDamage(BodyPart.Thorax, config.thoraxMaxHp - 1f);   // 흉부 1
            body.ApplySystemicDamage(0.5f);
            Assert.IsFalse(body.IsDead);
            body.ApplySystemicDamage(0.5f);
            Assert.IsTrue(body.IsDead, "흉부 0 = 사망");
            Assert.AreEqual(BodyPart.Thorax, body.DeathCause);
            Assert.AreEqual(BodyPart.Thorax, cause);
        }

        [Test]
        public void SystemicDamage_FullHealth_ThoraxReachesZeroFirst_At160Seconds()
        {
            // 7C-2a는 머리(35)가 70초에 먼저 0 → 사인 머리였다(HeadReachesZeroFirst). Q33 A 문구 "흉부 80 → 160초"에 맞추려고
            // 리뷰 7C 1 결정 (b)로 탈수 피해에서 머리를 뺐다 → 흉부가 먼저(160초) 0, 사인 흉부.
            // TODO(GDD-?) Q33: 전신 사인 문구("탈수")는 MVP 남은 목록.
            for (int i = 0; i < 159; i++)
                body.ApplySystemicDamage(config.dehydratedHpPerSec);
            Assert.IsFalse(body.IsDead, "159초: 흉부 0.5 남음");
            Assert.AreEqual(config.MaxHp(BodyPart.Head), body.GetHp(BodyPart.Head), "머리는 그대로");
            body.ApplySystemicDamage(config.dehydratedHpPerSec);
            Assert.IsTrue(body.IsDead, "160초에 흉부 0 = 사망");
            Assert.AreEqual(BodyPart.Thorax, body.DeathCause);
        }

        // 리뷰 7C 5: 사망하면 같은 호출의 남은 부위(흉부 다음 순서)는 깎지 않는다
        [Test]
        public void SystemicDamage_DeathStopsRemainingParts()
        {
            body.ApplyDamage(BodyPart.Thorax, config.thoraxMaxHp - 1f);   // 흉부 1
            float stomach = body.GetHp(BodyPart.Stomach);
            float legs = body.GetHp(BodyPart.RightLeg);
            body.ApplySystemicDamage(1f);
            Assert.IsTrue(body.IsDead);
            Assert.AreEqual(BodyPart.Thorax, body.DeathCause);
            Assert.AreEqual(stomach, body.GetHp(BodyPart.Stomach), 1e-4f, "사망 뒤 복부 그대로");
            Assert.AreEqual(legs, body.GetHp(BodyPart.RightLeg), 1e-4f, "사망 뒤 다리 그대로");
        }

        [Test]
        public void SystemicDamage_HeadNeverDamaged()
        {
            body.ApplyDamage(BodyPart.Head, 1f);
            float head = body.GetHp(BodyPart.Head);
            body.ApplySystemicDamage(10f);
            Assert.AreEqual(head, body.GetHp(BodyPart.Head), 1e-4f);
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void SystemicDamage_InvalidAmount_Ignored(float amount)
        {
            body.ApplySystemicDamage(amount);
            foreach (BodyPart part in Parts)
                Assert.AreEqual(config.MaxHp(part), body.GetHp(part));
        }

        [Test]
        public void SystemicDamage_AfterDeath_Ignored()
        {
            body.ApplyDamage(BodyPart.Head, 1000f);
            float legs = body.GetHp(BodyPart.LeftLeg);
            body.ApplySystemicDamage(5f);
            Assert.AreEqual(legs, body.GetHp(BodyPart.LeftLeg));
        }

        [Test]
        public void MaxHpMultiplier_ClampsCurrentHp_AllParts()
        {
            body.ApplyDamage(BodyPart.LeftLeg, 30f);   // 35/65 → 새 최대 52 아래라 그대로
            body.MaxHpMultiplier = 0.8f;
            foreach (BodyPart part in Parts)
            {
                Assert.AreEqual(config.MaxHp(part) * 0.8f, body.GetMaxHp(part), 1e-4f, $"{part} 최대 × 0.8");
                if (part != BodyPart.LeftLeg)
                    Assert.AreEqual(config.MaxHp(part) * 0.8f, body.GetHp(part), 1e-4f, $"{part} 현재 HP를 새 최대로 자름");
            }
            Assert.AreEqual(35f, body.GetHp(BodyPart.LeftLeg), 1e-4f, "새 최대보다 낮으면 그대로");
            Assert.AreEqual(HealthTier.Healthy, body.GetTier(BodyPart.Thorax), "가득 = 새 최대 기준 Healthy");

            body.MaxHpMultiplier = 1f;
            Assert.AreEqual(config.thoraxMaxHp, body.GetMaxHp(BodyPart.Thorax), 1e-4f);
            Assert.AreEqual(config.thoraxMaxHp * 0.8f, body.GetHp(BodyPart.Thorax), 1e-4f, "배율 복구 = HP 회복 없음 (Q36)");
            Assert.AreEqual(HealthTier.Healthy, body.GetTier(BodyPart.Thorax), "0.8 ≥ 부상 경계 0.75");
        }

        [Test]
        public void MaxHpMultiplier_HealCappedAtNewMax()
        {
            body.MaxHpMultiplier = 0.8f;
            body.ApplyDamage(BodyPart.Thorax, 20f);
            body.Heal(BodyPart.Thorax, 100f);
            Assert.AreEqual(64f, body.GetHp(BodyPart.Thorax), 1e-4f);
        }

        [Test]
        public void MaxHpMultiplier_TierRecomputed()
        {
            body.ApplyDamage(BodyPart.Thorax, 21f);   // 59/80 = 0.74 → 부상
            Assert.AreEqual(HealthTier.Injured, body.GetTier(BodyPart.Thorax));
            body.MaxHpMultiplier = 0.8f;                // 59/64 = 0.92 → 정상
            Assert.AreEqual(HealthTier.Healthy, body.GetTier(BodyPart.Thorax), "등급은 새 최대 기준");
        }

        [Test]
        public void SyncRadiation_Dose100_AppliesSickness_ThenStaysAfterMore()
        {
            radiation.Tick(99f, 1f);
            body.SyncRadiation();
            Assert.AreEqual(1f, body.MaxHpMultiplier);
            Assert.AreEqual(SurvivalTier.Low, body.GetSurvivalTier(SurvivalStat.Radiation), "≥ 50 오염");

            radiation.Tick(1f, 1f);
            body.SyncRadiation();
            Assert.AreEqual(0.8f, body.MaxHpMultiplier, 1e-6f, "선량 100 → 최대 HP × 0.8");
            Assert.AreEqual(SurvivalTier.Critical, body.GetSurvivalTier(SurvivalStat.Radiation));
            Assert.AreEqual(config.headMaxHp * 0.8f, body.GetHp(BodyPart.Head), 1e-4f);

            radiation.Tick(100f, 1f);
            body.SyncRadiation();
            Assert.AreEqual(0.8f, body.MaxHpMultiplier, 1e-6f, "200에도 추가 효과 없음 (Q35)");
            Assert.AreEqual(config.headMaxHp * 0.8f, body.GetMaxHp(BodyPart.Head), 1e-4f);
        }

        [Test]
        public void SurvivalTier_DelegatesToAttachedModels()
        {
            survival.Tick(800f, false);   // 수분 20(Low), 에너지 44(Normal)
            Assert.AreEqual(SurvivalTier.Low, body.GetSurvivalTier(SurvivalStat.Hydration));
            Assert.AreEqual(SurvivalTier.Normal, body.GetSurvivalTier(SurvivalStat.Energy));
            Assert.AreEqual(SurvivalTier.Normal, body.GetSurvivalTier(SurvivalStat.Radiation));
            IBodyStatusView view = body;
            Assert.AreEqual(SurvivalTier.Low, view.GetSurvivalTier(SurvivalStat.Hydration), "IBodyStatusView로도 등급만");
        }

        [Test]
        public void SurvivalTier_WithoutAttach_Normal()
        {
            var plain = new BodyHealthModel(config, new FixedRandom(0.99));
            foreach (SurvivalStat stat in Enum.GetValues(typeof(SurvivalStat)))
                Assert.AreEqual(SurvivalTier.Normal, plain.GetSurvivalTier(stat));
            plain.SyncRadiation();   // 생존 상태 없음 = 아무것도 안 함
            Assert.AreEqual(1f, plain.MaxHpMultiplier);
        }

        [Test]
        public void StatusChanged_OnSurvivalTierChange_Only()
        {
            survival.Tick(100f, false);
            Assert.AreEqual(0, statusChanged, "등급 변화 없음");
            survival.Tick(660f, false);   // 수분 Low
            Assert.AreEqual(1, statusChanged, "수분 Low → StatusChanged");
            survival.Tick(1f, false);
            Assert.AreEqual(1, statusChanged);

            radiation.Tick(50f, 1f);
            body.SyncRadiation();
            Assert.AreEqual(2, statusChanged, "방사능 Low");
            body.SyncRadiation();
            Assert.AreEqual(2, statusChanged, "같은 등급 — 매 프레임 내지 않는다");
            radiation.Tick(50f, 1f);
            body.SyncRadiation();
            Assert.AreEqual(3, statusChanged, "피폭 (최대 HP 배율 + 등급)");

            radiation.Reset();
            body.SyncRadiation();
            Assert.AreEqual(4, statusChanged, "해독 → Normal");
            Assert.AreEqual(1f, body.MaxHpMultiplier);
        }
    }
}
