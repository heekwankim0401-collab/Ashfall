namespace Ashfall.Core
{
    // 낱탄(탄 상자)을 한 발씩 꺼내는 곳 — 샷건 튜브 장전 (M7 설계 §1.3, TODO(GDD-?) Q5).
    // IMagazineSource와 별개 인터페이스(기존 구현·테스트 가짜를 깨지 않는다).
    // 구현: Inventory PlayerInventory(조끼 → 주머니 → 배낭, 탄이 가장 적은 상자부터), Core LocalMagazineSource.
    public interface ILooseAmmoSource
    {
        bool HasLooseRound(AmmoType ammo);    // 그 탄을 1발 이상 든 탄 상자가 있나
        bool TakeLooseRound(AmmoType ammo);   // 한 발 꺼냄 (탄이 가장 적은 상자부터). 없으면 false. 빈 상자는 그대로 남는다 (Q6)
    }
}
