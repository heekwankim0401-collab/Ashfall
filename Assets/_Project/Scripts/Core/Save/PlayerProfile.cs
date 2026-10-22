using System;
using UnityEngine;

namespace Ashfall.Core
{
    // 새 프로필 기본값 (HideoutConfigSO 값 — Core는 Hideout 타입을 모르므로 값만 받는다).
    public readonly struct ProfileDefaults
    {
        public readonly Vector2Int StashSize;     // HideoutConfigSO.stashSize
        public readonly int StartingCasings;      // HideoutConfigSO.startingCasings

        public ProfileDefaults(Vector2Int stashSize, int startingCasings)
        {
            StashSize = stashSize;
            StartingCasings = startingCasings;
        }
    }

    // 씬을 넘는 플레이어 진행 상태 (§9 "진행도 = 보유 아이템", M5 설계 §1.1). GameManager.Profiles가 하나를 가진다.
    public sealed class PlayerProfile
    {
        private Loadout loadout = Loadout.Empty;
        private int casings;

        // stashSize의 각 축은 최소 1. casings 음수 → 0.
        public PlayerProfile(Vector2Int stashSize, int casings)
        {
            var size = new Vector2Int(Math.Max(1, stashSize.x), Math.Max(1, stashSize.y));
            // GridRef(GridKind.Loot) — 은신처에서 인벤토리의 열린 외부 그리드 자리로 연다 (TODO(GDD-?) Q28)
            Stash = new ItemGrid(new GridRef(GridKind.Loot), size);
            Casings = casings;
        }

        // §9 보관함 (10×30 고정, 업그레이드 없음 — TODO(GDD-?) Q1)
        public ItemGrid Stash { get; }

        // 은신처 장비 (다음 반입, Q3). null 대입 → Empty
        public Loadout Loadout
        {
            get => loadout;
            set => loadout = value ?? Loadout.Empty;
        }

        // §9 탄피 (프로필 정수, Q8). 음수 대입 → 0
        public int Casings
        {
            get => casings;
            set => casings = Math.Max(0, value);
        }

        // 부랑자 키트 마지막 수령 (UTC). null = 받은 적 없음 (Q13·Q14)
        public DateTime? LastKitClaimUtc { get; set; }

        // §13.3 raid_in_progress. ProfileService·SaveSerializer만 바꾼다
        public bool RaidInProgress { get; internal set; }
    }
}
