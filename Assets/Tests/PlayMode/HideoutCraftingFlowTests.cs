#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
    // M7 AC2 제작대 (M7 설계 §1.5, §6 7C-2b, Q44~Q47): 실제 은신처 씬에서 재료 지급(보관함) → [제작] 탭 → [제작](UI Toolkit 버튼, UiPointer)
    // → 보관함에 가득 찬 탄창, 재료 빠짐 → [메인 메뉴](W2 저장) → save.json 확인 → 새 ProfileService로 다시 읽기 → 다시 [은신처] 진입.
    // 세이브는 SaveSandbox 새 폴더(실제 persistentDataPath 없음). HideoutController.Crafting 씬 연결은 7C-3 씬 메뉴 몫.
    public class HideoutCraftingFlowTests
    {
        private FileSaveStorage storage;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Assert.IsNotNull(GameManager.Instance, "GameManager 없음");
            M5Flow.ResetFlow();
            storage = SaveSandbox.UseFreshStorage("hideout-crafting");
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return M5Flow.TearDown("after-hideout-crafting");
        }

        [UnityTest]
        [Timeout(240000)]
        public IEnumerator GiveMaterials_CraftButton_StashOutput_SavedOnMainMenu_Reloaded()
        {
            var menu = new M4Menu();
            SceneManager.LoadScene(SceneNames.Boot);
            yield return M3Scene.WaitUntil(() => SceneManager.GetActiveScene().name == SceneNames.MainMenu, 15f, () => "Boot → MainMenu");
            yield return M4Menu.Attach(menu);
            GameManager gm = GameManager.Instance;

            using (var pointer = new UiPointer())
            {
                yield return pointer.Click(menu.Start);
                Assert.AreNotEqual(UiClickPath.None, pointer.LastPath, "[은신처]가 눌리지 않았다");
                var hideout = new M5Hideout();
                yield return M5Hideout.Attach(hideout, 60f);
                Assert.AreEqual(HideoutPanel.Hub, hideout.Presenter.Panel, "새 프로필 → 허브");
                Assert.IsNotNull(hideout.Controller.Crafting,
                    "HideoutController.Crafting 미연결 — 은신처 씬에 CraftingConfig.asset을 연결해야 한다 (7C-3 씬 메뉴)");
                ItemDatabaseSO database = hideout.Controller.ItemDatabase;
                HideoutConfigSO config = hideout.Controller.Config;
                int casings = hideout.Service.Casings;

                // ── 재료 지급: 보관함에 파이프 2 + 전선 1 (테스트 준비 — 루트 획득 대신) ──
                foreach (string id in new[] { "JNK-01", "JNK-01", "JNK-03" })
                    Assert.IsTrue(StashRules.TryPlaceNew(hideout.Profile.Stash, ItemFactory.Create(hideout.Def(id))), id);
                gm.Events.Publish(new HideoutChanged());
                yield return M5Hideout.Frames(2);

                // ── [제작] 탭 ──
                yield return pointer.Click(hideout.ButtonNamed(HideoutHubView.TabCraftingName));
                Assert.AreNotEqual(UiClickPath.None, pointer.LastPath, "[제작] 탭이 눌리지 않았다");
                yield return M5Hideout.Frames(2);
                Assert.AreEqual(HideoutTab.Crafting, hideout.Presenter.Tab);
                Assert.IsTrue(hideout.Shown(CraftingView.PanelName), "제작 패널 표시");
                Assert.IsFalse(hideout.Shown(TraderView.PanelName));
                Assert.IsFalse(hideout.Shown(InventoryScreenView.ScreenName), "[장비] 숨김");

                CraftingRecipeView ammo = Recipe(hideout, "RCP-AMMO-556");
                CraftingRecipeView antiRad = Recipe(hideout, "RCP-MED-ANT");
                Assert.AreEqual(hideout.Service.Recipes.Count,
                    hideout.Ui.Q(CraftingView.RecipesName).Query(className: CraftingView.RowClass).ToList().Count, "레시피마다 한 줄");
                Assert.IsTrue(RowButton(hideout, CraftingViewModel.RecipeName(ammo)).enabledSelf, "재료 있음 → [제작] 활성");
                Assert.IsFalse(RowButton(hideout, CraftingViewModel.RecipeName(antiRad)).enabledSelf, "엔진 부품 없음 → 비활성");
                CollectionAssert.AreEqual(ammo.Inputs.Select(i => UiText.CraftingInputCount(i.Owned, i.Required)),
                    RowLabels(hideout, CraftingViewModel.RecipeName(ammo), CraftingView.InputCountClass), "가진 수/필요 수");

                // ── [제작] ──
                using (var changed = new EventLog<HideoutChanged>(gm.Events))
                {
                    Button craft = RowButton(hideout, CraftingViewModel.RecipeName(ammo));
                    yield return ScrollIntoView(craft);
                    yield return pointer.Click(RowButton(hideout, CraftingViewModel.RecipeName(ammo)));
                    Assert.AreNotEqual(UiClickPath.None, pointer.LastPath, "[제작]이 눌리지 않았다");
                    yield return M5Hideout.Frames(2);
                    Assert.Greater(changed.Count, 0, "HideoutChanged");
                }
                Assert.AreEqual(UiText.Crafted(CraftingViewModel.RecipeName(ammo)), hideout.Text(HideoutHubView.MessageName));
                CollectionAssert.AreEqual(new[] { "MAG-556-30" }, hideout.StashIds(), "재료 → 산출");
                var made = (Magazine)hideout.StashItem("MAG-556-30");
                Assert.IsTrue(made.IsFull, "산출 탄창 가득 (Q46)");
                Assert.AreEqual(casings, hideout.Service.Casings, "탄피 비용 없음 (Q45)");
                Assert.IsFalse(RowButton(hideout, CraftingViewModel.RecipeName(ammo)).enabledSelf, "재료를 다 썼다 → 비활성");
                Assert.AreEqual(0, M5Save.Read(storage).stash.Count, "제작은 메모리에서만 (Q7)");

                // ── [메인 메뉴] → W2 저장 ──
                yield return pointer.Click(hideout.ButtonNamed(HideoutHubView.MainMenuName));
                Assert.AreNotEqual(UiClickPath.None, pointer.LastPath, "[메인 메뉴]가 눌리지 않았다");
                yield return M3Scene.WaitUntil(() => SceneManager.GetActiveScene().name == SceneNames.MainMenu, 30f, () => "Hideout → MainMenu");
                yield return M4Menu.Attach(menu);
                SaveData saved = M5Save.Read(storage);
                CollectionAssert.AreEqual(new[] { "MAG-556-30" }, M5Save.SortedIds(saved.stash), "W2: 산출물 저장, 재료 없음");
                Assert.AreEqual(made.Capacity, saved.stash[0].rounds, "가득 찬 탄 수로 저장");

                // ── 다시 읽기 (다음 실행과 같게 새 ProfileService) ──
                var nextRun = new ProfileService(new FileSaveStorage(storage.DirectoryPath), () => DateTime.UtcNow);
                SaveLoadReport report = nextRun.EnsureLoaded(database, config.ToProfileDefaults());
                Assert.AreEqual(SaveLoadStatus.Loaded, report.Status);
                List<ItemInstance> reloaded = nextRun.Profile.Stash.Items.Select(p => p.Item).ToList();
                Assert.AreEqual(1, reloaded.Count);
                Assert.IsTrue(reloaded[0] is Magazine m && m.Definition.id == "MAG-556-30" && m.IsFull, "다시 읽어도 가득 찬 5.56 탄창");

                // ── 다시 [은신처] (같은 실행 = 메모리 상태) ──
                yield return pointer.Click(menu.Start);
                hideout = new M5Hideout();
                yield return M5Hideout.Attach(hideout, 60f);
                CollectionAssert.AreEqual(new[] { "MAG-556-30" }, hideout.StashIds());
                Assert.IsTrue(((Magazine)hideout.StashItem("MAG-556-30")).IsFull);
            }
        }

        private static CraftingRecipeView Recipe(M5Hideout hideout, string id)
        {
            foreach (CraftingRecipeView r in hideout.Service.Recipes)
                if (r.Id == id)
                    return r;
            Assert.Fail($"레시피 {id} 없음: " + string.Join(", ", hideout.Service.Recipes.Select(r => r.Id)));
            return default;
        }

        private static VisualElement Row(M5Hideout hideout, string name)
        {
            foreach (VisualElement row in hideout.Ui.Q(CraftingView.RecipesName).Query(className: CraftingView.RowClass).ToList())
            {
                Label label = row.Q<Label>(className: CraftingView.RowNameClass);
                if (label != null && label.text == name)
                    return row;
            }
            Assert.Fail($"제작 목록에 '{name}' 줄이 없다");
            return null;
        }

        private static Button RowButton(M5Hideout hideout, string name)
        {
            Button b = Row(hideout, name).Q<Button>(className: CraftingView.RowButtonClass);
            Assert.IsNotNull(b, $"{name} 줄에 [제작] 없음");
            return b;
        }

        private static List<string> RowLabels(M5Hideout hideout, string name, string className) =>
            Row(hideout, name).Query<Label>(className: className).ToList().Select(l => l.text).ToList();

        private static IEnumerator ScrollIntoView(VisualElement element)
        {
            for (VisualElement v = element.parent; v != null; v = v.parent)
            {
                if (v is ScrollView scroll)
                {
                    scroll.ScrollTo(element);
                    break;
                }
            }
            yield return M5Hideout.Frames(2);
        }
    }
}
#endif
