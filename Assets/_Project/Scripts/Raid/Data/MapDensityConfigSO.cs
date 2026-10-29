using UnityEngine;

namespace Ashfall.Raid
{
    // 맵 밀도 수치 (docs/design/MapDensity_design.md §5.1·§8.1, GDD §8.1·§10.2·§14). 에셋: Data/Raid/MapDensityConfig.asset
    // (Ashfall/Setup/Apply Map Density가 없을 때만 만든다 — 기획 조정 보존).
    // 지금은 에디터 생성기(EditorTools MapDensity*)만 읽는다. 폭풍 컬링(설계 §8.3)을 넣으면 런타임도 읽는다.
    // 거리는 전부 실제 거리(m) — 생성기가 LODGroup 화면 비율로 바꾼다(플레이어 카메라 FOV·품질 단계 lodBias 반영).
    // 필드 초기값 = 설계값, 전부 TODO(GDD-?) 임시값("MVP 뒤 폴리시 기본값(플레이 검증 전)").
    [CreateAssetMenu(menuName = "Ashfall/Map Density Config", fileName = "MapDensityConfig")]
    public class MapDensityConfigSO : ScriptableObject
    {
        [Header("죽은 나무 (바꾸면 Rebuild 메뉴)")]
        public int treeCount = 1500;
        [Tooltip("나무끼리 최소 간격 (m, 포아송 디스크)")]
        public float treeMinSpacingM = 5f;
        [Tooltip("군락 마스크 Perlin 파장 (m)")]
        public float treeGroveNoiseScaleM = 90f;
        [Tooltip("이 값 이상인 노이즈 자리가 '죽은 숲' 군락")]
        [Range(0f, 1f)] public float treeGroveThreshold = 0.45f;
        [Tooltip("군락 밖(흩어진 나무) 비율")]
        [Range(0f, 1f)] public float treeScatterShare = 0.25f;
        [Tooltip("크기 배율 범위 (최소, 최대)")]
        public Vector2 treeScaleRange = new Vector2(0.8f, 1.3f);
        public float treeMaxSlopeDeg = 30f;

        [Header("덤불·통나무 (바꾸면 Rebuild 메뉴)")]
        public int bushCount = 400;
        public int logCount = 120;
        [Tooltip("덤불 키 상한 (m) — 적 시야 레이를 막지 않으므로 플레이어만 가리는 불공정을 피한다(§8.4)")]
        public float bushMaxHeightM = 1.0f;

        [Header("풀 (Terrain Detail, Refresh 메뉴로 다시 맞춤)")]
        [Tooltip("풀이 덮는 걷는 지형 비율")]
        [Range(0f, 1f)] public float grassCoverage = 0.4f;
        [Tooltip("최대 밀도 (포기/m²)")]
        public float grassMaxPerM2 = 1.2f;
        [Tooltip("보이는 거리 (m) = Terrain.detailObjectDistance")]
        public float grassDistanceM = 50f;
        [Tooltip("키 범위 (m) — 적 시야와 무관(§8.4)이라 0.8m 이하")]
        public Vector2 grassHeightRange = new Vector2(0.3f, 0.8f);
        [Tooltip("디테일 해상도 (지형 한 변 셀 수)")]
        public int detailResolution = 1024;
        public int detailResolutionPerPatch = 32;
        [Tooltip("풀 노이즈 파장 (m)")]
        public float grassNoiseScaleM = 40f;

        [Header("컬링 거리 (m, Refresh 메뉴로 다시 맞춤)")]
        [Tooltip("나무 LOD0 → LOD1")]
        public float treeLod1DistanceM = 60f;
        public float treeCullDistanceM = 260f;
        public float bushCullDistanceM = 90f;
        public float logCullDistanceM = 120f;
        public float smallStructureCullDistanceM = 180f;
        public float carCullDistanceM = 300f;
        public float buildingCullDistanceM = 450f;
        public float crateCullDistanceM = 120f;

        [Header("그림자")]
        [Tooltip("이보다 낮은 새 산포물·구조물은 그림자를 드리우지 않는다 (m)")]
        public float shadowMinHeightM = 1.5f;
        [Tooltip("나무 LOD1이 그림자를 드리우는가")]
        public bool treeLod1Shadows = false;

        [Header("엄폐 지점 상한 (확인용 — CoverSelector 선형 탐색, 설계 §7.4)")]
        public int maxNewCoverPoints = 700;
        public int maxTotalCoverPoints = 1200;
        [Tooltip("새 엄폐 지점끼리·기존 지점과 이보다 가까우면 뺀다 (m) — 질의 비용·중복 줄이기")]
        public float newCoverMinSpacingM = 4f;
    }
}
