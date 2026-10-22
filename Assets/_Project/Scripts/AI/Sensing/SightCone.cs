using Ashfall.Core;
using UnityEngine;

namespace Ashfall.AI
{
    // §7 시야: 콘 110°, 거리 60m, 웅크림 ×0.6, 엎드림 ×0.3 (M3 설계 §1.5). 재 폭풍 상한(M6 §1.5). 할당 없음.
    public static class SightCone
    {
        // 표적 자세에 따른 유효 시야 거리. 모든 상태에서 같은 규칙.
        public static float EffectiveRange(float sightRangeM, TargetPosture posture, AiConfigSO config)
        {
            switch (posture)
            {
                case TargetPosture.Crouching:
                    return sightRangeM * config.crouchSightMultiplier;
                case TargetPosture.Prone:
                    return sightRangeM * config.proneSightMultiplier;
                default:
                    return sightRangeM;
            }
        }

        // 재 폭풍 시야 상한 (§8.3 "시야 30m로 감소", M6 Q26) = min(자세 반영 거리, visibilityCapM).
        // cap = +∞(폭풍 없음)이면 기존과 같다. cap ≤ 0이면 0. NaN은 잘못된 값이라 상한 없음으로 본다.
        public static float EffectiveRange(float sightRangeM, TargetPosture posture, AiConfigSO config, float visibilityCapM)
        {
            float range = EffectiveRange(sightRangeM, posture, config);
            if (float.IsNaN(visibilityCapM))
                return range;
            if (visibilityCapM <= 0f)
                return 0f;
            return visibilityCapM < range ? visibilityCapM : range;
        }

        // fullAngleDeg = 110 → 반각 55. 수평·수직 모두 같은 3D 각으로 판정한다(단순).
        public static bool InCone(Vector3 eye, Vector3 forward, Vector3 point, float fullAngleDeg, float rangeM)
        {
            Vector3 to = point - eye;
            float sqr = to.sqrMagnitude;
            if (sqr > rangeM * rangeM)
                return false;
            if (sqr < 1e-6f)
                return true;
            return Vector3.Angle(forward, to) <= fullAngleDeg * 0.5f;
        }

        // 감각 갱신 1회의 시야 판정 (설계 §6 2~3). 콘·거리 안일 때만 레이를 쏜다:
        // 눈 → 표적 눈, 실패하면 눈 → 표적 중심 (최대 2레이).
        public static bool IsVisible(Vector3 eye, Vector3 forward, Vector3 targetEye, Vector3 targetCenter,
                                     float fullAngleDeg, float rangeM, ILineOfSight los, Transform targetRoot, Transform selfRoot)
        {
            if (!InCone(eye, forward, targetCenter, fullAngleDeg, rangeM))
                return false;
            if (los == null)
                return true;
            return los.CanSee(eye, targetEye, targetRoot, selfRoot) || los.CanSee(eye, targetCenter, targetRoot, selfRoot);
        }
    }
}
