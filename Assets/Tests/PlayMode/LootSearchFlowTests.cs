#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Ashfall.Core;
using Ashfall.Inventory;
using Ashfall.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Ashfall.Tests.PlayMode
{
    // M2 AC3 — 루트 컨테이너 검색 연출 (§6: 아이템 1개당 0.5초). M2 씬 상자 B(6개 → 3초).
    public class LootSearchFlowTests
    {
        private const float PerItemSec = 0.5f;
        private M2Scene scene;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            scene = new M2Scene();
            yield return M2Scene.Load(scene);
        }

        [UnityTest]
        public IEnumerator OpenBox_RevealsOneItemPerHalfSecond_HiddenItemsCannotBeTaken()
        {
            IInventoryService inventory = scene.Inventory;
            Assert.AreEqual(PerItemSec, scene.InventoryBehaviour.Config.searchTimePerItemSec, 1e-6f, "§6 검색 시간");
            using var session = new EventLog<LootSessionChanged>(scene.Events);
            using var screen = new EventLog<ScreenStateChanged>(scene.Events);

            var box = scene.LootBox("LootBox_B");
            box.Interact(scene.InventoryBehaviour);   // F 상호작용과 같은 호출
            Assert.AreEqual(1, session.Count);
            Assert.IsTrue(session.Last.Open);
            Assert.AreEqual(ScreenKind.Inventory, scene.Ui.Presenter.Screens.Current, "루트 열림 → 인벤토리 화면");
            Assert.AreEqual(ScreenKind.Inventory, screen.Last.Screen);
            Assert.IsTrue(scene.Motor.InputBlocked, "화면 열림 → 이동 입력 차단");

            ILootView loot = inventory.ActiveLoot;
            Assert.IsNotNull(loot);
            LootContainerModel model = box.Model;
            ItemInstance[] items = loot.Items.Select(p => p.Item).ToArray();
            Assert.AreEqual(6, items.Length);
            Assert.AreEqual(0, model.RevealedCount);

            // 검색 전 아이템은 집을 수 없다
            ItemInstance hidden = items[0];
            Assert.IsFalse(loot.IsRevealed(hidden));
            Assert.IsFalse(inventory.TryQuickMove(hidden));
            Assert.IsFalse(inventory.TryMove(hidden, scene.StorageGrid(GridKind.Backpack).Ref, new Vector2Int(3, 0), false));
            Assert.IsFalse(inventory.TryDrop(hidden));

            // UI: 검색 전 칸은 "?", "검색 중…" 문구
            yield return null;
            float elapsed = Time.deltaTime;   // 열려 있던 시간 (Tick이 받은 dt 합). 이 프레임에도 Tick이 돌았다.
            VisualElement lootPanel = scene.UiRoot.Q(InventoryScreenView.LootPanelName);
            Assert.IsNotNull(lootPanel);
            var texts = M2Scene.LabelTexts(lootPanel).ToList();
            Assert.Contains(UiText.LootSearching, texts);
            Assert.GreaterOrEqual(texts.Count(t => t == UiText.HiddenItem), 5, "검색 전 아이템은 \"?\"");

            // 0.5초마다 하나씩, 배치 순서대로 드러난다. 두 개 드러나면 닫았다가 다시 연다.
            var revealAt = new List<float>();
            int seen = model.RevealedCount;
            Assert.AreEqual(0, seen, "첫 프레임(0.5초 미만)에는 드러난 것 없음");
            bool reopened = false;
            float deadline = Time.realtimeSinceStartup + 10f;
            while (!loot.SearchComplete)
            {
                Assert.Less(Time.realtimeSinceStartup, deadline, "검색이 끝나지 않는다");
                yield return null;
                elapsed += Time.deltaTime;
                while (seen < model.RevealedCount)
                {
                    Assert.IsTrue(loot.IsRevealed(items[seen]), "배치 순서대로 드러남");
                    if (seen + 1 < items.Length)
                        Assert.IsFalse(loot.IsRevealed(items[seen + 1]));
                    revealAt.Add(elapsed);
                    seen++;
                }

                if (!reopened && seen == 2)
                {
                    reopened = true;
                    // Esc → 화면 닫힘 → 루트 닫힘, 닫힌 동안 진행 없음
                    scene.Ui.Presenter.Cancel();
                    Assert.IsNull(inventory.ActiveLoot, "화면을 닫으면 루트 닫힘");
                    Assert.IsFalse(session.Last.Open);
                    yield return M2Scene.WaitSeconds(1f);
                    Assert.AreEqual(2, model.RevealedCount, "닫힌 동안 검색 진행 없음");
                    box.Interact(scene.InventoryBehaviour);
                    Assert.AreSame(loot, inventory.ActiveLoot, "같은 상자 다시 열기 = 이어서");
                }
            }

            Assert.AreEqual(6, revealAt.Count);
            for (int i = 0; i < revealAt.Count; i++)
            {
                float due = PerItemSec * (i + 1);
                Assert.GreaterOrEqual(revealAt[i], due - 1e-3f, $"{i + 1}번째가 너무 일찍 드러남");
                Assert.Less(revealAt[i], due + 0.1f, $"{i + 1}번째가 너무 늦게 드러남");
            }
            Assert.AreEqual(6 * PerItemSec, revealAt[5], 0.1f, "N개 → N × 0.5초 (열려 있던 시간 기준)");

            // 다 드러난 뒤에는 가져올 수 있다
            Assert.IsTrue(inventory.TryQuickMove(hidden));
            Assert.IsTrue(scene.Inventory.IsInStorage(hidden));
            yield return null;
            Assert.IsFalse(M2Scene.LabelTexts(lootPanel).Any(t => t == UiText.HiddenItem), "검색 완료 후 \"?\" 없음");
        }
    }
}
#endif
