using System.Linq;
using Ashfall.Core;
using Ashfall.Inventory;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // §6 루트 컨테이너 검색 연출: 아이템 1개당 0.5초, 처음 배치 순서대로 드러남
    public class LootSearchTests
    {
        private static LootContainerModel Loot(int count, out ItemInstance[] items)
        {
            items = Enumerable.Range(0, count).Select(_ => new ItemInstance(TestData.Wires())).ToArray();
            return new LootContainerModel("상자", new Vector2Int(4, 4), items, 0.5f);
        }

        [Test]
        public void NothingRevealedBefore0_5s_OneAt0_5s()
        {
            var loot = Loot(3, out ItemInstance[] items);
            Assert.IsFalse(loot.Tick(0.49f));
            Assert.AreEqual(0, loot.RevealedCount);
            Assert.IsFalse(loot.IsRevealed(items[0]));

            var other = Loot(3, out ItemInstance[] otherItems);
            Assert.IsTrue(other.Tick(0.5f));
            Assert.AreEqual(1, other.RevealedCount);
            Assert.IsTrue(other.IsRevealed(otherItems[0]), "처음 배치 순서대로");
            Assert.IsFalse(other.IsRevealed(otherItems[1]));
        }

        [Test]
        public void NItems_CompleteAfterNTimesHalfSecond()
        {
            var loot = Loot(6, out _);
            for (int i = 0; i < 5; i++)
                loot.Tick(0.5f);
            Assert.IsFalse(loot.SearchComplete);
            Assert.AreEqual(5, loot.RevealedCount);
            loot.Tick(0.5f);
            Assert.IsTrue(loot.SearchComplete);
            Assert.AreEqual(6, loot.RevealedCount);
            Assert.IsFalse(loot.Tick(1f), "다 드러나면 더 없음");
        }

        [Test]
        public void SmallFrames_AccumulateToSameTime()
        {
            var loot = Loot(2, out _);
            for (int i = 0; i < 60; i++)       // 60 × 1/60 = 1초
                loot.Tick(1f / 60f);
            Assert.AreEqual(2, loot.RevealedCount);
            Assert.IsTrue(loot.SearchComplete);
        }

        [Test]
        public void BigTick_RevealsSeveral()
        {
            var loot = Loot(6, out _);
            Assert.IsTrue(loot.Tick(1.6f));
            Assert.AreEqual(3, loot.RevealedCount);
        }

        [Test]
        public void EmptyContainer_IsCompleteImmediately()
        {
            var loot = new LootContainerModel("빈 상자", new Vector2Int(2, 2), null, 0.5f);
            Assert.IsTrue(loot.SearchComplete);
            Assert.IsFalse(loot.Tick(1f));
        }

        [Test]
        public void ClosedLoot_NotTicked_ReopenContinues()
        {
            // 검색은 열린 동안만 진행 (브리지가 ActiveLootModel만 Tick). 닫으면 ActiveLootModel이 없다.
            var inventory = new PlayerInventory(TestData.Inventory());
            var loot = Loot(3, out ItemInstance[] items);
            inventory.OpenLoot(loot);
            inventory.ActiveLootModel.Tick(0.5f);
            inventory.CloseLoot();
            Assert.IsNull(inventory.ActiveLootModel);

            inventory.OpenLoot(loot);
            Assert.AreEqual(1, inventory.ActiveLootModel.RevealedCount, "다시 열면 이어서");
            Assert.IsTrue(inventory.ActiveLoot.IsRevealed(items[0]));
            Assert.IsFalse(inventory.ActiveLoot.SearchComplete);
        }

        [Test]
        public void RevealedItem_QuickMoveToStorage_HiddenRejected()
        {
            var inventory = new PlayerInventory(TestData.Inventory());
            var loot = Loot(2, out ItemInstance[] items);
            inventory.OpenLoot(loot);
            loot.Tick(0.5f);
            Assert.IsTrue(inventory.TryQuickMove(items[0]));
            Assert.IsFalse(inventory.TryQuickMove(items[1]));
            Assert.IsTrue(inventory.IsInStorage(items[0]));
        }
    }
}
