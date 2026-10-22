#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Linq;
using Ashfall.Core;
using Ashfall.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ashfall.Tests.PlayMode
{
    // M5 AC2 — 세이브 손상·지원 안 하는 버전·알 수 없는 id (M5 설계 §2.4, §7 CorruptSaveFlowTests, TODO(GDD-?) Q22·Q23).
    // 샌드박스 save.json을 직접 써 두고 MainMenu [은신처]로 첫 진입 → 알림 패널, 격리(save.corrupt.json)·새 프로필 또는 레코드 버림,
    // W1이 쓴 새 save.json은 다시 읽힌다. 실제 persistentDataPath는 쓰지 않는다.
    public class CorruptSaveFlowTests
    {
        private FileSaveStorage storage;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Assert.IsNotNull(GameManager.Instance, "GameManager 없음");
            M5Flow.ResetFlow();
            storage = SaveSandbox.UseFreshStorage("corrupt-save");
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return M5Flow.TearDown("after-corrupt-save");
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator GarbageFile_Quarantined_NewProfile_CorruptNotice()
        {
            const string garbage = "{ \"version\": 1, \"casings\": 99999, \"stash\": [ { \"id\": ";   // 잘린 JSON
            yield return EnterWithFile(garbage, SaveLoadStatus.Corrupt, UiText.NoticeCorrupt);
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator EmptyFile_Quarantined_NewProfile_CorruptNotice()
        {
            yield return EnterWithFile(string.Empty, SaveLoadStatus.Corrupt, UiText.NoticeCorrupt);
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator FutureVersion_Quarantined_NewProfile_UnsupportedVersionNotice()
        {
            string future = Json(99, 4321, raidInProgress: false,
                stash: Record("WPN-PST-01", "Stash", x: 0), loadout: "");
            yield return EnterWithFile(future, SaveLoadStatus.UnsupportedVersion, UiText.NoticeUnsupportedVersion);
        }

        // 알 수 없는 id: 그 레코드만 버리고 나머지는 불러온다 (격리 없음)
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator UnknownItemIds_RecordsDropped_RestLoaded_Notice()
        {
            string json = Json(1, 1234, raidInProgress: false,
                stash: Record("WPN-PST-01", "Stash", x: 0) + "," + Record("XXX-NOT-AN-ITEM", "Stash", x: 4) + "," +
                       Record("MAG-9MM-12", "Stash", x: 6, rounds: 7),
                loadout: Record("YYY-NOT-AN-ITEM", "Pocket", x: 0) + "," + Record("MED-BND-01", "Pocket", x: 0, index: 1, usesLeft: 1));
            yield return EnterHideout(json);
            M5Hideout hideout = Current;

            SaveLoadReport report = hideout.Controller.EntryReport;
            Assert.AreEqual(SaveLoadStatus.Loaded, report.Status);
            CollectionAssert.AreEquivalent(new[] { "XXX-NOT-AN-ITEM", "YYY-NOT-AN-ITEM" }, report.UnknownItemIds);
            Assert.IsNull(report.QuarantinedPath, "알 수 없는 id는 격리하지 않는다");
            Assert.IsFalse(File.Exists(storage.CorruptPath));

            Assert.AreEqual(HideoutPanel.Notice, hideout.Presenter.Panel);
            Assert.IsTrue(hideout.Shown(HideoutNoticeView.PanelName));
            Assert.That(hideout.Text(HideoutNoticeView.TextName), Does.Contain(UiText.NoticeUnknownItems(2)));
            Assert.That(hideout.Text(HideoutNoticeView.TextName), Does.Not.Contain(UiText.NoticeCorrupt));
            Assert.IsFalse(hideout.Shown(HideoutNoticeView.ListSectionName), "RULE-03이 아니면 잃은 장비 목록 없음");

            // 나머지는 그대로
            Assert.AreEqual(1234, hideout.Service.Casings);
            CollectionAssert.AreEqual(new[] { "MAG-9MM-12", "WPN-PST-01" }, hideout.StashIds());
            Assert.AreEqual(7, ((Magazine)hideout.StashItem("MAG-9MM-12")).Rounds, "탄창 탄 수 유지");
            CollectionAssert.AreEqual(new[] { "MED-BND-01" },
                M5Hideout.SortedIds(Loadout.Capture(hideout.Inventory).Entries.Select(e => e.Item)), "알 수 있는 장비는 남는다");

            // W1이 쓴 파일엔 알 수 없는 id가 없다
            SaveData saved = M5Save.Read(storage);
            CollectionAssert.AreEqual(new[] { "MAG-9MM-12", "WPN-PST-01" }, M5Save.SortedIds(saved.stash));
            CollectionAssert.AreEqual(new[] { "MED-BND-01" }, M5Save.SortedIds(saved.loadout));
            Assert.AreEqual(1234, saved.casings);

            using (var pointer = new UiPointer())
                yield return hideout.ConfirmNotice(pointer);
            Assert.AreEqual(HideoutPanel.Hub, hideout.Presenter.Panel);
        }

        // ── 도우미 ──

        private M5Hideout Current;

        private IEnumerator EnterHideout(string fileText)
        {
            Directory.CreateDirectory(storage.DirectoryPath);
            File.WriteAllText(storage.FilePath, fileText);
            Assert.IsFalse(GameManager.Instance.Profiles.IsLoaded);

            var menu = new M4Menu();
            yield return M4Raid.LoadScene(SceneNames.MainMenu);
            yield return M4Menu.Attach(menu);
            using (var pointer = new UiPointer())
            {
                yield return pointer.Click(menu.Start);
                Assert.AreNotEqual(UiClickPath.None, pointer.LastPath, "[은신처]가 눌리지 않았다");
            }
            Current = new M5Hideout();
            yield return M5Hideout.Attach(Current, 60f);
        }

        // 손상·버전: 격리 → 새 프로필 → 알림 → W1 새 파일
        private IEnumerator EnterWithFile(string fileText, SaveLoadStatus expected, string noticeLine)
        {
            yield return EnterHideout(fileText);
            M5Hideout hideout = Current;

            SaveLoadReport report = hideout.Controller.EntryReport;
            Assert.AreEqual(expected, report.Status);
            Assert.AreEqual(storage.CorruptPath, report.QuarantinedPath, "save.corrupt.json으로 격리");
            Assert.IsTrue(File.Exists(storage.CorruptPath), "save.corrupt.json 없음");
            Assert.AreEqual(fileText, File.ReadAllText(storage.CorruptPath), "원본 내용 보존");

            // 알림 패널
            Assert.AreEqual(HideoutPanel.Notice, hideout.Presenter.Panel, "알림이 먼저");
            Assert.IsTrue(hideout.Shown(HideoutNoticeView.PanelName));
            Assert.IsFalse(hideout.Shown(HideoutHubView.HubName));
            string text = hideout.Text(HideoutNoticeView.TextName);
            Assert.That(text, Does.Contain(noticeLine));
            Assert.That(text, Does.Contain(UiText.NoticeQuarantined + storage.CorruptPath), "격리 경로 안내");
            Assert.That(text, Does.Not.Contain(UiText.NoticeCrash));
            Assert.IsFalse(hideout.Shown(HideoutNoticeView.ListSectionName), "잃은 장비 목록 없음");

            // 새 프로필
            Assert.AreEqual(6000, hideout.Service.Casings, "시작 탄피");
            Assert.AreEqual(0, hideout.StashItems().Count, "빈 보관함");
            Assert.IsTrue(Loadout.Capture(hideout.Inventory).IsEmpty);
            Assert.IsTrue(hideout.Service.IsKitAvailable);

            // W1이 쓴 새 save.json은 읽을 수 있다
            SaveData saved = M5Save.Read(storage);
            Assert.IsFalse(saved.raidInProgress);
            Assert.AreEqual(6000, saved.casings);
            Assert.AreEqual(0, saved.stash.Count);

            using (var pointer = new UiPointer())
                yield return hideout.ConfirmNotice(pointer);
            Assert.AreEqual(HideoutPanel.Hub, hideout.Presenter.Panel);
            Assert.IsTrue(hideout.Shown(HideoutHubView.HubName));
        }

        private static string Json(int version, int casings, bool raidInProgress, string stash, string loadout) =>
            "{\n" +
            $"  \"version\": {version},\n" +
            "  \"savedAtUtc\": \"2026-10-22T09:30:00.0000000Z\",\n" +
            $"  \"raidInProgress\": {(raidInProgress ? "true" : "false")},\n" +
            $"  \"casings\": {casings},\n" +
            "  \"kitLastClaimUtcTicks\": 0,\n" +
            $"  \"stash\": [{stash}],\n" +
            $"  \"loadout\": [{loadout}],\n" +
            "  \"quickSlots\": [-1, -1, -1, -1]\n" +
            "}";

        private static string Record(string id, string container, int x, int y = 0, int index = 0, int rounds = -1, int usesLeft = -1) =>
            $"{{ \"id\": \"{id}\", \"container\": \"{container}\", \"slot\": \"\", \"index\": {index}, \"x\": {x}, \"y\": {y}, " +
            $"\"rotated\": false, \"rounds\": {rounds}, \"usesLeft\": {usesLeft}, \"loadedMagazineId\": \"\", \"loadedMagazineRounds\": 0 }}";
    }
}
#endif
