namespace Ashfall.Core
{
    // §6 희귀도 5단계: 고철 / 일반 / 우수 / 희귀 / 유물
    public enum Rarity
    {
        Scrap,
        Common,
        Uncommon,
        Rare,
        Relic,
    }

    // §5.2 분류
    public enum WeaponClass
    {
        Pistol,
        Smg,
        AssaultRifle,
        Shotgun,
        Sniper,
    }

    // §5.2 탄
    public enum AmmoType
    {
        Pistol9mm,
        Rifle556,
        Rifle762,
        Gauge12,
    }

    // TODO(GDD-?): §5에 무기별 사격 모드와 모드 전환 키가 없다. M1은 WPN-RIF-01 자동만 쓴다.
    public enum FireMode
    {
        Semi,
        Auto,
    }
}
