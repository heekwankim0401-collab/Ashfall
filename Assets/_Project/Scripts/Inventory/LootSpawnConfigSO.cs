using Ashfall.Core;
using UnityEngine;

namespace Ashfall.Inventory
{
    // 비활성 루트 지점 처리 (docs/design/MapDensity_design.md §5.3·§13 Q1)
    public enum InactiveLootMode
    {
        Empty,      // 빈 컨테이너 (겉모습·콜라이더·프롬프트 그대로, 열면 비어 있다) — 기본안
        JunkOnly,   // inactiveTable 굴림 (고철 0~1개)
    }

    // 구역 하나의 루트 지점 설정 (§5.1). zoneId = POI id / ROUTE / OUTSKIRT.
    [System.Serializable]
    public struct LootZoneConfig
    {
        public string zoneId;
        [Tooltip("배치 생성 목표 상자 수(기존 포함) — 바꾸면 Rebuild 메뉴")]
        public int crateTargetCount;
        [Tooltip("배치 생성 목표 트렁크 수 — 바꾸면 Rebuild 메뉴")]
        public int trunkTargetCount;
        [Range(0f, 1f)] public float crateActiveRatio;
        [Range(0f, 1f)] public float trunkActiveRatio;
        public LootTableSO crateTable;
        public LootTableSO trunkTable;

        public float RatioOf(LootSlotKind kind) => kind == LootSlotKind.Trunk ? trunkActiveRatio : crateActiveRatio;
        public LootTableSO TableOf(LootSlotKind kind) => kind == LootSlotKind.Trunk ? trunkTable : crateTable;
    }

    // 루트 지점 약 200곳의 구역별 목표·활성 비율·테이블 (docs/design/MapDensity_design.md §5.1, ⓪ 계약 — 에셋·값은 ③ data-designer).
    // 에셋: Data/Inventory/LootSpawnConfig.asset(③). 수치는 전부 TODO(GDD-?) 임시값(경제 시뮬레이션으로 확정).
    [CreateAssetMenu(menuName = "Ashfall/Loot Spawn Config", fileName = "LootSpawnConfig")]
    public class LootSpawnConfigSO : ScriptableObject
    {
        public LootZoneConfig[] zones = new LootZoneConfig[0];

        [Tooltip("구역·종류별 최소 활성 수 (지점이 1개 이상일 때)")]
        public int minActivePerZone = 1;

        public InactiveLootMode inactiveMode = InactiveLootMode.Empty;
        [Tooltip("inactiveMode = JunkOnly일 때 굴리는 테이블")]
        public LootTableSO inactiveTable;

        [Tooltip("상자 그리드 (M4 상자와 같다)")]
        public Vector2Int crateGridSize = new Vector2Int(6, 4);
        [Tooltip("트렁크 그리드. TODO(GDD-?)")]
        public Vector2Int trunkGridSize = new Vector2Int(5, 3);
        public string crateDisplayName = "보급 상자";
        public string trunkDisplayName = "차량 트렁크";

        // zoneId의 설정. 없으면 false.
        public bool TryGetZone(string zoneId, out LootZoneConfig zone)
        {
            if (zones != null)
                foreach (LootZoneConfig z in zones)
                    if (z.zoneId == zoneId)
                    {
                        zone = z;
                        return true;
                    }
            zone = default;
            return false;
        }

        // LootActivationPlanner.Plan에 넘기는 비율 조회
        public float? RatioOf(string zoneId, LootSlotKind kind) => TryGetZone(zoneId, out LootZoneConfig z) ? z.RatioOf(kind) : (float?)null;
    }
}
