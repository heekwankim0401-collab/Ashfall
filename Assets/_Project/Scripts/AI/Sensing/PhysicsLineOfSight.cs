using UnityEngine;

namespace Ashfall.AI
{
    // ILineOfSight 물리 구현 (M3 설계 §1.5, §1.7). RaycastNonAlloc + 고정 버퍼 8, 루트 비교. 할당 없음.
    // 새 레이어를 만들지 않으므로(D11) 모든 레이어를 보고 트리거는 무시한다.
    // 레이 원점이 콜라이더 안이면 PhysX가 그 콜라이더를 보고하지 않는다(자기 캡슐 안의 눈).
    // TODO(GDD-?): 한 레이에 9개 이상 맞으면 버퍼 밖 충돌은 놓친다(그레이박스 규모에서는 충분).
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

            int count = Physics.RaycastNonAlloc(from, delta / distance, buffer, distance, layerMask, QueryTriggerInteraction.Ignore);
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

            int count = Physics.RaycastNonAlloc(from, delta / distance, buffer, distance, layerMask, QueryTriggerInteraction.Ignore);
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
