#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Ashfall.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ashfall.Tests.PlayMode
{
    // MVP 마무리 F4 §5.3 방어구 흐름 (설계 §7 AC-F4, MF-Q16~Q24). M2 씬(실제 ProjectileSystem 탄 → 플레이어 캡슐 → PlayerHealth → ArmorRules).
    // 결정적 조합만 쓴다(관통 확률 0 또는 1 = 난수 미소비). 피해는 상태이상 굴림 최소 피해(10)보다 작은 값이 몸에 들어가게 해 굴림이 없다.
    public class ArmorFlowTests
    {
        private const float ShotDistanceM = 4f;
        private const float ThoraxRatio = 0.75f;
        private const float HeadRatio = 0.93f;

        private M2Scene scene;
        private readonly List<Object> created = new List<Object>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            scene = new M2Scene();
            yield return M2Scene.Load(scene);
            Assert.IsNull(scene.Inventory.GetEquipped(EquipSlot.Helmet), "M2 씬은 헬멧 없이 시작");
            Assert.IsNull(scene.Inventory.GetEquipped(EquipSlot.BodyArmor), "M2 씬은 방탄복 없이 시작");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in created)
                if (o != null)
                    Object.Destroy(o);
            created.Clear();
        }

        private ArmorDefinitionSO ArmorDef(string id, ArmorKind kind, int armorClass, float maxDurability, params BodyPart[] covers)
        {
            var a = ScriptableObject.CreateInstance<ArmorDefinitionSO>();
            a.id = id; a.displayName = id; a.size = new Vector2Int(2, 2); a.weightKg = 1f;
            a.kind = kind; a.armorClass = armorClass; a.maxDurability = maxDurability; a.covers = covers;
            created.Add(a);
            return a;
        }

        private ArmorItem Equip(ArmorDefinitionSO def, EquipSlot slot)
        {
            var item = (ArmorItem)ItemFactory.Create(def);
            Assert.IsTrue(scene.Inventory.TryEquipNew(item, slot), $"{def.id} → {slot}");
            return item;
        }

        private AmmoDefinitionSO Ammo(float damage, int penetration)
        {
            var ammo = ScriptableObject.CreateInstance<AmmoDefinitionSO>();
            ammo.damage = damage; ammo.velocity = 900f; ammo.maxFlightTimeSec = 1f; ammo.penetration = penetration; ammo.pelletCount = 1;
            created.Add(ammo);
            return ammo;
        }

        // PlayerDamageTests.ShootPlayer와 같은 조준(물리 캡슐 배율 보정)
        private IEnumerator ShootPlayer(float heightRatio, float damage, int penetration)
        {
            Transform player = scene.Motor.transform;
            var controller = player.GetComponent<CharacterController>();
            Vector3 center = player.TransformPoint(controller.center);
            Assert.IsTrue(Physics.Raycast(center + Vector3.up * 3f, Vector3.down, out RaycastHit top, 3f), "캡슐 꼭대기 측정 실패");
            float physicsScale = (top.point.y - center.y) / (controller.height * 0.5f);
            Vector3 target = center + Vector3.up * ((heightRatio - 0.5f) * controller.height * physicsScale);
            Vector3 origin = target + player.forward * ShotDistanceM;
            scene.Projectiles.Fire(origin, -player.forward, Ammo(damage, penetration), null);
            yield return M2Scene.WaitUntil(() => scene.Projectiles.LiveCount == 0, 3f);
        }

        private float[] Snapshot() =>
            System.Enum.GetValues(typeof(BodyPart)).Cast<BodyPart>().Select(p => scene.Body.GetHp(p)).ToArray();

        private void AssertOnlyPartDamaged(float[] before, BodyPart part, float damage)
        {
            float[] after = Snapshot();
            foreach (BodyPart p in System.Enum.GetValues(typeof(BodyPart)))
            {
                float expected = p == part ? before[(int)p] - damage : before[(int)p];
                Assert.AreEqual(expected, after[(int)p], 1e-3f, $"{part} 피격 후 {p} HP");
            }
        }

        // 방탄복(등급 3) + 9mm(관통 1 → 확률 0): 흉부 피해 30% · 방탄복 내구도 −원피해 × 0.2. 머리(헬멧 없음)는 그대로.
        [UnityTest]
        public IEnumerator BodyArmor_9mm_Thorax_Blunt30Percent_WearsArmor_HeadUnprotected()
        {
            ArmorItem vest = Equip(ArmorDef("TEST-BDY", ArmorKind.BodyArmor, 3, 60f, BodyPart.Thorax, BodyPart.Stomach), EquipSlot.BodyArmor);

            float[] before = Snapshot();
            yield return ShootPlayer(ThoraxRatio, 30f, 1);
            AssertOnlyPartDamaged(before, BodyPart.Thorax, 9f);
            Assert.AreEqual(54f, vest.Durability, 1e-3f, "방탄복 60 − 30 × 0.2");

            before = Snapshot();
            yield return ShootPlayer(HeadRatio, 8f, 1);
            AssertOnlyPartDamaged(before, BodyPart.Head, 8f);
            Assert.AreEqual(54f, vest.Durability, 1e-3f, "머리 피격은 방탄복을 깎지 않는다");
            for (int p = 0; p < 7; p++)
                Assert.IsFalse(scene.Body.HasStatus((BodyPart)p, StatusEffect.Bleed), "몸에 들어간 피해 < 10 → 상태이상 굴림 없음");
        }

        // 헬멧 내구도 0 = 막지 못함 → 머리 피해 그대로, 내구도도 0 그대로.
        [UnityTest]
        public IEnumerator Helmet_DurabilityZero_HeadTakesFullDamage()
        {
            ArmorItem helmet = Equip(ArmorDef("TEST-HLM3", ArmorKind.Helmet, 3, 40f, BodyPart.Head), EquipSlot.Helmet);
            helmet.Durability = 0f;

            float[] before = Snapshot();
            yield return ShootPlayer(HeadRatio, 8f, 1);
            AssertOnlyPartDamaged(before, BodyPart.Head, 8f);
            Assert.AreEqual(0f, helmet.Durability);
        }

        // 7.62(관통 4) 대 등급 2(확률 1): 전부 관통 · 마모 없음.
        [UnityTest]
        public IEnumerator Class2_Vs762_AlwaysPenetrates_NoWear()
        {
            ArmorItem vest = Equip(ArmorDef("TEST-BDY2", ArmorKind.BodyArmor, 2, 50f, BodyPart.Thorax, BodyPart.Stomach), EquipSlot.BodyArmor);

            float[] before = Snapshot();
            yield return ShootPlayer(ThoraxRatio, 8f, 4);
            AssertOnlyPartDamaged(before, BodyPart.Thorax, 8f);
            Assert.AreEqual(50f, vest.Durability, "관통은 마모 없음");
        }

        // 변이체 근접(부위 힌트, 관통 0) 대 방탄복 등급 3: 같은 공식(MF-Q20) → 30%. 헬멧(머리만)은 흉부를 막지 않는다.
        [UnityTest]
        public IEnumerator MeleeHint_Thorax_BlockedByBodyArmor_HelmetIgnored()
        {
            ArmorItem helmet = Equip(ArmorDef("TEST-HLM", ArmorKind.Helmet, 3, 40f, BodyPart.Head), EquipSlot.Helmet);
            ArmorItem vest = Equip(ArmorDef("TEST-BDY3", ArmorKind.BodyArmor, 3, 60f, BodyPart.Thorax, BodyPart.Stomach), EquipSlot.BodyArmor);
            yield return null;

            float[] before = Snapshot();
            Vector3 p = scene.Motor.transform.position + Vector3.up;
            scene.Health.ApplyDamage(new DamageInfo(20f, 0, p, Vector3.forward, 1f, BodyPart.Thorax));
            AssertOnlyPartDamaged(before, BodyPart.Thorax, 6f);
            Assert.AreEqual(56f, vest.Durability, 1e-3f);
            Assert.AreEqual(40f, helmet.Durability, "헬멧은 흉부를 덮지 않는다");
        }
    }
}
#endif
