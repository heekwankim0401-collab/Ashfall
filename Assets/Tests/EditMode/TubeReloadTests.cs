using System;
using Ashfall.Core;
using Ashfall.Weapons;
using NUnit.Framework;

namespace Ashfall.Tests.EditMode
{
    // WPN-SHT-01 내장 튜브 한 발씩 장전 (M7 7A, Q5~Q7) + RULE-04 분류
    public class TubeReloadTests
    {
        private MagazineDefinitionSO tube;
        private MagazineDefinitionSO box;
        private WeaponDefinitionSO shotgun;

        [SetUp]
        public void SetUp()
        {
            tube = M7TestData.ShotgunTube();
            box = M7TestData.Box12Ga8();
            shotgun = M7TestData.Shotgun(tube);
        }

        private WeaponModel NewShotgun(int tubeRounds, params Magazine[] boxes)
        {
            var item = new WeaponItem(shotgun, new Magazine(tube, tubeRounds));
            return new WeaponModel(item, new LocalMagazineSource(boxes));
        }

        // ── 내장 튜브 생성 ────────────────────────────────────

        [Test]
        public void NewWeapon_HasEmptyInternalTube()
        {
            var item = new WeaponItem(shotgun);
            Assert.IsTrue(item.HasInternalMagazine);
            Assert.IsNotNull(item.LoadedMagazine);
            Assert.AreSame(tube, item.LoadedMagazine.Definition);
            Assert.AreEqual(0, item.LoadedMagazine.Rounds);
            Assert.AreEqual(6, item.LoadedMagazine.Capacity);

            var created = (WeaponItem)ItemFactory.Create(shotgun);
            Assert.AreSame(tube, created.LoadedMagazine.Definition, "ItemFactory도 빈 튜브");
            Assert.AreEqual(0, created.LoadedMagazine.Rounds);
        }

        [Test]
        public void DetachableWeapon_StillHasNoMagazine()
        {
            var item = new WeaponItem(TestData.Rifle());
            Assert.IsFalse(item.HasInternalMagazine);
            Assert.IsNull(item.LoadedMagazine);
        }

        [Test]
        public void Model_IsTubeReload()
        {
            Assert.IsTrue(NewShotgun(0).IsTubeReload);
            Assert.IsFalse(new WeaponModel(new WeaponItem(TestData.Rifle()), new LocalMagazineSource(null)).IsTubeReload);
        }

        // ── 한 발씩 ───────────────────────────────────────────

        [Test]
        public void Reload_OneRoundPerInterval_UntilFull()
        {
            var b = Magazine.Full(box);
            WeaponModel weapon = NewShotgun(0, b);

            Assert.IsTrue(weapon.CanReload);
            Assert.IsTrue(weapon.PressReload());
            Assert.IsTrue(weapon.IsReloading);
            Assert.IsFalse(weapon.IsFastReload);

            weapon.Tick(0.49f);
            Assert.AreEqual(0, weapon.Current.Rounds);
            weapon.Tick(0.02f);
            Assert.AreEqual(1, weapon.Current.Rounds);
            Assert.AreEqual(1, weapon.RoundsLoadedThisReload);
            Assert.AreEqual(7, b.Rounds);

            for (int i = 0; i < 5; i++)
                weapon.Tick(0.5f);
            Assert.AreEqual(6, weapon.Current.Rounds);
            Assert.AreEqual(6, weapon.RoundsLoadedThisReload);
            Assert.IsFalse(weapon.IsReloading, "가득 차면 끝");
            Assert.AreEqual(2, b.Rounds);
            Assert.IsFalse(weapon.CanReload, "가득 = 재장전 불가");
        }

        [Test]
        public void Reload_LargeDt_LoadsSeveralRounds()
        {
            WeaponModel weapon = NewShotgun(1, Magazine.Full(box));
            weapon.PressReload();
            weapon.Tick(10f);
            Assert.AreEqual(6, weapon.Current.Rounds);
            Assert.IsFalse(weapon.IsReloading);
        }

        [Test]
        public void Reload_StopsWhenLooseRoundsRunOut()
        {
            var b = new Magazine(box, 3);
            WeaponModel weapon = NewShotgun(0, b);
            weapon.PressReload();
            weapon.Tick(0.5f);
            weapon.Tick(0.5f);
            Assert.IsTrue(weapon.IsReloading);
            weapon.Tick(0.5f);
            Assert.AreEqual(3, weapon.Current.Rounds);
            Assert.IsFalse(weapon.IsReloading, "낱탄이 없으면 끝");
            Assert.AreEqual(0, b.Rounds);
        }

        [Test]
        public void Reload_TakesFromLeastFilledBoxFirst()
        {
            var small = new Magazine(box, 3);
            var big = new Magazine(box, 5);
            WeaponModel weapon = NewShotgun(0, big, small);
            weapon.PressReload();
            weapon.Tick(0.5f);
            Assert.AreEqual(2, small.Rounds, "탄이 적은 상자부터");
            Assert.AreEqual(5, big.Rounds);
            weapon.Tick(10f);
            Assert.AreEqual(6, weapon.Current.Rounds);
            Assert.AreEqual(0, small.Rounds);
            Assert.AreEqual(2, big.Rounds);
        }

