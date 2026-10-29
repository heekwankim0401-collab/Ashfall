using UnityEngine;

namespace Ashfall.Inventory
{
    // 상자·트렁크 오브젝트에 붙는 루트 지점 표시 (docs/design/MapDensity_design.md §5.3, ⓪ 계약 — 값은 ③ 배치 메뉴가 생성 때 고정).
    // 같은 오브젝트의 LootContainer를 레이드 시작 때 LootSpawnDirector가 활성(테이블·시드) 또는 빈 컨테이너로 정한다(③).
    // slotKey는 씬 순서·인스턴스 ID와 무관한 정렬 키(오브젝트 이름) — 활성화 계획이 이 값으로 정렬해 결정적이다.
    [DisallowMultipleComponent]
    public sealed class LootSpawnSlot : MonoBehaviour
    {
        [SerializeField] private string slotKey;
        [Tooltip("POI id(POI-HWY 등) / ROUTE / OUTSKIRT — LootSpawnConfigSO.zones[].zoneId")]
        [SerializeField] private string zoneId;
        [SerializeField] private LootSlotKind kind;
        [Tooltip("항상 활성 (신호탄 상자 — EXT-03 조건, M4 Q7). 비율 계산에서 빠진다")]
        [SerializeField] private bool alwaysActive;

        public string SlotKey => slotKey;
        public string ZoneId => zoneId;
        public LootSlotKind Kind => kind;
        public bool AlwaysActive => alwaysActive;

        public LootActivationPlanner.Slot ToPlannerSlot() => new LootActivationPlanner.Slot(slotKey, zoneId, kind, alwaysActive);
    }
}
