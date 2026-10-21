using Ashfall.Core;
using UnityEngine;

namespace Ashfall.Raid
{
    // 탈출 조건·구역 판정 (§8.2 EXT-01~04, M4 설계 §3.1). 상태 없음.
    public static class ExtractionRules
    {
        // 구역 = 중심에서 수평 반경 안 + 높이 차 허용 안 (경계 포함).
        // TODO(GDD-?) Q8: 물리 트리거 대신 위치 판정. 반경·높이 허용은 RaidConfigSO 임시값.
        public static bool IsInZone(Vector3 player, Vector3 center, float radiusM, float heightToleranceM)
        {
            if (radiusM <= 0f)
                return false;
            float dx = player.x - center.x;
            float dz = player.z - center.z;
            if (dx * dx + dz * dz > radiusM * radiusM)
                return false;
            return Mathf.Abs(player.y - center.y) <= heightToleranceM;
        }

        // 조건만 본다(구역·활성은 호출자). 처음 걸린 조건 하나를 돌려준다.
        // 순서(문구 우선순위, M4 설계 §4.2): 전원 → 시간 → 배낭 → 아이템.
        // hasConsumedItem은 consumedItem이 없으면 보지 않는다.
        public static ExtractionBlock Evaluate(in ExtractionPointConfig config, float elapsedSec,
            bool switchOn, bool hasBackpack, bool hasConsumedItem)
        {
            // EXT-02: TODO(GDD-?) Q5 스위치는 F 1회로 레이드 끝까지 켜짐
            if (!string.IsNullOrEmpty(config.requiredSwitchId) && !switchOn)
                return ExtractionBlock.NoPower;
            // EXT-03 "레이드 15분 이후만" = 경과 ≥ availableAfterSec
            if (config.availableAfterSec > 0f && elapsedSec < config.availableAfterSec)
                return ExtractionBlock.TooEarly;
            // EXT-04 "배낭 미착용 시만" — 조끼는 허용
            if (config.forbidsBackpack && hasBackpack)
                return ExtractionBlock.BackpackEquipped;
            // EXT-03 신호탄. TODO(GDD-?) Q6: 소모는 대기 완료 순간 (RaidSession)
            if (config.consumedItem != null && !hasConsumedItem)
                return ExtractionBlock.MissingItem;
            return ExtractionBlock.None;
        }
    }
}
