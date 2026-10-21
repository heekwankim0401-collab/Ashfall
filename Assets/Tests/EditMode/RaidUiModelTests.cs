using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Ashfall.Core;
using Ashfall.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Ashfall.Tests.EditMode
{
    // M4 2b UI 순수 모델 (M4 설계 §4.5, §7 AC2·AC3): 탈출 HUD 문구, 화면 상태(Pause/Map/RaidEnd),
    // 지도 모델(RULE-06), 결과 뷰모델(RULE-01 목록, 정보 희소성), 프레젠터의 일시정지·포기·종료 흐름.

    // IRaidInfo 가짜 (지도 화면). 내 위치 멤버가 없다는 것은 인터페이스가 보장한다.
    internal sealed class UiFakeRaidInfo : IRaidInfo
    {
        public float ElapsedSec { get; set; }
        public float RemainingSec { get; set; } = 1500f;
        public Rect MapAreaXZ { get; set; } = new Rect(-500f, -500f, 1000f, 1000f);
        public readonly List<MapMarker> Pois = new List<MapMarker>();
        public readonly List<MapMarker> Extractions = new List<MapMarker>();
        public IReadOnlyList<MapMarker> PointsOfInterest => Pois;
        public IReadOnlyList<MapMarker> ActiveExtractions => Extractions;
    }

    public class InteractionVerbM4Tests
    {
        [Test]
        public void Activate_UsesActivateVerb_NotPickUp()
        {
            Assert.AreEqual("작동", UiText.InteractionVerb(InteractionKind.Activate));
            Assert.AreEqual("F  작동: 전원 스위치", UiText.Prompt(InteractionKind.Activate, "전원 스위치", "F"));
            Assert.AreEqual("열기", UiText.InteractionVerb(InteractionKind.OpenContainer));
            Assert.AreEqual("줍기", UiText.InteractionVerb(InteractionKind.PickUp));
        }
    }

    public class ExtractionHudModelTests
    {
        [Test]
        public void InZoneCountingDown_ShowsSecondsOnly()
        {
            var hud = new ExtractionHudModel();
            hud.Apply(new ExtractionStatusChanged(true, "남쪽 도로", ExtractionBlock.None, 7));
            Assert.AreEqual("탈출까지 7", hud.Text);
            hud.Apply(new ExtractionStatusChanged(true, "남쪽 도로", ExtractionBlock.None, 6));
            Assert.AreEqual("탈출까지 6", hud.Text);
        }

        [TestCase(ExtractionBlock.NoPower, "전원이 꺼져 있다")]
        [TestCase(ExtractionBlock.TooEarly, "아직 헬기가 오지 않는다")]
        [TestCase(ExtractionBlock.MissingItem, "신호탄이 필요하다")]
        [TestCase(ExtractionBlock.BackpackEquipped, "배낭을 메고는 들어갈 수 없다")]
        public void Blocked_ShowsReason_WithoutCountdown(ExtractionBlock block, string expected)
        {
            var hud = new ExtractionHudModel();
            hud.Apply(new ExtractionStatusChanged(true, "x", block, 15));
            Assert.AreEqual(expected, hud.Text);
            StringAssert.DoesNotContain("15", hud.Text);
        }

        [Test]
        public void LeavingZone_Hides()
        {
            var hud = new ExtractionHudModel();
            hud.Apply(new ExtractionStatusChanged(true, "x", ExtractionBlock.None, 3));
            hud.Apply(new ExtractionStatusChanged(false, null, ExtractionBlock.None, 0));
            Assert.IsNull(hud.Text);
        }

        [Test]
        public void ResetToFullWait_AfterBlock_ShowsCountdownAgain()
        {
            var hud = new ExtractionHudModel();
            hud.Apply(new ExtractionStatusChanged(true, "x", ExtractionBlock.None, 4));
            hud.Apply(new ExtractionStatusChanged(true, "x", ExtractionBlock.BackpackEquipped, 7));
            hud.Apply(new ExtractionStatusChanged(true, "x", ExtractionBlock.None, 7));
            Assert.AreEqual("탈출까지 7", hud.Text);
        }

        [Test]
        public void SameValue_KeepsSameString()
        {
            var hud = new ExtractionHudModel();
            hud.Apply(new ExtractionStatusChanged(true, "x", ExtractionBlock.None, 5));
            string first = hud.Text;
            hud.Apply(new ExtractionStatusChanged(true, "x", ExtractionBlock.None, 5));
            Assert.AreSame(first, hud.Text, "같은 값이면 문자열을 다시 만들지 않는다");
        }
    }

    public class ScreenStateModelM4Tests
    {
        [Test]
        public void OpenPause_OnlyWhenNothingOpen()
        {
            var screens = new ScreenStateModel();
            screens.ToggleInventory();
            Assert.IsFalse(screens.OpenPause());
            screens.CloseAll();
            Assert.IsTrue(screens.OpenPause());
            Assert.AreEqual(ScreenKind.Pause, screens.Current);
            Assert.IsFalse(screens.HudVisible);
        }

        [Test]
        public void Pause_IgnoresTabHM_AndClosesWithCloseAll()
        {
            var screens = new ScreenStateModel();
            screens.OpenPause();
            Assert.IsFalse(screens.ToggleInventory());
            Assert.IsFalse(screens.ToggleBodyStatus());
            Assert.IsFalse(screens.ToggleMap());
            Assert.IsFalse(screens.OpenInventory());
            Assert.AreEqual(ScreenKind.Pause, screens.Current);
            Assert.IsTrue(screens.CloseAll());
            Assert.AreEqual(ScreenKind.None, screens.Current);
        }

        [Test]
        public void Pause_NotWhenDead()
        {
            var screens = new ScreenStateModel { IsDead = true };
            Assert.IsFalse(screens.OpenPause());
        }

        [Test]
        public void ToggleMap_OpensClosesAndSwitches()
        {
            var screens = new ScreenStateModel();
            Assert.IsTrue(screens.ToggleMap());
            Assert.AreEqual(ScreenKind.Map, screens.Current);
            Assert.IsTrue(screens.ToggleInventory());
            Assert.AreEqual(ScreenKind.Inventory, screens.Current);
            Assert.IsTrue(screens.ToggleMap());
            Assert.IsTrue(screens.ToggleMap());
            Assert.AreEqual(ScreenKind.None, screens.Current);
        }

        [Test]
        public void RaidEnd_OpensAfterDeath_AndNeverCloses()
        {
            var screens = new ScreenStateModel();
            screens.IsDead = true;
            Assert.IsTrue(screens.ShowRaidEnd());
            Assert.AreEqual(ScreenKind.RaidEnd, screens.Current);

            Assert.IsFalse(screens.CloseAll(), "Esc로 닫히지 않는다");
            screens.IsDead = true;
            Assert.IsFalse(screens.ToggleInventory());
            Assert.IsFalse(screens.ToggleMap());
            Assert.IsFalse(screens.OpenPause());
            Assert.AreEqual(ScreenKind.RaidEnd, screens.Current);
            Assert.IsFalse(screens.HudVisible);
        }

        [Test]
        public void RaidEnd_ReplacesOpenScreen_AndDeathAfterwardKeepsIt()
        {
            var screens = new ScreenStateModel();
            screens.OpenPause();
            Assert.IsTrue(screens.ShowRaidEnd());
            screens.IsDead = true;
            Assert.AreEqual(ScreenKind.RaidEnd, screens.Current);
        }

        [Test]
        public void Reset_ClosesRaidEnd()
        {
            var screens = new ScreenStateModel();
            screens.ShowRaidEnd();
            Assert.IsTrue(screens.Reset());
            Assert.AreEqual(ScreenKind.None, screens.Current);
            Assert.IsFalse(screens.IsRaidEnded);
        }
    }

    public class MapScreenModelTests
    {
        private static readonly Rect Area = new Rect(-500f, -500f, 1000f, 1000f);

        [Test]
        public void Normalize_NorthIsUp_EastIsRight_Clamped()
        {
            Assert.AreEqual(new Vector2(0.5f, 0.5f), MapScreenModel.Normalize(Area, Vector2.zero));
            Assert.AreEqual(new Vector2(0f, 0f), MapScreenModel.Normalize(Area, new Vector2(-500f, 500f)), "북서 = 왼쪽 위");
            Assert.AreEqual(new Vector2(1f, 1f), MapScreenModel.Normalize(Area, new Vector2(500f, -500f)), "남동 = 오른쪽 아래");
            Assert.AreEqual(new Vector2(1f, 0f), MapScreenModel.Normalize(Area, new Vector2(900f, 900f)), "영역 밖은 가장자리");
            Vector2 ext01 = MapScreenModel.Normalize(Area, new Vector2(0f, -470f));
            Assert.AreEqual(0.97f, ext01.y, 1e-4f);
        }

        [TestCase(1500f, "남은 시간: 약 25분")]
        [TestCase(750f, "남은 시간: 약 13분")]     // 12.5분 → 반올림
        [TestCase(719f, "남은 시간: 약 12분")]
        [TestCase(60f, "남은 시간: 약 1분")]
        [TestCase(59.9f, "남은 시간: 1분 미만")]
        [TestCase(0f, "남은 시간: 없음")]
        [TestCase(-3f, "남은 시간: 없음")]
        public void RemainingText_IsApproximateMinutes_NoSeconds(float remaining, string expected)
        {
            Assert.AreEqual(expected, MapScreenModel.RemainingTextFor(remaining));
        }

        [Test]
        public void Build_PoisAndActiveExtractionsOnly()
        {
            var info = new UiFakeRaidInfo { RemainingSec = 600f };
            info.Pois.Add(new MapMarker("POI-HWY", "붕괴된 고속도로", new Vector2(-60f, -300f)));
            info.Pois.Add(new MapMarker("POI-REF", "방사능 정유소", new Vector2(300f, -60f)));
            info.Extractions.Add(new MapMarker("EXT-01", "남쪽 도로", new Vector2(0f, -470f)));
            info.Extractions.Add(new MapMarker("EXT-04", null, new Vector2(-470f, 200f)));

            var model = new MapScreenModel();
            model.Build(info);

            Assert.AreEqual(4, model.Markers.Count);
            Assert.AreEqual(2, model.Markers.Count(m => m.Kind == MapMarkerKind.PointOfInterest));
            Assert.AreEqual(2, model.Markers.Count(m => m.Kind == MapMarkerKind.Extraction));
            Assert.AreEqual("EXT-04", model.Markers[3].Label, "이름이 없으면 id");
            Assert.AreEqual("남은 시간: 약 10분", model.RemainingText);
        }

        [Test]
        public void RefreshTime_TrueOnlyWhenMinuteChanges()
        {
            var info = new UiFakeRaidInfo { RemainingSec = 600f };
            var model = new MapScreenModel();
            model.Build(info);

            info.RemainingSec = 590f;
            Assert.IsFalse(model.RefreshTime(info), "10분 그대로");
            info.RemainingSec = 560f;
            Assert.IsTrue(model.RefreshTime(info));
            Assert.AreEqual("남은 시간: 약 9분", model.RemainingText);
        }
    }

    public class RaidResultViewModelTests
    {
        [Test]
        public void Titles_ForEachOutcome()
        {
            Assert.AreEqual("탈출 성공 — 남쪽 도로",
                RaidResultViewModel.From(M4TestData.Result(RaidOutcome.Extracted, "EXT-01", "남쪽 도로")).Title);
            Assert.AreEqual("사망 — 머리", RaidResultViewModel.From(M4TestData.Result(RaidOutcome.Killed)).Title);
            Assert.AreEqual("작전 중 실종 (MIA)", RaidResultViewModel.From(M4TestData.Result(RaidOutcome.MissingInAction)).Title);
            Assert.AreEqual("레이드 포기", RaidResultViewModel.From(M4TestData.Result(RaidOutcome.Abandoned)).Title);
        }

        [Test]
        public void Killed_WithoutCause_PlainTitle()
        {
            var result = new RaidResult(RaidOutcome.Killed, null, null, false, BodyPart.Head, 10f, 0, null, null, null);
            Assert.AreEqual("사망", RaidResultViewModel.From(result).Title);
        }

        [Test]
        public void DurationAndKills()
        {
            var vm = RaidResultViewModel.From(M4TestData.Result(RaidOutcome.Killed));   // 600초, 처치 2
            Assert.AreEqual("레이드 시간 10:00", vm.DurationText);
            Assert.AreEqual("처치 2", vm.KillsText);
            Assert.AreEqual("0:05", RaidResultViewModel.FormatDuration(5.9f));
            Assert.AreEqual("25:00", RaidResultViewModel.FormatDuration(1500f));
            Assert.AreEqual("0:00", RaidResultViewModel.FormatDuration(-1f));
        }

        [Test]
        public void Lists_GroupByDefinition_InFirstSeenOrder()
        {
            var mag = TestData.Magazine30();
            var rifle = TestData.Rifle();
            var bandage = TestData.Bandage();
            var vm = RaidResultViewModel.From(M4TestData.Result(RaidOutcome.Killed,
                broughtIn: new ItemDefinitionSO[] { rifle, mag, bandage, mag, mag },
                lost: new ItemDefinitionSO[] { rifle, mag }));

            CollectionAssert.AreEqual(new[]
            {
                RaidResultViewModel.ItemName(rifle),
                RaidResultViewModel.ItemName(mag) + " ×3",
                RaidResultViewModel.ItemName(bandage),
            }, vm.BroughtIn);
            CollectionAssert.AreEqual(new[] { RaidResultViewModel.ItemName(rifle), RaidResultViewModel.ItemName(mag) }, vm.Lost);
        }

        // RULE-01: 탈출이면 반출만, 사망·MIA·포기면 소실만 보인다
        [TestCase(RaidOutcome.Extracted, true, false)]
        [TestCase(RaidOutcome.Killed, false, true)]
        [TestCase(RaidOutcome.MissingInAction, false, true)]
        [TestCase(RaidOutcome.Abandoned, false, true)]
        public void Sections_ByOutcome(RaidOutcome outcome, bool carriedOut, bool lost)
        {
            var vm = RaidResultViewModel.From(M4TestData.Result(outcome));
            Assert.AreEqual(carriedOut, vm.ShowCarriedOut);
            Assert.AreEqual(lost, vm.ShowLost);
            Assert.AreEqual(outcome == RaidOutcome.Extracted ? RaidResultViewModel.SurvivedClass : RaidResultViewModel.DiedClass,
                vm.OutcomeClass);
        }

        // 정보 희소성: 탄창 탄 수·의약품 남은 횟수는 결과 화면에도 없다 (이름 + 묶음 수량만)
        [Test]
        public void CarriedOut_NoAmmoCountOrUses()
        {
            var magDef = TestData.Magazine30();
            var bandageDef = TestData.Bandage();
            var carried = new List<ItemInstance>
            {
                new Magazine(magDef, 17),
                new Magazine(magDef, 30),
                new MedicalItem(bandageDef),
            };
            var vm = RaidResultViewModel.From(M4TestData.Result(RaidOutcome.Extracted, "EXT-01", "남쪽 도로", carriedOut: carried));

            CollectionAssert.AreEqual(new[]
            {
                RaidResultViewModel.ItemName(magDef) + " ×2",
                RaidResultViewModel.ItemName(bandageDef),
            }, vm.CarriedOut);
            foreach (string line in vm.CarriedOut)
            {
                StringAssert.DoesNotContain("17", line);
                StringAssert.DoesNotContain("30", line);
                StringAssert.DoesNotContain("/", line);
            }
        }

        [Test]
        public void EmptyLists_AreEmpty()
        {
            var vm = RaidResultViewModel.From(M4TestData.Result(RaidOutcome.MissingInAction));
            CollectionAssert.IsEmpty(vm.BroughtIn);
            CollectionAssert.IsEmpty(vm.CarriedOut);
            CollectionAssert.IsEmpty(vm.Lost);
        }

        [Test]
        public void OverlayText_OneLinePerOutcome()
        {
            Assert.AreEqual("탈출 성공", RaidResultViewModel.OverlayText(RaidOutcome.Extracted));
            Assert.AreEqual("사망", RaidResultViewModel.OverlayText(RaidOutcome.Killed));
            Assert.AreEqual("작전 중 실종", RaidResultViewModel.OverlayText(RaidOutcome.MissingInAction));
            Assert.AreEqual("레이드 포기", RaidResultViewModel.OverlayText(RaidOutcome.Abandoned));
        }
    }

    // GameUIPresenter M4 확장: Esc 일시정지·세계 정지·포기·종료 오버레이·탈출 HUD·지도
    public class GameUIPresenterM4Tests
    {
        private EventBus bus;
        private ServiceRegistry services;
        private FakeInventoryService inventory;
        private HudConfigSO hud;
        private FlowRecorder rec;
        private GameFlow flow;
        private GameUIPresenter presenter;
        private List<ScreenKind> published;
        private int abandonRequests;

        [SetUp]
        public void SetUp()
        {
            bus = new EventBus();
            services = new ServiceRegistry();
            inventory = new FakeInventoryService();
            services.Register<IInventoryService>(inventory);
            services.Register<IBodyStatusView>(new FakeBodyStatusView());
            hud = TestData.Hud();
            hud.mapItem = M4TestData.MapItem();
            rec = new FlowRecorder();
            flow = rec.Create();
            presenter = new GameUIPresenter(bus, services, hud, flow) { InteractKeyLabel = "F" };
            presenter.Enable();
            published = new List<ScreenKind>();
            bus.Subscribe<ScreenStateChanged>(e => published.Add(e.Screen));
            abandonRequests = 0;
            bus.Subscribe<RaidAbandonRequested>(_ => abandonRequests++);
        }

        private UiFakeRaidInfo RegisterRaid()
        {
            var info = new UiFakeRaidInfo { RemainingSec = 900f };
            info.Pois.Add(new MapMarker("POI-PYL", "송전탑 언덕", new Vector2(230f, 300f)));
            info.Extractions.Add(new MapMarker("EXT-01", "남쪽 도로", new Vector2(0f, -470f)));
            services.Register<IRaidInfo>(info);
            return info;
        }

        private void GiveMap()
        {
            var pocket = new FakeGridView(new GridRef(GridKind.Pocket), new Vector2Int(1, 2));
            pocket.Placed.Add(new PlacedItem(new ItemInstance(hud.mapItem), Vector2Int.zero, false));
            inventory.Grids.Add(pocket);
        }

        [Test]
        public void Esc_NothingOpen_OpensPause_AndPausesWorld_EscAgainResumes()
        {
            Assert.IsTrue(presenter.Cancel());
            Assert.AreEqual(ScreenKind.Pause, presenter.Screens.Current);
            Assert.IsTrue(flow.IsWorldPaused);
            CollectionAssert.AreEqual(new[] { 0f }, rec.TimeScales);

            Assert.IsTrue(presenter.Cancel());
            Assert.AreEqual(ScreenKind.None, presenter.Screens.Current);
            Assert.IsFalse(flow.IsWorldPaused);
            CollectionAssert.AreEqual(new[] { 0f, 1f }, rec.TimeScales);
            CollectionAssert.AreEqual(new[] { ScreenKind.Pause, ScreenKind.None }, published);
        }

        [Test]
        public void Esc_WithScreenOpen_ClosesOnly_NoPause()
        {
            presenter.ToggleInventory();
            presenter.Cancel();
            Assert.AreEqual(ScreenKind.None, presenter.Screens.Current);
            CollectionAssert.IsEmpty(rec.TimeScales);
        }

        [Test]
        public void Resume_ClosesPause()
        {
            presenter.Cancel();
            Assert.IsTrue(presenter.Resume());
            Assert.AreEqual(ScreenKind.None, presenter.Screens.Current);
            Assert.IsFalse(flow.IsWorldPaused);
            Assert.IsFalse(presenter.Resume());
        }

        [Test]
        public void Pause_IgnoresTabHM()
        {
            RegisterRaid();
            GiveMap();
            presenter.Cancel();
            Assert.IsFalse(presenter.ToggleInventory());
            Assert.IsFalse(presenter.ToggleBodyStatus());
            Assert.IsFalse(presenter.ToggleMap());
            Assert.AreEqual(ScreenKind.Pause, presenter.Screens.Current);
        }

        [Test]
        public void Pause_LootOpened_DoesNotOpenInventory_AndClosesLoot()
        {
            presenter.Cancel();
            bus.Publish(new LootSessionChanged(true));
            Assert.AreEqual(ScreenKind.Pause, presenter.Screens.Current);
            Assert.AreEqual(1, inventory.CloseLootCalls);
        }

        [Test]
        public void Pause_WhenDead_DoesNotOpen()
        {
            bus.Publish(new PlayerDied(BodyPart.Thorax));
            Assert.IsFalse(presenter.Cancel());
            Assert.AreEqual(ScreenKind.None, presenter.Screens.Current);
            CollectionAssert.IsEmpty(rec.TimeScales);
        }

        [Test]
        public void Abandon_WithoutRaid_NotOffered()
        {
            presenter.Cancel();
            Assert.IsFalse(presenter.CanAbandon);
            Assert.IsFalse(presenter.RequestAbandon());
            Assert.IsFalse(presenter.ConfirmAbandon());
            Assert.AreEqual(0, abandonRequests);
        }

        [Test]
        public void Abandon_NeedsConfirm_ThenPublishesOnce()
        {
            RegisterRaid();
            int dirty = 0;
            presenter.PauseDirty += () => dirty++;

            Assert.IsFalse(presenter.RequestAbandon(), "일시정지 밖에서는 안 됨");
            presenter.Cancel();
            Assert.IsTrue(presenter.CanAbandon);
            Assert.IsFalse(presenter.ConfirmAbandon(), "확인 단계 전에는 발행 안 함");
            Assert.IsTrue(presenter.RequestAbandon());
            Assert.IsTrue(presenter.IsConfirmingAbandon);
            Assert.AreEqual(0, abandonRequests);

            Assert.IsTrue(presenter.ConfirmAbandon());
            Assert.AreEqual(1, abandonRequests);
            Assert.IsFalse(presenter.CanAbandon);
            Assert.IsFalse(presenter.RequestAbandon());
            Assert.IsFalse(presenter.ConfirmAbandon());
            Assert.AreEqual(1, abandonRequests);
            Assert.AreEqual(2, dirty);
        }

        [Test]
        public void Esc_WhileConfirming_CancelsConfirmOnly()
        {
            RegisterRaid();
            presenter.Cancel();
            presenter.RequestAbandon();
            Assert.IsTrue(presenter.Cancel());
            Assert.IsFalse(presenter.IsConfirmingAbandon);
            Assert.AreEqual(ScreenKind.Pause, presenter.Screens.Current);
            Assert.IsTrue(flow.IsWorldPaused);
        }

        // 포기 → Raid가 끝내면 RaidEnded → 종료 오버레이. 세계는 멈춘 채로 (UI가 풀지 않는다)
        [Test]
        public void RaidEnded_FromPause_ShowsOverlay_KeepsWorldPaused()
        {
            RegisterRaid();
            presenter.Cancel();
            presenter.RequestAbandon();
            presenter.ConfirmAbandon();
            bus.Publish(new RaidEnded(RaidOutcome.Abandoned, null));

            Assert.AreEqual(ScreenKind.RaidEnd, presenter.Screens.Current);
            Assert.AreEqual("레이드 포기", presenter.RaidEndText);
            Assert.IsTrue(flow.IsWorldPaused);
            CollectionAssert.AreEqual(new[] { 0f }, rec.TimeScales);
            Assert.IsFalse(presenter.Cancel(), "Esc로 닫히지 않는다");
            Assert.AreEqual(ScreenKind.RaidEnd, presenter.Screens.Current);
        }

        [Test]
        public void RaidEnded_AfterDeath_ShowsOverlay_AndDeathAfterwardKeepsIt()
        {
            bus.Publish(new PlayerDied(BodyPart.Head));
            bus.Publish(new RaidEnded(RaidOutcome.Killed, null));
            Assert.AreEqual(ScreenKind.RaidEnd, presenter.Screens.Current);
            Assert.AreEqual("사망", presenter.RaidEndText);

            presenter.Disable();
            presenter.Enable();
            bus.Publish(new RaidEnded(RaidOutcome.Extracted, "남쪽 도로"));
            bus.Publish(new PlayerDied(BodyPart.Head));
            Assert.AreEqual(ScreenKind.RaidEnd, presenter.Screens.Current);
            Assert.AreEqual("탈출 성공", presenter.RaidEndText);
            Assert.IsFalse(presenter.ToggleInventory());
            Assert.IsFalse(presenter.Screens.HudVisible);
        }

        [Test]
        public void RaidEnded_ClosesInventory_AndClearsHud()
        {
            presenter.ToggleInventory();
            bus.Publish(new InteractionPromptChanged(true, InteractionKind.Activate, "전원 스위치"));
            bus.Publish(new ExtractionStatusChanged(true, "남쪽 도로", ExtractionBlock.None, 1));
            bus.Publish(new RaidEnded(RaidOutcome.Extracted, "남쪽 도로"));

            Assert.AreEqual(ScreenKind.RaidEnd, published.Last());
            Assert.IsNull(presenter.Prompt.Text);
            Assert.IsNull(presenter.ExtractionHud.Text);
            bus.Publish(new ExtractionStatusChanged(true, "남쪽 도로", ExtractionBlock.None, 3));
            Assert.IsNull(presenter.ExtractionHud.Text, "종료 후 무시");
        }

        [Test]
        public void ExtractionStatus_DrivesHud_ClearedOnDeath()
        {
            bus.Publish(new ExtractionStatusChanged(true, "열차 신호소", ExtractionBlock.NoPower, 15));
            Assert.AreEqual("전원이 꺼져 있다", presenter.ExtractionHud.Text);
            bus.Publish(new ExtractionStatusChanged(true, "열차 신호소", ExtractionBlock.None, 15));
            Assert.AreEqual("탈출까지 15", presenter.ExtractionHud.Text);
            bus.Publish(new PlayerDied(BodyPart.Head));
            Assert.IsNull(presenter.ExtractionHud.Text);
        }

        [Test]
        public void ActivatePrompt_UsesActivateVerb()
        {
            bus.Publish(new InteractionPromptChanged(true, InteractionKind.Activate, "전원 스위치"));
            Assert.AreEqual("F  작동: 전원 스위치", presenter.Prompt.Text);
        }

        [Test]
        public void Map_WithoutRaidInfo_Refused_WithWarning()
        {
            GiveMap();
            LogAssert.Expect(LogType.Warning, new Regex("IRaidInfo"));
            Assert.IsFalse(presenter.ToggleMap());
            Assert.AreEqual(ScreenKind.None, presenter.Screens.Current);
        }

        [Test]
        public void Map_WithoutMapItem_RefusedSilently()
        {
            RegisterRaid();
            Assert.IsFalse(presenter.ToggleMap());
            Assert.AreEqual(ScreenKind.None, presenter.Screens.Current);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Map_ConfigWithoutMapItem_Refused_WithWarning()
        {
            RegisterRaid();
            GiveMap();
            hud.mapItem = null;
            LogAssert.Expect(LogType.Warning, new Regex("mapItem"));
            Assert.IsFalse(presenter.ToggleMap());
        }

        [Test]
        public void Map_WithItem_OpensWithMarkersAndTime_NoWorldPause()
        {
            UiFakeRaidInfo info = RegisterRaid();
            GiveMap();

            Assert.IsTrue(presenter.ToggleMap());
            Assert.AreEqual(ScreenKind.Map, presenter.Screens.Current);
            Assert.AreSame(info, presenter.RaidInfo);
            Assert.AreEqual(2, presenter.Map.Markers.Count);
            Assert.AreEqual("남은 시간: 약 15분", presenter.Map.RemainingText);
            CollectionAssert.IsEmpty(rec.TimeScales, "지도는 세계를 멈추지 않는다");
            CollectionAssert.AreEqual(new[] { ScreenKind.Map }, published);

            int dirty = 0;
            presenter.MapTimeDirty += () => dirty++;
            info.RemainingSec = 880f;
            presenter.Tick(0.016f);
            Assert.AreEqual(0, dirty);
            info.RemainingSec = 860f;
            presenter.Tick(0.016f);
            Assert.AreEqual(1, dirty);
            Assert.AreEqual("남은 시간: 약 14분", presenter.Map.RemainingText);

            Assert.IsTrue(presenter.ToggleMap());
            Assert.AreEqual(ScreenKind.None, presenter.Screens.Current);
        }

        [Test]
        public void Map_EquippedMapItem_AlsoCounts()
        {
            RegisterRaid();
            inventory.Equipped[EquipSlot.Backpack] = new ItemInstance(hud.mapItem);   // Contains는 장착도 본다
            Assert.IsTrue(presenter.ToggleMap());
        }

        [Test]
        public void Disable_FromPause_ResumesWorld_AndUnsubscribesM4Events()
        {
            presenter.Cancel();
            presenter.Disable();

            Assert.IsFalse(flow.IsWorldPaused);
            Assert.AreEqual(ScreenKind.None, presenter.Screens.Current);
            Assert.AreEqual(0, bus.SubscriberCount<RaidStarted>());
            Assert.AreEqual(0, bus.SubscriberCount<ExtractionStatusChanged>());
            Assert.AreEqual(0, bus.SubscriberCount<RaidEnded>());
            Assert.AreEqual(0, bus.SubscriberCount<PlayerDied>());
        }

        [Test]
        public void Disable_FromRaidEnd_ReleasesInputBlock()
        {
            bus.Publish(new RaidEnded(RaidOutcome.MissingInAction, null));
            presenter.Disable();
            Assert.AreEqual(ScreenKind.None, published.Last());
        }

        [Test]
        public void NullFlow_PauseStillOpens()
        {
            var plain = new GameUIPresenter(new EventBus(), services, hud);
            Assert.IsTrue(plain.Cancel());
            Assert.AreEqual(ScreenKind.Pause, plain.Screens.Current);
            Assert.IsTrue(plain.Cancel());
        }
    }

    // UXML ↔ 뷰 이름 계약: 요소 이름이 바뀌면 뷰 생성자가 예외를 던진다. 렌더링 결과 일부도 확인.
    public class RaidUiUxmlTests
    {
        private const string GameUxml = "Assets/_Project/Art/UI/GameUI.uxml";
        private const string MainMenuUxml = "Assets/_Project/Art/UI/MainMenu.uxml";

        private static VisualElement Clone(string path)
        {
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
            Assert.IsNotNull(asset, path);
            return asset.CloneTree();
        }

        [Test]
        public void GameUxml_HasM4Views_AndHudHasNoTimerOrNumbersByDefault()
        {
            var root = Clone(GameUxml);
            var hud = new HudView(root);
            var map = new MapScreenView(root);
            var pause = new PauseScreenView(root, () => { }, () => { }, () => { }, () => { });
            var end = new RaidEndOverlayView(root);

            hud.Set(true, null, null, "탈출까지 9");
            Assert.AreEqual("탈출까지 9", root.Q<Label>(HudView.ExtractName).text);

            var markers = new List<MapMarkerView>
            {
                new MapMarkerView("EXT-01", "남쪽 도로", MapMarkerKind.Extraction, new Vector2(0.5f, 0.97f)),
                new MapMarkerView("POI-HWY", "붕괴된 고속도로", MapMarkerKind.PointOfInterest, new Vector2(0.44f, 0.8f)),
            };
            map.Render(markers, "남은 시간: 약 10분");
            var area = root.Q(MapScreenView.AreaName);
            Assert.AreEqual(2, area.childCount, "표식은 POI·탈출구뿐 (내 위치 없음)");
            Assert.AreEqual("남은 시간: 약 10분", root.Q<Label>(MapScreenView.TimeName).text);

            pause.Render(confirming: false, canAbandon: false);
            Assert.AreEqual(DisplayStyle.None,
                root.Q(PauseScreenView.AbandonName).style.display.value, "레이드 밖에서는 [계속]만");
            end.Render("사망");
            Assert.AreEqual("사망", root.Q<Label>(RaidEndOverlayView.TextName).text);
            pause.Dispose();
        }

        [Test]
        public void MainMenuUxml_HasMenuAndResultElements()
        {
            var root = Clone(MainMenuUxml);
            Assert.IsNotNull(root.Q<Button>(MainMenuRoot.StartName));
            Assert.IsNotNull(root.Q<Button>(MainMenuRoot.QuitName));
            Assert.IsNotNull(root.Q(MainMenuRoot.MenuPanelName));

            var view = new RaidResultView(root, () => { });
            var mag = TestData.Magazine30();
            view.Render(RaidResultViewModel.From(M4TestData.Result(RaidOutcome.Killed,
                broughtIn: new ItemDefinitionSO[] { mag, mag }, lost: new ItemDefinitionSO[] { mag })));

            Assert.AreEqual("사망 — 머리", root.Q<Label>(RaidResultView.TitleName).text);
            Assert.AreEqual(DisplayStyle.None,
                root.Q(RaidResultView.CarriedOutSectionName).style.display.value, "사망이면 반출 없음");
            Assert.AreEqual(1, root.Q(RaidResultView.BroughtInName).childCount);
            Assert.AreEqual(RaidResultViewModel.ItemName(mag) + " ×2",
                root.Q(RaidResultView.BroughtInName).Q<Label>().text);
            Assert.AreEqual(UiText.ResultNone,
                root.Q(RaidResultView.CarriedOutName).Q<Label>().text, "빈 목록은 '없음'");
            view.Dispose();
        }
    }
}
