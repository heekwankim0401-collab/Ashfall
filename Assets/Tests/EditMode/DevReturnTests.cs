using System.Collections.Generic;
using Ashfall.Core;
using Ashfall.Inventory;
using Ashfall.Raid;
using Ashfall.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ashfall.Tests.EditMode
{
    // DEV-RETURN — 개발 빌드 전용 귀환 (DevFeatures, 2026-10-28 사용자 결정).
    // 판정(릴리스 = 없음), 세션 DevReturn = 탈출 성공과 같은 결과(RULE-01 반출), 프레젠터 확인 단계, 뷰 버튼 유무.
    public class DevReturnTests
    {
        private static readonly Vector3 Ext01Pos = new Vector3(0f, 0f, -470f);

        private RaidConfigSO config;
        private int endedCount;

        [SetUp]
        public void SetUp()
        {
            config = M4TestData.RaidConfig();
            endedCount = 0;
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(config);

        private RaidSession Session()
        {
            var models = new List<ExtractionModel>
            {
                new ExtractionModel(M4TestData.Find(config, M4TestData.Ext01), Ext01Pos, true),
            };
            var session = new RaidSession(config, models);
            session.Ended += () => endedCount++;
            return session;
        }

        // ── 판정 ──

        [TestCase(false, false, false)]   // 릴리스 빌드
        [TestCase(true, false, true)]     // 개발 빌드 플레이어
        [TestCase(true, true, true)]      // 에디터 Play (isDebugBuild 늘 true)
        [TestCase(false, true, true)]     // 에디터 (안전)
        public void ShowDevReturn_OnlyEditorOrDevelopmentBuild(bool isDebugBuild, bool isEditor, bool expected)
        {
            Assert.AreEqual(expected, DevFeatures.ShowDevReturn(isDebugBuild, isEditor));
        }

        [Test]
        public void DevReturnId_DoesNotLookLikeRealExtraction()
        {
            StringAssert.DoesNotStartWith("EXT-", DevFeatures.DevReturnExtractionId);
            foreach (ExtractionPointConfig e in config.extractions)
                Assert.AreNotEqual(e.id, DevFeatures.DevReturnExtractionId);
        }

        // ── 세션 ──

        [Test]
        public void Session_DevReturn_EndsAsExtracted_WithoutExtractionPoint()
        {
            RaidSession session = Session();
            session.Tick(1f, Vector3.zero, null);
            session.DevReturn();

            Assert.IsTrue(session.IsOver);
            Assert.AreEqual(RaidOutcome.Extracted, session.Outcome);
            Assert.IsTrue(session.IsDevReturn);
            Assert.IsNull(session.CompletedExtraction);
            Assert.IsFalse(session.HasDeathCause);
            Assert.AreEqual(1, endedCount);
        }

        [Test]
        public void Session_DevReturn_AfterEnd_Ignored()
        {
            RaidSession session = Session();
            session.Abandon();
            session.DevReturn();
            Assert.AreEqual(RaidOutcome.Abandoned, session.Outcome, "이미 끝난 레이드는 바뀌지 않는다");
            Assert.IsFalse(session.IsDevReturn);
            Assert.AreEqual(1, endedCount);

            RaidSession dead = Session();
            dead.EndByDeath(BodyPart.Head);
            dead.DevReturn();
            Assert.AreEqual(RaidOutcome.Killed, dead.Outcome, "사망 후 귀환으로 살아나지 않는다 (RULE-01)");
            Assert.IsFalse(dead.IsDevReturn);
        }

        [Test]
        public void Session_AfterDevReturn_OtherEndsIgnored()
        {
            RaidSession session = Session();
            session.DevReturn();
            session.EndByDeath(BodyPart.Thorax);
            session.Abandon();
            session.AdvanceTime(5000f);
            session.Tick(0.1f, Vector3.zero, null);
            Assert.AreEqual(RaidOutcome.Extracted, session.Outcome);
            Assert.IsFalse(session.HasDeathCause);
            Assert.AreEqual(1, endedCount);
        }

        [Test]
        public void Session_RealExtraction_IsNotDevReturn()
        {
            RaidSession session = Session();
            for (int i = 0; i < 101; i++)
                session.Tick(0.1f, Ext01Pos, null);
            Assert.AreEqual(RaidOutcome.Extracted, session.Outcome);
            Assert.IsFalse(session.IsDevReturn);
        }

        // ── 결과 (RULE-01 판정 한 곳: RaidResultBuilder) ──

        [Test]
        public void Result_DevReturn_CarriesOutEverything_LikeExtraction()
        {
            var inventory = new PlayerInventory(TestData.Inventory());
            var rifle = new WeaponItem(TestData.Rifle(), Magazine.Full(TestData.Magazine30()));
            var vest = new ItemInstance(TestData.Vest());
            ItemInstance bandage = ItemFactory.Create(TestData.Bandage());
            Assert.IsTrue(inventory.TryEquipNew(rifle, EquipSlot.Primary1));
            Assert.IsTrue(inventory.TryEquipNew(vest, EquipSlot.TacticalVest));
            Assert.IsTrue(inventory.TryAutoStore(bandage));
            var broughtIn = new List<ItemDefinitionSO> { rifle.Definition, vest.Definition, bandage.Definition };
            var junk = new ItemInstance(TestData.Wires());
            Assert.IsTrue(inventory.TryAutoStore(junk), "레이드 중 획득");

            RaidSession session = Session();
            session.RecordKill();
            session.Tick(12f, Vector3.zero, inventory);
            session.DevReturn();
            Loadout end = Loadout.Capture(inventory);
            RaidResult result = RaidResultBuilder.Build(session, broughtIn, end);

            Assert.IsTrue(result.Survived);
            Assert.AreEqual(RaidOutcome.Extracted, result.Outcome);
            Assert.AreEqual(DevFeatures.DevReturnExtractionId, result.ExtractionId);
            Assert.AreEqual(DevFeatures.DevReturnLabel, result.ExtractionName);
            Assert.AreSame(end, result.CarriedOutLoadout, "반출 Loadout(위치 그대로) → 은신처 장비·세이브 (ProfileService.ApplyRaidResult)");
            CollectionAssert.AreEquivalent(new ItemInstance[] { rifle, vest, bandage, junk }, result.CarriedOut);
            Assert.IsEmpty(result.Lost);
            Assert.AreEqual(1, result.EnemiesKilled);
            Assert.AreEqual(12f, result.DurationSec, 0.01f);
            Assert.AreEqual("탈출 성공 — 귀환(개발)", RaidResultViewModel.TitleFor(result));
        }

        [Test]
        public void Result_RealExtraction_Unchanged()
        {
            RaidSession session = Session();
            for (int i = 0; i < 101; i++)
                session.Tick(0.1f, Ext01Pos, null);
            RaidResult result = RaidResultBuilder.Build(session, null, Loadout.Empty);
            Assert.AreEqual(M4TestData.Ext01, result.ExtractionId);
            Assert.AreEqual("남쪽 도로", result.ExtractionName);
        }

        // ── 프레젠터 ──

        private sealed class PresenterRig
        {
            public readonly EventBus Bus = new EventBus();
            public readonly ServiceRegistry Services = new ServiceRegistry();
            public readonly FlowRecorder Rec = new FlowRecorder();
            public readonly GameUIPresenter Presenter;
            public int DevReturns;
            public int Abandons;

            public PresenterRig(bool devReturnEnabled, bool inRaid)
            {
                Services.Register<IInventoryService>(new FakeInventoryService());
                Services.Register<IBodyStatusView>(new FakeBodyStatusView());
                if (inRaid)
                    Services.Register<IRaidInfo>(new UiFakeRaidInfo { RemainingSec = 900f });
                Presenter = new GameUIPresenter(Bus, Services, TestData.Hud(), Rec.Create(), devReturnEnabled);
                Presenter.Enable();
                Bus.Subscribe<DevReturnRequested>(_ => DevReturns++);
                Bus.Subscribe<RaidAbandonRequested>(_ => Abandons++);
            }
        }

        [Test]
        public void Presenter_ReleaseBuild_NoDevReturn()
        {
            var rig = new PresenterRig(devReturnEnabled: false, inRaid: true);
            rig.Presenter.Cancel();
            Assert.IsFalse(rig.Presenter.DevReturnEnabled);
            Assert.IsFalse(rig.Presenter.CanDevReturn);
            Assert.IsFalse(rig.Presenter.RequestDevReturn());
            Assert.IsFalse(rig.Presenter.ConfirmDevReturn());
            Assert.AreEqual(0, rig.DevReturns);
            Assert.IsTrue(rig.Presenter.CanAbandon, "포기는 그대로");
        }

        [Test]
        public void Presenter_WithoutRaid_NoDevReturn()
        {
            var rig = new PresenterRig(devReturnEnabled: true, inRaid: false);
            rig.Presenter.Cancel();
            Assert.IsFalse(rig.Presenter.CanDevReturn, "확인 씬(레이드 정보 없음)에서는 [계속]만");
            Assert.IsFalse(rig.Presenter.RequestDevReturn());
            Assert.AreEqual(0, rig.DevReturns);
        }

        [Test]
        public void Presenter_DevReturn_NeedsConfirm_ThenPublishesOnce()
        {
            var rig = new PresenterRig(devReturnEnabled: true, inRaid: true);
            GameUIPresenter p = rig.Presenter;
            int dirty = 0;
            p.PauseDirty += () => dirty++;

            Assert.IsFalse(p.RequestDevReturn(), "일시정지 밖에서는 안 됨");
            p.Cancel();
            Assert.IsTrue(p.CanDevReturn);
            Assert.IsFalse(p.ConfirmDevReturn(), "확인 단계 전에는 발행 안 함");
            Assert.IsTrue(p.RequestDevReturn());
            Assert.IsTrue(p.IsConfirmingDevReturn);
            Assert.IsFalse(p.IsConfirmingAbandon);
            Assert.IsFalse(p.RequestAbandon(), "귀환 확인 중에는 포기 확인으로 바뀌지 않는다");
            Assert.IsFalse(p.ConfirmAbandon(), "귀환 확인 중 [포기] 확인 경로로는 끝나지 않는다");
            Assert.AreEqual(0, rig.DevReturns);
            Assert.AreEqual(0, rig.Abandons);

            Assert.IsTrue(p.ConfirmDevReturn());
            Assert.AreEqual(1, rig.DevReturns);
            Assert.AreEqual(0, rig.Abandons);
            Assert.IsFalse(p.IsConfirmingDevReturn);
            Assert.IsFalse(p.CanDevReturn, "한 번만");
            Assert.IsFalse(p.CanAbandon, "귀환을 요청했으면 포기도 숨긴다");
            Assert.IsFalse(p.RequestDevReturn());
            Assert.IsFalse(p.ConfirmDevReturn());
            Assert.AreEqual(1, rig.DevReturns);
            Assert.AreEqual(2, dirty);

            // 새 레이드면 다시 가능
            rig.Bus.Publish(new RaidStarted("SPAWN", 1500f));
            Assert.IsTrue(p.CanDevReturn);
        }

        [Test]
        public void Presenter_EscOrCancel_WhileConfirmingDevReturn_CancelsConfirmOnly()
        {
            var rig = new PresenterRig(devReturnEnabled: true, inRaid: true);
            GameUIPresenter p = rig.Presenter;
            p.Cancel();
            p.RequestDevReturn();
            Assert.IsTrue(p.Cancel(), "Esc");
            Assert.IsFalse(p.IsConfirmingDevReturn);
            Assert.AreEqual(ScreenKind.Pause, p.Screens.Current);

            p.RequestDevReturn();
            Assert.IsTrue(p.CancelAbandon(), "[취소] 버튼");
            Assert.IsFalse(p.IsConfirmingDevReturn);
            Assert.AreEqual(ScreenKind.Pause, p.Screens.Current);

            p.RequestDevReturn();
            Assert.IsTrue(p.Resume());
            Assert.IsFalse(p.IsConfirmingDevReturn, "일시정지를 닫으면 확인 단계도 끝");
            Assert.AreEqual(0, rig.DevReturns);
        }

        // ── 뷰 (GameUI.uxml) ──

        private static VisualElement CloneGameUi()
        {
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/Art/UI/GameUI.uxml");
            Assert.IsNotNull(asset);
            return asset.CloneTree();
        }

        [Test]
        public void View_ReleaseBuild_HasNoDevReturnButton()
        {
            VisualElement root = CloneGameUi();
            Assert.IsNull(root.Q(PauseScreenView.DevReturnName), "UXML에는 귀환 버튼이 없다");
            var view = new PauseScreenView(root, () => { }, () => { }, () => { }, () => { });
            Assert.IsFalse(view.HasDevReturnButton);
            Assert.IsNull(root.Q(PauseScreenView.DevReturnName), "릴리스 빌드(콜백 없음) = 버튼 없음");
            view.Render(false, true);
            Assert.AreEqual(UiText.AbandonConfirm, root.Q<Label>(PauseScreenView.ConfirmTextName).text);
            view.Dispose();
        }

        [Test]
        public void View_DevBuild_AddsButton_AndSwapsConfirmText()
        {
            VisualElement root = CloneGameUi();
            int clicks = 0;
            var view = new PauseScreenView(root, () => { }, () => { }, () => { }, () => { }, () => clicks++);
            Assert.IsTrue(view.HasDevReturnButton);
            var button = root.Q<Button>(PauseScreenView.DevReturnName);
            Assert.IsNotNull(button);
            Assert.AreSame(root.Q(PauseScreenView.MainName), button.parent, "pause-main 안");
            Assert.AreEqual("귀환(개발)", button.text);

            view.Render(confirming: false, canAbandon: true, confirmingDevReturn: false, canDevReturn: true);
            Assert.AreEqual(DisplayStyle.Flex, button.style.display.value);
            view.Render(confirming: false, canAbandon: false, confirmingDevReturn: false, canDevReturn: false);
            Assert.AreEqual(DisplayStyle.None, button.style.display.value, "레이드 밖·요청 후에는 숨김");

            view.Render(confirming: false, canAbandon: true, confirmingDevReturn: true, canDevReturn: true);
            Assert.AreEqual(DisplayStyle.Flex, root.Q(PauseScreenView.ConfirmName).style.display.value);
            Assert.AreEqual(DisplayStyle.None, root.Q(PauseScreenView.MainName).style.display.value);
            Assert.AreEqual(UiText.DevReturnConfirm, root.Q<Label>(PauseScreenView.ConfirmTextName).text);
            Assert.AreEqual(UiText.DevReturnYes, root.Q<Button>(PauseScreenView.ConfirmYesName).text);

            view.Render(confirming: true, canAbandon: true, confirmingDevReturn: false, canDevReturn: true);
            Assert.AreEqual(UiText.AbandonConfirm, root.Q<Label>(PauseScreenView.ConfirmTextName).text, "포기 확인은 원래 문구");
            Assert.AreEqual(UiText.AbandonYes, root.Q<Button>(PauseScreenView.ConfirmYesName).text);
            view.Dispose();
        }
    }
}
