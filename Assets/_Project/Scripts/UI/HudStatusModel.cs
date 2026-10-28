using System.Collections.Generic;
using Ashfall.Core;
using UnityEngine;

namespace Ashfall.UI
{
    // 상시 HUD(B안, 2026-10-28 — RULE-02·04·05 개정, §12) 순수 C# 뷰모델.
    // 데이터는 Core 인터페이스로만 읽는다: IPlayerHudSource(Player), IWeaponHudSource(Weapons), IInventoryService(무게·퀵슬롯·예비 탄창),
    // IRaidInfo(남은 시간), IPlayerTarget(자세). 피격 방향은 PlayerHitReceived를 GameUIPresenter가 NotifyHit으로 넘긴다.
    // 매 프레임 Refresh를 불러도 할당이 없다 — 표시 값이 바뀔 때만 문자열을 새로 만들고 Version을 올린다(뷰는 Version이 바뀔 때만 그린다).

    public enum HudHpTone
    {
        Normal,     // Bone
        Warn,       // Ember (HudConfigSO.partHpWarnBelow 미만)
        Destroyed,  // 0 = 파괴 (어둡게)
    }

    public enum HudWeightTone
    {
        Normal,
        Warn,   // > IPlayerHudSource.WeightPenaltyStartKg (§4.1 25kg)
        Heavy,  // > IPlayerHudSource.SprintMaxWeightKg (§4.1 40kg)
    }

    // 피격 방향 가장자리 띠 (화면 기준). 앞 = 위, 뒤 = 아래.
    public enum HudEdge
    {
        Top = 0,
        Right = 1,
        Bottom = 2,
        Left = 3,
    }

    // 표시 규칙 (순수 함수, EditMode 테스트 대상)
    public static class HudRules
    {
        public const int EdgeCount = 4;
        public const int QuickSlotCount = 4;   // 키 4~7

        // 레이드 남은 시간 "분:초" (올림 초, 음수·NaN = 0). 예: 1500 → "25:00", 65.2 → "01:06"
        public static string FormatRemaining(float remainingSec) => FormatSeconds(CeilSeconds(remainingSec));

        public static int CeilSeconds(float sec) => sec > 0f ? Mathf.CeilToInt(sec) : 0;

        public static string FormatSeconds(int totalSec)
        {
            if (totalSec < 0)
                totalSec = 0;
            int minutes = totalSec / 60;
            int seconds = totalSec % 60;
            return (minutes < 10 ? "0" : string.Empty) + minutes + ":" + (seconds < 10 ? "0" : string.Empty) + seconds;
        }

        // 부위 HP 색: 0 이하 = 파괴, 비율 < warnBelow = 경고, 그 외 = 기본. 최대가 0 이하면 파괴로 본다.
        public static HudHpTone HpTone(float hp, float maxHp, float warnBelow)
        {
            if (!(hp > 0f) || !(maxHp > 0f))
                return HudHpTone.Destroyed;
            return hp / maxHp < warnBelow ? HudHpTone.Warn : HudHpTone.Normal;
        }

        // §4.1: 25kg "초과"부터 감속, 40kg "초과" 질주 불가 → 경계 값 자체는 아래 단계
        public static HudWeightTone WeightTone(float kg, float warnKg, float heavyKg)
        {
            if (kg > heavyKg)
                return HudWeightTone.Heavy;
            if (kg > warnKg)
                return HudWeightTone.Warn;
            return HudWeightTone.Normal;
        }

        // 정면 기준 각도(+ = 오른쪽) → 띠. |각| ≤ 45 = 위(앞), 45 < 각 ≤ 135 = 오른쪽, -135 ≤ 각 < -45 = 왼쪽, 나머지 = 아래(뒤).
        public static HudEdge EdgeFor(float relativeYawDeg)
        {
            float a = Mathf.Repeat(relativeYawDeg + 180f, 360f) - 180f;   // -180 ~ 180
            if (a >= -45f && a <= 45f)
                return HudEdge.Top;
            if (a > 45f && a <= 135f)
                return HudEdge.Right;
            if (a < -45f && a >= -135f)
                return HudEdge.Left;
            return HudEdge.Bottom;
        }

        // 남은 시간에 비례해 선형으로 사라진다
        public static float EdgeOpacity(float remainingSec, float durationSec, float maxOpacity)
        {
            if (!(remainingSec > 0f) || !(durationSec > 0f))
                return 0f;
            return maxOpacity * Mathf.Clamp01(remainingSec / durationSec);
        }

