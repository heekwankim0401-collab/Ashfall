using Ashfall.AI;
using NUnit.Framework;

namespace Ashfall.Tests.EditMode
{
    // §7 적 3부위 = §4.2 피해 공식 축약 (M3 설계 §1.3, M3 AC4)
    public class EnemyBodyTests
    {
        private AiConfigSO config;

        [SetUp]
        public void SetUp() => config = M3TestData.AiConfig();

        private EnemyBodyModel Body() => new EnemyBodyModel(M3TestData.Scv(), config);

        [Test]
        public void MaxHp_FromArchetype()
        {
            EnemyBodyModel b = Body();
            Assert.AreEqual(35f, b.GetMaxHp(EnemyHitRegion.Head));
            Assert.AreEqual(80f, b.GetMaxHp(EnemyHitRegion.Body));
            Assert.AreEqual(65f, b.GetMaxHp(EnemyHitRegion.Legs));
            Assert.AreEqual(80f, b.GetHp(EnemyHitRegion.Body));
            Assert.IsFalse(b.IsDead);
            Assert.IsNull(b.DeathCause);
            Assert.AreEqual(1f, b.MoveSpeedMultiplier);
            Assert.IsTrue(b.SprintAllowed);
        }

        [Test]
        public void Head_OneRifleShot_Kills()
        {
            EnemyBodyModel b = Body();
            EnemyHitRegion? died = null;
            b.Died += r => died = r;
            b.ApplyHit(EnemyHitRegion.Head, 45f);
            Assert.IsTrue(b.IsDead);
            Assert.AreEqual(EnemyHitRegion.Head, b.DeathCause);
            Assert.AreEqual(EnemyHitRegion.Head, died);
            Assert.AreEqual(0f, b.GetHp(EnemyHitRegion.Head), "초과분은 버림");
        }

        [Test]
        public void Body_TwoRifleShots_Kill_PistolThree()
        {
            EnemyBodyModel rifle = Body();
            rifle.ApplyHit(EnemyHitRegion.Body, 45f);
            Assert.IsFalse(rifle.IsDead);
            rifle.ApplyHit(EnemyHitRegion.Body, 45f);
            Assert.IsTrue(rifle.IsDead);
            Assert.AreEqual(EnemyHitRegion.Body, rifle.DeathCause);

            EnemyBodyModel pistol = Body();
            pistol.ApplyHit(EnemyHitRegion.Body, 35f);
            pistol.ApplyHit(EnemyHitRegion.Body, 35f);
            Assert.IsFalse(pistol.IsDead);
            pistol.ApplyHit(EnemyHitRegion.Body, 35f);
            Assert.IsTrue(pistol.IsDead);
        }

        [Test]
        public void Legs_Destroyed_SlowAndNoSprint_NotDead()
        {
            EnemyBodyModel b = Body();
            b.ApplyHit(EnemyHitRegion.Legs, 45f);
            b.ApplyHit(EnemyHitRegion.Legs, 45f);
            Assert.IsTrue(b.IsDestroyed(EnemyHitRegion.Legs));
            Assert.AreEqual(0f, b.GetHp(EnemyHitRegion.Legs), "초과분 버림");
            Assert.IsFalse(b.IsDead);
            Assert.AreEqual(0.6f, b.MoveSpeedMultiplier, 1e-5f);
            Assert.IsFalse(b.SprintAllowed);
            Assert.AreEqual(80f, b.GetHp(EnemyHitRegion.Body), "파괴 순간의 초과분은 몸통으로 가지 않는다");
        }

        [Test]
        public void DestroyedLegs_FurtherDamage_70PercentToBody()
        {
            EnemyBodyModel b = Body();
            b.ApplyHit(EnemyHitRegion.Legs, 65f);
            b.ApplyHit(EnemyHitRegion.Legs, 50f);
            Assert.AreEqual(80f - 35f, b.GetHp(EnemyHitRegion.Body), 1e-4f);
            b.ApplyHit(EnemyHitRegion.Legs, 100f);   // 70 → 몸통 45 - 70 → 사망
            Assert.IsTrue(b.IsDead);
            Assert.AreEqual(EnemyHitRegion.Body, b.DeathCause);
        }

        [Test]
        public void AfterDeath_IgnoresHits()
        {
            EnemyBodyModel b = Body();
            int deaths = 0;
            b.Died += _ => deaths++;
            b.ApplyHit(EnemyHitRegion.Head, 100f);
            b.ApplyHit(EnemyHitRegion.Body, 100f);
            b.ApplyHit(EnemyHitRegion.Legs, 100f);
            Assert.AreEqual(1, deaths);
            Assert.AreEqual(80f, b.GetHp(EnemyHitRegion.Body));
            Assert.AreEqual(65f, b.GetHp(EnemyHitRegion.Legs));
        }

        [Test]
        public void ZeroOrNegativeDamage_Ignored()
        {
            EnemyBodyModel b = Body();
            b.ApplyHit(EnemyHitRegion.Body, 0f);
            b.ApplyHit(EnemyHitRegion.Body, -10f);
            Assert.AreEqual(80f, b.GetHp(EnemyHitRegion.Body));
        }

        [Test]
        public void HitResolver_Boundaries()
        {
            Assert.AreEqual(EnemyHitRegion.Head, EnemyHitResolver.Resolve(0.86f * 1.8f, 1.8f, config));
            Assert.AreEqual(EnemyHitRegion.Body, EnemyHitResolver.Resolve(0.84f * 1.8f, 1.8f, config));
            Assert.AreEqual(EnemyHitRegion.Body, EnemyHitResolver.Resolve(0.46f * 1.8f, 1.8f, config));
            Assert.AreEqual(EnemyHitRegion.Legs, EnemyHitResolver.Resolve(0.44f * 1.8f, 1.8f, config));
            Assert.AreEqual(EnemyHitRegion.Legs, EnemyHitResolver.Resolve(-0.1f, 1.8f, config));
            Assert.AreEqual(EnemyHitRegion.Legs, EnemyHitResolver.Resolve(1f, 0f, config), "높이 0이면 다리");
        }
    }
}
