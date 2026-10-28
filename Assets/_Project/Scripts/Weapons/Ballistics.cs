using UnityEngine;

namespace Ashfall.Weapons
{
    // §5.1 탄도: 초기 속도 + 중력 + 공기 저항(정조준·탄도, AB-Q3). 히트스캔이 아니다.
    public static class Ballistics
    {
        // 반암시적 오일러 한 스텝 (저항 없음 — 예전 호출 호환). 고정 dt(FixedUpdate)로 돌린다.
        public static void Step(ref Vector3 position, ref Vector3 velocity, Vector3 gravity, float dt) =>
            Step(ref position, ref velocity, gravity, 0f, dt);

        // 한 스텝. k = AmmoDefinitionSO.dragPerM (1/m), 가속도 a = g - k·|v|·v.
        // k ≤ 0: 예전과 똑같은 반암시적 오일러(v += g·dt, p += v·dt) — 저항 없는 탄도는 이전 동작과 비트 단위로 같다.
        // k > 0: 저항은 이차 저항의 1차원 해 v/(1 + k|v|dt)로 줄이고(속도 방향 유지), 위치는 스텝 앞뒤 속도의 평균으로 옮긴다.
        //   dt 0.02(FixedUpdate)에서 반암시적 오일러는 200m 낙차가 정밀해(RK4) 대비 약 3% 어긋났다(AdsBallisticsTests 실측) — 이 방식은 0.1% 안팎.
        //   비용은 제곱근 1번 + 나눗셈 1번. 플레이어·AI 탄이 모두 이 함수로 움직인다(ProjectileSystem).
        // 영점 해법(BallisticZero)도 같은 함수·같은 dt로 적분해 게임 탄도와 어긋나지 않는다.
        public static void Step(ref Vector3 position, ref Vector3 velocity, Vector3 gravity, float dragPerM, float dt)
        {
            if (dragPerM <= 0f)
            {
                velocity += gravity * dt;
                position += velocity * dt;
                return;
            }
            Vector3 before = velocity;
            velocity = velocity / (1f + dragPerM * velocity.magnitude * dt) + gravity * dt;
            position += (before + velocity) * (0.5f * dt);
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
