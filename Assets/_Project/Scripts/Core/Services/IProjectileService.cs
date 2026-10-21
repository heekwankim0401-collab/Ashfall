using UnityEngine;

namespace Ashfall.Core
{
    // 탄도 투사체 발사 (§5.1). Weapons의 ProjectileSystem이 구현해 등록한다. AI가 쓴다 (M3 설계 D1).
    public interface IProjectileService
    {
        // damage = DamageRules.ProjectileDamage(weapon, ammo). shooterRoot의 콜라이더는 맞지 않는다.
        void Fire(Vector3 origin, Vector3 direction, AmmoDefinitionSO ammo, float damage, Transform shooterRoot);
    }
}