        // 막대 채움 0~1 (최대 0 이하 = 0)
        public static float Fill01(float value, float max) => max > 0f ? Mathf.Clamp01(value / max) : 0f;

        // 스태미나 "가득이면 흐리게"
        public static bool IsFull(float value, float max) => max > 0f && value >= max - 1e-3f;

        // 막대 비교·스타일용 정수 퍼센트 (0~100)
        public static int Percent(float fill01) => Mathf.RoundToInt(Mathf.Clamp01(fill01) * 100f);

        // 예비 탄창 수 = 주머니·조끼(재장전이 꺼내는 곳, IMagazineSource와 같은 범위)의 같은 탄 쓰는 탄 있는 탄창.
        // TODO(GDD-?): 배낭 탄창·빈 탄창을 셀지 문서에 없다 — 재장전에 바로 쓸 수 있는 것만 센다.
        public static int CountSpareMagazines(IInventoryService inventory, AmmoType ammo)
        {
            if (inventory == null)
                return 0;
            int count = 0;
            IReadOnlyList<IGridView> grids = inventory.StorageGrids;
            if (grids == null)
                return 0;
            for (int g = 0; g < grids.Count; g++)
            {
                IGridView grid = grids[g];
                if (grid == null || grid.Ref.Kind == GridKind.Backpack)
                    continue;
                IReadOnlyList<PlacedItem> items = grid.Items;
                if (items == null)
                    continue;
                for (int i = 0; i < items.Count; i++)
                {
                    if (items[i].Item is Magazine mag && !mag.IsEmpty && mag.Definition != null && mag.Definition.ammo == ammo)
                        count++;
                }
            }
            return count;
        }

        // "31.5kg" (소수 한 자리, 문화권 무관)
        public static string FormatWeight(float kg)
        {
            int tenths = Mathf.RoundToInt(Mathf.Max(0f, kg) * 10f);
            return (tenths / 10) + "." + (tenths % 10) + "kg";
        }
    }

    public sealed class HudStatusModel
    {
        public const int PartCount = 7;

        private readonly HudConfigSO config;
        private readonly string[] quickKeys = new string[HudRules.QuickSlotCount];

        // ── 출력 (뷰가 읽는다) ──
        public int Version { get; private set; }       // 문구·색·표시 여부·막대가 바뀌면 +1
        public int EdgeVersion { get; private set; }   // 피격 가장자리 불투명도가 바뀌면 +1

        public bool PlayerVisible { get; private set; }
        public readonly string[] PartTexts = new string[PartCount];
        public readonly HudHpTone[] PartTones = new HudHpTone[PartCount];
        public readonly string[] PartStatusTexts = new string[PartCount];   // 출혈·중출혈·골절 (없으면 빈 문자열)
        public string BodyEffectsText { get; private set; } = string.Empty;  // 방사능·탈수·허기 (전신)

        public float StaminaFill { get; private set; }
        public bool StaminaFaded { get; private set; }
        public float HydrationFill { get; private set; }
        public float EnergyFill { get; private set; }

        public bool RadiationVisible { get; private set; }
        public float RadiationFill { get; private set; }
        public string RadiationText { get; private set; } = string.Empty;

        public bool WeaponVisible { get; private set; }
        public string WeaponText { get; private set; } = string.Empty;      // "이름 · 자동"
        public string AmmoText { get; private set; } = string.Empty;        // "27/30" (탄창 없음 = "—")
        public string SpareText { get; private set; } = string.Empty;       // "예비 2"
        public string ConditionText { get; private set; } = string.Empty;   // 양호/낡음/위험 (내구도 없음 = 빈)
        public ItemConditionTier ConditionTier { get; private set; }

        public bool QuickSlotsVisible { get; private set; }
        public readonly string[] QuickSlotTexts = new string[HudRules.QuickSlotCount];
        public readonly bool[] QuickSlotFilled = new bool[HudRules.QuickSlotCount];

        public bool PostureVisible { get; private set; }
        public string PostureText { get; private set; } = string.Empty;
        public bool WeightVisible { get; private set; }
        public string WeightText { get; private set; } = string.Empty;
        public HudWeightTone WeightTone { get; private set; }

        public bool TimeVisible { get; private set; }
        public string TimeText { get; private set; } = string.Empty;

