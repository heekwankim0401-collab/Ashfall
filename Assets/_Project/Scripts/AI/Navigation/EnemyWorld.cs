using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Ashfall.AI
{
    // IEnemyWorld 런타임 구현 (M3 설계 §5.1): CoverIndex + CoverSelector + ILineOfSight + NavMesh.SamplePosition.
    // 엄폐 질의는 프레임당 maxCoverQueriesPerFrame개까지, 초과는 Deferred(다음 틱 재시도). 할당 없음(등록 제외).
    public sealed class EnemyWorld : IEnemyWorld
    {
        private readonly AiConfigSO config;
        private readonly CoverIndex covers;
        private readonly ILineOfSight los;
        private readonly List<Transform> agentRoots = new List<Transform>(32);
        private int[] scratchIds;
        private float[] scratchScores;
        private int coverQueriesThisFrame;
        private Transform targetRoot;

        public EnemyWorld(AiConfigSO config, CoverIndex covers, ILineOfSight los)
        {
            this.config = config;
            this.covers = covers;
            this.los = los;
            int k = Mathf.Max(1, config.coverLosCandidates);
            scratchIds = new int[k];
            scratchScores = new float[k];
        }

        public float Now => Time.time;
        public int CoverQueriesThisFrame => coverQueriesThisFrame;

        // AiDirector가 프레임 시작에 부른다.
        public void BeginFrame() => coverQueriesThisFrame = 0;

        public void SetTarget(Transform root) => targetRoot = root;

        public void SetAgentRoot(int agentId, Transform root)
        {
            while (agentRoots.Count <= agentId)
                agentRoots.Add(null);
            agentRoots[agentId] = root;
        }

        public CoverResult TryFindCover(in CoverQuery query, out CoverSpot spot)
        {
            spot = default;
            if (coverQueriesThisFrame >= config.maxCoverQueriesPerFrame)
                return CoverResult.Deferred;
            coverQueriesThisFrame++;

            if (scratchIds.Length < config.coverLosCandidates)
            {
                scratchIds = new int[config.coverLosCandidates];
                scratchScores = new float[config.coverLosCandidates];
            }
            return CoverSelector.TrySelect(covers, query, config, los, RootOf(query.AgentId), targetRoot,
                scratchIds, scratchScores, out spot)
                ? CoverResult.Found
                : CoverResult.None;
        }

        public void ReleaseCover(int agentId) => covers.ReleaseAll(agentId);

        public bool IsBlocked(Vector3 from, Vector3 to, int ignoreAgentId) =>
            los.IsBlocked(from, to, RootOf(ignoreAgentId), targetRoot);

        public bool TrySampleNavPoint(Vector3 around, float radiusM, out Vector3 point)
        {
            if (NavMesh.SamplePosition(around, out NavMeshHit hit, radiusM, NavMesh.AllAreas))
            {
                point = hit.position;
                return true;
            }
            point = around;
            return false;
        }

        private Transform RootOf(int agentId) =>
            agentId >= 0 && agentId < agentRoots.Count ? agentRoots[agentId] : null;
    }
}
