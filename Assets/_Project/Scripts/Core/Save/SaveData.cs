using System;
using System.Collections.Generic;

namespace Ashfall.Core
{
    // save.json 최상위 (M5 설계 §2.2, version 1). JsonUtility DTO — 필드 이름 = JSON 키.
    // §13.3의 raid_in_progress는 키 raidInProgress로 쓴다(의미 같음).
    [Serializable]
    public sealed class SaveData
    {
        public int version;
        public string savedAtUtc;          // ISO 8601 "o", 정보용 (읽을 때 무시)
        public bool raidInProgress;        // §13.3 / RULE-03
        public int casings;                // §9 탄피
        public long kitLastClaimUtcTicks;  // 부랑자 키트 마지막 수령 UTC Ticks, 0 = 받은 적 없음
        public List<ItemRecord> stash = new List<ItemRecord>();
        public List<ItemRecord> loadout = new List<ItemRecord>();   // Loadout.Entries 순서
        public int[] quickSlots;           // 길이 4, loadout 인덱스, -1 = 빈 칸
    }

    // 아이템 하나 = 레코드 하나 (M5 설계 §2.2). 무기의 장착 탄창은 무기 레코드 안 필드.
    [Serializable]
    public sealed class ItemRecord
    {
        public const string StashContainer = "Stash";
        public const string EquippedContainer = "Equipped";

        public string id;
        public string container;   // "Stash" | "Equipped" | "Pocket" | "TacticalVest" | "Backpack"
        public string slot;        // EquipSlot 이름 (Equipped일 때)
        public int index;          // 주머니 번호
        public int x, y;           // 그리드 왼쪽 위 칸
        public bool rotated;
        public int rounds = -1;    // Magazine 탄 수, 그 밖 -1
        public int usesLeft = -1;  // MedicalItem 남은 횟수, 그 밖 -1
        public string loadedMagazineId;
        public int loadedMagazineRounds;
    }
}
