using Ashfall.Core;
using Ashfall.Inventory;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // SYS-INV 그리드 배치·회전·충돌 (§6)
    public class ItemGridTests
    {
        private static ItemGrid Grid(int w, int h) => new ItemGrid(new GridRef(GridKind.Backpack), new Vector2Int(w, h));
        private static ItemInstance Sized(int w, int h) => new ItemInstance(TestData.Item("TEST", "t", new Vector2Int(w, h), 0f));

        [Test]
        public void OutOfBounds_Rejected()
        {
            var grid = Grid(4, 2);
            var item = Sized(2, 1);
            Assert.IsFalse(grid.CanPlace(item, new Vector2Int(3, 0), false));
            Assert.IsFalse(grid.CanPlace(item, new Vector2Int(-1, 0), false));
            Assert.IsFalse(grid.CanPlace(item, new Vector2Int(0, 1), true), "회전하면 1×2 → 아래로 넘침");
            Assert.IsTrue(grid.CanPlace(item, new Vector2Int(2, 1), false));
        }

        [Test]
        public void Overlap_Rejected()
        {
            var grid = Grid(4, 4);
            Assert.IsTrue(grid.TryPlace(Sized(2, 2), new Vector2Int(1, 1), false));
            Assert.IsFalse(grid.TryPlace(Sized(1, 1), new Vector2Int(2, 2), false));
            Assert.IsTrue(grid.TryPlace(Sized(1, 1), new Vector2Int(3, 3), false));
            Assert.AreEqual(2, grid.Items.Count);
        }

        [Test]
        public void Rotated_2x1_FitsInto1x2Slot()
        {
            var grid = Grid(1, 2);
            var item = Sized(2, 1);
            Assert.IsFalse(grid.CanPlace(item, Vector2Int.zero, false));
            Assert.IsTrue(grid.TryPlace(item, Vector2Int.zero, true));
            Assert.IsTrue(grid.TryGetPlacement(item, out PlacedItem placed));
            Assert.IsTrue(placed.Rotated);
            Assert.AreEqual(new Vector2Int(1, 2), placed.Size);
        }

        [Test]
        public void MoveWithinSameGrid_IgnoresSelf()
        {
            var grid = Grid(3, 1);
            var item = Sized(2, 1);
            grid.TryPlace(item, Vector2Int.zero, false);
            Assert.IsTrue(grid.CanPlace(item, new Vector2Int(1, 0), false), "자기 자리와 겹쳐도 된다");

            var other = Sized(1, 1);
            Assert.IsFalse(grid.CanPlace(other, new Vector2Int(1, 0), false));
            Assert.IsTrue(grid.CanPlace(other, new Vector2Int(1, 0), false, ignore: item));
        }

        [Test]
        public void TryPlace_TwiceSameItem_Rejected()
        {
            var grid = Grid(3, 3);
            var item = Sized(1, 1);
            Assert.IsTrue(grid.TryPlace(item, Vector2Int.zero, false));
            Assert.IsFalse(grid.TryPlace(item, new Vector2Int(2, 2), false));
        }

        [Test]
        public void TryFindFreeCell_TopToBottom_LeftToRight_UnrotatedFirst()
        {
            var grid = Grid(3, 2);
            grid.TryPlace(Sized(2, 1), Vector2Int.zero, false);

            Assert.IsTrue(grid.TryFindFreeCell(Sized(1, 1), out Vector2Int cell, out bool rotated));
            Assert.AreEqual(new Vector2Int(2, 0), cell);
            Assert.IsFalse(rotated);

            Assert.IsTrue(grid.TryFindFreeCell(Sized(1, 2), out cell, out rotated));
            Assert.AreEqual(new Vector2Int(2, 0), cell, "1×2가 (2,0)에 세워서 들어간다");
            Assert.IsFalse(rotated);

            Assert.IsTrue(grid.TryFindFreeCell(Sized(3, 1), out cell, out rotated));
            Assert.AreEqual(new Vector2Int(0, 1), cell);
        }

        [Test]
        public void TryFindFreeCell_UsesRotationWhenNeeded()
        {
            var grid = Grid(3, 1);
            Assert.IsTrue(grid.TryFindFreeCell(Sized(1, 3), out Vector2Int cell, out bool rotated));
            Assert.AreEqual(Vector2Int.zero, cell);
            Assert.IsTrue(rotated);
        }

        [Test]
        public void TryFindFreeCell_Full_ReturnsFalse()
        {
            var grid = Grid(1, 1);
            grid.TryPlace(Sized(1, 1), Vector2Int.zero, false);
            Assert.IsFalse(grid.TryFindFreeCell(Sized(1, 1), out _, out _));
        }

        [Test]
        public void Remove_FreesCells()
        {
            var grid = Grid(1, 1);
            var item = Sized(1, 1);
            grid.TryPlace(item, Vector2Int.zero, false);
            Assert.IsTrue(grid.Remove(item));
            Assert.IsFalse(grid.Contains(item));
            Assert.IsTrue(grid.IsEmpty);
            Assert.IsFalse(grid.Remove(item));
        }
    }
}
