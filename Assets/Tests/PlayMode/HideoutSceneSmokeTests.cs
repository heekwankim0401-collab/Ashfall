#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Linq;
using Ashfall.Core;
using Ashfall.Hideout;
using Ashfall.Inventory;
using Ashfall.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Ashfall.Tests.PlayMode
{
    // M5 AC1 스모크 (M5 설계 §7 HideoutSceneSmokeTests, SYS-HUB): Ashfall/Setup/Create M5 Hideout Scene이 만든 Hideout 씬을
    // 이름으로 불러(빌드 설정) 흐름 단계·서비스 등록·UI 요소·진입 저장(W1)·부랑자 키트 수령 → 보관함·[장비] 탭 보관함 표시를 본다.
    // 세이브는 SaveSandbox의 새 임시 폴더(새 프로필). 콘솔 오류가 나면 테스트 프레임워크가 실패시킨다.
    public class HideoutSceneSmokeTests
    {
        private FileSaveStorage storage;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            GameManager gm = GameManager.Instance;
            Assert.IsNotNull(gm, "GameManager 없음");
            // 앞 테스트의 결과·흐름 레이드 표·정지가 남지 않게 (흐름 밖 결과는 은신처가 표시만 한다 — 이 테스트는 허브를 본다)
            gm.Flow.AcknowledgeResult();
            gm.Flow.TakeRaidReturn(out _);
            gm.Flow.SetWorldPaused(false);
            Time.timeScale = 1f;
            storage = SaveSandbox.UseFreshStorage("hideout-smoke");
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            // 은신처를 내려 다음 테스트에 서비스·구독이 남지 않게 (MainMenu는 프로필을 읽지 않는다)
            if (SceneManager.GetActiveScene().name == SceneNames.Hideout)
                yield return M4Raid.LoadScene(SceneNames.MainMenu);
            GameManager gm = GameManager.Instance;
            if (gm != null)
                SaveSandbox.UseFreshStorage("after-hideout-smoke");
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator HideoutScene_Loads_RegistersServices_ShowsHub_ClaimsKitIntoStash()
        {
            Assert.IsFalse(File.Exists(storage.FilePath), "새 폴더는 비어 있어야 한다");
            yield return M4Raid.LoadScene(SceneNames.Hideout);
            yield return M4Raid.WaitForHideout();
            GameManager gm = GameManager.Instance;

            // ── 흐름·구성 ──
            Assert.AreEqual(GamePhase.Hideout, gm.Flow.Phase, "Hideout 단계");
            Assert.AreEqual(1f, Time.timeScale);
            Assert.AreEqual(1, Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Length, "카메라 수");
            Assert.AreEqual(CursorLockMode.None, UnityEngine.Cursor.lockState, "은신처에서 커서가 잠겨 있다");
            Assert.IsTrue(UnityEngine.Cursor.visible, "커서 숨김");

            HideoutController controller = Single<HideoutController>();
            HideoutInventoryBehaviour inventoryBehaviour = Single<HideoutInventoryBehaviour>();
            HideoutRoot root = Single<HideoutRoot>();
            Assert.IsTrue(controller.enabled, "HideoutController 꺼짐 (참조 누락?)");
            Assert.IsTrue(inventoryBehaviour.enabled, "HideoutInventoryBehaviour 꺼짐");
            Assert.IsNotNull(controller.Config, "HideoutConfig 연결");
            Assert.IsNotNull(controller.Trader, "TraderConfig 연결");
            Assert.IsNotNull(controller.ItemDatabase, "ItemDatabase 연결");
            Assert.IsNotNull(inventoryBehaviour.Config, "InventoryConfig 연결");
            Assert.AreSame(controller.gameObject, inventoryBehaviour.gameObject, "HideoutSystems 한 오브젝트");

            // ── 서비스 ──
            Assert.IsTrue(gm.Services.TryGet(out IHideoutService hideout), "IHideoutService 등록");
            Assert.AreSame(controller.Service, hideout);
            Assert.IsTrue(gm.Services.TryGet(out IInventoryService inventory), "IInventoryService 등록");
            Assert.AreSame(inventoryBehaviour.Inventory, inventory);
            Assert.IsFalse(gm.Services.TryGet(out IMagazineSource _), "은신처는 IMagazineSource를 등록하지 않는다");

            // ── 새 프로필 + 진입 저장(W1) ──
            Assert.IsTrue(gm.Profiles.IsLoaded);
            PlayerProfile profile = gm.Profiles.Profile;
            Assert.AreEqual(controller.Config.startingCasings, hideout.Casings, "시작 탄피");
            Assert.AreEqual(6000, hideout.Casings, "Q9 시작 탄피 6000");
            Assert.AreEqual(new Vector2Int(10, 30), profile.Stash.Size, "§9 보관함 10×30");
            Assert.AreEqual(0, profile.Stash.Items.Count, "새 보관함은 비어 있다");
            Assert.IsTrue(profile.Loadout.IsEmpty, "새 은신처 장비는 비어 있다");
            Assert.IsTrue(hideout.IsKitAvailable, "새 프로필은 키트를 바로 받는다 (Q13)");
            Assert.IsFalse(hideout.LastSaveFailed, "진입 저장 실패");
            Assert.IsTrue(File.Exists(storage.FilePath), $"진입 저장 파일 없음: {storage.FilePath}");
            Assert.IsFalse(SaveSandbox.IsUnderPersistentDataPath(storage.FilePath));
            AssertSaveFile(raidInProgress: false, casings: 6000, stashCount: 0);

            // 보관함 = 열린 외부 그리드 자리 (Q28)
            ILootView loot = inventory.ActiveLoot;
            Assert.IsNotNull(loot, "보관함이 열려 있지 않다");
            Assert.AreEqual(new Vector2Int(10, 30), loot.Size);
            Assert.AreEqual(GridKind.Loot, loot.Ref.Kind);
            Assert.AreSame(profile.Stash, hideout.Stash);
            Assert.AreEqual(inventoryBehaviour.StashDisplayName, loot.DisplayName);

            // ── UI ──
            HideoutPresenter presenter = root.Presenter;
            Assert.AreEqual(HideoutPanel.Hub, presenter.Panel, "알림·결과 없음 → 허브");
            Assert.AreEqual(HideoutTab.Loadout, presenter.Tab, "첫 탭 = 장비");
            var document = root.GetComponent<UIDocument>();
            Assert.IsNotNull(document.panelSettings, "PanelSettings 없음");
            Assert.IsNotNull(document.visualTreeAsset, "HideoutUI.uxml 없음");
            VisualElement ui = document.rootVisualElement;
            Assert.IsNotNull(ui.Q<VisualElement>(HideoutHubView.RootName), "hideout-root");
            Assert.IsTrue(UiQuery.IsShown(ui.Q<VisualElement>(HideoutHubView.HubName)), "허브 표시");
            Assert.IsFalse(UiQuery.IsShown(ui.Q<VisualElement>(HideoutNoticeView.PanelName)), "알림 패널 숨김");
            Assert.IsFalse(UiQuery.IsShown(ui.Q<VisualElement>(RaidResultView.PanelName)), "결과 패널 숨김");
            Assert.AreEqual(UiText.Casings(6000), ui.Q<Label>(HideoutHubView.CasingsName).text, "탄피 문구");
            Assert.AreEqual(UiText.HideoutKitReady, ui.Q<Label>(HideoutHubView.KitStatusName).text, "키트 상태 문구");
            foreach (string name in new[] { HideoutHubView.TabLoadoutName, HideoutHubView.TabTraderName, HideoutHubView.KitButtonName,
                         HideoutHubView.StartRaidName, HideoutHubView.MainMenuName })
            {
                Button b = ui.Q<Button>(name);
                Assert.IsNotNull(b, $"버튼 {name} 없음");
                Assert.IsTrue(UiQuery.IsShown(b), $"버튼 {name} 숨김");
                Assert.IsTrue(b.enabledInHierarchy, $"버튼 {name} 비활성");
            }
            Assert.IsTrue(UiQuery.IsShown(ui.Q<VisualElement>(InventoryScreenView.ScreenName)), "[장비] 탭 = 인벤토리 화면");
            Assert.IsFalse(UiQuery.IsShown(ui.Q<VisualElement>(TraderView.PanelName)), "[상인] 패널 숨김");
            Assert.IsNotNull(ui.Q<ScrollView>(HideoutHubView.StashScrollName), "보관함 ScrollView");
            Assert.AreEqual(inventoryBehaviour.StashDisplayName, ui.Q<Label>(InventoryScreenView.LootTitleName).text, "보관함 제목");
            Assert.AreEqual(0, StashItemElements(ui).Count, "빈 보관함");

            // ── 부랑자 키트 수령 → 보관함 (§9: 녹슨 권총 + 권총 탄창 ×2 + 붕대) ──
            using (var pointer = new UiPointer())
            {
                yield return pointer.Click(ui.Q<Button>(HideoutHubView.KitButtonName));
            }
            Assert.IsFalse(hideout.IsKitAvailable, "수령 후 쿨다운");
            Assert.Greater(hideout.KitCooldownRemainingSec, controller.Config.vagrantKitCooldownSec - 60f, "쿨다운 거의 전부 남음");
            var stashIds = profile.Stash.Items.Select(p => p.Item.Definition.id).OrderBy(s => s, System.StringComparer.Ordinal).ToArray();
            CollectionAssert.AreEqual(new[] { "MAG-9MM-12", "MAG-9MM-12", "MED-BND-01", "WPN-PST-01" }, stashIds, "키트 내용");
            var pistol = profile.Stash.Items.Select(p => p.Item).OfType<WeaponItem>().Single();
            Assert.IsNull(pistol.LoadedMagazine, "권총은 탄창 없이 (Q17)");
            foreach (Magazine m in profile.Stash.Items.Select(p => p.Item).OfType<Magazine>())
                Assert.AreEqual(m.Capacity, m.Rounds, "탄창 가득");
            Assert.AreEqual(6000, hideout.Casings, "키트는 무료");

            // UI 다시 그림: 키트 문구·버튼, [장비] 탭의 보관함 패널에 4개
            yield return null;
            yield return null;
            Assert.AreEqual(HideoutPanel.Hub, presenter.Panel);
            Assert.AreNotEqual(UiText.HideoutKitReady, ui.Q<Label>(HideoutHubView.KitStatusName).text, "키트 문구가 바뀌지 않았다");
            Assert.IsFalse(ui.Q<Button>(HideoutHubView.KitButtonName).enabledInHierarchy, "쿨다운 중 키트 버튼 비활성");
            Assert.AreEqual(4, loot.Items.Count, "열린 보관함 = 프로필 보관함");
            Assert.AreEqual(4, StashItemElements(ui).Count, "[장비] 탭 보관함에 키트 4개가 그려진다");
            Assert.AreEqual(0, StashItemElements(ui).Count(e => e.ClassListContains(InventoryScreenView.HiddenItemClass)),
                "보관함은 검색 없이 전부 드러남");

            // [상인] 탭: 보관함 매도 줄 4개, 다시 [장비]
            Assert.IsTrue(presenter.SelectTab(HideoutTab.Trader));
            yield return null;
            Assert.IsTrue(UiQuery.IsShown(ui.Q<VisualElement>(TraderView.PanelName)), "[상인] 패널 표시");
            Assert.IsFalse(UiQuery.IsShown(ui.Q<VisualElement>(InventoryScreenView.ScreenName)), "[상인] 탭에서 인벤토리 숨김");
            Assert.AreEqual(4, ui.Q<VisualElement>(TraderView.SellName).Query<VisualElement>(className: TraderView.RowClass).ToList().Count,
                "매도 줄 = 보관함 아이템");
            Assert.Greater(ui.Q<VisualElement>(TraderView.OffersName).Query<VisualElement>(className: TraderView.RowClass).ToList().Count, 0,
                "판매 목록");
            Assert.IsTrue(presenter.SelectTab(HideoutTab.Loadout));
            yield return null;
            Assert.IsTrue(UiQuery.IsShown(ui.Q<VisualElement>(InventoryScreenView.ScreenName)));

            // 거래·키트는 메모리에서만 (Q7): 파일은 진입 저장 그대로
            AssertSaveFile(raidInProgress: false, casings: 6000, stashCount: 0);
        }

        private static T Single<T>() where T : Object
        {
            T[] found = Object.FindObjectsByType<T>(FindObjectsSortMode.None);
            Assert.AreEqual(1, found.Length, $"{typeof(T).Name} 수");
            return found[0];
        }

        private static System.Collections.Generic.List<VisualElement> StashItemElements(VisualElement ui) =>
            ui.Q<VisualElement>(InventoryScreenView.LootGridName).Query<VisualElement>(className: InventoryScreenView.ItemClass).ToList();

        private void AssertSaveFile(bool raidInProgress, int casings, int stashCount)
        {
            string json = File.ReadAllText(storage.FilePath);
            Assert.IsTrue(SaveSerializer.TryParse(json, out SaveData data, out _, out string error), $"세이브 파싱 실패: {error}");
            Assert.AreEqual(SaveSerializer.CurrentVersion, data.version);
            Assert.AreEqual(raidInProgress, data.raidInProgress, "raidInProgress");
            Assert.AreEqual(casings, data.casings, "casings");
            Assert.AreEqual(stashCount, data.stash.Count, "stash 레코드 수");
        }
    }
}
#endif