        [Test]
        public void NoLooseRounds_CannotReload()
        {
            var empty = new Magazine(box, 0);
            var source = new LocalMagazineSource(new[] { empty });
            var weapon = new WeaponModel(new WeaponItem(shotgun), source);
            Assert.IsFalse(weapon.CanReload);
            Assert.IsFalse(weapon.PressReload());
            Assert.AreEqual(1, source.Magazines.Count, "Q6: 빈 상자는 남는다");
        }

        [Test]
        public void DetachableMagazineOfSameAmmo_IsNotLooseAmmo()
        {
            MagazineDefinitionSO notBox = M7TestData.Box12Ga8();
            notBox.isAmmoBox = false;
            WeaponModel weapon = NewShotgun(0, Magazine.Full(notBox));
            Assert.IsFalse(weapon.CanReload, "튜브는 탄 상자에서만 꺼낸다");
        }

        [Test]
        public void NoFastReload_SecondPressIgnored()
        {
            WeaponModel weapon = NewShotgun(0, Magazine.Full(box));
            Assert.IsTrue(weapon.PressReload());
            weapon.Tick(0.3f);
            Assert.IsFalse(weapon.PressReload(), "튜브는 빠른 재장전 없음");
            Assert.IsFalse(weapon.IsFastReload);
            Assert.IsTrue(weapon.IsReloading);
        }

        [Test]
        public void ExplicitLooseSource_IsUsed()
        {
            var item = new WeaponItem(shotgun);
            var loose = new LocalMagazineSource(new[] { Magazine.Full(box) });
            var weapon = new WeaponModel(item, new NoLooseSource(), loose);
            Assert.IsTrue(weapon.CanReload);
            weapon.PressReload();
            weapon.Tick(0.5f);
            Assert.AreEqual(1, item.LoadedMagazine.Rounds);
        }

        [Test]
        public void NoLooseSourceAtAll_CannotReload()
        {
            var weapon = new WeaponModel(new WeaponItem(shotgun), new NoLooseSource());
            Assert.IsFalse(weapon.CanReload);
            Assert.IsFalse(weapon.PressReload());
        }

        // ── 장전 중 사격 (Q7) ─────────────────────────────────

        [Test]
        public void FireDuringReload_WithRounds_InterruptsAndFires()
        {
            var b = Magazine.Full(box);
            WeaponModel weapon = NewShotgun(2, b);
            weapon.PressReload();
            weapon.Tick(0.3f);   // 첫 발 진행 중

            Assert.IsTrue(weapon.TryFire(10f, true));
            Assert.IsFalse(weapon.IsReloading, "사격으로 장전 중단");
            Assert.AreEqual(1, weapon.Current.Rounds);
            Assert.AreEqual(8, b.Rounds, "진행 중인 한 발은 꺼낸 적 없음");

            weapon.Tick(1f);
            Assert.AreEqual(1, weapon.Current.Rounds, "중단 후 더 넣지 않는다");
        }

        [Test]
        public void FireDuringReload_EmptyTube_Ignored()
        {
            WeaponModel weapon = NewShotgun(0, Magazine.Full(box));
            weapon.PressReload();
            weapon.Tick(0.3f);
            Assert.IsFalse(weapon.TryFire(10f, true));
            Assert.IsTrue(weapon.IsReloading, "0발이면 장전 계속");
            weapon.Tick(0.25f);
            Assert.AreEqual(1, weapon.Current.Rounds);
        }

        // M7 리뷰 7A 중간 1: 좌클릭을 누른 채 R → 반자동 새 당김이 아니라 발사 안 됨 → 장전이 끊기지 않는다.
        [Test]
        public void FireDuringReload_TriggerHeldSinceBeforeR_KeepsReloading()
        {
            var b = Magazine.Full(box);
            WeaponModel weapon = NewShotgun(3, b);
            Assert.IsTrue(weapon.TryFire(0f, true), "첫 당김 발사");
            weapon.TryFire(5f, true);   // 누른 채 (반자동 — 발사 없음)

            Assert.IsTrue(weapon.PressReload());
            weapon.Tick(0.3f);
            Assert.IsFalse(weapon.TryFire(10f, true), "누른 채인 방아쇠는 새 당김이 아니다");
            Assert.IsTrue(weapon.IsReloading, "발사가 성립하지 않으면 장전 계속");
            weapon.Tick(0.25f);
            Assert.AreEqual(3, weapon.Current.Rounds, "2 + 장전 1");
            Assert.IsFalse(weapon.TryFire(10.5f, true), "첫 발이 들어간 뒤에도 누른 채면 쏘지 않는다");
            Assert.IsTrue(weapon.IsReloading);

            weapon.TryFire(10.6f, false);
            Assert.IsTrue(weapon.TryFire(10.7f, true), "떼었다 다시 당기면 중단 후 사격");
            Assert.IsFalse(weapon.IsReloading);
            Assert.AreEqual(2, weapon.Current.Rounds);
        }

