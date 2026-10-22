using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Ashfall.Core
{
    // JSON ↔ SaveData ↔ PlayerProfile (M5 설계 §2.1~§2.4). JsonUtility, 평평한 레코드, enum은 이름 문자열.
    // 예외를 밖으로 던지지 않는다(파싱). RULE-03 감지(raidInProgress → 장비 삭제)는 ProfileService가 한다.
    public static class SaveSerializer
    {
        public const int CurrentVersion = 1;

        private const string PocketContainer = nameof(GridKind.Pocket);
        private const string VestContainer = nameof(GridKind.TacticalVest);
        private const string BackpackContainer = nameof(GridKind.Backpack);

        // ── 쓰기 ─────────────────────────────────────────────

        public static string ToJson(PlayerProfile profile, DateTime utcNow) =>
            JsonUtility.ToJson(ToData(profile, utcNow), true);

        public static SaveData ToData(PlayerProfile profile, DateTime utcNow)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));

            var data = new SaveData
            {
                version = CurrentVersion,
                savedAtUtc = ToUtc(utcNow).ToString("o", CultureInfo.InvariantCulture),
                raidInProgress = profile.RaidInProgress,
                casings = profile.Casings,
                kitLastClaimUtcTicks = profile.LastKitClaimUtc.HasValue ? ToUtc(profile.LastKitClaimUtc.Value).Ticks : 0L,
            };

            IReadOnlyList<PlacedItem> stashItems = profile.Stash.Items;
            for (int i = 0; i < stashItems.Count; i++)
            {
                PlacedItem placed = stashItems[i];
                ItemRecord record = ToRecord(placed.Item);
                record.container = ItemRecord.StashContainer;
                record.x = placed.Cell.x;
                record.y = placed.Cell.y;
                record.rotated = placed.Rotated;
                data.stash.Add(record);
            }

            Loadout loadout = profile.Loadout;
            IReadOnlyList<LoadoutEntry> entries = loadout.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                ItemRecord record = ToRecord(entries[i].Item);
                WriteLocation(record, entries[i].Location);
                data.loadout.Add(record);
            }

            data.quickSlots = new int[Loadout.QuickSlotCount];
            for (int q = 0; q < Loadout.QuickSlotCount; q++)
            {
                data.quickSlots[q] = -1;
                ItemInstance item = loadout.QuickSlots[q];
                if (item == null)
                    continue;
                for (int i = 0; i < entries.Count; i++)
                {
                    if (ReferenceEquals(entries[i].Item, item))
                    {
                        data.quickSlots[q] = i;
                        break;
                    }
                }
            }
            return data;
        }

        // 상태 필드만 채운다(id·탄 수·남은 횟수·장착 탄창). 자리(container·slot·index·x·y·rotated)는 호출자.
        public static ItemRecord ToRecord(ItemInstance item)
        {
            if (item == null)
                throw new ArgumentNullException(nameof(item));
            var record = new ItemRecord
            {
                id = item.Definition.id ?? string.Empty,
                container = string.Empty,
                slot = string.Empty,
                loadedMagazineId = string.Empty,
            };
            switch (item)
            {
                case Magazine magazine:
                    record.rounds = magazine.Rounds;
                    break;
                case MedicalItem medical:
                    record.usesLeft = medical.UsesLeft;
                    break;
                case WeaponItem weapon when weapon.LoadedMagazine != null:
                    record.loadedMagazineId = weapon.LoadedMagazine.Definition.id ?? string.Empty;
                    record.loadedMagazineRounds = weapon.LoadedMagazine.Rounds;
                    break;
            }
            return record;
        }

        // ── 읽기 ─────────────────────────────────────────────

        // 실패면 false + failStatus(Corrupt | UnsupportedVersion) + error. 성공이면 failStatus = Loaded.
        public static bool TryParse(string json, out SaveData data, out SaveLoadStatus failStatus, out string error)
        {
            data = null;
            error = null;
            failStatus = SaveLoadStatus.Corrupt;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "빈 세이브 파일";
                return false;
            }

            SaveData parsed;
            try
            {
                parsed = JsonUtility.FromJson<SaveData>(json);
            }
            catch (Exception e)
            {
                error = $"JSON 파싱 실패: {e.Message}";
                return false;
            }

            if (parsed == null)
            {
                error = "JSON 파싱 결과가 없다";
                return false;
            }
            if (parsed.version <= 0)
            {
                error = "version 필드가 없거나 잘못됐다";
                return false;
            }
            if (parsed.version > CurrentVersion)
            {
                failStatus = SaveLoadStatus.UnsupportedVersion;
                error = $"지원하지 않는 세이브 버전 {parsed.version} (현재 {CurrentVersion})";
                return false;
            }
            // version < CurrentVersion 변환은 지금 없다 (version 1이 처음).

            parsed.stash ??= new List<ItemRecord>();
            parsed.loadout ??= new List<ItemRecord>();
            data = parsed;
            failStatus = SaveLoadStatus.Loaded;
            return true;
        }

        // §2.4 규칙으로 프로필을 만든다. raidInProgress는 그대로 옮긴다(감지는 ProfileService).
        // - 알 수 없는 id → 그 레코드만 버림(report.UnknownItemIds). 무기의 알 수 없는 탄창 id → 탄창 없는 무기(같은 목록).
        // - 보관함 자리가 범위 밖·겹침 → 다른 아이템을 모두 놓은 뒤 자동 배치, 안 되면 버림(report.DroppedItemCount).
        // - 은신처 장비의 container·slot을 해석할 수 없음 → 보관함 자동 배치 → 안 되면 버림.
        //   (해석은 되지만 자리가 틀린 것은 Loadout에 그대로 두고 Inventory 적용 단계가 거부·보관함으로 옮긴다.)
        //   raidInProgress면 어차피 장비가 지워지므로 보관함으로 구하지 않고 report.CrashLost에 더한다.
        // - 퀵슬롯: 범위 밖·빈 칸·의약품이 아님·버려진 레코드 → 빈 칸.
        public static PlayerProfile ToProfile(SaveData data, IItemCatalog catalog, ProfileDefaults defaults, SaveLoadReport report)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));
            report ??= new SaveLoadReport();

            var profile = new PlayerProfile(defaults.StashSize, data.casings);
            profile.LastKitClaimUtc = data.kitLastClaimUtcTicks > 0 && data.kitLastClaimUtcTicks <= DateTime.MaxValue.Ticks
                ? new DateTime(data.kitLastClaimUtcTicks, DateTimeKind.Utc)
                : (DateTime?)null;
            profile.RaidInProgress = data.raidInProgress;

            // 보관함: 1차 = 기록된 자리, 2차 = 실패한 것 자동 배치
            var displaced = new List<ItemInstance>();
            if (data.stash != null)
            {
                for (int i = 0; i < data.stash.Count; i++)
                {
                    ItemRecord record = data.stash[i];
                    ItemInstance item = FromRecord(record, catalog, report);
                    if (item == null)
                        continue;
                    if (!profile.Stash.TryPlace(item, new Vector2Int(record.x, record.y), record.rotated))
                        displaced.Add(item);
                }
            }

            // 은신처 장비
            var entries = new List<LoadoutEntry>();
            int recordCount = data.loadout?.Count ?? 0;
            var recordItems = new ItemInstance[recordCount];   // loadout 인덱스 → 장비에 남은 인스턴스 (퀵슬롯용)
            for (int i = 0; i < recordCount; i++)
            {
                ItemRecord record = data.loadout[i];
                ItemInstance item = FromRecord(record, catalog, report);
                if (item == null)
                    continue;
                if (TryReadLocation(record, out ItemLocation location))
                {
                    entries.Add(new LoadoutEntry(item, location));
                    recordItems[i] = item;
                }
                else if (data.raidInProgress)
                {
                    report.CrashLost.Add(item.Definition);
                }
                else
                {
                    displaced.Add(item);
                }
            }

            for (int i = 0; i < displaced.Count; i++)
            {
                ItemInstance item = displaced[i];
                if (profile.Stash.TryFindFreeCell(item, out Vector2Int cell, out bool rotated) &&
                    profile.Stash.TryPlace(item, cell, rotated))
                    continue;
                report.DroppedItemCount++;
            }

            var quick = new ItemInstance[Loadout.QuickSlotCount];
            if (data.quickSlots != null)
            {
                for (int q = 0; q < Loadout.QuickSlotCount && q < data.quickSlots.Length; q++)
                {
                    int index = data.quickSlots[q];
                    if (index >= 0 && index < recordItems.Length && recordItems[index] is MedicalItem medical)
                        quick[q] = medical;
                }
            }
            profile.Loadout = new Loadout(entries, quick);
            return profile;
        }

        // 레코드 → 런타임 아이템. 모르는 id → null (report.UnknownItemIds). 탄 수·남은 횟수는 범위로 자른다.
        public static ItemInstance FromRecord(ItemRecord record, IItemCatalog catalog, SaveLoadReport report)
        {
            if (record == null || catalog == null)
                return null;
            if (!catalog.TryGet(record.id, out ItemDefinitionSO definition) || definition == null)
            {
                report?.UnknownItemIds.Add(record.id ?? string.Empty);
                return null;
            }

            switch (definition)
            {
                case MagazineDefinitionSO magazine:
                    return new Magazine(magazine, record.rounds);   // 생성자가 0~capacity로 자른다
                case MedicalDefinitionSO medical:
                    return new MedicalItem(medical, record.usesLeft);
                case WeaponDefinitionSO weapon:
                    return new WeaponItem(weapon, ReadLoadedMagazine(record, weapon, catalog, report));
                default:
                    return new ItemInstance(definition);
            }
        }

        // ── 자리 ─────────────────────────────────────────────

        private static void WriteLocation(ItemRecord record, ItemLocation location)
        {
            if (location.IsEquipped)
            {
                record.container = ItemRecord.EquippedContainer;
                record.slot = location.Slot.ToString();
                return;
            }
            record.container = location.Grid.Kind.ToString();
            record.index = location.Grid.Kind == GridKind.Pocket ? location.Grid.Index : 0;
            record.x = location.Cell.x;
            record.y = location.Cell.y;
            record.rotated = location.Rotated;
        }

        // Equipped(slot) 또는 Pocket[index]/TacticalVest/Backpack 칸. "Stash"·"Loot"·모르는 이름 → false.
        private static bool TryReadLocation(ItemRecord record, out ItemLocation location)
        {
            location = default;
            string container = record.container;
            if (container == ItemRecord.EquippedContainer)
            {
                if (!TryParseName(record.slot, out EquipSlot slot))
                    return false;
                location = ItemLocation.Equipped(slot);
                return true;
            }

            GridKind kind;
            if (container == PocketContainer)
                kind = GridKind.Pocket;
            else if (container == VestContainer)
                kind = GridKind.TacticalVest;
            else if (container == BackpackContainer)
                kind = GridKind.Backpack;
            else
                return false;

            var grid = new GridRef(kind, kind == GridKind.Pocket ? record.index : 0);
            location = ItemLocation.InGrid(grid, new Vector2Int(record.x, record.y), record.rotated);
            return true;
        }

        // 이름 문자열만 받는다 (숫자 문자열·대소문자 다름은 거부 — enum 순서가 바뀌어도 세이브가 깨지지 않게).
        private static bool TryParseName<T>(string name, out T value) where T : struct, Enum
        {
            value = default;
            if (string.IsNullOrEmpty(name) || !Enum.TryParse(name, false, out T parsed))
                return false;
            if (Enum.GetName(typeof(T), parsed) != name)
                return false;
            value = parsed;
            return true;
        }

        // 장착 탄창: 알 수 없는 id·탄창 아님 → 알 수 없는 id로 셈 / 탄약 종류가 무기와 다름(직접 고친 세이브) → 탄창 없는 무기 + 버림 집계 (M5 리뷰 8)
        private static Magazine ReadLoadedMagazine(ItemRecord record, WeaponDefinitionSO weapon, IItemCatalog catalog, SaveLoadReport report)
        {
            if (string.IsNullOrEmpty(record.loadedMagazineId))
                return null;
            if (catalog.TryGet(record.loadedMagazineId, out ItemDefinitionSO definition) && definition is MagazineDefinitionSO magazine)
            {
                if (magazine.ammo != weapon.ammo)
                {
                    if (report != null)
                        report.DroppedItemCount++;
                    return null;
                }
                return new Magazine(magazine, record.loadedMagazineRounds);
            }
            report?.UnknownItemIds.Add(record.loadedMagazineId);
            return null;
        }

        private static DateTime ToUtc(DateTime time) =>
            time.Kind == DateTimeKind.Local ? time.ToUniversalTime() : DateTime.SpecifyKind(time, DateTimeKind.Utc);
    }
}
