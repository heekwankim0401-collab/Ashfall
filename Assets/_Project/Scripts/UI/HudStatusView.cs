using UnityEngine.UIElements;

namespace Ashfall.UI
{
    // 상시 HUD(B안, 2026-10-28 — RULE-02·04·05 개정) 얇은 뷰. 상태·문구는 HudStatusModel이 정한다.
    // 모델 Version이 바뀔 때만 라벨·클래스·막대 폭을 건드리고, 피격 가장자리는 EdgeVersion이 바뀔 때만 불투명도를 바꾼다.
    // 색·크기는 Art/UI/Ashfall.uss(hud-* 클래스, --hud-* 변수)에 있다.
    public sealed class HudStatusView
    {
        public const string TimeName = "hud-time";
        public const string LeftName = "hud-left";
        public const string RightName = "hud-right";
        public const string WeaponName = "hud-weapon";
        public const string WeaponNameLabel = "hud-weapon-name";
        public const string AmmoName = "hud-ammo";
        public const string SpareName = "hud-spare";
        public const string ConditionName = "hud-condition";
        public const string PostureName = "hud-posture";
        public const string WeightName = "hud-weight";
        public const string QuickSlotsName = "hud-quickslots";
        public const string QuickSlotPrefix = "hud-quick-";
        public const string PartPrefix = "hud-part-";
        public const string PartStatusPrefix = "hud-part-status-";
        public const string BodyEffectsName = "hud-body-effects";
        public const string RadiationName = "hud-radiation";
        public const string RadiationTextName = "hud-radiation-text";
        public const string RadiationFillName = "hud-radiation-fill";
        public const string StaminaBarName = "hud-stamina-bar";
        public const string StaminaFillName = "hud-stamina-fill";
        public const string HydrationFillName = "hud-hydration-fill";
        public const string EnergyFillName = "hud-energy-fill";
        public static readonly string[] EdgeNames = { "hud-edge-top", "hud-edge-right", "hud-edge-bottom", "hud-edge-left" };

        public const string PartWarnClass = "hud-part--warn";
        public const string PartDestroyedClass = "hud-part--destroyed";
        public const string FadedClass = "hud-faded";
        public const string WeightWarnClass = "hud-weight--warn";
        public const string WeightHeavyClass = "hud-weight--heavy";
        public const string ConditionWornClass = "hud-condition--worn";
        public const string ConditionCriticalClass = "hud-condition--critical";
        public const string QuickEmptyClass = "hud-quick--empty";

        private readonly VisualElement left, right, weapon, quickSlots, radiation, staminaRow;
        private readonly Label time, weaponName, ammo, spare, condition, posture, weight, bodyEffects, radiationText;
        private readonly VisualElement radiationFill, staminaFill, hydrationFill, energyFill;
        private readonly Label[] parts = new Label[HudStatusModel.PartCount];
        private readonly Label[] partStatus = new Label[HudStatusModel.PartCount];
        private readonly Label[] quick = new Label[HudRules.QuickSlotCount];
        private readonly VisualElement[] edges = new VisualElement[HudRules.EdgeCount];

        private int drawnVersion = -1;
        private int drawnEdgeVersion = -1;

        public HudStatusView(VisualElement documentRoot)
        {
            time = Find<Label>(documentRoot, TimeName);
            left = Find<VisualElement>(documentRoot, LeftName);
            right = Find<VisualElement>(documentRoot, RightName);
            weapon = Find<VisualElement>(documentRoot, WeaponName);
            weaponName = Find<Label>(documentRoot, WeaponNameLabel);
            ammo = Find<Label>(documentRoot, AmmoName);
            spare = Find<Label>(documentRoot, SpareName);
            condition = Find<Label>(documentRoot, ConditionName);
            posture = Find<Label>(documentRoot, PostureName);
            weight = Find<Label>(documentRoot, WeightName);
            quickSlots = Find<VisualElement>(documentRoot, QuickSlotsName);
            bodyEffects = Find<Label>(documentRoot, BodyEffectsName);
            radiation = Find<VisualElement>(documentRoot, RadiationName);
            radiationText = Find<Label>(documentRoot, RadiationTextName);
            radiationFill = Find<VisualElement>(documentRoot, RadiationFillName);
            staminaRow = Find<VisualElement>(documentRoot, StaminaBarName).parent;
            staminaFill = Find<VisualElement>(documentRoot, StaminaFillName);
            hydrationFill = Find<VisualElement>(documentRoot, HydrationFillName);
            energyFill = Find<VisualElement>(documentRoot, EnergyFillName);
            for (int i = 0; i < parts.Length; i++)
            {
                parts[i] = Find<Label>(documentRoot, PartPrefix + i);
                partStatus[i] = Find<Label>(documentRoot, PartStatusPrefix + i);
            }
            for (int i = 0; i < quick.Length; i++)
                quick[i] = Find<Label>(documentRoot, QuickSlotPrefix + i);
            for (int i = 0; i < edges.Length; i++)
            {
                edges[i] = Find<VisualElement>(documentRoot, EdgeNames[i]);
                edges[i].style.display = DisplayStyle.None;   // 평소 숨김
            }

            // 원천이 등록되기 전(첫 Render 전)에는 아무것도 보이지 않는다
            Show(time, false);
            Show(left, false);
            Show(weapon, false);
            Show(posture, false);
            Show(weight, false);
            Show(quickSlots, false);
            Show(radiation, false);
        }

