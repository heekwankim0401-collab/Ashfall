using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Ashfall.Core;
using Ashfall.Hideout;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ashfall.Tests.EditMode
{
    // SYS-HUB 은신처 서비스 (M5 설계 §1.4 W1~W4, §4.2, AC3): 진입 절차·저장·레이드 시작·메뉴 복귀 + 상인·키트.
    // 세이브는 메모리 저장소만 쓴다(실제 persistentDataPath 없음). 은신처 인벤토리는 UiTestFakes.FakeInventoryService.
    public class HideoutServiceTests
    {
        private MemorySaveStorage storage;
        private FakeCatalog catalog;
        private FakeClock clock;
        private ProfileService profiles;
        private FlowRecorder rec;
        private GameFlow flow;
        private HideoutConfigSO config;
        private TraderConfigSO trader;
        private FakeInventoryService inventory;
        private bool inventoryPresent;
        private int changedCount;

        [SetUp]
        public void SetUp()
        {
            storage = new MemorySaveStorage();
            catalog = FakeCatalog.Standard();
            clock = new FakeClock();
            profiles = new ProfileService(storage, clock.Now);
            rec = new FlowRecorder();
            flow = rec.Create(profiles);
            config = M5TestData.HideoutConfig(catalog);
            trader = M5TestData.TraderConfig(catalog);
            inventory = new FakeInventoryService();
            inventoryPresent = true;
            changedCount = 0;
        }

        // 메인 메뉴 → 은신처 씬 로드 완료 (Phase Hideout)
        private void ToHideoutScene()
        {
            flow.OnSceneLoaded(SceneNames.MainMenu);
            Assert.IsTrue(flow.EnterHideout());
            flow.OnSceneLoaded(SceneNames.Hideout);
        }

        private SaveLoadReport Enter() =>
            HideoutService.Enter(profiles, flow, catalog, M5TestData.Defaults());

        private HideoutService CreateService() =>
            new HideoutService(profiles, flow, config, trader,
                () => inventoryPresent ? inventory : null, clock.Now, () => changedCount++);

        // 첫 은신처 진입까지 끝낸 서비스
        private HideoutService InHideout()
        {
            ToHideoutScene();
            Enter();
            return CreateService();
        }

        // 가짜 인벤토리에 장비: 소총(17발) Primary1 + 주머니 0에 붕대(퀵슬롯 0)
        private (WeaponItem rifle, MedicalItem bandage) Equip()
        {
            WeaponItem rifle = M5TestData.Rifle(catalog, 17);
            MedicalItem bandage = M5TestData.Bandage(catalog);
            inventory.Equipped[EquipSlot.Primary1] = rifle;
            var pocket = new FakeGridView(new GridRef(GridKind.Pocket, 0), Vector2Int.one);
            pocket.Placed.Add(new PlacedItem(bandage, Vector2Int.zero, false));
            inventory.Grids.Add(pocket);
            inventory.QuickSlots[0] = bandage;
            return (rifle, bandage);
        }

        private static List<ItemInstance> Items(Loadout loadout)
        {
            var list = new List<ItemInstance>();
            loadout.CollectItems(list);
            return list;
        }

        private static RaidResult Killed() =>
            new RaidResult(RaidOutcome.Killed, null, null, true, BodyPart.Head, 300f, 0, null, null, null);

        // ── 생성 ──────────────────────────────────────────────

        [Test]
        public void Constructor_RequiresLoadedProfile()
        {
            Assert.Throws<InvalidOperationException>(() => CreateService());
        }

        [Test]
        public void Constructor_Nulls_Throw()
        {
            Enter();
            Assert.Throws<ArgumentNullException>(() => new HideoutService(null, flow, config, trader, null, clock.Now, null));
            Assert.Throws<ArgumentNullException>(() => new HideoutService(profiles, null, config, trader, null, clock.Now, null));
            Assert.Throws<ArgumentNullException>(() => new HideoutService(profiles, flow, null, trader, null, clock.Now, null));
            Assert.Throws<ArgumentNullException>(() => new HideoutService(profiles, flow, config, null, null, clock.Now, null));
            Assert.Throws<ArgumentNullException>(() => new HideoutService(profiles, flow, config, trader, null, null, null));
            Assert.DoesNotThrow(() => new HideoutService(profiles, flow, config, trader, null, clock.Now, null));
        }

        // ── W1 진입 ───────────────────────────────────────────

        [Test]
        public void Enter_NewProfile_LoadsAndSavesOnce()
        {
            ToHideoutScene();
            SaveLoadReport report = Enter();
            Assert.AreEqual(SaveLoadStatus.NewProfile, report.Status);
            Assert.IsTrue(profiles.IsLoaded);
            Assert.AreEqual(1, storage.ReadCount);
            Assert.AreEqual(1, storage.WriteCount);
            SaveData written = storage.LastWritten();
            Assert.IsFalse(written.raidInProgress);
            Assert.AreEqual(M5TestData.StartingCasings, written.casings);
            Assert.IsNull(profiles.PendingNotice);
        }

        [Test]
        public void Enter_SecondTimeSameRun_DoesNotReread_SavesAgain()
        {
            ToHideoutScene();
            SaveLoadReport first = Enter();
            profiles.Profile.Casings = 1234;
            SaveLoadReport second = Enter();
            Assert.AreSame(first, second);
            Assert.AreEqual(1, storage.ReadCount);
            Assert.AreEqual(2, storage.WriteCount);
            Assert.AreEqual(1234, storage.LastWritten().casings);
        }

        [Test]
        public void Enter_AfterExtraction_CarriedOutBecomesLoadout_SavedNotInRaid()
        {
            HideoutService service = InHideout();
            Equip();
            Assert.IsTrue(service.StartRaid());
            flow.OnSceneLoaded(SceneNames.RaidCrimsonBasin);

            Loadout carried = M5TestData.SampleLoadout(catalog, out Dictionary<string, ItemInstance> items);
            Assert.IsTrue(flow.CompleteRaid(M5TestData.Extracted(carried)));
            int writes = storage.WriteCount;

            Enter();
            flow.OnSceneLoaded(SceneNames.Hideout);
            Assert.AreSame(carried, profiles.Profile.Loadout);
            Assert.AreEqual(writes + 1, storage.WriteCount);
            SaveData written = storage.LastWritten();
            Assert.IsFalse(written.raidInProgress);
            Assert.AreEqual(4, written.loadout.Count);
            Assert.IsNotNull(flow.PendingResult);   // 결과 패널은 표시용으로 남는다
            Assert.IsNull(profiles.PendingNotice);
            Assert.IsFalse(flow.HasRaidInFlow);
        }

        [Test]
        public void Enter_AfterDeath_LoadoutEmpty_StashKept()
        {
            HideoutService service = InHideout();
            Equip();
            Assert.AreEqual(KitClaimResult.Ok, service.TryClaimKit());
            Assert.IsTrue(service.StartRaid());
            flow.OnSceneLoaded(SceneNames.RaidCrimsonBasin);
            Assert.IsTrue(flow.CompleteRaid(Killed()));

            Enter();
            Assert.IsTrue(profiles.Profile.Loadout.IsEmpty);
            Assert.AreEqual(4, profiles.Profile.Stash.Items.Count);
            SaveData written = storage.LastWritten();
            Assert.IsFalse(written.raidInProgress);
            Assert.AreEqual(0, written.loadout.Count);
            Assert.AreEqual(4, written.stash.Count);
        }

        [Test]
        public void Enter_Interrupted_CrashNoticeWithBroughtItems()
        {
            HideoutService service = InHideout();
            (WeaponItem rifle, MedicalItem bandage) = Equip();
            Assert.IsTrue(service.StartRaid());
            flow.OnSceneLoaded(SceneNames.RaidCrimsonBasin);

            // 결과 없이 은신처로 (디버그·테스트 경로)
            Enter();
            Assert.IsTrue(profiles.Profile.Loadout.IsEmpty);
            Assert.IsFalse(profiles.Profile.RaidInProgress);
            Assert.IsNotNull(profiles.PendingNotice);
            Assert.IsTrue(profiles.PendingNotice.CrashPenaltyApplied);
            CollectionAssert.AreEquivalent(new ItemDefinitionSO[] { rifle.Definition, bandage.Definition }, profiles.PendingNotice.CrashLost);
            Assert.IsFalse(storage.LastWritten().raidInProgress);
        }

        [Test]
        public void Enter_OutOfFlowRaidResult_DisplayOnly_NotApplied()
        {
            HideoutService service = InHideout();
            Loadout kept = M5TestData.SampleLoadout(catalog, out _);
            profiles.Profile.Loadout = kept;

            // 에디터에서 레이드 씬 직접 Play (흐름 레이드 표 없음, Q25)
            flow.OnSceneLoaded(SceneNames.RaidCrimsonBasin);
            Loadout other = M5TestData.SampleLoadout(catalog, out _);
            Assert.IsTrue(flow.CompleteRaid(M5TestData.Extracted(other)));

            Enter();
            Assert.AreSame(kept, profiles.Profile.Loadout);
            Assert.IsNotNull(flow.PendingResult);
            Assert.IsNull(profiles.PendingNotice);
        }

        [Test]
        public void Enter_CrashedLastRun_DetectedAndSaved()
        {
            // 지난 실행: 레이드 시작 기록 후 종료
            HideoutService service = InHideout();
            Equip();
            Assert.IsTrue(service.StartRaid());
            Assert.IsTrue(storage.LastWritten().raidInProgress);

            // 다음 실행 (같은 파일, 새 서비스·흐름)
            profiles = new ProfileService(storage, clock.Now);
            rec = new FlowRecorder();
            flow = rec.Create(profiles);
            ToHideoutScene();
            SaveLoadReport report = Enter();
            Assert.IsTrue(report.CrashPenaltyApplied);
            Assert.AreEqual(2, report.CrashLost.Count);
            Assert.IsTrue(profiles.Profile.Loadout.IsEmpty);
            Assert.AreSame(report, profiles.PendingNotice);
            SaveData written = storage.LastWritten();
            Assert.IsFalse(written.raidInProgress);
            Assert.AreEqual(0, written.loadout.Count);
        }

        [Test]
        public void Enter_SaveFails_StillEnters_LastSaveFailed()
        {
            storage.WriteFails = true;
            ToHideoutScene();
            LogAssert.Expect(LogType.Error, new Regex("세이브 실패"));
            Enter();
            HideoutService service = CreateService();
            Assert.IsTrue(profiles.IsLoaded);
            Assert.IsTrue(service.LastSaveFailed);
        }

        // ── 읽기 ──────────────────────────────────────────────

        [Test]
        public void Reads_ReflectProfileAndTrader()
        {
            HideoutService service = InHideout();
            Assert.AreEqual(M5TestData.StartingCasings, service.Casings);
            Assert.AreEqual("고물상", service.TraderName);
            Assert.AreEqual(trader.catalog.Length, service.Offers.Count);
            Assert.AreSame(profiles.Profile.Stash, service.Stash);
            Assert.AreEqual(new Vector2Int(10, 30), service.Stash.Size);
            Assert.IsTrue(service.IsKitAvailable);
            Assert.AreEqual(0f, service.KitCooldownRemainingSec);
            Assert.IsFalse(service.IsLeaving);
            Assert.IsFalse(service.LastSaveFailed);
        }

        [Test]
        public void IHideoutService_Implemented()
        {
            IHideoutService service = InHideout();
            Assert.IsNotNull(service);
        }

        // ── 상인·키트 + HideoutChanged ────────────────────────

        [Test]
        public void Buy_Sell_Kit_PublishOnlyOnSuccess()
        {
            HideoutService service = InHideout();
            ItemDefinitionSO map = catalog.Get("ITM-MAP-01");

            Assert.AreEqual(TradeResult.Ok, service.TryBuy(map));
            Assert.AreEqual(1, changedCount);
            Assert.AreEqual(M5TestData.StartingCasings - 100, service.Casings);

            Assert.AreEqual(TradeResult.NotForSale, service.TryBuy(catalog.Get("ITM-FLR-01")));
            Assert.AreEqual(1, changedCount);

            ItemInstance bought = service.Stash.Items[0].Item;
            Assert.AreEqual(40, service.GetSellPrice(bought));
            Assert.AreEqual(TradeResult.Ok, service.TrySell(bought));
            Assert.AreEqual(2, changedCount);
            Assert.AreEqual(TradeResult.NotInStash, service.TrySell(bought));
            Assert.AreEqual(2, changedCount);

            Assert.AreEqual(KitClaimResult.Ok, service.TryClaimKit());
            Assert.AreEqual(3, changedCount);
            Assert.AreEqual(KitClaimResult.OnCooldown, service.TryClaimKit());
            Assert.AreEqual(3, changedCount);
            Assert.IsFalse(service.IsKitAvailable);
            Assert.AreEqual(1200f, service.KitCooldownRemainingSec, 1e-3f);
        }

        [Test]
        public void Trades_MemoryOnly_UntilSave()
        {
            HideoutService service = InHideout();
            int writes = storage.WriteCount;
            service.TryBuy(catalog.Get("ITM-MAP-01"));
            service.TryClaimKit();
            Assert.AreEqual(writes, storage.WriteCount);   // Q7: 거래·키트마다 저장하지 않는다
        }

        [Test]
        public void AcknowledgeNotice_ClearsPendingNotice()
        {
            HideoutService service = InHideout();
            Equip();
            service.StartRaid();
            flow.OnSceneLoaded(SceneNames.RaidCrimsonBasin);
            Enter();
            service = CreateService();
            Assert.IsNotNull(service.PendingNotice);
            service.AcknowledgeNotice();
            Assert.IsNull(service.PendingNotice);
            Assert.IsNull(profiles.PendingNotice);
        }

        // ── W2/W3 SaveNow ─────────────────────────────────────

        [Test]
        public void SaveNow_CapturesInventoryIntoProfileAndFile()
        {
            HideoutService service = InHideout();
            (WeaponItem rifle, MedicalItem bandage) = Equip();
            int writes = storage.WriteCount;
            Assert.IsTrue(service.SaveNow());
            Assert.AreEqual(writes + 1, storage.WriteCount);
            CollectionAssert.AreEqual(new ItemInstance[] { rifle, bandage }, Items(profiles.Profile.Loadout));
            Assert.AreSame(bandage, profiles.Profile.Loadout.QuickSlots[0]);
            SaveData written = storage.LastWritten();
            Assert.AreEqual(2, written.loadout.Count);
            Assert.AreEqual("WPN-RIF-01", written.loadout[0].id);
            Assert.AreEqual("Primary1", written.loadout[0].slot);
            Assert.AreEqual(17, written.loadout[0].loadedMagazineRounds);
            Assert.AreEqual(1, written.quickSlots[0]);   // loadout 인덱스 (붕대 = 1번 레코드)
            Assert.IsFalse(written.raidInProgress);
        }

        [Test]
        public void SaveNow_NoInventory_KeepsProfileLoadout()
        {
            HideoutService service = InHideout();
            Loadout kept = M5TestData.SampleLoadout(catalog, out _);
            profiles.Profile.Loadout = kept;
            inventoryPresent = false;
            Assert.IsTrue(service.SaveNow());
            Assert.AreSame(kept, profiles.Profile.Loadout);
            Assert.AreEqual(4, storage.LastWritten().loadout.Count);
        }

        [Test]
        public void SaveNow_WriteFails_FalseAndFlag()
        {
            HideoutService service = InHideout();
            storage.WriteFails = true;
            LogAssert.Expect(LogType.Error, new Regex("세이브 실패"));
            Assert.IsFalse(service.SaveNow());
            Assert.IsTrue(service.LastSaveFailed);
        }

        // ── W4 StartRaid ──────────────────────────────────────

        [Test]
        public void StartRaid_CaptureGoesToFileAndFlow_ThenProfileLoadoutEmpty()
        {
            HideoutService service = InHideout();
            (WeaponItem rifle, MedicalItem bandage) = Equip();
            int writes = storage.WriteCount;

            Assert.IsTrue(service.StartRaid());
            Assert.IsTrue(service.IsLeaving);
            Assert.AreEqual(SceneNames.RaidCrimsonBasin, rec.Loads.Last());
            Assert.AreEqual(writes + 1, storage.WriteCount);

            // 파일 = 반입 그대로 + raidInProgress (Q4)
            SaveData written = storage.LastWritten();
            Assert.IsTrue(written.raidInProgress);
            Assert.AreEqual(2, written.loadout.Count);

            // 레이드 인벤토리가 받는 반입 = 같은 인스턴스·위치
            Assert.IsTrue(flow.TryGetRaidLoadout(SceneNames.RaidCrimsonBasin, out Loadout brought));
            CollectionAssert.AreEqual(new ItemInstance[] { rifle, bandage }, Items(brought));
            Assert.IsTrue(brought.Entries[0].Location.IsEquipped);
            Assert.AreEqual(EquipSlot.Primary1, brought.Entries[0].Location.Slot);
            Assert.AreSame(bandage, brought.QuickSlots[0]);

            // 메모리 은신처 장비는 비었다(레이드가 가져감), 보관함은 그대로
            Assert.IsTrue(profiles.Profile.Loadout.IsEmpty);
            Assert.IsTrue(profiles.Profile.RaidInProgress);
        }

        [Test]
        public void StartRaid_EmptyEquipment_Allowed()
        {
            HideoutService service = InHideout();
            Assert.IsTrue(service.StartRaid());
            Assert.IsTrue(flow.TryGetRaidLoadout(SceneNames.RaidCrimsonBasin, out Loadout brought));
            Assert.IsTrue(brought.IsEmpty);
        }

        [Test]
        public void StartRaid_PublishesLeaving_SecondCallRejected_TradesUnavailable()
        {
            HideoutService service = InHideout();
            Assert.IsTrue(service.StartRaid());
            Assert.AreEqual(1, changedCount);
            int loads = rec.Loads.Count;
            Assert.IsFalse(service.StartRaid());
            Assert.IsFalse(service.ReturnToMainMenu());
            Assert.AreEqual(loads, rec.Loads.Count);

            Assert.AreEqual(TradeResult.Unavailable, service.TryBuy(catalog.Get("ITM-MAP-01")));
            Assert.AreEqual(TradeResult.Unavailable, service.TrySell(null));
            Assert.AreEqual(KitClaimResult.Unavailable, service.TryClaimKit());
            Assert.AreEqual(M5TestData.StartingCasings, service.Casings);
        }

        [Test]
        public void StartRaid_ThenSaveNow_Ignored_RaidRecordKept()
        {
            HideoutService service = InHideout();
            Equip();
            service.StartRaid();
            int writes = storage.WriteCount;
            Assert.IsFalse(service.SaveNow());   // W3 앱 종료가 레이드 시작 기록을 덮지 않는다
            Assert.AreEqual(writes, storage.WriteCount);
            Assert.IsTrue(storage.LastWritten().raidInProgress);
        }

        [Test]
        public void StartRaid_SaveFails_NotStarted_LeavingReleased_EquipmentKept()
        {
            HideoutService service = InHideout();
            (WeaponItem rifle, MedicalItem bandage) = Equip();
            storage.WriteFails = true;
            LogAssert.Expect(LogType.Error, new Regex("세이브 실패"));

            Assert.IsFalse(service.StartRaid());
            Assert.IsFalse(service.IsLeaving);
            Assert.IsFalse(rec.Loads.Contains(SceneNames.RaidCrimsonBasin));
            Assert.IsFalse(flow.HasRaidInFlow);
            Assert.IsFalse(profiles.Profile.RaidInProgress);
            CollectionAssert.AreEqual(new ItemInstance[] { rifle, bandage }, Items(profiles.Profile.Loadout));
            Assert.AreEqual(2, changedCount);   // 떠남 → 해제

            storage.WriteFails = false;
            Assert.IsTrue(service.StartRaid());   // 다시 시도 가능
        }

        [Test]
        public void StartRaid_LoadRequestFails_Cancelled_LeavingReleased()
        {
            HideoutService service = InHideout();
            (WeaponItem rifle, MedicalItem bandage) = Equip();
            rec.LoadSucceeds = false;
            LogAssert.Expect(LogType.Error, new Regex("로드 요청 실패"));

            Assert.IsFalse(service.StartRaid());
            Assert.IsFalse(service.IsLeaving);
            Assert.IsFalse(profiles.Profile.RaidInProgress);
            Assert.IsFalse(storage.LastWritten().raidInProgress);
            CollectionAssert.AreEqual(new ItemInstance[] { rifle, bandage }, Items(profiles.Profile.Loadout));
        }

        [Test]
        public void StartRaid_NotInHideoutPhase_Rejected()
        {
            Enter();   // 흐름 밖(Phase None)에서 프로필만 읽음
            HideoutService service = CreateService();
            Assert.IsFalse(service.StartRaid());
            Assert.IsFalse(service.IsLeaving);
            Assert.IsFalse(profiles.Profile.RaidInProgress);
        }

        [Test]
        public void StartRaid_NoInventory_UsesProfileLoadout()
        {
            HideoutService service = InHideout();
            Loadout kept = M5TestData.SampleLoadout(catalog, out _);
            profiles.Profile.Loadout = kept;
            inventoryPresent = false;
            Assert.IsTrue(service.StartRaid());
            Assert.IsTrue(flow.TryGetRaidLoadout(SceneNames.RaidCrimsonBasin, out Loadout brought));
            Assert.AreSame(kept, brought);
        }

        // ── W2 ReturnToMainMenu ───────────────────────────────

        [Test]
        public void ReturnToMainMenu_SavesThenLeaves()
        {
            HideoutService service = InHideout();
            (WeaponItem rifle, _) = Equip();
            service.TryBuy(catalog.Get("ITM-MAP-01"));
            int writes = storage.WriteCount;

            Assert.IsTrue(service.ReturnToMainMenu());
            Assert.IsTrue(service.IsLeaving);
            Assert.AreEqual(SceneNames.MainMenu, rec.Loads.Last());
            Assert.AreEqual(writes + 1, storage.WriteCount);
            SaveData written = storage.LastWritten();
            Assert.AreEqual(2, written.loadout.Count);
            Assert.AreEqual(1, written.stash.Count);
            Assert.AreEqual(M5TestData.StartingCasings - 100, written.casings);
            Assert.IsFalse(written.raidInProgress);

            Assert.IsFalse(service.SaveNow());   // 떠나는 중
            Assert.AreEqual(writes + 1, storage.WriteCount);
        }

        // M5 리뷰 6: W2 저장 실패 → 이동하지 않는다(메시지는 은신처 UI에 보인다). 다시 누르면 또 실패해도 이동.
        [Test]
        public void ReturnToMainMenu_SaveFails_StaysOnce_SecondPressLeaves()
        {
            HideoutService service = InHideout();
            storage.WriteFails = true;
            int loads = rec.Loads.Count;
            LogAssert.Expect(LogType.Error, new Regex("세이브 실패"));

            Assert.IsFalse(service.ReturnToMainMenu());
            Assert.IsTrue(service.LastSaveFailed);
            Assert.IsFalse(service.IsLeaving);
            Assert.AreEqual(loads, rec.Loads.Count, "이동하지 않는다");

            LogAssert.Expect(LogType.Error, new Regex("세이브 실패"));
            Assert.IsTrue(service.ReturnToMainMenu(), "두 번째는 저장이 또 실패해도 이동");
            Assert.IsTrue(service.IsLeaving);
            Assert.AreEqual(SceneNames.MainMenu, rec.Loads.Last());
            Assert.AreEqual(3, storage.WriteAttempts, "W1 1회 + 두 번 다 저장을 시도했다");
        }

        [Test]
        public void ReturnToMainMenu_SaveFailsThenSucceeds_Leaves()
        {
            HideoutService service = InHideout();
            storage.WriteFails = true;
            LogAssert.Expect(LogType.Error, new Regex("세이브 실패"));
            Assert.IsFalse(service.ReturnToMainMenu());

            storage.WriteFails = false;
            int writes = storage.WriteCount;
            Assert.IsTrue(service.ReturnToMainMenu());
            Assert.AreEqual(writes + 1, storage.WriteCount);
            Assert.IsFalse(service.LastSaveFailed);
        }

        // M5 리뷰 1: 읽기 전용(세이브를 못 읽음) — 메뉴 복귀는 바로, 레이드 시작은 W4 실패로 거부, 파일 쓰기 없음
        [Test]
        public void ReadOnly_StartRaidRejected_ReturnToMenuLeavesImmediately_NoWrites()
        {
            storage.Text = "{\"version\": 1, \"casings\": 77}";
            storage.ReadFails = true;
            profiles = new ProfileService(storage, clock.Now, _ => { });
            flow = rec.Create(profiles);
            ToHideoutScene();
            LogAssert.Expect(LogType.Error, new Regex("세이브를 읽지 못했다"));
            SaveLoadReport report = Enter();
            HideoutService service = CreateService();
            Equip();

            Assert.AreEqual(SaveLoadStatus.ReadError, report.Status);
            Assert.IsTrue(service.IsReadOnly);
            Assert.IsNotNull(service.PendingNotice);
            Assert.IsFalse(service.StartRaid());
            Assert.IsTrue(service.LastSaveFailed);
            Assert.IsFalse(service.IsLeaving);
            Assert.IsFalse(flow.HasRaidInFlow);
            Assert.IsFalse(rec.Loads.Contains(SceneNames.RaidCrimsonBasin));

            Assert.IsTrue(service.ReturnToMainMenu(), "읽기 전용이면 두 번 누르지 않아도 나간다");
            Assert.AreEqual(SceneNames.MainMenu, rec.Loads.Last());
            Assert.AreEqual(0, storage.WriteAttempts);
            Assert.AreEqual("{\"version\": 1, \"casings\": 77}", storage.Text);
        }

        // M5 리뷰 7: W1 저장 실패 표시가 남아도, 저장 없이 거부된 레이드 시작은 "저장 실패"가 아니다
        [Test]
        public void StartRaid_RejectedWithoutSave_ClearsEarlierSaveFailure()
        {
            storage.WriteFails = true;
            LogAssert.Expect(LogType.Error, new Regex("세이브 실패"));
            Enter();   // 흐름 밖(Phase None) — W1 저장 실패
            Assert.IsTrue(profiles.LastSaveFailed);
            HideoutService service = CreateService();

            Assert.IsFalse(service.StartRaid(), "단계 거부 (저장 시도 없음)");
            Assert.IsFalse(service.LastSaveFailed);
        }

        // M5 리뷰 5: 진입 완료 표시 — Enter가 토큰을 남기고, 레이드 시작·메뉴 복귀가 지운다
        [Test]
        public void Enter_MarksEntryToken_ClearedOnLeave()
        {
            ToHideoutScene();
            HideoutService.Enter(profiles, flow, catalog, M5TestData.Defaults(), 42);
            Assert.IsTrue(profiles.IsHideoutEntered(42));
            HideoutService service = CreateService();
            Assert.IsTrue(service.StartRaid());
            Assert.IsFalse(profiles.IsHideoutEntered(42));

            // 레이드 → 은신처 (새 씬 인스턴스)
            flow.OnSceneLoaded(SceneNames.RaidCrimsonBasin);
            flow.CompleteRaid(M5TestData.Extracted(Loadout.Empty));
            HideoutService.Enter(profiles, flow, catalog, M5TestData.Defaults(), 43);
            Assert.IsTrue(profiles.IsHideoutEntered(43));
            Assert.IsFalse(profiles.IsHideoutEntered(42));
            flow.OnSceneLoaded(SceneNames.Hideout);
            service = CreateService();
            Assert.IsTrue(service.ReturnToMainMenu());
            Assert.IsFalse(profiles.IsHideoutEntered(43));
        }

        // M5 리뷰 2: 진입 절차 예외 → false, 진입 표시 없음, 흐름 레이드 표 비움(다음 레이드 시작이 막히지 않게)
        [Test]
        public void TryEnter_Exception_False_FlowTableCleared()
        {
            HideoutService first = InHideout();
            Assert.IsTrue(first.StartRaid());
            Assert.IsTrue(flow.HasRaidInFlow);
            profiles.UseStorage(storage);   // 아직 읽지 않은 상태 → EnsureLoaded가 카탈로그 null로 예외
            LogAssert.Expect(LogType.Exception, new Regex("ArgumentNullException"));
            LogAssert.Expect(LogType.Error, new Regex("진입 절차 실패"));

            bool ok = HideoutService.TryEnter(profiles, flow, null, M5TestData.Defaults(), 9, CreateService,
                out SaveLoadReport report, out HideoutService service);

            Assert.IsFalse(ok);
            Assert.IsNull(report);
            Assert.IsNull(service);
            Assert.IsFalse(profiles.IsHideoutEntered(9));
            Assert.IsFalse(flow.HasRaidInFlow, "흐름 레이드 표를 비웠다");
        }

        [Test]
        public void TryEnter_ServiceFactoryThrows_False()
        {
            ToHideoutScene();
            LogAssert.Expect(LogType.Exception, new Regex("테스트 생성 실패"));
            LogAssert.Expect(LogType.Error, new Regex("진입 절차 실패"));
            bool ok = HideoutService.TryEnter(profiles, flow, catalog, M5TestData.Defaults(), 3,
                () => throw new InvalidOperationException("테스트 생성 실패"), out _, out HideoutService service);
            Assert.IsFalse(ok);
            Assert.IsNull(service);
            Assert.IsFalse(profiles.IsHideoutEntered(3));
        }

        [Test]
        public void TryEnter_Ok_ServiceAndReport()
        {
            ToHideoutScene();
            bool ok = HideoutService.TryEnter(profiles, flow, catalog, M5TestData.Defaults(), 4, CreateService,
                out SaveLoadReport report, out HideoutService service);
            Assert.IsTrue(ok);
            Assert.AreEqual(SaveLoadStatus.NewProfile, report.Status);
            Assert.IsNotNull(service);
            Assert.IsTrue(profiles.IsHideoutEntered(4));
        }

        [Test]
        public void ReturnToMainMenu_FlowRejects_LeavingReleased()
        {
            flow.OnSceneLoaded(SceneNames.MainMenu);   // Phase MainMenu → LeaveHideout 거부
            Enter();
            HideoutService service = CreateService();
            Assert.IsFalse(service.ReturnToMainMenu());
            Assert.IsFalse(service.IsLeaving);
        }

        // ── 디버그 ────────────────────────────────────────────

        [Test]
        public void Debug_AddCasings_ResetKit()
        {
            HideoutService service = InHideout();
            service.DebugAddCasings(1000);
            Assert.AreEqual(M5TestData.StartingCasings + 1000, service.Casings);
            service.TryClaimKit();
            Assert.IsFalse(service.IsKitAvailable);
            service.DebugResetKitCooldown();
            Assert.IsTrue(service.IsKitAvailable);
            Assert.AreEqual(3, changedCount);
        }

        // ── AC1 지속성 (4단계 보충): 키트·거래 결과가 메뉴 복귀 저장(W2) → 다음 실행에서 그대로, 쿨다운은 실제 시간으로 이어진다 ──

        [Test]
        public void KitAndTrades_SavedOnMainMenu_NextRun_Restored_CooldownContinues()
        {
            HideoutService service = InHideout();
            Assert.AreEqual(KitClaimResult.Ok, service.TryClaimKit());
            Assert.AreEqual(TradeResult.Ok, service.TryBuy(catalog.Get("ITM-MAP-01")));
            ItemInstance soldMag = service.Stash.Items.First(p => p.Item.Definition.id == "MAG-9MM-12").Item;
            Assert.AreEqual(TradeResult.Ok, service.TrySell(soldMag));
            int casings = M5TestData.StartingCasings - 100 + 48;   // 지도 매수 100, 9mm 12발 탄창 매도 floor(120 × 0.4)
            Assert.AreEqual(casings, service.Casings);
            Assert.IsTrue(service.ReturnToMainMenu(), "W2: 저장 → 메인 메뉴");
            Assert.IsFalse(storage.LastWritten().raidInProgress);

            // 다음 실행: 같은 저장소(파일 내용)를 새 ProfileService·GameFlow가 읽는다. 10분 지남.
            clock.Advance(600);
            profiles = new ProfileService(storage, clock.Now);
            rec = new FlowRecorder();
            flow = rec.Create(profiles);
            HideoutService next = InHideout();

            Assert.AreEqual(casings, next.Casings, "탄피 유지");
            CollectionAssert.AreEquivalent(new[] { "ITM-MAP-01", "MAG-9MM-12", "MED-BND-01", "WPN-PST-01" },
                next.Stash.Items.Select(p => p.Item.Definition.id).ToArray(), "보관함 유지 (판 탄창 없음)");
            Assert.IsFalse(next.IsKitAvailable, "쿨다운이 다음 실행에도 남는다");
            Assert.AreEqual(600f, next.KitCooldownRemainingSec, 1f, "남은 시간 = 1200 − 600");
            Assert.AreEqual(KitClaimResult.OnCooldown, next.TryClaimKit());
            clock.Advance(600);
            Assert.IsTrue(next.IsKitAvailable, "20분 뒤 다시 받을 수 있다");
            Assert.AreEqual(KitClaimResult.Ok, next.TryClaimKit());
        }

        // ── 루프 1회 (AC3 축약) ───────────────────────────────

        [Test]
        public void Loop_Kit_Buy_Raid_Extract_Return()
        {
            HideoutService service = InHideout();
            Assert.AreEqual(KitClaimResult.Ok, service.TryClaimKit());
            Assert.AreEqual(TradeResult.Ok, service.TryBuy(catalog.Get("WPN-RIF-01")));
            Assert.AreEqual(M5TestData.StartingCasings - 4000, service.Casings);
            Assert.AreEqual(5, service.Stash.Items.Count);

            // 장비로 옮김 (보관함에서 빼서 가짜 인벤토리 장착 — 실제 이동은 Inventory 몫)
            ItemInstance rifle = service.Stash.Items.First(p => p.Item.Definition.id == "WPN-RIF-01").Item;
            profiles.Profile.Stash.Remove(rifle);
            inventory.Equipped[EquipSlot.Primary1] = rifle;

            Assert.IsTrue(service.StartRaid());
            flow.OnSceneLoaded(SceneNames.RaidCrimsonBasin);
            Assert.IsTrue(flow.TryGetRaidLoadout(SceneNames.RaidCrimsonBasin, out Loadout brought));
            Assert.AreSame(rifle, brought.Entries[0].Item);

            Assert.IsTrue(flow.CompleteRaid(M5TestData.Extracted(brought)));
            Enter();
            flow.OnSceneLoaded(SceneNames.Hideout);
            HideoutService next = CreateService();
            Assert.AreSame(rifle, profiles.Profile.Loadout.Entries[0].Item);
            Assert.AreEqual(4, next.Stash.Items.Count);
            Assert.AreEqual(M5TestData.StartingCasings - 4000, next.Casings);
            Assert.IsFalse(next.IsLeaving);
            Assert.IsFalse(storage.LastWritten().raidInProgress);
        }
    }
}
