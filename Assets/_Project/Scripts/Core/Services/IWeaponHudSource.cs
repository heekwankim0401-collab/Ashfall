namespace Ashfall.Core
{
    // 상시 HUD(B안, 2026-10-28 — RULE-04 개정)가 읽는 현재 무기. Weapons(WeaponController)가 OnEnable에 등록, OnDisable에 해제한다.
    // 탄 수·용량·내구도는 Core WeaponItem(LoadedMagazine·Durability)에서 읽는다. 예비 탄창은 UI가 IInventoryService로 센다.
    public interface IWeaponHudSource
    {
        WeaponItem CurrentWeapon { get; }        // 손에 든 무기. 교체 중이면 새로 든 무기(교체 시간 동안에도 표시). 비무장 = null
        FireMode CurrentFireMode { get; }
    }
}
