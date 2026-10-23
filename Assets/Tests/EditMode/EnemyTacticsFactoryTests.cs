using System.Collections.Generic;
using Ashfall.AI;
using Ashfall.Core;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // M7 7B-1 계약: EnemyArchetypeSO.tactics → EnemyBrain 전술 팩토리 (M7 설계 §1.4, Q20).
    // Auto = 기존 규칙(usesCover ? Raider : Scavenger), 명시 종류는 usesCover와 무관하게 그 전술.
    public class EnemyTacticsFactoryTests
    {
        private AiConfigSO config;
        private FakeEnemyWorld world;

        [SetUp]
        public void SetUp()
        {
            config = M3TestData.AiConfig();
            world = new FakeEnemyWorld();
        }

        private EnemyBrain Make(EnemyArchetypeSO archetype, IReadOnlyList<Vector3> route = null) =>
            new EnemyBrain(1, archetype, config, world, route, null, new System.Random(7));

        private static EnemyArchetypeSO With(EnemyArchetypeSO a, EnemyTacticsKind kind)
        {
            a.tactics = kind;
            return a;
        }

        // ── 기본값 (기존 에셋 호환) ──

        [Test]
        public void NewArchetype_M7Defaults_KeepLegacyBehaviour()
        {
            var a = ScriptableObject.CreateInstance<EnemyArchetypeSO>();
            Assert.AreEqual(EnemyTacticsKind.Auto, a.tactics);
            Assert.AreEqual(0f, a.anchorRadiusM);
            Assert.AreEqual(new Vector2(100f, 100f), a.weaponDurabilityRange);
            Assert.AreEqual(0f, a.meleeDamage, "근접 없음");
            Assert.AreEqual(0f, a.telegraphSec);
            Assert.AreEqual(0, (int)EnemyTacticsKind.Auto, "직렬화 값 0 = Auto (필드 없는 기존 에셋)");
        }

        [Test]
        public void AiConfig_M7Defaults()
        {
            Assert.AreEqual(0.35f, config.glintAngularSizeDeg);
            Assert.AreEqual(4f, config.vocalCooldownSec);
        }

        // ── Auto = 기존 규칙 ──

        [Test]
        public void Auto_UsesCover_IsRaider()
        {
            EnemyArchetypeSO rdr = M3TestData.Rdr();
            Assert.AreEqual(EnemyTacticsKind.Auto, rdr.tactics);
            Assert.AreEqual(EnemyTacticsKind.Raider, EnemyBrain.ResolveTacticsKind(rdr));
            Assert.AreEqual(EnemyTacticsKind.Raider, Make(rdr).TacticsKind);
        }

        [Test]
        public void Auto_NoCover_IsScavenger()
        {
            EnemyArchetypeSO scv = M3TestData.Scv();
            Assert.AreEqual(EnemyTacticsKind.Scavenger, EnemyBrain.ResolveTacticsKind(scv));
            Assert.AreEqual(EnemyTacticsKind.Scavenger, Make(scv).TacticsKind);
        }

        [Test]
        public void UndefinedValue_FallsBackToAutoRule()
        {
            EnemyArchetypeSO rdr = With(M3TestData.Rdr(), (EnemyTacticsKind)99);
            Assert.AreEqual(EnemyTacticsKind.Raider, EnemyBrain.ResolveTacticsKind(rdr));
            EnemyArchetypeSO scv = With(M3TestData.Scv(), (EnemyTacticsKind)(-1));
            Assert.AreEqual(EnemyTacticsKind.Scavenger, EnemyBrain.ResolveTacticsKind(scv));
        }

        [Test]
        public void Resolve_NullArchetype_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() => EnemyBrain.ResolveTacticsKind(null));
        }

        // ── 명시 종류 ──

        [TestCase(EnemyTacticsKind.Scavenger)]
        [TestCase(EnemyTacticsKind.Raider)]
        [TestCase(EnemyTacticsKind.Sniper)]
        [TestCase(EnemyTacticsKind.Mutant)]
        public void Explicit_IgnoresUsesCover(EnemyTacticsKind kind)
        {
            EnemyArchetypeSO withCover = With(M3TestData.Rdr(), kind);
            EnemyArchetypeSO noCover = With(M3TestData.Scv(), kind);
            Assert.AreEqual(kind, EnemyBrain.ResolveTacticsKind(withCover));
            Assert.AreEqual(kind, EnemyBrain.ResolveTacticsKind(noCover));
            Assert.AreEqual(kind, Make(withCover).TacticsKind);
            Assert.AreEqual(kind, Make(noCover).TacticsKind);
        }

        // 행동으로 확인: Raider 전술은 Alert에서 엄폐를 찾고, Scavenger 전술은 찾지 않는다(usesCover 값과 무관).
        [Test]
        public void ExplicitRaider_WithoutUsesCover_QueriesCover()
        {
            world.AddCover(new Vector3(3f, 0f, 0f), Vector3.forward);
            EnemyBrain brain = Make(With(M3TestData.Scv(), EnemyTacticsKind.Raider));
            brain.OnHeard(new Vector3(0f, 0f, 30f), NoiseKind.Gunshot, 0f);
            brain.Tick(AiTest.Unseen(0.1f, Vector3.zero), 0.1f);
            Assert.AreEqual(EnemyState.Alert, brain.State);
            Assert.Greater(world.TryFindCoverCalls, 0);
        }

        [Test]
        public void ExplicitScavenger_WithUsesCover_NeverQueriesCover()
        {
            world.AddCover(new Vector3(3f, 0f, 0f), Vector3.forward);
            EnemyBrain brain = Make(With(M3TestData.Rdr(), EnemyTacticsKind.Scavenger));
            brain.OnHeard(new Vector3(0f, 0f, 30f), NoiseKind.Gunshot, 0f);
            brain.Tick(AiTest.Unseen(0.1f, Vector3.zero), 0.1f);
            Assert.AreEqual(EnemyState.Alert, brain.State);
            Assert.AreEqual(0, world.TryFindCoverCalls);
            Assert.AreEqual(EnemyMoveMode.Run, brain.Intent.Move, "SCV: LKP로 달림");
        }

        // 계약 껍데기 저격수: 경로가 있어도 순찰·경계·수색에서 움직이지 않는다(퍼치 고정, TryTickRelaxed 훅).
        [Test]
        public void Sniper_NeverMoves_InPatrolAlertAndSearch()
        {
            var route = new List<Vector3> { new Vector3(0f, 0f, 10f), new Vector3(10f, 0f, 10f) };
            EnemyBrain brain = Make(With(M3TestData.Scv(), EnemyTacticsKind.Sniper), route);
            float t = 0f;
            for (int i = 0; i < 20; i++)
            {
                t += 0.2f;
                brain.Tick(AiTest.NoTarget(t, Vector3.zero), 0.2f);
                Assert.IsFalse(brain.Intent.HasDestination, $"평시 {brain.State}");
            }
            Assert.AreEqual(EnemyState.Patrol, brain.State, "전이는 EnemyBrain 그대로");

            // Alert → (alertTimeoutSec) → Search
            brain.OnHeard(new Vector3(0f, 0f, 30f), NoiseKind.Gunshot, t);
            for (int i = 0; i < 100 && brain.State != EnemyState.Search; i++)
            {
                t += 0.2f;
                brain.Tick(AiTest.Unseen(t, Vector3.zero), 0.2f);
                Assert.IsFalse(brain.Intent.HasDestination, $"{brain.State}");
            }
            Assert.AreEqual(EnemyState.Search, brain.State);
            for (int i = 0; i < 10; i++)
            {
                t += 0.2f;
                brain.Tick(AiTest.Unseen(t, Vector3.zero), 0.2f);
                Assert.IsFalse(brain.Intent.HasDestination, "수색도 제자리");
            }
        }

        // M7 의도 필드는 기존 전술에서 항상 false.
        [Test]
        public void Intent_M7Fields_DefaultFalse()
        {
            EnemyBrain brain = Make(M3TestData.Scv());
            brain.Tick(AiTest.Seen(0.1f, Vector3.zero, new Vector3(0f, 0f, 20f)), 0.1f);
            Assert.IsFalse(brain.Intent.Melee);
            Assert.IsFalse(brain.Intent.Telegraph);
        }
    }
}
