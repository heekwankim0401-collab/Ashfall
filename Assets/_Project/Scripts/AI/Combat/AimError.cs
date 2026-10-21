using UnityEngine;

namespace Ashfall.AI
{
    // 적 명중 오차 (M3 설계 §1.4). 사격선에 수직인 원판(반지름 r) 안 균일 점을 조준점에 더한다.
    // r = (base + perMeter × 거리) × 정착배율 × 이동배율 + burstGrowth × 점사 내 발 순번
    // 리드(이동 표적 예측) 없음. 수치는 전부 EnemyArchetypeSO (TODO(GDD-?)). 할당 없음.
    public static class AimError
    {
        public static float Radius(EnemyArchetypeSO a, float distanceM, float timeOnTargetSec,
                                   bool selfMoving, bool targetMoving, int shotIndexInBurst)
        {
            // 정착배율: 표적을 연속으로 본 시간 0 → aimSettleSec에 걸쳐 unsettled → 1
            float unsettled = a.unsettledErrorMultiplier > 0f ? a.unsettledErrorMultiplier : 1f;
            float settle = a.aimSettleSec > 0f
                ? Mathf.Lerp(unsettled, 1f, Mathf.Clamp01(timeOnTargetSec / a.aimSettleSec))
                : 1f;

            float move = 1f;
            if (selfMoving && a.selfMovingErrorMultiplier > 0f)
                move *= a.selfMovingErrorMultiplier;
            if (targetMoving && a.targetMovingErrorMultiplier > 0f)
                move *= a.targetMovingErrorMultiplier;

            float r = (a.aimErrorBaseM + a.aimErrorPerMeterM * Mathf.Max(0f, distanceM)) * settle * move
                      + a.burstErrorGrowthM * Mathf.Max(0, shotIndexInBurst);
            return Mathf.Max(0f, r);
        }

        // 사격선(muzzle→aimPoint)에 수직인 반지름 radiusM 원판 안 균일 점 → 방향(정규화)
        public static Vector3 Direction(Vector3 muzzle, Vector3 aimPoint, float radiusM, System.Random random)
        {
            Vector3 line = aimPoint - muzzle;
            if (line.sqrMagnitude < 1e-8f)
                return Vector3.forward;
            Vector3 forward = line.normalized;
            if (radiusM <= 0f || random == null)
                return forward;

            Vector3 right = Vector3.Cross(Vector3.up, forward);
            if (right.sqrMagnitude < 1e-6f)
                right = Vector3.Cross(Vector3.forward, forward);
            right.Normalize();
            Vector3 up = Vector3.Cross(forward, right);

            // 원판 균일 분포: 반지름은 sqrt(u)
            double angle = random.NextDouble() * 2.0 * System.Math.PI;
            float rr = radiusM * (float)System.Math.Sqrt(random.NextDouble());
            Vector3 offset = right * (rr * (float)System.Math.Cos(angle)) + up * (rr * (float)System.Math.Sin(angle));
            return (aimPoint + offset - muzzle).normalized;
        }
    }
}
