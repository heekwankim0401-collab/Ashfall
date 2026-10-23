using UnityEngine;

namespace Ashfall.Core
{
    // M7 STAT-RAD 방사능 구역 (§4.3, §8.1 정유소, M7 설계 §1.2·§1.5, Q39). Raid(RadiationField)가 OnEnable에 등록, OnDisable에 해제한다.
    // 레이드 밖(은신처·메인 메뉴)에는 없다 = 구역 방사능 0. 소비자(Player 선량 누적·Audio 가이거)는 매 틱 동기로 읽는다(할당 없음).
    // 재 폭풍 방사능(IAshStormInfo.RadiationPerSec)은 포함하지 않는다 — 합은 소비자가 한다(방독면은 폭풍 몫만 막는다, Q38).
    public interface IRadiationField
    {
        // 그 위치(수평 거리 기준)의 구역 방사능률 (선량/초). 구역 밖 = 0, 겹치면 합.
        float ZoneRatePerSec(Vector3 position);
    }
}
