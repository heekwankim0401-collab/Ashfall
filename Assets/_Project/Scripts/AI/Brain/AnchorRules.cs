using UnityEngine;

namespace Ashfall.AI
{
    // 거점 반경 (M7 설계 §1.4, Q28·Q29): 보스·호위는 시작 위치에서 anchorRadiusM 밖으로 이동 목적지를 잡지 않는다.
    // 순수 규칙. 수평(XZ)으로만 자르고 높이(y)는 원래 목적지 값을 둔다(NavMeshAgent가 가까운 NavMesh 점으로 간다).
    public static class AnchorRules
    {
        // radiusM ≤ 0(또는 NaN) = 무제한 → point 그대로.
        public static Vector3 Clamp(Vector3 point, Vector3 center, float radiusM)
        {
            if (!(radiusM > 0f))
                return point;
            float dx = point.x - center.x;
            float dz = point.z - center.z;
            float sqr = dx * dx + dz * dz;
            if (sqr <= radiusM * radiusM)
                return point;
            float scale = radiusM / Mathf.Sqrt(sqr);
            return new Vector3(center.x + dx * scale, point.y, center.z + dz * scale);
        }
    }
}