        public readonly float[] EdgeOpacity = new float[HudRules.EdgeCount];

        // ── 바뀜 판정용 이전 값 ──
        private readonly long[] partKeys = new long[PartCount];
        private readonly int[] partStatusKeys = new int[PartCount];
        private int bodyEffectsKey = -1;
        private int staminaKey = -1, hydrationKey = -1, energyKey = -1, radiationKey = -1;
        private WeaponItem weaponRef;
        private FireMode weaponMode;
        private long ammoKey = long.MinValue;
        private int spareKey = int.MinValue;
        private int conditionKey = int.MinValue;
        private readonly ItemInstance[] quickRefs = new ItemInstance[HudRules.QuickSlotCount];
        private readonly int[] quickUses = new int[HudRules.QuickSlotCount];
        private int postureKey = -1;
        private int weightKey = int.MinValue;
        private int timeKey = int.MinValue;
        private readonly float[] edgeRemaining = new float[HudRules.EdgeCount];

        // config 필수(null이면 예외). quickSlotKeys: 퀵슬롯 키 표시 문자열(입력 바인딩, 앞에서 4개). 없으면 "4"~"7".
        public HudStatusModel(HudConfigSO config, IReadOnlyList<string> quickSlotKeys = null)
        {
            this.config = config != null ? config : throw new System.ArgumentNullException(nameof(config));
            for (int i = 0; i < quickKeys.Length; i++)
                quickKeys[i] = quickSlotKeys != null && i < quickSlotKeys.Count && !string.IsNullOrEmpty(quickSlotKeys[i])
                    ? quickSlotKeys[i] : (i + 4).ToString();
            for (int i = 0; i < PartCount; i++)
            {
                partKeys[i] = long.MinValue;
                partStatusKeys[i] = -1;
                PartTexts[i] = string.Empty;
                PartStatusTexts[i] = string.Empty;
            }
            for (int i = 0; i < QuickSlotTexts.Length; i++)
            {
                QuickSlotTexts[i] = UiText.HudQuickSlot(quickKeys[i], null);
                quickUses[i] = -1;
            }
        }


        // 레지스트리에서 원천을 찾아 갱신 (GameUIPresenter.Tick). 사전 조회만, 할당 없음.
        public void Tick(float dt, ServiceRegistry services)
        {
            services.TryGet(out IPlayerHudSource player);
            services.TryGet(out IWeaponHudSource weapon);
            services.TryGet(out IInventoryService inventory);
            services.TryGet(out IRaidInfo raid);
            services.TryGet(out IPlayerTarget target);
            Refresh(player, weapon, inventory, raid, target);
            TickEdges(dt);
        }

        // 피격 (PlayerHitReceived). 방향을 모르면 네 띠 모두.
        public void NotifyHit(float relativeYawDeg, bool hasDirection)
        {
            float duration = config.hitEdgeDurationSec;
            if (hasDirection)
            {
                edgeRemaining[(int)HudRules.EdgeFor(relativeYawDeg)] = duration;
            }
            else
            {
                for (int i = 0; i < edgeRemaining.Length; i++)
                    edgeRemaining[i] = duration;
            }
            UpdateEdgeOpacity();
        }

        public void TickEdges(float dt)
        {
            bool any = false;
            for (int i = 0; i < edgeRemaining.Length; i++)
            {
                if (edgeRemaining[i] > 0f)
                {
                    edgeRemaining[i] = Mathf.Max(0f, edgeRemaining[i] - Mathf.Max(0f, dt));
                    any = true;
                }
            }
            if (any)
                UpdateEdgeOpacity();
        }

        private void UpdateEdgeOpacity()
        {
            float duration = config.hitEdgeDurationSec, max = config.hitEdgeMaxOpacity;
            bool changed = false;
            for (int i = 0; i < EdgeOpacity.Length; i++)
            {
                float o = HudRules.EdgeOpacity(edgeRemaining[i], duration, max);
                if (!Mathf.Approximately(o, EdgeOpacity[i]))
                {
                    EdgeOpacity[i] = o;
                    changed = true;
                }
            }
            if (changed)
                EdgeVersion++;
        }

        public void Refresh(IPlayerHudSource player, IWeaponHudSource weapon, IInventoryService inventory, IRaidInfo raid, IPlayerTarget target)
        {
            RefreshPlayer(player);
            RefreshWeapon(weapon != null ? weapon.CurrentWeapon : null, weapon != null ? weapon.CurrentFireMode : FireMode.Semi, inventory);
            RefreshInventory(inventory, player);
            RefreshPosture(target);
            RefreshTime(raid);
        }

