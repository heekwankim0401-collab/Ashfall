namespace Ashfall.Core
{
    // 재장전할 탄창을 꺼내고 빼낸 탄창을 회수하는 곳 (§5.1).
    // M2: Inventory의 PlayerInventory(조끼 + 주머니)가 구현해 등록한다. 인벤토리 없는 씬·적 예비 탄창은 Core의 LocalMagazineSource.
    public interface IMagazineSource
    {
        bool HasLoadable(AmmoType ammo);              // 탄이 1발 이상 든 맞는 탄창이 있나
        Magazine TakeFullest(AmmoType ammo);          // 가장 많이 든 것을 꺼냄. 없으면 null
        bool TryStore(Magazine magazine);             // 일반 재장전 회수. 공간 없으면 false
    }
}
