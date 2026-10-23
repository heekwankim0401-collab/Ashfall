using Ashfall.AI;
using Ashfall.Core;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // 시체 아이템 (§6 시체, M3 설계 §1.9, M3 AC2): 무기(장착 탄창) → 예비 탄창 → 루트 테이블
    public class CorpseLootBuilderTests
    {
        private EnemyWeapon Weapon(EnemyArchetypeSO a, int loadout, int spares) =>
            new EnemyWeapon(a.loadouts[loadout], spares, a, new FakeProjectileService(), new System.Random(2));

        private static void Shoot(EnemyWeapon w, float seconds)
        {
            const float dt = 1f / 60f;
            for (int f = 0; f * dt < seconds; f++)
                w.Tick(f * dt, dt, true, Vector3.zero, Vector3.forward * 10f, 0f, null);
        }

        [Test]
        public void Order_WeaponThenMagazinesThenLoot()
        {
            EnemyArchetypeSO scv = M3TestData.Scv();
            EnemyWeapon w = Weapon(scv, 1, 2);
            LootTableSO table = M3TestData.LootTable(new Vector2Int(2, 2), (TestData.Wires(), 1f));

            ItemInstance[] items = CorpseLootBuilder.Build(w, table, new System.Random(1));

            Assert.AreEqual(1 + 2 + 2, items.Length);
            Assert.AreSame(w.Item, items[0], "첫 아이템 = 그 적의 무기");
            Assert.IsInstanceOf<WeaponItem>(items[0]);
            Assert.IsInstanceOf<Magazine>(items[1]);
            Assert.IsInstanceOf<Magazine>(items[2]);
            Assert.AreEqual("JNK-03", items[3].Definition.id);
            Assert.AreEqual("JNK-03", items[4].Definition.id);
        }

        [Test]
        public void LoadedMagazine_KeepsRemainingRounds()
        {
            EnemyWeapon w = Weapon(M3TestData.Scv(), 1, 1);
            Shoot(w, 0.5f);
            int remaining = w.Item.LoadedMagazine.Rounds;
            Assert.Less(remaining, 30);

            ItemInstance[] items = CorpseLootBuilder.Build(w, null, new System.Random(1));
            var weapon = (WeaponItem)items[0];
            Assert.AreEqual(remaining, weapon.LoadedMagazine.Rounds, "사망 시점 탄 수 그대로");
            Assert.AreEqual(30, ((Magazine)items[1]).Rounds);
            Assert.AreEqual(2, items.Length, "루트 테이블 없음");
        }

        [Test]
        public void ReloadInProgress_CancelledAndMagazineKept()
        {
            EnemyWeapon w = Weapon(M3TestData.Rdr(), 0, 1);
            Shoot(w, 0.3f);
            Magazine loaded = w.Item.LoadedMagazine;
            Assert.IsTrue(w.TryReload());

            ItemInstance[] items = CorpseLootBuilder.Build(w, null, new System.Random(1));
            Assert.IsFalse(w.Model.IsReloading);
            Assert.AreSame(loaded, ((WeaponItem)items[0]).LoadedMagazine);
            Assert.AreEqual(30, ((Magazine)items[1]).Rounds, "예비 탄창 그대로");
        }

        [Test]
        public void EmptiedMagazines_AreIncludedWithRealCount()
        {
            EnemyWeapon w = Weapon(M3TestData.Scv(), 0, 1);   // 권총 12 + 예비 1
            Shoot(w, 20f);                                   // 둘 다 비움 (재장전 포함)
            ItemInstance[] items = CorpseLootBuilder.Build(w, null, new System.Random(1));
            Assert.AreEqual(2, items.Length);
            Assert.AreEqual(0, ((WeaponItem)items[0]).LoadedMagazine.Rounds);
            Assert.AreEqual(0, ((Magazine)items[1]).Rounds, "회수된 빈 탄창");
        }

        [Test]
        public void NullWeapon_OnlyLoot()
        {
            LootTableSO table = M3TestData.LootTable(new Vector2Int(1, 1), (TestData.Bandage(), 1f));
            ItemInstance[] items = CorpseLootBuilder.Build(null, table, new System.Random(1));
            Assert.AreEqual(1, items.Length);
            Assert.AreEqual("MED-BND-01", items[0].Definition.id);
        }

        [Test]
        public void ArchetypeLoot_RollCountRange_SeedReproducible()
        {
            EnemyArchetypeSO rdr = M3TestData.Rdr();   // LT-RDR rollCount (1, 3)
            for (int seed = 1; seed <= 30; seed++)
            {
                ItemInstance[] a = CorpseLootBuilder.Build(Weapon(rdr, 0, 3), rdr.loot, new System.Random(seed));
                ItemInstance[] b = CorpseLootBuilder.Build(Weapon(rdr, 0, 3), rdr.loot, new System.Random(seed));
                int loot = a.Length - 4;
                Assert.That(loot, Is.InRange(1, 3));
                Assert.AreEqual(a.Length, b.Length);
                for (int i = 4; i < a.Length; i++)
                    Assert.AreEqual(a[i].Definition.id, b[i].Definition.id);
            }
        }

        // ── M7 7B-2b: 적 전용 무기 (Q27) ──

        // 보스 LMG 대역: 소총 정의 복제 + enemyOnly. 탄창 = 30발 탄창(테스트용).
        private static EnemyArchetypeSO BossLike(out WeaponDefinitionSO lmg, out MagazineDefinitionSO belt)
        {
            EnemyArchetypeSO a = M3TestData.Rdr();
            lmg = M3TestData.RifleWithDamage();
            lmg.id = "WPN-LMG-TEST";
            lmg.enemyOnly = true;
            belt = TestData.Magazine30();
            a.loadouts = new System.Collections.Generic.List<EnemyLoadout>
            {
                new EnemyLoadout { weapon = lmg, ammo = M3TestData.Ammo556(), magazine = belt, weight = 1f },
            };
            return a;
        }

        [Test]
        public void EnemyOnlyWeapon_WeaponAndMagazinesExcluded_LootKept()
        {
            EnemyArchetypeSO boss = BossLike(out _, out _);
            EnemyWeapon w = Weapon(boss, 0, 2);
            LootTableSO table = M3TestData.LootTable(new Vector2Int(2, 2), (TestData.Wires(), 1f));

            ItemInstance[] items = CorpseLootBuilder.Build(w, table, new System.Random(1));

            Assert.AreEqual(2, items.Length, "무기·장착 탄창·예비 2 없음, 루트 2만");
            foreach (ItemInstance item in items)
            {
                Assert.IsNotInstanceOf<WeaponItem>(item);
                Assert.IsNotInstanceOf<Magazine>(item);
                Assert.AreEqual("JNK-03", item.Definition.id);
            }
        }

        [Test]
        public void EnemyOnlyWeapon_LootTableRollsOfItOrItsMagazine_Dropped()
        {
            EnemyArchetypeSO boss = BossLike(out WeaponDefinitionSO lmg, out MagazineDefinitionSO belt);
            EnemyWeapon w = Weapon(boss, 0, 1);
            LootTableSO table = M3TestData.LootTable(new Vector2Int(30, 30),
                (lmg, 1f), (belt, 1f), (TestData.Bandage(), 1f));

            ItemInstance[] items = CorpseLootBuilder.Build(w, table, new System.Random(4));

            Assert.Greater(items.Length, 0, "다른 루트는 남는다");
            foreach (ItemInstance item in items)
                Assert.AreEqual("MED-BND-01", item.Definition.id, "적 전용 무기·그 탄창은 루트에서도 빠짐");
        }

        [Test]
        public void EnemyOnlyWeaponInLoot_DroppedEvenForNormalWeaponCorpse()
        {
            BossLike(out WeaponDefinitionSO lmg, out _);
            EnemyArchetypeSO rdr = M3TestData.Rdr();
            EnemyWeapon w = Weapon(rdr, 0, 1);
            LootTableSO table = M3TestData.LootTable(new Vector2Int(10, 10), (lmg, 1f), (TestData.Bandage(), 1f));

            ItemInstance[] items = CorpseLootBuilder.Build(w, table, new System.Random(2));

            Assert.AreSame(w.Item, items[0], "일반 무기는 그대로");
            Assert.IsInstanceOf<Magazine>(items[1]);
            for (int i = 2; i < items.Length; i++)
                Assert.AreEqual("MED-BND-01", items[i].Definition.id);
        }

        [Test]
        public void NormalWeapon_StillIncluded_WithSpawnDurability()
        {
            EnemyArchetypeSO rdr = M3TestData.Rdr();
            rdr.weaponDurabilityRange = new Vector2(35f, 85f);
            EnemyWeapon w = Weapon(rdr, 0, 1);
            ItemInstance[] items = CorpseLootBuilder.Build(w, null, new System.Random(1));
            var weapon = (WeaponItem)items[0];
            Assert.IsFalse(weapon.Definition.enemyOnly);
            Assert.That(weapon.Durability, Is.InRange(35f, 85f), "시체 무기 = 스폰 내구도 범위 (Q12)");
            Assert.IsTrue(w.SpawnDurabilityRolled);
        }

        // 내구도 굴림은 루트 굴림 뒤 → 같은 시드의 루트 결과는 내구도 범위와 무관 (M3 시드 재현 유지)
        [Test]
        public void DurabilityRoll_DoesNotChangeLootRolls()
        {
            for (int seed = 1; seed <= 20; seed++)
            {
                EnemyArchetypeSO plain = M3TestData.Rdr();
                EnemyArchetypeSO worn = M3TestData.Rdr();
                worn.weaponDurabilityRange = new Vector2(35f, 85f);
                ItemInstance[] a = CorpseLootBuilder.Build(Weapon(plain, 0, 3), plain.loot, new System.Random(seed));
                ItemInstance[] b = CorpseLootBuilder.Build(Weapon(worn, 0, 3), worn.loot, new System.Random(seed));
                Assert.AreEqual(a.Length, b.Length);
                for (int i = 4; i < a.Length; i++)
                    Assert.AreEqual(a[i].Definition.id, b[i].Definition.id, $"seed {seed} 루트 {i}");
                Assert.AreEqual(100f, ((WeaponItem)a[0]).Durability);
            }
        }

        [Test]
        public void EnemyOnlyWeapon_DurabilityNotRolled()
        {
            EnemyArchetypeSO boss = BossLike(out _, out _);
            boss.weaponDurabilityRange = new Vector2(35f, 85f);
            EnemyWeapon w = Weapon(boss, 0, 1);
            CorpseLootBuilder.Build(w, null, new System.Random(1));
            Assert.IsFalse(w.SpawnDurabilityRolled, "시체에 안 들어가는 무기는 굴리지 않음");
        }
    }
}
