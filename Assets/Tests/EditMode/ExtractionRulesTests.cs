using Ashfall.Core;
using Ashfall.Raid;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // M4 AC2 — §8.2 탈출 조건·구역 판정 (ExtractionRules, M4 설계 §3.1)
    public class ExtractionRulesTests
    {
        private RaidConfigSO config;

        [SetUp]
        public void SetUp() => config = M4TestData.RaidConfig();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(config);

        private ExtractionPointConfig Ext(string id) => M4TestData.Find(config, id);

        // ── 구역 ─────────────────────────────────────────────

        [Test]
        public void IsInZone_HorizontalRadius_InclusiveBoundary()
        {
            Vector3 center = new Vector3(10f, 5f, -20f);
            Assert.IsTrue(ExtractionRules.IsInZone(center, center, 8f, 3f), "중심");
            Assert.IsTrue(ExtractionRules.IsInZone(center + new Vector3(8f, 0f, 0f), center, 8f, 3f), "반경 경계 포함");
            Assert.IsFalse(ExtractionRules.IsInZone(center + new Vector3(8.01f, 0f, 0f), center, 8f, 3f), "반경 밖");
            Assert.IsTrue(ExtractionRules.IsInZone(center + new Vector3(5f, 0f, 6f), center, 8f, 3f), "대각선 √61 < 8");
            Assert.IsFalse(ExtractionRules.IsInZone(center + new Vector3(6f, 0f, 6f), center, 8f, 3f), "대각선 √72 > 8");
        }

        [Test]
        public void IsInZone_HeightTolerance()
        {
            Vector3 center = Vector3.zero;
            Assert.IsTrue(ExtractionRules.IsInZone(new Vector3(0f, 3f, 0f), center, 8f, 3f), "위 3m 포함");
            Assert.IsTrue(ExtractionRules.IsInZone(new Vector3(0f, -3f, 0f), center, 8f, 3f), "아래 3m 포함");
            Assert.IsFalse(ExtractionRules.IsInZone(new Vector3(0f, 3.1f, 0f), center, 8f, 3f), "고가 위");
            Assert.IsFalse(ExtractionRules.IsInZone(new Vector3(0f, -3.1f, 0f), center, 8f, 3f), "아래 층");
        }

        [Test]
        public void IsInZone_ZeroRadius_NeverInside()
        {
            Assert.IsFalse(ExtractionRules.IsInZone(Vector3.zero, Vector3.zero, 0f, 3f));
        }

        // ── 조건 ─────────────────────────────────────────────

        [Test]
        public void Ext01_AlwaysOpen()
        {
            Assert.AreEqual(ExtractionBlock.None,
                ExtractionRules.Evaluate(Ext(M4TestData.Ext01), 0f, false, true, false), "조건 없음 — 배낭·아이템 무관");
        }

        [Test]
        public void Ext02_NeedsPowerSwitch()
        {
            ExtractionPointConfig ext = Ext(M4TestData.Ext02);
            Assert.AreEqual(ExtractionBlock.NoPower, ExtractionRules.Evaluate(ext, 0f, false, false, false));
            Assert.AreEqual(ExtractionBlock.None, ExtractionRules.Evaluate(ext, 0f, true, false, false));
        }

        [Test]
        public void Ext03_AvailableFrom900Seconds()
        {
            ExtractionPointConfig ext = Ext(M4TestData.Ext03);
            Assert.AreEqual(ExtractionBlock.TooEarly, ExtractionRules.Evaluate(ext, 899f, false, false, true), "14:59");
            Assert.AreEqual(ExtractionBlock.TooEarly, ExtractionRules.Evaluate(ext, 899.99f, false, false, true));
            Assert.AreEqual(ExtractionBlock.None, ExtractionRules.Evaluate(ext, 900f, false, false, true), "15:00 = 이후");
        }

        [Test]
        public void Ext03_NeedsFlare()
        {
            ExtractionPointConfig ext = Ext(M4TestData.Ext03);
            Assert.AreEqual(ExtractionBlock.MissingItem, ExtractionRules.Evaluate(ext, 900f, false, false, false));
            Assert.AreEqual(ExtractionBlock.None, ExtractionRules.Evaluate(ext, 900f, false, false, true));
        }

        [Test]
        public void Ext04_ForbidsBackpackOnly()
        {
            ExtractionPointConfig ext = Ext(M4TestData.Ext04);
            Assert.AreEqual(ExtractionBlock.BackpackEquipped, ExtractionRules.Evaluate(ext, 0f, false, true, false));
            Assert.AreEqual(ExtractionBlock.None, ExtractionRules.Evaluate(ext, 0f, false, false, false), "배낭 없음(조끼는 판정 대상 아님)");
        }

        [Test]
        public void ConditionOrder_Power_Time_Backpack_Item()
        {
            // 모든 조건을 가진 가상의 탈출구로 우선순위 확인
            ExtractionPointConfig all = M4TestData.Extraction("EXT-X", "모두", 5f, 5f,
                requiredSwitchId: M4TestData.PowerSwitchId, availableAfterSec: 900f, consumedItem: M4TestData.Flare(),
                forbidsBackpack: true);

            Assert.AreEqual(ExtractionBlock.NoPower, ExtractionRules.Evaluate(all, 0f, false, true, false));
            Assert.AreEqual(ExtractionBlock.TooEarly, ExtractionRules.Evaluate(all, 0f, true, true, false));
            Assert.AreEqual(ExtractionBlock.BackpackEquipped, ExtractionRules.Evaluate(all, 900f, true, true, false));
            Assert.AreEqual(ExtractionBlock.MissingItem, ExtractionRules.Evaluate(all, 900f, true, false, false));
            Assert.AreEqual(ExtractionBlock.None, ExtractionRules.Evaluate(all, 900f, true, false, true));
        }
    }
}
