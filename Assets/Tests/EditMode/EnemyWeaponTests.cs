using System.Collections.Generic;
using Ashfall.AI;
using Ashfall.Core;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // 적 무장 (M3 설계 §1.4): 점사/반자동 패턴, 탄 소모·재장전, 피해 = 무기 값 (§5.2, M3 AC2·AC4)
    public class EnemyWeaponTests
    {
        private const float Dt = 1f / 60f;
        private GameObject shooter;
        private FakeProjectileService projectiles;

        [SetUp]
        public void SetUp()
        {
            shooter = new GameObject("EnemyWeaponTests_Shooter");
            projectiles = new FakeProjectileService();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(shooter);

        private EnemyWeapon Make(EnemyArchetypeSO a, int loadout, int spares, int seed = 3) =>
            new EnemyWeapon(a.loadouts[loadout], spares, a, projectiles, new System.Random(seed));

        // seconds 동안 매 프레임 wantsFire로 돌리고 발사 시각을 돌려준다.
        private List<float> Run(EnemyWeapon w, float seconds, float start = 0f)
        {
            var times = new List<float>();
            int frames = Mathf.RoundToInt(seconds / Dt);
            for (int f = 0; f < frames; f++)
            {
                float now = start + f * Dt;
                if (w.Tick(now, Dt, true, Vector3.zero, Vector3.forward * 10f, 0f, shooter.transform) > 0)
                    times.Add(now);
            }
            return times;
        }

        [Test]
        public void Damage_IsWeaponValue()
        {
            EnemyArchetypeSO scv = M3TestData.Scv();
            Assert.AreEqual(35f, Make(scv, 0, 0).Damage, "권총 §5.2");
            Assert.AreEqual(30f, Make(scv, 1, 0).Damage, "SMG §5.2");
            Assert.AreEqual(45f, Make(M3TestData.Rdr(), 0, 0).Damage, "소총 §5.2");
        }

        [Test]
        public void StartsWithFullMagazineAndSpares()
        {
            EnemyWeapon w = Make(M3TestData.Scv(), 1, 2);
            Assert.AreEqual(30, w.Item.LoadedMagazine.Rounds);
            Assert.AreEqual(2, w.Spares.Magazines.Count);
            Assert.IsTrue(w.HasAmmo);
            Assert.AreEqual(1f, w.AmmoRatio);
            Assert.AreEqual(FireMode.Auto, w.Mode);
        }

        [Test]
        public void Fire_RecordsShooterRootDamageAmmo()
        {
            EnemyArchetypeSO scv = M3TestData.Scv();
            EnemyWeapon w = Make(scv, 0, 0);
            int shots = w.Tick(0f, Dt, true, new Vector3(0f, 1.4f, 0f), new Vector3(0f, 1f, 10f), 0f, shooter.transform);
            Assert.AreEqual(1, shots);
            Assert.AreEqual(1, projectiles.Count);
            Assert.AreSame(shooter.transform, projectiles.Last.ShooterRoot);
            Assert.AreEqual(35f, projectiles.Last.Damage);
            Assert.AreSame(scv.loadouts[0].ammo, projectiles.Last.Ammo);
            Assert.AreEqual(new Vector3(0f, 1.4f, 0f), projectiles.Last.Origin);
            Assert.AreEqual(11, w.Item.LoadedMagazine.Rounds, "실제 탄창에서 1발 소모");
        }

        [Test]
        public void NoFireIntent_NoShots()
        {
            EnemyWeapon w = Make(M3TestData.Scv(), 1, 0);
            for (int f = 0; f < 60; f++)
                w.Tick(f * Dt, Dt, false, Vector3.zero, Vector3.forward, 0f, shooter.transform);
            Assert.AreEqual(0, projectiles.Count);
        }

        [Test]
        public void Pistol_SemiInterval()
        {
            EnemyWeapon w = Make(M3TestData.Scv(), 0, 0);
            Assert.AreEqual(FireMode.Semi, w.Mode);
            List<float> times = Run(w, 3f);
            Assert.GreaterOrEqual(times.Count, 5);
            for (int i = 1; i < times.Count; i++)
            {
                float gap = times[i] - times[i - 1];
                Assert.GreaterOrEqual(gap, 60f / 400f, "RPM 간격");
                Assert.GreaterOrEqual(gap, 0.35f - 1e-4f, "semiShotIntervalSec 최소");
                Assert.LessOrEqual(gap, 0.6f + Dt + 1e-4f, "semiShotIntervalSec 최대 (+1프레임)");
            }
        }

        [Test]
        public void Smg_BurstSizesAndPauses()
        {
            EnemyArchetypeSO scv = M3TestData.Scv();
            EnemyWeapon w = Make(scv, 1, 0);
            List<float> times = Run(w, 6f);
            Assert.AreEqual(30, times.Count + w.Item.LoadedMagazine.Rounds);

            // 점사 묶기: RPM 간격(0.075)보다 크게 벌어지면 새 점사
            var bursts = new List<int>();
            var pauses = new List<float>();
            int size = 1;
            for (int i = 1; i < times.Count; i++)
            {
                float gap = times[i] - times[i - 1];
                if (gap > 0.2f)
                {
                    bursts.Add(size);
                    pauses.Add(gap);
                    size = 1;
                }
                else
                {
                    Assert.GreaterOrEqual(gap, 60f / 800f - Dt - 1e-4f, "RPM 간격 (프레임 양자화 1프레임 허용)");
                    size++;
                }
            }
            Assert.GreaterOrEqual(bursts.Count, 3);
            foreach (int b in bursts)
                Assert.That(b, Is.InRange(scv.burstRounds.x, scv.burstRounds.y));
            foreach (float p in pauses)
                Assert.GreaterOrEqual(p, scv.burstPauseSec.x);
        }

        [Test]
        public void EmptyMagazine_AutoReload_UsesSpare()
        {
            EnemyWeapon w = Make(M3TestData.Scv(), 1, 2);
            List<float> times = Run(w, 20f);
            Assert.Greater(times.Count, 30, "재장전 후 계속 사격");

            // 첫 탄창이 비고 나서 재장전 시간(2.3초) 동안은 쏘지 않는다
            float lastOfFirstMag = times[29];
            float firstOfSecond = times[30];
            Assert.GreaterOrEqual(firstOfSecond - lastOfFirstMag, 2.3f - 1e-3f);

            int loadable = 0;
            foreach (Magazine m in w.Spares.Magazines)
                if (!m.IsEmpty)
                    loadable++;
            Assert.LessOrEqual(loadable, 1, "예비 탄창이 줄었다");
            Assert.AreEqual(3, w.Spares.Magazines.Count + (w.Item.LoadedMagazine != null ? 1 : 0),
                "일반 재장전: 빈 탄창은 회수(떨어뜨리지 않음)");
        }

        [Test]
        public void OutOfAmmo_StopsFiring()
        {
            EnemyWeapon w = Make(M3TestData.Scv(), 0, 0);
            List<float> times = Run(w, 10f);
            Assert.AreEqual(12, times.Count);
            Assert.IsFalse(w.HasAmmo);
            Assert.IsFalse(w.TryReload());
        }

        [Test]
        public void TryReload_FullFalse_PartialTrue_CancelKeepsMagazine()
        {
            EnemyWeapon w = Make(M3TestData.Rdr(), 0, 1);
            Assert.IsFalse(w.TryReload(), "가득 찬 탄창은 재장전 안 함");
            Run(w, 0.5f);
            Magazine before = w.Item.LoadedMagazine;
            Assert.Less(before.Rounds, 30);
            Assert.IsTrue(w.TryReload());
            Assert.IsTrue(w.Model.IsReloading);
            Assert.IsFalse(w.TryReload(), "재장전 중");
            w.CancelReload();
            Assert.IsFalse(w.Model.IsReloading);
            Assert.AreSame(before, w.Item.LoadedMagazine);
        }

        [Test]
        public void ErrorRadius_ScattersAroundAimPoint()
        {
            EnemyWeapon w = Make(M3TestData.Rdr(), 0, 0);
            var aim = new Vector3(0f, 1f, 20f);
            for (int f = 0; f < 60; f++)
                w.Tick(f * Dt, Dt, true, Vector3.zero, aim, 1f, shooter.transform);
            Assert.Greater(projectiles.Count, 1);
            bool anyOff = false;
            foreach (FakeProjectileService.Shot s in projectiles.Shots)
                if (Vector3.Angle(s.Direction, aim.normalized) > 0.01f)
                    anyOff = true;
            Assert.IsTrue(anyOff);
        }

        [Test]
        public void ShotsInBurst_CountsWithinBurst_ResetsAfter()
        {
            EnemyWeapon w = Make(M3TestData.Rdr(), 0, 0);
            int max = 0;
            for (int f = 0; f < 30; f++)
            {
                w.Tick(f * Dt, Dt, true, Vector3.zero, Vector3.forward * 10f, 0f, shooter.transform);
                max = Mathf.Max(max, w.ShotsInBurst);
            }
            Assert.Greater(max, 0);
            w.Tick(1f, Dt, false, Vector3.zero, Vector3.forward, 0f, shooter.transform);
            Assert.AreEqual(0, w.ShotsInBurst);
        }

        [Test]
        public void PickLoadout_ForcedOrWeighted()
        {
            EnemyArchetypeSO scv = M3TestData.Scv();
            Assert.AreEqual(1, EnemyWeapon.PickLoadout(scv.loadouts, 1, new System.Random(1)));
            Assert.AreEqual(-1, EnemyWeapon.PickLoadout(new List<EnemyLoadout>(), -1, new System.Random(1)));

            var random = new System.Random(123);
            int pistols = 0;
            const int n = 3000;
            for (int i = 0; i < n; i++)
                if (EnemyWeapon.PickLoadout(scv.loadouts, -1, random) == 0)
                    pistols++;
            Assert.That(pistols / (float)n, Is.InRange(0.62f, 0.71f), "권총 가중 2 : SMG 1");
        }
    }
}
