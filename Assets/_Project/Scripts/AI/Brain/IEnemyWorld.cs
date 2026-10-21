using UnityEngine;

namespace Ashfall.AI
{
    // EnemyBrain이 쓰는 월드 질의 (엄폐·NavMesh 점). 런타임 구현은 EnemyWorld, 테스트는 가짜 (M3 설계 §5.1).
    public interface IEnemyWorld
    {
        CoverResult TryFindCover(in CoverQuery query, out CoverSpot spot);   // 성공 시 query.AgentId로 예약
        void ReleaseCover(int agentId);
        bool IsBlocked(Vector3 from, Vector3 to, int ignoreAgentId);         // 엄폐 무효화 등 (표적 루트는 무시)
        bool TrySampleNavPoint(Vector3 around, float radiusM, out Vector3 point);
        float Now { get; }
    }
}
