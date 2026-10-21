using Ashfall.Core;

namespace Ashfall.UI
{
    // §12 허용 HUD: F 상호작용 프롬프트("F  열기: 상자" / "F  줍기: 붕대").
    public sealed class InteractionPromptModel
    {
        // null = 표시 안 함
        public string Text { get; private set; }

        public void Apply(in InteractionPromptChanged evt, string keyLabel)
        {
            Text = evt.Visible ? UiText.Prompt(evt.Kind, evt.TargetName, keyLabel) : null;
        }

        public void Clear() => Text = null;
    }
}
