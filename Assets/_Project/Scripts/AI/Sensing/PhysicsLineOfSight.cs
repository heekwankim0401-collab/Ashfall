using UnityEngine;

namespace Ashfall.AI
{
    // ILineOfSight 물리 구현 (M3 설계 §1.5, §1.7). RaycastNonAlloc + 고정 버퍼 8, 루트 비교. 할당 없음.
    // 새 레이어를 만들지 않으므로(D11) 모든 레이어를 보고 트리거는 무시한다.
    // 레이 원점이 콜라이더 안이면 PhysX가 그 콜라이더를 보고하지 않는다(자기 캡슐 안의 눈).
    // 맵 밀도(나무 1,500): 가장 가까운 충돌을 먼저 보고(Physics.Raycast), 그것이 무시 대상일 때만 전체 수집(버퍼 8)으로 넘어간다.
    // TODO(GDD-?): 그 전체 수집에서 9개 이상 맞으면 버퍼 밖 충돌은 놓친다(가장 가까운 것이 자기 몸인 드문 경우만).
    public sealed class PhysicsLineOfSight : ILineOfSight
    {
        private const int BufferSize = 8;
        private readonly RaycastHit[] buffer = new RaycastHit[BufferSize];
        private readonly int layerMask;

        public PhysicsLineOfSight(int layerMask = Physics.DefaultRaycastLayers)
        {
            this.layerMask = layerMask;
        }

        public bool CanSee(Vector3 from, Vector3 to, Transform targetRoot, Transform ignoreRoot)
        {
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < 1e-4f)
                return true;

            Vector3 direction = delta / distance;
            // 가장 가까운 충돌을 먼저 본다(맵 밀도 리뷰 M2): 무시 대상이 아니면 그것이 답이다 — 버퍼 8개 넘는 충돌(숲)에서 가까운 가림막을 놓치지 않는다.
            // 가장 가까운 것이 무시 대상(자기 몸)일 때만 아래 전체 수집으로 그 뒤 첫 충돌을 찾는다.
            if (!Physics.Raycast(from, direction, out RaycastHit first, distance, layerMask, QueryTriggerInteraction.Ignore))
                return true;
            Transform firstRoot = first.collider.transform.root;
            if (ignoreRoot == null || firstRoot != ignoreRoot)
                return targetRoot != null && firstRoot == targetRoot;

            int count = Physics.RaycastNonAlloc(from, direction, buffer, distance, layerMask, QueryTriggerInteraction.Ignore);
            int nearest = -1;
            float nearestDistance = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                Transform root = buffer[i].collider.transform.root;
                if (ignoreRoot != null && root == ignoreRoot)
                    continue;
                if (buffer[i].distance < nearestDistance)
                {
                    nearestDistance = buffer[i].distance;
                    nearest = i;
                }
            }
            // 아무것도 안 맞음 = 보임 (플레이어 눈이 캡슐 꼭대기보다 살짝 높은 경우 포함)
            if (nearest < 0)
                return true;
            return targetRoot != null && buffer[nearest].collider.transform.root == targetRoot;
        }

        public bool IsBlocked(Vector3 from, Vector3 to, Transform ignoreA, Transform ignoreB)
        {
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < 1e-4f)
                return false;

            Vector3 direction = delta / distance;
            // 맵 밀도(나무 1,500): 긴 레이가 콜라이더를 많이 지나 모든 충돌 수집이 비싸졌다 → 가장 가까운 충돌을 먼저 본다.
            // 그것이 무시 대상이 아니면 막힘(결과 같음), 무시 대상이거나 없으면 아래 전체 검사(이전 동작)로 확정한다.
            if (!Physics.Raycast(from, direction, out RaycastHit first, distance, layerMask, QueryTriggerInteraction.Ignore))
                return false;
            Transform firstRoot = first.collider.transform.root;
            if ((ignoreA == null || firstRoot != ignoreA) && (ignoreB == null || firstRoot != ignoreB))
                return true;

            int count = Physics.RaycastNonAlloc(from, direction, buffer, distance, layerMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Transform root = buffer[i].collider.transform.root;
                if ((ignoreA != null && root == ignoreA) || (ignoreB != null && root == ignoreB))
                    continue;
                return true;
            }
            return false;
        }
    }
}
