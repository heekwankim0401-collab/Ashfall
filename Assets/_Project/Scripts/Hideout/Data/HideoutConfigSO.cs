using Ashfall.Core;
using UnityEngine;

namespace Ashfall.Hideout
{
    // SYS-HUB 은신처 설정 (§9, M5 설계 §3.1·§3.3). §14 스키마에 없는 추가. 에셋 Data/Hideout/HideoutConfig.asset.
    [CreateAssetMenu(menuName = "Ashfall/Hideout Config", fileName = "HideoutConfig")]
    public class HideoutConfigSO : ScriptableObject
    {
        [Header("§9 보관함")]
        [Tooltip("보관함 그리드 (칸, W×H). §9 10×30. 업그레이드 없음 (TODO(GDD-?) Q1). " +
                 "줄이면 범위 밖 아이템은 다음 로드에서 자동 배치 → 안 되면 버린다 (M5 설계 §2.4). 각 축 최소 1")]
        public Vector2Int stashSize = new Vector2Int(10, 30);

        [Header("§9 탄피")]
        [Tooltip("새 프로필(세이브 없음·손상)의 시작 탄피. 0 이상. TODO(GDD-?) Q9")]
        [Min(0)]
        public int startingCasings = 6000;

        [Header("§9 부랑자 키트")]
        [Tooltip("수령 후 다시 받을 때까지 (실제 시간 UTC 초, Q14). §9 20분 = 1200. 0 = 쿨다운 없음")]
        [Min(0f)]
        public float vagrantKitCooldownSec = 1200f;
        [Tooltip("지급 아이템 (같은 것은 여러 번, null 칸은 무시). §9 녹슨 권총 + 탄창 2개 + 붕대. " +
                 "보관함에 전부 들어갈 때만 지급 (Q15). 탄창은 가득, 무기는 탄창 없이 (Q17)")]
        public ItemDefinitionSO[] vagrantKitItems;

        // ProfileService.EnsureLoaded에 넘기는 새 프로필 기본값
        public ProfileDefaults ToProfileDefaults() => new ProfileDefaults(stashSize, startingCasings);

        private void OnValidate()
        {
            stashSize = new Vector2Int(Mathf.Max(1, stashSize.x), Mathf.Max(1, stashSize.y));
            startingCasings = Mathf.Max(0, startingCasings);
            vagrantKitCooldownSec = Mathf.Max(0f, vagrantKitCooldownSec);
        }
    }
}
