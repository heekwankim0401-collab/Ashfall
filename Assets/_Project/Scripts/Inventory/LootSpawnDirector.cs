using UnityEngine;

namespace Ashfall.Inventory
{
    // 레이드 루트 지점 지휘자 (docs/design/MapDensity_design.md §5.3, ⓪ 계약 껍데기).
    // ③에서: OnEnable에 RaidStarted 구독 → LootActivationPlanner로 계획 → 각 LootContainer를 활성(테이블·내용물 시드) 또는 빈 컨테이너로.
    // 씬 최상위 1개, 정적 인스턴스 없음. 지휘자가 없는 씬은 LootContainer 직렬화 값 그대로(지금 동작).
    // 지금(⓪)은 설정 참조만 가진다 — 동작은 ③ 범위라 씬에도 아직 두지 않는다.
    [DisallowMultipleComponent]
    public sealed class LootSpawnDirector : MonoBehaviour
    {
        [SerializeField] private LootSpawnConfigSO config;

        public LootSpawnConfigSO Config => config;
    }
}
