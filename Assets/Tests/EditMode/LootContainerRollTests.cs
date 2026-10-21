using System.Collections.Generic;
using System.Linq;
using Ashfall.Core;
using Ashfall.Inventory;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // M4 맵 상자 — LootContainer의 LootTableSO 굴림 (TODO(GDD-?) Q17, Q7 신호탄 고정 상자, M4 설계 §4.4)
    public class LootContainerRollTests
    {
        private readonly List<Object> created = new List<Object>();
        private ItemDefinitionSO flare, wires, pipe;
        private LootTableSO table;

        [SetUp]
        public void SetUp()
        {
            flare = Track(M4TestData.Flare());
            wires = Track(TestData.Wires());
            pipe = Track(TestData.Pipe());
            table = Track(ScriptableObject.CreateInstance<LootTableSO>());
            table.id = "LT-TEST";
            table.entries = new List<LootEntry>
            {
                new LootEntry { item = wires, weight = 5f },
                new LootEntry { item = pipe, weight = 3f },
                new LootEntry { item = flare, weight = 1f },
            };
            table.rollCount = new Vector2Int(1, 3);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in created)
                if (o != null)
                    Object.DestroyImmediate(o);
            created.Clear();
        }

        private T Track<T>(T obj) where T : Object
        {
            created.Add(obj);
            return obj;
        }

        private LootContainer NewCrate(ItemDefinitionSO[] contents, LootTableSO lootTable, int seed,
            Vector2Int? gridSize = null)
        {
            var go = Track(new GameObject("Crate"));
            var crate = go.AddComponent<LootContainer>();
            var so = new SerializedObject(crate);
            so.FindProperty("displayName").stringValue = "보급 상자";
            so.FindProperty("gridSize").vector2IntValue = gridSize ?? new Vector2Int(6, 6);
            SerializedProperty list = so.FindProperty("contents");
            list.arraySize = contents.Length;
            for (int i = 0; i < contents.Length; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = contents[i];
            so.FindProperty("lootTable").objectReferenceValue = lootTable;
            so.FindProperty("seed").intValue = seed;
            so.ApplyModifiedPropertiesWithoutUndo();
            return crate;
        }

        private static List<ItemDefinitionSO> Definitions(LootContainerModel model) =>
            model.Items.Select(p => p.Item.Definition).ToList();

        // ── CreateContents (순수) ────────────────────────────

        [Test]
        public void CreateContents_FixedFirst_ThenRolled()
        {
            List<ItemInstance> items = LootContainer.CreateContents(new[] { flare, null, pipe }, table, new System.Random(7));

            Assert.AreEqual(flare, items[0].Definition, "고정 내용물 순서 유지");
            Assert.AreEqual(pipe, items[1].Definition, "null 제외");
            Assert.That(items.Count - 2, Is.InRange(1, 3), "rollCount 1~3");
            foreach (ItemInstance item in items.Skip(2))
                Assert.IsTrue(item.Definition == wires || item.Definition == pipe || item.Definition == flare);
        }

        [Test]
        public void CreateContents_SameSeed_SameDefinitions()
        {
            for (int seed = 1; seed <= 20; seed++)
            {
                var a = LootContainer.CreateContents(null, table, new System.Random(seed)).Select(i => i.Definition);
                var b = LootContainer.CreateContents(null, table, new System.Random(seed)).Select(i => i.Definition);
                CollectionAssert.AreEqual(a.ToList(), b.ToList(), $"시드 {seed}");
            }
        }

        [Test]
        public void CreateContents_MatchesLootRoller()
        {
            var expected = new List<ItemInstance>();
            LootRoller.Roll(table, new System.Random(11), expected);
            List<ItemInstance> items = LootContainer.CreateContents(null, table, new System.Random(11));
            CollectionAssert.AreEqual(expected.Select(i => i.Definition).ToList(), items.Select(i => i.Definition).ToList(),
                "굴림은 M3 LootRoller 그대로");
        }

        [Test]
        public void CreateContents_NoTable_FixedOnly()
        {
            List<ItemInstance> items = LootContainer.CreateContents(new[] { flare }, null, new System.Random(1));
            Assert.AreEqual(1, items.Count);
            Assert.AreEqual(flare, items[0].Definition);
            Assert.IsEmpty(LootContainer.CreateContents(null, null, null));
        }

        [Test]
        public void CreateContents_NewInstancesEachCall()
        {
            var a = LootContainer.CreateContents(new[] { flare }, null, null);
            var b = LootContainer.CreateContents(new[] { flare }, null, null);
            Assert.AreNotSame(a[0], b[0]);
        }

        // ── EnsureModel (상자 컴포넌트) ──────────────────────

        [Test]
        public void EnsureModel_FixedSeed_Reproducible()
        {
            LootContainer a = NewCrate(new ItemDefinitionSO[0], table, 42);
            LootContainer b = NewCrate(new ItemDefinitionSO[0], table, 42);

            CollectionAssert.AreEqual(Definitions(a.EnsureModel(0.5f)), Definitions(b.EnsureModel(0.5f)));
            Assert.That(a.Model.Items.Count, Is.InRange(1, 3));
        }

        [Test]
        public void EnsureModel_CreatedOnce_SameModelLater()
        {
            LootContainer crate = NewCrate(new[] { flare }, table, 0);
            LootContainerModel first = crate.EnsureModel(0.5f);
            LootContainerModel second = crate.EnsureModel(0.5f);
            Assert.AreSame(first, second, "처음 열 때 한 번만 굴린다 (검색 진행 유지)");
            Assert.AreSame(first, crate.Model);
        }

        [Test]
        public void EnsureModel_SeedZero_StillRollsWithinTable()
        {
            for (int i = 0; i < 10; i++)
            {
                LootContainer crate = NewCrate(new ItemDefinitionSO[0], table, 0);
                List<ItemDefinitionSO> defs = Definitions(crate.EnsureModel(0.5f));
                Assert.That(defs.Count, Is.InRange(1, 3));
                Assert.IsTrue(defs.All(d => d == wires || d == pipe || d == flare));
            }
        }

        [Test]
        public void EnsureModel_FlareCrate_FixedContentsWithoutTable()
        {
            // Q7: 신호탄 고정 상자 = contents만
            LootContainer crate = NewCrate(new[] { flare }, null, 0);
            CollectionAssert.AreEqual(new[] { flare }, Definitions(crate.EnsureModel(0.5f)));
            Assert.AreEqual("보급 상자", crate.Model.DisplayName);
            Assert.AreEqual(new Vector2Int(6, 6), crate.Model.Size);
        }

        [Test]
        public void EnsureModel_FixedThenRolled_SearchOrder()
        {
            LootContainer crate = NewCrate(new[] { pipe }, table, 5);
            LootContainerModel model = crate.EnsureModel(0.5f);
            ItemInstance firstPlaced = model.Items[0].Item;
            Assert.AreEqual(pipe, firstPlaced.Definition);

            Assert.IsTrue(model.Tick(0.5f));
            Assert.IsTrue(model.IsRevealed(firstPlaced), "고정 내용물이 먼저 드러난다");
        }

        [Test]
        public void EnsureModel_RollCountRespected_ManySeeds()
        {
            table.rollCount = new Vector2Int(2, 2);
            for (int seed = 1; seed <= 30; seed++)
                Assert.AreEqual(2, NewCrate(new ItemDefinitionSO[0], table, seed).EnsureModel(0.5f).Items.Count, $"시드 {seed}");
        }

        [Test]
        public void SerializedFields_Exposed()
        {
            LootContainer crate = NewCrate(new ItemDefinitionSO[0], table, 9);
            Assert.AreSame(table, crate.LootTable);
            Assert.AreEqual(9, crate.Seed);
            Assert.AreEqual(InteractionKind.OpenContainer, crate.Kind);
        }
    }
}
