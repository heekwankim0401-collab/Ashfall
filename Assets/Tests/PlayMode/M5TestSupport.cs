#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Ashfall.Core;
using Ashfall.Hideout;
using Ashfall.Inventory;
using Ashfall.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Ashfall.Tests.PlayMode
{
    // M5 통합 테스트 공용 (M5 설계 §6 4단계, §7): 은신처 씬 참조·UI 버튼·상인 줄 클릭, 장비 옮기기(서비스 명령),
    // 레이드 시작·결과 패널, 세이브 파일 읽기. 세이브는 SaveSandbox 폴더만 쓴다(실제 persistentDataPath 없음).
    internal sealed class M5Hideout
    {
        public const float StartRaidTimeoutSec = 60f;

        public GameManager Gm;
        public HideoutController Controller;
        public HideoutInventoryBehaviour InventoryBehaviour;
        public HideoutRoot Root;
        public UIDocument Document;

        public GameFlow Flow => Gm.Flow;
        public HideoutService Service => Controller.Service;
        public PlayerInventory Inventory => InventoryBehaviour.Inventory;
        public PlayerProfile Profile => Gm.Profiles.Profile;
        public HideoutPresenter Presenter => Root.Presenter;
        public VisualElement Ui => Document.rootVisualElement;

        // 은신처 씬이 활성이고 UI가 준비될 때까지 기다린 뒤 참조를 잡는다.
        public static IEnumerator Attach(M5Hideout into, float timeoutSec = M4Raid.EndToMenuTimeoutSec)
        {
            yield return M4Raid.WaitForHideout(timeoutSec);
            into.Gm = GameManager.Instance;
            Assert.IsNotNull(into.Gm, "GameManager 없음");
            into.Controller = Object.FindFirstObjectByType<HideoutController>();
            into.InventoryBehaviour = Object.FindFirstObjectByType<HideoutInventoryBehaviour>();
            into.Root = Object.FindFirstObjectByType<HideoutRoot>();
            Assert.IsNotNull(into.Controller, "HideoutController 없음");
            Assert.IsNotNull(into.Controller.Service, "HideoutService 없음 (참조 누락?)");
            Assert.IsNotNull(into.InventoryBehaviour, "HideoutInventoryBehaviour 없음");
            Assert.IsNotNull(into.InventoryBehaviour.Inventory, "은신처 인벤토리 없음");
            Assert.IsNotNull(into.Root, "HideoutRoot 없음");
            into.Document = into.Root.GetComponent<UIDocument>();
            Assert.AreEqual(GamePhase.Hideout, into.Gm.Flow.Phase, "Hideout 단계");
            yield return Frames(2);   // 첫 그리기
        }

        public static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++)
                yield return null;
        }

        // ── UI 읽기 ──

        public Button ButtonNamed(string name)
        {
            Button b = Ui.Q<Button>(name);
            Assert.IsNotNull(b, $"버튼 {name} 없음");
            return b;
        }

        public string Text(string name) => Ui.Q<Label>(name)?.text;

        public List<string> Lines(string listName) =>
            Ui.Q<VisualElement>(listName).Query<Label>().ToList().Select(l => l.text).ToList();

        public bool Shown(string name) => UiQuery.IsShown(Ui.Q<VisualElement>(name));

        // ── 데이터 ──

        public ItemDefinitionSO Def(string id)
        {
            Assert.IsTrue(Controller.ItemDatabase.TryGet(id, out ItemDefinitionSO def), $"ItemDatabase에 {id} 없음");
            return def;
        }

        public List<ItemInstance> StashItems(string id = null) =>
            Profile.Stash.Items.Select(p => p.Item).Where(i => id == null || i.Definition.id == id).ToList();

        public ItemInstance StashItem(string id)
        {
            ItemInstance item = StashItems(id).FirstOrDefault();
            Assert.IsNotNull(item, $"보관함에 {id} 없음");
            return item;
        }

        public string[] StashIds() => SortedIds(StashItems());

        // ── 허브 명령 (UI 버튼) ──

        public IEnumerator ClickKit(UiPointer pointer)
        {
            yield return pointer.Click(ButtonNamed(HideoutHubView.KitButtonName));
            Assert.AreNotEqual(UiClickPath.None, pointer.LastPath, "[부랑자 키트 받기]가 눌리지 않았다");
            yield return Frames(2);
        }

        public IEnumerator SelectTab(UiPointer pointer, HideoutTab tab)
        {
            if (Presenter.Tab == tab)
                yield break;
            yield return pointer.Click(ButtonNamed(tab == HideoutTab.Trader ? HideoutHubView.TabTraderName : HideoutHubView.TabLoadoutName));
            Assert.AreEqual(tab, Presenter.Tab, $"탭 {tab}로 바뀌지 않았다");
            yield return Frames(2);
        }

        // [상인] 탭 판매 목록에서 정의 이름 줄의 [구매]를 누른다 (줄은 그릴 때마다 새로 만들어지므로 매번 찾는다).
        public IEnumerator Buy(UiPointer pointer, string id)
        {
            yield return SelectTab(pointer, HideoutTab.Trader);
            ItemDefinitionSO def = Def(id);
            int before = Service.Casings;
            int stashBefore = StashItems(id).Count;
            string rowName = RaidResultViewModel.ItemName(def);
            yield return ScrollIntoView(RowButton(TraderView.OffersName, rowName));
            yield return pointer.Click(RowButton(TraderView.OffersName, rowName));
            Assert.AreNotEqual(UiClickPath.None, pointer.LastPath, $"[구매] {id}가 눌리지 않았다");
            yield return Frames(2);
            Assert.AreEqual(stashBefore + 1, StashItems(id).Count, $"{id} 구매 → 보관함 (메시지: {Text(HideoutHubView.MessageName)})");
            Assert.Less(Service.Casings, before, $"{id} 구매 → 탄피 차감");
        }

        // [상인] 탭 매도 목록 줄의 [판매] (첫 번째 같은 이름 줄)
        public IEnumerator Sell(UiPointer pointer, string rowName)
        {
            yield return SelectTab(pointer, HideoutTab.Trader);
            yield return ScrollIntoView(RowButton(TraderView.SellName, rowName));
            yield return pointer.Click(RowButton(TraderView.SellName, rowName));
            Assert.AreNotEqual(UiClickPath.None, pointer.LastPath, $"[판매] {rowName}가 눌리지 않았다");
            yield return Frames(2);
        }

        // 목록 ScrollView 밖에 있는 줄은 실제 포인터 경로가 화면의 다른 요소를 누를 수 있다 → 먼저 보이게 스크롤한다.
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
            yield return Frames(2);
        }

        public Button RowButton(string listName, string rowName)
        {
            foreach (VisualElement row in Ui.Q<VisualElement>(listName).Query<VisualElement>(className: TraderView.RowClass).ToList())
            {
                Label name = row.Q<Label>(className: TraderView.RowNameClass);
                if (name != null && name.text == rowName)
                {
                    Button b = row.Q<Button>(className: TraderView.RowButtonClass);
                    Assert.IsNotNull(b, $"{rowName} 줄에 버튼 없음");
                    return b;
                }
            }
            Assert.Fail($"{listName}에 '{rowName}' 줄이 없다: " +
                        string.Join(", ", Ui.Q<VisualElement>(listName).Query<Label>(className: TraderView.RowNameClass).ToList().Select(l => l.text)));
            return null;
        }

        // ── 장비 옮기기 (서비스 명령 — 실제 마우스 드래그는 사람 확인) ──

        public void Equip(string id, EquipSlot slot)
        {
            ItemInstance item = StashItem(id);
            Assert.IsTrue(Inventory.TryEquip(item, slot), $"{id} → {slot} 장착 실패");
        }

        // 보관함(열린 루트) → 저장 공간 자동 배치 (Ctrl+클릭과 같은 경로)
        public ItemInstance QuickMove(string id)
        {
            ItemInstance item = StashItem(id);
            Assert.IsTrue(Inventory.TryQuickMove(item), $"{id} 보관함 → 장비 빠른 이동 실패");
            return item;
        }

        // [레이드 시작] → 레이드 씬 로드 → M4Raid 참조. keepEnemies false면 적을 지운다(흐름만 본다).
        public IEnumerator StartRaid(UiPointer pointer, M4Raid raid, bool keepEnemies = false)
        {
            Assert.AreEqual(HideoutPanel.Hub, Presenter.Panel, "허브에서 시작");
            yield return pointer.Click(ButtonNamed(HideoutHubView.StartRaidName));
            Assert.AreNotEqual(UiClickPath.None, pointer.LastPath, "[레이드 시작]이 눌리지 않았다");
            yield return M3Scene.WaitUntil(() => SceneManager.GetActiveScene().name == SceneNames.RaidCrimsonBasin,
                StartRaidTimeoutSec, () => $"레이드 씬 로드 (메시지: {SafeMessage()})");
            yield return Frames(3);
            yield return M4Raid.Attach(raid);
            Assert.AreEqual(GamePhase.Raid, raid.Flow.Phase);
            Assert.AreEqual(1f, Time.timeScale);
            if (!keepEnemies)
                yield return raid.RemoveEnemies();
        }

        private string SafeMessage()
        {
            try
            {
                return Document != null && Document.rootVisualElement != null ? Text(HideoutHubView.MessageName) : "(은신처 없음)";
            }
            catch (System.Exception)
            {
                return "(은신처 없음)";
            }
        }

        // ── 결과 패널 (M4 결과 화면이 은신처로 이동 — Q21) ──

        private static readonly Regex ItemLine = new Regex(@"^(?<name>.+?)( ×(?<n>[0-9]+))?$");

        public void AssertResultPanel(RaidResult result, bool expectCarriedOut)
        {
            Assert.AreEqual(HideoutPanel.Result, Presenter.Panel, "결과 패널 단계");
            Assert.IsTrue(Shown(RaidResultView.PanelName), "결과 패널 표시");
            Assert.IsFalse(Shown(HideoutHubView.HubName), "결과 먼저 (허브 숨김)");
            Assert.IsFalse(Shown(HideoutNoticeView.PanelName), "알림 패널 숨김");
            Assert.AreEqual(RaidResultViewModel.TitleFor(result), Text(RaidResultView.TitleName));
            Assert.That(Text(RaidResultView.DurationName), Does.Match(@"^레이드 시간 [0-9]+:[0-5][0-9]$"));
            Assert.AreEqual("처치 " + result.EnemiesKilled, Text(RaidResultView.KillsName));

            Assert.AreEqual(expectCarriedOut, Shown(RaidResultView.CarriedOutSectionName), "반출 섹션은 탈출만");
            Assert.AreEqual(!expectCarriedOut, Shown(RaidResultView.LostSectionName), "소실 섹션은 탈출 외");

            AssertItemLines(Lines(RaidResultView.BroughtInName), result.BroughtIn);
            if (expectCarriedOut)
                AssertItemLines(Lines(RaidResultView.CarriedOutName), result.CarriedOut.Select(i => i.Definition).ToList());
            else
            {
                AssertItemLines(Lines(RaidResultView.LostName), result.Lost);
                Assert.Greater(result.Lost.Count, 0, "소실 목록이 비어 있다");
            }

            // 탄 수·HP 수치 없음 (RULE-04·05): 아이템 줄은 위에서 정의 이름(×n)과 같음을 봤다. 그 밖 줄에 n/m·%·HP 없음.
            foreach (string line in Ui.Q(RaidResultView.PanelName).Query<Label>().ToList().Select(l => l.text ?? ""))
                Assert.That(line, Does.Not.Match(@"[0-9]+\s*/\s*[0-9]+").And.Not.Contain("HP").And.Not.Contain("%"),
                    $"결과 패널에 탄 수·HP 표기: '{line}'");
        }

        public IEnumerator ConfirmResult(UiPointer pointer)
        {
            yield return pointer.Click(ButtonNamed(RaidResultView.ConfirmName));
            Assert.AreNotEqual(UiClickPath.None, pointer.LastPath, "결과 [확인]이 눌리지 않았다");
            yield return Frames(2);
            Assert.IsNull(Flow.PendingResult, "[확인] → 결과 확인");
            Assert.AreEqual(HideoutPanel.Hub, Presenter.Panel, "결과 확인 → 허브");
            Assert.IsTrue(Shown(HideoutHubView.HubName), "허브 표시");
        }

        public IEnumerator ConfirmNotice(UiPointer pointer)
        {
            yield return pointer.Click(ButtonNamed(HideoutNoticeView.ConfirmName));
            Assert.AreNotEqual(UiClickPath.None, pointer.LastPath, "알림 [확인]이 눌리지 않았다");
            yield return Frames(2);
            Assert.IsNull(Service.PendingNotice, "알림 확인");
            Assert.AreNotEqual(HideoutPanel.Notice, Presenter.Panel);
        }

        // 줄 = 정의별 묶음 "이름 ×n"(첫 등장 순), 수량 합 = 정의 수
        public static void AssertItemLines(List<string> lines, IReadOnlyList<ItemDefinitionSO> definitions)
        {
            if (definitions.Count == 0)
            {
                CollectionAssert.AreEqual(new[] { UiText.ResultNone }, lines);
                return;
            }
            var expected = new List<string>();
            var counts = new Dictionary<string, int>();
            foreach (ItemDefinitionSO d in definitions)
            {
                string name = RaidResultViewModel.ItemName(d);
                if (!counts.ContainsKey(name))
                {
                    counts[name] = 0;
                    expected.Add(name);
                }
                counts[name]++;
            }
            Assert.AreEqual(expected.Count, lines.Count, "줄 수 = 정의 종류 수: " + string.Join(", ", lines));
            for (int i = 0; i < lines.Count; i++)
            {
                Match m = ItemLine.Match(lines[i]);
                Assert.IsTrue(m.Success, lines[i]);
                Assert.AreEqual(expected[i], m.Groups["name"].Value, "줄 순서·이름");
                int n = m.Groups["n"].Success ? int.Parse(m.Groups["n"].Value) : 1;
                Assert.AreEqual(counts[expected[i]], n, $"{expected[i]} 수량");
            }
        }

        public static string StripCount(string line) => ItemLine.Match(line).Groups["name"].Value;

        // ── 비교용 문자열 ──

        public static string[] SortedIds(IEnumerable<ItemInstance> items) =>
            items.Select(i => i.Definition.id).OrderBy(s => s, System.StringComparer.Ordinal).ToArray();

        public static string[] SortedIds(IEnumerable<ItemDefinitionSO> defs) =>
            defs.Select(d => d.id).OrderBy(s => s, System.StringComparer.Ordinal).ToArray();

        // 반입(Loadout) 항목 = "id@위치" (위치·회전 포함, 탄창 탄 수 포함). 정렬해 비교한다.
        public static string[] Describe(Loadout loadout)
        {
            var lines = new List<string>();
            foreach (LoadoutEntry e in loadout.Entries)
            {
                ItemLocation l = e.Location;
                string where = l.IsEquipped ? $"Equipped:{l.Slot}" : $"{l.Grid}({l.Cell.x},{l.Cell.y}){(l.Rotated ? "R" : "")}";
                string state = e.Item is Magazine m ? $" rounds {m.Rounds}" : "";
                lines.Add($"{e.Item.Definition.id}@{where}{state}");
            }
            for (int i = 0; i < loadout.QuickSlots.Count; i++)
                if (loadout.QuickSlots[i] != null)
                    lines.Add($"quick{i}={loadout.QuickSlots[i].Definition.id}");
            lines.Sort(System.StringComparer.Ordinal);
            return lines.ToArray();
        }

        public static string[] Describe(IInventoryService inventory) => Describe(Loadout.Capture(inventory));
    }

    // 세이브 파일 읽기 (SaveSandbox 저장소). 파싱은 게임과 같은 SaveSerializer.TryParse.
    internal static class M5Save
    {
        public static SaveData Read(FileSaveStorage storage)
        {
            Assert.IsTrue(File.Exists(storage.FilePath), $"세이브 파일 없음: {storage.FilePath}");
            Assert.IsFalse(SaveSandbox.IsUnderPersistentDataPath(storage.FilePath), "세이브가 실제 persistentDataPath에 있다");
            string json = File.ReadAllText(storage.FilePath);
            Assert.IsTrue(SaveSerializer.TryParse(json, out SaveData data, out _, out string error), $"세이브 파싱 실패: {error}\n{json}");
            Assert.AreEqual(SaveSerializer.CurrentVersion, data.version);
            return data;
        }

        public static string[] SortedIds(List<ItemRecord> records) =>
            records.Select(r => r.id).OrderBy(s => s, System.StringComparer.Ordinal).ToArray();

        // 폴더의 파일 이름·크기·수정 시각 (레이드 중 쓰기 없음 확인용)
        public static string Snapshot(FileSaveStorage storage)
        {
            if (!Directory.Exists(storage.DirectoryPath))
                return "(폴더 없음)";
            return string.Join("\n", Directory.GetFiles(storage.DirectoryPath).OrderBy(f => f, System.StringComparer.Ordinal)
                .Select(f =>
                {
                    var info = new FileInfo(f);
                    return $"{info.Name} {info.Length} {info.LastWriteTimeUtc.Ticks}";
                }));
        }

        // 실제 세이브 위치(%USERPROFILE%\AppData\LocalLow\DefaultCompany\Ashfall)의 세이브 파일들
        public static string[] RealSaveFiles()
        {
            var real = new FileSaveStorage(Application.persistentDataPath);
            return new[] { real.FilePath, real.BackupPath, real.TempPath, real.CorruptPath }.Where(File.Exists).ToArray();
        }
    }

    // M5 흐름 테스트 공용 정리: 결과·흐름 레이드 표·정지를 비우고, 은신처·레이드 씬을 MainMenu로 내린 뒤 새 샌드박스 폴더.
    internal static class M5Flow
    {
        public static void ResetFlow()
        {
            GameManager gm = GameManager.Instance;
            if (gm != null && gm.Flow != null)
            {
                gm.Flow.AcknowledgeResult();
                gm.Flow.TakeRaidReturn(out _);
                gm.Flow.SetWorldPaused(false);
            }
            Time.timeScale = 1f;
        }

        public static IEnumerator TearDown(string label)
        {
            ResetFlow();
            string active = SceneManager.GetActiveScene().name;
            if (active != SceneNames.MainMenu)
                yield return M4Raid.LoadScene(SceneNames.MainMenu);
            ResetFlow();
            if (GameManager.Instance != null)
                SaveSandbox.UseFreshStorage(label);
        }

        // 레이드·은신처 관련 이벤트 구독 수 (메뉴 기준과 같아야 한다 — 씬 객체가 내려가며 모두 해지)
        public static Dictionary<string, int> SubscriberCounts(EventBus bus) => new Dictionary<string, int>
        {
            { nameof(PlayerDied), bus.SubscriberCount<PlayerDied>() },
            { nameof(EnemyKilled), bus.SubscriberCount<EnemyKilled>() },
            { nameof(RaidAbandonRequested), bus.SubscriberCount<RaidAbandonRequested>() },
            { nameof(RaidEnded), bus.SubscriberCount<RaidEnded>() },
            { nameof(RaidStarted), bus.SubscriberCount<RaidStarted>() },
            { nameof(ExtractionStatusChanged), bus.SubscriberCount<ExtractionStatusChanged>() },
            { nameof(NoiseEmitted), bus.SubscriberCount<NoiseEmitted>() },
            { nameof(InventoryChanged), bus.SubscriberCount<InventoryChanged>() },
            { nameof(EquipmentChanged), bus.SubscriberCount<EquipmentChanged>() },
            { nameof(ScreenStateChanged), bus.SubscriberCount<ScreenStateChanged>() },
            { nameof(HideoutChanged), bus.SubscriberCount<HideoutChanged>() },
        };

        public static void AssertNoLeak(EventBus bus, Dictionary<string, int> baseline, string when)
        {
            Dictionary<string, int> now = SubscriberCounts(bus);
            foreach (KeyValuePair<string, int> kv in baseline)
                Assert.AreEqual(kv.Value, now[kv.Key], $"{when}: {kv.Key} 구독 수 (누수)");
        }

        // 레이드 맵 상자 하나를 열어(F 상호작용과 같은 호출) 검색이 끝나기를 기다린 뒤, 저장 공간에 들어가는 아이템 하나를 가져온다.
        public static IEnumerator TakeCrateLoot(M4Raid raid, List<ItemInstance> taken)
        {
            foreach (LootContainer crate in Object.FindObjectsByType<LootContainer>(FindObjectsSortMode.InstanceID))
            {
                if (crate.LootTable == null)
                    continue;
                crate.Interact(raid.InventoryBehaviour);
                ILootView loot = raid.Inventory.ActiveLoot;
                if (loot == null || loot.Items.Count == 0)
                {
                    raid.Inventory.CloseLoot();
                    continue;
                }
                yield return M3Scene.WaitUntil(() => loot.SearchComplete, 30f, () => $"상자 검색 ({crate.name})");
                foreach (ItemInstance item in loot.Items.Select(p => p.Item).ToList())
                {
                    if (raid.Inventory.TryQuickMove(item))
                    {
                        taken.Add(item);
                        raid.Inventory.CloseLoot();
                        yield return null;
                        yield break;
                    }
                }
                raid.Inventory.CloseLoot();
            }
            Assert.Fail("저장 공간에 들어가는 상자 루트를 찾지 못했다");
        }
    }
}
#endif
