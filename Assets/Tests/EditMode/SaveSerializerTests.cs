using System;
using System.Collections.Generic;
using Ashfall.Core;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // §13.3 세이브 형식 (M5 설계 §2.1~§2.4): 왕복, 고정 스키마, 손상·버전·알 수 없는 id·자리 규칙
    public class SaveSerializerTests
    {
        private FakeCatalog catalog;
        private SaveLoadReport report;

        [SetUp]
        public void SetUp()
        {
            catalog = FakeCatalog.Standard();
            report = new SaveLoadReport();
        }

        private PlayerProfile Load(string json)
        {
            Assert.IsTrue(SaveSerializer.TryParse(json, out SaveData data, out SaveLoadStatus status, out string error), error);
            Assert.AreEqual(SaveLoadStatus.Loaded, status);
            return SaveSerializer.ToProfile(data, catalog, M5TestData.Defaults(), report);
        }

        private PlayerProfile RoundTrip(PlayerProfile profile) => Load(SaveSerializer.ToJson(profile, M5TestData.Epoch));

        private static PlacedItem StashAt(PlayerProfile profile, int index) => profile.Stash.Items[index];

        // ── 왕복 ─────────────────────────────────────────────

        [Test]
        public void RoundTrip_ScalarFields()
        {
            var profile = new PlayerProfile(M5TestData.StashSize, 5480)
            {
                LastKitClaimUtc = M5TestData.Epoch.AddMinutes(-3),
            };

            PlayerProfile loaded = RoundTrip(profile);

            Assert.AreEqual(5480, loaded.Casings);
            Assert.AreEqual(M5TestData.Epoch.AddMinutes(-3), loaded.LastKitClaimUtc);
            Assert.AreEqual(DateTimeKind.Utc, loaded.LastKitClaimUtc.Value.Kind);
            Assert.IsFalse(loaded.RaidInProgress);
            Assert.AreEqual(M5TestData.StashSize, loaded.Stash.Size);
            Assert.IsTrue(loaded.Loadout.IsEmpty);
            Assert.IsFalse(report.HasNotice);
        }

        [Test]
        public void RoundTrip_KitNeverClaimed_IsNull()
        {
            var profile = new PlayerProfile(M5TestData.StashSize, 0);
            string json = SaveSerializer.ToJson(profile, M5TestData.Epoch);
            StringAssert.Contains("\"kitLastClaimUtcTicks\": 0", json);
            Assert.IsNull(RoundTrip(profile).LastKitClaimUtc);
        }

        [Test]
        public void RoundTrip_StashItems_StateAndPlacement()
        {
            var profile = new PlayerProfile(M5TestData.StashSize, 0);
            var mag = M5TestData.Mag556(catalog, 5);
            var rifle = M5TestData.Rifle(catalog, 9);
            var emptyRifle = M5TestData.Rifle(catalog);
            var medDef = TestData.Medical("MED-TEST-3", "3회 약", Vector2Int.one, 0.1f, 1f, StatusEffect.Bleed, uses: 3);
            catalog.Add(medDef, 10);
            var med = new MedicalItem(medDef, 2);
            var pipe = M5TestData.Plain(catalog, "JNK-01");
            Assert.IsTrue(profile.Stash.TryPlace(mag, new Vector2Int(9, 0), false));
            Assert.IsTrue(profile.Stash.TryPlace(rifle, new Vector2Int(0, 0), false));
            Assert.IsTrue(profile.Stash.TryPlace(emptyRifle, new Vector2Int(0, 2), true));
            Assert.IsTrue(profile.Stash.TryPlace(med, new Vector2Int(5, 5), false));
            Assert.IsTrue(profile.Stash.TryPlace(pipe, new Vector2Int(3, 27), false));

            PlayerProfile loaded = RoundTrip(profile);

            Assert.AreEqual(5, loaded.Stash.Items.Count);
            var lMag = (Magazine)StashAt(loaded, 0).Item;
            Assert.AreEqual(5, lMag.Rounds);
            Assert.AreEqual(new Vector2Int(9, 0), StashAt(loaded, 0).Cell);

            var lRifle = (WeaponItem)StashAt(loaded, 1).Item;
            Assert.AreSame(catalog.Get("WPN-RIF-01"), lRifle.Definition);
            Assert.IsNotNull(lRifle.LoadedMagazine);
            Assert.AreEqual(9, lRifle.LoadedMagazine.Rounds);
            Assert.AreSame(catalog.Get("MAG-556-30"), lRifle.LoadedMagazine.Definition);

            var lEmpty = (WeaponItem)StashAt(loaded, 2).Item;
            Assert.IsNull(lEmpty.LoadedMagazine);
            Assert.IsTrue(StashAt(loaded, 2).Rotated);
            Assert.AreEqual(new Vector2Int(0, 2), StashAt(loaded, 2).Cell);

            Assert.AreEqual(2, ((MedicalItem)StashAt(loaded, 3).Item).UsesLeft);
            Assert.AreEqual(typeof(ItemInstance), StashAt(loaded, 4).Item.GetType());
            Assert.AreEqual(new Vector2Int(3, 27), StashAt(loaded, 4).Cell);
            Assert.IsFalse(report.HasNotice);
        }

        [Test]
        public void RoundTrip_Loadout_LocationsRotationQuickSlots()
        {
            var profile = new PlayerProfile(M5TestData.StashSize, 0);
            profile.Loadout = M5TestData.SampleLoadout(catalog, out _);

            Loadout loaded = RoundTrip(profile).Loadout;

            Assert.AreEqual(4, loaded.Entries.Count);
            LoadoutEntry rifle = loaded.Entries[0];
            Assert.IsTrue(rifle.Location.IsEquipped);
            Assert.AreEqual(EquipSlot.Primary1, rifle.Location.Slot);
            Assert.AreEqual(17, ((WeaponItem)rifle.Item).LoadedMagazine.Rounds);

            Assert.AreEqual(EquipSlot.TacticalVest, loaded.Entries[1].Location.Slot);

            LoadoutEntry mag = loaded.Entries[2];
            Assert.IsFalse(mag.Location.IsEquipped);
            Assert.AreEqual(new GridRef(GridKind.TacticalVest), mag.Location.Grid);
            Assert.AreEqual(new Vector2Int(0, 1), mag.Location.Cell);
            Assert.IsTrue(mag.Location.Rotated);
            Assert.AreEqual(30, ((Magazine)mag.Item).Rounds);

            LoadoutEntry bandage = loaded.Entries[3];
            Assert.AreEqual(new GridRef(GridKind.Pocket, 1), bandage.Location.Grid);
            Assert.AreSame(bandage.Item, loaded.QuickSlots[0], "퀵슬롯은 같은 인스턴스를 가리킨다");
            Assert.IsNull(loaded.QuickSlots[1]);
        }

        [Test]
        public void RoundTrip_FromInventoryCapture()
        {
            var inventory = M5TestData.EquippedInventory(catalog, out _);
            var profile = new PlayerProfile(M5TestData.StashSize, 0) { Loadout = Loadout.Capture(inventory) };

            Loadout loaded = RoundTrip(profile).Loadout;

            Assert.AreEqual(profile.Loadout.Entries.Count, loaded.Entries.Count);
            for (int i = 0; i < loaded.Entries.Count; i++)
            {
                LoadoutEntry a = profile.Loadout.Entries[i];
                LoadoutEntry b = loaded.Entries[i];
                Assert.AreSame(a.Item.Definition, b.Item.Definition, $"#{i}");
                Assert.AreEqual(a.Location.IsEquipped, b.Location.IsEquipped, $"#{i}");
                Assert.AreEqual(a.Location.Slot, b.Location.Slot, $"#{i}");
                Assert.AreEqual(a.Location.Grid, b.Location.Grid, $"#{i}");
                Assert.AreEqual(a.Location.Cell, b.Location.Cell, $"#{i}");
                Assert.AreEqual(a.Location.Rotated, b.Location.Rotated, $"#{i}");
            }
            Assert.IsNotNull(loaded.QuickSlots[0]);
        }

        [Test]
        public void ToJson_WritesSchemaKeys()
        {
            var profile = new PlayerProfile(M5TestData.StashSize, 7);
            profile.Loadout = M5TestData.SampleLoadout(catalog, out _);
            string json = SaveSerializer.ToJson(profile, M5TestData.Epoch);

            StringAssert.Contains("\"version\": 1", json);
            StringAssert.Contains("\"raidInProgress\": false", json);
            StringAssert.Contains("\"casings\": 7", json);
            StringAssert.Contains("\"savedAtUtc\": \"2026-10-22T09:30:00.0000000Z\"", json);
            StringAssert.Contains("\"container\": \"Equipped\"", json);
            StringAssert.Contains("\"slot\": \"Primary1\"", json);
            StringAssert.Contains("\"container\": \"TacticalVest\"", json);
            StringAssert.Contains("\"container\": \"Pocket\"", json);
            StringAssert.Contains("\"loadedMagazineId\": \"MAG-556-30\"", json);

            SaveData data = JsonUtility.FromJson<SaveData>(json);
            CollectionAssert.AreEqual(new[] { 3, -1, -1, -1 }, data.quickSlots, "퀵슬롯 = loadout 인덱스");
        }

        // 설계 §2.2 예시 그대로 — 스키마 안정성 (키 이름이 바뀌면 깨진다)
        [Test]
        public void Parse_DesignExampleJson()
        {
            const string json = @"{
    ""version"": 1,
    ""savedAtUtc"": ""2026-10-22T09:30:00.0000000Z"",
    ""raidInProgress"": false,
    ""casings"": 5480,
    ""kitLastClaimUtcTicks"": 638651718000000000,
    ""stash"": [
        { ""id"": ""WPN-PST-01"", ""container"": ""Stash"", ""slot"": """", ""index"": 0, ""x"": 0, ""y"": 0, ""rotated"": false,
          ""rounds"": -1, ""usesLeft"": -1, ""loadedMagazineId"": """", ""loadedMagazineRounds"": 0 }
    ],
    ""loadout"": [
        { ""id"": ""WPN-RIF-01"", ""container"": ""Equipped"", ""slot"": ""Primary1"", ""index"": 0, ""x"": 0, ""y"": 0, ""rotated"": false,
          ""rounds"": -1, ""usesLeft"": -1, ""loadedMagazineId"": ""MAG-556-30"", ""loadedMagazineRounds"": 17 },
        { ""id"": ""RIG-01"", ""container"": ""Equipped"", ""slot"": ""TacticalVest"", ""index"": 0, ""x"": 0, ""y"": 0, ""rotated"": false,
          ""rounds"": -1, ""usesLeft"": -1, ""loadedMagazineId"": """", ""loadedMagazineRounds"": 0 },
        { ""id"": ""MAG-556-30"", ""container"": ""TacticalVest"", ""slot"": """", ""index"": 0, ""x"": 0, ""y"": 0, ""rotated"": true,
          ""rounds"": 30, ""usesLeft"": -1, ""loadedMagazineId"": """", ""loadedMagazineRounds"": 0 },
        { ""id"": ""MED-BND-01"", ""container"": ""Pocket"", ""slot"": """", ""index"": 2, ""x"": 0, ""y"": 0, ""rotated"": false,
          ""rounds"": -1, ""usesLeft"": 1, ""loadedMagazineId"": """", ""loadedMagazineRounds"": 0 }
    ],
    ""quickSlots"": [3, -1, -1, -1]
}";
            PlayerProfile profile = Load(json);

            Assert.AreEqual(5480, profile.Casings);
            Assert.AreEqual(new DateTime(638651718000000000, DateTimeKind.Utc), profile.LastKitClaimUtc);
            Assert.AreEqual(1, profile.Stash.Items.Count);
            Assert.AreSame(catalog.Get("WPN-PST-01"), StashAt(profile, 0).Item.Definition);
            Assert.AreEqual(4, profile.Loadout.Entries.Count);
            Assert.AreEqual(17, ((WeaponItem)profile.Loadout.Entries[0].Item).LoadedMagazine.Rounds);
            Assert.IsTrue(profile.Loadout.Entries[2].Location.Rotated);
            Assert.AreEqual(new GridRef(GridKind.Pocket, 2), profile.Loadout.Entries[3].Location.Grid);
            Assert.AreSame(profile.Loadout.Entries[3].Item, profile.Loadout.QuickSlots[0]);
            Assert.IsFalse(report.HasNotice);
        }

        [Test]
        public void RaidInProgress_IsCarriedThrough()
        {
            SaveData data = MinimalData();
            data.raidInProgress = true;
            PlayerProfile profile = SaveSerializer.ToProfile(data, catalog, M5TestData.Defaults(), report);
            Assert.IsTrue(profile.RaidInProgress, "감지는 ProfileService — 직렬화는 그대로 옮긴다");
            StringAssert.Contains("\"raidInProgress\": true", SaveSerializer.ToJson(profile, M5TestData.Epoch));
        }

        // ── 읽기 규칙 (§2.4) ──────────────────────────────────

        [Test]
        public void UnknownId_DropsThatRecordOnly()
        {
            SaveData data = MinimalData();
            data.stash.Add(Record("NOPE-01", "Stash", x: 0, y: 0));
            data.stash.Add(Record("JNK-03", "Stash", x: 1, y: 0));
            data.loadout.Add(Record("NOPE-02", "Equipped", slot: "Helmet"));

            PlayerProfile profile = SaveSerializer.ToProfile(data, catalog, M5TestData.Defaults(), report);

            Assert.AreEqual(1, profile.Stash.Items.Count);
            Assert.IsTrue(profile.Loadout.IsEmpty);
            CollectionAssert.AreEqual(new[] { "NOPE-01", "NOPE-02" }, report.UnknownItemIds);
            Assert.IsTrue(report.HasNotice);
        }

        [Test]
        public void UnknownLoadedMagazineId_WeaponWithoutMagazine()
        {
            SaveData data = MinimalData();
            ItemRecord rifle = Record("WPN-RIF-01", "Stash");
            rifle.loadedMagazineId = "MAG-NOPE";
            rifle.loadedMagazineRounds = 10;
            data.stash.Add(rifle);
            ItemRecord rifle2 = Record("WPN-RIF-01", "Stash", y: 2);
            rifle2.loadedMagazineId = "JNK-01";   // 탄창이 아닌 정의 = 알 수 없는 탄창
            data.stash.Add(rifle2);

            PlayerProfile profile = SaveSerializer.ToProfile(data, catalog, M5TestData.Defaults(), report);

            Assert.AreEqual(2, profile.Stash.Items.Count);
            Assert.IsNull(((WeaponItem)StashAt(profile, 0).Item).LoadedMagazine);
            Assert.IsNull(((WeaponItem)StashAt(profile, 1).Item).LoadedMagazine);
            CollectionAssert.AreEqual(new[] { "MAG-NOPE", "JNK-01" }, report.UnknownItemIds);
        }

        // M5 리뷰 8: 소총(5.56)에 9mm 탄창 → 탄창 없는 무기 + 버림 집계 (알 수 없는 id 아님)
        [Test]
        public void LoadedMagazine_AmmoMismatch_WeaponWithoutMagazine_CountedAsDropped()
        {
            SaveData data = MinimalData();
            ItemRecord rifle = Record("WPN-RIF-01", "Stash");
            rifle.loadedMagazineId = "MAG-9MM-30";
            rifle.loadedMagazineRounds = 20;
            data.stash.Add(rifle);
            ItemRecord ok = Record("WPN-RIF-01", "Stash", y: 2);
            ok.loadedMagazineId = "MAG-556-30";
            ok.loadedMagazineRounds = 12;
            data.stash.Add(ok);

            PlayerProfile profile = SaveSerializer.ToProfile(data, catalog, M5TestData.Defaults(), report);

            Assert.AreEqual(2, profile.Stash.Items.Count);
            Assert.IsNull(((WeaponItem)StashAt(profile, 0).Item).LoadedMagazine);
            Magazine kept = ((WeaponItem)StashAt(profile, 1).Item).LoadedMagazine;
            Assert.IsNotNull(kept);
            Assert.AreEqual(12, kept.Rounds);
            Assert.AreEqual(1, report.DroppedItemCount);
            Assert.AreEqual(0, report.UnknownItemIds.Count);
            Assert.IsTrue(report.HasNotice);
        }

        [Test]
        public void StateValues_ClampedToRange()
        {
            SaveData data = MinimalData();
            ItemRecord over = Record("MAG-556-30", "Stash", x: 0);
            over.rounds = 99;
            ItemRecord under = Record("MAG-556-30", "Stash", x: 1);
            under.rounds = -4;
            ItemRecord med = Record("MED-BND-01", "Stash", x: 2);
            med.usesLeft = 7;
            data.stash.Add(over);
            data.stash.Add(under);
            data.stash.Add(med);

            PlayerProfile profile = SaveSerializer.ToProfile(data, catalog, M5TestData.Defaults(), report);

            Assert.AreEqual(30, ((Magazine)StashAt(profile, 0).Item).Rounds);
            Assert.AreEqual(0, ((Magazine)StashAt(profile, 1).Item).Rounds);
            Assert.AreEqual(1, ((MedicalItem)StashAt(profile, 2).Item).UsesLeft);
        }

        [Test]
        public void Stash_OutOfRangeOrOverlap_AutoPlacedAfterOthers()
        {
            SaveData data = MinimalData();
            data.stash.Add(Record("JNK-01", "Stash", x: 50, y: 50));   // 범위 밖 (1×3)
            data.stash.Add(Record("JNK-03", "Stash", x: 0, y: 0));     // 정상
            data.stash.Add(Record("JNK-03", "Stash", x: 0, y: 0));     // 겹침

            PlayerProfile profile = SaveSerializer.ToProfile(data, catalog, M5TestData.Defaults(), report);

            Assert.AreEqual(3, profile.Stash.Items.Count);
            Assert.AreEqual(0, report.DroppedItemCount);
            Assert.AreSame(catalog.Get("JNK-03"), StashAt(profile, 0).Item.Definition, "정상 자리가 먼저");
            Assert.AreEqual(Vector2Int.zero, StashAt(profile, 0).Cell);
            Assert.AreEqual(new Vector2Int(1, 0), StashAt(profile, 1).Cell, "범위 밖 → 자동 배치 (위→아래, 왼→오른쪽)");
            Assert.AreEqual(new Vector2Int(2, 0), StashAt(profile, 2).Cell);
            Assert.IsFalse(report.HasNotice, "자동 배치로 구한 것은 알림 없음");
        }

        [Test]
        public void Stash_NoRoom_DroppedCount()
        {
            SaveData data = MinimalData();
            data.stash.Add(Record("JNK-03", "Stash", x: 0, y: 0));
            data.stash.Add(Record("JNK-03", "Stash", x: 0, y: 0));   // 겹침, 1×1 보관함엔 자리 없음

            PlayerProfile profile = SaveSerializer.ToProfile(data, catalog, new ProfileDefaults(Vector2Int.one, 0), report);

            Assert.AreEqual(1, profile.Stash.Items.Count);
            Assert.AreEqual(1, report.DroppedItemCount);
            Assert.IsTrue(report.HasNotice);
        }

        [Test]
        public void Loadout_InvalidContainerOrSlot_GoesToStash()
        {
            SaveData data = MinimalData();
            data.loadout.Add(Record("JNK-03", "Fridge"));
            data.loadout.Add(Record("ARM-HLM-01", "Equipped", slot: "Hat"));
            data.loadout.Add(Record("JNK-01", "Equipped", slot: "0"));       // 숫자 문자열은 이름이 아니다
            data.loadout.Add(Record("JNK-02", "Equipped", slot: "primary1")); // 대소문자 다름
            data.loadout.Add(Record("WPN-RIF-01", "Equipped", slot: "Primary1"));

            PlayerProfile profile = SaveSerializer.ToProfile(data, catalog, M5TestData.Defaults(), report);

            Assert.AreEqual(1, profile.Loadout.Entries.Count);
            Assert.AreEqual(EquipSlot.Primary1, profile.Loadout.Entries[0].Location.Slot);
            Assert.AreEqual(4, profile.Stash.Items.Count, "해석 못 한 자리 → 보관함 자동 배치");
            Assert.AreEqual(0, report.DroppedItemCount);
        }

        // 해석은 되지만 자리가 틀린 것(주머니 번호 9, 겹침)은 Loadout에 그대로 — Inventory 적용 단계가 거부한다
        [Test]
        public void Loadout_ParsableButWrongCell_KeptForInventoryToReject()
        {
            SaveData data = MinimalData();
            data.loadout.Add(Record("JNK-03", "Pocket", index: 9));
            data.loadout.Add(Record("JNK-03", "Backpack", x: 99, y: 99));

            PlayerProfile profile = SaveSerializer.ToProfile(data, catalog, M5TestData.Defaults(), report);

            Assert.AreEqual(2, profile.Loadout.Entries.Count);
            Assert.AreEqual(new GridRef(GridKind.Pocket, 9), profile.Loadout.Entries[0].Location.Grid);
            Assert.AreEqual(0, profile.Stash.Items.Count);
        }

        [Test]
        public void Loadout_InvalidLocation_WhileRaidInProgress_CountsAsCrashLost()
        {
            SaveData data = MinimalData();
            data.raidInProgress = true;
            data.loadout.Add(Record("JNK-03", "Stash"));

            PlayerProfile profile = SaveSerializer.ToProfile(data, catalog, M5TestData.Defaults(), report);

            Assert.AreEqual(0, profile.Stash.Items.Count, "레이드 중이던 장비는 보관함으로 구하지 않는다 (RULE-03)");
            CollectionAssert.AreEqual(new[] { catalog.Get("JNK-03") }, report.CrashLost);
        }

        [Test]
        public void QuickSlots_InvalidIndexOrNonMedical_Empty()
        {
            SaveData data = MinimalData();
            data.loadout.Add(Record("JNK-03", "Pocket", index: 0));
            data.loadout.Add(Record("MED-BND-01", "Pocket", index: 1));
            data.loadout.Add(Record("NOPE", "Pocket", index: 2));
            data.quickSlots = new[] { 0, 1, 7, 2, 1 };

            Loadout loadout = SaveSerializer.ToProfile(data, catalog, M5TestData.Defaults(), report).Loadout;

            Assert.IsNull(loadout.QuickSlots[0], "의약품 아님");
            Assert.IsInstanceOf<MedicalItem>(loadout.QuickSlots[1]);
            Assert.IsNull(loadout.QuickSlots[2], "범위 밖");
            Assert.IsNull(loadout.QuickSlots[3], "버려진 레코드");
        }

        [Test]
        public void NegativeCasings_Zero()
        {
            SaveData data = MinimalData();
            data.casings = -50;
            Assert.AreEqual(0, SaveSerializer.ToProfile(data, catalog, M5TestData.Defaults(), report).Casings);
        }

        [Test]
        public void MissingLists_TreatedAsEmpty()
        {
            PlayerProfile profile = Load("{\"version\": 1, \"casings\": 3}");
            Assert.AreEqual(3, profile.Casings);
            Assert.AreEqual(0, profile.Stash.Items.Count);
            Assert.IsTrue(profile.Loadout.IsEmpty);
        }

        // ── 손상·버전 ─────────────────────────────────────────

        [TestCase("")]
        [TestCase("   ")]
        [TestCase(null)]
        [TestCase("hello")]
        [TestCase("{{{")]
        [TestCase("[1, 2, 3]")]
        [TestCase("{\"version\": 1, \"stash\": [")]
        [TestCase("{}")]
        [TestCase("{\"casings\": 10}")]
        [TestCase("{\"version\": 0}")]
        [TestCase("{\"version\": -3}")]
        public void TryParse_Garbage_CorruptWithoutException(string json)
        {
            bool ok = true;
            SaveLoadStatus status = SaveLoadStatus.Loaded;
            string error = null;
            Assert.DoesNotThrow(() => ok = SaveSerializer.TryParse(json, out _, out status, out error));
            Assert.IsFalse(ok);
            Assert.AreEqual(SaveLoadStatus.Corrupt, status);
            Assert.IsFalse(string.IsNullOrEmpty(error));
        }

        [Test]
        public void TryParse_NewerVersion_Unsupported()
        {
            Assert.IsFalse(SaveSerializer.TryParse("{\"version\": 2}", out SaveData data, out SaveLoadStatus status, out string error));
            Assert.AreEqual(SaveLoadStatus.UnsupportedVersion, status);
            Assert.IsNull(data);
            StringAssert.Contains("2", error);
        }

        // ── 헬퍼 ─────────────────────────────────────────────

        private static SaveData MinimalData() => new SaveData { version = SaveSerializer.CurrentVersion };

        private static ItemRecord Record(string id, string container, string slot = "", int index = 0, int x = 0, int y = 0) =>
            new ItemRecord
            {
                id = id, container = container, slot = slot, index = index, x = x, y = y,
                loadedMagazineId = string.Empty,
            };
    }
}
