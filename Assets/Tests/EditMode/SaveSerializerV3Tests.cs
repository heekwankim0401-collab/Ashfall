using Ashfall.Core;
using Ashfall.Hideout;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // save.json version 3 (MVP 마무리 설계 §1.6, §7 AC-F2, MF-Q6): stashLevel 왕복, v1·v2 → 단계 0, 설정 최대로 자름, v4 거부.
    public class SaveSerializerV3Tests
    {
        private FakeCatalog catalog;
        private HideoutConfigSO config;
        private SaveLoadReport report;

        [SetUp]
        public void SetUp()
        {
            catalog = FakeCatalog.Standard();
            config = M5TestData.HideoutConfig(catalog);   // stashUpgrades = C# 기본값 (10×40 5000, 10×50 12000)
            report = new SaveLoadReport();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(config);

        private PlayerProfile Load(string json, ProfileDefaults defaults)
        {
            Assert.IsTrue(SaveSerializer.TryParse(json, out SaveData data, out SaveLoadStatus status, out string error), error);
            Assert.AreEqual(SaveLoadStatus.Loaded, status);
            return SaveSerializer.ToProfile(data, catalog, defaults, report);
        }

        private PlayerProfile Load(string json) => Load(json, config.ToProfileDefaults());

        private static string Json(int version, int stashLevel, string stash = "") =>
            "{\"version\": " + version + ", \"casings\": 10, \"stashLevel\": " + stashLevel + ", \"stash\": [" + stash + "]}";

        private static string PipeAt(int x, int y) =>
            "{\"id\": \"JNK-01\", \"container\": \"Stash\", \"x\": " + x + ", \"y\": " + y + ", \"rotated\": false, \"rounds\": -1, \"usesLeft\": -1}";

        // ── 쓰기 ─────────────────────────────────────────────

        [Test]
        public void ToJson_WritesVersion3_AndStashLevel()
        {
            var profile = new PlayerProfile(new Vector2Int(10, 50), 0, 2);
            string json = SaveSerializer.ToJson(profile, M5TestData.Epoch);
            StringAssert.Contains("\"version\": 3", json);
            StringAssert.Contains("\"stashLevel\": 2", json);
            Assert.AreEqual(2, SaveSerializer.ToData(profile, M5TestData.Epoch).stashLevel);
        }

        // ── 왕복 ─────────────────────────────────────────────

        [Test]
        public void RoundTrip_Level2_KeepsSizeAndItemsInRows40To49()
        {
            var profile = new PlayerProfile(config.ToProfileDefaults().StashSizeFor(0), 0);
            Assert.IsTrue(profile.TryUpgradeStash(new Vector2Int(10, 40)));
            Assert.IsTrue(profile.TryUpgradeStash(new Vector2Int(10, 50)));
            var low = new ItemInstance(TestData.Pipe());
            var high = new ItemInstance(TestData.Pipe());
            Assert.IsTrue(profile.Stash.TryPlace(low, new Vector2Int(9, 41), false));
            Assert.IsTrue(profile.Stash.TryPlace(high, new Vector2Int(0, 47), false));   // 1×3 → 47~49줄

            PlayerProfile loaded = Load(SaveSerializer.ToJson(profile, M5TestData.Epoch));

            Assert.AreEqual(2, loaded.StashLevel);
            Assert.AreEqual(new Vector2Int(10, 50), loaded.Stash.Size);
            Assert.AreEqual(2, loaded.Stash.Items.Count);
            Assert.AreEqual(new Vector2Int(9, 41), loaded.Stash.Items[0].Cell);
            Assert.AreEqual(new Vector2Int(0, 47), loaded.Stash.Items[1].Cell);
            Assert.AreEqual(0, report.DroppedItemCount);
        }

        [Test]
        public void V3_Level1_Size10x40()
        {
            PlayerProfile loaded = Load(Json(3, 1, PipeAt(0, 37)));
            Assert.AreEqual(1, loaded.StashLevel);
            Assert.AreEqual(new Vector2Int(10, 40), loaded.Stash.Size);
            Assert.AreEqual(new Vector2Int(0, 37), loaded.Stash.Items[0].Cell, "30줄 밖이지만 단계 1 범위 안 — 기록된 자리");
        }

        // ── 이전 ─────────────────────────────────────────────

        [TestCase(1)]
        [TestCase(2)]
        public void V1V2_Level0_EvenIfFieldPresent(int version)
        {
            PlayerProfile loaded = Load(Json(version, 2));
            Assert.AreEqual(0, loaded.StashLevel, "필드 값이 아니라 버전으로 판정");
            Assert.AreEqual(new Vector2Int(10, 30), loaded.Stash.Size);
        }

        [Test]
        public void V3_MissingField_Level0()
        {
            PlayerProfile loaded = Load("{\"version\": 3, \"casings\": 5}");
            Assert.AreEqual(0, loaded.StashLevel);
            Assert.AreEqual(new Vector2Int(10, 30), loaded.Stash.Size);
        }

        [Test]
        public void LevelAboveConfigMax_ClampedToMax()
        {
            PlayerProfile loaded = Load(Json(3, 7));
            Assert.AreEqual(2, loaded.StashLevel);
            Assert.AreEqual(new Vector2Int(10, 50), loaded.Stash.Size);
        }

        [Test]
        public void NegativeLevel_Level0()
        {
            Assert.AreEqual(0, Load(Json(3, -3)).StashLevel);
        }

        // 설정에서 단계를 없앤 경우(단계 1개 기본값) → 0, 범위 밖 아이템은 기존 규칙(자동 배치)
        [Test]
        public void SingleLevelDefaults_ClampTo0_OutOfRangeItemAutoPlaced()
        {
            PlayerProfile loaded = Load(Json(3, 2, PipeAt(0, 45)), M5TestData.Defaults());
            Assert.AreEqual(0, loaded.StashLevel);
            Assert.AreEqual(M5TestData.StashSize, loaded.Stash.Size);
            Assert.AreEqual(1, loaded.Stash.Items.Count, "자동 배치");
            Assert.AreEqual(Vector2Int.zero, loaded.Stash.Items[0].Cell);
        }

        [TestCase(0, 3, 5, 0)]
        [TestCase(2, 2, 5, 0)]
        [TestCase(2, 3, 5, 2)]
        [TestCase(9, 3, 2, 2)]
        [TestCase(-1, 3, 2, 0)]
        [TestCase(1, 3, 0, 0)]
        public void ReadStashLevel(int recorded, int version, int max, int expected)
        {
            Assert.AreEqual(expected, SaveSerializer.ReadStashLevel(recorded, version, max));
        }

        // ── 버전 ─────────────────────────────────────────────

        [Test]
        public void V4_Unsupported()
        {
            Assert.IsFalse(SaveSerializer.TryParse("{\"version\": 4, \"stashLevel\": 1}", out SaveData data, out SaveLoadStatus status, out _));
            Assert.AreEqual(SaveLoadStatus.UnsupportedVersion, status);
            Assert.IsNull(data);
        }

        [Test]
        public void V1V2V3_Parse()
        {
            for (int v = 1; v <= 3; v++)
                Assert.IsTrue(SaveSerializer.TryParse("{\"version\": " + v + "}", out _, out _, out _), "v" + v);
        }

        // ── 기본값 (HideoutConfigSO.ToProfileDefaults) ─────────

        [Test]
        public void ToProfileDefaults_SizesByLevel()
        {
            ProfileDefaults d = config.ToProfileDefaults();
            Assert.AreEqual(new Vector2Int(10, 30), d.StashSize);
            Assert.AreEqual(2, d.MaxStashLevel);
            Assert.AreEqual(new Vector2Int(10, 30), d.StashSizeFor(0));
            Assert.AreEqual(new Vector2Int(10, 40), d.StashSizeFor(1));
            Assert.AreEqual(new Vector2Int(10, 50), d.StashSizeFor(2));
            Assert.AreEqual(new Vector2Int(10, 50), d.StashSizeFor(5), "범위 밖은 자름");
            Assert.AreEqual(new Vector2Int(10, 30), d.StashSizeFor(-1));
            Assert.AreEqual(M5TestData.StartingCasings, d.StartingCasings);
        }

        [Test]
        public void ProfileDefaults_LegacyConstructor_SingleLevel()
        {
            var d = new ProfileDefaults(new Vector2Int(4, 5), 7);
            Assert.AreEqual(0, d.MaxStashLevel);
            Assert.AreEqual(new Vector2Int(4, 5), d.StashSizeFor(0));
            Assert.AreEqual(new Vector2Int(4, 5), d.StashSizeFor(3));
            Assert.AreEqual(0, default(ProfileDefaults).MaxStashLevel);
        }

        // 줄 수가 증가하지 않는 단계부터는 무시 (설정 오류에도 TryGrow 실패 없음)
        [Test]
        public void ToProfileDefaults_NonIncreasingStep_Ignored()
        {
            config.stashUpgrades = new[] { new StashUpgradeStep(40, 1), new StashUpgradeStep(40, 2), new StashUpgradeStep(60, 3) };
            ProfileDefaults d = config.ToProfileDefaults();
            Assert.AreEqual(1, d.MaxStashLevel);
            Assert.IsFalse(config.TryGetStashUpgrade(1, out _, out _));
            config.stashUpgrades = null;
            Assert.AreEqual(0, config.ToProfileDefaults().MaxStashLevel);
        }
    }
}
