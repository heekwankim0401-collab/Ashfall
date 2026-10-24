#if UNITY_EDITOR
using System.Collections;
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
    // MVP 마무리 F3 AC-F3 내구도 표시·수리·가격 (§5.1, 설계 §1.4·§7, MF-Q10~Q14): 실제 은신처 씬에서 보관함의 낡은 권총 →
    // [상인] 매도 줄 "이름 (낡음)"·낮은 매도가·[수리 (N 탄피)] → 클릭(UiPointer) → 탄피 차감·내구도 100·문구 "(양호)"·[수리] 숨김·매도가 원래대로
    // → [장비] 탭 보관함 칸 라벨 "(양호)". 세이브는 SaveSandbox 새 폴더(실제 persistentDataPath 없음).
    public class RepairFlowTests
    {
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Assert.IsNotNull(GameManager.Instance, "GameManager 없음");
            M5Flow.ResetFlow();
            SaveSandbox.UseFreshStorage("repair");
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return M5Flow.TearDown("after-repair");
        }

        private static VisualElement SellRow(M5Hideout hideout, string rowName)
        {
            foreach (VisualElement row in hideout.Ui.Q<VisualElement>(TraderView.SellName).Query<VisualElement>(className: TraderView.RowClass).ToList())
            {
                Label name = row.Q<Label>(className: TraderView.RowNameClass);
                if (name != null && name.text == rowName)
                    return row;
            }
            Assert.Fail($"매도 목록에 '{rowName}' 줄이 없다: " + string.Join(", ",
                hideout.Ui.Q<VisualElement>(TraderView.SellName).Query<Label>(className: TraderView.RowNameClass).ToList().Select(l => l.text)));
            return null;
        }

        [UnityTest]
        [Timeout(180000)]
        public IEnumerator WornPistol_RepairButton_DeductsCasings_ShowsGood()
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

                // ── 보관함에 낡은 권총 (내구도 30 = 낡음) ──
                var pistol = (WeaponItem)ItemFactory.Create(hideout.Def("WPN-PST-01"));
                pistol.Durability = 30f;
                Assert.IsTrue(hideout.Profile.Stash.TryPlace(pistol, new Vector2Int(0, 0), false) ||
                              StashRules.TryPlaceNew(hideout.Profile.Stash, pistol), "보관함에 놓기");
                hideout.Profile.Casings = 10000;
                hideout.Gm.Events.Publish(new HideoutChanged());
                yield return M5Hideout.Frames(2);

                TraderConfigSO trader = hideout.Controller.Trader;
                int cost = PriceRules.RepairCost(pistol, trader);
                Assert.Greater(cost, 0);
                int wornPrice = hideout.Service.GetSellPrice(pistol);
                string label = UiText.ItemLabel(pistol);

                // ── [상인] 매도 줄 ──
                yield return hideout.SelectTab(pointer, HideoutTab.Trader);
                VisualElement row = SellRow(hideout, label + " (낡음)");
                Assert.AreEqual(UiText.Price(wornPrice), row.Q<Label>(className: TraderView.RowPriceClass).text);
                Button repair = row.Q<Button>(className: TraderView.RowRepairClass);
                Assert.IsNotNull(repair, "[수리] 버튼 없음");
                Assert.IsTrue(UiQuery.IsShown(repair), "낡은 무기 → [수리] 표시");
                Assert.IsTrue(repair.enabledSelf);
                Assert.AreEqual(UiText.RepairButton(cost), repair.text);
                for (VisualElement v = row.parent; v != null; v = v.parent)
                {
                    if (v is ScrollView scroll)
                    {
                        scroll.ScrollTo(row);
                        break;
                    }
                }
                yield return M5Hideout.Frames(2);

                using (var changed = new EventLog<HideoutChanged>(hideout.Gm.Events))
                {
                    yield return pointer.Click(repair);
                    Assert.AreNotEqual(UiClickPath.None, pointer.LastPath, "[수리]가 눌리지 않았다");
                    yield return M5Hideout.Frames(2);
                    Assert.Greater(changed.Count, 0, "HideoutChanged");
                }
                Assert.AreEqual(10000 - cost, hideout.Service.Casings, "탄피 차감");
                Assert.AreEqual(WeaponItem.MaxDurability, pistol.Durability, "내구도 100");
                Assert.AreEqual(UiText.Repaired(label + " (양호)"), hideout.Text(HideoutHubView.MessageName));
                row = SellRow(hideout, label + " (양호)");
                Assert.IsFalse(UiQuery.IsShown(row.Q<Button>(className: TraderView.RowRepairClass)), "수리 후 [수리] 숨김");
                int fullPrice = hideout.Service.GetSellPrice(pistol);
                Assert.Greater(fullPrice, wornPrice, "매도가 원래대로");
                Assert.AreEqual(UiText.Price(fullPrice), row.Q<Label>(className: TraderView.RowPriceClass).text);

                // ── [장비] 탭 보관함 칸 라벨 ──
                yield return hideout.SelectTab(pointer, HideoutTab.Loadout);
                yield return M5Hideout.Frames(2);
                CollectionAssert.Contains(hideout.Ui.Q(InventoryScreenView.LootGridName)
                        .Query<Label>(className: InventoryScreenView.ItemClass).ToList().Select(l => l.text).ToList(),
                    label + " (양호)");
            }
        }
    }
}
#endif
