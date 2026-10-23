using System.Collections.Generic;
using Ashfall.Core;
using UnityEngine;

namespace Ashfall.Hideout
{
    // SYS-HUB 제작대 규칙 (§9 "고철을 탄약·의약품으로", M7 설계 §1.5, Q44~Q47). 순수 정적.
    // - 재료 = 보관함만 (Q44). 같은 정의의 아무 인스턴스를 보관함 배치 순(위→아래, 왼→오른쪽)으로 쓴다.
    // - 즉시 완료, 탄피 비용 없음 (Q45). 산출 = ItemFactory.Create(output) × outputCount — 탄창·탄 상자는 가득 (Q46).
    // - 순서: 재료 수 확인 → 재료 제거 → 산출물 전부 StashRules.TryPlaceAll → 실패면 보관함을 제작 전 배치 그대로 되돌리고 NoSpace.
    //   재료를 먼저 빼므로 재료가 있던 자리에 산출물이 들어갈 수 있다.
    // 변경 알림(HideoutChanged)·저장 시점은 호출자(HideoutService)가 정한다.
    public static class CraftingModel
    {
        // 레시피가 쓸 수 있는 형태인가: id·산출 정의·산출 개수 ≥ 1, 재료 1줄 이상이고 모든 줄이 정의 + 개수 ≥ 1.
        // 재료 없는 레시피는 공짜 생성기가 되므로 막는다 (TODO(GDD-?) Q47: 데이터 오류로 보고 목록에서 뺀다).
        public static bool IsValid(CraftingRecipe recipe)
        {
            if (recipe == null || string.IsNullOrEmpty(recipe.id) || recipe.output == null || recipe.outputCount < 1)
                return false;
            if (recipe.inputs == null || recipe.inputs.Length == 0)
                return false;
            foreach (CraftingIngredient input in recipe.inputs)
                if (input.item == null || input.count < 1)
                    return false;
            return true;
        }

        // 보관함의 같은 정의 인스턴스 수 (UI "가진 수", 재료 개수만)
        public static int CountOwned(IGridView stash, ItemDefinitionSO definition)
        {
            if (stash == null || stash.Items == null || definition == null)
                return 0;
            int count = 0;
            IReadOnlyList<PlacedItem> items = stash.Items;
            for (int i = 0; i < items.Count; i++)
                if (items[i].Item != null && items[i].Item.Definition == definition)
                    count++;
            return count;
        }

        // 같은 정의가 재료 줄 둘 이상에 나오면 합친다 (정의 → 필요 수, 첫 등장 순)
        public static List<KeyValuePair<ItemDefinitionSO, int>> RequiredTotals(CraftingRecipe recipe)
        {
            var totals = new List<KeyValuePair<ItemDefinitionSO, int>>();
            if (recipe?.inputs == null)
                return totals;
            foreach (CraftingIngredient input in recipe.inputs)
            {
                if (input.item == null || input.count < 1)
                    continue;
                int index = totals.FindIndex(p => p.Key == input.item);
                if (index >= 0)
                    totals[index] = new KeyValuePair<ItemDefinitionSO, int>(input.item, totals[index].Value + input.count);
                else
                    totals.Add(new KeyValuePair<ItemDefinitionSO, int>(input.item, input.count));
            }
            return totals;
        }

        // UI용 한 줄. 재료 줄은 레시피 순 그대로, Owned = 그 정의의 보관함 전체 수 (같은 정의가 두 줄이면 두 줄 모두 같은 Owned).
        public static CraftingRecipeView ToView(CraftingRecipe recipe, IGridView stash)
        {
            var inputs = new List<CraftingInputView>();
            if (recipe?.inputs != null)
            {
                foreach (CraftingIngredient input in recipe.inputs)
                    inputs.Add(new CraftingInputView(input.item, input.count, CountOwned(stash, input.item)));
            }
            return new CraftingRecipeView(recipe?.id, recipe?.displayName, recipe?.output, recipe?.outputCount ?? 0, inputs);
        }

        // 재료가 모두 있나 (같은 정의 줄은 합쳐서 본다)
        public static bool HasMaterials(IGridView stash, CraftingRecipe recipe)
        {
            foreach (KeyValuePair<ItemDefinitionSO, int> need in RequiredTotals(recipe))
                if (CountOwned(stash, need.Key) < need.Value)
                    return false;
            return true;
        }

        // 제작 하나. stash null·레시피 무효 → UnknownRecipe(쓸 수 없는 레시피 = 없는 것과 같게), 재료 부족 → MissingMaterials,
        // 산출물 자리 없음 → NoSpace(보관함은 제작 전과 같은 배치·순서). 성공하면 재료가 빠지고 산출물이 보관함에 놓인다.
        public static CraftResult TryCraft(ItemGrid stash, CraftingRecipe recipe)
        {
            if (stash == null || !IsValid(recipe))
                return CraftResult.UnknownRecipe;
            if (!HasMaterials(stash, recipe))
                return CraftResult.MissingMaterials;

            // 되돌리기용: 제작 전 배치 전체(순서 포함)
            var before = new List<PlacedItem>(stash.Items);

            foreach (ItemInstance used in PickMaterials(stash, recipe))
                stash.Remove(used);

            var outputs = new List<ItemInstance>(recipe.outputCount);
            for (int i = 0; i < recipe.outputCount; i++)
                outputs.Add(ItemFactory.Create(recipe.output));   // 탄창·탄 상자는 가득 (Q46)

            if (StashRules.TryPlaceAll(stash, outputs))
                return CraftResult.Ok;

            Restore(stash, before);
            return CraftResult.NoSpace;
        }

        // 쓸 재료 인스턴스: 정의마다 보관함 배치 순(위→아래, 왼→오른쪽)으로 필요 수만큼
        private static List<ItemInstance> PickMaterials(ItemGrid stash, CraftingRecipe recipe)
        {
            var ordered = new List<PlacedItem>(stash.Items);
            ordered.Sort((a, b) =>
            {
                int c = a.Cell.y.CompareTo(b.Cell.y);
                return c != 0 ? c : a.Cell.x.CompareTo(b.Cell.x);
            });

            var picked = new List<ItemInstance>();
            foreach (KeyValuePair<ItemDefinitionSO, int> need in RequiredTotals(recipe))
            {
                int left = need.Value;
                for (int i = 0; i < ordered.Count && left > 0; i++)
                {
                    ItemInstance item = ordered[i].Item;
                    if (item != null && item.Definition == need.Key)
                    {
                        picked.Add(item);
                        left--;
                    }
                }
            }
            return picked;
        }

        // 보관함을 기록한 배치로 되돌린다 (원래 자리·회전·목록 순서). 기록이 유효한 배치였으므로 전부 들어간다.
        private static void Restore(ItemGrid stash, List<PlacedItem> before)
        {
            var current = new List<PlacedItem>(stash.Items);
            foreach (PlacedItem placed in current)
                stash.Remove(placed.Item);
            foreach (PlacedItem placed in before)
            {
                if (!stash.TryPlace(placed.Item, placed.Cell, placed.Rotated))
                    Debug.LogError($"[CraftingModel] 재료를 원래 자리로 되돌리지 못했다: {placed.Item?.Definition?.id} {placed.Cell}");
            }
        }
    }
}
