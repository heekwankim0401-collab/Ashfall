using System;
using UnityEngine.UIElements;

namespace Ashfall.UI
{
    // [제작] 탭 (Art/UI/HideoutUI.uxml, M7 설계 §1.5). 레시피 줄 = 이름 · 재료 칸("이름 가진 수/필요 수") · [제작].
    // 데이터는 CraftingViewModel. 줄은 그릴 때마다 새로 만든다(TraderView와 같음). 결과 문구는 허브 메시지 줄(hideout-message).
    public sealed class CraftingView
    {
        public const string PanelName = "crafting-panel";
        public const string TitleName = "crafting-title";
        public const string HintName = "crafting-hint";
        public const string RecipesName = "crafting-recipes";

        public const string RowClass = "crafting-row";
        public const string RowNameClass = "crafting-row-name";
        public const string InputsClass = "crafting-inputs";
        public const string InputClass = "crafting-input";
        public const string InputNameClass = "crafting-input-name";
        public const string InputCountClass = "crafting-input-count";
        public const string InputMissingClass = "crafting-input--missing";
        public const string RowButtonClass = "crafting-row-button";
        public const string EmptyClass = "result-line--empty";

        private readonly VisualElement panel;
        private readonly Label title;
        private readonly Label hint;
        private readonly VisualElement recipes;
        private readonly Action<string> onCraft;

        public CraftingView(VisualElement documentRoot, Action<string> onCraft)
        {
            panel = RaidResultView.Find<VisualElement>(documentRoot, PanelName);
            title = RaidResultView.Find<Label>(documentRoot, TitleName);
            hint = RaidResultView.Find<Label>(documentRoot, HintName);
            recipes = RaidResultView.Find<VisualElement>(documentRoot, RecipesName);
            this.onCraft = onCraft;
            title.text = UiText.CraftingTitle;
            hint.text = UiText.CraftingHint;
            SetVisible(false);
        }

        public void SetVisible(bool visible) =>
            panel.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        // hubEnabled = false(알림·결과·떠나는 중)면 모든 버튼 비활성
        public void Render(CraftingViewModel model, bool hubEnabled)
        {
            recipes.Clear();
            if (model == null || model.IsEmpty)
            {
                var empty = new Label(UiText.CraftingEmpty) { pickingMode = PickingMode.Ignore };
                empty.AddToClassList(RaidResultView.LineClass);
                empty.AddToClassList(EmptyClass);
                recipes.Add(empty);
                return;
            }

            foreach (CraftingRecipeRow recipe in model.Recipes)
            {
                var row = new VisualElement();
                row.AddToClassList(RowClass);

                var name = new Label(recipe.Name) { pickingMode = PickingMode.Ignore };
                name.AddToClassList(RowNameClass);
                row.Add(name);

                var inputs = new VisualElement { pickingMode = PickingMode.Ignore };
                inputs.AddToClassList(InputsClass);
                foreach (CraftingInputRow input in recipe.Inputs)
                {
                    var chip = new VisualElement { pickingMode = PickingMode.Ignore };
                    chip.AddToClassList(InputClass);
                    chip.EnableInClassList(InputMissingClass, !input.Satisfied);
                    var inputName = new Label(input.Name) { pickingMode = PickingMode.Ignore };
                    inputName.AddToClassList(InputNameClass);
                    chip.Add(inputName);
                    var count = new Label(input.CountText) { pickingMode = PickingMode.Ignore };
                    count.AddToClassList(InputCountClass);
                    chip.Add(count);
                    inputs.Add(chip);
                }
                row.Add(inputs);

                string id = recipe.Id;
                var button = new Button(() => onCraft?.Invoke(id)) { text = UiText.CraftingButton };
                button.AddToClassList("ash-button");
                button.AddToClassList(RowButtonClass);
                button.SetEnabled(hubEnabled && recipe.CanCraft);
                row.Add(button);

                recipes.Add(row);
            }
        }
    }
}