        // M7 리뷰 7A 중간 1: 70RPM 간격 안의 클릭 → 발사 안 됨 → 장전이 끊기지 않는다.
        [Test]
        public void FireDuringReload_ClickWithinFireInterval_KeepsReloading()
        {
            var b = Magazine.Full(box);
            WeaponModel weapon = NewShotgun(3, b);
            Assert.IsTrue(weapon.TryFire(0f, true));
            weapon.TryFire(0.05f, false);

            Assert.IsTrue(weapon.PressReload());
            weapon.Tick(0.3f);
            Assert.IsFalse(weapon.TryFire(0.4f, true), "70rpm ≈ 0.857초 간격 안");
            Assert.IsTrue(weapon.IsReloading, "간격 안 클릭은 장전을 끊지 않는다");
            weapon.TryFire(0.45f, false);
            weapon.Tick(0.25f);
            Assert.AreEqual(3, weapon.Current.Rounds);
            Assert.IsTrue(weapon.IsReloading);

            Assert.IsTrue(weapon.TryFire(0.9f, true), "간격 뒤 새 당김 = 중단 후 사격");
            Assert.IsFalse(weapon.IsReloading);
            Assert.AreEqual(2, weapon.Current.Rounds);
        }

        // M7 리뷰 7A 낮음 5: allowShot = false(교체 중)에도 방아쇠 상태는 기록 → 누른 채 끝나면 반자동은 쏘지 않는다.
        [Test]
        public void TryFire_NotAllowed_TracksTriggerWithoutFiring()
        {
            WeaponModel weapon = NewShotgun(6);
            Assert.IsFalse(weapon.TryFire(0f, true, false), "막힘");
            Assert.AreEqual(6, weapon.Current.Rounds);
            Assert.IsFalse(weapon.TryFire(0.5f, true, true), "막힌 동안 누른 방아쇠는 새 당김이 아니다");
            weapon.TryFire(0.6f, false, true);
            Assert.IsTrue(weapon.TryFire(0.7f, true, true), "간격을 쓰지 않았으므로 바로 쏜다");
            Assert.AreEqual(5, weapon.Current.Rounds);
        }

        [Test]
        public void TubeFire_Semi_ConsumesOnePerPull()
        {
            WeaponModel weapon = NewShotgun(6);
            Assert.IsTrue(weapon.TryFire(0f, true));
            Assert.IsFalse(weapon.TryFire(5f, true), "반자동");
            weapon.TryFire(5.1f, false);
            Assert.IsTrue(weapon.TryFire(5.2f, true));
            Assert.AreEqual(4, weapon.Current.Rounds);
        }

        [Test]
        public void TubeRpm70_Interval()
        {
            WeaponModel weapon = NewShotgun(6);
            Assert.IsTrue(weapon.TryFire(0f, true));
            weapon.TryFire(0.1f, false);
            Assert.IsFalse(weapon.TryFire(0.5f, true), "70rpm ≈ 0.857초 간격");
            weapon.TryFire(0.6f, false);
            Assert.IsTrue(weapon.TryFire(0.9f, true));
        }

        [Test]
        public void Jammed_TubeCannotReload()
        {
            var item = new WeaponItem(shotgun, new Magazine(tube, 2)) { Durability = 0f };
            var weapon = new WeaponModel(item, new LocalMagazineSource(new[] { Magazine.Full(box) }),
                wear: new WeaponWear(50f, 1f, new Random(1)));
            Assert.IsFalse(weapon.TryFire(0f, true));
            Assert.IsTrue(weapon.IsJammed);
            Assert.IsFalse(weapon.CanReload);
            Assert.IsFalse(weapon.PressReload());
        }

        // ── RULE-04 ───────────────────────────────────────────

        [TestCase(6, MagazineFillLevel.Full)]
        [TestCase(3, MagazineFillLevel.Half)]
        [TestCase(1, MagazineFillLevel.AlmostEmpty)]
        [TestCase(0, MagazineFillLevel.AlmostEmpty)]
        public void MagazineCheck_ClassifiesTubeLikeMagazine(int rounds, MagazineFillLevel expected)
        {
            WeaponHandlingConfigSO config = M7TestData.Handling();
            var item = new WeaponItem(shotgun, new Magazine(tube, rounds));
            Assert.AreEqual(expected, MagazineCheckModel.Classify(item.LoadedMagazine, config));
        }

        // 낱탄이 없는 IMagazineSource (ILooseAmmoSource 미구현)
        private sealed class NoLooseSource : IMagazineSource
        {
            public bool HasLoadable(AmmoType ammo) => false;
            public Magazine TakeFullest(AmmoType ammo) => null;
            public bool TryStore(Magazine magazine) => false;
        }
    }
}
