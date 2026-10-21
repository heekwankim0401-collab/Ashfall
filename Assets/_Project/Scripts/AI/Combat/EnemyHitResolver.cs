namespace Ashfall.AI
{
    // §7 적 3부위 판정 (M3 설계 §1.3, D4). 단일 캡슐의 피격 높이 비율로 머리/몸통/다리를 정한다.
    // 경계 비율은 AiConfigSO (TODO(GDD-?)). M2 BodyPartResolver(Player)는 쓰지 않는다.
    public static class EnemyHitResolver
    {
        public static EnemyHitRegion Resolve(float localY, float capsuleHeight, AiConfigSO config)
        {
            float ratio = capsuleHeight > 0f ? localY / capsuleHeight : 0f;
            if (ratio >= config.headFrom)
                return EnemyHitRegion.Head;
            if (ratio >= config.bodyFrom)
                return EnemyHitRegion.Body;
            return EnemyHitRegion.Legs;
        }
    }
}
