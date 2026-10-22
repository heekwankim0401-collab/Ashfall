using Ashfall.Core;
using Ashfall.Inventory;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // M7 7A-2b: PlayerInventory의 ILooseAmmoSource(샷건 튜브 낱탄) — 설계 §1.3, §4.3, Q5·Q6.
    // 조끼 → 주머니 → (설정 시) 배낭, 탄이 가장 적은 상자부터, 잠김·사망·레이드 종료 거부, 상자는 일반 재장전에 안 쓰임.
    public class PlayerInventoryLooseAmmoTests
    {
        private InventoryConfigSO config;
        private PlayerInventory inventory;
        private MagazineDefinitionSO box;

        [SetUp]
        public void SetUp()
        {
            config = TestData.Inventory();
            inventory = new PlayerInventory(config);
            box = M7TestData.Box12Ga8();
        }

        private void EquipVest() => Assert.IsTrue(inventory.TryEquipNew(new ItemInstance(TestData.Vest()), EquipSlot.TacticalVest));
        private void EquipBackpack() => Assert.IsTrue(inventory.TryEquipNew(new ItemInstance(TestData.Backpack()), EquipSlot.Backpack));

        private Magazine Place(GridKind kind, int index, int rounds, Vector2Int cell = default)
        {
            var m = new Magazine(box, rounds);
            Assert.IsTrue(inventory.TryPlaceNew(m, new GridRef(kind, index), cell, false), $"{kind}{index} {cell}");
            return m;
        }

        private Magazine Pocket(int index, int rounds) => Place(GridKind.Pocket, index, rounds);

        // ── 기본 ──────────────────────────────────────────

        [Test]
        public void ImplementsLooseAmmoSource_SameInstanceAsMagazineSource()
        {
            IMagazineSource source = inventory;
            Assert.AreSame(inventory, source as ILooseAmmoSource, "WeaponModel의 `source as ILooseAmmoSource` 경로");
        }

        [Test]
        public void NoBox_NoLooseRound()
        {
            Assert.IsFalse(inventory.HasLooseRound(AmmoType.Gauge12));
            Assert.IsFalse(inventory.TakeLooseRound(AmmoType.Gauge12));
        }

        [Test]
        public void Take_DecrementsBox_RaisesChanged()
        {
            Magazine b = Pocket(0, 3);
            int changed = 0;
            inventory.Changed += () => changed++;

            Assert.IsTrue(inventory.HasLooseRound(AmmoType.Gauge12));
            Assert.IsTrue(inventory.TakeLooseRound(AmmoType.Gauge12));
            Assert.AreEqual(2, b.Rounds);
            Assert.AreEqual(1, changed, "InventoryChanged 경로");
        }

        [Test]
        public void EmptyBox_StaysAsZeroRoundItem()
        {
            Magazine b = Pocket(0, 1);
            Assert.IsTrue(inventory.TakeLooseRound(AmmoType.Gauge12));
            Assert.AreEqual(0, b.Rounds);
            Assert.IsTrue(inventory.IsInStorage(b), "Q6: 빈 상자는 남는다");
            Assert.IsTrue(inventory.TryGetLocation(b, out ItemLocation loc));
            Assert.AreEqual(GridKind.Pocket, loc.Grid.Kind);

            Assert.IsFalse(inventory.HasLooseRound(AmmoType.Gauge12));
            Assert.IsFalse(inventory.TakeLooseRound(AmmoType.Gauge12));
        }

        [Test]
        public void OtherAmmo_NotServed()
        {
            Pocket(0, 8);
            Assert.IsFalse(inventory.HasLooseRound(AmmoType.Rifle556));
            Assert.IsFalse(inventory.TakeLooseRound(AmmoType.Rifle556));
        }

        [Test]
        public void RegularMagazine_IsNotLooseAmmo()
        {
            var mag = ScriptableObject.CreateInstance<MagazineDefinitionSO>();
            mag.id = "MAG-12GA-TEST"; mag.size = Vector2Int.one; mag.ammo = AmmoType.Gauge12; mag.capacity = 5;
            var m = Magazine.Full(mag);
            Assert.IsTrue(inventory.TryAutoStore(m));

            Assert.IsFalse(inventory.HasLooseRound(AmmoType.Gauge12), "isAmmoBox가 아닌 탄창은 낱탄이 아니다");
            Assert.IsFalse(inventory.TakeLooseRound(AmmoType.Gauge12));
            Assert.AreEqual(5, m.Rounds);
        }

        // ── 순서 ──────────────────────────────────────────

        [Test]
        public void LeastRoundsFirst_AcrossPockets()
        {
            Magazine a = Pocket(0, 5);
            Magazine b = Pocket(1, 2);
            Magazine c = Pocket(2, 7);

            Assert.IsTrue(inventory.TakeLooseRound(AmmoType.Gauge12));
            Assert.AreEqual(1, b.Rounds, "가장 적은 상자부터");
            Assert.AreEqual(5, a.Rounds);
            Assert.AreEqual(7, c.Rounds);
        }

        [Test]
        public void EmptiesSmallestBox_ThenNextSmallest()
        {
            Magazine a = Pocket(0, 3);
            Magazine b = Pocket(1, 1);
            Assert.IsTrue(inventory.TakeLooseRound(AmmoType.Gauge12));
            Assert.AreEqual(0, b.Rounds);
            Assert.IsTrue(inventory.TakeLooseRound(AmmoType.Gauge12), "빈 상자는 건너뛴다");
            Assert.AreEqual(2, a.Rounds);
            Assert.AreEqual(0, b.Rounds);
        }

        [Test]
        public void Tie_VestBeforePockets()
        {
            EquipVest();
            Magazine pocket = Pocket(0, 4);
            Magazine vest = Place(GridKind.TacticalVest, 0, 4, new Vector2Int(3, 1));

            Assert.IsTrue(inventory.TakeLooseRound(AmmoType.Gauge12));
            Assert.AreEqual(3, vest.Rounds, "같으면 조끼 쪽");
            Assert.AreEqual(4, pocket.Rounds);
        }

        [Test]
        public void Tie_EarlierPocketFirst()
        {
            Magazine first = Pocket(0, 4);
            Magazine second = Pocket(3, 4);
            Assert.IsTrue(inventory.TakeLooseRound(AmmoType.Gauge12));
            Assert.AreEqual(3, first.Rounds);
            Assert.AreEqual(4, second.Rounds);
        }

        [Test]
        public void FewerInPocket_BeatsVest()
        {
            EquipVest();
            Magazine vest = Place(GridKind.TacticalVest, 0, 6);
            Magazine pocket = Pocket(0, 2);

            Assert.IsTrue(inventory.TakeLooseRound(AmmoType.Gauge12));
            Assert.AreEqual(1, pocket.Rounds, "범위 전체에서 가장 적은 상자");
            Assert.AreEqual(6, vest.Rounds);
        }

        [Test]
        public void Backpack_ExcludedByDefault()
        {
            EquipBackpack();
            Magazine bag = Place(GridKind.Backpack, 0, 8);

            Assert.IsFalse(config.reloadFromBackpack);
            Assert.IsFalse(inventory.HasLooseRound(AmmoType.Gauge12));
            Assert.IsFalse(inventory.TakeLooseRound(AmmoType.Gauge12));
            Assert.AreEqual(8, bag.Rounds);
        }

        [Test]
        public void Backpack_IncludedWhenConfigured_AfterVestAndPockets()
        {
            config.reloadFromBackpack = true;
            EquipBackpack();
            EquipVest();
            Magazine bag = Place(GridKind.Backpack, 0, 3);
            Magazine vest = Place(GridKind.TacticalVest, 0, 3);

            Assert.IsTrue(inventory.TakeLooseRound(AmmoType.Gauge12));
            Assert.AreEqual(2, vest.Rounds, "같으면 조끼가 배낭보다 먼저");
            Assert.AreEqual(3, bag.Rounds);

            Assert.IsTrue(inventory.TakeLooseRound(AmmoType.Gauge12));
            Assert.AreEqual(1, vest.Rounds, "조끼가 더 적다");

            vest.TakeRound();   // 조끼 상자 비움 (0발 남음)
            Assert.IsTrue(inventory.TakeLooseRound(AmmoType.Gauge12));
            Assert.AreEqual(2, bag.Rounds, "남은 것은 배낭");
        }

        [Test]
        public void OpenLoot_NotUsed()
        {
            var lootBox = new Magazine(box, 8);
            var loot = new LootContainerModel("상자", new Vector2Int(2, 2), new ItemInstance[] { lootBox }, 0.5f);
            loot.Tick(1f);
            inventory.OpenLoot(loot);

            Assert.IsFalse(inventory.HasLooseRound(AmmoType.Gauge12), "열린 루트는 소지품이 아니다");
            Assert.IsFalse(inventory.TakeLooseRound(AmmoType.Gauge12));
            Assert.AreEqual(8, lootBox.Rounds);
        }

        // ── 거부 ──────────────────────────────────────────

        [Test]
        public void LockedBox_Skipped()
        {
            Magazine small = Pocket(0, 1);
            Magazine big = Pocket(1, 6);
            inventory.SetItemLocked(small, true);

            Assert.IsTrue(inventory.TakeLooseRound(AmmoType.Gauge12));
            Assert.AreEqual(1, small.Rounds, "잠긴 상자는 건너뛴다");
            Assert.AreEqual(5, big.Rounds);

            inventory.SetItemLocked(big, true);
            Assert.IsFalse(inventory.HasLooseRound(AmmoType.Gauge12));
            Assert.IsFalse(inventory.TakeLooseRound(AmmoType.Gauge12));
        }

        [Test]
        public void DeadLocked_Refuses()
        {
            Magazine b = Pocket(0, 5);
            int changed = 0;
            inventory.Changed += () => changed++;
            inventory.IsDeadLocked = true;

            Assert.IsFalse(inventory.HasLooseRound(AmmoType.Gauge12));
            Assert.IsFalse(inventory.TakeLooseRound(AmmoType.Gauge12));
            Assert.AreEqual(5, b.Rounds);
            Assert.AreEqual(0, changed);
        }

        [Test]
        public void RaidEndLocked_Refuses()
        {
            Magazine b = Pocket(0, 5);
            inventory.IsRaidEndLocked = true;

            Assert.IsFalse(inventory.HasLooseRound(AmmoType.Gauge12));
            Assert.IsFalse(inventory.TakeLooseRound(AmmoType.Gauge12));
            Assert.AreEqual(5, b.Rounds);
        }

        // ── 일반 재장전·장착에서 제외 ───────────────────────────

        [Test]
        public void Box_NotUsedForMagazineReload()
        {
            Magazine b = Pocket(0, 8);
            Assert.IsFalse(inventory.HasLoadable(AmmoType.Gauge12), "탄 상자는 무기에 넣을 수 없다");
            Assert.IsNull(inventory.TakeFullest(AmmoType.Gauge12));
            Assert.IsTrue(inventory.IsInStorage(b));
        }

        [Test]
        public void SameAmmoBox_SkippedForFullest_RegularMagazineTaken()
        {
            // 같은 탄의 상자가 탄창보다 많이 들어 있어도 탄창을 고른다
            var rifleBox = ScriptableObject.CreateInstance<MagazineDefinitionSO>();
            rifleBox.id = "BOX-556-TEST"; rifleBox.size = Vector2Int.one; rifleBox.ammo = AmmoType.Rifle556;
            rifleBox.capacity = 60; rifleBox.isAmmoBox = true;
            EquipVest();
            var boxItem = Magazine.Full(rifleBox);
            var mag = new Magazine(TestData.Magazine30(), 10);
            Assert.IsTrue(inventory.TryAutoStore(boxItem));
            Assert.IsTrue(inventory.TryAutoStore(mag));

            Assert.IsTrue(inventory.HasLoadable(AmmoType.Rifle556));
            Assert.AreSame(mag, inventory.TakeFullest(AmmoType.Rifle556));
            Assert.IsFalse(inventory.HasLoadable(AmmoType.Rifle556), "남은 것은 상자뿐");
            Assert.IsNull(inventory.TakeFullest(AmmoType.Rifle556));
            Assert.IsTrue(inventory.IsInStorage(boxItem));
        }

        [Test]
        public void Box_CannotBeEquipped()
        {
            Magazine b = Pocket(0, 8);
            foreach (EquipSlot slot in System.Enum.GetValues(typeof(EquipSlot)))
                Assert.IsFalse(inventory.CanEquip(b, slot), slot.ToString());
        }

        // ── WeaponModel 튜브 장전 연동 ──────────────────────────

        [Test]
        public void ShotgunTubeReload_DrawsFromInventory()
        {
            Magazine small = Pocket(0, 2);
            Magazine big = Pocket(1, 8);
            var shotgun = new WeaponItem(M7TestData.Shotgun());
            var model = new WeaponModel(shotgun, inventory);   // looseAmmo = source as ILooseAmmoSource

            Assert.IsTrue(model.IsTubeReload);
            Assert.IsTrue(model.CanReload);
            Assert.IsTrue(model.PressReload());
            for (int i = 0; i < 20 && model.IsReloading; i++)
                model.Tick(0.5f);

            Assert.IsFalse(model.IsReloading);
            Assert.AreEqual(6, shotgun.LoadedMagazine.Rounds, "튜브 6발");
            Assert.AreEqual(0, small.Rounds, "적은 상자부터 비운다");
            Assert.AreEqual(4, big.Rounds);
            Assert.IsTrue(inventory.IsInStorage(small), "빈 상자는 남는다");
            Assert.IsFalse(model.CanReload, "튜브가 가득");
        }

        [Test]
        public void ShotgunTubeReload_RefusedAfterDeath()
        {
            Pocket(0, 8);
            var model = new WeaponModel(new WeaponItem(M7TestData.Shotgun()), inventory);
            inventory.IsDeadLocked = true;
            Assert.IsFalse(model.CanReload);
            Assert.IsFalse(model.PressReload());
        }
    }
}
