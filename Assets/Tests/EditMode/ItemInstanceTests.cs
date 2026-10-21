using Ashfall.Core;
using Ashfall.Weapons;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // 런타임 아이템 (설계 D4)과 IMagazineSource 연동 재장전 (§5.1)
    public class ItemInstanceTests
    {
        [Test]
        public void SizeFor_RotatedSwapsWidthAndHeight()
        {
            var item = new ItemInstance(TestData.EnginePart());
            Assert.AreEqual(new Vector2Int(3, 2), item.SizeFor(false));
            Assert.AreEqual(new Vector2Int(2, 3), item.SizeFor(true));
        }

        [Test]
        public void ItemFactory_CreatesMatchingInstanceType()
        {
            var mag = ItemFactory.Create(TestData.Magazine30());
            Assert.IsInstanceOf<Magazine>(mag);
            Assert.AreEqual(30, ((Magazine)mag).Rounds, "탄창은 꽉 찬 상태로 생성");

            var weapon = ItemFactory.Create(TestData.Rifle());
            Assert.IsInstanceOf<WeaponItem>(weapon);
            Assert.IsNull(((WeaponItem)weapon).LoadedMagazine);

            var med = ItemFactory.Create(TestData.Bandage());
            Assert.IsInstanceOf<MedicalItem>(med);
            Assert.AreEqual(1, ((MedicalItem)med).UsesLeft);

            Assert.AreEqual(typeof(ItemInstance), ItemFactory.Create(TestData.Vest()).GetType());
        }

        [Test]
        public void WeaponItem_WeightIncludesLoadedMagazine()
        {
            var rifleDef = TestData.Rifle();
            rifleDef.weightKg = 3.5f;
            var magDef = TestData.Magazine30();
            magDef.weightKg = 0.5f;

            var weapon = new WeaponItem(rifleDef);
            Assert.AreEqual(3.5f, weapon.WeightKg, 1e-5f);
            weapon.LoadedMagazine = Magazine.Full(magDef);
            Assert.AreEqual(4.0f, weapon.WeightKg, 1e-5f);
        }

        [Test]
        public void MedicalItem_ConsumeUse_StopsAtZero()
        {
            var med = new MedicalItem(TestData.Bandage());
            Assert.IsTrue(med.ConsumeUse());
            Assert.AreEqual(0, med.UsesLeft);
            Assert.IsFalse(med.ConsumeUse());
        }

        [Test]
        public void LocalMagazineSource_TakesFullestMatchingAmmo()
        {
            var mag30 = TestData.Magazine30();
            var nine = Magazine.Full(TestData.PistolMagazine());
            var ten = new Magazine(mag30, 10);
            var twenty = new Magazine(mag30, 20);
            var empty = new Magazine(mag30, 0);
            var source = new LocalMagazineSource(new[] { nine, ten, empty, twenty });

            Assert.IsTrue(source.HasLoadable(AmmoType.Rifle556));
            Assert.AreSame(twenty, source.TakeFullest(AmmoType.Rifle556));
            Assert.AreSame(ten, source.TakeFullest(AmmoType.Rifle556));
            Assert.IsNull(source.TakeFullest(AmmoType.Rifle556), "빈 탄창은 꺼내지 않는다");
            Assert.IsFalse(source.HasLoadable(AmmoType.Rifle556));
            Assert.IsFalse(source.HasLoadable(AmmoType.Rifle762));
        }

        // 회수할 공간이 없는 소스 (인벤토리가 꽉 찬 경우)
        private sealed class NoSpaceSource : IMagazineSource
        {
            private Magazine spare;
            public NoSpaceSource(Magazine spare) => this.spare = spare;
            public bool HasLoadable(AmmoType ammo) => spare != null;
            public Magazine TakeFullest(AmmoType ammo) { var m = spare; spare = null; return m; }
            public bool TryStore(Magazine magazine) => false;
        }

        [Test]
        public void NormalReload_WithoutStorageSpace_DropsOldMagazine()
        {
            var mag30 = TestData.Magazine30();
            var first = Magazine.Full(mag30);
            var spare = Magazine.Full(mag30);
            var weapon = new WeaponModel(new WeaponItem(TestData.Rifle(), first), new NoSpaceSource(spare));
            Magazine dropped = null;
            weapon.MagazineDropped += m => dropped = m;

            Assert.IsTrue(weapon.PressReload());
            weapon.Tick(2.6f);

            Assert.AreSame(spare, weapon.Current);
            Assert.AreSame(spare, weapon.Item.LoadedMagazine);
            Assert.AreSame(first, dropped, "회수 공간이 없으면 바닥으로");
        }

        [Test]
        public void Reload_RefusedForWrongAmmoMagazines()
        {
            var source = new LocalMagazineSource(new[] { Magazine.Full(TestData.PistolMagazine()) });
            var weapon = new WeaponModel(new WeaponItem(TestData.Rifle(), Magazine.Full(TestData.Magazine30())), source);
            Assert.IsFalse(weapon.PressReload());
        }

        [Test]
        public void CancelReload_KeepsCurrentMagazine()
        {
            var mag30 = TestData.Magazine30();
            var first = Magazine.Full(mag30);
            var source = new LocalMagazineSource(new[] { Magazine.Full(mag30) });
            var weapon = new WeaponModel(new WeaponItem(TestData.Rifle(), first), source);

            weapon.PressReload();
            weapon.Tick(1f);
            weapon.CancelReload();
            weapon.Tick(5f);

            Assert.IsFalse(weapon.IsReloading);
            Assert.AreSame(first, weapon.Current);
            Assert.AreEqual(1, source.Magazines.Count);
        }
    }
}
