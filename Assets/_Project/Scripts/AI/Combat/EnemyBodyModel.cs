using System;

namespace Ashfall.AI
{
    // §7 적 3부위 체력 = §4.2 공식 축약 (M3 설계 §1.3, D5).
    // - 머리 0 또는 몸통 0 → 사망 (몸통 = 흉부 역할)
    // - 다리 0 → 이동 ×legsDestroyedSpeedMultiplier, 질주 불가
    // - 파괴된 부위가 받은 피해 → destroyedPartRedistribution(70%)를 몸통으로
    // - 0 아래 초과분은 버림. 상태이상·복부 배율 없음 (§7 단순화, TODO(GDD-?))
    public sealed class EnemyBodyModel
    {
        private const int RegionCount = 3;

        private readonly AiConfigSO config;
        private readonly float[] hp = new float[RegionCount];
        private readonly float[] maxHp = new float[RegionCount];

        public event Action<EnemyHitRegion> Died;

        public EnemyBodyModel(EnemyArchetypeSO archetype, AiConfigSO config)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            if (archetype == null)
                throw new ArgumentNullException(nameof(archetype));
            maxHp[(int)EnemyHitRegion.Head] = archetype.headHp;
            maxHp[(int)EnemyHitRegion.Body] = archetype.hp;
            maxHp[(int)EnemyHitRegion.Legs] = archetype.legsHp;
            for (int i = 0; i < RegionCount; i++)
                hp[i] = maxHp[i];
        }

        public float GetHp(EnemyHitRegion region) => hp[(int)region];
        public float GetMaxHp(EnemyHitRegion region) => maxHp[(int)region];
        public bool IsDestroyed(EnemyHitRegion region) => hp[(int)region] <= 0f;
        public bool IsDead { get; private set; }
        public EnemyHitRegion? DeathCause { get; private set; }

        public float MoveSpeedMultiplier => IsDestroyed(EnemyHitRegion.Legs) ? config.legsDestroyedSpeedMultiplier : 1f;
        public bool SprintAllowed => !IsDestroyed(EnemyHitRegion.Legs);

        public void ApplyHit(EnemyHitRegion region, float damage)
        {
            if (IsDead || damage <= 0f)
                return;

            if (IsDestroyed(region) && region != EnemyHitRegion.Body)
            {
                // 파괴 부위(다리) 추가 피해 → 몸통으로 분산 (§4.2). 머리는 파괴 = 사망이라 여기 오지 않는다.
                region = EnemyHitRegion.Body;
                damage *= config.destroyedPartRedistribution;
            }

            int index = (int)region;
            hp[index] = Math.Max(0f, hp[index] - damage);

            if (hp[index] <= 0f && (region == EnemyHitRegion.Head || region == EnemyHitRegion.Body))
            {
                IsDead = true;
                DeathCause = region;
                Died?.Invoke(region);
            }
        }
    }
}
