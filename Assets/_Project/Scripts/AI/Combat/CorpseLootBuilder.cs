using System.Collections.Generic;
using Ashfall.Core;

namespace Ashfall.AI
{
    // 시체 아이템 목록 (§6 시체, M3 설계 §1.9). 순서 = 검색으로 드러나는 순서: 무기 → 탄창 → 루트.
    // M7 (설계 §1.4, Q27): 적 전용 무기(WeaponDefinitionSO.enemyOnly — 보스 WPN-LMG-01)는 무기와 그 무기 탄창(장착·예비)을 넣지 않는다.
    // 루트 테이블 굴림 결과에서도 적 전용 무기 정의는 늘 빼고, 탄창은 이 시체의 무기가 적 전용일 때 그 무기 탄창(Loadout.magazine)만 뺀다.
    // (다른 적 전용 무기의 탄창이 표에 있으면 빼지 않는다 — 표에 없음은 데이터 테스트가 확인한다. 7B 리뷰 8)
    // 무기 내구도(EnemyWeapon.RollSpawnDurability, archetype.weaponDurabilityRange)도 여기서 정한다 — 적 무기는 마모가 없어(Q18)
    // 시체가 되기 전에는 값이 쓰이지 않는다.
    // 사망 때 한 번 불리므로 할당해도 된다.
    public static class CorpseLootBuilder
    {
        // weapon.CollectLoot + LootRoller.Roll(table). 진행 중 재장전은 먼저 취소한다(장착 탄창 그대로).
        public static ItemInstance[] Build(EnemyWeapon weapon, LootTableSO table, System.Random random)
        {
            var items = new List<ItemInstance>(8);
            MagazineDefinitionSO excludedMagazine = null;
            if (weapon != null)
            {
                weapon.CancelReload();
                if (IsEnemyOnly(weapon.Item.Definition))
                    excludedMagazine = weapon.Loadout.magazine;
                else
                    weapon.CollectLoot(items);
            }

            int lootStart = items.Count;
            LootRoller.Roll(table, random, items);
            // 굴림 결과 정리: 적 전용 무기 정의(어느 무기든) + 이 시체 무기가 적 전용이면 그 탄창 정의만
            for (int i = items.Count - 1; i >= lootStart; i--)
            {
                ItemDefinitionSO def = items[i].Definition;
                if (IsEnemyOnly(def as WeaponDefinitionSO) || (excludedMagazine != null && def == excludedMagazine))
                    items.RemoveAt(i);
            }

            // 시체 무기 내구도 (Q12): 루트 굴림 뒤에 굴려 루트 결과(시드 재현)를 바꾸지 않는다.
            if (weapon != null && excludedMagazine == null)
                weapon.RollSpawnDurability(random);
            return items.ToArray();
        }

        public static bool IsEnemyOnly(WeaponDefinitionSO weapon) => weapon != null && weapon.enemyOnly;
    }
}
