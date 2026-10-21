using System.Collections.Generic;
using Ashfall.Core;
using Ashfall.Inventory;
using Ashfall.Raid;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // M4 AC2 — EXT-02 전원 스위치 F 상호작용 (Core IWorldInteractable, M4 설계 §1.1·§4.4 Q5):
    // PowerSwitch(Raid) 동작 + Inventory의 F 대상 판정(InteractionTargets)이 Core 인터페이스를 잡는지.
    public class WorldInteractableTests
    {
        private readonly List<Object> created = new List<Object>();
        private RaidConfigSO config;

        [SetUp]
        public void SetUp()
        {
            config = M4TestData.RaidConfig();
            created.Add(config);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in created)
                if (o != null)
                    Object.DestroyImmediate(o);
            created.Clear();
        }

        private GameObject NewObject(string name)
        {
            var go = new GameObject(name);
            created.Add(go);
            return go;
        }

        private PowerSwitch NewSwitch(GameObject host = null, string switchId = M4TestData.PowerSwitchId,
            string displayName = "전원 스위치")
        {
            host = host ? host : NewObject("PowerSwitch");
            var sw = host.AddComponent<PowerSwitch>();
            var so = new SerializedObject(sw);
            so.FindProperty("switchId").stringValue = switchId;
            so.FindProperty("displayName").stringValue = displayName;
            so.ApplyModifiedPropertiesWithoutUndo();
            return sw;
        }

        private RaidSession NewSession() => new RaidSession(config, new List<ExtractionModel>
        {
            new ExtractionModel(M4TestData.Find(config, M4TestData.Ext02), Vector3.zero, true),
        });

        // ── PowerSwitch ──────────────────────────────────────

        [Test]
        public void PowerSwitch_IsWorldInteractable_Activate()
        {
            PowerSwitch sw = NewSwitch();
            IWorldInteractable world = sw;
            Assert.AreEqual(InteractionKind.Activate, world.Kind);
            Assert.AreEqual("전원 스위치", world.TargetName);
            Assert.AreEqual(M4TestData.PowerSwitchId, sw.SwitchId);
        }

        [Test]
        public void PowerSwitch_Unbound_NotAvailable_InteractNoOp()
        {
            PowerSwitch sw = NewSwitch();
            Assert.IsFalse(sw.IsAvailable, "세션 전에는 프롬프트 없음");
            Assert.DoesNotThrow(() => sw.Interact());
            Assert.IsFalse(sw.IsOn);
        }

        [Test]
        public void PowerSwitch_Interact_TurnsOnOnce_ThenUnavailable()
        {
            PowerSwitch sw = NewSwitch();
            RaidSession session = NewSession();
            sw.Bind(session);
            Assert.IsTrue(sw.IsAvailable);

            sw.Interact();

            Assert.IsTrue(session.IsSwitchOn(M4TestData.PowerSwitchId), "EXT-02 전원");
            Assert.IsTrue(sw.IsOn);
            Assert.IsFalse(sw.IsAvailable, "Q5: 켠 뒤로는 레이드 끝까지 유지, 프롬프트 없음");
            sw.Interact();
            Assert.IsTrue(session.IsSwitchOn(M4TestData.PowerSwitchId));
        }

        [Test]
        public void PowerSwitch_AfterRaidOver_NotAvailable()
        {
            PowerSwitch sw = NewSwitch();
            RaidSession session = NewSession();
            sw.Bind(session);
            session.Abandon();

            Assert.IsFalse(sw.IsAvailable);
            sw.Interact();
            Assert.IsFalse(session.IsSwitchOn(M4TestData.PowerSwitchId));
        }

        [Test]
        public void PowerSwitch_UnbindWithNull()
        {
            PowerSwitch sw = NewSwitch();
            sw.Bind(NewSession());
            sw.Bind(null);
            Assert.IsFalse(sw.IsAvailable);
        }

        [Test]
        public void PowerSwitch_OtherSwitchIdDoesNotPowerExt02()
        {
            PowerSwitch sw = NewSwitch(switchId: "SW-OTHER");
            RaidSession session = NewSession();
            sw.Bind(session);
            sw.Interact();
            Assert.IsTrue(session.IsSwitchOn("SW-OTHER"));
            Assert.IsFalse(session.IsSwitchOn(M4TestData.PowerSwitchId));
        }

        // ── InteractionTargets (Inventory F 판정) ───────────

        [Test]
        public void Find_ColliderOnSwitch_ReturnsSwitch()
        {
            GameObject go = NewObject("Switch");
            go.AddComponent<BoxCollider>();
            PowerSwitch sw = NewSwitch(go);

            Assert.AreSame(sw, InteractionTargets.Find(go.GetComponent<Collider>()));
        }

        [Test]
        public void Find_ChildCollider_FindsParentSwitch()
        {
            GameObject root = NewObject("SwitchRoot");
            PowerSwitch sw = NewSwitch(root);
            GameObject child = NewObject("Lever");
            child.transform.SetParent(root.transform);
            var collider = child.AddComponent<BoxCollider>();

            Assert.AreSame(sw, InteractionTargets.Find(collider));
        }

        [Test]
        public void Find_NoTarget_Null()
        {
            var collider = NewObject("Wall").AddComponent<BoxCollider>();
            Assert.IsNull(InteractionTargets.Find(collider));
            Assert.IsNull(InteractionTargets.Find(null));
        }

        [Test]
        public void Find_InventoryInteractable_TakesPriority()
        {
            GameObject go = NewObject("Both");
            var collider = go.AddComponent<BoxCollider>();
            var loot = go.AddComponent<LootContainer>();
            NewSwitch(go);

            Assert.AreSame(loot, InteractionTargets.Find(collider));
        }

        [Test]
        public void IsUsable_FollowsAvailability()
        {
            PowerSwitch sw = NewSwitch();
            Assert.IsFalse(InteractionTargets.IsUsable(sw), "세션 없음");
            sw.Bind(NewSession());
            Assert.IsTrue(InteractionTargets.IsUsable(sw));
            sw.Interact();
            Assert.IsFalse(InteractionTargets.IsUsable(sw), "켠 뒤");
            Assert.IsFalse(InteractionTargets.IsUsable(null));
        }

        [Test]
        public void IsUsable_DestroyedTarget_False()
        {
            GameObject go = NewObject("Box");
            var loot = go.AddComponent<LootContainer>();
            Assert.IsTrue(InteractionTargets.IsUsable(loot));
            Object.DestroyImmediate(go);
            Assert.IsFalse(InteractionTargets.IsUsable(loot));
        }

        [Test]
        public void TryDescribe_Switch_ActivateWithName()
        {
            PowerSwitch sw = NewSwitch(displayName: "변전 설비 스위치");
            Assert.IsTrue(InteractionTargets.TryDescribe(sw, out InteractionKind kind, out string name));
            Assert.AreEqual(InteractionKind.Activate, kind);
            Assert.AreEqual("변전 설비 스위치", name);
        }

        [Test]
        public void TryDescribe_LootContainer_OpenContainer()
        {
            var loot = NewObject("Crate").AddComponent<LootContainer>();
            Assert.IsTrue(InteractionTargets.TryDescribe(loot, out InteractionKind kind, out _));
            Assert.AreEqual(InteractionKind.OpenContainer, kind);
            Assert.IsFalse(InteractionTargets.TryDescribe(null, out _, out _));
        }

        [Test]
        public void Interact_Switch_CallsWorldInteract()
        {
            PowerSwitch sw = NewSwitch();
            RaidSession session = NewSession();
            sw.Bind(session);

            Assert.IsTrue(InteractionTargets.Interact(sw, null));
            Assert.IsTrue(session.IsSwitchOn(M4TestData.PowerSwitchId));
            Assert.IsFalse(InteractionTargets.Interact(sw, null), "이미 켬 — 실행 안 함");
        }

        [Test]
        public void Interact_InventoryTarget_PassesBehaviour()
        {
            var fake = NewObject("Fake").AddComponent<FakeInventoryInteractable>();
            Assert.IsTrue(InteractionTargets.Interact(fake, null));
            Assert.AreEqual(1, fake.Calls);
        }
    }

    // 테스트 전용 Inventory 상호작용 대상 (호출 수만 센다)
    internal sealed class FakeInventoryInteractable : MonoBehaviour, IInteractable
    {
        public int Calls;
        public InteractionKind Kind => InteractionKind.PickUp;
        public string TargetName => "가짜";
        public void Interact(PlayerInventoryBehaviour by) => Calls++;
    }
}
