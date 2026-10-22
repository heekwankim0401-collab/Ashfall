using Ashfall.Core;
using UnityEngine;

namespace Ashfall.Audio
{
    // 총성 거리 레이어 (§11 "총성 거리 레이어", M6 Q15). 순수.
    public enum GunshotLayer
    {
        Near,   // ≤ nearMaxM
        Mid,    // ≤ midMaxM
        Far,    // ≤ farMaxM (메아리 꼬리)
    }

    // 거리 → 레이어 하나 + 게인 (Q15: 구간별 1개 레이어, 구간 선형 게인 — 경계에서 연속).
    // 게인 0m gainAt0 → nearMaxM gainAtNear → midMaxM gainAtMid → farMaxM gainAtFar, 그 너머는 안 들림.
    // 소리 전달 지연·차폐 없음(Q16·Q17, TODO(GDD-?) M7 폴리시 후보).
    public static class GunshotMix
    {
        // 들리면 true. 거리가 음수면 0으로 본다. NaN·설정 없음이면 false.
        public static bool Evaluate(float distanceM, AudioConfigSO cfg, out GunshotLayer layer, out float gain)
        {
            layer = GunshotLayer.Near;
            gain = 0f;
            if (cfg == null || float.IsNaN(distanceM))
                return false;

            float d = Mathf.Max(0f, distanceM);
            float near = Mathf.Max(0f, cfg.nearMaxM);
            float mid = Mathf.Max(near, cfg.midMaxM);
            float far = Mathf.Max(mid, cfg.farMaxM);

            if (d <= near)
            {
                layer = GunshotLayer.Near;
                gain = Segment(d, 0f, near, cfg.gainAt0, cfg.gainAtNear);
            }
            else if (d <= mid)
            {
                layer = GunshotLayer.Mid;
                gain = Segment(d, near, mid, cfg.gainAtNear, cfg.gainAtMid);
            }
            else if (d <= far)
            {
                layer = GunshotLayer.Far;
                gain = Segment(d, mid, far, cfg.gainAtMid, cfg.gainAtFar);
            }
            else
            {
                return false;
            }
            gain = Mathf.Max(0f, gain);
            return true;
        }

        // 무기 정의의 분류 세트. 무기 없음(null)·세트 없는 분류 → fallbackGunshotClass 세트. 그것도 없으면 null. 할당 없음.
        public static GunshotSet FindSet(AudioConfigSO cfg, WeaponDefinitionSO weapon)
        {
            if (cfg == null)
                return null;
            if (weapon != null)
            {
                GunshotSet set = FindSet(cfg, weapon.cls);
                if (set != null)
                    return set;
            }
            return FindSet(cfg, cfg.fallbackGunshotClass);
        }

        public static GunshotSet FindSet(AudioConfigSO cfg, WeaponClass cls)
        {
            if (cfg == null || cfg.gunshots == null)
                return null;
            for (int i = 0; i < cfg.gunshots.Length; i++)
            {
                GunshotSet set = cfg.gunshots[i];
                if (set != null && set.weaponClass == cls)
                    return set;
            }
            return null;
        }

        public static AudioClip Clip(GunshotSet set, GunshotLayer layer)
        {
            if (set == null)
                return null;
            switch (layer)
            {
                case GunshotLayer.Mid: return set.mid;
                case GunshotLayer.Far: return set.far;
                default: return set.near;
            }
        }

        // 세트의 그 레이어 클립이 비었으면 대체 분류 세트의 같은 레이어 클립(사용자가 일부만 채운 경우).
        public static AudioClip ClipOrFallback(AudioConfigSO cfg, GunshotSet set, GunshotLayer layer)
        {
            AudioClip clip = Clip(set, layer);
            if (clip != null || cfg == null)
                return clip;
            return Clip(FindSet(cfg, cfg.fallbackGunshotClass), layer);
        }

        private static float Segment(float d, float from, float to, float gainFrom, float gainTo)
        {
            if (to <= from)
                return gainTo;
            return Mathf.Lerp(gainFrom, gainTo, (d - from) / (to - from));
        }
    }
}
