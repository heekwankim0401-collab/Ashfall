using System.Collections.Generic;
using Ashfall.Core;
using Ashfall.Inventory;
using Ashfall.Player;
using Ashfall.UI;
using Ashfall.Weapons;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // 테스트용 설정. GDD 표 값을 그대로 넣는다 (에셋 값이 바뀌어도 규칙 검증은 표 기준).
    // GDD에 없는 값은 M2 설계 §4의 임시값과 같다 (TODO(GDD-?)).
    // M2 2단계 이후로는 이 파일을 고치지 않는다. 추가 도우미는 각자 새 파일(HealthTestData.cs 등)에.
    internal static class TestData
    {
        // §4.1 + §4.2 + §4.3 (+ 임시값)
        public static PlayerConfigSO Player()
        {
            var c = ScriptableObject.CreateInstance<PlayerConfigSO>();
            c.walkSpeed = 3.0f; c.sprintSpeed = 5.5f; c.crouchSpeed = 1.6f; c.proneSpeed = 0.8f;
            c.walkNoise = 8f; c.sprintNoise = 20f; c.crouchNoise = 3f; c.proneNoise = 1f; c.jumpNoise = 15f;
            c.jumpHeightM = 1.0f; c.jumpStaminaCost = 20f; c.leanAngleDeg = 15f;
            c.staminaMax = 100f; c.sprintStaminaPerSec = 15f;
            c.staminaRegenMovingPerSec = 10f; c.staminaRegenStationaryPerSec = 20f; c.exhaustedSprintLockSec = 3f;
            c.weightPenaltyStartKg = 25f; c.weightPenaltyPerKg = 0.02f; c.sprintMaxWeightKg = 40f;
            c.standHeight = 1.8f; c.crouchHeight = 1.2f; c.proneHeight = 0.6f; c.eyeBelowTop = 0.12f;

            // §4.2
            c.headMaxHp = 35f; c.thoraxMaxHp = 80f; c.stomachMaxHp = 70f; c.armMaxHp = 60f; c.legMaxHp = 65f;
            c.destroyedPartRedistribution = 0.7f;
            c.stomachDestroyedDamageMultiplier = 1.5f;
            c.armDestroyedAimSwayAdd = 1.0f;
            c.legDestroyedSpeedMultiplier = 0.6f;
            // §4.3
            c.bleedHpPerSec = 1f; c.heavyBleedHpPerSec = 3f;
            // 임시값 (설계 §4.1)
            c.bleedMinDamage = 10f; c.heavyBleedChance = 0.15f; c.bleedChance = 0.35f;
            c.fractureMinDamage = 20f; c.fractureChance = 0.2f;
            c.fractureLegSpeedMultiplier = 0.8f; c.fractureArmAimSwayAdd = 0.5f;
            c.tierInjuredBelow = 0.75f; c.tierCriticalBelow = 0.35f;
            c.headZoneFrom = 0.87f; c.thoraxZoneFrom = 0.62f; c.stomachZoneFrom = 0.45f; c.armZoneSideRatio = 0.6f;
            return c;
        }

        // ── 무기 (§5.2) ───────────────────────────────────────

        public static WeaponDefinitionSO Rifle()
        {
            var w = ScriptableObject.CreateInstance<WeaponDefinitionSO>();
            w.id = "WPN-RIF-01"; w.displayName = "Rifle"; w.size = new Vector2Int(4, 2);
            w.cls = WeaponClass.AssaultRifle;
            w.ammo = AmmoType.Rifle556; w.rpm = 650; w.modes = new[] { FireMode.Auto };
            w.reloadTimeSec = 2.5f; w.fastReloadTimeSec = 1.5f;
            return w;
        }

        // 장착 슬롯 판정(Secondary) 테스트용 권총. 수치는 의미 없음.
        public static WeaponDefinitionSO Pistol()
        {
            var w = ScriptableObject.CreateInstance<WeaponDefinitionSO>();
            w.id = "TEST-PST"; w.displayName = "Pistol"; w.size = new Vector2Int(2, 1); w.weightKg = 0.8f;
            w.cls = WeaponClass.Pistol;
            w.ammo = AmmoType.Pistol9mm; w.rpm = 400; w.modes = new[] { FireMode.Semi };
            w.reloadTimeSec = 2f; w.fastReloadTimeSec = 1f;
            return w;
        }

        public static MagazineDefinitionSO Magazine30()
        {
            var m = ScriptableObject.CreateInstance<MagazineDefinitionSO>();
            m.id = "MAG-556-30"; m.displayName = "5.56 Mag"; m.size = new Vector2Int(1, 2);
            m.ammo = AmmoType.Rifle556; m.capacity = 30;
            return m;
        }

        // 다른 탄 탄창 (호환 판정 테스트용)
        public static MagazineDefinitionSO PistolMagazine()
        {
            var m = ScriptableObject.CreateInstance<MagazineDefinitionSO>();
            m.id = "TEST-MAG-9"; m.displayName = "9mm Mag"; m.size = new Vector2Int(1, 1);
            m.ammo = AmmoType.Pistol9mm; m.capacity = 15;
            return m;
        }

        // ── 의약품 (설계 §4.5, TODO(GDD-?)) ─────────────────────

        public static MedicalDefinitionSO Bandage() =>
            Medical("MED-BND-01", "붕대", new Vector2Int(1, 1), 0.05f, 3f, StatusEffect.Bleed);

        public static MedicalDefinitionSO Tourniquet() =>
            Medical("MED-TRQ-01", "지혈대", new Vector2Int(1, 1), 0.1f, 4f, StatusEffect.HeavyBleed);

        public static MedicalDefinitionSO Splint() =>
            Medical("MED-SPL-01", "부목", new Vector2Int(1, 2), 0.2f, 5f, StatusEffect.Fracture);

        public static MedicalDefinitionSO Medical(string id, string name, Vector2Int size, float kg, float useTime,
            StatusEffect cure, float heal = 0f, int uses = 1)
        {
            var m = ScriptableObject.CreateInstance<MedicalDefinitionSO>();
            m.id = id; m.displayName = name; m.size = size; m.weightKg = kg;
            m.cures = new[] { cure }; m.healAmount = heal; m.useTime = useTime; m.uses = uses;
            return m;
        }

        // ── 조끼·배낭·방어구 (설계 §4.5) ─────────────────────────

        public static ContainerDefinitionSO Vest() =>
            Container("RIG-01", "전술조끼", ContainerKind.TacticalVest, new Vector2Int(2, 2), 1.2f, new Vector2Int(4, 2));

        public static ContainerDefinitionSO Backpack() =>
            Container("BAG-01", "배낭", ContainerKind.Backpack, new Vector2Int(3, 3), 1.5f, new Vector2Int(4, 5));

        public static ContainerDefinitionSO Container(string id, string name, ContainerKind kind, Vector2Int size, float kg, Vector2Int grid)
        {
            var c = ScriptableObject.CreateInstance<ContainerDefinitionSO>();
            c.id = id; c.displayName = name; c.size = size; c.weightKg = kg;
            c.kind = kind; c.gridSize = grid;
            return c;
        }

        public static ArmorDefinitionSO Helmet() =>
            Armor("ARM-HLM-01", "헬멧", ArmorKind.Helmet, new Vector2Int(2, 2), 1.4f, 40f, BodyPart.Head);

        public static ArmorDefinitionSO BodyArmor() =>
            Armor("ARM-BDY-01", "방탄복", ArmorKind.BodyArmor, new Vector2Int(3, 3), 8.0f, 60f, BodyPart.Thorax, BodyPart.Stomach);

        public static ArmorDefinitionSO Armor(string id, string name, ArmorKind kind, Vector2Int size, float kg, float durability,
            params BodyPart[] covers)
        {
            var a = ScriptableObject.CreateInstance<ArmorDefinitionSO>();
            a.id = id; a.displayName = name; a.size = size; a.weightKg = kg;
            a.armorClass = 3; a.maxDurability = durability; a.covers = covers; a.kind = kind;
            return a;
        }

        // ── 잡동사니 (설계 §4.5) ──────────────────────────────

        public static ItemDefinitionSO Pipe() => Item("JNK-01", "녹슨 파이프", new Vector2Int(1, 3), 1.0f);
        public static ItemDefinitionSO EnginePart() => Item("JNK-02", "엔진 부품", new Vector2Int(3, 2), 20.0f);
        public static ItemDefinitionSO Wires() => Item("JNK-03", "전선 뭉치", new Vector2Int(1, 1), 0.3f);

        public static ItemDefinitionSO Item(string id, string name, Vector2Int size, float kg, Rarity rarity = Rarity.Scrap)
        {
            var i = ScriptableObject.CreateInstance<ItemDefinitionSO>();
            i.id = id; i.displayName = name; i.size = size; i.weightKg = kg; i.rarity = rarity;
            return i;
        }

        // ── 설정 SO (설계 §4.2~4.4) ───────────────────────────

        public static WeaponHandlingConfigSO WeaponHandling()
        {
            var h = ScriptableObject.CreateInstance<WeaponHandlingConfigSO>();
            h.magCheckTimeSec = 0.8f;
            h.magCheckFullAtOrAbove = 0.67f; h.magCheckHalfAtOrAbove = 0.34f;
            h.aimSwayBaseDeg = 0.15f; h.aimSwayFrequencyHz = 0.5f;
            return h;
        }

        public static InventoryConfigSO Inventory()
        {
            var i = ScriptableObject.CreateInstance<InventoryConfigSO>();
            i.pocketCount = 4; i.pocketSize = Vector2Int.one;
            i.searchTimePerItemSec = 0.5f;
            i.interactRangeM = 2.0f;
            i.reloadFromBackpack = false;
            i.dropForwardM = 0.6f; i.dropSpeed = 1.5f; i.worldItemCellM = 0.1f;
            return i;
        }

        public static HudConfigSO Hud()
        {
            var h = ScriptableObject.CreateInstance<HudConfigSO>();
            h.magCheckResultSec = 2.0f;
            return h;
        }
    }

    // NextDouble이 정해진 값을 차례로 돌려주는 난수 (상태이상 굴림 테스트용). 다 쓰면 마지막 값을 반복한다.
    internal sealed class FixedRandom : System.Random
    {
        private readonly Queue<double> values;
        private double last;

        public FixedRandom(params double[] values)
        {
            this.values = new Queue<double>(values);
            last = values.Length > 0 ? values[values.Length - 1] : 0.0;
        }

        public override double NextDouble()
        {
            if (values.Count > 0)
                last = values.Dequeue();
            return last;
        }

        protected override double Sample() => NextDouble();
        public override int Next(int maxValue) => (int)(NextDouble() * maxValue);
        public override int Next(int minValue, int maxValue) => minValue + (int)(NextDouble() * (maxValue - minValue));
    }
}
