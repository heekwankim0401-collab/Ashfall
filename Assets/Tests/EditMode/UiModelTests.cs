using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Ashfall.Core;
using Ashfall.Inventory;
using Ashfall.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ashfall.Tests.EditMode
{
    // 설계 §5.4 UI 순수 모델. AC2(드래그·회전), AC3(검색 전 아이템), AC4(RULE-04, RULE-05) 검증.
    public class ScreenStateModelTests
    {
        [Test]
        public void ToggleInventory_OpensThenCloses()
        {
            var screens = new ScreenStateModel();
            Assert.IsTrue(screens.ToggleInventory());
            Assert.AreEqual(ScreenKind.Inventory, screens.Current);
            Assert.IsTrue(screens.ToggleInventory());
            Assert.AreEqual(ScreenKind.None, screens.Current);
        }

        [Test]
        public void ToggleBodyStatus_WhileInventoryOpen_SwitchesScreens()
        {
            var screens = new ScreenStateModel();
            screens.ToggleInventory();
            Assert.IsTrue(screens.ToggleBodyStatus());
            Assert.AreEqual(ScreenKind.BodyStatus, screens.Current);
            Assert.IsTrue(screens.ToggleInventory());
            Assert.AreEqual(ScreenKind.Inventory, screens.Current);
        }

        [Test]
        public void CloseAll_ClosesOpenScreen_AndReportsNoChangeWhenNothingOpen()
        {
            var screens = new ScreenStateModel();
            Assert.IsFalse(screens.CloseAll());
            screens.ToggleBodyStatus();
            Assert.IsTrue(screens.CloseAll());
            Assert.AreEqual(ScreenKind.None, screens.Current);
        }

        [Test]
        public void OpenInventory_WhenAlreadyOpen_ReturnsFalse_AndSwitchesFromBodyStatus()
        {
            var screens = new ScreenStateModel();
            screens.ToggleBodyStatus();
            Assert.IsTrue(screens.OpenInventory());
            Assert.AreEqual(ScreenKind.Inventory, screens.Current);
            Assert.IsFalse(screens.OpenInventory());
            Assert.AreEqual(ScreenKind.Inventory, screens.Current);
        }

        [Test]
        public void Dead_ClosesOpenScreen_AndRefusesToOpen()
        {
            var screens = new ScreenStateModel();
            screens.ToggleInventory();
            screens.IsDead = true;
            Assert.AreEqual(ScreenKind.None, screens.Current);
            Assert.IsFalse(screens.ToggleInventory());
            Assert.IsFalse(screens.ToggleBodyStatus());
            Assert.IsFalse(screens.OpenInventory());
            Assert.AreEqual(ScreenKind.None, screens.Current);
        }

        [Test]
        public void CanOpenFalse_RefusesToOpen()
        {
            var screens = new ScreenStateModel { CanOpen = kind => kind != ScreenKind.BodyStatus };
            Assert.IsFalse(screens.ToggleBodyStatus());
            Assert.AreEqual(ScreenKind.None, screens.Current);
            Assert.IsTrue(screens.ToggleInventory());
        }

        [Test]
        public void Changed_FiresOnlyOnChange_WithNewScreen()
        {
            var screens = new ScreenStateModel();
            var seen = new List<ScreenKind>();
            screens.Changed += seen.Add;

            screens.ToggleInventory();
            screens.OpenInventory();    // 변화 없음
            screens.ToggleBodyStatus();
            screens.CloseAll();
            screens.CloseAll();         // 변화 없음

            CollectionAssert.AreEqual(new[] { ScreenKind.Inventory, ScreenKind.BodyStatus, ScreenKind.None }, seen);
        }

        [Test]
        public void HudVisible_OnlyWhenNoScreenOpenAndAlive()
        {
            var screens = new ScreenStateModel();
            Assert.IsTrue(screens.HudVisible);
            screens.ToggleInventory();
            Assert.IsFalse(screens.HudVisible);
            screens.CloseAll();
            screens.IsDead = true;
            Assert.IsFalse(screens.HudVisible);
        }
    }

    public class MagazineCheckToastModelTests
    {
        [TestCase(MagazineFillLevel.Full, "가득")]
        [TestCase(MagazineFillLevel.Half, "절반")]
        [TestCase(MagazineFillLevel.AlmostEmpty, "거의 없음")]
        [TestCase(MagazineFillLevel.NoMagazine, "탄창 없음")]
        public void TextFor_ReturnsRule04Text(MagazineFillLevel level, string expected)
        {
            Assert.AreEqual(expected, MagazineCheckToastModel.TextFor(level));
        }

        [Test]
        public void TextFor_NeverContainsDigits()
        {
            foreach (MagazineFillLevel level in Enum.GetValues(typeof(MagazineFillLevel)))
                StringAssert.DoesNotMatch("[0-9%]", MagazineCheckToastModel.TextFor(level));
        }

        [Test]
        public void Completed_ShowsTextForConfiguredTime_ThenHides()
        {
            var toast = new MagazineCheckToastModel(TestData.Hud());   // 2.0초
            Assert.IsNull(toast.Text);

            toast.OnCompleted(MagazineFillLevel.Half);
            Assert.AreEqual("절반", toast.Text);
            toast.Tick(1.9f);
            Assert.AreEqual("절반", toast.Text);
            toast.Tick(0.2f);
            Assert.IsNull(toast.Text);
        }

        [Test]
        public void Started_ClearsPreviousResult()
        {
            var toast = new MagazineCheckToastModel(TestData.Hud());
            toast.OnCompleted(MagazineFillLevel.Full);
            toast.OnStarted();
            Assert.IsNull(toast.Text);
        }

        [Test]
        public void NewResult_RestartsDisplayTime()
        {
            var toast = new MagazineCheckToastModel(TestData.Hud());
            toast.OnCompleted(MagazineFillLevel.Full);
            toast.Tick(1.5f);
            toast.OnCompleted(MagazineFillLevel.AlmostEmpty);
            toast.Tick(1.5f);
            Assert.AreEqual("거의 없음", toast.Text);
        }
    }

    public class BodyStatusPresenterTests
    {
        [Test]
        public void Build_ReturnsSevenRowsInBodyPartOrder_WithLabels()
        {
            var rows = BodyStatusPresenter.Build(new FakeBodyStatusView());
            Assert.AreEqual(7, rows.Count);
            CollectionAssert.AreEqual(Enum.GetValues(typeof(BodyPart)).Cast<BodyPart>(), rows.Select(r => r.Part));
            CollectionAssert.AreEqual(new[] { "머리", "흉부", "복부", "왼팔", "오른팔", "왼다리", "오른다리" },
                rows.Select(r => r.Label));
            Assert.IsTrue(rows.All(r => r.TierText == "정상" && r.TierClass == "tier-healthy" && r.StatusTexts.Count == 0));
        }

        [Test]
        public void Build_MapsTierAndStatuses()
        {
            var view = new FakeBodyStatusView();
            view.Tiers[BodyPart.LeftArm] = HealthTier.Destroyed;
            view.Tiers[BodyPart.Thorax] = HealthTier.Critical;
            view.Tiers[BodyPart.RightLeg] = HealthTier.Injured;
            view.Statuses.Add((BodyPart.LeftArm, StatusEffect.Fracture));
            view.Statuses.Add((BodyPart.LeftArm, StatusEffect.Bleed));
            view.Statuses.Add((BodyPart.Thorax, StatusEffect.HeavyBleed));

            var rows = BodyStatusPresenter.Build(view).ToDictionary(r => r.Part);

            Assert.AreEqual("파괴", rows[BodyPart.LeftArm].TierText);
            Assert.AreEqual("tier-destroyed", rows[BodyPart.LeftArm].TierClass);
            CollectionAssert.AreEqual(new[] { "출혈", "골절" }, rows[BodyPart.LeftArm].StatusTexts);
            Assert.AreEqual("중상", rows[BodyPart.Thorax].TierText);
            Assert.AreEqual("tier-critical", rows[BodyPart.Thorax].TierClass);
            CollectionAssert.AreEqual(new[] { "중출혈" }, rows[BodyPart.Thorax].StatusTexts);
            Assert.AreEqual("부상", rows[BodyPart.RightLeg].TierText);
            Assert.AreEqual("tier-injured", rows[BodyPart.RightLeg].TierClass);
        }

        // RULE-05: 어떤 상태에서도 숫자·퍼센트가 없다
        [Test]
        public void Build_NoDigitsOrPercent_InAnyText()
        {
            foreach (HealthTier tier in Enum.GetValues(typeof(HealthTier)))
            {
                var view = new FakeBodyStatusView();
                foreach (BodyPart part in Enum.GetValues(typeof(BodyPart)))
                {
                    view.Tiers[part] = tier;
                    foreach (StatusEffect effect in Enum.GetValues(typeof(StatusEffect)))
                        view.Statuses.Add((part, effect));
                }

                foreach (BodyPartRow row in BodyStatusPresenter.Build(view))
                {
                    foreach (string text in new[] { row.Label, row.TierText }.Concat(row.StatusTexts))
                        StringAssert.DoesNotMatch("[0-9%]", text, $"{row.Part} {tier}");
                }
            }
        }

        // RULE-05를 구조로 강제: IBodyStatusView는 수치를 내주지 않는다
        [Test]
        public void BodyStatusView_ExposesNoNumericMembers()
        {
            var numeric = new HashSet<Type>
            {
                typeof(float), typeof(double), typeof(decimal), typeof(int), typeof(uint),
                typeof(long), typeof(ulong), typeof(short), typeof(ushort), typeof(byte), typeof(sbyte),
            };
            foreach (MethodInfo method in typeof(IBodyStatusView).GetMethods())
                Assert.IsFalse(numeric.Contains(method.ReturnType), $"{method.Name}이 수치를 반환한다");
            foreach (PropertyInfo property in typeof(IBodyStatusView).GetProperties())
                Assert.IsFalse(numeric.Contains(property.PropertyType), $"{property.Name}이 수치다");
        }
    }

    public class InteractionPromptTests
    {
        [Test]
        public void Prompt_FormatsVerbAndTarget()
        {
            Assert.AreEqual("F  열기: 상자", UiText.Prompt(InteractionKind.OpenContainer, "상자", "F"));
            Assert.AreEqual("F  줍기: 붕대", UiText.Prompt(InteractionKind.PickUp, "붕대", "F"));
            Assert.AreEqual("F  줍기", UiText.Prompt(InteractionKind.PickUp, null, "F"));
        }

        [Test]
        public void Model_ShowsAndHides()
        {
            var prompt = new InteractionPromptModel();
            prompt.Apply(new InteractionPromptChanged(true, InteractionKind.OpenContainer, "상자"), "F");
            Assert.AreEqual("F  열기: 상자", prompt.Text);
            prompt.Apply(new InteractionPromptChanged(false, InteractionKind.OpenContainer, "상자"), "F");
            Assert.IsNull(prompt.Text);
        }

        [Test]
        public void ItemLabel_UsesDisplayName_WithoutRoundCount()
        {
            // RULE-04: 탄 수를 붙이지 않는다 — 탄을 쏴도 이름만 그대로
            var magazine = Magazine.Full(TestData.Magazine30());
            magazine.TakeRound();
            Assert.AreEqual(TestData.Magazine30().displayName, UiText.ItemLabel(magazine));
            Assert.AreEqual("전선 뭉치", UiText.ItemLabel(new ItemInstance(TestData.Wires())));
        }
    }

    public class InventoryLayoutTests
    {
        [TestCase(0.7f, 0.7f, 1, 1, 0, 0)]
        [TestCase(1.2f, 0.3f, 1, 1, 1, 0)]
        [TestCase(1.4f, 1.4f, 2, 2, 0, 0)]
        [TestCase(1.6f, 1.6f, 2, 2, 1, 1)]
        [TestCase(0.5f, 1.5f, 1, 3, 0, 0)]
        [TestCase(0.5f, 2.6f, 1, 3, 0, 1)]
        public void TopLeftCell_CentersItemOnPointer(float px, float py, int w, int h, int ex, int ey)
        {
            const float cell = 48f;
            Vector2Int got = InventoryLayout.TopLeftCellForPointer(new Vector2(px, py) * cell, cell, new Vector2Int(w, h));
            Assert.AreEqual(new Vector2Int(ex, ey), got);
        }

        [Test]
        public void CellPx_ReturnsZeroBeforeLayout()
        {
            Assert.AreEqual(0f, InventoryLayout.CellPx(float.NaN, 4));
            Assert.AreEqual(0f, InventoryLayout.CellPx(0f, 4));
            Assert.AreEqual(0f, InventoryLayout.CellPx(192f, 0));
            Assert.AreEqual(48f, InventoryLayout.CellPx(192f, 4), 1e-4f);
            Assert.AreEqual(Vector2Int.zero, InventoryLayout.TopLeftCellForPointer(new Vector2(100f, 100f), 0f, Vector2Int.one));
        }

        [Test]
        public void ExceedsThreshold()
        {
            Assert.IsFalse(InventoryLayout.ExceedsThreshold(Vector2.zero, new Vector2(2f, 2f), 4f));
            Assert.IsTrue(InventoryLayout.ExceedsThreshold(Vector2.zero, new Vector2(4f, 0f), 4f));
        }
    }

    public class InventoryDragModelTests
    {
        private static readonly GridRef Vest = new GridRef(GridKind.TacticalVest);

        private static (FakeInventoryService inventory, InventoryDragModel drag, ItemInstance pipe) Setup(bool rotated = false)
        {
            var inventory = new FakeInventoryService();
            var pipe = new ItemInstance(TestData.Pipe());   // 1×3
            inventory.Locations[pipe] = ItemLocation.InGrid(Vest, new Vector2Int(1, 0), rotated);
            return (inventory, new InventoryDragModel(inventory), pipe);
        }

        [Test]
        public void Begin_StartsWithCurrentPlacementRotation()
        {
            var (_, drag, pipe) = Setup(rotated: true);
            Assert.IsTrue(drag.Begin(pipe));
            Assert.AreSame(pipe, drag.Dragging);
            Assert.IsTrue(drag.Rotated);
            Assert.AreEqual(new Vector2Int(3, 1), drag.Size);
        }

        [Test]
        public void Begin_EquippedItem_IsNotRotated()
        {
            var (inventory, drag, pipe) = Setup();
            inventory.Locations[pipe] = ItemLocation.Equipped(EquipSlot.Primary1);
            drag.Begin(pipe);
            Assert.IsFalse(drag.Rotated);
            Assert.AreEqual(new Vector2Int(1, 3), drag.Size);
        }

        // AC2: 회전 토글 후 DropAt이 rotated=true로 TryMove를 호출
        [Test]
        public void ToggleRotate_ThenDropAt_CallsTryMoveRotated()
        {
            var (inventory, drag, pipe) = Setup();
            drag.Begin(pipe);
            drag.ToggleRotate();

            Assert.IsTrue(drag.DropAt(Vest, new Vector2Int(0, 1)));

            Assert.IsTrue(inventory.LastMove.HasValue);
            Assert.AreSame(pipe, inventory.LastMove.Value.Item);
            Assert.AreEqual(Vest, inventory.LastMove.Value.Grid);
            Assert.AreEqual(new Vector2Int(0, 1), inventory.LastMove.Value.Cell);
            Assert.IsTrue(inventory.LastMove.Value.Rotated);
            Assert.IsFalse(drag.IsDragging);
        }

        // AC2: 실패 시 원위치 (서비스가 바꾸지 않음 + 드래그 종료)
        [Test]
        public void DropAt_Failure_EndsDrag_AndItemStaysInPlace()
        {
            var (inventory, drag, pipe) = Setup();
            inventory.MoveResult = false;
            ItemLocation before = inventory.Locations[pipe];

            drag.Begin(pipe);
            drag.ToggleRotate();
            Assert.IsFalse(drag.DropAt(Vest, new Vector2Int(3, 3)));

            Assert.IsFalse(drag.IsDragging);
            Assert.IsFalse(drag.Rotated);
            ItemLocation after = inventory.Locations[pipe];
            Assert.AreEqual(before.Grid, after.Grid);
            Assert.AreEqual(before.Cell, after.Cell);
            Assert.AreEqual(before.Rotated, after.Rotated);
        }

        [Test]
        public void CanDropAt_PassesCurrentRotation()
        {
            var (inventory, drag, pipe) = Setup();
            drag.Begin(pipe);
            drag.ToggleRotate();
            inventory.CanPlaceResult = false;

            Assert.IsFalse(drag.CanDropAt(Vest, new Vector2Int(2, 0)));
            Assert.IsTrue(inventory.LastCanPlace.Value.Rotated);
            Assert.AreEqual(new Vector2Int(2, 0), inventory.LastCanPlace.Value.Cell);
            Assert.IsTrue(drag.IsDragging, "판정만으로 드래그가 끝나면 안 된다");
        }

        [Test]
        public void DropOnSlot_CallsTryEquip()
        {
            var (inventory, drag, pipe) = Setup();
            drag.Begin(pipe);
            drag.Drop(DropTarget.OnSlot(EquipSlot.Backpack));
            Assert.AreEqual(EquipSlot.Backpack, inventory.LastEquip.Value.Slot);
            Assert.AreSame(pipe, inventory.LastEquip.Value.Item);
        }

        [Test]
        public void DropOutside_CallsTryDrop()
        {
            var (inventory, drag, pipe) = Setup();
            drag.Begin(pipe);
            Assert.IsTrue(drag.Drop(DropTarget.Outside()));
            Assert.AreSame(pipe, inventory.LastDrop);
        }

        [Test]
        public void DropOnPanel_CancelsWithoutCommands()
        {
            var (inventory, drag, pipe) = Setup();
            drag.Begin(pipe);
            Assert.IsFalse(drag.Drop(DropTarget.Panel()));
            Assert.IsFalse(drag.IsDragging);
            CollectionAssert.IsEmpty(inventory.Calls);
        }

        [Test]
        public void Drop_WithoutDrag_DoesNothing()
        {
            var (inventory, drag, _) = Setup();
            Assert.IsFalse(drag.DropAt(Vest, Vector2Int.zero));
            Assert.IsFalse(drag.DropOutside());
            Assert.IsFalse(drag.DropOnSlot(EquipSlot.Helmet));
            CollectionAssert.IsEmpty(inventory.Calls);
        }

        // AC3: 검색 전 루트 아이템은 집을 수 없다
        [Test]
        public void Begin_UnrevealedLootItem_IsRefused_RevealedIsAllowed()
        {
            var (inventory, drag, _) = Setup();
            var loot = new FakeLootView(new Vector2Int(4, 4));
            var hidden = new ItemInstance(TestData.Wires());
            var shown = new ItemInstance(TestData.Wires());
            loot.Placed.Add(new PlacedItem(hidden, Vector2Int.zero, false));
            loot.Placed.Add(new PlacedItem(shown, new Vector2Int(1, 0), false));
            loot.Revealed.Add(shown);
            inventory.Loot = loot;

            Assert.IsFalse(drag.Begin(hidden));
            Assert.IsFalse(drag.IsDragging);
            Assert.IsTrue(drag.Begin(shown));
        }

        [Test]
        public void ToggleRotate_WithoutDrag_DoesNothing()
        {
            var (_, drag, _) = Setup();
            drag.ToggleRotate();
            Assert.IsFalse(drag.Rotated);
        }

        // 리뷰 수정: 사용 중(잠긴) 의약품은 드래그 시작 거부
        [Test]
        public void Begin_LockedItem_IsRefused()
        {
            var (inventory, drag, pipe) = Setup();
            inventory.Locked.Add(pipe);
            Assert.IsFalse(InventoryDragModel.CanPick(inventory, pipe));
            Assert.IsFalse(drag.Begin(pipe));
            Assert.IsFalse(drag.IsDragging);
        }
    }

    public class GameUIPresenterTests
    {
        private EventBus bus;
        private ServiceRegistry services;
        private FakeInventoryService inventory;
        private FakeBodyStatusView body;
        private GameUIPresenter presenter;
        private List<ScreenKind> published;

        [SetUp]
        public void SetUp()
        {
            bus = new EventBus();
            services = new ServiceRegistry();
            inventory = new FakeInventoryService();
            body = new FakeBodyStatusView();
            services.Register<IInventoryService>(inventory);
            services.Register<IBodyStatusView>(body);
            presenter = new GameUIPresenter(bus, services, TestData.Hud()) { InteractKeyLabel = "F" };
            presenter.Enable();
            published = new List<ScreenKind>();
            bus.Subscribe<ScreenStateChanged>(e => published.Add(e.Screen));
        }

        [Test]
        public void ToggleScreens_PublishScreenStateChanged()
        {
            presenter.ToggleInventory();
            presenter.ToggleBodyStatus();
            presenter.Cancel();
            CollectionAssert.AreEqual(new[] { ScreenKind.Inventory, ScreenKind.BodyStatus, ScreenKind.None }, published);
            Assert.AreSame(inventory, presenter.InventoryService);
            Assert.AreSame(body, presenter.BodyView);
            Assert.IsNotNull(presenter.Drag);
        }

        [Test]
        public void MissingService_ScreenDoesNotOpen()
        {
            services.Unregister<IInventoryService>(inventory);
            LogAssert.Expect(LogType.Warning, new Regex("Inventory"));

            Assert.IsFalse(presenter.ToggleInventory());
            Assert.AreEqual(ScreenKind.None, presenter.Screens.Current);
            CollectionAssert.IsEmpty(published);
        }

        [Test]
        public void LootSessionOpened_OpensInventory()
        {
            bus.Publish(new LootSessionChanged(true));
            Assert.AreEqual(ScreenKind.Inventory, presenter.Screens.Current);
            CollectionAssert.AreEqual(new[] { ScreenKind.Inventory }, published);
        }

        [Test]
        public void LeavingInventory_ClosesActiveLoot()
        {
            inventory.Loot = new FakeLootView(new Vector2Int(2, 2));
            bus.Publish(new LootSessionChanged(true));
            presenter.ToggleBodyStatus();
            Assert.AreEqual(1, inventory.CloseLootCalls);
        }

        [Test]
        public void ClosingInventoryWithoutLoot_DoesNotCallCloseLoot()
        {
            presenter.ToggleInventory();
            presenter.ToggleInventory();
            Assert.AreEqual(0, inventory.CloseLootCalls);
        }

        [Test]
        public void PlayerDied_ClosesScreen_ClearsHud_AndBlocksReopen()
        {
            presenter.ToggleInventory();
            bus.Publish(new InteractionPromptChanged(true, InteractionKind.PickUp, "붕대"));
            bus.Publish(new MagazineCheckCompleted(MagazineFillLevel.Full));

            bus.Publish(new PlayerDied(BodyPart.Head));

            Assert.AreEqual(ScreenKind.None, presenter.Screens.Current);
            Assert.AreEqual(ScreenKind.None, published.Last());
            Assert.IsNull(presenter.Prompt.Text);
            Assert.IsNull(presenter.Toast.Text);
            Assert.IsFalse(presenter.Screens.HudVisible);
            Assert.IsFalse(presenter.ToggleInventory());
            Assert.IsFalse(presenter.ToggleBodyStatus());
        }

        [Test]
        public void MagazineCheckEvents_DriveToast()
        {
            bus.Publish(new MagazineCheckCompleted(MagazineFillLevel.AlmostEmpty));
            Assert.AreEqual("거의 없음", presenter.Toast.Text);
            bus.Publish(new MagazineCheckStarted());
            Assert.IsNull(presenter.Toast.Text);
            bus.Publish(new MagazineCheckCompleted(MagazineFillLevel.Full));
            presenter.Tick(2.1f);
            Assert.IsNull(presenter.Toast.Text);
        }

        [Test]
        public void InteractionPrompt_UsesKeyLabel()
        {
            bus.Publish(new InteractionPromptChanged(true, InteractionKind.OpenContainer, "상자"));
            Assert.AreEqual("F  열기: 상자", presenter.Prompt.Text);
            bus.Publish(new InteractionPromptChanged(false, InteractionKind.OpenContainer, null));
            Assert.IsNull(presenter.Prompt.Text);
        }

        [Test]
        public void InventoryChanged_MarksDirtyOnlyWhileInventoryOpen()
        {
            int dirty = 0;
            presenter.InventoryDirty += () => dirty++;

            bus.Publish(new InventoryChanged());
            Assert.AreEqual(0, dirty);
            presenter.ToggleInventory();
            bus.Publish(new InventoryChanged());
            Assert.AreEqual(1, dirty);
        }

        [Test]
        public void BodyStatusChanged_MarksDirtyOnlyWhileBodyStatusOpen()
        {
            int dirty = 0;
            presenter.BodyStatusDirty += () => dirty++;

            bus.Publish(new PlayerBodyStatusChanged());
            Assert.AreEqual(0, dirty);
            presenter.ToggleBodyStatus();
            bus.Publish(new PlayerBodyStatusChanged());
            Assert.AreEqual(1, dirty);
        }

        [Test]
        public void Cancel_WhileDragging_CancelsDragOnly()
        {
            var wires = new ItemInstance(TestData.Wires());
            presenter.ToggleInventory();
            presenter.Drag.Begin(wires);

            Assert.IsTrue(presenter.Cancel());
            Assert.IsFalse(presenter.Drag.IsDragging);
            Assert.AreEqual(ScreenKind.Inventory, presenter.Screens.Current);
            Assert.IsTrue(presenter.Cancel());
            Assert.AreEqual(ScreenKind.None, presenter.Screens.Current);
        }

        [Test]
        public void RotateDragged_OnlyWhileDraggingInInventory()
        {
            Assert.IsFalse(presenter.RotateDragged());
            presenter.ToggleInventory();
            Assert.IsFalse(presenter.RotateDragged());
            presenter.Drag.Begin(new ItemInstance(TestData.Pipe()));
            Assert.IsTrue(presenter.RotateDragged());
            Assert.IsTrue(presenter.Drag.Rotated);
        }

        [Test]
        public void AssignQuickSlot_OnlyWhileInventoryOpen()
        {
            var bandage = new MedicalItem(TestData.Bandage());
            Assert.IsFalse(presenter.AssignQuickSlot(0, bandage));
            presenter.ToggleInventory();
            Assert.IsFalse(presenter.AssignQuickSlot(0, null));
            Assert.IsTrue(presenter.AssignQuickSlot(2, bandage));
            Assert.AreEqual(2, inventory.LastAssign.Value.Index);
            Assert.AreSame(bandage, inventory.LastAssign.Value.Item);
        }

        // 이벤트 해지 누락 없음 + 열린 화면을 닫아 조작 차단을 푼다
        [Test]
        public void Disable_UnsubscribesAll_AndClosesOpenScreen()
        {
            presenter.ToggleInventory();
            presenter.Disable();

            Assert.AreEqual(ScreenKind.None, published.Last());
            Assert.AreEqual(0, bus.SubscriberCount<InventoryChanged>());
            Assert.AreEqual(0, bus.SubscriberCount<LootSessionChanged>());
            Assert.AreEqual(0, bus.SubscriberCount<PlayerBodyStatusChanged>());
            Assert.AreEqual(0, bus.SubscriberCount<MagazineCheckStarted>());
            Assert.AreEqual(0, bus.SubscriberCount<MagazineCheckCompleted>());
            Assert.AreEqual(0, bus.SubscriberCount<InteractionPromptChanged>());
            Assert.AreEqual(0, bus.SubscriberCount<PlayerDied>());
        }

        [Test]
        public void Enable_Twice_SubscribesOnce()
        {
            presenter.Enable();
            Assert.AreEqual(1, bus.SubscriberCount<InventoryChanged>());
        }

        // 리뷰 수정: 드래그 중 아이템이 다른 경로로 인벤토리를 떠나면 InventoryChanged에서 드래그 취소
        [Test]
        public void InventoryChanged_ItemGone_CancelsDrag_ItemStillThere_Keeps()
        {
            var pipe = new ItemInstance(TestData.Pipe());
            inventory.Locations[pipe] = ItemLocation.InGrid(new GridRef(GridKind.Pocket), Vector2Int.zero, false);
            presenter.ToggleInventory();
            Assert.IsTrue(presenter.Drag.Begin(pipe));

            bus.Publish(new InventoryChanged());
            Assert.IsTrue(presenter.Drag.IsDragging, "아직 인벤토리에 있으면 유지");

            inventory.Locations.Remove(pipe);   // 재장전·사용 완료 등으로 빠짐
            bus.Publish(new InventoryChanged());
            Assert.IsFalse(presenter.Drag.IsDragging);
            Assert.IsFalse(presenter.Drag.DropAt(new GridRef(GridKind.Pocket), Vector2Int.zero));
            CollectionAssert.DoesNotContain(inventory.Calls, nameof(IInventoryService.TryMove));
        }
    }

    // 리뷰 수정: 실제 PlayerInventory + 드래그 모델로 복제·부활 시나리오
    public class InventoryDragIntegrationTests
    {
        // 시나리오 A: 드래그 중 재장전 완료(TakeFullest)로 탄창이 빠짐 → 드래그 취소, 놓아도 다시 배치되지 않음
        [Test]
        public void MagazineTakenWhileDragging_DragCancelled_NotDuplicated()
        {
            var inventory = new PlayerInventory(TestData.Inventory());
            Assert.IsTrue(inventory.TryEquipNew(new ItemInstance(TestData.Vest()), EquipSlot.TacticalVest));
            var magazine = Magazine.Full(TestData.Magazine30());
            Assert.IsTrue(inventory.TryAutoStore(magazine));
            var drag = new InventoryDragModel(inventory);
            Assert.IsTrue(drag.Begin(magazine));

            Assert.AreSame(magazine, inventory.TakeFullest(AmmoType.Rifle556));
            // 취소가 늦어 그대로 놓아도 서비스가 거부한다
            Assert.IsFalse(drag.DropAt(new GridRef(GridKind.TacticalVest), new Vector2Int(3, 0)));
            Assert.IsFalse(inventory.IsInStorage(magazine), "장전된 동시에 조끼 안에 있으면 안 된다");

            // InventoryChanged 경로: 다시 집어도 CancelIfGone이 취소한다
            Assert.IsTrue(drag.Begin(magazine));
            Assert.IsTrue(drag.CancelIfGone());
            Assert.IsFalse(drag.IsDragging);
        }

        // 시나리오 B: 사용 중(잠긴) 의약품은 드래그를 시작할 수 없다
        [Test]
        public void LockedMedical_DragRefused()
        {
            var inventory = new PlayerInventory(TestData.Inventory());
            var bandage = new MedicalItem(TestData.Bandage());
            inventory.TryAutoStore(bandage);
            inventory.SetItemLocked(bandage, true);
            var drag = new InventoryDragModel(inventory);

            Assert.IsFalse(InventoryDragModel.CanPick(inventory, bandage));
            Assert.IsFalse(drag.Begin(bandage));
            inventory.SetItemLocked(bandage, false);
            Assert.IsTrue(drag.Begin(bandage));
        }

        [Test]
        public void ConsumedMedicalWhileDragging_CancelIfGone()
        {
            var inventory = new PlayerInventory(TestData.Inventory());
            var bandage = new MedicalItem(TestData.Bandage());   // uses 1
            inventory.TryAutoStore(bandage);
            var drag = new InventoryDragModel(inventory);
            Assert.IsTrue(drag.Begin(bandage));
            Assert.IsFalse(drag.CancelIfGone());

            inventory.ConsumeMedicalUse(bandage);   // 사용 완료 → 제거
            Assert.IsTrue(drag.CancelIfGone());
            Assert.IsFalse(drag.IsDragging);
            Assert.IsFalse(inventory.TryMove(bandage, new GridRef(GridKind.Pocket, 1), Vector2Int.zero, false));
            Assert.IsFalse(inventory.IsInStorage(bandage), "UsesLeft 0 붕대 부활 없음");
        }
    }
}
