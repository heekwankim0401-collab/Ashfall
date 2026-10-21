using UnityEngine;

namespace Ashfall.Weapons
{
    // §5.1 탄도: 초기 속도 + 중력. 히트스캔이 아니다.
    public static class Ballistics
    {
        // 반암시적 오일러 한 스텝. 고정 dt(FixedUpdate)로 돌린다.
        public static void Step(ref Vector3 position, ref Vector3 velocity, Vector3 gravity, float dt)
        {
            velocity += gravity * dt;
            position += velocity * dt;
        }

        // 원뿔 반각 안에서 무작위로 방향을 흩뜨린다. spreadDeg = 0이면 그대로.
        public static Vector3 ApplySpread(Vector3 forward, float spreadDeg, System.Random random)
        {
            if (spreadDeg <= 0f)
                return forward.normalized;

            float angle = spreadDeg * Mathf.Sqrt((float)random.NextDouble());
            float roll = (float)random.NextDouble() * 360f;
            Quaternion toForward = Quaternion.LookRotation(forward);
            return toForward * (Quaternion.Euler(0f, 0f, roll) * Quaternion.Euler(angle, 0f, 0f) * Vector3.forward);
        }

        // 발당 반동: 위로 recoilV, 좌우로 ±recoilH 사이 무작위.
        public static Vector2 RecoilKick(float recoilV, float recoilH, System.Random random)
        {
            float yaw = ((float)random.NextDouble() * 2f - 1f) * recoilH;
            return new Vector2(recoilV, yaw);
        }
    }
}
