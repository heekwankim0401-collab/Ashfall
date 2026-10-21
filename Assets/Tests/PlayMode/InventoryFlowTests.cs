#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Ashfall.Core;
using Ashfall.Inventory;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Ashfall.Tests.PlayMode
{
    // M2 AC2 — 그리드 인벤토리, 아이템 회전, 장착 슬롯 (SYS-INV) + §4.1 무게 패널티 + §5.1 재장전 ↔ 인벤토리 탄창.
    // M2 씬에서 IInventoryService(레지스트리)로 명령하고, 다른 기능(Player·Weapons)에 이벤트로 도달하는지 본다.
    public class InventoryFlowTests
    {
        private M2Scene scene;
        private SimKeyboard keyboard;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            scene = new M2Scene();
            yield return M2Scene.Load(scene);
        }

        [TearDown]
        public void TearDown()
        {
            keyboard?.Dispose();
            Time.captureDeltaTime = 0f;
            keyboard = null;
        }

        [UnityTest]
        public IEnumerator StartingLoadout_IsPlacedAndPublished()
        {
            Assert.IsTrue(scene.Gm.Services.TryGet(out IInventoryService inventory));
            Assert.AreSame(scene.Inventory, inventory);

            var rifle = inventory.GetEquipped(EquipSlot.Primary1) as WeaponItem;
            Assert.IsNotNull(rifle);
            Assert.AreEqual(30, rifle.LoadedMagazine.Rounds, "장착 탄창 30발");
            Assert.IsTrue(scene.Weapon.IsArmed);
            Assert.AreSame(rifle, scene.Weapon.Model.Item, "WeaponController가 Primary1 무기를 들었다");

            var vest = scene.StorageGrid(GridKind.TacticalVest);
            Assert.AreEqual(3, vest.Items.Count(p => p.Item is Magazine), "조끼 탄창 3");
            Assert.AreEqual(4, inventory.StorageGrids.Count(g => g.Ref.Kind == GridKind.Pocket), "주머니 4");
            Assert.AreEqual(inventory.TotalWeightKg, scene.Motor.CarriedWeightKg, 1e-4f, "Start에서 CarriedWeightChanged → PlayerMotor");
            yield break;
        }

        [UnityTest]
        public IEnumerator MoveRotateEquip_AndWeight_ReachPlayerMotor()
        {
            IInventoryService inventory = scene.Inventory;
            using var weight = new EventLog<CarriedWeightChanged>(scene.Events);
            using var equipment = new EventLog<EquipmentChanged>(scene.Events);
            using var changed = new EventLog<InventoryChanged>(scene.Events);
            float baseKg = inventory.TotalWeightKg;

            // 회전 이동: 부목(1×2)을 배낭 맨 아래 줄에 회전(2×1)해서 놓는다
            ItemInstance splint = scene.FindStored("MED-SPL-01");
            Assert.IsNotNull(splint);
            GridRef backpack = scene.StorageGrid(GridKind.Backpack).Ref;
            Vector2Int bottom = new Vector2Int(0, scene.StorageGrid(GridKind.Backpack).Size.y - 1);
            Assert.IsFalse(inventory.CanPlace(splint, backpack, bottom, false), "회전 안 하면 맨 아래 줄에 1×2가 안 들어간다");
            Assert.IsTrue(inventory.TryMove(splint, backpack, bottom, true));
            Assert.IsTrue(inventory.TryGetLocation(splint, out ItemLocation loc));
            Assert.AreEqual(bottom, loc.Cell);
            Assert.IsTrue(loc.Rotated);
            Assert.AreEqual(new Vector2Int(2, 1), splint.SizeFor(loc.Rotated));
            Assert.GreaterOrEqual(changed.Count, 1, "이동 → InventoryChanged");

            // 바닥 방탄복 줍기(F 상호작용 대상) → 저장 공간 → 무게 +8kg가 PlayerMotor에 도달
            WorldItem armorOnFloor = M2Scene.FindWorldItem("ARM-BDY-01");
            Assert.IsNotNull(armorOnFloor);
            ItemInstance armor = armorOnFloor.Item;
            armorOnFloor.Interact(scene.InventoryBehaviour);
            Assert.IsTrue(scene.Inventory.IsInStorage(armor));
            yield return null;
            Assert.IsTrue(armorOnFloor == null, "주운 WorldItem 제거");
            Assert.AreEqual(baseKg + 8f, scene.Motor.CarriedWeightKg, 1e-3f);
            Assert.AreEqual(baseKg + 8f, weight.Last.TotalKg, 1e-3f);

            // 장착 슬롯: 잘못된 슬롯 거부, 맞는 슬롯 장착 → EquipmentChanged. 장착품도 무게에 포함
            Assert.IsFalse(inventory.TryEquip(armor, EquipSlot.Helmet));
            Assert.IsFalse(inventory.TryEquip(armor, EquipSlot.Primary2));
            Assert.IsTrue(inventory.TryEquip(armor, EquipSlot.BodyArmor));
            Assert.AreSame(armor, inventory.GetEquipped(EquipSlot.BodyArmor));
            Assert.IsFalse(scene.Inventory.IsInStorage(armor));
            Assert.AreEqual(EquipSlot.BodyArmor, equipment.Last.Slot);
            Assert.AreSame(armor, equipment.Last.Item);
            Assert.AreEqual(baseKg + 8f, inventory.TotalWeightKg, 1e-3f);

            // 내용물 있는 조끼는 해제 불가
            ItemInstance vest = inventory.GetEquipped(EquipSlot.TacticalVest);
            Assert.IsFalse(inventory.TryDrop(vest));

            // 상자 C(엔진 부품 20kg × 2) → 저장 공간 → 40kg 초과: 질주 불가, 속도 감소
            var boxC = scene.LootBox("LootBox_C");
            boxC.Interact(scene.InventoryBehaviour);
            ILootView loot = inventory.ActiveLoot;
            Assert.IsNotNull(loot);
            yield return M2Scene.WaitUntil(() => loot.SearchComplete, 3f);
            foreach (ItemInstance part in loot.Items.Select(p => p.Item).ToArray())
                Assert.IsTrue(inventory.TryQuickMove(part), $"{part} 빠른 이동");
            yield return null;
            float kg = scene.Motor.CarriedWeightKg;
            Assert.AreEqual(baseKg + 48f, kg, 1e-3f, "CarriedWeightChanged → PlayerMotor");
            Assert.Greater(kg, 40f);
            var motor = scene.Motor;
            Assert.IsFalse(motor.Locomotion.CanSprint(true, kg, motor.Stamina, motor.Condition), "40kg 초과 질주 불가");
            float expectedWalk = motor.Locomotion.Speed(false, 0f, ConditionModifiers.Healthy) * (1f - 0.02f * (kg - 25f));
            Assert.AreEqual(expectedWalk, motor.Locomotion.Speed(false, kg, motor.Condition), 1e-3f, "§4.1 25kg 초과 -2%/kg");

            // 실제 이동: 가상 키보드 W+Shift → 질주하지 않고 감속된 걷기 속도
            CloseScreens();
            yield return null;
            keyboard = new SimKeyboard();
            // 씬의 CharacterController.minMoveDistance = 0이라 배치모드 초고속 프레임(≈1ms)에서도 dt 고정 없이 이동한다 (리뷰 수정 8).
            keyboard.Hold(Key.W, Key.LeftShift);
            for (int i = 0; i < 3; i++)
                yield return null;
            Vector3 start = motor.transform.position;
            float elapsed = 0f;
            while (elapsed < 0.5f)
            {
                yield return null;
                elapsed += Time.deltaTime;
            }
            Assert.IsFalse(motor.IsSprinting, "무거워서 질주 안 됨");
            Vector3 d = motor.transform.position - start;
            d.y = 0f;
            keyboard.Release(Key.W, Key.LeftShift);
            Assert.AreEqual(expectedWalk, d.magnitude / elapsed, expectedWalk * 0.05f, "실제 이동 속도");
        }

        // Primary1에서 무기를 빼면 WeaponController 비무장, 다시 장착하면 무장 (EquipmentChanged → Weapons)
        [UnityTest]
        public IEnumerator UnequipPrimary_DisarmsWeaponController_ReequipArms()
        {
            IInventoryService inventory = scene.Inventory;
            var rifle = (WeaponItem)inventory.GetEquipped(EquipSlot.Primary1);

            scene.LootBox("LootBox_C").Interact(scene.InventoryBehaviour);
            ILootView loot = inventory.ActiveLoot;
            yield return M2Scene.WaitUntil(() => loot.SearchComplete, 3f);
            foreach (ItemInstance part in loot.Items.Select(p => p.Item).ToArray())
                Assert.IsTrue(inventory.TryQuickMove(part));

            Assert.IsTrue(inventory.TryMove(rifle, loot.Ref, Vector2Int.zero, false), "슬롯 → 루트 그리드 = 장착 해제");
            Assert.IsNull(inventory.GetEquipped(EquipSlot.Primary1));
            Assert.IsFalse(scene.Weapon.IsArmed, "비무장");
            int rounds = rifle.LoadedMagazine.Rounds;
            for (int i = 0; i < 5; i++)
                yield return null;
            Assert.AreEqual(rounds, rifle.LoadedMagazine.Rounds);

            Assert.IsTrue(inventory.TryEquip(rifle, EquipSlot.Primary1));
            Assert.IsTrue(scene.Weapon.IsArmed);
            Assert.AreSame(rifle, scene.Weapon.Model.Item);
        }

        // §5.1 일반 재장전: 이전 탄창이 인벤토리로 돌아온다. 빠른 재장전: 바닥 WorldItem이 생기고 F로 다시 줍는다.
        [UnityTest]
        public IEnumerator Reload_Normal_ReturnsMagazine_Fast_DropsWorldItem()
        {
            var weapon = scene.Weapon;
            Magazine first = weapon.Model.Current;
            for (int i = 0; i < 10; i++)
                Assert.IsTrue(first.TakeRound());
            Assert.AreEqual(20, first.Rounds);
            int worldItemsBefore = M2Scene.WorldItems().Length;

            using var drops = new EventLog<ItemDropRequested>(scene.Events);
            Assert.IsTrue(weapon.Model.PressReload());
            yield return M2Scene.WaitUntil(() => !weapon.Model.IsReloading, 5f);
            Magazine second = weapon.Model.Current;
            Assert.AreNotSame(first, second);
            Assert.AreEqual(30, second.Rounds, "가장 많이 든 탄창을 꺼냄");
            Assert.IsTrue(scene.Inventory.IsInStorage(first), "일반 재장전 → 이전 탄창 회수");
            Assert.IsFalse(scene.Inventory.IsInStorage(second));
            Assert.AreEqual(0, drops.Count);
            Assert.AreEqual(worldItemsBefore, M2Scene.WorldItems().Length);

            Assert.IsTrue(weapon.Model.PressReload());
            Assert.IsTrue(weapon.Model.PressReload(), "재장전 중 R 한 번 더 = 빠른 재장전");
            Assert.IsTrue(weapon.Model.IsFastReload);
            yield return M2Scene.WaitUntil(() => !weapon.Model.IsReloading, 5f);
            Assert.AreNotSame(second, weapon.Model.Current);
            Assert.AreEqual(1, drops.Count);
            Assert.AreSame(second, drops.Last.Item);
            Assert.IsFalse(scene.Inventory.IsInStorage(second), "빠른 재장전 → 회수 안 함");
            WorldItem dropped = M2Scene.FindWorldItem(second);
            Assert.IsNotNull(dropped, "버린 탄창이 바닥 WorldItem");
            Assert.AreEqual(30, ((Magazine)dropped.Item).Rounds, "탄 수 유지");

            dropped.Interact(scene.InventoryBehaviour);
            Assert.IsTrue(scene.Inventory.IsInStorage(second), "F 줍기 → 저장 공간");
            yield return null;
            Assert.IsTrue(dropped == null);
        }

        // 씬 언로드 후 이벤트 구독·서비스 등록이 남지 않는다
        [UnityTest]
        public IEnumerator SceneUnload_LeavesNoSubscribersOrServices()
        {
            Scene m2 = SceneManager.GetActiveScene();
            Scene empty = SceneManager.CreateScene("M2LeakCheck");
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync(m2);
            yield return null;

            Assert.IsNotNull(GameManager.Instance);
            EventBus bus = GameManager.Instance.Events;
            MethodInfo count = typeof(EventBus).GetMethod(nameof(EventBus.SubscriberCount));
            var eventTypes = typeof(IGameEvent).Assembly.GetTypes()
                .Where(t => t.IsValueType && typeof(IGameEvent).IsAssignableFrom(t)).ToArray();
            Assert.Greater(eventTypes.Length, 10);
            foreach (Type t in eventTypes)
                Assert.AreEqual(0, (int)count.MakeGenericMethod(t).Invoke(bus, null), $"{t.Name} 구독 누수");

            var services = GameManager.Instance.Services;
            Assert.IsFalse(services.TryGet(out IInventoryService _));
            Assert.IsFalse(services.TryGet(out IMagazineSource _));
            Assert.IsFalse(services.TryGet(out IBodyStatusView _));
        }

        private void CloseScreens()
        {
            scene.Ui.Presenter.Cancel();
            Assert.AreEqual(ScreenKind.None, scene.Ui.Presenter.Screens.Current);
        }
    }
}
#endif
