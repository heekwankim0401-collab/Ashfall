using System;
using System.Collections.Generic;
using Ashfall.Core;

namespace Ashfall.UI
{
    // 레시피 재료 한 칸 (이름 · "가진 수/필요 수")
    public readonly struct CraftingInputRow
    {
        public readonly ItemDefinitionSO Item;
        public readonly string Name;
        public readonly string CountText;   // "1/2" — 재료 개수뿐
        public readonly bool Satisfied;

        public CraftingInputRow(ItemDefinitionSO item, string name, string countText, bool satisfied)
        {
            Item = item;
            Name = name;
            CountText = countText;
            Satisfied = satisfied;
        }
    }

    // 레시피 한 줄 (이름 · 재료 · [제작])
    public readonly struct CraftingRecipeRow
    {
        public readonly string Id;
        public readonly string Name;
        public readonly IReadOnlyList<CraftingInputRow> Inputs;
        public readonly bool HasMaterials;   // 재료가 모두 있다 (같은 정의 줄은 합쳐서)
        public readonly bool CanCraft;       // HasMaterials + 떠나는 중·읽기 전용 아님

        public CraftingRecipeRow(string id, string name, IReadOnlyList<CraftingInputRow> inputs, bool hasMaterials, bool canCraft)
        {
            Id = id;
            Name = name;
            Inputs = inputs;
            HasMaterials = hasMaterials;
            CanCraft = canCraft;
        }
    }

    // [제작] 탭 데이터 (M7 설계 §1.5, Q44~Q47). 순수 C#. IHideoutService.Recipes(읽을 때의 보관함 기준) 순.
    // 숫자는 재료 개수 "가진 수/필요 수"뿐 — 탄 수·체력은 만들지 않는다.
    public sealed class CraftingViewModel
    {
        private CraftingViewModel(IReadOnlyList<CraftingRecipeRow> recipes)
        {
            Recipes = recipes;
        }

        public IReadOnlyList<CraftingRecipeRow> Recipes { get; }
        public bool IsEmpty => Recipes.Count == 0;

        public static CraftingViewModel Build(IHideoutService hideout)
        {
            if (hideout == null)
                throw new ArgumentNullException(nameof(hideout));
            bool blocked = hideout.IsLeaving || hideout.IsReadOnly;

            var rows = new List<CraftingRecipeRow>();
            IReadOnlyList<CraftingRecipeView> source = hideout.Recipes;
            if (source != null)
            {
                for (int i = 0; i < source.Count; i++)
                {
                    CraftingRecipeView recipe = source[i];
                    if (string.IsNullOrEmpty(recipe.Id))
                        continue;
                    var inputs = new List<CraftingInputRow>();
                    bool hasMaterials = recipe.Inputs != null && recipe.Inputs.Count > 0;
                    if (recipe.Inputs != null)
                    {
                        // 같은 정의가 여러 줄이면 합계로 판정한다 (Owned는 줄마다 같은 보관함 전체 수).
                        // 칸 충족 표시도 버튼과 같은 합계 기준 — 줄마다 1/1이어도 합계 2 > 가진 1이면 모두 부족 (리뷰 7C 4).
                        var needed = new Dictionary<ItemDefinitionSO, int>();
                        foreach (CraftingInputView input in recipe.Inputs)
                        {
                            if (input.Item == null)
                                continue;
                            needed.TryGetValue(input.Item, out int sum);
                            needed[input.Item] = sum + input.Required;
                        }
                        foreach (CraftingInputView input in recipe.Inputs)
                        {
                            bool satisfied = input.Item != null && input.Owned >= needed[input.Item];
                            inputs.Add(new CraftingInputRow(input.Item, RaidResultViewModel.ItemName(input.Item),
                                UiText.CraftingInputCount(input.Owned, input.Required), satisfied));
                            if (!satisfied)
                                hasMaterials = false;
                        }
                    }
                    rows.Add(new CraftingRecipeRow(recipe.Id, RecipeName(recipe), inputs, hasMaterials, hasMaterials && !blocked));
                }
            }
            return new CraftingViewModel(rows);
        }

        // 레시피 이름이 없으면 산출 이름(×n)
        public static string RecipeName(CraftingRecipeView recipe)
        {
            if (!string.IsNullOrEmpty(recipe.DisplayName))
                return recipe.DisplayName;
            string name = RaidResultViewModel.ItemName(recipe.Output);
            return recipe.OutputCount > 1 ? name + " ×" + recipe.OutputCount : name;
        }

        public bool TryGetRecipe(string recipeId, out CraftingRecipeRow row)
        {
            for (int i = 0; i < Recipes.Count; i++)
            {
                if (Recipes[i].Id == recipeId)
                {
                    row = Recipes[i];
                    return true;
                }
            }
            row = default;
            return false;
        }
    }
}
