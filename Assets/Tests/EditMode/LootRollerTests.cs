using System.Collections.Generic;
using Ashfall.Core;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // §14 LootTableSO 굴림 (M3 결정: rollCount 최소~최대 포함 중 하나 → 가중치 비례 복원 추출, SYS-INV)
    public class LootRollerTests
    {
        [Test]
        public void NullOrEmptyTable_AddsNothing()
        {
            var into = new List<ItemInstance>();
            var random = new System.Random(1);

            Assert.AreEqual(0, LootRoller.Roll(null, random, into));
            Assert.AreEqual(0, LootRoller.Roll(M3TestData.LootTable(), random, into));
            Assert.IsEmpty(into);
        }

        [Test]
        public void AllWeightsZero_AddsNothing()
        {
            var table = M3TestData.LootTable(new Vector2Int(3, 3), (TestData.Wires(), 0f), (TestData.Pipe(), -1f));
            var into = new List<ItemInstance>();

            Assert.AreEqual(0, LootRoller.Roll(table, new System.Random(1), into));
            Assert.IsEmpty(into);
        }

        [Test]
        public void FixedRollCount_AddsExactlyThatMany_AndAppends()
        {
            var table = M3TestData.LootTable(new Vector2Int(2, 2), (TestData.Wires(), 1f));
            var existing = new ItemInstance(TestData.Pipe());
            var into = new List<ItemInstance> { existing };

            int added = LootRoller.Roll(table, new System.Random(7), into);

            Assert.AreEqual(2, added);
            Assert.AreEqual(3, into.Count);
            Assert.AreSame(existing, into[0], "기존 목록 뒤에 더한다");
            Assert.AreNotSame(into[1], into[2], "뽑을 때마다 새 인스턴스");
        }

        [Test]
        public void RollCount_StaysInInclusiveRange_AndHitsBothEnds()
        {
            var table = M3TestData.LootTable(new Vector2Int(1, 3), (TestData.Wires(), 1f));
            var seen = new HashSet<int>();
            var random = new System.Random(123);
            var into = new List<ItemInstance>();

            for (int i = 0; i < 300; i++)
            {
                into.Clear();
                int added = LootRoller.Roll(table, random, into);
                Assert.That(added, Is.InRange(1, 3));
                Assert.AreEqual(added, into.Count);
                seen.Add(added);
            }
            CollectionAssert.AreEquivalent(new[] { 1, 2, 3 }, seen);
        }

        [Test]
        public void ZeroWeightAndNullEntries_AreNeverPicked()
        {
            ItemDefinitionSO wires = TestData.Wires();
            var table = M3TestData.LootTable(new Vector2Int(1, 1), (TestData.Pipe(), 0f), ((ItemDefinitionSO)null, 5f), (wires, 1f));
            var random = new System.Random(5);
            var into = new List<ItemInstance>();

            for (int i = 0; i < 200; i++)
                LootRoller.Roll(table, random, into);

            Assert.AreEqual(200, into.Count);
            foreach (ItemInstance item in into)
                Assert.AreSame(wires, item.Definition);
        }

        [Test]
        public void Weights_AreProportional()
        {
            ItemDefinitionSO common = TestData.Wires();
            ItemDefinitionSO rare = TestData.Pipe();
            var table = M3TestData.LootTable(new Vector2Int(1, 1), (common, 3f), (rare, 1f));
            var random = new System.Random(42);
            var into = new List<ItemInstance>();

            for (int i = 0; i < 4000; i++)
                LootRoller.Roll(table, random, into);

            int commonCount = 0;
            foreach (ItemInstance item in into)
                if (item.Definition == common)
                    commonCount++;
            Assert.That(commonCount / 4000f, Is.EqualTo(0.75f).Within(0.03f));
        }

        [Test]
        public void SameSeed_ReproducesSameResult()
        {
            LootTableSO table = M3TestData.Scv().loot;

            List<string> RollIds(int seed)
            {
                var random = new System.Random(seed);
                var into = new List<ItemInstance>();
                for (int i = 0; i < 20; i++)
                    LootRoller.Roll(table, random, into);
                var ids = new List<string>();
                foreach (ItemInstance item in into)
                    ids.Add(item.Definition.id);
                return ids;
            }

            CollectionAssert.AreEqual(RollIds(99), RollIds(99));
        }

        [Test]
        public void CreatesTypedInstances_ViaItemFactory()
        {
            MagazineDefinitionSO mag = M3TestData.Mag9_12();
            MedicalDefinitionSO bandage = TestData.Bandage();
            var into = new List<ItemInstance>();

            LootRoller.Roll(M3TestData.LootTable((mag, 1f)), new System.Random(1), into);
            LootRoller.Roll(M3TestData.LootTable((bandage, 1f)), new System.Random(1), into);

            Assert.IsInstanceOf<Magazine>(into[0]);
            Assert.AreEqual(12, ((Magazine)into[0]).Rounds, "탄창은 가득 찬 상태로 생성");
            Assert.IsInstanceOf<MedicalItem>(into[1]);
        }

        [Test]
        public void NewTable_DefaultRollCountIsOne()
        {
            var table = ScriptableObject.CreateInstance<LootTableSO>();
            Assert.AreEqual(Vector2Int.one, table.rollCount);
            Assert.IsNotNull(table.entries);
        }
    }
}