        // ── 플레이어: 부위 HP·상태이상·막대·방사능 ──
        private void RefreshPlayer(IPlayerHudSource player)
        {
            bool visible = player != null;
            if (PlayerVisible != visible)
            {
                PlayerVisible = visible;
                Version++;
            }
            if (!visible)
                return;

            float warn = config.partHpWarnBelow;
            for (int i = 0; i < PartCount; i++)
            {
                var part = (BodyPart)i;
                float hp = player.GetPartHp(part);
                float max = player.GetPartMaxHp(part);
                HudHpTone tone = HudRules.HpTone(hp, max, warn);
                int hpInt = HudRules.CeilSeconds(hp);   // 올림 정수 (0.3 = 1, 파괴 전엔 0이 보이지 않게)
                int maxInt = HudRules.CeilSeconds(max);
                long key = ((long)hpInt * 100000L + maxInt) * 4L + (int)tone;
                if (key != partKeys[i])
                {
                    partKeys[i] = key;
                    PartTexts[i] = UiText.HudPartHp(BodyStatusPresenter.PartLabel(part), hpInt, maxInt);
                    PartTones[i] = tone;
                    Version++;
                }

                int statusKey = (player.HasStatus(part, StatusEffect.Bleed) ? 1 : 0)
                              | (player.HasStatus(part, StatusEffect.HeavyBleed) ? 2 : 0)
                              | (player.HasStatus(part, StatusEffect.Fracture) ? 4 : 0);
                if (statusKey != partStatusKeys[i])
                {
                    partStatusKeys[i] = statusKey;
                    PartStatusTexts[i] = UiText.HudStatusIcons(statusKey);
                    Version++;
                }
            }

            SurvivalTier rad = player.GetSurvivalTier(SurvivalStat.Radiation);
            SurvivalTier hyd = player.GetSurvivalTier(SurvivalStat.Hydration);
            SurvivalTier eng = player.GetSurvivalTier(SurvivalStat.Energy);
            int effectsKey = (int)rad * 9 + (int)hyd * 3 + (int)eng;
            if (effectsKey != bodyEffectsKey)
            {
                bodyEffectsKey = effectsKey;
                BodyEffectsText = UiText.HudBodyEffects(rad, hyd, eng);
                Version++;
            }

            float staminaMax = player.StaminaMax;
            float stamina = player.Stamina;
            int sKey = HudRules.Percent(HudRules.Fill01(stamina, staminaMax)) * 2 + (HudRules.IsFull(stamina, staminaMax) ? 1 : 0);
            if (sKey != staminaKey)
            {
                staminaKey = sKey;
                StaminaFill = sKey / 2 / 100f;
                StaminaFaded = (sKey & 1) == 1;
                Version++;
            }
            int hKey = HudRules.Percent(HudRules.Fill01(player.Hydration, player.HydrationMax));
            if (hKey != hydrationKey)
            {
                hydrationKey = hKey;
                HydrationFill = hKey / 100f;
                Version++;
            }
            int eKey = HudRules.Percent(HudRules.Fill01(player.Energy, player.EnergyMax));
            if (eKey != energyKey)
            {
                energyKey = eKey;
                EnergyFill = eKey / 100f;
                Version++;
            }

            // 방사능 누적 (0~100, 누적 > 0일 때만). 표시 정수 = 올림 (0.2 → 1, 처음 쌓이자마자 보인다)
            float dose = player.RadiationDose;
            float radiationMax = config.radiationDisplayMax;
            int doseInt = dose > 0f ? Mathf.Min(HudRules.CeilSeconds(dose), Mathf.CeilToInt(radiationMax)) : 0;
            if (doseInt != radiationKey)
            {
                radiationKey = doseInt;
                RadiationVisible = doseInt > 0;
                RadiationFill = HudRules.Fill01(doseInt, radiationMax);
                RadiationText = doseInt > 0 ? UiText.HudRadiation(doseInt) : string.Empty;
                Version++;
            }
        }

