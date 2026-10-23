using Ashfall.Core;
using Ashfall.Player;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // M7 물·음식·해독제 (Q42·Q36): 의약품 흐름 재사용. 부위 대상이 없고 전신 효과가 필요하면 시작(대상 = 흉부 표시),
    // 완료 시 수분·에너지 회복(상한) / 선량 0 + 최대 HP 배율 1(HP 회복 없음).
    public class MedicalSurvivalTests
    {
        private PlayerConfigSO config;
        private BodyHealthModel body;
        private SurvivalModel survival;
        private RadiationDoseModel radiation;
        private MedicalUseModel medical;
        private MedicalDefinitionSO water, food, antidote;

        [SetUp]
        public void SetUp()
        {
            config = SurvivalModelTests.Config();
            body = new BodyHealthModel(config, new FixedRandom(0.99));
            survival = new SurvivalModel(config);
            radiation = new RadiationDoseModel();
            body.AttachSurvival(survival, radiation);
            medical = new MedicalUseModel(body);
            // 7C-1b 데이터와 같은 모양: cures 빈 배열, healAmount 0 (TODO(GDD-?) Q42 값)
            water = Systemic("ITM-WTR-01", 3f, 2, hydration: 50f);
            food = Systemic("ITM-FOD-01", 4f, 1, energy: 60f);
            antidote = Systemic("MED-ANT-01", 3f, 1, cures: true);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(config);
            Object.DestroyImmediate(water);
            Object.DestroyImmediate(food);
            Object.DestroyImmediate(antidote);
        }

        private static MedicalDefinitionSO Systemic(string id, float useTime, int uses,
            float hydration = 0f, float energy = 0f, bool cures = false)
        {
            var m = ScriptableObject.CreateInstance<MedicalDefinitionSO>();
            m.id = id; m.displayName = id; m.size = Vector2Int.one; m.weightKg = 0.5f;
            m.cures = new StatusEffect[0]; m.healAmount = 0f; m.useTime = useTime; m.uses = uses;
            m.hydrationRestore = hydration; m.energyRestore = energy; m.curesRadiation = cures;
            return m;
        }

        [Test]
        public void AllFull_NotNeeded()
        {
            Assert.AreEqual(MedicalStartResult.NotNeeded, medical.TryStart(new MedicalItem(water)), "수분 최대");
            Assert.AreEqual(MedicalStartResult.NotNeeded, medical.TryStart(new MedicalItem(food)), "에너지 최대");
            Assert.AreEqual(MedicalStartResult.NotNeeded, medical.TryStart(new MedicalItem(antidote)), "선량 0 (Q36)");
            Assert.IsFalse(medical.IsUsing);
        }

        [Test]
        public void Water_Needed_WhenHydrationBelowMax_TargetThorax_RestoresCapped()
        {
            survival.Tick(300f, false);   // 수분 70
            Assert.AreEqual(MedicalStartResult.Started, medical.TryStart(new MedicalItem(water)));
            Assert.AreEqual(BodyPart.Thorax, medical.Target, "전신 표시 = 흉부");
            Assert.AreEqual(MedicalUseModel.SystemicTarget, medical.Target);
            Assert.AreEqual(3f, medical.RemainingSec);

            Assert.IsFalse(medical.Tick(2.9f));
            Assert.AreEqual(70f, survival.Hydration, 1e-3f, "사용 시간 동안 효과 없음");
            Assert.IsTrue(medical.Tick(0.2f));
            Assert.AreEqual(100f, survival.Hydration, 1e-4f, "+50이지만 최대에서 자름");
            Assert.AreEqual(79f, survival.Energy, 1e-3f, "물은 에너지 무관");
        }

        [Test]
        public void Water_NotNeeded_WhenOnlyEnergyLow()
        {
            survival.Tick(300f, false);
            survival.Restore(100f, 0f);   // 수분 최대, 에너지 79
            Assert.AreEqual(MedicalStartResult.NotNeeded, medical.TryStart(new MedicalItem(water)));
            Assert.AreEqual(MedicalStartResult.Started, medical.TryStart(new MedicalItem(food)));
        }

        [Test]
        public void Food_RestoresEnergy_FromZero_EndsStarving()
        {
            survival.Tick(2000f, false);
            Assert.IsTrue(survival.IsStarving);
            Assert.AreEqual(MedicalStartResult.Started, medical.TryStart(new MedicalItem(food)));
            Assert.IsTrue(medical.Tick(4f));
            Assert.AreEqual(60f, survival.Energy, 1e-4f);
            Assert.AreEqual(0f, survival.Hydration, "음식은 수분 무관");
            Assert.IsFalse(survival.IsStarving);
            Assert.AreEqual(SurvivalTier.Normal, body.GetSurvivalTier(SurvivalStat.Energy));
        }

        [Test]
        public void Antidote_Needed_WhenDosePositive_ResetsDose_RestoresMaxHp_NoHeal()
        {
            radiation.Tick(120f, 1f);
            body.SyncRadiation();
            Assert.AreEqual(0.8f, body.MaxHpMultiplier, 1e-6f);
            float thorax = body.GetHp(BodyPart.Thorax);   // 64

            Assert.AreEqual(MedicalStartResult.Started, medical.TryStart(new MedicalItem(antidote)));
            Assert.AreEqual(BodyPart.Thorax, medical.Target);
            Assert.IsTrue(medical.Tick(3f));
            Assert.AreEqual(0f, radiation.Dose, "선량 0");
            Assert.AreEqual(1f, body.MaxHpMultiplier, "최대 HP 원래대로");
            Assert.AreEqual(config.thoraxMaxHp, body.GetMaxHp(BodyPart.Thorax), 1e-4f);
            Assert.AreEqual(thorax, body.GetHp(BodyPart.Thorax), 1e-4f, "HP 회복 없음 (Q36)");
            Assert.AreEqual(SurvivalTier.Normal, body.GetSurvivalTier(SurvivalStat.Radiation));
        }

        [Test]
        public void Antidote_LowDose_StillNeeded()
        {
            radiation.Tick(1f, 0.5f);
            Assert.AreEqual(MedicalStartResult.Started, medical.TryStart(new MedicalItem(antidote)), "선량 > 0이면 사용");
        }

        [Test]
        public void PartTarget_TakesPriority_OverSystemic()
        {
            // 붕대 + 수분 회복을 모두 가진 아이템: 출혈 부위가 있으면 그 부위가 대상, 완료 시 수분도 회복
            MedicalDefinitionSO combo = TestData.Bandage();
            combo.hydrationRestore = 10f;
            try
            {
                survival.Tick(300f, false);
                body.AddStatus(BodyPart.LeftLeg, StatusEffect.Bleed);
                Assert.AreEqual(MedicalStartResult.Started, medical.TryStart(new MedicalItem(combo)));
                Assert.AreEqual(BodyPart.LeftLeg, medical.Target);
                Assert.IsTrue(medical.Tick(3f));
                Assert.IsFalse(body.HasStatus(BodyPart.LeftLeg, StatusEffect.Bleed));
                Assert.AreEqual(80f, survival.Hydration, 1e-3f);
            }
            finally
            {
                Object.DestroyImmediate(combo);
            }
        }

        [Test]
        public void ExistingMedical_Unchanged_NoSystemicNeed()
        {
            survival.Tick(300f, false);
            radiation.Tick(10f, 1f);
            Assert.AreEqual(MedicalStartResult.NotNeeded, medical.TryStart(new MedicalItem(TestData.Bandage())),
                "붕대는 수분·선량과 무관 — 출혈 없으면 불필요");
        }

        [Test]
        public void WithoutSurvivalAttached_SystemicItems_NotNeeded()
        {
            var plainBody = new BodyHealthModel(config, new FixedRandom(0.99));
            var plain = new MedicalUseModel(plainBody);
            Assert.AreEqual(MedicalStartResult.NotNeeded, plain.TryStart(new MedicalItem(water)));
            Assert.AreEqual(MedicalStartResult.NotNeeded, plain.TryStart(new MedicalItem(antidote)));
            Assert.IsNull(MedicalUseModel.FindTarget(plainBody, food));
        }

        [Test]
        public void Water_TwoUses_FlowUnchanged_DepletedRule()
        {
            survival.Tick(800f, false);   // 수분 20
            var item = new MedicalItem(water);
            Assert.AreEqual(2, item.UsesLeft);
            Assert.AreEqual(MedicalStartResult.Started, medical.TryStart(item));
            Assert.AreEqual(MedicalStartResult.Busy, medical.TryStart(new MedicalItem(food)), "사용 중 = Busy (기존 규칙)");
            Assert.IsTrue(medical.Tick(3f));
            Assert.AreEqual(70f, survival.Hydration, 1e-3f);
            Assert.AreEqual(MedicalStartResult.Depleted, medical.TryStart(new MedicalItem(water, 0)), "횟수 0 = Depleted");
        }

        [Test]
        public void DeadBody_Rejects()
        {
            survival.Tick(300f, false);
            body.ApplyDamage(BodyPart.Head, 1000f);
            Assert.AreEqual(MedicalStartResult.Dead, medical.TryStart(new MedicalItem(water)));
        }
    }
}
