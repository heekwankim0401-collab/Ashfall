using UnityEngine;

namespace Ashfall.Raid
{
    // 레이드 수치 (§14 RaidConfigSO + M4 설계 §4.2 추가). 에셋: Data/Raid/RaidConfig.asset (M4 2단계 데이터 메뉴가 만든다).
    [CreateAssetMenu(menuName = "Ashfall/Raid Config", fileName = "RaidConfig")]
    public class RaidConfigSO : ScriptableObject
    {
        [Tooltip("RULE-02 레이드 제한 시간 (초). 초과 = MIA")]
        public float timeLimitSec = 1500f;

        [Tooltip("§8.2 탈출 지점 EXT-01~04")]
        public ExtractionPointConfig[] extractions;

        [Tooltip("스폰별 활성 탈출구. TODO(GDD-?) Q2·Q3")]
        public SpawnRule[] spawnRules;

        [Tooltip("spawnRules에 스폰이 없을 때 활성 탈출구 수 (§8.2 2~3, 포함 범위)")]
        public Vector2Int randomActiveCount = new Vector2Int(2, 3);

        [Tooltip("항상 활성 (§8.2 EXT-01 \"항상 열림\" 해석). TODO(GDD-?) Q3")]
        public string[] alwaysActiveIds = { "EXT-01" };

        [Tooltip("레이드 종료 오버레이 시간 (실시간 초, 세계 정지). TODO(GDD-?) Q15")]
        public float endDelaySec = 3f;

        [Tooltip("탈출 구역 높이 허용 (m). TODO(GDD-?) Q8")]
        public float zoneHeightToleranceM = 3f;

        [Tooltip("지도 화면에 그릴 월드 XZ 범위 (§8.1 1km×1km, 원점 = 맵 중앙). TODO(GDD-?) Q11")]
        public Rect mapAreaXZ = new Rect(-500f, -500f, 1000f, 1000f);
    }
}
