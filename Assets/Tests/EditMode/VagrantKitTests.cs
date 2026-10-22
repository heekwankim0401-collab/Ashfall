using System;
using System.Linq;
using Ashfall.Core;
using Ashfall.Hideout;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // SYS-HUB 부랑자 키트 (§9 녹슨 권총 + 탄창 2개 + 붕대, 20분 쿨다운, M5 설계 §3.3, Q13~Q17)
    public class VagrantKitTests
    {
        private FakeCatalog catalog;
        private HideoutConfigSO config;
        private PlayerProfile profile;
        private FakeClock clock;
        private VagrantKitModel kit;

        [SetUp]
        public void SetUp()
        {
            catalog = FakeCatalog.Standard();
            config = M5TestData.HideoutConfig(catalog);
            profile = new PlayerProfile(M5TestData.StashSize, M5TestData.StartingCasings);
            clock = new FakeClock();
            kit = new VagrantKitModel(profile, config, clock.Now);
        }

        [Test]
        public void Constructor_Nulls_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => new VagrantKitModel(null, config, clock.Now));
            Assert.Throws<ArgumentNullException>(() => new VagrantKitModel(profile, null, clock.Now));
            Assert.Throws<ArgumentNullException>(() => new VagrantKitModel(profile, config, null));
        }

        [Test]
        public void NewProfile_AvailableImmediately()
        {
            Assert.IsTrue(kit.IsAvailable);
            Assert.AreEqual(0f, kit.RemainingSec);
        }

        [Test]
        public void Claim_GivesFourItems_PistolEmpty_MagsFull_Bandage()
        {
            Assert.AreEqual(KitClaimResult.Ok, kit.TryClaim());
            var items = profile.Stash.Items.Select(p => p.Item).ToList();
            Assert.AreEqual(4, items.Count);

            var pistol = items.OfType<WeaponItem>().Single();
            Assert.AreEqual("WPN-PST-01", pistol.Definition.id);
            Assert.IsNull(pistol.LoadedMagazine);

            var mags = items.OfType<Magazine>().ToList();
            Assert.AreEqual(2, mags.Count);
            Assert.IsTrue(mags.All(m => m.Definition.id == "MAG-9MM-12" && m.Rounds == 12));
            Assert.AreNotSame(mags[0], mags[1]);

            var bandage = items.OfType<MedicalItem>().Single();
            Assert.AreEqual("MED-BND-01", bandage.Definition.id);
            Assert.AreEqual(bandage.Definition.uses, bandage.UsesLeft);

            Assert.AreEqual(M5TestData.Epoch, profile.LastKitClaimUtc);
            Assert.AreEqual(M5TestData.StartingCasings, profile.Casings);   // 무료
        }

        [Test]
        public void Cooldown_1199sOnCooldown_1200sAvailable()
        {
            Assert.AreEqual(KitClaimResult.Ok, kit.TryClaim());
            Assert.IsFalse(kit.IsAvailable);
            Assert.AreEqual(1200f, kit.RemainingSec, 1e-3f);

            clock.Advance(1199);
            Assert.AreEqual(1f, kit.RemainingSec, 1e-3f);
            Assert.AreEqual(KitClaimResult.OnCooldown, kit.TryClaim());
            Assert.AreEqual(4, profile.Stash.Items.Count);

            clock.Advance(1);
            Assert.IsTrue(kit.IsAvailable);
            Assert.AreEqual(KitClaimResult.Ok, kit.TryClaim());
            Assert.AreEqual(8, profile.Stash.Items.Count);
            Assert.AreEqual(clock.UtcNow, profile.LastKitClaimUtc);
        }

        [Test]
        public void ClockMovedBack_RemainingAtMostCooldown()
        {
            kit.TryClaim();
            clock.Advance(-3600);   // 시스템 시계를 한 시간 되돌림
            Assert.AreEqual(1200f, kit.RemainingSec, 1e-3f);
            Assert.IsFalse(kit.IsAvailable);
        }

        [Test]
        public void LastClaimFromSave_RespectsCooldown()
        {
            profile.LastKitClaimUtc = M5TestData.Epoch.AddSeconds(-600);
            Assert.AreEqual(600f, kit.RemainingSec, 1e-3f);
            Assert.AreEqual(KitClaimResult.OnCooldown, kit.TryClaim());
            Assert.IsTrue(profile.Stash.IsEmpty);
        }

        [Test]
        public void NoSpace_NothingPlaced_CooldownNotUsed()
        {
            var small = new PlayerProfile(new Vector2Int(2, 2), 0);   // 권총 2×1 + 탄창 1×1 ×2 = 4칸, 붕대가 안 들어간다
            var smallKit = new VagrantKitModel(small, config, clock.Now);
            Assert.AreEqual(KitClaimResult.NoSpace, smallKit.TryClaim());
            Assert.IsTrue(small.Stash.IsEmpty);
            Assert.IsNull(small.LastKitClaimUtc);
            Assert.IsTrue(smallKit.IsAvailable);
        }

        [Test]
        public void NoSpace_ExistingItemsUntouched()
        {
            var small = new PlayerProfile(new Vector2Int(3, 2), 0);
            var wires = M5TestData.Plain(catalog, "JNK-03");
            // 3×2 중 2칸을 막아 4칸만 남긴다 → 권총(회전)·탄창 2개는 놓이다가 붕대에서 실패 → 되돌림
            Assert.IsTrue(small.Stash.TryPlace(wires, new Vector2Int(2, 1), false));
            var blocker = M5TestData.Plain(catalog, "JNK-03");
            Assert.IsTrue(small.Stash.TryPlace(blocker, new Vector2Int(1, 0), false));
            var smallKit = new VagrantKitModel(small, config, clock.Now);
            Assert.AreEqual(KitClaimResult.NoSpace, smallKit.TryClaim());
            Assert.AreEqual(2, small.Stash.Items.Count);
            Assert.IsNull(small.LastKitClaimUtc);
        }

        [Test]
        public void EmptyKitList_Unavailable()
        {
            config.vagrantKitItems = new ItemDefinitionSO[] { null };
            Assert.AreEqual(KitClaimResult.Unavailable, kit.TryClaim());
            Assert.IsNull(profile.LastKitClaimUtc);
            config.vagrantKitItems = null;
            Assert.AreEqual(KitClaimResult.Unavailable, kit.TryClaim());
        }

        [Test]
        public void ZeroCooldown_AlwaysAvailable()
        {
            config.vagrantKitCooldownSec = 0f;
            Assert.AreEqual(KitClaimResult.Ok, kit.TryClaim());
            Assert.IsTrue(kit.IsAvailable);
            Assert.AreEqual(KitClaimResult.Ok, kit.TryClaim());
        }

        [Test]
        public void DefaultConfig_TwentyMinutes()
        {
            var fresh = ScriptableObject.CreateInstance<HideoutConfigSO>();
            Assert.AreEqual(1200f, fresh.vagrantKitCooldownSec);
            Assert.AreEqual(new Vector2Int(10, 30), fresh.stashSize);
            Assert.AreEqual(6000, fresh.startingCasings);
        }
    }
}
