namespace Ashfall.Audio
{
    // 지면 레이캐스트 결과 하나 (SurfaceProbe가 재사용 배열에 채운다).
    public struct SurfaceHit
    {
        public float Distance;
        public bool HasTag;          // GroundSurface가 있음
        public SurfaceKind Tag;
        public bool IsTerrain;       // TerrainCollider
        public bool IsTrigger;       // 트리거 콜라이더 (태그 없으면 지면이 아니다 — 탈출 구역·상호작용 트리거 등)
    }

    // 지면 재질 판정 규칙 (M6 Q11, 설계 §1.4). 순수, 할당 없음.
    // 우선순위: 가장 가까운 GroundSurface > 지형(terrain) > 그 밖 콜라이더(untagged).
    // 태그 없는 트리거는 건너뛴다. 맞은 것이 없으면 untagged.
    // 태그가 지형보다 우선이라 지형과 살짝 겹친(지형 아래로 묻힌) 물웅덩이 트리거도 물로 판정된다.
    public static class SurfaceResolver
    {
        public static SurfaceKind Pick(SurfaceHit[] hits, int count, SurfaceKind terrain, SurfaceKind untagged)
        {
            if (hits == null || count <= 0)
                return untagged;
            if (count > hits.Length)
                count = hits.Length;

            bool anyTag = false, anyTerrain = false;
            float bestTagDistance = float.PositiveInfinity;
            SurfaceKind bestTag = untagged;
            for (int i = 0; i < count; i++)
            {
                SurfaceHit h = hits[i];
                if (h.HasTag)
                {
                    if (!anyTag || h.Distance < bestTagDistance)
                    {
                        anyTag = true;
                        bestTagDistance = h.Distance;
                        bestTag = h.Tag;
                    }
                    continue;
                }
                if (h.IsTrigger)
                    continue;
                if (h.IsTerrain)
                    anyTerrain = true;
            }
            if (anyTag)
                return bestTag;
            return anyTerrain ? terrain : untagged;
        }
    }
}
