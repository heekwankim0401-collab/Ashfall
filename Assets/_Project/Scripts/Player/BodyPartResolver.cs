using Ashfall.Core;
using UnityEngine;

namespace Ashfall.Player
{
    // 캡슐 피격 지점 → 부위 (설계 D3, 그레이박스 근사). 히트박스 콜라이더 없이 높이 비율·좌우로 판정한다.
    // TODO(GDD-?): 엎드림(캡슐이 낮아짐)에서는 높이 구간이 부정확하다. M3에서 히트박스로 바꿀 수 있다.
    public static class BodyPartResolver
    {
        // localPoint: 플레이어 루트 기준 (발 = y 0). x < 0 = 왼쪽.
        public static BodyPart Resolve(Vector3 localPoint, float capsuleHeight, float capsuleRadius, PlayerConfigSO config)
        {
            float ratio = capsuleHeight > 0f ? Mathf.Clamp01(localPoint.y / capsuleHeight) : 0f;
            bool left = localPoint.x < 0f;

            if (ratio >= config.headZoneFrom)
                return BodyPart.Head;

            if (ratio >= config.stomachZoneFrom)
            {
                // 흉부·복부 구간에서 옆쪽은 팔
                if (Mathf.Abs(localPoint.x) >= capsuleRadius * config.armZoneSideRatio)
                    return left ? BodyPart.LeftArm : BodyPart.RightArm;
                return ratio >= config.thoraxZoneFrom ? BodyPart.Thorax : BodyPart.Stomach;
            }

            return left ? BodyPart.LeftLeg : BodyPart.RightLeg;
        }
    }
}
