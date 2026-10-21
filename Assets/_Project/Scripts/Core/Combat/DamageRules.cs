namespace Ashfall.Core
{
    // 투사체 피해 값 규칙 (§5.2 무기별 피해, M3 결정). 플레이어·적이 같은 규칙을 쓰도록 한 곳에 둔다.
    public static class DamageRules
    {
        // 무기 피해가 0보다 크면 무기 값, 아니면 탄약 값(무기 없이 탄약만으로 쏘는 터렛·테스트용 기본값).
        public static float ProjectileDamage(WeaponDefinitionSO weapon, AmmoDefinitionSO ammo) =>
            weapon != null && weapon.damage > 0f ? weapon.damage : (ammo != null ? ammo.damage : 0f);
    }
}
