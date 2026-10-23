using System;
using Ashfall.Core;

namespace Ashfall.Hideout
{
    // SYS-HUB 보관함 업그레이드 (§9 "업그레이드로 확장", MVP 마무리 설계 §1.3, MF-Q5~Q9). 순수 C#.
    // - 단계 k → k+1: HideoutConfigSO.stashUpgrades[k] (너비 고정, 줄 수·탄피 비용, TODO(GDD-?) MF-Q5 임시값).
    // - 탄피를 먼저 확인하고, 확장이 성공할 때만 차감한다(실패하면 변화 없음).
    // - 그리드는 제자리에서 키운다(PlayerProfile.TryUpgradeStash → ItemGrid.TryGrow) — 아이템 자리·참조 그대로.
    // - 떠나는 중·읽기 전용 판단은 호출자(HideoutService). 읽기 전용 프로필도 허용(MF-Q8 — 상인과 같음, 저장만 안 된다).
    public sealed class StashUpgradeModel
    {
        private readonly PlayerProfile profile;
        private readonly HideoutConfigSO config;

        public StashUpgradeModel(PlayerProfile profile, HideoutConfigSO config)
        {
            this.profile = profile ?? throw new ArgumentNullException(nameof(profile));
            this.config = config != null ? config : throw new ArgumentNullException(nameof(config));
        }

        public int Level => profile.StashLevel;

        // 다음 단계 (최대면 HasNext = false)
        public StashUpgradeOffer Next =>
            config.TryGetStashUpgrade(profile.StashLevel, out UnityEngine.Vector2Int size, out int cost)
                ? new StashUpgradeOffer(size, cost)
                : StashUpgradeOffer.None;

        // 최대 → MaxLevel, 탄피 부족 → NotEnoughCasings, 확장 실패(설정과 그리드 크기가 어긋남) → Unavailable. 모두 변화 없음.
        public StashUpgradeResult TryUpgrade()
        {
            StashUpgradeOffer next = Next;
            if (!next.HasNext)
                return StashUpgradeResult.MaxLevel;
            if (profile.Casings < next.Cost)
                return StashUpgradeResult.NotEnoughCasings;
            if (!profile.TryUpgradeStash(next.NewSize))
                return StashUpgradeResult.Unavailable;
            profile.Casings -= next.Cost;
            return StashUpgradeResult.Ok;
        }
    }
}
