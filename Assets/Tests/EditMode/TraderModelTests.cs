using System;
using Ashfall.Core;
using Ashfall.Hideout;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // SYS-HUB 상인 매매 (§9, M5 설계 §3.2, Q8~Q12) + StashRules
    public class TraderModelTests
    {
        private FakeCatalog catalog;
        private TraderConfigSO trader;
        private PlayerProfile profile;
        private TraderModel model;

        [SetUp]
        public void SetUp()
        {
            catalog = FakeCatalog.Standard();
            trader = M5TestData.TraderConfig(catalog);
            profile = new PlayerProfile(M5TestData.StashSize, M5TestData.StartingCasings);
            model = new TraderModel(profile, trader);
        }

        private ItemInstance OnlyStashItem()
        {
            Assert.AreEqual(1, profile.Stash.Items.Count);
            return profile.Stash.Items[0].Item;
        }

        [Test]
        public void Constructor_Nulls_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => new TraderModel(null, trader));
            Assert.Throws<ArgumentNullException>(() => new TraderModel(profile, null));
        }

        // ── 판매 목록 ─────────────────────────────────────────

        [Test]
        public void Offers_CatalogOrder_WithBuyPrices()
        {
            Assert.AreEqual(trader.catalog.Length, model.Offers.Count);
            for (int i = 0; i < trader.catalog.Length; i++)
            {
                Assert.AreSame(trader.catalog[i], model.Offers[i].Item);
                Assert.AreEqual(trader.catalog[i].basePrice, model.Offers[i].BuyPrice);
            }
            Assert.AreEqual("고물상", model.TraderName);
        }

        [Test]
        public void Offers_SkipNullZeroPriceAndDuplicates()
        {
            ItemDefinitionSO free = TestData.Item("TEST-FREE", "공짜", Vector2Int.one, 0.1f);
            free.basePrice = 0;
            trader.catalog = new[] { catalog.Get("MED-BND-01"), null, free, catalog.Get("MED-BND-01"), catalog.Get("RIG-01") };
            var m = new TraderModel(profile, trader);
            Assert.AreEqual(2, m.Offers.Count);
            Assert.AreEqual("MED-BND-01", m.Offers[0].Item.id);
            Assert.AreEqual("RIG-01", m.Offers[1].Item.id);
        }

        [Test]
        public void Offers_NullCatalog_Empty()
        {
            trader.catalog = null;
            Assert.AreEqual(0, new TraderModel(profile, trader).Offers.Count);
        }

        // ── 매수 ──────────────────────────────────────────────

        [Test]
        public void TryBuy_DeductsAndPlacesInStash()
        {
            Assert.AreEqual(TradeResult.Ok, model.TryBuy(catalog.Get("ITM-MAP-01")));
            Assert.AreEqual(6000 - 100, profile.Casings);
            ItemInstance bought = OnlyStashItem();
            Assert.AreEqual("ITM-MAP-01", bought.Definition.id);
            Assert.AreEqual(Vector2Int.zero, profile.Stash.Items[0].Cell);
        }

        [Test]
        public void TryBuy_MagazineIsFull_WeaponHasNoMagazine()
        {
            Assert.AreEqual(TradeResult.Ok, model.TryBuy(catalog.Get("MAG-556-30")));
            var mag = (Magazine)OnlyStashItem();
            Assert.AreEqual(mag.Capacity, mag.Rounds);
            profile.Stash.Remove(mag);

            Assert.AreEqual(TradeResult.Ok, model.TryBuy(catalog.Get("WPN-RIF-01")));
            var rifle = (WeaponItem)OnlyStashItem();
            Assert.IsNull(rifle.LoadedMagazine);
            profile.Stash.Remove(rifle);

            Assert.AreEqual(TradeResult.Ok, model.TryBuy(catalog.Get("MED-BND-01")));
            var med = (MedicalItem)OnlyStashItem();
            Assert.AreEqual(med.Definition.uses, med.UsesLeft);
        }

        [Test]
        public void TryBuy_ExactCasings_Ok_ToZero()
        {
            profile.Casings = 100;
            Assert.AreEqual(TradeResult.Ok, model.TryBuy(catalog.Get("ITM-MAP-01")));
            Assert.AreEqual(0, profile.Casings);
        }

        [Test]
        public void TryBuy_NotEnoughCasings_NoChange()
        {
            profile.Casings = 3999;
            Assert.AreEqual(TradeResult.NotEnoughCasings, model.TryBuy(catalog.Get("WPN-RIF-01")));
            Assert.AreEqual(3999, profile.Casings);
            Assert.IsTrue(profile.Stash.IsEmpty);
        }

        [Test]
        public void TryBuy_NoSpace_NoCharge()
        {
            var small = new PlayerProfile(new Vector2Int(2, 2), 6000);
            var m = new TraderModel(small, trader);
            Assert.AreEqual(TradeResult.NoSpace, m.TryBuy(catalog.Get("WPN-RIF-01")));   // 4×2는 2×2에 안 들어간다
            Assert.AreEqual(6000, small.Casings);
            Assert.IsTrue(small.Stash.IsEmpty);
        }

        [Test]
        public void TryBuy_FillsStashUntilNoSpace()
        {
            var small = new PlayerProfile(new Vector2Int(2, 1), 6000);
            var m = new TraderModel(small, trader);
            Assert.AreEqual(TradeResult.Ok, m.TryBuy(catalog.Get("MED-BND-01")));
            Assert.AreEqual(TradeResult.Ok, m.TryBuy(catalog.Get("MED-BND-01")));
            Assert.AreEqual(TradeResult.NoSpace, m.TryBuy(catalog.Get("MED-BND-01")));
            Assert.AreEqual(6000 - 120, small.Casings);
            Assert.AreEqual(2, small.Stash.Items.Count);
        }

        [Test]
        public void TryBuy_RotatesWhenOnlyRotatedFits()
        {
            var tall = new PlayerProfile(new Vector2Int(2, 4), 6000);   // 4×2 소총은 회전해야 들어간다
            var m = new TraderModel(tall, trader);
            Assert.AreEqual(TradeResult.Ok, m.TryBuy(catalog.Get("WPN-RIF-01")));
            Assert.IsTrue(tall.Stash.Items[0].Rotated);
        }

        [Test]
        public void TryBuy_NotInCatalog_NotForSale()
        {
            Assert.AreEqual(TradeResult.NotForSale, model.TryBuy(catalog.Get("ITM-FLR-01")));   // 신호탄은 팔지 않는다 (Q11)
            Assert.AreEqual(TradeResult.NotForSale, model.TryBuy(null));
            Assert.AreEqual(6000, profile.Casings);
            Assert.IsTrue(profile.Stash.IsEmpty);
        }

        // ── 매도 ──────────────────────────────────────────────

        [Test]
        public void TrySell_StashItem_RemovesAndAddsCasings()
        {
            WeaponItem rifle = M5TestData.Rifle(catalog, 17);
            Assert.IsTrue(StashRules.TryPlaceNew(profile.Stash, rifle));
            Assert.AreEqual(1760, model.GetSellPrice(rifle));
            Assert.AreEqual(TradeResult.Ok, model.TrySell(rifle));
            Assert.AreEqual(6000 + 1760, profile.Casings);
            Assert.IsTrue(profile.Stash.IsEmpty);
        }

        [Test]
        public void TrySell_ZeroPrice_DisposesItem()
        {
            ItemDefinitionSO junk = TestData.Item("TEST-JUNK", "쓰레기", Vector2Int.one, 0.1f);
            junk.basePrice = 0;
            var item = new ItemInstance(junk);
            Assert.IsTrue(StashRules.TryPlaceNew(profile.Stash, item));
            Assert.AreEqual(0, model.GetSellPrice(item));
            Assert.AreEqual(TradeResult.Ok, model.TrySell(item));
            Assert.AreEqual(6000, profile.Casings);
            Assert.IsTrue(profile.Stash.IsEmpty);
        }

        [Test]
        public void TrySell_NotInStash_Rejected()
        {
            var loose = M5TestData.Bandage(catalog);   // 장비·어디에도 없는 아이템
            Assert.AreEqual(-1, model.GetSellPrice(loose));
            Assert.AreEqual(TradeResult.NotInStash, model.TrySell(loose));
            Assert.AreEqual(TradeResult.NotInStash, model.TrySell(null));
            Assert.AreEqual(-1, model.GetSellPrice(null));
            Assert.AreEqual(6000, profile.Casings);
        }

        [Test]
        public void TrySell_Twice_SecondRejected()
        {
            var bandage = M5TestData.Bandage(catalog);
            StashRules.TryPlaceNew(profile.Stash, bandage);
            Assert.AreEqual(TradeResult.Ok, model.TrySell(bandage));
            Assert.AreEqual(TradeResult.NotInStash, model.TrySell(bandage));
            Assert.AreEqual(6000 + 24, profile.Casings);
        }

        [Test]
        public void BuyThenSell_LosesMoney()
        {
            Assert.AreEqual(TradeResult.Ok, model.TryBuy(catalog.Get("ARM-HLM-01")));
            Assert.AreEqual(TradeResult.Ok, model.TrySell(OnlyStashItem()));
            Assert.AreEqual(6000 - 1200 + 480, profile.Casings);
        }

        // ── StashRules ────────────────────────────────────────

        [Test]
        public void StashRules_TryPlaceNew_RejectsNullDuplicateAndFull()
        {
            var grid = new ItemGrid(new GridRef(GridKind.Loot), new Vector2Int(1, 1));
            var a = M5TestData.Bandage(catalog);
            Assert.IsFalse(StashRules.TryPlaceNew(null, a));
            Assert.IsFalse(StashRules.TryPlaceNew(grid, null));
            Assert.IsTrue(StashRules.TryPlaceNew(grid, a));
            Assert.IsFalse(StashRules.TryPlaceNew(grid, a));
            Assert.IsFalse(StashRules.TryPlaceNew(grid, M5TestData.Bandage(catalog)));
        }

        [Test]
        public void StashRules_TryPlaceAll_AllOrNothing()
        {
            var grid = new ItemGrid(new GridRef(GridKind.Loot), new Vector2Int(2, 1));
            var existing = M5TestData.Plain(catalog, "JNK-03");
            Assert.IsTrue(grid.TryPlace(existing, new Vector2Int(1, 0), false));

            var items = new ItemInstance[] { M5TestData.Bandage(catalog), null, M5TestData.Bandage(catalog) };
            Assert.IsFalse(StashRules.TryPlaceAll(grid, items));
            Assert.AreEqual(1, grid.Items.Count);
            Assert.AreSame(existing, grid.Items[0].Item);

            grid.Remove(existing);
            Assert.IsTrue(StashRules.TryPlaceAll(grid, items));
            Assert.AreEqual(2, grid.Items.Count);
            Assert.IsTrue(StashRules.TryPlaceAll(grid, new ItemInstance[0]));
            Assert.IsTrue(StashRules.TryPlaceAll(grid, null));
            Assert.IsFalse(StashRules.TryPlaceAll(null, items));
        }
    }
}
