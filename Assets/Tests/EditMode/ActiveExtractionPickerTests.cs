using System.Collections.Generic;
using Ashfall.Raid;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // M4 AC2 — §8.2 "스폰 위치 기준 2~3개만 활성" (ActiveExtractionPicker, M4 설계 §3.2 Q2·Q3)
    public class ActiveExtractionPickerTests
    {
        private RaidConfigSO config;

        [SetUp]
        public void SetUp() => config = M4TestData.RaidConfig();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(config);

        private List<string> Pick(string spawnId, int seed)
        {
            var into = new List<string>();
            ActiveExtractionPicker.Pick(config, spawnId, new System.Random(seed), into);
            return into;
        }

        [TestCase(M4TestData.SpawnWest, new[] { M4TestData.Ext01, M4TestData.Ext02, M4TestData.Ext03 })]
        [TestCase(M4TestData.SpawnEast, new[] { M4TestData.Ext01, M4TestData.Ext04, M4TestData.Ext03 })]
        [TestCase(M4TestData.SpawnNorth, new[] { M4TestData.Ext01, M4TestData.Ext02, M4TestData.Ext04 })]
        public void SpawnRule_UsedAsIs(string spawnId, string[] expected)
        {
            for (int seed = 1; seed <= 20; seed++)
                CollectionAssert.AreEqual(expected, Pick(spawnId, seed), $"시드 {seed}");
        }

        [Test]
        public void SpawnRule_MissingAlwaysActive_IsAppended()
        {
            config.spawnRules = new[] { M4TestData.SpawnRule("SP-T", M4TestData.Ext02, M4TestData.Ext04) };
            CollectionAssert.AreEqual(new[] { M4TestData.Ext02, M4TestData.Ext04, M4TestData.Ext01 }, Pick("SP-T", 1),
                "EXT-01 항상 열림 (Q3)");
        }

        [Test]
        public void SpawnRule_UnknownIdsAndDuplicates_Skipped()
        {
            config.spawnRules = new[] { M4TestData.SpawnRule("SP-T", M4TestData.Ext01, "EXT-99", M4TestData.Ext01, M4TestData.Ext03) };
            CollectionAssert.AreEqual(new[] { M4TestData.Ext01, M4TestData.Ext03 }, Pick("SP-T", 1));
        }

        [Test]
        public void NoRule_Ext01PlusRandom_TwoToThree()
        {
            var counts = new HashSet<int>();
            for (int seed = 1; seed <= 200; seed++)
            {
                List<string> ids = Pick("SP-UNKNOWN", seed);
                Assert.AreEqual(M4TestData.Ext01, ids[0], "항상 활성이 먼저");
                Assert.That(ids.Count, Is.InRange(2, 3), $"시드 {seed}");
                CollectionAssert.AllItemsAreUnique(ids);
                foreach (string id in ids)
                    Assert.IsTrue(id == M4TestData.Ext01 || id == M4TestData.Ext02 || id == M4TestData.Ext03 || id == M4TestData.Ext04);
                counts.Add(ids.Count);
            }
            CollectionAssert.AreEquivalent(new[] { 2, 3 }, counts, "2개와 3개가 모두 나온다");
        }

        [Test]
        public void NoRule_EmptyActiveList_FallsBackToRandom()
        {
            config.spawnRules = new[] { M4TestData.SpawnRule(M4TestData.SpawnWest) };
            List<string> ids = Pick(M4TestData.SpawnWest, 3);
            Assert.That(ids.Count, Is.InRange(2, 3));
            Assert.AreEqual(M4TestData.Ext01, ids[0]);
        }

        [Test]
        public void NoRule_SameSeed_SameResult()
        {
            for (int seed = 1; seed <= 20; seed++)
                CollectionAssert.AreEqual(Pick(null, seed), Pick(null, seed), $"시드 {seed} 재현");
        }

        [Test]
        public void Pick_ReturnsAddedCount_AndAppends()
        {
            var into = new List<string> { "기존" };
            int added = ActiveExtractionPicker.Pick(config, M4TestData.SpawnWest, new System.Random(1), into);
            Assert.AreEqual(3, added);
            Assert.AreEqual(4, into.Count);
            Assert.AreEqual("기존", into[0]);
        }

        [Test]
        public void Pick_NullConfig_Zero()
        {
            var into = new List<string>();
            Assert.AreEqual(0, ActiveExtractionPicker.Pick(null, M4TestData.SpawnWest, new System.Random(1), into));
            Assert.IsEmpty(into);
        }

        [Test]
        public void PickSpawnIndex_InRange_AndReproducible()
        {
            Assert.AreEqual(-1, ActiveExtractionPicker.PickSpawnIndex(0, new System.Random(1)), "스폰 없음");
            var seen = new HashSet<int>();
            for (int seed = 1; seed <= 100; seed++)
            {
                int index = ActiveExtractionPicker.PickSpawnIndex(3, new System.Random(seed));
                Assert.That(index, Is.InRange(0, 2));
                Assert.AreEqual(index, ActiveExtractionPicker.PickSpawnIndex(3, new System.Random(seed)));
                seen.Add(index);
            }
            Assert.AreEqual(3, seen.Count, "Q2: 3곳 모두 나온다");
        }
    }
}
