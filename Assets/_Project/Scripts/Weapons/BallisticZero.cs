using UnityEngine;

namespace Ashfall.Weapons
{
    // 영점 해법 (정조준·탄도 AB-Q2, docs/design/AdsBallistics_design.md §3).
    // 시선 좌표계 = 카메라(눈) 기준: 시선 = +z 축, 위 = +y. 수평으로 정조준했다고 보고 중력은 -y.
    // 총구(origin)를 떠난 탄이 시선 위 영점 거리 점 (0, 0, zeroRange)를 지나는 발사 방향을 구한다 — 실제 총의 가늠자·총열 각과 같다
    // (영점은 총에 고정, 위·아래로 쏘면 실제처럼 조금 달라진다).
    // 게임 탄도와 같은 Ballistics.Step·같은 dt로 적분하고, 구간 사이는 선형 보간한다(ProjectileSystem 구간 레이캐스트와 같은 판정).
    // 공기 저항은 속도 방향으로만 작용해 수평 진행 방향이 바뀌지 않으므로 좌우는 직선으로 정하고, 높이만 할선법으로 맞춘다.
    // 할당 없음. 장착 시 1회 부르고 결과를 캐시한다(WeaponController).
    public static class BallisticZero
    {
        public const int MaxIterations = 24;
        // 높이 오차 허용치 (m). 이보다 작으면 멈춘다 — 수치 해법의 수렴 판정이지 게임 수치가 아니다.
        public const float ToleranceM = 1e-5f;

        // 성공하면 true와 단위 방향. 영점까지 maxTime 안에 못 가면(속도·비행 시간 부족) false — 호출자는 시선과 평행하게 쏜다.
        public static bool TrySolve(Vector3 origin, float zeroRangeM, float speed, float gravity, float dragPerM,
            float dt, float maxTime, out Vector3 direction, out int iterations)
        {
            direction = Vector3.forward;
            iterations = 0;
            if (zeroRangeM <= origin.z || speed <= 0f || dt <= 0f || maxTime <= 0f)
                return false;

            // 수평: 총구 → (0, zeroRange) 직선
            var heading = new Vector2(-origin.x, zeroRangeM - origin.z);
            float horizontal = heading.magnitude;
            heading /= horizontal;

            // 첫 추정 = 영점 점을 똑바로 겨눈 각, 둘째 = 그 각에서의 낙차만큼 올린 각
            float pitch0 = Mathf.Atan2(-origin.y, horizontal);
            if (!HeightAtRange(origin, heading, pitch0, zeroRangeM, speed, gravity, dragPerM, dt, maxTime, out float f0))
                return false;
            float pitch1 = pitch0 - f0 / horizontal;
            // 높이 < 0인 각(lo)과 > 0인 각(hi)을 둘 다 보았으면, 할선 추정이 그 구간 밖으로 튀거나 기울기가 0일 때 이분한다(보호).
            bool hasLo = false, hasHi = false;
            float lo = 0f, hi = 0f;
            Track(pitch0, f0, ref hasLo, ref hasHi, ref lo, ref hi);
            for (int i = 1; i <= MaxIterations; i++)
            {
                if (!HeightAtRange(origin, heading, pitch1, zeroRangeM, speed, gravity, dragPerM, dt, maxTime, out float f1))
                    return false;
                iterations = i;
                if (Mathf.Abs(f1) <= ToleranceM)
                {
                    direction = Direction(heading, pitch1);
                    return true;
                }
                Track(pitch1, f1, ref hasLo, ref hasHi, ref lo, ref hi);
                float slope = (f1 - f0) / (pitch1 - pitch0);
                float next = Mathf.Abs(slope) > 1e-9f ? pitch1 - f1 / slope : float.NaN;
                bool outside = float.IsNaN(next) || next <= Mathf.Min(lo, hi) || next >= Mathf.Max(lo, hi);
                if (hasLo && hasHi && outside)
                    next = (lo + hi) * 0.5f;
                else if (float.IsNaN(next))
                    return false;
                pitch0 = pitch1;
                f0 = f1;
                pitch1 = next;
            }
            return false;   // MaxIterations 안에 수렴 못 함
        }

        private static void Track(float pitch, float height, ref bool hasLo, ref bool hasHi, ref float lo, ref float hi)
        {
            if (height < 0f)
            {
                lo = pitch;
                hasLo = true;
            }
            else
            {
                hi = pitch;
                hasHi = true;
            }
        }

        // 발사 방향 → 시선 위 z = range 평면을 지날 때의 높이 (y). 도달 못 하면 false.
        public static bool HeightAtRange(Vector3 origin, Vector2 heading, float pitch, float range, float speed, float gravity,
            float dragPerM, float dt, float maxTime, out float height)
        {
            Vector3 position = origin;
            Vector3 velocity = Direction(heading, pitch) * speed;
            var g = new Vector3(0f, -gravity, 0f);
            for (float age = 0f; age < maxTime; age += dt)
            {
                Vector3 from = position;
                Ballistics.Step(ref position, ref velocity, g, dragPerM, dt);
                if (position.z >= range)
                {
                    float t = (range - from.z) / (position.z - from.z);
                    height = from.y + (position.y - from.y) * t;
                    return true;
                }
            }
            height = 0f;
            return false;
        }

        private static Vector3 Direction(Vector2 heading, float pitch)
        {
            float c = Mathf.Cos(pitch);
            return new Vector3(heading.x * c, Mathf.Sin(pitch), heading.y * c);
        }
    }
}
