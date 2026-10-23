#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ashfall.Audio;
using Ashfall.Core;
using Ashfall.Hideout;
using Ashfall.Raid;
using Ashfall.UI;
using Ashfall.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Ashfall.Tests.PlayMode
{
    // M7 7C-4 검증 — 흐름 레이드(은신처 → 레이드)의 생존·제작 (M7 설계 §6 "7C-4", §7 AC2, §4.3 STAT-RAD·DEHY·HUNG, §8.3 방독면, Q32·Q36·Q38·Q41·Q46):
    // ① 상인에서 산 방독면(헬멧 슬롯)·해독제 반입 → 폭풍 Peak 강제 → 선량 0 유지(폭풍 몫 차단, Q38)
    //    → 정유소 RAD-REF 중심으로 PlaceAt → 선량 증가(구역 몫 0.5/s 그대로)·가이거 입력 = 폭풍 1 + 구역 0.5(방독면 무관, Q41)
    //    → 구역 밖에서 해독제(실제 인벤토리 사용 경로) → 선량 0(Q36)
    //    → EXT-01 탈출 → 은신처 → 다음 레이드: 수분·에너지 = 최대, 선량 0(이월 없음, Q32 — 세이브에 생존 값 없음).
    // ② 은신처 [제작] 탭에서 고철로 권총 탄창 제작(UI 버튼) → 그 탄창만 반입 → 레이드에서 R로 그 탄창 장전(가득, Q46) → 사격.
    // 세이브는 테스트마다 SaveSandbox 새 폴더(실제 persistentDataPath 없음). 적은 지운다(흐름만 본다).
    public class M7SurvivalRaidFlowTests
    {
        private const float ShotTimeoutSec = 6f;
        private const float ReloadTimeoutSec = 15f;
        private const float Eps = 1e-4f;

        // 설계 §3.10 RAD-REF (TODO(GDD-?) Q39) — 값 자체는 RadiationFieldFlowTests·M7SurvivalDataAssetTests가 본다
        private const string ZoneId = "RAD-REF";

        private FileSaveStorage storage;
        private SimInput input;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Assert.IsNotNull(GameManager.Instance, "GameManager 없음");
            M5Flow.ResetFlow();
            storage = SaveSandbox.UseFreshStorage("m7-survival-raid-flow");
            Assert.IsFalse(SaveSandbox.IsUnderPersistentDataPath(storage.FilePath), "샌드박스 경로");
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            DisposeInput();
            yield return M5Flow.TearDown("after-m7-survival-raid-flow");
            AudioListener.pause = false;
        }

        // ── ① 방독면 + 폭풍 Peak → 정유소 구역 → 해독제 → 탈출 → 다음 레이드 초기값 ──

        [UnityTest]
        [Timeout(420000)]
        public IEnumerator GasMaskPeak_RefineryZone_Antidote_Extract_NextRaidSurvivalReset()
        {
            GameManager gm = GameManager.Instance;
            using (var pointer = new UiPointer())
            {
                var hideout = new M5Hideout();
                yield return EnterHideoutFromMenu(pointer, hideout);
                yield return hideout.ClickKit(pointer);
                yield return hideout.Buy(pointer, "GEAR-GSM-01");
                yield return hideout.Buy(pointer, "MED-ANT-01");
                var maskDef = hideout.Def("GEAR-GSM-01") as ArmorDefinitionSO;
                Assert.IsNotNull(maskDef, "방독면 = ArmorDefinitionSO");
                Assert.AreEqual(ArmorKind.Helmet, maskDef.kind, "방독면 = 헬멧 종류");
                Assert.IsTrue(maskDef.blocksStormRadiation);

                yield return hideout.SelectTab(pointer, HideoutTab.Loadout);
                hideout.Equip("WPN-PST-01", EquipSlot.Secondary);
                hideout.Equip("GEAR-GSM-01", EquipSlot.Helmet);
                hideout.QuickMove("MED-ANT-01");
                CollectionAssert.AreEqual(new[] { "GEAR-GSM-01", "MED-ANT-01", "WPN-PST-01" },
                    M5Hideout.SortedIds(Loadout.Capture(hideout.Inventory).Entries.Select(e => e.Item)), "반입 = 권총 + 방독면 + 해독제");

                // ── 흐름 레이드 1 ──
                var raid = new M4Raid();
                yield return hideout.StartRaid(pointer, raid);
                Assert.IsTrue(gm.Flow.HasRaidInFlow, "흐름 레이드");
                AshStormController storm = Object.FindFirstObjectByType<AshStormController>();
                AudioDirector audio = Object.FindFirstObjectByType<AudioDirector>();
                Assert.IsNotNull(storm, "AshStormController");
                Assert.IsNotNull(audio, "AudioDirector");
                Assert.IsTrue(raid.Gm.Services.TryGet(out IRadiationField field), "IRadiationField 미등록 (Apply M7 Content)");
                RadiationZoneConfig zone = raid.Config.radiationZones.Single(z => z.id == ZoneId);

                ItemInstance mask = raid.Inventory.GetEquipped(EquipSlot.Helmet);
                Assert.IsNotNull(mask, "방독면이 헬멧 슬롯으로 반입됐다");
                Assert.AreEqual("GEAR-GSM-01", mask.Definition.id);
                yield return M5Hideout.Frames(2);
                Assert.IsTrue(raid.Health.GasMaskWorn, "PlayerHealth가 방독면을 본다");
                Assert.AreEqual(0f, raid.Health.RadiationDose, "시작 선량 0");
                Assert.AreEqual(0f, field.ZoneRatePerSec(raid.Motor.transform.position), "스폰 = 구역 밖");

                // 폭풍 → Peak (레이드 경과 시간 기준)
                Assert.IsTrue(storm.ForceStartNow());
                yield return M3Scene.WaitUntil(() => storm.Phase == AshStormPhase.Rising, 3f, () => "Rising");
                raid.Controller.AdvanceTime(storm.Config.riseSec + 0.5f);
                yield return M3Scene.WaitUntil(() => storm.Phase == AshStormPhase.Peak, 3f, () => "Peak");
                Assert.AreEqual(1f, storm.RadiationPerSec, Eps, "§8.3 Peak +1/s");
                yield return M2Scene.WaitSeconds(2f);
                Assert.AreEqual(AshStormPhase.Peak, storm.Phase);
                Assert.AreEqual(0f, raid.Health.LastRadiationRatePerSec, "방독면 = 폭풍 몫 0 (Q38)");
                Assert.AreEqual(0f, raid.Health.RadiationDose, "방독면 + Peak 2초 → 선량 0 유지");
                Assert.AreEqual(1f, audio.Ambient.GeigerInputPerSec, Eps, "가이거는 방독면과 무관하게 폭풍 률 (Q41)");

                // ── 정유소 구역 중심으로 ──
                raid.Placement.PlaceAt(GroundAt(zone.centerXZ), 0f);
                yield return M5Hideout.Frames(2);
                float doseIn0 = raid.Health.RadiationDose;
                yield return M2Scene.WaitSeconds(2f);
                float zoneRate = field.ZoneRatePerSec(raid.Motor.transform.position);
                Assert.AreEqual(zone.ratePerSec, zoneRate, Eps, "구역 중심 = 최대 률");
                Assert.AreEqual(zoneRate, raid.Health.LastRadiationRatePerSec, Eps, "방독면 = 구역 몫만 (폭풍 0)");
                float doseIn = raid.Health.RadiationDose;
                Assert.Greater(doseIn, doseIn0, "구역 안 → 선량 증가 (방독면을 써도)");
                Assert.That(doseIn - doseIn0, Is.InRange(zone.ratePerSec * 1.5f, zone.ratePerSec * 2.6f), "약 0.5/s × 2초");
                Assert.Greater(audio.ZoneRadiationPerSec, 0f, "가이거 구역 몫 > 0");
                Assert.AreEqual(1f + zone.ratePerSec, audio.Ambient.GeigerInputPerSec, Eps, "가이거 입력 = 폭풍 1 + 구역 0.5");
                Assert.Greater(audio.Ambient.GeigerInputPerSec, 0f);

                // ── 구역 밖(스폰)으로 → 선량 고정 → 해독제 → 0 ──
                yield return raid.LeaveZone();
                yield return M5Hideout.Frames(2);
                float doseOut = raid.Health.RadiationDose;
                Assert.Greater(doseOut, 0f);
                yield return M2Scene.WaitSeconds(0.5f);
                Assert.AreEqual(doseOut, raid.Health.RadiationDose, Eps, "구역 밖 + 방독면 + Peak = 증가 없음");

                ItemInstance antidote = raid.Carried().Single(i => i.Definition.id == "MED-ANT-01");
                using (var completed = new EventLog<MedicalUseCompleted>(raid.Events))
                using (var rejected = new EventLog<MedicalUseRejected>(raid.Events))
                {
                    Assert.IsTrue(raid.Inventory.TryUse(antidote), "해독제 사용 요청");
                    yield return M3Scene.WaitUntil(() => completed.Count > 0 || rejected.Count > 0, 10f, () => "해독제 사용 완료");
                    Assert.AreEqual(0, rejected.Count, "선량 > 0 → 해독제 필요");
                }
                Assert.AreEqual(0f, raid.Health.RadiationDose, "해독제 → 선량 0 (Q36)");
                Assert.AreEqual(1f, raid.Health.Body.MaxHpMultiplier);
                Assert.AreEqual(0, raid.Carried().Count(i => i.Definition.id == "MED-ANT-01"), "해독제 1회 → 소모");
                yield return M2Scene.WaitSeconds(0.5f);
                Assert.AreEqual(0f, raid.Health.RadiationDose, "해독제 뒤에도 Peak 몫 0");

                // 수분·에너지를 확실히 줄여 둔다 (300초분 — 자연 감소 0.1/s·0.07/s를 한 번에)
                raid.Health.Survival.Tick(300f, false);
                float hydration1 = raid.Health.Survival.Hydration;
                float energy1 = raid.Health.Survival.Energy;
                Assert.Less(hydration1, raid.Health.Survival.HydrationMax - 20f, "레이드 1 수분 감소");
                Assert.Less(energy1, raid.Health.Survival.EnergyMax - 15f, "레이드 1 에너지 감소");
                Assert.IsFalse(raid.Health.Survival.IsDehydrated);

                // 다시 구역에 잠깐 들어가 선량 > 0으로 탈출 (이월 확인용)
                raid.Placement.PlaceAt(GroundAt(zone.centerXZ), 0f);
                yield return M2Scene.WaitSeconds(1f);
                float doseAtExtract = raid.Health.RadiationDose;
                Assert.Greater(doseAtExtract, 0f, "탈출 시 선량 > 0");

                // ── EXT-01 탈출 ──
                RaidResult extracted;
                using (var ended = new EventLog<RaidEnded>(raid.Events))
                {
                    yield return raid.EnterZone("EXT-01");
                    yield return M3Scene.WaitUntil(() => ended.Count > 0, 25f, () => "EXT-01 탈출");
                    extracted = raid.Controller.LastResult;
                }
                Assert.AreEqual(RaidOutcome.Extracted, extracted.Outcome);

                // ── 은신처: 방독면 반출, 해독제 없음, 세이브에 생존 값 없음 ──
                hideout = new M5Hideout();
                yield return M5Hideout.Attach(hideout);
                hideout.AssertResultPanel(extracted, expectCarriedOut: true);
                yield return hideout.ConfirmResult(pointer);
                Assert.IsNotNull(hideout.Inventory.GetEquipped(EquipSlot.Helmet), "방독면 반출 → 은신처 헬멧 슬롯");
                Assert.AreEqual("GEAR-GSM-01", hideout.Inventory.GetEquipped(EquipSlot.Helmet).Definition.id);
                string json = File.ReadAllText(storage.FilePath);
                M5Save.Read(storage);
                foreach (string word in new[] { "hydration", "energy", "radiation", "dose" })
                    StringAssert.DoesNotContain(word, json.ToLowerInvariant(), $"세이브에 생존 값 없음 (Q32): {word}");

                // ── 흐름 레이드 2: 초기값 ──
                var raid2 = new M4Raid();
                yield return hideout.StartRaid(pointer, raid2);
                Assert.AreNotSame(raid.Health, raid2.Health, "새 플레이어");
                Assert.AreEqual(raid2.Health.Survival.HydrationMax, raid2.Health.Survival.Hydration, 0.5f, "다음 레이드 수분 = 최대 (이월 없음)");
                Assert.AreEqual(raid2.Health.Survival.EnergyMax, raid2.Health.Survival.Energy, 0.5f, "다음 레이드 에너지 = 최대");
                Assert.Greater(raid2.Health.Survival.Hydration, hydration1 + 20f);
                Assert.Greater(raid2.Health.Survival.Energy, energy1 + 15f);
                Assert.AreEqual(0f, raid2.Health.RadiationDose, "다음 레이드 선량 0 (이월 없음)");
                Assert.AreEqual(1f, raid2.Health.Body.MaxHpMultiplier);
                Assert.AreEqual(SurvivalTier.Normal, BodyView(raid2).GetSurvivalTier(SurvivalStat.Hydration));
                Assert.AreEqual(SurvivalTier.Normal, BodyView(raid2).GetSurvivalTier(SurvivalStat.Energy));
                Assert.AreEqual(SurvivalTier.Normal, BodyView(raid2).GetSurvivalTier(SurvivalStat.Radiation));
                AshStormController storm2 = Object.FindFirstObjectByType<AshStormController>();
                Assert.IsNotNull(storm2);
                Assert.AreEqual(AshStormPhase.Pending, storm2.Phase, "다음 레이드 폭풍 Pending");
                yield return M5Hideout.Frames(2);
                Assert.IsTrue(raid2.Health.GasMaskWorn, "반출한 방독면을 다시 반입");
                yield return M2Scene.WaitSeconds(1f);
                Assert.AreEqual(0f, raid2.Health.RadiationDose, "스폰·폭풍 전 → 선량 0 그대로");
            }
        }

        // ── ② 고철로 탄창 제작 → 반입 → 그 탄창으로 재장전·사격 ──

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator CraftedMagazine_Loadout_ReloadAndFireInRaid()
        {
            GameManager gm = GameManager.Instance;
            using (var pointer = new UiPointer())
            {
                var hideout = new M5Hideout();
                yield return EnterHideoutFromMenu(pointer, hideout);
                Assert.IsNotNull(hideout.Controller.Crafting, "HideoutController.Crafting 미연결 (Apply M7 Content)");
                yield return hideout.ClickKit(pointer);
                Assert.IsTrue(hideout.Shown(HideoutHubView.HubName));

                // 레시피 재료(고철)를 보관함에 지급 (루트 획득 대신 — 레시피 표 값을 그대로 따른다)
                CraftingRecipeView recipe = Recipe(hideout, "RCP-AMMO-9MM12");
                Assert.AreEqual("MAG-9MM-12", recipe.Output.id, "권총 탄창 레시피");
                Assert.AreEqual(1, recipe.OutputCount);
                foreach (CraftingInputView inputView in recipe.Inputs)
                {
                    StringAssert.StartsWith("JNK-", inputView.Item.id, "재료 = 고철");
                    for (int i = 0; i < inputView.Required; i++)
                        Assert.IsTrue(StashRules.TryPlaceNew(hideout.Profile.Stash, ItemFactory.Create(inputView.Item)), inputView.Item.id);
                }
                gm.Events.Publish(new HideoutChanged());
                yield return M5Hideout.Frames(2);
                var before = new HashSet<ItemInstance>(hideout.StashItems("MAG-9MM-12"));
                int junkBefore = hideout.StashItems().Count(i => i.Definition.id.StartsWith("JNK-"));
                Assert.Greater(junkBefore, 0);

                // ── [제작] 탭 → [제작] (UI 버튼) ──
                yield return pointer.Click(hideout.ButtonNamed(HideoutHubView.TabCraftingName));
                Assert.AreNotEqual(UiClickPath.None, pointer.LastPath, "[제작] 탭이 눌리지 않았다");
                yield return M5Hideout.Frames(2);
                Assert.AreEqual(HideoutTab.Crafting, hideout.Presenter.Tab);
                recipe = Recipe(hideout, "RCP-AMMO-9MM12");
                string rowName = CraftingViewModel.RecipeName(recipe);
                Button craft = CraftButton(hideout, rowName);
                Assert.IsTrue(craft.enabledSelf, "재료 있음 → [제작] 활성");
                yield return ScrollIntoView(craft);
                yield return pointer.Click(CraftButton(hideout, rowName));
                Assert.AreNotEqual(UiClickPath.None, pointer.LastPath, "[제작]이 눌리지 않았다");
                yield return M5Hideout.Frames(2);
                Assert.AreEqual(UiText.Crafted(rowName), hideout.Text(HideoutHubView.MessageName));
                Assert.AreEqual(0, hideout.StashItems().Count(i => i.Definition.id.StartsWith("JNK-")), "재료 소모");
                List<ItemInstance> made = hideout.StashItems("MAG-9MM-12").Where(i => !before.Contains(i)).ToList();
                Assert.AreEqual(1, made.Count, "새 권총 탄창 1개");
                var crafted = (Magazine)made[0];
                Assert.IsTrue(crafted.IsFull, "산출 탄창 가득 (Q46)");

                // ── 반입: 키트 권총 + 제작한 탄창만 (키트 탄창은 보관함에 남긴다) ──
                yield return hideout.SelectTab(pointer, HideoutTab.Loadout);
                hideout.Equip("WPN-PST-01", EquipSlot.Secondary);
                Assert.IsTrue(hideout.Inventory.TryQuickMove(crafted), "제작 탄창 → 장비");
                List<ItemInstance> loadout = Loadout.Capture(hideout.Inventory).Entries.Select(e => e.Item).ToList();
                CollectionAssert.AreEqual(new[] { "MAG-9MM-12", "WPN-PST-01" }, M5Hideout.SortedIds(loadout), "반입 = 권총 + 제작 탄창");
                Assert.IsTrue(loadout.Contains(crafted), "반입한 탄창 = 제작한 인스턴스");

                // ── 흐름 레이드 ──
                var raid = new M4Raid();
                yield return hideout.StartRaid(pointer, raid);
                Assert.IsTrue(gm.Flow.HasRaidInFlow);
                WeaponController weapon = raid.Motor.GetComponentInChildren<WeaponController>(true);
                Assert.IsNotNull(weapon, "Player WeaponController 없음");
                var pistol = raid.Inventory.GetEquipped(EquipSlot.Secondary) as WeaponItem;
                Assert.IsNotNull(pistol);
                Assert.AreEqual(EquipSlot.Secondary, weapon.ActiveSlot, "시작 = Secondary (Q2)");
                Assert.IsNull(pistol.LoadedMagazine, "키트 권총 = 탄창 없음");
                var carriedMag = (Magazine)raid.Carried().Single(i => i.Definition.id == "MAG-9MM-12");
                Assert.AreEqual(crafted.Capacity, carriedMag.Rounds, "반입 탄창 가득");

                input = new SimInput();
                raid.LookDown(-45f);   // 하늘로 쏜다
                Assert.IsTrue(weapon.Model.CanReload, "재장전 가능");
                yield return input.Keyboard.Tap(Key.R);
                Assert.IsTrue(weapon.Model.IsReloading, "R로 재장전 시작");
                yield return M2Scene.WaitUntil(() => !weapon.Model.IsReloading, ReloadTimeoutSec);
                Assert.IsNotNull(pistol.LoadedMagazine, "제작 탄창 장전");
                Assert.AreSame(carriedMag, pistol.LoadedMagazine, "장전된 탄창 = 반입한 제작 탄창");
                Assert.AreEqual(12, pistol.LoadedMagazine.Rounds);

                using (var shots = new EventLog<ShotFired>(raid.Events))
                {
                    int liveBefore = raid.Projectiles.LiveCount;
                    float deadline = Time.realtimeSinceStartup + ShotTimeoutSec;
                    while (shots.Count == 0)
                    {
                        Assert.Less(Time.realtimeSinceStartup, deadline, "사격이 나가지 않았다");
                        yield return input.Mouse.ClickLeft();
                    }
                    Assert.AreEqual(1, shots.Count, "한 발");
                    Assert.AreSame(pistol.Definition, shots.Last.Weapon);
                    Assert.AreEqual(1, raid.Projectiles.LiveCount - liveBefore, "투사체 1");
                }
                Assert.AreEqual(11, carriedMag.Rounds, "제작 탄창에서 1발 소모");
                DisposeInput();
            }
        }

        // ── 도우미 ──

        private static IEnumerator EnterHideoutFromMenu(UiPointer pointer, M5Hideout hideout)
        {
            var menu = new M4Menu();
            yield return M4Raid.LoadScene(SceneNames.MainMenu);
            yield return M4Menu.Attach(menu);
            yield return pointer.Click(menu.Start);
            Assert.AreNotEqual(UiClickPath.None, pointer.LastPath, "[은신처]가 눌리지 않았다");
            yield return M5Hideout.Attach(hideout, 60f);
        }

        private void DisposeInput()
        {
            input?.Dispose();
            input = null;
        }

        private static IBodyStatusView BodyView(M4Raid raid)
        {
            Assert.IsTrue(raid.Gm.Services.TryGet(out IBodyStatusView view), "IBodyStatusView 미등록");
            return view;
        }

        // 위에서 아래로 쏴서 지면(구조물 포함) 점 — RadiationFieldFlowTests.GroundAt과 같음
        private static Vector3 GroundAt(Vector2 xz)
        {
            var top = new Vector3(xz.x, 400f, xz.y);
            RaycastHit[] hits = Physics.RaycastAll(top, Vector3.down, 800f, ~0, QueryTriggerInteraction.Ignore);
            Assert.Greater(hits.Length, 0, $"{xz} 지면 없음");
            float y = hits.Max(h => h.point.y);
            return new Vector3(xz.x, y + 0.05f, xz.y);
        }

        private static CraftingRecipeView Recipe(M5Hideout hideout, string id)
        {
            foreach (CraftingRecipeView r in hideout.Service.Recipes)
                if (r.Id == id)
                    return r;
            Assert.Fail($"레시피 {id} 없음: " + string.Join(", ", hideout.Service.Recipes.Select(r => r.Id)));
            return default;
        }

        private static Button CraftButton(M5Hideout hideout, string rowName)
        {
            foreach (VisualElement row in hideout.Ui.Q(CraftingView.RecipesName).Query(className: CraftingView.RowClass).ToList())
            {
                Label label = row.Q<Label>(className: CraftingView.RowNameClass);
                if (label != null && label.text == rowName)
                {
                    Button b = row.Q<Button>(className: CraftingView.RowButtonClass);
                    Assert.IsNotNull(b, $"{rowName} 줄에 [제작] 없음");
                    return b;
                }
            }
            Assert.Fail($"제작 목록에 '{rowName}' 줄이 없다");
            return null;
        }

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
