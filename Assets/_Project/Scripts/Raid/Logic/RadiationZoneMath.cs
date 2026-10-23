using System.Collections.Generic;
using UnityEngine;

namespace Ashfall.Raid
{
    // 방사능 구역률 계산 (§4.3 STAT-RAD, M7 설계 §1.5·§3.10, Q39). 순수 — 할당 없음.
    // 구역마다 중심과의 수평(XZ) 거리 d로:
    //   d ≤ 반경 − 감쇠 폭 → 최대 률, 그 바깥 ~ 반경 → 선형으로 0, d ≥ 반경 → 0. 겹치면 합.
    // 반경·률이 0 이하이거나 유한하지 않은 구역은 0. 감쇠 폭 ≤ 0(또는 NaN) = 경계에서 끊김(d ≤ 반경이면 최대),
    // 감쇠 폭 ≥ 반경 = 중심에서부터 선형.
    public static class RadiationZoneMath
    {
        public static float RateAt(IReadOnlyList<RadiationZoneConfig> zones, Vector3 position)
        {
            if (zones == null)
                return 0f;

            float total = 0f;
            for (int i = 0; i < zones.Count; i++)
                total += RateAt(zones[i], position);
            return total;
        }

        public static float RateAt(in RadiationZoneConfig zone, Vector3 position)
        {
            float radius = zone.radiusM;
            float rate = zone.ratePerSec;
            if (!(radius > 0f) || !(rate > 0f) || float.IsInfinity(radius) || float.IsInfinity(rate))
                return 0f;

            float dx = position.x - zone.centerXZ.x;
            float dz = position.z - zone.centerXZ.y;
            float d = Mathf.Sqrt(dx * dx + dz * dz);
            if (!(d <= radius))      // 밖 또는 NaN 위치
                return 0f;

            float falloff = zone.edgeFalloffM > 0f ? Mathf.Min(zone.edgeFalloffM, radius) : 0f;
            float inner = radius - falloff;
            if (d <= inner)
                return rate;
            // inner < d ≤ radius, falloff > 0
            return rate * (radius - d) / falloff;
        }
    }
}
