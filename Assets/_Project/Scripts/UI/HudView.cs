using System;
using UnityEngine.UIElements;

namespace Ashfall.UI
{
    // §12 HUD: 기본은 아무것도 없다. 허용된 일시 표시만 — F 상호작용 프롬프트, RULE-04 탄창 확인 문구.
    // 체력·탄수·미니맵·크로스헤어는 만들지 않는다 (RULE-04~07).
    public sealed class HudView
    {
        public const string RootName = "hud";
        public const string PromptName = "hud-prompt";
        public const string ToastName = "hud-toast";

        private readonly VisualElement root;
        private readonly Label prompt;
        private readonly Label toast;

        private bool visible = true;
        private string promptText;
        private string toastText;

        public HudView(VisualElement documentRoot)
        {
            root = documentRoot.Q<VisualElement>(RootName) ?? throw Missing(RootName);
            prompt = documentRoot.Q<Label>(PromptName) ?? throw Missing(PromptName);
            toast = documentRoot.Q<Label>(ToastName) ?? throw Missing(ToastName);
            Apply(prompt, null);
            Apply(toast, null);
        }

        // 바뀔 때만 요소를 건드린다 (매 프레임 호출)
        public void Set(bool hudVisible, string toastValue, string promptValue)
        {
            if (visible != hudVisible)
            {
                visible = hudVisible;
                root.style.display = hudVisible ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (!string.Equals(promptText, promptValue))
            {
                promptText = promptValue;
                Apply(prompt, promptValue);
            }
            if (!string.Equals(toastText, toastValue))
            {
                toastText = toastValue;
                Apply(toast, toastValue);
            }
        }

        private static void Apply(Label label, string text)
        {
            label.text = text ?? string.Empty;
            label.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        internal static Exception Missing(string name) =>
            new InvalidOperationException($"[GameUI] UXML에 '{name}' 요소가 없다 (Art/UI/GameUI.uxml 확인).");
    }
}
