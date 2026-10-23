using System.IO;
using System.Text.RegularExpressions;
using Ashfall.Core;
using Ashfall.EditorTools;
using Ashfall.Hideout;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ashfall.Tests.EditMode
{
    // SYS-HUB 보관함 업그레이드 (§9, MVP 마무리 설계 §1.3·§7 AC-F2, MF-Q5~Q9): 순수 모델 0 → 1 → 2, 탄피 차감·부족·최대, 제자리 확장.
    public class StashUpgradeModelTests
    {
        private FakeCatalog catalog;
        private HideoutConfigSO config;
        private PlayerProfile profile;
        private StashUpgradeModel model;

        [SetUp]
        public void SetUp()
        {
            catalog = FakeCatalog.Standard();
            config = M5TestData.HideoutConfig(catalog);
            profile = new PlayerProfile(M5TestData.StashSize, 20000);
            model = new StashUpgradeModel(profile, config);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(config);

        [Test]
        public void ConfigDefaults_Are40For5000_50For12000()
        {
            var fresh = ScriptableObject.CreateInstance<HideoutConfigSO>();
            Assert.AreEqual(2, fresh.stashUpgrades.Length);
            Assert.AreEqual(40, fresh.stashUpgrades[0].rows);
            Assert.AreEqual(5000, fresh.stashUpgrades[0].costCasings);
            Assert.AreEqual(50, fresh.stashUpgrades[1].rows);
            Assert.AreEqual(12000, fresh.stashUpgrades[1].costCasings);
            Object.DestroyImmediate(fresh);
        }

        [Test]
        public void Next_FromLevel0()
        {
            StashUpgradeOffer next = model.Next;
            Assert.IsTrue(next.HasNext);
            Assert.AreEqual(new Vector2Int(10, 40), next.NewSize);
            Assert.AreEqual(5000, next.Cost);
            Assert.AreEqual(0, model.Level);
        }

        [Test]
        public void Upgrade_0To1To2_DeductsCasings_ThenMaxLevel()
        {
            ItemGrid stash = profile.Stash;
            Assert.AreEqual(StashUpgradeResult.Ok, model.TryUpgrade());
            Assert.AreEqual(1, profile.StashLevel);
            Assert.AreEqual(new Vector2Int(10, 40), profile.Stash.Size);
            Assert.AreEqual(15000, profile.Casings);

            Assert.AreEqual(StashUpgradeResult.Ok, model.TryUpgrade());
            Assert.AreEqual(2, profile.StashLevel);
            Assert.AreEqual(new Vector2Int(10, 50), profile.Stash.Size);
            Assert.AreEqual(3000, profile.Casings);
            Assert.AreSame(stash, profile.Stash, "같은 그리드를 제자리에서 키운다");

            Assert.IsFalse(model.Next.HasNext);
            Assert.AreEqual(StashUpgradeResult.MaxLevel, model.TryUpgrade());
            Assert.AreEqual(2, profile.StashLevel);
            Assert.AreEqual(3000, profile.Casings);
        }

        [Test]
        public void NotEnoughCasings_NoChange()
        {
            profile.Casings = 4999;
            Assert.AreEqual(StashUpgradeResult.NotEnoughCasings, model.TryUpgrade());
            Assert.AreEqual(0, profile.StashLevel);
            Assert.AreEqual(M5TestData.StashSize, profile.Stash.Size);
            Assert.AreEqual(4999, profile.Casings);

            profile.Casings = 5000;
            Assert.AreEqual(StashUpgradeResult.Ok, model.TryUpgrade(), "정확히 비용이면 된다");
            Assert.AreEqual(0, profile.Casings);
        }

        [Test]
        public void Upgrade_KeepsItemPlacement_NewRowsUsable()
        {
            var pipe = new ItemInstance(TestData.Pipe());
            Assert.IsTrue(profile.Stash.TryPlace(pipe, new Vector2Int(3, 27), false));   // 27~29줄
            var later = new ItemInstance(TestData.Pipe());
            Assert.IsFalse(profile.Stash.CanPlace(later, new Vector2Int(0, 35), false));

            Assert.AreEqual(StashUpgradeResult.Ok, model.TryUpgrade());
            Assert.IsTrue(profile.Stash.TryGetPlacement(pipe, out PlacedItem placed));
            Assert.AreEqual(new Vector2Int(3, 27), placed.Cell);
            Assert.IsTrue(profile.Stash.TryPlace(later, new Vector2Int(0, 35), false), "새 줄에 놓을 수 있다");
        }

        // 그리드가 이미 다음 단계 크기 이상(설정과 어긋남) → 확장 실패 → 탄피 그대로
        [Test]
        public void GrowFails_Unavailable_NoCharge()
        {
            var big = new PlayerProfile(new Vector2Int(10, 45), 9000);   // 단계 0인데 45줄
            var m = new StashUpgradeModel(big, config);
            Assert.AreEqual(StashUpgradeResult.Unavailable, m.TryUpgrade());
            Assert.AreEqual(9000, big.Casings);
            Assert.AreEqual(0, big.StashLevel);
        }

        [Test]
        public void ProfileTryUpgradeStash_RejectsShrinkAndSame()
        {
            Assert.IsFalse(profile.TryUpgradeStash(M5TestData.StashSize));
            Assert.IsFalse(profile.TryUpgradeStash(new Vector2Int(10, 20)));
            Assert.IsFalse(profile.TryUpgradeStash(new Vector2Int(9, 40)), "너비 감소");
            Assert.AreEqual(0, profile.StashLevel);
            Assert.AreEqual(0, new PlayerProfile(Vector2Int.one, 0, -2).StashLevel, "음수 단계 → 0");
        }

        [Test]
        public void Constructor_Nulls_Throw()
        {
            Assert.Throws<System.ArgumentNullException>(() => new StashUpgradeModel(null, config));
            Assert.Throws<System.ArgumentNullException>(() => new StashUpgradeModel(profile, null));
        }
    }

    // HideoutService 위임 (MF-Q8: 읽기 전용 허용, 떠나는 중 Unavailable) + HideoutChanged
    public class HideoutServiceStashUpgradeTests
    {
        private MemorySaveStorage storage;
        private FakeCatalog catalog;
        private FakeClock clock;
        private ProfileService profiles;
        private FlowRecorder rec;
        private GameFlow flow;
        private HideoutConfigSO config;
        private TraderConfigSO trader;
        private int changedCount;

        [SetUp]
        public void SetUp()
        {
            storage = new MemorySaveStorage();
            catalog = FakeCatalog.Standard();
            clock = new FakeClock();
            profiles = new ProfileService(storage, clock.Now);
            rec = new FlowRecorder();
            flow = rec.Create(profiles);
            config = M5TestData.HideoutConfig(catalog);
            trader = M5TestData.TraderConfig(catalog);
            changedCount = 0;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(config);
            Object.DestroyImmediate(trader);
        }

        private HideoutService InHideout()
        {
            flow.OnSceneLoaded(SceneNames.MainMenu);
            Assert.IsTrue(flow.EnterHideout());
            flow.OnSceneLoaded(SceneNames.Hideout);
            HideoutService.Enter(profiles, flow, catalog, config.ToProfileDefaults());
            return new HideoutService(profiles, flow, config, trader, () => null, clock.Now, () => changedCount++);
        }

        [Test]
        public void TryUpgradeStash_Ok_PublishesChanged_StashReferenceSame()
        {
            HideoutService service = InHideout();
            IGridView stash = service.Stash;
            Assert.AreEqual(0, service.StashLevel);
            Assert.AreEqual(new Vector2Int(10, 40), service.NextStashUpgrade.NewSize);
            Assert.AreEqual(5000, service.NextStashUpgrade.Cost);
            int changed = changedCount;

            Assert.AreEqual(StashUpgradeResult.Ok, service.TryUpgradeStash());
            Assert.AreEqual(changed + 1, changedCount);
            Assert.AreEqual(1, service.StashLevel);
            Assert.AreEqual(1000, service.Casings);
            Assert.AreSame(stash, service.Stash);
            Assert.AreEqual(new Vector2Int(10, 40), service.Stash.Size);
            Assert.AreEqual(12000, service.NextStashUpgrade.Cost);

            Assert.AreEqual(StashUpgradeResult.NotEnoughCasings, service.TryUpgradeStash());
            Assert.AreEqual(changed + 1, changedCount, "실패는 알리지 않는다");
        }

        [Test]
        public void TryUpgradeStash_Leaving_Unavailable()
        {
            HideoutService service = InHideout();
            Assert.IsTrue(service.StartRaid());
            Assert.AreEqual(StashUpgradeResult.Unavailable, service.TryUpgradeStash());
            Assert.AreEqual(0, profiles.Profile.StashLevel);
        }

        // 메모리에서만 바뀌고 다음 저장(W2/W3/W4)에 기록된다 → 다시 읽으면 단계 유지
        [Test]
        public void Upgrade_SavedOnNextSave_ReloadKeepsLevel()
        {
            HideoutService service = InHideout();
            int writes = storage.WriteCount;
            Assert.AreEqual(StashUpgradeResult.Ok, service.TryUpgradeStash());
            Assert.AreEqual(writes, storage.WriteCount, "업그레이드 자체는 저장하지 않는다");
            Assert.IsTrue(service.SaveNow());
            StringAssert.Contains("\"stashLevel\": 1", storage.Text);

            var next = new ProfileService(storage, clock.Now);
            next.EnsureLoaded(catalog, config.ToProfileDefaults());
            Assert.AreEqual(1, next.Profile.StashLevel);
            Assert.AreEqual(new Vector2Int(10, 40), next.Profile.Stash.Size);
        }

        // MF-Q8: 읽기 전용(세이브를 못 읽음)에서도 상인처럼 허용 — 저장만 안 된다
        [Test]
        public void ReadOnly_UpgradeAllowed()
        {
            storage.Text = "{\"version\": 3, \"casings\": 9000}";
            storage.ReadFails = true;
            profiles = new ProfileService(storage, clock.Now, _ => { });
            flow = rec.Create(profiles);
            LogAssert.Expect(LogType.Error, new Regex("세이브를 읽지 못했다"));
            HideoutService service = InHideout();
            Assert.IsTrue(service.IsReadOnly);
            Assert.AreEqual(StashUpgradeResult.Ok, service.TryUpgradeStash());
            Assert.AreEqual(1, service.StashLevel);
        }
    }

    // HideoutConfig.asset 명시 기록 (MvpFinishDataSetup, 설계 §1.7) + 재실행 바이트 동일
    public class MvpFinishDataAssetTests
    {
        private const string HideoutConfigPath = "Assets/_Project/Data/Hideout/HideoutConfig.asset";

        [Test]
        public void HideoutConfigAsset_HasStashUpgrades()
        {
            Assert.IsTrue(SetupUtil.IsFieldSerialized(HideoutConfigPath, nameof(HideoutConfigSO.stashUpgrades)), "파일에 명시 기록");
            var config = AssetDatabase.LoadAssetAtPath<HideoutConfigSO>(HideoutConfigPath);
            Assert.IsNotNull(config);
            Assert.AreEqual(new Vector2Int(10, 30), config.stashSize);
            Assert.AreEqual(2, config.stashUpgrades.Length);
            Assert.AreEqual(40, config.stashUpgrades[0].rows);
            Assert.AreEqual(5000, config.stashUpgrades[0].costCasings);
            Assert.AreEqual(50, config.stashUpgrades[1].rows);
            Assert.AreEqual(12000, config.stashUpgrades[1].costCasings);
            Assert.AreEqual(2, config.ToProfileDefaults().MaxStashLevel);
        }

        [Test]
        public void ApplyHideoutConfig_Rerun_KeepsBytes()
        {
            string full = Path.GetFullPath(HideoutConfigPath);
            byte[] before = File.ReadAllBytes(full);
            MvpFinishDataSetup.ApplyHideoutConfig();
            AssetDatabase.SaveAssets();
            CollectionAssert.AreEqual(before, File.ReadAllBytes(full));
        }
    }
}
