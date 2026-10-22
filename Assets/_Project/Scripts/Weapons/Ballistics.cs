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

        // M7 산탄 (TODO(GDD-?) Q8): 펠릿마다 묶음 방향 둘레 pelletSpreadDeg 원뿔로 따로 흩뜨린다(조준 중에도).
        // buffer 앞쪽 min(count, buffer.Length)칸을 채우고 그 수를 돌려준다. 할당 없음.
        public static int PelletDirections(Vector3 bundle, float pelletSpreadDeg, int count, System.Random random, Vector3[] buffer)
        {
            int n = Mathf.Min(Mathf.Max(0, count), buffer.Length);
            for (int i = 0; i < n; i++)
                buffer[i] = ApplySpread(bundle, pelletSpreadDeg, random);
            return n;
        }

        // 한 발: 묶음 방향 = 지향 퍼짐(조준하면 0, RULE-07) 1회 → 펠릿마다 pelletSpreadDeg. 묶음 방향을 돌려준다.
        // 단발 탄(count 1, pelletSpreadDeg 0)은 예전과 같은 난수 순서·결과다.
        public static Vector3 ShotDirections(Vector3 forward, float hipSpreadDeg, float aimProgress, float pelletSpreadDeg,
            int count, System.Random random, Vector3[] buffer)
        {
            Vector3 bundle = ApplySpread(forward, Mathf.Lerp(hipSpreadDeg, 0f, aimProgress), random);
            PelletDirections(bundle, pelletSpreadDeg, count, random, buffer);
            return bundle;
        }

        // 발당 반동: 위로 recoilV, 좌우로 ±recoilH 사이 무작위.
        public static Vector2 RecoilKick(float recoilV, float recoilH, System.Random random)
        {
            float yaw = ((float)random.NextDouble() * 2f - 1f) * recoilH;
            return new Vector2(recoilV, yaw);
        }
    }
}
