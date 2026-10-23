using System.Collections.Generic;
using Ashfall.Core;
using Ashfall.Player;
using Ashfall.Raid;
using Ashfall.UI;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // MVP 마무리 F1 전신 사인 (AC-F1, MVP_finish_design §1.2·§7, MF-Q1~Q4):
    // BodyHealthModel.DeathKind(피격 Wound·출혈 틱 Bleeding·수분 0 Dehydration) → PlayerDied.Kind → RaidSession.EndByDeath
    // → RaidResultBuilder → RaidResult.DeathKind → RaidResultViewModel.TitleFor "사망 — 탈수/출혈/부위".
    public class DeathCauseBodyTests
    {
        private PlayerConfigSO config;
        private BodyHealthModel body;
        private SurvivalModel survival;
        private RadiationDoseModel radiation;
        private readonly List<BodyPart> died = new List<BodyPart>();

        [SetUp]
        public void SetUp()
        {
            config = SurvivalModelTests.Config();
            body = new BodyHealthModel(config, new FixedRandom(0.99));   // 상태이상 굴림 없음
            survival = new SurvivalModel(config);
            radiation = new RadiationDoseModel();
            body.AttachSurvival(survival, radiation);
            died.Clear();
            body.Died += died.Add;
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(config);

        [Test]
        public void Alive_DefaultKind_Wound()
        {
            Assert.IsFalse(body.IsDead);
            Assert.AreEqual(DeathCauseKind.Wound, body.DeathKind);
        }

        [Test]
        public void Dehydration_160Seconds_KindDehydration_CauseThorax()
        {
            for (int i = 0; i < 160; i++)
                body.ApplySystemicDamage(config.dehydratedHpPerSec);
            Assert.IsTrue(body.IsDead, "160초에 흉부 0");
            Assert.AreEqual(BodyPart.Thorax, body.DeathCause, "사인 부위는 흉부 그대로 (기존 단언)");
            Assert.AreEqual(DeathCauseKind.Dehydration, body.DeathKind);
            CollectionAssert.AreEqual(new[] { BodyPart.Thorax }, died, "Died(BodyPart) 서명 그대로, 한 번");
        }

        [Test]
        public void BleedTick_ThoraxReachesZero_KindBleeding()
        {
            body.ApplyDamage(BodyPart.Thorax, config.thoraxMaxHp - 1f);   // 흉부 1 (피격, 아직 살아 있음)
            body.AddStatus(BodyPart.Thorax, StatusEffect.HeavyBleed);
            for (int i = 0; i < 5 && !body.IsDead; i++)
                body.Tick(1f);
            Assert.IsTrue(body.IsDead);
            Assert.AreEqual(BodyPart.Thorax, body.DeathCause);
            Assert.AreEqual(DeathCauseKind.Bleeding, body.DeathKind, "출혈 틱이 낸 사망 (MF-Q2)");
        }

        [Test]
        public void BleedTick_DestroyedLegRedistribution_KindBleeding()
        {
            body.ApplyDamage(BodyPart.LeftLeg, 1000f);                   // 다리 파괴 (분산 없음 — 멀쩡한 부위가 0이 됨)
            body.ApplyDamage(BodyPart.Thorax, config.thoraxMaxHp - 1f);   // 흉부 1
            Assert.IsFalse(body.IsDead);
            body.AddStatus(BodyPart.LeftLeg, StatusEffect.Bleed);         // 파괴 부위 출혈 → 70% 흉부·복부 분산
            for (int i = 0; i < 10 && !body.IsDead; i++)
                body.Tick(1f);
            Assert.IsTrue(body.IsDead, "분산 피해로 흉부 0");
            Assert.AreEqual(BodyPart.Thorax, body.DeathCause);
            Assert.AreEqual(DeathCauseKind.Bleeding, body.DeathKind, "파괴 부위 분산 사망도 출혈 (MF-Q2)");
        }

        [Test]
        public void Hit_KindWound()
        {
            body.ApplyHit(BodyPart.Head, 1000f);
            Assert.IsTrue(body.IsDead);
            Assert.AreEqual(BodyPart.Head, body.DeathCause);
            Assert.AreEqual(DeathCauseKind.Wound, body.DeathKind);
        }

        [Test]
        public void ApplyDamage_KindWound()
        {
            body.ApplyDamage(BodyPart.Thorax, 1000f);
            Assert.IsTrue(body.IsDead);
            Assert.AreEqual(DeathCauseKind.Wound, body.DeathKind);
        }

        [Test]
        public void HitWhileDehydratedAndBleeding_KindWound()
        {
            // 탈수·출혈로 깎이던 중이라도 마지막 한 방이 피격이면 피격 사망
            body.AddStatus(BodyPart.Thorax, StatusEffect.Bleed);
            body.ApplySystemicDamage(10f);
            body.Tick(1f);
            Assert.IsFalse(body.IsDead);
            body.ApplyHit(BodyPart.Thorax, 1000f);
            Assert.IsTrue(body.IsDead);
            Assert.AreEqual(DeathCauseKind.Wound, body.DeathKind);
        }

        [Test]
        public void BleedThenDehydrationKills_KindDehydration()
        {
            body.AddStatus(BodyPart.Thorax, StatusEffect.Bleed);
            body.Tick(1f);
            body.ApplySystemicDamage(1000f);
            Assert.IsTrue(body.IsDead);
            Assert.AreEqual(DeathCauseKind.Dehydration, body.DeathKind, "진입점마다 종류를 다시 정한다");
        }

        [Test]
        public void AfterDeath_OtherPathsDoNotChangeKind()
        {
            body.ApplySystemicDamage(1000f);
            Assert.IsTrue(body.IsDead);
            Assert.AreEqual(DeathCauseKind.Dehydration, body.DeathKind);

            body.AddStatus(BodyPart.Head, StatusEffect.HeavyBleed);   // 사망 뒤 무시
            body.Tick(10f);
            body.ApplyHit(BodyPart.Head, 1000f);
            body.ApplyDamage(BodyPart.Head, 1000f);
            Assert.AreEqual(DeathCauseKind.Dehydration, body.DeathKind, "사망 뒤 바뀌지 않음");
            Assert.AreEqual(BodyPart.Thorax, body.DeathCause);
            Assert.AreEqual(1, died.Count, "Died 한 번");
        }

        [Test]
        public void RadiationSickness_ClampsButNeverKills()
        {
            // MF-Q3: 피폭(최대 HP × 0.8)은 현재 HP를 자를 뿐 0으로 만들지 않는다 → 방사능 사인 없음
            body.ApplyDamage(BodyPart.Thorax, config.thoraxMaxHp * 0.5f);
            radiation.Tick(500f, 1f);
            body.SyncRadiation();
            Assert.AreEqual(0.8f, body.MaxHpMultiplier, 1e-6f);
            Assert.IsFalse(body.IsDead, "피폭만으로는 죽지 않는다");
            foreach (BodyPart part in System.Enum.GetValues(typeof(BodyPart)))
                Assert.Greater(body.GetHp(part), 0f, $"{part} HP > 0");
            Assert.AreEqual(0, died.Count);
        }
    }

    public class DeathCauseEventTests
    {
        [Test]
        public void PlayerDied_LegacyConstructor_IsWound()
        {
            var evt = new PlayerDied(BodyPart.Head);
            Assert.AreEqual(BodyPart.Head, evt.Cause);
            Assert.AreEqual(DeathCauseKind.Wound, evt.Kind);
        }

        [Test]
        public void PlayerDied_WithKind()
        {
            var evt = new PlayerDied(BodyPart.Thorax, DeathCauseKind.Dehydration);
            Assert.AreEqual(BodyPart.Thorax, evt.Cause);
            Assert.AreEqual(DeathCauseKind.Dehydration, evt.Kind);
        }

        [Test]
        public void RaidResult_OptionalKindOmitted_IsWound()
        {
            var result = new RaidResult(RaidOutcome.Killed, null, null, true, BodyPart.Thorax, 1f, 0, null, null, null);
            Assert.AreEqual(DeathCauseKind.Wound, result.DeathKind);
            var withKind = new RaidResult(RaidOutcome.Killed, null, null, true, BodyPart.Thorax, 1f, 0, null, null, null,
                deathKind: DeathCauseKind.Bleeding);
            Assert.AreEqual(DeathCauseKind.Bleeding, withKind.DeathKind);
        }
    }

    public class DeathCauseSessionTests
    {
        private RaidConfigSO config;
        private readonly List<ItemDefinitionSO> broughtIn = new List<ItemDefinitionSO>();
        private readonly List<ItemInstance> carried = new List<ItemInstance>();

        [SetUp]
        public void SetUp() => config = M4TestData.RaidConfig();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(config);

        private RaidSession Session() => new RaidSession(config, new List<ExtractionModel>
        {
            new ExtractionModel(M4TestData.Find(config, M4TestData.Ext01), new Vector3(0f, 0f, -470f), true),
        });

        [TestCase(DeathCauseKind.Wound)]
        [TestCase(DeathCauseKind.Bleeding)]
        [TestCase(DeathCauseKind.Dehydration)]
        public void EndByDeath_Kind_FlowsToResult(DeathCauseKind kind)
        {
            RaidSession session = Session();
            session.EndByDeath(BodyPart.Thorax, kind);
            Assert.AreEqual(kind, session.DeathKind);
            RaidResult result = RaidResultBuilder.Build(session, broughtIn, carried);
            Assert.AreEqual(RaidOutcome.Killed, result.Outcome);
            Assert.IsTrue(result.HasDeathCause);
            Assert.AreEqual(BodyPart.Thorax, result.DeathCause);
            Assert.AreEqual(kind, result.DeathKind);
        }

        [Test]
        public void EndByDeath_KindOmitted_IsWound()
        {
            RaidSession session = Session();
            session.EndByDeath(BodyPart.Head);
            Assert.AreEqual(DeathCauseKind.Wound, RaidResultBuilder.Build(session, broughtIn, carried).DeathKind);
        }

        [Test]
        public void EndByDeath_AfterOver_DoesNotChangeKind()
        {
            RaidSession session = Session();
            session.EndByDeath(BodyPart.Thorax, DeathCauseKind.Dehydration);
            session.EndByDeath(BodyPart.Head, DeathCauseKind.Bleeding);
            Assert.AreEqual(DeathCauseKind.Dehydration, session.DeathKind);
            Assert.AreEqual(BodyPart.Thorax, session.DeathCause);
        }

        [Test]
        public void UnknownDeath_NoCause_TitleIsDeathOnly()
        {
            RaidSession session = Session();
            session.EndByDeath();
            RaidResult result = RaidResultBuilder.Build(session, broughtIn, carried);
            Assert.IsFalse(result.HasDeathCause);
            Assert.AreEqual("사망", RaidResultViewModel.TitleFor(result));
        }
    }

    public class DeathCauseTitleTests
    {
        private static RaidResult Killed(bool hasCause, BodyPart part, DeathCauseKind kind) =>
            new RaidResult(RaidOutcome.Killed, null, null, hasCause, part, 60f, 0, null, null, null, null, kind);

        [Test]
        public void Title_Dehydration() =>
            Assert.AreEqual("사망 — 탈수", RaidResultViewModel.TitleFor(Killed(true, BodyPart.Thorax, DeathCauseKind.Dehydration)));

        [Test]
        public void Title_Bleeding_NoPart() =>
            Assert.AreEqual("사망 — 출혈", RaidResultViewModel.TitleFor(Killed(true, BodyPart.Thorax, DeathCauseKind.Bleeding)));

        [TestCase(BodyPart.Thorax, "사망 — 흉부")]
        [TestCase(BodyPart.Head, "사망 — 머리")]
        public void Title_Wound_Part(BodyPart part, string expected) =>
            Assert.AreEqual(expected, RaidResultViewModel.TitleFor(Killed(true, part, DeathCauseKind.Wound)));

        [Test]
        public void Title_NoCause_DeathOnly_EvenWithKind() =>
            Assert.AreEqual("사망", RaidResultViewModel.TitleFor(Killed(false, BodyPart.Head, DeathCauseKind.Dehydration)));

        [Test]
        public void ViewModel_UsesTitle()
        {
            Assert.AreEqual("사망 — 탈수",
                RaidResultViewModel.From(Killed(true, BodyPart.Thorax, DeathCauseKind.Dehydration)).Title);
        }

        [Test]
        public void UiText_Words()
        {
            Assert.AreEqual("탈수", UiText.DeathDehydration);
            Assert.AreEqual("출혈", UiText.DeathBleeding);
        }
    }
}
