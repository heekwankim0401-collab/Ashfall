#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ashfall.Core;
using Ashfall.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Ashfall.Tests.PlayMode
{
    // M5 AC2 — RULE-03 강제 종료 처리 (§3, §13.3, M5 설계 §1.4·§7 CrashRecoveryFlowTests).
    // 은신처에서 장비 구성 → [레이드 시작] → save.json에 raidInProgress=true + 반입 기록 → 레이드 중 쓰기 없음
    // → "다음 실행" 흉내(흐름 레이드 표 비움 + 같은 폴더의 새 FileSaveStorage = 메모리 프로필 버림) → Boot → MainMenu → [은신처]
    // → 반입 삭제·알림 패널·raidInProgress=false 저장, 보관함·탄피·키트 쿨다운 그대로.
    public class CrashRecoveryFlowTests
    {
        private FileSaveStorage storage;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Assert.IsNotNull(GameManager.Instance, "GameManager 없음");
            M5Flow.ResetFlow();
            storage = SaveSandbox.UseFreshStorage("crash-recovery");
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return M5Flow.TearDown("after-crash-recovery");
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator RaidInProgress_NextRun_FirstHideoutEntry_DeletesLoadout_NoticeShown_FlagCleared()
        {
            GameManager gm = GameManager.Instance;
            using (var keyboard = new SimKeyboard())
            using (var pointer = new UiPointer())
            {
                // ── 은신처: 키트·지도 → 장비 ──
                var menu = new M4Menu();
                yield return M4Raid.LoadScene(SceneNames.MainMenu);
                yield return M4Menu.Attach(menu);
                yield return pointer.Click(menu.Start);
                var hideout = new M5Hideout();
                yield return M5Hideout.Attach(hideout, 60f);
                yield return hideout.ClickKit(pointer);
                yield return hideout.Buy(pointer, "ITM-MAP-01");
                yield return hideout.Buy(pointer, "RIG-01");
                yield return hideout.SelectTab(pointer, HideoutTab.Loadout);
                hideout.Equip("WPN-PST-01", EquipSlot.Secondary);
                hideout.Equip("RIG-01", EquipSlot.TacticalVest);
                hideout.QuickMove("ITM-MAP-01");
                hideout.QuickMove("MED-BND-01");
                hideout.QuickMove("MAG-9MM-12");
                string[] brought = M5Hideout.SortedIds(Loadout.Capture(hideout.Inventory).Entries.Select(e => e.Item));
                CollectionAssert.AreEqual(new[] { "ITM-MAP-01", "MAG-9MM-12", "MED-BND-01", "RIG-01", "WPN-PST-01" }, brought);
                string[] stashBefore = hideout.StashIds();
                CollectionAssert.AreEqual(new[] { "MAG-9MM-12" }, stashBefore);
                int casings = hideout.Service.Casings;
                Assert.AreEqual(6000 - 100 - 600, casings);

                // ── [레이드 시작] = 레이드 진입 기록 (W4) ──
                var raid = new M4Raid();
                yield return hideout.StartRaid(pointer, raid);
                SaveData atRaid = M5Save.Read(storage);
                Assert.IsTrue(atRaid.raidInProgress, "§13.3 레이드 진입 시 raidInProgress=true");
                CollectionAssert.AreEqual(brought, M5Save.SortedIds(atRaid.loadout), "파일에 반입 기록");
                CollectionAssert.AreEqual(stashBefore, M5Save.SortedIds(atRaid.stash));
                Assert.AreEqual(casings, atRaid.casings);
                Assert.Greater(atRaid.kitLastClaimUtcTicks, 0L, "키트 수령 시각 기록");
                Assert.IsFalse(File.Exists(storage.TempPath), "임시 파일이 남지 않는다");
                string snapshot = M5Save.Snapshot(storage);

                // ── 레이드 중: 루트·일시정지·시간 경과 — 아무것도 쓰지 않는다 (RULE-03) ──
                yield return keyboard.Tap(Key.Escape);
                yield return null;
                Assert.AreEqual(ScreenKind.Pause, raid.Presenter.Screens.Current);
                yield return keyboard.Tap(Key.Escape);
                Assert.AreEqual(ScreenKind.None, raid.Presenter.Screens.Current);
                var taken = new List<ItemInstance>();
                yield return M5Flow.TakeCrateLoot(raid, taken);
                yield return M2Scene.WaitSeconds(2f);
                Assert.AreEqual(snapshot, M5Save.Snapshot(storage), "레이드 중 세이브 폴더 변화 (RULE-03 위반)");
                Assert.IsFalse(raid.Session.IsOver);

                // ── 강제 종료 → 다음 실행 흉내 ──
                // 새 프로세스는 GameFlow가 새것(흐름 레이드 표 없음)이고 프로필을 아직 읽지 않았다.
                M5Flow.ResetFlow();
                Assert.IsFalse(gm.Flow.HasRaidInFlow);
                gm.UseSaveStorage(new FileSaveStorage(storage.DirectoryPath));
                Assert.IsFalse(gm.Profiles.IsLoaded, "다음 실행: 프로필 미로드");
                SceneManager.LoadScene(SceneNames.Boot);
                yield return M3Scene.WaitUntil(() => SceneManager.GetActiveScene().name == SceneNames.MainMenu, 30f, () => "Boot → MainMenu");
                yield return M4Menu.Attach(menu);
                Assert.IsFalse(gm.Profiles.IsLoaded, "메인 메뉴는 프로필을 읽지 않는다 (Q5)");
                Assert.AreEqual(snapshot, M5Save.Snapshot(storage), "은신처 전에는 아무 일도 없다");

                // ── 첫 은신처 진입: RULE-03 감지 ──
                yield return pointer.Click(menu.Start);
                hideout = new M5Hideout();
                yield return M5Hideout.Attach(hideout, 60f);
                SaveLoadReport report = hideout.Controller.EntryReport;
                Assert.AreEqual(SaveLoadStatus.Loaded, report.Status);
                Assert.IsTrue(report.CrashPenaltyApplied, "RULE-03 처리");
                CollectionAssert.AreEqual(brought, M5Hideout.SortedIds(report.CrashLost), "잃은 목록 = 반입");
                Assert.IsNull(gm.Flow.PendingResult, "결과 없음 (레이드가 끝나지 않았다)");

                // 알림 패널: 문구 + 잃은 장비 묶음
                Assert.AreEqual(HideoutPanel.Notice, hideout.Presenter.Panel, "알림이 먼저");
                Assert.IsTrue(hideout.Shown(HideoutNoticeView.PanelName), "알림 패널 표시");
                Assert.IsFalse(hideout.Shown(HideoutHubView.HubName), "허브 숨김");
                Assert.IsFalse(hideout.Shown(RaidResultView.PanelName), "결과 패널 숨김");
                Assert.That(hideout.Text(HideoutNoticeView.TextName), Does.Contain(UiText.NoticeCrash));
                Assert.IsTrue(hideout.Shown(HideoutNoticeView.ListSectionName), "잃은 장비 목록 표시");
                M5Hideout.AssertItemLines(hideout.Lines(HideoutNoticeView.ListName), report.CrashLost);

                // 장비 비어 있음, 보관함·탄피·키트 쿨다운 그대로, 크래시 전 루트도 없음
                Assert.IsTrue(Loadout.Capture(hideout.Inventory).IsEmpty, "RULE-03: 반입 장비 삭제");
                Assert.IsTrue(gm.Profiles.Profile.Loadout.IsEmpty);
                CollectionAssert.AreEqual(stashBefore, hideout.StashIds(), "보관함 그대로");
                Assert.AreEqual(casings, hideout.Service.Casings, "탄피 그대로");
                Assert.IsFalse(hideout.Service.IsKitAvailable, "키트 쿨다운 그대로");
                Assert.IsFalse(hideout.StashItems().Contains(taken[0]), "레이드 중 루트는 사라진다");

                // W1: flag false·loadout 빈 배열 저장
                SaveData after = M5Save.Read(storage);
                Assert.IsFalse(after.raidInProgress, "W1: raidInProgress=false");
                Assert.AreEqual(0, after.loadout.Count, "save.json loadout 빈 배열");
                CollectionAssert.AreEqual(stashBefore, M5Save.SortedIds(after.stash));
                Assert.AreEqual(casings, after.casings);
                Assert.AreEqual(atRaid.kitLastClaimUtcTicks, after.kitLastClaimUtcTicks, "키트 시각 그대로");
                Assert.IsTrue(File.Exists(storage.BackupPath), "직전 세대(.bak) 보존");

                // [확인] → 허브 (결과 패널 없음)
                yield return hideout.ConfirmNotice(pointer);
                Assert.AreEqual(HideoutPanel.Hub, hideout.Presenter.Panel);
                Assert.IsTrue(hideout.Shown(HideoutHubView.HubName));

                // 한 번 더 "다음 실행": 이미 처리됐으므로 알림 없음 (멱등 — 두 번 지우지 않는다)
                yield return M4Raid.LoadScene(SceneNames.MainMenu);   // 은신처를 먼저 내리고(프로필을 쥔 객체 정리) 저장소를 바꾼다
                M5Flow.ResetFlow();
                gm.UseSaveStorage(new FileSaveStorage(storage.DirectoryPath));
                yield return M4Menu.Attach(menu);
                yield return pointer.Click(menu.Start);
                hideout = new M5Hideout();
                yield return M5Hideout.Attach(hideout, 60f);
                Assert.IsFalse(hideout.Controller.EntryReport.CrashPenaltyApplied, "처리 뒤 다음 실행은 알림 없음");
                Assert.AreEqual(HideoutPanel.Hub, hideout.Presenter.Panel);
                CollectionAssert.AreEqual(stashBefore, hideout.StashIds());
            }
        }
    }
}
#endif
