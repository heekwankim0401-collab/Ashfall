using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ashfall.Core
{
    // 새 프로필 기본값 (HideoutConfigSO 값 — Core는 Hideout 타입을 모르므로 값만 받는다).
    // MVP 마무리 F2 (§9 보관함 업그레이드, MF-Q5·Q6): 단계별 보관함 크기. [0] = 시작 크기(StashSize), [k] = 업그레이드 k단계.
    public readonly struct ProfileDefaults
    {
        public readonly Vector2Int StashSize;     // HideoutConfigSO.stashSize (단계 0)
        public readonly int StartingCasings;      // HideoutConfigSO.startingCasings
        private readonly Vector2Int[] stashSizesByLevel;   // null = 단계 0 하나 (기존 생성자·default)

        // 단계 1개(업그레이드 없음) — 기존 호출 호환
        public ProfileDefaults(Vector2Int stashSize, int startingCasings)
        {
            StashSize = stashSize;
            StartingCasings = startingCasings;
            stashSizesByLevel = null;
        }

        // stashSizesByLevel[0] = 시작 크기. null·빈 목록 → 단계 0 하나(크기 = 1×1, 각 축은 PlayerProfile이 최소 1로 자른다).
        public ProfileDefaults(IReadOnlyList<Vector2Int> stashSizesByLevel, int startingCasings)
        {
            StartingCasings = startingCasings;
            if (stashSizesByLevel == null || stashSizesByLevel.Count == 0)
            {
                StashSize = Vector2Int.one;
                this.stashSizesByLevel = null;
                return;
            }
            var copy = new Vector2Int[stashSizesByLevel.Count];
            for (int i = 0; i < copy.Length; i++)
                copy[i] = stashSizesByLevel[i];
            StashSize = copy[0];
            this.stashSizesByLevel = copy;
        }

        // 가장 높은 단계 (0 = 업그레이드 없음)
        public int MaxStashLevel => stashSizesByLevel != null ? stashSizesByLevel.Length - 1 : 0;

        // 단계의 보관함 크기. 범위 밖 단계는 0~MaxStashLevel로 자른다.
        public Vector2Int StashSizeFor(int level)
        {
            if (stashSizesByLevel == null)
                return StashSize;
            int clamped = Math.Max(0, Math.Min(level, stashSizesByLevel.Length - 1));
            return stashSizesByLevel[clamped];
        }
    }

    // 씬을 넘는 플레이어 진행 상태 (§9 "진행도 = 보유 아이템", M5 설계 §1.1). GameManager.Profiles가 하나를 가진다.
    public sealed class PlayerProfile
    {
        private Loadout loadout = Loadout.Empty;
        private int casings;

        // stashSize의 각 축은 최소 1. casings 음수 → 0. stashLevel 음수 → 0 (크기는 호출자가 단계에 맞춰 넘긴다 — ProfileDefaults.StashSizeFor).
        public PlayerProfile(Vector2Int stashSize, int casings, int stashLevel = 0)
        {
            var size = new Vector2Int(Math.Max(1, stashSize.x), Math.Max(1, stashSize.y));
            // GridRef(GridKind.Loot) — 은신처에서 인벤토리의 열린 외부 그리드 자리로 연다 (TODO(GDD-?) Q28)
            Stash = new ItemGrid(new GridRef(GridKind.Loot), size);
            Casings = casings;
            StashLevel = Math.Max(0, stashLevel);
        }

        // §9 보관함. MVP 마무리 F2: 업그레이드는 같은 그리드를 제자리에서 키운다(TryUpgradeStash) — 참조가 바뀌지 않는다.
        public ItemGrid Stash { get; }

        // §9 보관함 업그레이드 단계 (0 = 시작 10×30, 세이브 v3 stashLevel, MF-Q6)
        public int StashLevel { get; private set; }

        // 보관함을 newSize로 키우고 단계 +1. 줄이기·같은 크기 → false(변화 없음). 탄피 차감·단계 상한은 호출자(StashUpgradeModel).
        public bool TryUpgradeStash(Vector2Int newSize)
        {
            if (!Stash.TryGrow(newSize))
                return false;
            StashLevel++;
            return true;
        }

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
