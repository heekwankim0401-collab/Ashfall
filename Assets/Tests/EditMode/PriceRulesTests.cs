using Ashfall.Core;
using Ashfall.Hideout;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // SYS-HUB 상인 가격 규칙 (§9, M5 설계 §3.2, Q10)
    public class PriceRulesTests
    {
        private FakeCatalog catalog;
        private TraderConfigSO trader;

        [SetUp]
        public void SetUp()
        {
            catalog = FakeCatalog.Standard();
            trader = M5TestData.TraderConfig(catalog);
        }

        private ItemDefinitionSO WithPrice(int basePrice)
        {
            ItemDefinitionSO d = TestData.Item("TEST-PRICE", "가격 시험", Vector2Int.one, 0.1f);
            d.basePrice = basePrice;
            return d;
        }

        [Test]
        public void BuyPrice_DefaultMultiplier_IsBasePrice()
        {
            Assert.AreEqual(4000, PriceRules.BuyPrice(catalog.Get("WPN-RIF-01"), trader));
            Assert.AreEqual(60, PriceRules.BuyPrice(catalog.Get("MED-BND-01"), trader));
        }

        [Test]
        public void BuyPrice_RoundsUp_AndFloatErrorDoesNotAddOne()
        {
            trader.buyPriceMultiplier = 1.1f;
            Assert.AreEqual(110, PriceRules.BuyPrice(WithPrice(100), trader));   // 1.1f 오차로 111이 되지 않는다
            trader.buyPriceMultiplier = 1.25f;
            Assert.AreEqual(4, PriceRules.BuyPrice(WithPrice(3), trader));       // 3.75 → 4
        }

        [Test]
        public void BuyPrice_MinimumOne()
        {
            trader.buyPriceMultiplier = 0.01f;
            Assert.AreEqual(1, PriceRules.BuyPrice(WithPrice(5), trader));
            trader.buyPriceMultiplier = 0f;
            Assert.AreEqual(1, PriceRules.BuyPrice(WithPrice(5), trader));
        }

        [Test]
        public void BuyPrice_ZeroOrNegativeBase_NotForSale()
        {
            Assert.AreEqual(-1, PriceRules.BuyPrice(WithPrice(0), trader));
            Assert.AreEqual(-1, PriceRules.BuyPrice(WithPrice(-10), trader));
            Assert.AreEqual(-1, PriceRules.BuyPrice(catalog.Get("AMMO-9MM"), trader));
        }

        [Test]
        public void BuyPrice_Nulls_MinusOne()
        {
            Assert.AreEqual(-1, PriceRules.BuyPrice(null, trader));
            Assert.AreEqual(-1, PriceRules.BuyPrice(WithPrice(10), null));
        }

        [Test]
        public void SellPrice_RoundsDown()
        {
            Assert.AreEqual(24, PriceRules.SellPrice(M5TestData.Bandage(catalog), trader));     // 60 × 0.4
            Assert.AreEqual(1600, PriceRules.SellPrice(M5TestData.Rifle(catalog), trader));      // 4000 × 0.4
            Assert.AreEqual(1, PriceRules.SellPrice(new ItemInstance(WithPrice(3)), trader));    // 1.2 → 1
            trader.sellPriceMultiplier = 0.7f;
            Assert.AreEqual(70, PriceRules.SellPrice(new ItemInstance(WithPrice(100)), trader)); // 0.7f 오차로 69가 되지 않는다
        }

        [Test]
        public void SellPrice_ZeroBase_IsZero()
        {
            Assert.AreEqual(0, PriceRules.SellPrice(new ItemInstance(WithPrice(0)), trader));
            Assert.AreEqual(0, PriceRules.SellPrice(new ItemInstance(WithPrice(-5)), trader));
            Assert.AreEqual(0, PriceRules.SellPrice(new ItemInstance(WithPrice(2)), trader));    // 0.8 → 0
        }

        [Test]
        public void SellPrice_WeaponIncludesLoadedMagazine()
        {
            WeaponItem empty = M5TestData.Rifle(catalog);
            WeaponItem loaded = M5TestData.Rifle(catalog, 17);
            Assert.AreEqual(1600, PriceRules.SellPrice(empty, trader));
            Assert.AreEqual(1600 + 160, PriceRules.SellPrice(loaded, trader));   // 5.56 탄창 400 × 0.4
        }

        [Test]
        public void SellPrice_IgnoresRoundsAndUses()
        {
            int full = PriceRules.SellPrice(M5TestData.Mag556(catalog, 30), trader);
            int emptyMag = PriceRules.SellPrice(M5TestData.Mag556(catalog, 0), trader);
            Assert.AreEqual(full, emptyMag);
            var med = (MedicalDefinitionSO)catalog.Get("MED-BND-01");
            Assert.AreEqual(PriceRules.SellPrice(new MedicalItem(med), trader),
                PriceRules.SellPrice(new MedicalItem(med, 0), trader));
        }

        [Test]
        public void SellPrice_Nulls_MinusOne()
        {
            Assert.AreEqual(-1, PriceRules.SellPrice((ItemInstance)null, trader));
            Assert.AreEqual(-1, PriceRules.SellPrice(M5TestData.Bandage(catalog), null));
            Assert.AreEqual(-1, PriceRules.SellPrice((ItemDefinitionSO)null, trader));
        }

        [Test]
        public void DefaultMultipliers_SellNotAboveBuy()
        {
            var fresh = ScriptableObject.CreateInstance<TraderConfigSO>();
            Assert.LessOrEqual(fresh.sellPriceMultiplier, fresh.buyPriceMultiplier);
            foreach (TraderOffer offer in new TraderModel(new PlayerProfile(M5TestData.StashSize, 0), trader).Offers)
                Assert.LessOrEqual(PriceRules.SellPrice(ItemFactory.Create(offer.Item), trader), offer.BuyPrice, offer.Item.id);
        }
    }
}
