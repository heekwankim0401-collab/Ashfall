using System.Collections.Generic;
using System.Linq;
using Ashfall.Core;
using Ashfall.Inventory;
using Ashfall.Raid;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // 맵 밀도 ⓪ 계약 (docs/design/MapDensity_design.md §5.3·§10): SeedMath, RaidStarted.LootSeed, LootActivationPlanner, 새 SO 기본값.
    public class MapDensityContractTests
    {
        // ── SeedMath ──

        [Test]
        public void StableHash_FixedValues_PlatformIndependent()
        {
            // FNV-1a 32비트, UTF-16 코드 단위 하위·상위 바이트 (string.GetHashCode와 무관한 고정값)
            Assert.AreEqual(-2128831035, SeedMath.StableHash(""));
            Assert.AreEqual(-2128831035, SeedMath.StableHash(null));
            Assert.AreEqual(256786917, SeedMath.StableHash("LOOT"));
            Assert.AreEqual(751508133, SeedMath.StableHash("POI-HWY|Crate"));
            Assert.AreNotEqual(SeedMath.StableHash("Crate_POI-HWY_01"), SeedMath.StableHash("Crate_POI-HWY_02"));
        }

        [Test]
        public void Mix_IsDeterministic_OrderSensitive_NeverZero()
        {
            Assert.AreEqual(SeedMath.Mix(42, 7), SeedMath.Mix(42, 7));
            Assert.AreNotEqual(SeedMath.Mix(42, 7), SeedMath.Mix(7, 42));
            for (int a = -50; a <= 50; a++)
            for (int b = -50; b <= 50; b++)
                Assert.AreNotEqual(0, SeedMath.Mix(a, b));
            Assert.AreEqual(1, SeedMath.NonZero(0));
            Assert.AreEqual(5, SeedMath.NonZero(5));
        }

        // ── RaidStarted ──

        [Test]
        public void RaidStarted_OldConstructor_LootSeedZero_NewConstructorKeepsSeed()
        {
            var old = new RaidStarted("SP-W", 1500f);
            Assert.AreEqual("SP-W", old.SpawnId);
            Assert.AreEqual(1500f, old.TimeLimitSec);
            Assert.AreEqual(0, old.LootSeed, "기존 생성자 = 시드 없음");
            var seeded = new RaidStarted("SP-E", 1200f, 1234);
            Assert.AreEqual("SP-E", seeded.SpawnId);
            Assert.AreEqual(1234, seeded.LootSeed);
        }

        // ── LootActivationPlanner ──

        private static List<LootActivationPlanner.Slot> Slots(string zone, LootSlotKind kind, int n, string prefix = null)
        {
            var list = new List<LootActivationPlanner.Slot>();
            for (int i = 0; i < n; i++)
                list.Add(new LootActivationPlanner.Slot($"{prefix ?? zone + kind}_{i:000}", zone, kind, false));
            return list;
        }

        private static float? Ratio(string zone, LootSlotKind kind) =>
            zone == "POI-HWY" ? (kind == LootSlotKind.Crate ? 0.4f : 0.3f) : zone == "ROUTE" ? 0.3f : (float?)null;

        private static HashSet<string> ActiveSet(LootActivationPlanner.Decision[] decisions) =>
            new HashSet<string>(decisions.Where(d => d.Active).Select(d => d.Key));

        [Test]
        public void Plan_SameSeed_SameResult_DifferentSeed_UsuallyDifferent()
        {
            var slots = Slots("POI-HWY", LootSlotKind.Crate, 14).Concat(Slots("ROUTE", LootSlotKind.Crate, 36)).ToList();
            var a = LootActivationPlanner.Plan(slots, Ratio, 1, 99);
            var b = LootActivationPlanner.Plan(slots, Ratio, 1, 99);
            CollectionAssert.AreEqual(a.Select(d => d.Active), b.Select(d => d.Active));
            CollectionAssert.AreEqual(a.Select(d => d.ContentSeed), b.Select(d => d.ContentSeed));
            HashSet<string> first = ActiveSet(a);
            int same = 0;
            for (int seed = 1; seed <= 1000; seed++)
                if (ActiveSet(LootActivationPlanner.Plan(slots, Ratio, 1, seed)).SetEquals(first))
                    same++;
            Assert.Less(same, 20, "1000 seed 중 같은 집합이 너무 많다");
        }

        [Test]
        public void Plan_ExactCountPerZoneAndKind_MinimumOne_AlwaysActiveExtra()
        {
            var slots = Slots("POI-HWY", LootSlotKind.Crate, 14).Concat(Slots("POI-HWY", LootSlotKind.Trunk, 3))
                .Concat(Slots("ROUTE", LootSlotKind.Crate, 36)).Concat(Slots("ROUTE", LootSlotKind.Trunk, 1)).ToList();
            slots.Add(new LootActivationPlanner.Slot("FlareCrate_POI-HWY_01", "POI-HWY", LootSlotKind.Crate, true));
            for (int seed = 1; seed <= 50; seed++)
            {
                LootActivationPlanner.Decision[] plan = LootActivationPlanner.Plan(slots, Ratio, 1, seed);
                int Active(string prefix) => plan.Count(d => d.Active && d.Key.StartsWith(prefix));
                Assert.AreEqual(6, Active("POI-HWYCrate_"), "round(14 × 0.4) = 6");
                Assert.AreEqual(1, Active("POI-HWYTrunk_"), "round(3 × 0.3) = 1");
                Assert.AreEqual(11, Active("ROUTECrate_"), "round(36 × 0.3) = 11");
                Assert.AreEqual(1, Active("ROUTETrunk_"), "round(1 × 0.3) = 0 → 최소 1");
                Assert.IsTrue(plan.Single(d => d.Key == "FlareCrate_POI-HWY_01").Active, "신호탄 상자는 항상 활성");
                Assert.IsTrue(plan.All(d => d.ContentSeed != 0), "내용물 시드 0 아님");
            }
        }

        [Test]
        public void Plan_IndependentOfInputOrder()
        {
            var slots = Slots("POI-HWY", LootSlotKind.Crate, 14).Concat(Slots("ROUTE", LootSlotKind.Trunk, 22)).ToList();
            var reversed = Enumerable.Reverse(slots).ToList();
            CollectionAssert.AreEquivalent(ActiveSet(LootActivationPlanner.Plan(slots, Ratio, 1, 7)),
                ActiveSet(LootActivationPlanner.Plan(reversed, Ratio, 1, 7)));
        }

        [Test]
        public void ActiveCount_Boundaries()
        {
            Assert.AreEqual(0, LootActivationPlanner.ActiveCount(0, 0.5f, 1), "지점 없음");
            Assert.AreEqual(0, LootActivationPlanner.ActiveCount(10, null, 1), "구역 설정 없음");
            Assert.AreEqual(1, LootActivationPlanner.ActiveCount(10, 0f, 1), "비율 0 → 최소");
            Assert.AreEqual(0, LootActivationPlanner.ActiveCount(10, 0f, 0));
            Assert.AreEqual(10, LootActivationPlanner.ActiveCount(10, 1f, 1), "비율 1 = 전부");
            Assert.AreEqual(10, LootActivationPlanner.ActiveCount(10, 1.5f, 1), "비율 상한");
            Assert.AreEqual(3, LootActivationPlanner.ActiveCount(5, 0.5f, 1), "2.5 → 3 (반올림)");
            Assert.AreEqual(2, LootActivationPlanner.ActiveCount(2, 0.1f, 3), "최소가 n보다 크면 n");
        }

        [Test]
        public void GroupKey_SeparatorInZoneId_StaysDistinct()
        {
            Assert.AreNotEqual(LootActivationPlanner.GroupKey("A|Crate", LootSlotKind.Trunk), LootActivationPlanner.GroupKey("A", LootSlotKind.Crate));
            Assert.AreNotEqual(LootActivationPlanner.GroupKey("A|Trunk|", LootSlotKind.Crate), LootActivationPlanner.GroupKey("A|Trunk", LootSlotKind.Crate));
            Assert.AreEqual("7:POI-HWY|Crate", LootActivationPlanner.GroupKey("POI-HWY", LootSlotKind.Crate));
        }

        // 설계 §14.7: 비율 0이어도 지점이 있으면 minActivePerZone이 적용된다 — 끄려면 구역을 설정에서 뺀다(구역 없음 = 0)
        [Test]
        public void Plan_ZeroRatio_StillMinimum_RemovingZoneDisables()
        {
            var slots = Slots("ROUTE", LootSlotKind.Crate, 10);
            Assert.AreEqual(1, LootActivationPlanner.Plan(slots, (z, k) => 0f, 1, 5).Count(d => d.Active));
            Assert.AreEqual(0, LootActivationPlanner.Plan(slots, (z, k) => null, 1, 5).Count(d => d.Active));
        }

        [Test]
        public void Plan_UnknownZone_Inactive()
        {
            LootActivationPlanner.Decision[] plan = LootActivationPlanner.Plan(Slots("NOWHERE", LootSlotKind.Crate, 5), Ratio, 1, 3);
            Assert.IsTrue(plan.All(d => !d.Active));
        }

        // ── 새 SO 기본값 (설계 §5.1) ──

        [Test]
        public void LootSpawnConfig_Defaults()
        {
            var cfg = ScriptableObject.CreateInstance<LootSpawnConfigSO>();
            try
            {
                Assert.AreEqual(1, cfg.minActivePerZone);
                Assert.AreEqual(InactiveLootMode.Empty, cfg.inactiveMode, "Q1 기본안 A");
                Assert.AreEqual(new Vector2Int(6, 4), cfg.crateGridSize, "M4 상자와 같은 그리드");
                Assert.AreEqual(new Vector2Int(5, 3), cfg.trunkGridSize);
                Assert.AreEqual("보급 상자", cfg.crateDisplayName);
                Assert.AreEqual("차량 트렁크", cfg.trunkDisplayName);
                cfg.zones = new[] { new LootZoneConfig { zoneId = "ROUTE", crateActiveRatio = 0.3f, trunkActiveRatio = 0.25f } };
                Assert.AreEqual(0.25f, cfg.RatioOf("ROUTE", LootSlotKind.Trunk));
                Assert.IsNull(cfg.RatioOf("POI-HWY", LootSlotKind.Crate));
            }
            finally
            {
                Object.DestroyImmediate(cfg);
            }
        }

        [Test]
        public void MapDensityConfig_Defaults_MatchDesign()
        {
            var cfg = ScriptableObject.CreateInstance<MapDensityConfigSO>();
            try
            {
                Assert.AreEqual(1500, cfg.treeCount);
                Assert.AreEqual(5f, cfg.treeMinSpacingM);
                Assert.AreEqual(90f, cfg.treeGroveNoiseScaleM);
                Assert.AreEqual(0.45f, cfg.treeGroveThreshold);
                Assert.AreEqual(new Vector2(0.8f, 1.3f), cfg.treeScaleRange);
                Assert.AreEqual(30f, cfg.treeMaxSlopeDeg);
                Assert.AreEqual(400, cfg.bushCount);
                Assert.AreEqual(120, cfg.logCount);
                Assert.AreEqual(1.0f, cfg.bushMaxHeightM);
                Assert.AreEqual(0.4f, cfg.grassCoverage);
                Assert.AreEqual(1.2f, cfg.grassMaxPerM2);
                Assert.AreEqual(50f, cfg.grassDistanceM);
                Assert.AreEqual(new Vector2(0.3f, 0.8f), cfg.grassHeightRange);
                Assert.AreEqual(1024, cfg.detailResolution);
                Assert.AreEqual(32, cfg.detailResolutionPerPatch);
                Assert.AreEqual(60f, cfg.treeLod1DistanceM);
                Assert.AreEqual(260f, cfg.treeCullDistanceM);
                Assert.AreEqual(90f, cfg.bushCullDistanceM);
                Assert.AreEqual(120f, cfg.logCullDistanceM);
                Assert.AreEqual(180f, cfg.smallStructureCullDistanceM);
                Assert.AreEqual(300f, cfg.carCullDistanceM);
                Assert.AreEqual(450f, cfg.buildingCullDistanceM);
                Assert.AreEqual(120f, cfg.crateCullDistanceM);
                Assert.AreEqual(1.5f, cfg.shadowMinHeightM);
                Assert.IsFalse(cfg.treeLod1Shadows);
                Assert.AreEqual(700, cfg.maxNewCoverPoints);
                Assert.AreEqual(1200, cfg.maxTotalCoverPoints);
            }
            finally
            {
                Object.DestroyImmediate(cfg);
            }
        }
    }
}