        // 매 프레임 호출. 바뀐 것이 없으면 아무것도 하지 않는다.
        public void Render(HudStatusModel model)
        {
            if (model == null)
                return;
            if (model.EdgeVersion != drawnEdgeVersion)
            {
                drawnEdgeVersion = model.EdgeVersion;
                for (int i = 0; i < edges.Length; i++)
                {
                    float o = model.EdgeOpacity[i];
                    edges[i].style.display = o > 0f ? DisplayStyle.Flex : DisplayStyle.None;
                    edges[i].style.opacity = o;
                }
            }
            if (model.Version == drawnVersion)
                return;
            drawnVersion = model.Version;

            // 위 가운데: 레이드 남은 시간
            Show(time, model.TimeVisible);
            SetText(time, model.TimeText);

            // 왼쪽 아래: 방사능·막대·부위
            Show(left, model.PlayerVisible);
            if (model.PlayerVisible)
            {
                for (int i = 0; i < parts.Length; i++)
                {
                    SetText(parts[i], model.PartTexts[i]);
                    parts[i].EnableInClassList(PartWarnClass, model.PartTones[i] == HudHpTone.Warn);
                    parts[i].EnableInClassList(PartDestroyedClass, model.PartTones[i] == HudHpTone.Destroyed);
                    SetText(partStatus[i], model.PartStatusTexts[i]);
                    Show(partStatus[i], !string.IsNullOrEmpty(model.PartStatusTexts[i]));
                }
                SetText(bodyEffects, model.BodyEffectsText);
                Show(bodyEffects, !string.IsNullOrEmpty(model.BodyEffectsText));
                SetFill(staminaFill, model.StaminaFill);
                staminaRow.EnableInClassList(FadedClass, model.StaminaFaded);
                SetFill(hydrationFill, model.HydrationFill);
                SetFill(energyFill, model.EnergyFill);
                Show(radiation, model.RadiationVisible);
                SetText(radiationText, model.RadiationText);
                SetFill(radiationFill, model.RadiationFill);
            }

            // 오른쪽 아래: 자세·무게, 무기
            Show(posture, model.PostureVisible);
            SetText(posture, model.PostureText);
            Show(weight, model.WeightVisible);
            SetText(weight, model.WeightText);
            weight.EnableInClassList(WeightWarnClass, model.WeightTone == HudWeightTone.Warn);
            weight.EnableInClassList(WeightHeavyClass, model.WeightTone == HudWeightTone.Heavy);

            Show(weapon, model.WeaponVisible);
            if (model.WeaponVisible)
            {
                SetText(weaponName, model.WeaponText);
                SetText(ammo, model.AmmoText);
                SetText(spare, model.SpareText);
                Show(spare, !string.IsNullOrEmpty(model.SpareText));
                SetText(condition, model.ConditionText);
                Show(condition, !string.IsNullOrEmpty(model.ConditionText));
                condition.EnableInClassList(ConditionWornClass, model.ConditionTier == ItemConditionTier.Worn);
                condition.EnableInClassList(ConditionCriticalClass, model.ConditionTier == ItemConditionTier.Critical);
            }

            // 아래 가운데: 의약품 퀵슬롯 4~7
            Show(quickSlots, model.QuickSlotsVisible);
            for (int i = 0; i < quick.Length; i++)
            {
                SetText(quick[i], model.QuickSlotTexts[i]);
                quick[i].EnableInClassList(QuickEmptyClass, !model.QuickSlotFilled[i]);
            }
        }

        private static void SetText(Label label, string text)
        {
            text = text ?? string.Empty;
            if (!ReferenceEquals(label.text, text) && label.text != text)
                label.text = text;
        }

        private static void SetFill(VisualElement fill, float fill01) =>
            fill.style.width = new Length(fill01 * 100f, LengthUnit.Percent);

        private static void Show(VisualElement element, bool visible)
        {
            DisplayStyle display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (element.style.display != display)
                element.style.display = display;
        }

        private static T Find<T>(VisualElement root, string name) where T : VisualElement =>
            root.Q<T>(name) ?? throw HudView.Missing(name);
    }
}
