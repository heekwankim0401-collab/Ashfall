namespace Ashfall.Inventory
{
    // 루트 지점 종류 (docs/design/MapDensity_design.md §5.3, ⓪ 계약). 구역별 활성 비율·테이블이 종류마다 따로다.
    public enum LootSlotKind
    {
        Crate,   // 보급 상자 (POI = LT-CRATE, 길목·외곽 = LT-CRATE-LOW)
        Trunk,   // 승용차 트렁크 (LT-TRUNK, GDD §6)
    }
}
