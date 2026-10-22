using System;
using System.Collections.Generic;
using Ashfall.Core;
using Ashfall.Hideout;
using Ashfall.Inventory;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // M5 테스트 도우미 (M5 설계 §6 1단계). TestData.cs·M3TestData.cs·M4TestData.cs는 고치지 않는다(M4TestData FlowRecorder 제외).
    // 세이브는 메모리(MemorySaveStorage) 또는 OS 임시 폴더만 쓴다 — 실제 persistentDataPath의 save.json을 건드리지 않는다(§2.6).
    internal static class M5TestData
    {
        public const int StartingCasings = 6000;
        public static readonly Vector2Int StashSize = new Vector2Int(10, 30);
        public static readonly DateTime Epoch = new DateTime(2026, 10, 22, 9, 30, 0, DateTimeKind.Utc);

        // HideoutConfigSO 기본값과 같은 새 프로필 기본값 (10×30, 6000)
        public static ProfileDefaults Defaults() => new ProfileDefaults(StashSize, StartingCasings);

        // §9 부랑자 키트 (녹슨 권총 + 권총 탄창 ×2 + 붕대), 20분
        public static HideoutConfigSO HideoutConfig(FakeCatalog catalog)
        {
            var c = ScriptableObject.CreateInstance<HideoutConfigSO>();
            c.stashSize = StashSize;
            c.startingCasings = StartingCasings;
            c.vagrantKitCooldownSec = 1200f;
            c.vagrantKitItems = new[]
            {
                catalog.Get("WPN-PST-01"), catalog.Get("MAG-9MM-12"), catalog.Get("MAG-9MM-12"), catalog.Get("MED-BND-01"),
            };
            return c;
        }

        // §4.6 판매 목록(✔), 배율 1.0 / 0.4
        public static TraderConfigSO TraderConfig(FakeCatalog catalog)
        {
            var t = ScriptableObject.CreateInstance<TraderConfigSO>();
            t.id = "TRD-01";
            t.displayName = "고물상";
            t.buyPriceMultiplier = 1f;
            t.sellPriceMultiplier = 0.4f;
            t.catalog = new[]
            {
                catalog.Get("WPN-RIF-01"), catalog.Get("MAG-556-30"), catalog.Get("ITM-MAP-01"),
                catalog.Get("MED-BND-01"), catalog.Get("MED-TRQ-01"), catalog.Get("MED-SPL-01"),
                catalog.Get("RIG-01"), catalog.Get("BAG-01"), catalog.Get("ARM-HLM-01"), catalog.Get("ARM-BDY-01"),
            };
            return t;
        }

        // 메모리 저장소 + 가짜 시계로 첫 로드까지 끝낸 ProfileService. json null → 파일 없음(새 프로필).
        public static ProfileService LoadedProfiles(string json = null, MemorySaveStorage storage = null,
            FakeCatalog catalog = null, FakeClock clock = null)
        {
            storage ??= new MemorySaveStorage();
            if (json != null)
                storage.Text = json;
            catalog ??= FakeCatalog.Standard();
            clock ??= new FakeClock();
            var profiles = new ProfileService(storage, clock.Now);
            profiles.EnsureLoaded(catalog, Defaults());
            return profiles;
        }

        // ── 아이템·장비 ───────────────────────────────────────

        public static WeaponItem Rifle(FakeCatalog catalog, int loadedRounds = -1)
        {
            var mag = (MagazineDefinitionSO)catalog.Get("MAG-556-30");
            Magazine loaded = loadedRounds < 0 ? null : new Magazine(mag, loadedRounds);
            return new WeaponItem((WeaponDefinitionSO)catalog.Get("WPN-RIF-01"), loaded);
        }

        public static Magazine Mag556(FakeCatalog catalog, int rounds) =>
            new Magazine((MagazineDefinitionSO)catalog.Get("MAG-556-30"), rounds);

        public static MedicalItem Bandage(FakeCatalog catalog) =>
            new MedicalItem((MedicalDefinitionSO)catalog.Get("MED-BND-01"));

        public static ItemInstance Plain(FakeCatalog catalog, string id) => new ItemInstance(catalog.Get(id));

        // 장비가 갖춰진 인벤토리: 소총(장착 탄창 17발) Primary1, 헬멧, 조끼(5.56 탄창 30발 회전 없음·12발 회전),
        // 배낭(전선 뭉치), 주머니 2에 붕대 + 퀵슬롯 0 = 붕대. 아이템은 Items에 이름으로 담는다.
        public static PlayerInventory EquippedInventory(FakeCatalog catalog, out Dictionary<string, ItemInstance> items)
        {
            var inventory = new PlayerInventory(TestData.Inventory());
            items = new Dictionary<string, ItemInstance>();

            var rifle = Rifle(catalog, 17);
            var helmet = Plain(catalog, "ARM-HLM-01");
            var vest = Plain(catalog, "RIG-01");
            var backpack = Plain(catalog, "BAG-01");
            Require(inventory.TryEquipNew(rifle, EquipSlot.Primary1), "소총 장착");
            Require(inventory.TryEquipNew(helmet, EquipSlot.Helmet), "헬멧 장착");
            Require(inventory.TryEquipNew(vest, EquipSlot.TacticalVest), "조끼 장착");
            Require(inventory.TryEquipNew(backpack, EquipSlot.Backpack), "배낭 장착");

            var bandage = Bandage(catalog);
            Require(inventory.TryAutoStore(bandage), "붕대 보관");
            Require(inventory.TryMove(bandage, new GridRef(GridKind.Pocket, 2), Vector2Int.zero, false), "붕대 → 주머니 2");
            Require(inventory.TryAssignQuickSlot(0, bandage), "퀵슬롯 0");

            var magFull = Mag556(catalog, 30);
            var magPart = Mag556(catalog, 12);
            Require(inventory.TryAutoStore(magFull), "탄창 보관");
            Require(inventory.TryMove(magFull, new GridRef(GridKind.TacticalVest), new Vector2Int(0, 0), false), "탄창 → 조끼");
            Require(inventory.TryAutoStore(magPart), "탄창 보관");
            Require(inventory.TryMove(magPart, new GridRef(GridKind.TacticalVest), new Vector2Int(1, 0), true), "탄창 → 조끼 (회전)");

            var wires = Plain(catalog, "JNK-03");
            Require(inventory.TryAutoStore(wires), "전선 보관");
            Require(inventory.TryMove(wires, new GridRef(GridKind.Backpack), new Vector2Int(2, 3), false), "전선 → 배낭");

            items["rifle"] = rifle;
            items["helmet"] = helmet;
            items["vest"] = vest;
            items["backpack"] = backpack;
            items["bandage"] = bandage;
            items["magFull"] = magFull;
            items["magPart"] = magPart;
            items["wires"] = wires;
            return inventory;
        }

        // 위치를 직접 정한 Loadout (인벤토리 없이). 소총(탄창 17발) Primary1, 조끼, 조끼 안 5.56 탄창(회전), 주머니 1 붕대(퀵슬롯 0).
        public static Loadout SampleLoadout(FakeCatalog catalog, out Dictionary<string, ItemInstance> items)
        {
            var rifle = Rifle(catalog, 17);
            var vest = Plain(catalog, "RIG-01");
            var mag = Mag556(catalog, 30);
            var bandage = Bandage(catalog);
            items = new Dictionary<string, ItemInstance>
            {
                ["rifle"] = rifle, ["vest"] = vest, ["mag"] = mag, ["bandage"] = bandage,
            };
            var entries = new[]
            {
                new LoadoutEntry(rifle, ItemLocation.Equipped(EquipSlot.Primary1)),
                new LoadoutEntry(vest, ItemLocation.Equipped(EquipSlot.TacticalVest)),
                new LoadoutEntry(mag, ItemLocation.InGrid(new GridRef(GridKind.TacticalVest), new Vector2Int(0, 1), true)),
                new LoadoutEntry(bandage, ItemLocation.InGrid(new GridRef(GridKind.Pocket, 1), Vector2Int.zero, false)),
            };
            return new Loadout(entries, new ItemInstance[] { bandage, null, null, null });
        }

        // 열린 루트 (검색 끝난 상태가 아니어도 됨 — Capture는 루트를 보지 않는다)
        public static LootContainerModel Loot(params ItemInstance[] items) =>
            new LootContainerModel("상자", new Vector2Int(6, 6), items, 0.5f);

        // 탈출 결과 (CarriedOutLoadout 포함)
        public static RaidResult Extracted(Loadout carriedOut)
        {
            var flat = new List<ItemInstance>();
            carriedOut?.CollectItems(flat);
            return new RaidResult(RaidOutcome.Extracted, "EXT-01", "남쪽 도로", false, BodyPart.Head, 600f, 1,
                null, flat, null, carriedOut);
        }

        private static void Require(bool ok, string what)
        {
            if (!ok)
                throw new InvalidOperationException($"M5TestData 준비 실패: {what}");
        }
    }

    // 사전 기반 IItemCatalog. Standard()는 TestData·M3TestData·M4TestData 아이템을 등록하고 §4.6 basePrice를 넣는다.
    internal sealed class FakeCatalog : IItemCatalog
    {
        private readonly Dictionary<string, ItemDefinitionSO> items = new Dictionary<string, ItemDefinitionSO>();

        public int Lookups { get; private set; }

        public static FakeCatalog Standard()
        {
            var c = new FakeCatalog();
            c.Add(TestData.Rifle(), 4000);
            c.Add(M3TestData.Smg9(), 1500);
            c.Add(M3TestData.Pistol9(), 400);
            c.Add(TestData.Magazine30(), 400);
            c.Add(M3TestData.Mag9_30(), 300);
            c.Add(M3TestData.Mag9_12(), 120);
            c.Add(M4TestData.MapItem(), 100);
            c.Add(M4TestData.Flare(), 500);
            c.Add(TestData.Bandage(), 60);
            c.Add(TestData.Tourniquet(), 150);
            c.Add(TestData.Splint(), 120);
            c.Add(TestData.Vest(), 600);
            c.Add(TestData.Backpack(), 800);
            c.Add(TestData.Helmet(), 1200);
            c.Add(TestData.BodyArmor(), 2500);
            c.Add(TestData.Pipe(), 40);
            c.Add(TestData.EnginePart(), 300);
            c.Add(TestData.Wires(), 80);
            c.Add(M3TestData.Ammo9(), 0);
            c.Add(M3TestData.Ammo556(), 0);
            return c;
        }

        public void Add(ItemDefinitionSO definition, int basePrice)
        {
            definition.basePrice = basePrice;
            items[definition.id] = definition;
        }

        public ItemDefinitionSO Get(string id) =>
            items.TryGetValue(id, out ItemDefinitionSO d) ? d : throw new KeyNotFoundException($"FakeCatalog에 {id} 없음");

        public bool TryGet(string id, out ItemDefinitionSO definition)
        {
            Lookups++;
            definition = null;
            return id != null && items.TryGetValue(id, out definition);
        }
    }

    // 메모리 ISaveStorage. Text null = 파일 없음. 쓰기 실패·읽기 오류를 켤 수 있다.
    internal sealed class MemorySaveStorage : ISaveStorage
    {
        public string Text;               // 현재 "save.json" 내용
        public bool WriteFails;
        public bool ReadFails;
        public int ReadFailCount;         // 이 횟수만큼만 읽기 오류 (일시 잠금 흉내)
        public bool QuarantineFails;

        public int ReadCount { get; private set; }
        public int WriteCount { get; private set; }          // 성공한 쓰기 수
        public int WriteAttempts { get; private set; }
        public string LastWrittenText { get; private set; }
        public int QuarantineCount { get; private set; }
        public string QuarantinedText { get; private set; }

        public string Location => "memory://save.json";

        public SaveReadStatus TryRead(out string text, out string error)
        {
            ReadCount++;
            error = null;
            text = null;
            if (ReadFails || ReadFailCount > 0)
            {
                if (ReadFailCount > 0)
                    ReadFailCount--;
                error = "테스트 읽기 오류";
                return SaveReadStatus.Error;
            }
            if (Text == null)
                return SaveReadStatus.Missing;
            text = Text;
            return SaveReadStatus.Ok;
        }

        public bool TryWrite(string text, out string error)
        {
            WriteAttempts++;
            if (WriteFails)
            {
                error = "테스트 쓰기 실패";
                return false;
            }
            error = null;
            Text = text;
            LastWrittenText = text;
            WriteCount++;
            return true;
        }

        public bool TryQuarantine(out string movedTo, out string error)
        {
            movedTo = null;
            error = null;
            if (QuarantineFails || Text == null)
            {
                error = "테스트 격리 실패";
                return false;
            }
            QuarantineCount++;
            QuarantinedText = Text;
            Text = null;
            movedTo = "memory://save.corrupt.json";
            return true;
        }

        // 마지막으로 쓴 내용을 DTO로 (검사용)
        public SaveData LastWritten() => LastWrittenText == null ? null : JsonUtility.FromJson<SaveData>(LastWrittenText);
    }

    // 주입 시계 (UTC)
    internal sealed class FakeClock
    {
        public DateTime UtcNow = M5TestData.Epoch;

        public Func<DateTime> Now => () => UtcNow;

        public void Advance(double seconds) => UtcNow = UtcNow.AddSeconds(seconds);
    }
}
