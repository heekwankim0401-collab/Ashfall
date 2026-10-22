using Ashfall.Core;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // save.json version 2 (M7 설계 §1.6, Q13·Q14): 무기 내구도 왕복, v1 → 100 이전, 튜브 무기 복원, 잘못된 값, v3 거부
    public class SaveSerializerV2Tests
    {
        private FakeCatalog catalog;
        private SaveLoadReport report;
        private WeaponDefinitionSO shotgun;
        private MagazineDefinitionSO tube;
        private MagazineDefinitionSO box;

        [SetUp]
        public void SetUp()
        {
            catalog = FakeCatalog.Standard();
            tube = M7TestData.ShotgunTube();
            box = M7TestData.Box12Ga8();
            shotgun = M7TestData.Shotgun(tube);
            catalog.Add(shotgun, 2500);
            catalog.Add(tube, 0);
            catalog.Add(box, 200);
            report = new SaveLoadReport();
        }

        private PlayerProfile Load(string json)
        {
            Assert.IsTrue(SaveSerializer.TryParse(json, out SaveData data, out SaveLoadStatus status, out string error), error);
            Assert.AreEqual(SaveLoadStatus.Loaded, status);
            return SaveSerializer.ToProfile(data, catalog, M5TestData.Defaults(), report);
        }

        private PlayerProfile RoundTrip(PlayerProfile profile) => Load(SaveSerializer.ToJson(profile, M5TestData.Epoch));

        private static PlayerProfile WithStash(params ItemInstance[] items)
        {
            var profile = new PlayerProfile(M5TestData.StashSize, 0);
            for (int i = 0; i < items.Length; i++)
                Assert.IsTrue(profile.Stash.TryPlace(items[i], new Vector2Int(0, i * 2), false));
            return profile;
        }

        private static WeaponItem StashWeapon(PlayerProfile profile, int index) => (WeaponItem)profile.Stash.Items[index].Item;

        private static ItemRecord Record(string id) =>
            new ItemRecord { id = id, container = ItemRecord.StashContainer, slot = string.Empty, loadedMagazineId = string.Empty };

        // ── 쓰기 ─────────────────────────────────────────────

        [Test]
        public void CurrentVersion_Is2()
        {
            Assert.AreEqual(2, SaveSerializer.CurrentVersion);
            StringAssert.Contains("\"version\": 2", SaveSerializer.ToJson(WithStash(), M5TestData.Epoch));
        }

        [Test]
        public void ToRecord_WeaponWritesDurability_OthersMinusOne()
        {
            var rifle = new WeaponItem(TestData.Rifle()) { Durability = 63.25f };
            Assert.AreEqual(63.25f, SaveSerializer.ToRecord(rifle).durability);
            Assert.AreEqual(-1f, SaveSerializer.ToRecord(Magazine.Full(TestData.Magazine30())).durability);
            Assert.AreEqual(-1f, SaveSerializer.ToRecord(new ItemInstance(TestData.Pipe())).durability);
            StringAssert.Contains("\"durability\": 63.25", SaveSerializer.ToJson(WithStash(rifle), M5TestData.Epoch));
        }

        // ── 왕복 ─────────────────────────────────────────────

        [Test]
        public void RoundTrip_Durability()
        {
            var worn = new WeaponItem(TestData.Rifle(), Magazine.Full(TestData.Magazine30())) { Durability = 37.5f };
            var fresh = new WeaponItem(M3TestData.Pistol9());
            PlayerProfile loaded = RoundTrip(WithStash(worn, fresh));

            Assert.AreEqual(37.5f, StashWeapon(loaded, 0).Durability);
            Assert.AreEqual(30, StashWeapon(loaded, 0).LoadedMagazine.Rounds);
            Assert.AreEqual(100f, StashWeapon(loaded, 1).Durability);
            Assert.IsFalse(report.HasNotice);
        }

        [Test]
        public void RoundTrip_Durability_InLoadout()
        {
            var rifle = new WeaponItem(TestData.Rifle()) { Durability = 12f };
            var profile = new PlayerProfile(M5TestData.StashSize, 0)
            {
                Loadout = new Loadout(new[] { new LoadoutEntry(rifle, ItemLocation.Equipped(EquipSlot.Primary1)) }, new ItemInstance[Loadout.QuickSlotCount]),
            };
            PlayerProfile loaded = RoundTrip(profile);
            Assert.AreEqual(12f, ((WeaponItem)loaded.Loadout.Entries[0].Item).Durability);
        }

        [Test]
        public void JamState_NotSaved()
        {
            var rifle = new WeaponItem(TestData.Rifle()) { Durability = 10f, IsJammed = true };
            PlayerProfile loaded = RoundTrip(WithStash(rifle));
            Assert.IsFalse(StashWeapon(loaded, 0).IsJammed, "Q14: 은신처 귀환 = 해제된 것으로");
            Assert.AreEqual(10f, StashWeapon(loaded, 0).Durability);
        }

        // ── v1 이전 ───────────────────────────────────────────

        private const string V1Json = @"{
    ""version"": 1,
    ""savedAtUtc"": ""2026-10-22T09:30:00.0000000Z"",
    ""raidInProgress"": false,
    ""casings"": 777,
    ""kitLastClaimUtcTicks"": 0,
    ""stash"": [
        { ""id"": ""WPN-RIF-01"", ""container"": ""Stash"", ""slot"": """", ""index"": 0, ""x"": 0, ""y"": 0, ""rotated"": false,
          ""rounds"": -1, ""usesLeft"": -1, ""loadedMagazineId"": ""MAG-556-30"", ""loadedMagazineRounds"": 17 },
        { ""id"": ""MAG-556-30"", ""container"": ""Stash"", ""slot"": """", ""index"": 0, ""x"": 5, ""y"": 0, ""rotated"": false,
          ""rounds"": 9, ""usesLeft"": -1, ""loadedMagazineId"": """", ""loadedMagazineRounds"": 0 }
    ],
    ""loadout"": [
        { ""id"": ""WPN-PST-01"", ""container"": ""Equipped"", ""slot"": ""Secondary"", ""index"": 0, ""x"": 0, ""y"": 0, ""rotated"": false,
          ""rounds"": -1, ""usesLeft"": -1, ""loadedMagazineId"": """", ""loadedMagazineRounds"": 0 }
    ],
    ""quickSlots"": [-1, -1, -1, -1]
}";

        [Test]
        public void V1File_AllWeaponsMigratedTo100()
        {
            PlayerProfile profile = Load(V1Json);

            Assert.AreEqual(777, profile.Casings);
            Assert.AreEqual(2, profile.Stash.Items.Count);
            Assert.AreEqual(100f, StashWeapon(profile, 0).Durability);
            Assert.AreEqual(17, StashWeapon(profile, 0).LoadedMagazine.Rounds, "그 밖 필드는 같다");
            Assert.AreEqual(9, ((Magazine)profile.Stash.Items[1].Item).Rounds);
            Assert.AreEqual(100f, ((WeaponItem)profile.Loadout.Entries[0].Item).Durability);
            Assert.IsFalse(report.HasNotice);
        }

        [Test]
        public void V1File_DurabilityFieldPresent_StillVersionDecides()
        {
            // JsonUtility 누락 필드 처리에 기대지 않는다 — v1이면 기록 값과 상관없이 100
            string json = V1Json.Replace("\"loadedMagazineRounds\": 17 }", "\"loadedMagazineRounds\": 17, \"durability\": 0.0 }");
            PlayerProfile profile = Load(json);
            Assert.AreEqual(100f, StashWeapon(profile, 0).Durability);
        }

        [Test]
        public void V1File_ResavedAsV2()
        {
            PlayerProfile profile = Load(V1Json);
            StashWeapon(profile, 0).Durability = 80f;
            string json = SaveSerializer.ToJson(profile, M5TestData.Epoch);
            StringAssert.Contains("\"version\": 2", json);

            report = new SaveLoadReport();
            Assert.AreEqual(80f, StashWeapon(Load(json), 0).Durability);
        }

        [Test]
        public void V2File_MissingDurabilityField_TreatedAsNotApplicable_100()
        {
            string json = V1Json.Replace("\"version\": 1", "\"version\": 2");
            Assert.AreEqual(100f, StashWeapon(Load(json), 0).Durability);
        }

        // ── 잘못된 값 ─────────────────────────────────────────

        [TestCase(-1f, 100f)]
        [TestCase(-5f, 100f)]
        [TestCase(float.NaN, 100f)]
        [TestCase(150f, 100f)]
        [TestCase(0f, 0f)]
        [TestCase(49.5f, 49.5f)]
        [TestCase(100f, 100f)]
        public void V2_Durability_Clamped(float recorded, float expected)
        {
            ItemRecord record = Record("WPN-RIF-01");
            record.durability = recorded;
            var weapon = (WeaponItem)SaveSerializer.FromRecord(record, catalog, report, 2);
            Assert.AreEqual(expected, weapon.Durability);
            Assert.AreEqual(expected, SaveSerializer.ReadDurability(recorded, 2));
        }

        [Test]
        public void FromRecord_ThreeArg_UsesCurrentVersion()
        {
            ItemRecord record = Record("WPN-RIF-01");
            record.durability = 20f;
            Assert.AreEqual(20f, ((WeaponItem)SaveSerializer.FromRecord(record, catalog, report)).Durability);
            Assert.AreEqual(100f, ((WeaponItem)SaveSerializer.FromRecord(record, catalog, report, 1)).Durability);
        }

        // ── 튜브 무기 ─────────────────────────────────────────

        [Test]
        public void Tube_RoundTrip_KeepsRounds()
        {
            var weapon = new WeaponItem(shotgun, new Magazine(tube, 4)) { Durability = 70f };
            string json = SaveSerializer.ToJson(WithStash(weapon), M5TestData.Epoch);
            StringAssert.Contains("\"loadedMagazineId\": \"MAG-12GA-TUBE\"", json);

            WeaponItem loaded = StashWeapon(Load(json), 0);
            Assert.AreSame(tube, loaded.LoadedMagazine.Definition);
            Assert.AreEqual(4, loaded.LoadedMagazine.Rounds);
            Assert.AreEqual(70f, loaded.Durability);
            Assert.IsFalse(report.HasNotice);
        }

        [Test]
        public void Tube_EmptyId_RestoredAsEmptyTube()
        {
            ItemRecord record = Record("WPN-SHT-01");
            var weapon = (WeaponItem)SaveSerializer.FromRecord(record, catalog, report, 2);
            Assert.IsNotNull(weapon.LoadedMagazine);
            Assert.AreSame(tube, weapon.LoadedMagazine.Definition);
            Assert.AreEqual(0, weapon.LoadedMagazine.Rounds);
            Assert.IsFalse(report.HasNotice);
        }

        [Test]
        public void Tube_RoundsClampedToCapacity()
        {
            ItemRecord record = Record("WPN-SHT-01");
            record.loadedMagazineId = "MAG-12GA-TUBE";
            record.loadedMagazineRounds = 99;
            var weapon = (WeaponItem)SaveSerializer.FromRecord(record, catalog, report, 2);
            Assert.AreEqual(6, weapon.LoadedMagazine.Rounds);
        }

        [Test]
        public void Tube_OtherMagazineId_EmptyTube_CountedAsDropped()
        {
            ItemRecord record = Record("WPN-SHT-01");
            record.loadedMagazineId = "MAG-9MM-12";   // 탄약이 다름
            record.loadedMagazineRounds = 5;
            var weapon = (WeaponItem)SaveSerializer.FromRecord(record, catalog, report, 2);
            Assert.AreSame(tube, weapon.LoadedMagazine.Definition);
            Assert.AreEqual(0, weapon.LoadedMagazine.Rounds);
            Assert.AreEqual(1, report.DroppedItemCount);

            ItemRecord boxed = Record("WPN-SHT-01");
            boxed.loadedMagazineId = "BOX-12GA-8";     // 같은 탄이라도 튜브가 아님
            boxed.loadedMagazineRounds = 8;
            var weapon2 = (WeaponItem)SaveSerializer.FromRecord(boxed, catalog, report, 2);
            Assert.AreSame(tube, weapon2.LoadedMagazine.Definition);
            Assert.AreEqual(0, weapon2.LoadedMagazine.Rounds);
            Assert.AreEqual(2, report.DroppedItemCount);
        }

        [Test]
        public void Tube_UnknownId_EmptyTube_ReportedUnknown()
        {
            ItemRecord record = Record("WPN-SHT-01");
            record.loadedMagazineId = "MAG-NOPE";
            var weapon = (WeaponItem)SaveSerializer.FromRecord(record, catalog, report, 2);
            Assert.AreEqual(0, weapon.LoadedMagazine.Rounds);
            CollectionAssert.AreEqual(new[] { "MAG-NOPE" }, report.UnknownItemIds);
        }

        [Test]
        public void DetachableWeapon_AmmoBoxAsLoaded_Rejected()
        {
            MagazineDefinitionSO box556 = M7TestData.Box12Ga8();
            box556.id = "TEST-BOX-556";
            box556.ammo = AmmoType.Rifle556;
            catalog.Add(box556, 0);
            ItemRecord record = Record("WPN-RIF-01");
            record.loadedMagazineId = "TEST-BOX-556";
            record.loadedMagazineRounds = 5;
            var weapon = (WeaponItem)SaveSerializer.FromRecord(record, catalog, report, 2);
            Assert.IsNull(weapon.LoadedMagazine, "탄 상자는 무기에 넣을 수 없다");
            Assert.AreEqual(1, report.DroppedItemCount);
        }

        [Test]
        public void AmmoBox_RoundTrip_EmptyBoxKept()
        {
            PlayerProfile loaded = RoundTrip(WithStash(new Magazine(box, 0), new Magazine(box, 5)));
            Assert.AreEqual(2, loaded.Stash.Items.Count, "Q6: 빈 상자도 아이템");
            Assert.AreEqual(0, ((Magazine)loaded.Stash.Items[0].Item).Rounds);
            Assert.AreEqual(5, ((Magazine)loaded.Stash.Items[1].Item).Rounds);
            Assert.IsTrue(((Magazine)loaded.Stash.Items[1].Item).Definition.isAmmoBox);
        }

        // ── 버전 ─────────────────────────────────────────────

        [Test]
        public void V3_Unsupported()
        {
            Assert.IsFalse(SaveSerializer.TryParse("{\"version\": 3}", out SaveData data, out SaveLoadStatus status, out string error));
            Assert.AreEqual(SaveLoadStatus.UnsupportedVersion, status);
            Assert.IsNull(data);
            StringAssert.Contains("3", error);
        }

        [Test]
        public void V1_And_V2_Parse()
        {
            Assert.IsTrue(SaveSerializer.TryParse("{\"version\": 1}", out _, out _, out _));
            Assert.IsTrue(SaveSerializer.TryParse("{\"version\": 2}", out _, out _, out _));
        }
    }
}
