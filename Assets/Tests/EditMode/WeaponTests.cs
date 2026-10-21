using System.Linq;
using Ashfall.Core;
using Ashfall.Weapons;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // SYS-WPN §5.1·§5.2 WPN-RIF-01 사격·재장전·탄도
    public class WeaponTests
    {
        private WeaponDefinitionSO rifle;
        private MagazineDefinitionSO mag30;

        [SetUp]
        public void SetUp()
        {
            rifle = TestData.Rifle();
            mag30 = TestData.Magazine30();
        }

        private LocalMagazineSource source;

        private WeaponModel NewRifle(int spareCount = 2)
        {
            source = new LocalMagazineSource(Enumerable.Range(0, spareCount).Select(_ => Magazine.Full(mag30)));
            return new WeaponModel(new WeaponItem(rifle, Magazine.Full(mag30)), source);
        }

        [Test]
        public void FireControl_650Rpm_FiresAtInterval()
        {
            var fc = new FireControl(650, FireMode.Auto);
            float interval = 60f / 650f;
            int shots = 0;
            for (float t = 0f; t < 1f; t += 0.001f)
                if (fc.Tick(t, true)) shots++;
            // 1초 동안 0, i, 2i … → ⌈1/i⌉ = 11발
            Assert.AreEqual(Mathf.CeilToInt(1f / interval), shots);
        }

        [Test]
        public void FireControl_Semi_NeedsTriggerRelease()
        {
            var fc = new FireControl(650, FireMode.Semi);
            Assert.IsTrue(fc.Tick(0f, true));
            Assert.IsFalse(fc.Tick(1f, true), "누르고만 있으면 더 안 나간다");
            fc.Tick(1.1f, false);
            Assert.IsTrue(fc.Tick(1.2f, true));
        }

        [Test]
        public void Firing_ConsumesRoundsFromMagazine_StopsWhenEmpty()
        {
            var weapon = NewRifle();
            int shots = 0;
            for (float t = 0f; t < 10f; t += 0.01f)
                if (weapon.TryFire(t, true)) shots++;
            Assert.AreEqual(30, shots);
            Assert.AreEqual(0, weapon.Current.Rounds);
        }

        [Test]
        public void NormalReload_KeepsOldMagazineWithRemainingRounds()
        {
            var weapon = NewRifle(spareCount: 1);
            var first = weapon.Current;
            for (int i = 0; i < 5; i++) weapon.TryFire(i, true); // 서로 다른 시각 → 5발
            Assert.AreEqual(25, first.Rounds);

            Assert.IsTrue(weapon.PressReload());
            Assert.IsFalse(weapon.TryFire(100f, true), "재장전 중 사격 불가");
            weapon.Tick(2.4f);
            Assert.IsTrue(weapon.IsReloading);
            weapon.Tick(0.2f);

            Assert.IsFalse(weapon.IsReloading);
            Assert.AreEqual(30, weapon.Current.Rounds);
            Assert.Contains(first, source.Magazines.ToList(), "일반 재장전은 탄창을 회수한다");
            Assert.AreEqual(25, first.Rounds, "탄창의 남은 탄 수가 그대로 저장된다");
        }

        [Test]
        public void FastReload_DropsOldMagazine()
        {
            var weapon = NewRifle(spareCount: 1);
            var first = weapon.Current;
            Magazine dropped = null;
            weapon.MagazineDropped += m => dropped = m;

            weapon.PressReload();
            weapon.Tick(0.5f);
            Assert.IsTrue(weapon.PressReload(), "재장전 중 R 한 번 더 → 빠른 재장전");
            weapon.Tick(0.9f);
            Assert.IsTrue(weapon.IsReloading, "빠른 재장전 1.5초 - 경과 0.5초 = 1.0초 남음");
            weapon.Tick(0.2f);

            Assert.IsFalse(weapon.IsReloading);
            Assert.AreSame(first, dropped);
            Assert.IsEmpty(source.Magazines);
        }

        [Test]
        public void Reload_RefusedWithoutSpareRounds()
        {
            var weapon = NewRifle(spareCount: 0);
            Assert.IsFalse(weapon.PressReload());
        }

        [Test]
        public void Ballistics_FallsUnderGravity()
        {
            Vector3 pos = Vector3.zero;
            Vector3 vel = new Vector3(0f, 0f, 900f);
            Vector3 g = new Vector3(0f, -9.81f, 0f);
            const float dt = 0.02f;
            for (int i = 0; i < 50; i++) // 1초
                Ballistics.Step(ref pos, ref vel, g, dt);

            Assert.AreEqual(900f, pos.z, 1e-2f);
            // 연속해 ½gt² = 4.905m. 반암시적 오일러는 스텝 하나만큼 더 떨어진다 (½g·t·dt ≈ 0.1m).
            Assert.AreEqual(-4.905f, pos.y, 0.15f);
            Assert.Less(pos.y, -4.8f);
        }

        [Test]
        public void Spread_ZeroWhenAiming_WithinConeWhenHip()
        {
            var rng = new System.Random(1);
            Assert.AreEqual(Vector3.forward, Ballistics.ApplySpread(Vector3.forward, 0f, rng));
            for (int i = 0; i < 200; i++)
            {
                var dir = Ballistics.ApplySpread(Vector3.forward, 2f, rng);
                Assert.LessOrEqual(Vector3.Angle(Vector3.forward, dir), 2.0001f);
            }
        }

        [Test]
        public void Recoil_UpwardAndWithinHorizontalRange()
        {
            var rng = new System.Random(2);
            for (int i = 0; i < 100; i++)
            {
                Vector2 kick = Ballistics.RecoilKick(0.6f, 0.25f, rng);
                Assert.AreEqual(0.6f, kick.x);
                Assert.LessOrEqual(Mathf.Abs(kick.y), 0.25f);
            }
        }
    }
}
