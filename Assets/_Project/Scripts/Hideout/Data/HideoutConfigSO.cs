using System;
using System.Collections.Generic;
using Ashfall.Core;
using UnityEngine;

namespace Ashfall.Hideout
{
    // MVP 마무리 F2 보관함 업그레이드 한 단계 (§9, MF-Q5). 너비는 stashSize.x 고정, 줄 수만 늘린다.
    [Serializable]
    public struct StashUpgradeStep
    {
        [Tooltip("이 단계의 보관함 줄 수 (H). 이전 단계보다 커야 한다 — 아니면 이 단계부터 무시")]
        [Min(1)]
        public int rows;
        [Tooltip("이 단계로 올리는 탄피 비용. 0 이상")]
        [Min(0)]
        public int costCasings;

        public StashUpgradeStep(int rows, int costCasings)
        {
            this.rows = rows;
            this.costCasings = costCasings;
        }
    }

    // SYS-HUB 은신처 설정 (§9, M5 설계 §3.1·§3.3). §14 스키마에 없는 추가. 에셋 Data/Hideout/HideoutConfig.asset.
    [CreateAssetMenu(menuName = "Ashfall/Hideout Config", fileName = "HideoutConfig")]
    public class HideoutConfigSO : ScriptableObject
    {
        [Header("§9 보관함")]
        [Tooltip("보관함 시작 그리드 (칸, W×H) = 업그레이드 단계 0. §9 10×30. " +
                 "줄이면 범위 밖 아이템은 다음 로드에서 자동 배치 → 안 되면 버린다 (M5 설계 §2.4). 각 축 최소 1")]
        public Vector2Int stashSize = new Vector2Int(10, 30);

        // TODO(GDD-?) MF-Q5: GDD §9는 "업그레이드로 확장"만 — 단계 수·줄 수·비용은 임시값 (10×40 5000, 10×50 12000 탄피)
        [Tooltip("§9 보관함 업그레이드 단계 (MVP 마무리 F2). [k] = 단계 k+1. 너비 = stashSize.x 고정, rows는 단조 증가. " +
                 "TODO(GDD-?) MF-Q5 임시값")]
        public StashUpgradeStep[] stashUpgrades =
        {
            new StashUpgradeStep(40, 5000),
            new StashUpgradeStep(50, 12000),
        };

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

        // 시작 크기(각 축 최소 1)
        public Vector2Int BaseStashSize => new Vector2Int(Mathf.Max(1, stashSize.x), Mathf.Max(1, stashSize.y));

        // 쓸 수 있는 업그레이드 단계 수. 줄 수가 이전 단계보다 크지 않은 단계에서 멈춘다(그 뒤는 무시 — 설정 오류에도 TryGrow가 실패하지 않게).
        public int ValidStashUpgradeCount
        {
            get
            {
                if (stashUpgrades == null)
                    return 0;
                int prevRows = BaseStashSize.y;
                for (int i = 0; i < stashUpgrades.Length; i++)
                {
                    if (stashUpgrades[i].rows <= prevRows)
                        return i;
                    prevRows = stashUpgrades[i].rows;
                }
                return stashUpgrades.Length;
            }
        }

        // 단계별 보관함 크기 [0 = 시작, 1.. = 업그레이드]
        public List<Vector2Int> StashSizesByLevel()
        {
            Vector2Int baseSize = BaseStashSize;
            int count = ValidStashUpgradeCount;
            var sizes = new List<Vector2Int>(count + 1) { baseSize };
            for (int i = 0; i < count; i++)
                sizes.Add(new Vector2Int(baseSize.x, stashUpgrades[i].rows));
            return sizes;
        }

        // fromLevel → fromLevel+1 업그레이드. 없으면(최대·음수) false
        public bool TryGetStashUpgrade(int fromLevel, out Vector2Int newSize, out int cost)
        {
            newSize = default;
            cost = 0;
            if (fromLevel < 0 || fromLevel >= ValidStashUpgradeCount)
                return false;
            newSize = new Vector2Int(BaseStashSize.x, stashUpgrades[fromLevel].rows);
            cost = Mathf.Max(0, stashUpgrades[fromLevel].costCasings);
            return true;
        }

        // ProfileService.EnsureLoaded에 넘기는 새 프로필 기본값 (단계별 보관함 크기 포함 — 세이브 stashLevel을 이 크기로 읽는다)
        public ProfileDefaults ToProfileDefaults() => new ProfileDefaults(StashSizesByLevel(), startingCasings);

        private void OnValidate()
        {
            stashSize = new Vector2Int(Mathf.Max(1, stashSize.x), Mathf.Max(1, stashSize.y));
            startingCasings = Mathf.Max(0, startingCasings);
            vagrantKitCooldownSec = Mathf.Max(0f, vagrantKitCooldownSec);
            if (stashUpgrades != null)
            {
                // 줄 수 단조 증가(이전 + 1 이상)·비용 0 이상
                int prevRows = stashSize.y;
                for (int i = 0; i < stashUpgrades.Length; i++)
                {
                    StashUpgradeStep step = stashUpgrades[i];
                    step.rows = Mathf.Max(prevRows + 1, step.rows);
                    step.costCasings = Mathf.Max(0, step.costCasings);
                    stashUpgrades[i] = step;
                    prevRows = step.rows;
                }
            }
        }
    }
}
