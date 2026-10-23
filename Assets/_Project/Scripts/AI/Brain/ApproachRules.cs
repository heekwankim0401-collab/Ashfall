using UnityEngine;

namespace Ashfall.AI
{
    // 접근 정지 (7D-3 발견 1, AiConfigSO.approachStopDistanceM — TODO(GDD-?)): 약탈자·습격자가 LKP·표적 위치로 다가갈 때
    // 그 점까지 걸어 들어가지 않고 자기 쪽으로 stopDistanceM 앞에서 멈춘다(NavMeshAgent는 플레이어 CharacterController와 부딪히지 않아
    // 그대로 두면 몸이 겹치고, 겹친 적이 다른 사수의 시야를 막는다). 순수 규칙, 수평(XZ)만 당기고 높이는 표적 값을 둔다.
    public static class ApproachRules
    {
        // self가 이미 target에서 수평 stopDistanceM 안이면 true (= 도착).
        public static bool IsWithinStop(Vector3 self, Vector3 target, float stopDistanceM)
        {
            if (!(stopDistanceM > 0f))
                return false;
            float dx = self.x - target.x;
            float dz = self.z - target.z;
            return dx * dx + dz * dz <= stopDistanceM * stopDistanceM;
        }

        // target에서 self 쪽으로 수평 stopDistanceM 당긴 점. stopDistanceM ≤ 0(또는 NaN) = target 그대로.
        // self가 이미 그 거리 안이면 self의 수평 위치(높이 = target)를 돌려준다(더 다가가지 않음).
        public static Vector3 StopShort(Vector3 self, Vector3 target, float stopDistanceM)
        {
            if (!(stopDistanceM > 0f))
                return target;
            float dx = self.x - target.x;
            float dz = self.z - target.z;
            float sqr = dx * dx + dz * dz;
            if (sqr <= stopDistanceM * stopDistanceM)
                return new Vector3(self.x, target.y, self.z);
            float scale = stopDistanceM / Mathf.Sqrt(sqr);
            return new Vector3(target.x + dx * scale, target.y, target.z + dz * scale);
        }
    }
}
