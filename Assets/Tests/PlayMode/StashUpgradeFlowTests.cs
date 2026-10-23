#if UNITY_EDITOR
using System;
using System.Collections;
using Ashfall.Core;
using Ashfall.Hideout;
using Ashfall.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Ashfall.Tests.PlayMode
{
    // MVP 마무리 F2 AC-F2 보관함 업그레이드 (§9, 설계 §1.3·§7, MF-Q5~Q9): 실제 은신처 씬에서 [보관함 확장](UI Toolkit 버튼, UiPointer)
    // → 탄피 차감·보관함 화면 줄 수 30 → 40, 새 줄에 아이템 → [메인 메뉴](W2 저장) → save.json stashLevel 1(version 3)
    // → 새 ProfileService로 다시 읽기(단계·자리 유지) → 다시 [은신처] 진입(단계·아이템·다음 버튼 문구 유지).
    // 세이브는 SaveSandbox 새 폴더(실제 persistentDataPath 없음).
    public class StashUpgradeFlowTests
    {
        private FileSaveStorage storage;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Assert.IsNotNull(GameManager.Instance, "GameManager 없음");
            M5Flow.ResetFlow();
            storage = SaveSandbox.UseFreshStorage("stash-upgrade");
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return M5Flow.TearDown("after-stash-upgrade");
        }

        private static int StashRows(M5Hideout hideout)
        {
            VisualElement grid = hideout.Ui.Q(InventoryScreenView.LootGridName)?.Q(className: InventoryScreenView.GridClass);
            Assert.IsNotNull(grid, "보관함 그리드가 그려지지 않았다");
            return grid.Query(className: InventoryScreenView.GridRowClass).ToList().Count;
        }

        [UnityTest]
        [Timeout(240000)]
        public IEnumerator UpgradeButton_GrowsStash_SavedOnMainMenu_ReloadedAndReentered()
        {
            var menu = new M4Menu();
            SceneManager.LoadScene(SceneNames.Boot);
            yield return M3Scene.WaitUntil(() => SceneManager.GetActiveScene().name == SceneNames.MainMenu, 15f, () => "Boot → MainMenu");
            yield return M4Menu.Attach(menu);

            using (var pointer = new UiPointer())
            {
                yield return pointer.Click(menu.Start);
                Assert.AreNotEqual(UiClickPath.None, pointer.LastPath, "[은신처]가 눌리지 않았다");
                var hideout = new M5Hideout();
                yield return M5Hideout.Attach(hideout, 60f);
                Assert.AreEqual(HideoutPanel.Hub, hideout.Presenter.Panel, "새 프로필 → 허브");
                HideoutConfigSO config = hideout.Controller.Config;
                ItemDatabaseSO database = hideout.Controller.ItemDatabase;
                Assert.IsTrue(config.TryGetStashUpgrade(0, out Vector2Int size1, out int cost1), "HideoutConfig에 업그레이드 단계 없음");
                Assert.AreEqual(new Vector2Int(10, 40), size1);
                Assert.AreEqual(5000, cost1);
                int casings = hideout.Service.Casings;
                Assert.GreaterOrEqual(casings, cost1, "새 프로필 탄피로 1단계를 살 수 있어야 한다");

                Assert.AreEqual(HideoutTab.Loadout, hideout.Presenter.Tab);
                Assert.AreEqual(30, StashRows(hideout), "시작 10×30");
                Assert.AreEqual(UiText.StashStatus(new Vector2Int(10, 30)), hideout.Text(HideoutHubView.StashStatusName));
                Button upgrade = hideout.ButtonNamed(HideoutHubView.StashUpgradeButtonName);
                Assert.AreEqual(UiText.StashUpgradeButton(size1, cost1), upgrade.text);
                Assert.IsTrue(upgrade.enabledSelf);
                ItemGrid stash = hideout.Profile.Stash;

                // ── [보관함 확장] ──
                using (var changed = new EventLog<HideoutChanged>(hideout.Gm.Events))
                {
                    yield return pointer.Click(upgrade);
                    Assert.AreNotEqual(UiClickPath.None, pointer.LastPath, "[보관함 확장]이 눌리지 않았다");
                    yield return M5Hideout.Frames(2);
                    Assert.Greater(changed.Count, 0, "HideoutChanged");
                }
                Assert.AreEqual(UiText.StashUpgraded(size1), hideout.Text(HideoutHubView.MessageName));
                Assert.AreEqual(1, hideout.Service.StashLevel);
                Assert.AreEqual(casings - cost1, hideout.Service.Casings, "탄피 차감");
                Assert.AreSame(stash, hideout.Profile.Stash, "제자리 확장");
                Assert.AreEqual(40, StashRows(hideout), "보관함 화면 줄 수 증가");
                Assert.AreEqual(UiText.StashStatus(size1), hideout.Text(HideoutHubView.StashStatusName));
                Assert.IsTrue(config.TryGetStashUpgrade(1, out Vector2Int size2, out int cost2));
                Assert.AreEqual(UiText.StashUpgradeButton(size2, cost2), upgrade.text);
                Assert.AreEqual(hideout.Service.Casings >= cost2, upgrade.enabledSelf, "다음 단계 탄피 부족이면 비활성");
                Assert.AreEqual(0, M5Save.Read(storage).stashLevel, "업그레이드는 메모리에서만 (저장은 W2)");

                // ── 새 줄(35~37)에 아이템 ──
                ItemInstance pipe = ItemFactory.Create(hideout.Def("JNK-01"));
                Assert.IsTrue(stash.TryPlace(pipe, new Vector2Int(4, 35), false), "새 줄에 놓을 수 있다");
                hideout.Gm.Events.Publish(new HideoutChanged());
                yield return M5Hideout.Frames(2);

                // ── [메인 메뉴] → W2 저장 ──
                yield return pointer.Click(hideout.ButtonNamed(HideoutHubView.MainMenuName));
                Assert.AreNotEqual(UiClickPath.None, pointer.LastPath, "[메인 메뉴]가 눌리지 않았다");
                yield return M3Scene.WaitUntil(() => SceneManager.GetActiveScene().name == SceneNames.MainMenu, 30f, () => "Hideout → MainMenu");
                yield return M4Menu.Attach(menu);
                SaveData saved = M5Save.Read(storage);   // version = CurrentVersion 단언 포함
                Assert.AreEqual(3, saved.version);
                Assert.AreEqual(1, saved.stashLevel);
                Assert.AreEqual(casings - cost1, saved.casings);
                ItemRecord record = saved.stash.Find(r => r.id == "JNK-01");
                Assert.IsNotNull(record);
                Assert.AreEqual(4, record.x);
                Assert.AreEqual(35, record.y);

                // ── 다시 읽기 (다음 실행과 같게 새 ProfileService) ──
                var nextRun = new ProfileService(new FileSaveStorage(storage.DirectoryPath), () => DateTime.UtcNow);
                SaveLoadReport report = nextRun.EnsureLoaded(database, config.ToProfileDefaults());
                Assert.AreEqual(SaveLoadStatus.Loaded, report.Status);
                Assert.AreEqual(1, nextRun.Profile.StashLevel);
                Assert.AreEqual(size1, nextRun.Profile.Stash.Size);
                Assert.AreEqual(1, nextRun.Profile.Stash.Items.Count);
                Assert.AreEqual(new Vector2Int(4, 35), nextRun.Profile.Stash.Items[0].Cell, "새 줄 자리 유지");

                // ── 다시 [은신처] (같은 실행 = 메모리 상태) ──
                yield return pointer.Click(menu.Start);
                hideout = new M5Hideout();
                yield return M5Hideout.Attach(hideout, 60f);
                Assert.AreEqual(1, hideout.Service.StashLevel);
                Assert.AreEqual(40, StashRows(hideout));
                Assert.AreEqual(UiText.StashStatus(size1), hideout.Text(HideoutHubView.StashStatusName));
                Assert.AreEqual(UiText.StashUpgradeButton(size2, cost2), hideout.ButtonNamed(HideoutHubView.StashUpgradeButtonName).text);
                CollectionAssert.AreEqual(new[] { "JNK-01" }, hideout.StashIds());
            }
        }
    }
}
#endif
