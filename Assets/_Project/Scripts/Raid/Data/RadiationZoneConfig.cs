using System;
using UnityEngine;

namespace Ashfall.Raid
{
    // 방사능 구역 하나 (§4.3 STAT-RAD, §8.1 정유소, M7 설계 §3.10, Q39). 원형 — 높이는 보지 않는다(수평 거리).
    // 률 = (반경 − 감쇠 폭) 안 최대, 가장자리까지 선형으로 0, 밖 0. 계산은 RadiationZoneMath.
    [Serializable]
    public struct RadiationZoneConfig
    {
        [Tooltip("\"RAD-REF\" — 표시·테스트용 id")]
        public string id;
        [Tooltip("중심 월드 좌표 XZ (x = 월드 X, y = 월드 Z). TODO(GDD-?) Q39")]
        public Vector2 centerXZ;
        [Tooltip("반경 (m). 0 이하 = 꺼짐. TODO(GDD-?) Q39")]
        public float radiusM;
        [Tooltip("구역 안쪽 최대 방사능률 (선량/초). 0 이하 = 꺼짐. TODO(GDD-?) Q39")]
        public float ratePerSec;
        [Tooltip("가장자리 감쇠 폭 (m) — 반경 − 이 값부터 반경까지 선형으로 0. 0 이하 = 경계에서 바로 끊김, 반경 이상 = 중심부터 선형. TODO(GDD-?) Q39")]
        public float edgeFalloffM;
    }
}