        // ── 무기: 이름·발사 모드·탄 수/용량·예비 탄창·내구도 등급 ──
        private void RefreshWeapon(WeaponItem weapon, FireMode mode, IInventoryService inventory)
        {
            bool visible = weapon != null && weapon.Definition != null;
            if (WeaponVisible != visible)
            {
                WeaponVisible = visible;
                Version++;
            }
            if (!visible)
            {
                weaponRef = null;
                return;
            }

            if (!ReferenceEquals(weapon, weaponRef) || mode != weaponMode)
            {
                weaponRef = weapon;
                weaponMode = mode;
                WeaponText = UiText.HudWeapon(weapon.Definition.displayName, mode);
                Version++;
            }

            Magazine mag = weapon.LoadedMagazine;
            long aKey = mag != null ? (long)mag.Rounds * 100000L + mag.Capacity : -1L;
            if (aKey != ammoKey)
            {
                ammoKey = aKey;
                AmmoText = mag != null ? UiText.HudAmmo(mag.Rounds, mag.Capacity) : UiText.HudNoMagazine;
                Version++;
            }

            // 튜브(내장 탄창) 무기는 탄창을 갈지 않는다 → 예비 탄창 표시 없음. TODO(GDD-?): 낱탄 수를 보일지 문서에 없다.
            int spare = weapon.HasInternalMagazine ? -1 : HudRules.CountSpareMagazines(inventory, weapon.Definition.ammo);
            if (spare != spareKey)
            {
                spareKey = spare;
                SpareText = spare >= 0 ? UiText.HudSpareMagazines(spare) : string.Empty;
                Version++;
            }

            int cKey = ItemConditionRules.TryGetTier(weapon, config, out ItemConditionTier tier) ? (int)tier : -1;
            if (cKey != conditionKey)
            {
                conditionKey = cKey;
                ConditionTier = tier;
                ConditionText = cKey >= 0 ? UiText.ConditionWord(tier) : string.Empty;
                Version++;
            }
        }

        // ── 인벤토리: 퀵슬롯 4~7·총 무게 ──
        // 무게 경고 단계 경계는 플레이어 원천(PlayerConfigSO §4.1)에서 읽는다. 플레이어가 없으면 경고 없음.
        private void RefreshInventory(IInventoryService inventory, IPlayerHudSource player)
        {
            bool visible = inventory != null;
            if (QuickSlotsVisible != visible || WeightVisible != visible)
            {
                QuickSlotsVisible = visible;
                WeightVisible = visible;
                Version++;
            }
            if (!visible)
                return;

            for (int i = 0; i < HudRules.QuickSlotCount; i++)
            {
                ItemInstance item = inventory.GetQuickSlot(i);
                int uses = item is MedicalItem medical ? medical.UsesLeft : 0;
                if (!ReferenceEquals(item, quickRefs[i]) || uses != quickUses[i])
                {
                    quickRefs[i] = item;
                    quickUses[i] = uses;
                    QuickSlotTexts[i] = UiText.HudQuickSlot(quickKeys[i], item);
                    QuickSlotFilled[i] = item != null;
                    Version++;
                }
            }

            float kg = inventory.TotalWeightKg;
            HudWeightTone tone = player != null
                ? HudRules.WeightTone(kg, player.WeightPenaltyStartKg, player.SprintMaxWeightKg)
                : HudWeightTone.Normal;
            int wKey = Mathf.RoundToInt(Mathf.Max(0f, kg) * 10f) * 4 + (int)tone;
            if (wKey != weightKey)
            {
                weightKey = wKey;
                WeightText = HudRules.FormatWeight(kg);
                WeightTone = tone;
                Version++;
            }
        }

        private void RefreshPosture(IPlayerTarget target)
        {
            bool visible = target != null;
            if (PostureVisible != visible)
            {
                PostureVisible = visible;
                Version++;
            }
            if (!visible)
                return;
            int key = (int)target.Posture;
            if (key != postureKey)
            {
                postureKey = key;
                PostureText = UiText.HudPosture(target.Posture);
                Version++;
            }
        }

        // ── 레이드 남은 시간 (RULE-02 표시 개정): IRaidInfo가 있을 때만 = 레이드에서만 ──
        private void RefreshTime(IRaidInfo raid)
        {
            bool visible = raid != null;
            if (TimeVisible != visible)
            {
                TimeVisible = visible;
                Version++;
            }
            if (!visible)
                return;
            int sec = HudRules.CeilSeconds(raid.RemainingSec);
            if (sec != timeKey)
            {
                timeKey = sec;
                TimeText = HudRules.FormatSeconds(sec);
                Version++;
            }
        }
    }
}
