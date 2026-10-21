using UnityEngine;

namespace Ashfall.AI
{
    // 가림 판정 (M3 설계 §1.5, §1.7). 런타임은 PhysicsLineOfSight(RaycastNonAlloc), 테스트는 가짜.
    // 새 물리 레이어 없이 루트 비교 + 무시 목록으로 판정한다 (D11).
    public interface ILineOfSight
    {
        // from→to 첫 충돌이 없거나 targetRoot 소속이면 true. ignoreRoot 소속 콜라이더는 건너뛴다.
        bool CanSee(Vector3 from, Vector3 to, Transform targetRoot, Transform ignoreRoot);
        // from→to 사이에 ignoreA·ignoreB 소속이 아닌 콜라이더가 있으면 true.
        bool IsBlocked(Vector3 from, Vector3 to, Transform ignoreA, Transform ignoreB);
    }
}
