using System.Collections.Generic;
using System.Linq;
using Ashfall.Core;
using Ashfall.Hideout;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // M7 제작대 규칙 (M7 설계 §1.5, §6 7C-2b, Q44~Q47): 재료 부족, 보관함만, 공간 없음 → 재료 원위치·NoSpace,
    // 성공 → 재료 제거·산출 배치, 산출 탄창·탄 상자 가득.
    public class CraftingModelTests
    {
        private ItemDefinitionSO pipe;     // 1×3
        private ItemDefinitionSO wires;    // 1×1
        private ItemDefinitionSO engine;   // 3×2

        [SetUp]
        public void SetUp()
        {
            pipe = TestData.Pipe();
            wires = TestData.Wires();
            engine = TestData.EnginePart();
        }

        private static ItemGrid Stash(int w = 10, int h = 30) => new ItemGrid(new GridRef(GridKind.Loot), new Vector2Int(w, h));

        private static CraftingRecipe Recipe(string id, ItemDefinitionSO output, int outputCount,
            params (ItemDefinitionSO item, int count)[] inputs) => new CraftingRecipe
        {
            id = id,
            displayName = id + " 이름",
            output = output,
            outputCount = outputCount,
            inputs = inputs.Select(i => new CraftingIngredient { item = i.item, count = i.count }).ToArray(),
        };

        private static ItemInstance Put(ItemGrid grid, ItemDefinitionSO def, int x, int y, bool rotated = false)
        {
            var item = new ItemInstance(def);
            Assert.IsTrue(grid.TryPlace(item, new Vector2Int(x, y), rotated), $"{def.id} ({x},{y}) 배치");
            return item;
        }

        private static string Describe(ItemGrid grid) =>
            string.Join(" | ", grid.Items.Select(p => $"{p.Item.Definition.id}#{p.Item.GetHashCode()}@{p.Cell.x},{p.Cell.y}{(p.Rotated ? "R" : "")}"));

        // ── 유효성 ──

        [Test]
        public void IsValid_RequiresIdOutputCountAndInputs()
        {
            Assert.IsTrue(CraftingModel.IsValid(Recipe("R", wires, 1, (pipe, 1))));
            Assert.IsFalse(CraftingModel.IsValid(null));
            Assert.IsFalse(CraftingModel.IsValid(Recipe("", wires, 1, (pipe, 1))), "id 없음");
            Assert.IsFalse(CraftingModel.IsValid(Recipe("R", null, 1, (pipe, 1))), "산출 없음");
            Assert.IsFalse(CraftingModel.IsValid(Recipe("R", wires, 0, (pipe, 1))), "산출 0개");
            Assert.IsFalse(CraftingModel.IsValid(Recipe("R", wires, 1)), "재료 없는 레시피 = 공짜 생성기 → 무효");
            Assert.IsFalse(CraftingModel.IsValid(Recipe("R", wires, 1, (null, 1))), "재료 정의 없음");
            Assert.IsFalse(CraftingModel.IsValid(Recipe("R", wires, 1, (pipe, 0))), "재료 0개");
        }

        [Test]
        public void TryCraft_NullStashOrInvalidRecipe_UnknownRecipe()
        {
            Assert.AreEqual(CraftResult.UnknownRecipe, CraftingModel.TryCraft(null, Recipe("R", wires, 1, (pipe, 1))));
            ItemGrid stash = Stash();
            Put(stash, pipe, 0, 0);
            Assert.AreEqual(CraftResult.UnknownRecipe, CraftingModel.TryCraft(stash, Recipe("R", wires, 1)));
            Assert.AreEqual(1, stash.Items.Count, "보관함 그대로");
        }

        // ── 재료 부족 ──

        [Test]
        public void TryCraft_MissingMaterials_NothingChanges()
        {
            ItemGrid stash = Stash();
            Put(stash, pipe, 0, 0);
            Put(stash, wires, 1, 0);
            string before = Describe(stash);

            CraftResult result = CraftingModel.TryCraft(stash, Recipe("R", TestData.Magazine30(), 1, (pipe, 2), (wires, 1)));

            Assert.AreEqual(CraftResult.MissingMaterials, result);
            Assert.AreEqual(before, Describe(stash), "재료 부족 → 아무것도 빼지 않는다");
        }

        // 같은 정의가 재료 줄 둘에 나오면 합쳐서 본다 (파이프 1 + 파이프 1 = 2 필요)
        [Test]
        public void TryCraft_SameDefinitionTwoLines_SummedRequirement()
        {
            ItemGrid stash = Stash();
            Put(stash, pipe, 0, 0);
            CraftingRecipe recipe = Recipe("R", wires, 1, (pipe, 1), (pipe, 1));
            Assert.IsFalse(CraftingModel.HasMaterials(stash, recipe));
            Assert.AreEqual(CraftResult.MissingMaterials, CraftingModel.TryCraft(stash, recipe));

            Put(stash, pipe, 1, 0);
            Assert.AreEqual(CraftResult.Ok, CraftingModel.TryCraft(stash, recipe));
            Assert.AreEqual(0, CraftingModel.CountOwned(stash, pipe), "두 줄 합계만큼 뺀다");
        }

        // ── 재료 = 보관함만 (Q44) ──

        [Test]
        public void TryCraft_OnlyStashCounts_OtherGridsUntouched()
        {
            ItemGrid stash = Stash();
            var pocket = new ItemGrid(new GridRef(GridKind.Pocket, 0), new Vector2Int(1, 1));
            ItemInstance carried = Put(pocket, wires, 0, 0);
            Put(stash, pipe, 0, 0);
            CraftingRecipe recipe = Recipe("R", TestData.Magazine30(), 1, (pipe, 1), (wires, 1));

            Assert.AreEqual(0, CraftingModel.CountOwned(stash, wires), "장비 쪽 전선은 세지 않는다");
            Assert.AreEqual(CraftResult.MissingMaterials, CraftingModel.TryCraft(stash, recipe));
            Assert.IsTrue(pocket.Contains(carried), "장비 쪽은 건드리지 않는다");
            Assert.AreEqual(1, stash.Items.Count);
        }

        // ── 공간 없음 → 재료 원위치 + NoSpace ──

        [Test]
        public void TryCraft_NoSpace_MaterialsBackInPlace_SameOrder()
        {
            // 2×3 보관함: 파이프(세로 1×3) + 전선 3개. 전선 1 → 파이프 2개(1×3씩)는 들어갈 자리가 없다.
            ItemGrid stash = Stash(2, 3);
            ItemInstance p = Put(stash, pipe, 0, 0);
            ItemInstance w0 = Put(stash, wires, 1, 2);   // 목록 순서와 배치 순서를 일부러 다르게
            ItemInstance w1 = Put(stash, wires, 1, 0);
            ItemInstance w2 = Put(stash, wires, 1, 1);
            string before = Describe(stash);

            CraftResult result = CraftingModel.TryCraft(stash, Recipe("R", pipe, 2, (wires, 1)));

            Assert.AreEqual(CraftResult.NoSpace, result);
            Assert.AreEqual(before, Describe(stash), "같은 인스턴스·자리·회전·목록 순서로 되돌린다");
            CollectionAssert.AreEqual(new[] { p, w0, w1, w2 }, stash.Items.Select(i => i.Item).ToList());
        }

        // 일부 산출물만 들어가도 전부 거둔다 (StashRules.TryPlaceAll)
        [Test]
        public void TryCraft_NoSpace_PartialOutputsRemoved()
        {
            // 1×5: 전선(0,0) + 전선(0,1) + 빈 3칸. 전선 1 → 파이프(1×3) 2개: 첫 번째는 (0,2)에 들어가고 두 번째는 안 들어간다.
            ItemGrid stash = Stash(1, 5);
            Put(stash, wires, 0, 0);
            Put(stash, wires, 0, 1);
            string before = Describe(stash);

            Assert.AreEqual(CraftResult.NoSpace, CraftingModel.TryCraft(stash, Recipe("R", pipe, 2, (wires, 1))));
            Assert.AreEqual(before, Describe(stash));
            Assert.AreEqual(0, CraftingModel.CountOwned(stash, pipe), "놓였던 산출물도 거둔다");
        }

        // 재료를 먼저 빼므로 재료가 있던 자리에 산출물이 들어간다
        [Test]
        public void TryCraft_OutputUsesFreedMaterialSpace()
        {
            ItemGrid stash = Stash(1, 3);
            Put(stash, pipe, 0, 0);   // 꽉 참
            MagazineDefinitionSO mag = TestData.Magazine30();   // 1×2

            Assert.AreEqual(CraftResult.Ok, CraftingModel.TryCraft(stash, Recipe("R", mag, 1, (pipe, 1))));
            Assert.AreEqual(1, stash.Items.Count);
            Assert.AreSame(mag, stash.Items[0].Item.Definition);
        }

        // ── 성공 ──

        [Test]
        public void TryCraft_Ok_RemovesRequiredInGridOrder_PlacesOutput_MagazineFull()
        {
            ItemGrid stash = Stash();
            ItemInstance pipeLate = Put(stash, pipe, 5, 4);
            ItemInstance pipeEarly = Put(stash, pipe, 3, 0);
            ItemInstance pipeMid = Put(stash, pipe, 0, 4);
            ItemInstance wire = Put(stash, wires, 9, 9);
            ItemInstance engineItem = Put(stash, engine, 6, 0);
            MagazineDefinitionSO mag = TestData.Magazine30();

            CraftResult result = CraftingModel.TryCraft(stash, Recipe("RCP-AMMO-556", mag, 1, (pipe, 2), (wires, 1)));

            Assert.AreEqual(CraftResult.Ok, result);
            Assert.IsFalse(stash.Contains(pipeEarly), "배치 순 첫 파이프 (3,0)");
            Assert.IsFalse(stash.Contains(pipeMid), "배치 순 두 번째 파이프 (0,4)");
            Assert.IsTrue(stash.Contains(pipeLate), "필요 수만큼만 뺀다");
            Assert.IsFalse(stash.Contains(wire));
            Assert.IsTrue(stash.Contains(engineItem), "재료 아닌 것은 그대로");

            List<Magazine> made = stash.Items.Select(p => p.Item).OfType<Magazine>().ToList();
            Assert.AreEqual(1, made.Count, "산출 1개");
            Assert.AreSame(mag, made[0].Definition);
            Assert.IsTrue(made[0].IsFull, "산출 탄창은 가득 (Q46)");
            Assert.AreEqual(30, made[0].Rounds);
        }

        [Test]
        public void TryCraft_Ok_AmmoBoxFull_AndMultipleOutputsAreSeparateInstances()
        {
            ItemGrid stash = Stash();
            Put(stash, pipe, 0, 0);
            Put(stash, pipe, 1, 0);
            Put(stash, wires, 2, 0);
            MagazineDefinitionSO box = M7TestData.Box12Ga8();
            MedicalDefinitionSO bandage = TestData.Bandage();

            Assert.AreEqual(CraftResult.Ok, CraftingModel.TryCraft(stash, Recipe("RCP-AMMO-12GA", box, 1, (pipe, 2))));
            Magazine madeBox = stash.Items.Select(p => p.Item).OfType<Magazine>().Single();
            Assert.IsTrue(madeBox.Definition.isAmmoBox);
            Assert.AreEqual(box.capacity, madeBox.Rounds, "탄 상자도 가득 (Q46)");

            Assert.AreEqual(CraftResult.Ok, CraftingModel.TryCraft(stash, Recipe("RCP-MED-BND", bandage, 2, (wires, 1))));
            List<MedicalItem> bandages = stash.Items.Select(p => p.Item).OfType<MedicalItem>().ToList();
            Assert.AreEqual(2, bandages.Count, "산출 × 2");
            Assert.AreNotSame(bandages[0], bandages[1]);
            Assert.AreEqual(0, CraftingModel.CountOwned(stash, wires));
        }

        // ── 보기 ──

        [Test]
        public void ToView_OwnedCountsPerInput_InRecipeOrder()
        {
            ItemGrid stash = Stash();
            Put(stash, pipe, 0, 0);
            Put(stash, wires, 1, 0);
            Put(stash, wires, 2, 0);
            CraftingRecipe recipe = Recipe("R", TestData.Magazine30(), 1, (pipe, 2), (wires, 1), (engine, 1));

            CraftingRecipeView view = CraftingModel.ToView(recipe, stash);

            Assert.AreEqual("R", view.Id);
            Assert.AreEqual("R 이름", view.DisplayName);
            Assert.AreEqual(1, view.OutputCount);
            CollectionAssert.AreEqual(new[] { pipe, wires, engine }, view.Inputs.Select(i => i.Item));
            CollectionAssert.AreEqual(new[] { 2, 1, 1 }, view.Inputs.Select(i => i.Required));
            CollectionAssert.AreEqual(new[] { 1, 2, 0 }, view.Inputs.Select(i => i.Owned));
            CollectionAssert.AreEqual(new[] { false, true, false }, view.Inputs.Select(i => i.IsSatisfied));
        }
    }
}
