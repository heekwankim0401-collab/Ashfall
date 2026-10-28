#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ashfall.AI;
using Ashfall.Core;
using Ashfall.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Ashfall.Tests.PlayMode
{
    // M5 AC3 — 은신처 ↔ 레이드 전체 루프 (§2, §9, §13.3, RULE-01·02·03, M5 설계 §1.2·§1.3·§7 HideoutLoopFlowTests).
    // 실제 흐름: Boot → MainMenu [은신처] → Hideout(새 프로필) → [부랑자 키트 받기]·[상인] 구매(UI Toolkit 버튼, UiPointer)
    //           → 장비로 옮김(IInventoryService 명령 — 실제 마우스 드래그는 사람 확인) → [레이드 시작] → Raid_CrimsonBasin
    //           → 상자 루트 → EXT-01 탈출 → 종료 오버레이 → Hideout 결과 패널 → [확인] → 장비 = 반출품, save.json 갱신.
    // M4 RaidLoopFlowTests(삭제)가 보던 결과 문구·MIA·일시정지 포기 경로도 여기서 은신처 결과 패널로 본다.
    // 세이브는 테스트마다 SaveSandbox 새 폴더(새 프로필 = 새 실행).
    public class HideoutLoopFlowTests
    {
        private FileSaveStorage storage;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Assert.IsNotNull(GameManager.Instance, "GameManager 없음");
            M5Flow.ResetFlow();
            storage = SaveSandbox.UseFreshStorage("hideout-loop");
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return M5Flow.TearDown("after-hideout-loop");
        }

        // ① 탈출 루프 → ② 사망 루프 → ④ 메인 메뉴 저장·재진입. 구독 누수 없음.
        [UnityTest]
        [Timeout(420000)]
        public IEnumerator BootMenuHideout_KitTradeLoadout_RaidLootExtract_ThenDeath_ThenMenuReentry()
        {
            var menu = new M4Menu();
            SceneManager.LoadScene(SceneNames.Boot);
            yield return M3Scene.WaitUntil(() => SceneManager.GetActiveScene().name == SceneNames.MainMenu, 15f, () => "Boot → MainMenu");
            yield return M4Menu.Attach(menu);
            GameManager gm = GameManager.Instance;
            Assert.AreEqual(GamePhase.MainMenu, gm.Flow.Phase);
            Assert.IsFalse(gm.Profiles.IsLoaded, "메인 메뉴는 프로필을 읽지 않는다 (Q5)");
            Assert.IsFalse(File.Exists(storage.FilePath));
            Dictionary<string, int> baseline = M5Flow.SubscriberCounts(gm.Events);

            using (var pointer = new UiPointer())
            {
                // ── 메인 메뉴 [은신처] → 새 프로필 ──
                yield return pointer.Click(menu.Start);
                Assert.AreNotEqual(UiClickPath.None, pointer.LastPath, "[은신처]가 눌리지 않았다");
                var hideout = new M5Hideout();
                yield return M5Hideout.Attach(hideout, 60f);
                Assert.AreEqual(HideoutPanel.Hub, hideout.Presenter.Panel, "새 프로필 → 알림 없이 허브");
                Assert.AreEqual(6000, hideout.Service.Casings, "Q9 시작 탄피");
                Assert.AreEqual(UiText.Casings(6000), hideout.Text(HideoutHubView.CasingsName));
                SaveData entry = M5Save.Read(storage);
                Assert.IsFalse(entry.raidInProgress);
                Assert.AreEqual(0, entry.stash.Count);

                // ── 부랑자 키트 + 상인 구매 (UI 버튼) ──
                yield return hideout.ClickKit(pointer);
                CollectionAssert.AreEqual(new[] { "MAG-9MM-12", "MAG-9MM-12", "MED-BND-01", "WPN-PST-01" }, hideout.StashIds(), "키트 → 보관함");
                foreach (string id in new[] { "WPN-RIF-01", "MAG-556-30", "RIG-01", "ITM-MAP-01" })
                    yield return hideout.Buy(pointer, id);
                Assert.AreEqual(6000 - 4000 - 400 - 600 - 100, hideout.Service.Casings, "§4.6 가격으로 차감");
                Assert.AreEqual(UiText.Casings(900), hideout.Text(HideoutHubView.CasingsName), "탄피 문구 갱신");
                Assert.AreEqual(8, hideout.StashItems().Count);

                // ── [장비] 탭: 보관함 → 장비 ──
                yield return hideout.SelectTab(pointer, HideoutTab.Loadout);
                hideout.Equip("WPN-RIF-01", EquipSlot.Primary1);
                hideout.Equip("RIG-01", EquipSlot.TacticalVest);
                ItemInstance mag = hideout.QuickMove("MAG-556-30");
                hideout.QuickMove("ITM-MAP-01");
                ItemInstance bandage = hideout.QuickMove("MED-BND-01");
                Assert.IsTrue(hideout.Inventory.TryAssignQuickSlot(0, bandage), "붕대 퀵슬롯 4");
                RotateInVest(hideout, mag);
                yield return M5Hideout.Frames(2);
                CollectionAssert.AreEqual(new[] { "MAG-9MM-12", "MAG-9MM-12", "WPN-PST-01" }, hideout.StashIds(), "보관함 = 키트 나머지");
                string[] stashBeforeRaid = hideout.StashIds();
                string[] loadoutBeforeRaid = M5Hideout.Describe(hideout.Inventory);
                string[] broughtIds = { "ITM-MAP-01", "MAG-556-30", "MED-BND-01", "RIG-01", "WPN-RIF-01" };
                CollectionAssert.AreEqual(broughtIds, M5Hideout.SortedIds(Loadout.Capture(hideout.Inventory).Entries.Select(e => e.Item)));
                Assert.AreEqual(0, M5Save.Read(storage).loadout.Count, "장비 편집은 메모리에서만 (Q7)");

                // ── ① [레이드 시작] → 반입 = 그 장비, save.json raidInProgress=true ──
                var raid = new M4Raid();
                yield return hideout.StartRaid(pointer, raid);
                SaveData atRaid = M5Save.Read(storage);
                Assert.IsTrue(atRaid.raidInProgress, "§13.3 레이드 진입 시 raidInProgress=true");
                CollectionAssert.AreEqual(broughtIds, M5Save.SortedIds(atRaid.loadout), "파일에 반입 장비 기록 (RULE-03 삭제 대상)");
                CollectionAssert.AreEqual(stashBeforeRaid, M5Save.SortedIds(atRaid.stash), "보관함은 레이드에 가지 않는다");
                Assert.AreEqual(900, atRaid.casings);
                CollectionAssert.AreEqual(broughtIds, M5Hideout.SortedIds(raid.Carried()), "레이드 소지품 = 반입 (startingItems 아님)");
                CollectionAssert.AreEqual(broughtIds, M5Hideout.SortedIds(raid.Controller.BroughtIn), "반입 기록");
                CollectionAssert.AreEqual(loadoutBeforeRaid, M5Hideout.Describe(raid.Inventory), "반입 위치·회전·퀵슬롯 그대로");
                Assert.IsTrue(gm.Profiles.Profile.Loadout.IsEmpty, "메모리 은신처 장비는 레이드 동안 비어 있다");

                // 상자 루트 획득 → EXT-01
                var taken = new List<ItemInstance>();
                yield return M5Flow.TakeCrateLoot(raid, taken);
                ItemInstance picked = taken[0];
                string[] raidEndLoadout = M5Hideout.Describe(raid.Inventory);
                RaidResult extracted;
                using (var ended = new EventLog<RaidEnded>(raid.Events))
                {
                    yield return raid.EnterZone("EXT-01");
                    yield return M3Scene.WaitUntil(() => ended.Count > 0, 20f, () => "EXT-01 탈출");
                    yield return AssertEndOverlay(raid, "탈출 성공");
                    extracted = raid.Controller.LastResult;
                }
                Assert.AreEqual(RaidOutcome.Extracted, extracted.Outcome);
                Assert.IsNotNull(extracted.CarriedOutLoadout, "탈출 → 반출 Loadout");
                CollectionAssert.Contains(extracted.CarriedOut, picked, "획득품 반출");
                Assert.AreEqual(0, extracted.Lost.Count);
                Assert.AreEqual(atRaid.loadout.Count, M5Save.Read(storage).loadout.Count, "레이드 중 세이브 쓰기 없음");
                Assert.IsTrue(M5Save.Read(storage).raidInProgress, "레이드 종료 ~ 은신처 진입 전에는 파일이 그대로 (RULE-03 경계 Q31)");

                // ── 은신처: 결과 패널 → [확인] → 장비 = 반출품, save.json 갱신 ──
                hideout = new M5Hideout();
                yield return M5Hideout.Attach(hideout);
                Assert.AreSame(extracted, gm.Flow.PendingResult);
                hideout.AssertResultPanel(extracted, expectCarriedOut: true);
                Assert.AreEqual("탈출 성공 — 남쪽 도로", hideout.Text(RaidResultView.TitleName));
                CollectionAssert.Contains(hideout.Lines(RaidResultView.CarriedOutName).Select(M5Hideout.StripCount).ToList(),
                    RaidResultViewModel.ItemName(picked.Definition), "반출 목록에 획득품");
                yield return hideout.ConfirmResult(pointer);
                CollectionAssert.AreEqual(raidEndLoadout, M5Hideout.Describe(hideout.Inventory), "RULE-01: 장비 = 반출품(위치·회전·퀵슬롯 그대로)");
                CollectionAssert.IsSubsetOf(loadoutBeforeRaid, raidEndLoadout, "반입 장비가 그 자리에 돌아왔다");
                CollectionAssert.AreEqual(stashBeforeRaid, hideout.StashIds(), "보관함은 레이드 전과 같다");
                Assert.AreEqual(900, hideout.Service.Casings);
                SaveData afterExtract = M5Save.Read(storage);
                Assert.IsFalse(afterExtract.raidInProgress, "W1: raidInProgress=false");
                CollectionAssert.Contains(afterExtract.loadout.Select(r => r.id).ToList(), picked.Definition.id, "save.json 장비에 획득품");
                Assert.AreEqual(raidEndLoadout.Count(l => !l.StartsWith("quick")), afterExtract.loadout.Count, "save.json 장비 = 반출품");
                CollectionAssert.AreEqual(stashBeforeRaid, M5Save.SortedIds(afterExtract.stash));
                AssertNoLeak(gm, baseline, "탈출 루프 후 (은신처)");

                // ── ② 다시 [레이드 시작] → 머리 사망 → 장비 소실(RULE-01), 보관함 그대로 ──
                string[] secondBrought = M5Hideout.SortedIds(Loadout.Capture(hideout.Inventory).Entries.Select(e => e.Item));
                raid = new M4Raid();
                yield return hideout.StartRaid(pointer, raid);
                Assert.IsFalse(raid.Health.IsDead, "새 몸 (Q18 체력 이월 없음)");
                CollectionAssert.AreEqual(secondBrought, M5Hideout.SortedIds(raid.Controller.BroughtIn), "2회차 반입 = 반출품");
                Assert.IsTrue(M5Save.Read(storage).raidInProgress);
                RaidResult death;
                using (var ended = new EventLog<RaidEnded>(raid.Events))
                {
                    raid.ShootHead();
                    yield return M3Scene.WaitUntil(() => ended.Count > 0, 5f, () => "사망 → RaidEnded");
                    yield return AssertEndOverlay(raid, "사망");
                    death = raid.Controller.LastResult;
                }
                Assert.AreEqual(RaidOutcome.Killed, death.Outcome);
                Assert.AreEqual(BodyPart.Head, death.DeathCause, "머리 피격 사망");
                Assert.IsNull(death.CarriedOutLoadout, "RULE-01: 사망 반출 없음");
                CollectionAssert.AreEquivalent(secondBrought, M5Hideout.SortedIds(death.Lost), "반입 전부 소실");

                hideout = new M5Hideout();
                yield return M5Hideout.Attach(hideout);
                hideout.AssertResultPanel(death, expectCarriedOut: false);
                Assert.That(hideout.Text(RaidResultView.TitleName), Does.StartWith("사망").And.Contain("머리"));
                yield return hideout.ConfirmResult(pointer);
                Assert.IsTrue(Loadout.Capture(hideout.Inventory).IsEmpty, "RULE-01: 사망 → 장비 비어 있음");
                Assert.IsTrue(gm.Profiles.Profile.Loadout.IsEmpty);
                CollectionAssert.AreEqual(stashBeforeRaid, hideout.StashIds(), "보관함 그대로");
                SaveData afterDeath = M5Save.Read(storage);
                Assert.IsFalse(afterDeath.raidInProgress);
                Assert.AreEqual(0, afterDeath.loadout.Count, "save.json loadout 빈 배열");
                CollectionAssert.AreEqual(stashBeforeRaid, M5Save.SortedIds(afterDeath.stash));
                Assert.AreEqual(900, afterDeath.casings);

                // ── ④ [메인 메뉴] → 저장(W2) → 다시 [은신처] = 같은 상태 (같은 실행은 다시 읽지 않는다) ──
                // 장비 하나를 바꿔 W2가 Capture를 저장하는지 본다
                hideout.Equip("WPN-PST-01", EquipSlot.Secondary);
                yield return pointer.Click(hideout.ButtonNamed(HideoutHubView.MainMenuName));
                Assert.AreNotEqual(UiClickPath.None, pointer.LastPath, "[메인 메뉴]가 눌리지 않았다");
                yield return M3Scene.WaitUntil(() => SceneManager.GetActiveScene().name == SceneNames.MainMenu, 30f, () => "Hideout → MainMenu");
                yield return M4Menu.Attach(menu);
                Assert.AreEqual(GamePhase.MainMenu, gm.Flow.Phase);
                SaveData atMenu = M5Save.Read(storage);
                CollectionAssert.AreEqual(new[] { "WPN-PST-01" }, M5Save.SortedIds(atMenu.loadout), "W2: 장비 저장");
                CollectionAssert.AreEqual(new[] { "MAG-9MM-12", "MAG-9MM-12" }, M5Save.SortedIds(atMenu.stash));
                AssertNoLeak(gm, baseline, "은신처 → 메인 메뉴");

                // 같은 실행에서는 파일을 다시 읽지 않는다: 파일을 망가뜨려도 손상 알림 없이 메모리 상태 그대로, W1이 덮어쓴다
                File.WriteAllText(storage.FilePath, "not json");
                yield return pointer.Click(menu.Start);
                hideout = new M5Hideout();
                yield return M5Hideout.Attach(hideout, 60f);
                Assert.AreEqual(HideoutPanel.Hub, hideout.Presenter.Panel, "재진입 → 알림 없음");
                Assert.IsFalse(File.Exists(new FileSaveStorage(storage.DirectoryPath).CorruptPath), "재진입은 읽지 않으므로 격리도 없다");
                CollectionAssert.AreEqual(new[] { "WPN-PST-01" },
                    M5Hideout.SortedIds(Loadout.Capture(hideout.Inventory).Entries.Select(e => e.Item)), "장비 그대로");
                CollectionAssert.AreEqual(new[] { "MAG-9MM-12", "MAG-9MM-12" }, hideout.StashIds());
                Assert.AreEqual(900, hideout.Service.Casings);
                Assert.IsFalse(hideout.Service.IsKitAvailable, "키트 쿨다운 유지");
                SaveData reentered = M5Save.Read(storage);
                CollectionAssert.AreEqual(new[] { "WPN-PST-01" }, M5Save.SortedIds(reentered.loadout), "W1이 메모리 상태로 덮어씀");

                yield return pointer.Click(hideout.ButtonNamed(HideoutHubView.MainMenuName));
                yield return M3Scene.WaitUntil(() => SceneManager.GetActiveScene().name == SceneNames.MainMenu, 30f, () => "Hideout → MainMenu (2)");
                yield return M4Menu.Attach(menu);
                AssertNoLeak(gm, baseline, "루프 전체 후");
                Assert.IsFalse(gm.Services.TryGet(out IHideoutService _), "은신처 서비스 해지");
                Assert.IsFalse(gm.Services.TryGet(out IInventoryService _), "인벤토리 서비스 해지");
                Debug.Log("[HideoutLoopFlowTests] 클릭 경로: " + string.Join(" | ", UiPointer.Attempts));
            }
        }

        // ③ MIA (RULE-02): 결과 "작전 중 실종 (MIA)", 장비 소실, 보관함 그대로
        [UnityTest]
        [Timeout(240000)]
        public IEnumerator HideoutToRaid_MissingInAction_LoadoutLost_StashKept()
        {
            using (var pointer = new UiPointer())
            {
                var hideout = new M5Hideout();
                yield return EnterHideoutFromMenu(pointer, hideout);
                string[] stashBefore = PrepareKitLoadout(hideout, out string[] brought);
                var raid = new M4Raid();
                yield return hideout.StartRaid(pointer, raid);
                CollectionAssert.AreEqual(brought, M5Hideout.SortedIds(raid.Carried()), "반입 = 은신처 장비");

                raid.Controller.AdvanceTime(raid.Session.RemainingSec + 1f);
                yield return M3Scene.WaitUntil(() => raid.Session.IsOver, 5f, () => "MIA");
                yield return AssertEndOverlay(raid, "작전 중 실종");
                RaidResult result = raid.Controller.LastResult;
                Assert.AreEqual(RaidOutcome.MissingInAction, result.Outcome);
                Assert.AreEqual(0, result.CarriedOut.Count, "RULE-01/02: MIA 반출 0");
                CollectionAssert.AreEquivalent(brought, M5Hideout.SortedIds(result.Lost));

                hideout = new M5Hideout();
                yield return M5Hideout.Attach(hideout);
                hideout.AssertResultPanel(result, expectCarriedOut: false);
                Assert.AreEqual("작전 중 실종 (MIA)", hideout.Text(RaidResultView.TitleName));
                Assert.AreEqual("레이드 시간 25:00", hideout.Text(RaidResultView.DurationName));
                yield return hideout.ConfirmResult(pointer);
                AssertLostAndSaved(hideout, stashBefore);
            }
        }

        // ③ ESC 일시정지(세계·타이머·적 정지) → [계속] → 재개 → ESC → [레이드 포기] → [포기] → 결과 "레이드 포기", 장비 소실
        [UnityTest]
        [Timeout(240000)]
        public IEnumerator HideoutToRaid_EscPauseFreezesWorld_ResumeButton_ThenAbandon_LoadoutLost()
        {
            using (var keyboard = new SimKeyboard())
            using (var pointer = new UiPointer())
            {
                var hideout = new M5Hideout();
                yield return EnterHideoutFromMenu(pointer, hideout);
                string[] stashBefore = PrepareKitLoadout(hideout, out string[] brought);
                var raid = new M4Raid();
                yield return hideout.StartRaid(pointer, raid, keepEnemies: true);
                GameFlow flow = raid.Flow;
                yield return M2Scene.WaitSeconds(1f);   // 적 순찰 진행

                // Esc → Pause, 세계 정지
                yield return keyboard.Tap(Key.Escape);
                yield return null;
                Assert.AreEqual(ScreenKind.Pause, raid.Presenter.Screens.Current, "Esc → 일시정지");
                Assert.AreEqual(0f, Time.timeScale, "일시정지 = timeScale 0");
                Assert.IsTrue(flow.IsWorldPaused);
                Assert.IsTrue(UiQuery.IsShown(raid.UiRoot.Q(PauseScreenView.ScreenName)), "일시정지 화면 표시");
                Assert.AreNotEqual(CursorLockMode.Locked, UnityEngine.Cursor.lockState, "일시정지 중 커서 해제");
                float elapsedAtPause = raid.Session.ElapsedSec;
                Vector3[] enemyPositions = raid.Enemies.Select(e => e.transform.position).ToArray();
                EnemyState[] enemyStates = raid.Enemies.Select(e => e.State).ToArray();
                float realStart = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - realStart < 1.5f)
                    yield return null;
                Assert.AreEqual(elapsedAtPause, raid.Session.ElapsedSec, "일시정지 중 레이드 타이머 정지 (RULE-02)");
                for (int i = 0; i < raid.Enemies.Count; i++)
                {
                    Assert.AreEqual(0f, Vector3.Distance(enemyPositions[i], raid.Enemies[i].transform.position), 1e-4f,
                        $"{raid.Enemies[i].name}: 일시정지 중 이동");
                    Assert.AreEqual(enemyStates[i], raid.Enemies[i].State, $"{raid.Enemies[i].name}: 일시정지 중 상태 변화");
                }
                Assert.IsTrue(M5Save.Read(storage).raidInProgress, "일시정지는 저장하지 않는다");

                // Pause 중 Tab·M 무시
                yield return keyboard.Tap(Key.Tab);
                yield return keyboard.Tap(Key.M);
                Assert.AreEqual(ScreenKind.Pause, raid.Presenter.Screens.Current, "Pause 중 Tab·M 무시");

                // [계속] — timeScale 0에서 UI Toolkit 버튼
                yield return pointer.Click(raid.UiRoot.Q<Button>(PauseScreenView.ResumeName));
                UiClickPath resumePath = pointer.LastPath;
                Assert.AreNotEqual(UiClickPath.None, resumePath, "[계속] 버튼이 눌리지 않았다");
                Assert.AreEqual(ScreenKind.None, raid.Presenter.Screens.Current, "[계속] → 닫힘");
                Assert.AreEqual(1f, Time.timeScale, "재개");
                Assert.IsFalse(flow.IsWorldPaused);
                yield return M2Scene.WaitSeconds(0.5f);
                Assert.Greater(raid.Session.ElapsedSec, elapsedAtPause, "재개 후 타이머 진행");

                // Esc → Pause → Esc → 닫힘
                yield return keyboard.Tap(Key.Escape);
                Assert.AreEqual(ScreenKind.Pause, raid.Presenter.Screens.Current);
                yield return keyboard.Tap(Key.Escape);
                Assert.AreEqual(ScreenKind.None, raid.Presenter.Screens.Current, "Esc 다시 → 닫기");
                Assert.AreEqual(1f, Time.timeScale);

                // 포기: Esc → [레이드 포기] → 확인 [포기]
                yield return keyboard.Tap(Key.Escape);
                Assert.AreEqual(ScreenKind.Pause, raid.Presenter.Screens.Current);
                Assert.IsTrue(raid.Presenter.CanAbandon, "레이드 중 [레이드 포기] 가능");
                yield return null;
                using (var ended = new EventLog<RaidEnded>(raid.Events))
                {
                    yield return pointer.Click(raid.UiRoot.Q<Button>(PauseScreenView.AbandonName));
                    Assert.IsTrue(raid.Presenter.IsConfirmingAbandon, "[레이드 포기] → 확인 단계");
                    Assert.AreEqual(0, ended.Count, "확인 전에는 끝나지 않는다");
                    yield return null;
                    yield return pointer.Click(raid.UiRoot.Q<Button>(PauseScreenView.ConfirmYesName));
                    yield return M3Scene.WaitUntil(() => ended.Count > 0, 5f, () => "포기 → RaidEnded");
                    Assert.AreEqual(RaidOutcome.Abandoned, ended.Last.Outcome);
                }
                yield return AssertEndOverlay(raid, "레이드 포기");
                RaidResult result = raid.Controller.LastResult;
                Assert.AreEqual(0, result.CarriedOut.Count, "RULE-01(포기 = 사망과 같게): 반출 0");
                Assert.IsNull(result.CarriedOutLoadout);
                CollectionAssert.AreEquivalent(M5Hideout.SortedIds(raid.Carried()), M5Hideout.SortedIds(result.Lost));
                CollectionAssert.IsSubsetOf(brought, M5Hideout.SortedIds(result.Lost), "반입 전부 소실");

                hideout = new M5Hideout();
                yield return M5Hideout.Attach(hideout);
                hideout.AssertResultPanel(result, expectCarriedOut: false);
                Assert.AreEqual("레이드 포기", hideout.Text(RaidResultView.TitleName));
                yield return hideout.ConfirmResult(pointer);
                AssertLostAndSaved(hideout, stashBefore);
                Debug.Log($"[HideoutLoopFlowTests] 일시정지 [계속] 클릭 경로: {resumePath}");
            }
        }

        // DEV-RETURN: Esc → [귀환(개발)] → 확인 [귀환] → 탈출 성공과 같은 처리 — 결과 "탈출 성공 — 귀환(개발)",
        // 소지품(반입 + 획득) 그대로 은신처 장비, save.json 반영, 보관함 그대로 (에디터 = 개발 기능 켜짐)
        [UnityTest]
        [Timeout(240000)]
        public IEnumerator HideoutToRaid_EscPause_DevReturn_KeepsLoadout_AndSaves()
        {
            Assert.IsTrue(DevFeatures.IsDevReturnAvailable, "에디터에서는 개발용 귀환이 켜져 있다");
            using (var keyboard = new SimKeyboard())
            using (var pointer = new UiPointer())
            {
                var hideout = new M5Hideout();
                yield return EnterHideoutFromMenu(pointer, hideout);
                string[] stashBefore = PrepareKitLoadout(hideout, out string[] brought);
                var raid = new M4Raid();
                yield return hideout.StartRaid(pointer, raid);
                Assert.IsTrue(M5Save.Read(storage).raidInProgress);
                var taken = new List<ItemInstance>();
                yield return M5Flow.TakeCrateLoot(raid, taken);
                ItemInstance picked = taken[0];
                string[] raidEndLoadout = M5Hideout.Describe(raid.Inventory);
                // 상자를 열며 뜬 인벤토리 화면을 Esc로 닫는다
                if (raid.Presenter.Screens.Current != ScreenKind.None)
                    yield return keyboard.Tap(Key.Escape);
                Assert.AreEqual(ScreenKind.None, raid.Presenter.Screens.Current);

                // Esc → 일시정지: [귀환(개발)] 버튼이 보인다
                yield return keyboard.Tap(Key.Escape);
                yield return null;
                Assert.AreEqual(ScreenKind.Pause, raid.Presenter.Screens.Current);
                Assert.IsTrue(raid.Presenter.CanDevReturn);
                Button devReturn = raid.UiRoot.Q<Button>(PauseScreenView.DevReturnName);
                Assert.IsNotNull(devReturn, "개발 빌드·에디터: [귀환(개발)] 버튼");
                Assert.IsTrue(UiQuery.IsShown(devReturn));

                RaidResult result;
                using (var ended = new EventLog<RaidEnded>(raid.Events))
                {
                    yield return pointer.Click(devReturn);
                    Assert.IsTrue(raid.Presenter.IsConfirmingDevReturn, "[귀환(개발)] → 확인 단계");
                    Assert.AreEqual(UiText.DevReturnConfirm, raid.UiRoot.Q<Label>(PauseScreenView.ConfirmTextName).text);
                    Assert.AreEqual(0, ended.Count, "확인 전에는 끝나지 않는다");
                    yield return null;
                    yield return pointer.Click(raid.UiRoot.Q<Button>(PauseScreenView.ConfirmYesName));
                    yield return M3Scene.WaitUntil(() => ended.Count > 0, 5f, () => "귀환 → RaidEnded");
                    Assert.AreEqual(RaidOutcome.Extracted, ended.Last.Outcome);
                    Assert.AreEqual(DevFeatures.DevReturnLabel, ended.Last.ExtractionName);
                }
                yield return AssertEndOverlay(raid, "탈출 성공");
                result = raid.Controller.LastResult;
                Assert.IsTrue(raid.Session.IsDevReturn);
                Assert.IsTrue(result.Survived);
                Assert.AreEqual(DevFeatures.DevReturnExtractionId, result.ExtractionId);
                Assert.IsNotNull(result.CarriedOutLoadout, "탈출과 같게 반출 Loadout");
                CollectionAssert.Contains(result.CarriedOut, picked, "획득품 반출");
                Assert.AreEqual(0, result.Lost.Count, "잃는 것 없음");

                hideout = new M5Hideout();
                yield return M5Hideout.Attach(hideout);
                hideout.AssertResultPanel(result, expectCarriedOut: true);
                Assert.AreEqual("탈출 성공 — 귀환(개발)", hideout.Text(RaidResultView.TitleName));
                yield return hideout.ConfirmResult(pointer);
                CollectionAssert.AreEqual(raidEndLoadout, M5Hideout.Describe(hideout.Inventory), "장비 = 귀환 시점 소지품(위치 그대로)");
                CollectionAssert.IsSubsetOf(brought, M5Hideout.SortedIds(Loadout.Capture(hideout.Inventory).Entries.Select(e => e.Item)),
                    "반입 장비를 잃지 않았다");
                CollectionAssert.AreEqual(stashBefore, hideout.StashIds(), "보관함 그대로");
                SaveData saved = M5Save.Read(storage);
                Assert.IsFalse(saved.raidInProgress, "세이브 반영 (RULE-03: 은신처 진입 시 기록)");
                CollectionAssert.Contains(saved.loadout.Select(r => r.id).ToList(), picked.Definition.id, "save.json 장비에 획득품");
                Assert.AreEqual(raidEndLoadout.Count(l => !l.StartsWith("quick")), saved.loadout.Count, "save.json 장비 = 귀환 시점 소지품");
                CollectionAssert.AreEqual(stashBefore, M5Save.SortedIds(saved.stash));
            }
        }

        // ── 도우미 ──

        private IEnumerator EnterHideoutFromMenu(UiPointer pointer, M5Hideout hideout)
        {
            var menu = new M4Menu();
            yield return M4Raid.LoadScene(SceneNames.MainMenu);
            yield return M4Menu.Attach(menu);
            yield return pointer.Click(menu.Start);
            Assert.AreNotEqual(UiClickPath.None, pointer.LastPath, "[은신처]가 눌리지 않았다");
            yield return M5Hideout.Attach(hideout, 60f);
            Assert.AreEqual(HideoutPanel.Hub, hideout.Presenter.Panel);
        }

        // 키트를 받아 붕대·9mm 탄창 둘을 장비(주머니)로 옮긴다. 반환 = 레이드 전 보관함 id.
        private string[] PrepareKitLoadout(M5Hideout hideout, out string[] brought)
        {
            Assert.AreEqual(KitClaimResult.Ok, hideout.Service.TryClaimKit(), "키트 수령");
            hideout.QuickMove("MED-BND-01");
            hideout.QuickMove("MAG-9MM-12");
            hideout.QuickMove("MAG-9MM-12");
            brought = M5Hideout.SortedIds(Loadout.Capture(hideout.Inventory).Entries.Select(e => e.Item));
            CollectionAssert.AreEqual(new[] { "MAG-9MM-12", "MAG-9MM-12", "MED-BND-01" }, brought);
            return hideout.StashIds();
        }

        private void AssertLostAndSaved(M5Hideout hideout, string[] stashBefore)
        {
            Assert.IsTrue(Loadout.Capture(hideout.Inventory).IsEmpty, "RULE-01: 장비 비어 있음");
            CollectionAssert.AreEqual(stashBefore, hideout.StashIds(), "보관함 그대로");
            SaveData saved = M5Save.Read(storage);
            Assert.IsFalse(saved.raidInProgress);
            Assert.AreEqual(0, saved.loadout.Count, "save.json loadout 빈 배열");
            CollectionAssert.AreEqual(stashBefore, M5Save.SortedIds(saved.stash));
        }

        // 조끼 안 탄창을 회전 배치로 옮긴다 (반입·반출에서 회전이 보존되는지 보려고)
        private static void RotateInVest(M5Hideout hideout, ItemInstance mag)
        {
            IGridView vest = hideout.Inventory.StorageGrids.FirstOrDefault(g => g.Ref.Kind == GridKind.TacticalVest);
            Assert.IsNotNull(vest, "조끼 그리드 없음");
            Assert.IsTrue(hideout.Inventory.TryGetLocation(mag, out ItemLocation now));
            for (int y = 0; y < vest.Size.y; y++)
            for (int x = 0; x < vest.Size.x; x++)
            {
                var cell = new Vector2Int(x, y);
                if (now.Grid == vest.Ref && now.Cell == cell)
                    continue;
                if (hideout.Inventory.CanPlace(mag, vest.Ref, cell, true) && hideout.Inventory.TryMove(mag, vest.Ref, cell, true))
                    return;
            }
            Assert.Fail("조끼에 탄창을 회전해 놓을 자리가 없다");
        }

        // 종료 직후: RaidEnd 오버레이(문구), 세계 정지, Esc로 안 닫힘
        private static IEnumerator AssertEndOverlay(M4Raid raid, string expectedText)
        {
            yield return null;
            Assert.AreEqual(ScreenKind.RaidEnd, raid.Presenter.Screens.Current, "종료 오버레이");
            Assert.AreEqual(0f, Time.timeScale, "종료 연출 중 세계 정지");
            Label text = raid.UiRoot.Q<Label>(RaidEndOverlayView.TextName);
            Assert.IsTrue(UiQuery.IsShown(text), "오버레이 표시");
            Assert.AreEqual(expectedText, text.text);
            raid.Presenter.Cancel();
            Assert.AreEqual(ScreenKind.RaidEnd, raid.Presenter.Screens.Current, "종료 오버레이는 Esc로 닫히지 않는다");
            Assert.AreEqual(0f, Time.timeScale);
        }

        private static void AssertNoLeak(GameManager gm, Dictionary<string, int> baseline, string when)
        {
            // 은신처에 있는 동안은 은신처 객체의 구독이 더해진다 — 레이드 쪽 이벤트만 기준과 같아야 한다
            bool inHideout = SceneManager.GetActiveScene().name == SceneNames.Hideout;
            Dictionary<string, int> now = M5Flow.SubscriberCounts(gm.Events);
            foreach (KeyValuePair<string, int> kv in baseline)
            {
                if (inHideout && (kv.Key == nameof(InventoryChanged) || kv.Key == nameof(HideoutChanged) || kv.Key == nameof(EquipmentChanged)))
                    continue;
                Assert.AreEqual(kv.Value, now[kv.Key], $"{when}: {kv.Key} 구독 수 (누수)");
            }
        }
    }
}
#endif
