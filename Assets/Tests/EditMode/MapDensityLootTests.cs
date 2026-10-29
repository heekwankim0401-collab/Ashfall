using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Ashfall.Core;
using Ashfall.EditorTools;
using Ashfall.Inventory;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Ashfall.Tests.EditMode
{
    // 맵 밀도 ③ 상자·트렁크·루트 (docs/design/MapDensity_design.md §5·§6·§9·§10, SYS-INV §6 루트 컨테이너):
    // 레이드 루트 시드, LootContainer 레이드 설정(빈 컨테이너 Q1 A), 지휘자 계획·적용(결정적), 상자 배치 생성기, 데이터(LootSpawnConfig·LT-CRATE-LOW·LT-TRUNK), 경제 시뮬레이션(Q2 A).
    public class MapDensityLootTests
    {
        private readonly List<Object> created = new List<Object>();
        private ItemDefinitionSO flare, wires, pipe;
        private LootTableSO poiTable, lowTable, trunkTable, junkTable;

        [SetUp]
        public void SetUp()
        {
            flare = Track(M4TestData.Flare());
            wires = Track(TestData.Wires());
            pipe = Track(TestData.Pipe());
            poiTable = Table("LT-POI", (wires, 1f), (flare, 1f));
            lowTable = Table("LT-LOW", (pipe, 1f));
            trunkTable = Table("LT-TRK", (wires, 1f), (pipe, 1f));
            junkTable = Table("LT-JUNK", (pipe, 1f));
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in created)
                if (o != null)
                    Object.DestroyImmediate(o);
            created.Clear();
        }

        private T Track<T>(T obj) where T : Object
        {
            created.Add(obj);
            return obj;
        }

        private LootTableSO Table(string id, params (ItemDefinitionSO item, float weight)[] entries)
        {
            var t = Track(ScriptableObject.CreateInstance<LootTableSO>());
            t.id = id;
            t.entries = entries.Select(e => new LootEntry { item = e.item, weight = e.weight }).ToList();
            t.rollCount = new Vector2Int(2, 3);
            return t;
        }

        private LootSpawnConfigSO Config(InactiveLootMode mode = InactiveLootMode.Empty)
        {
            var cfg = Track(ScriptableObject.CreateInstance<LootSpawnConfigSO>());
            cfg.zones = new[]
            {
                new LootZoneConfig { zoneId = "POI-HWY", crateActiveRatio = 0.4f, trunkActiveRatio = 0.5f, crateTable = poiTable, trunkTable = trunkTable },
                new LootZoneConfig { zoneId = LootZoneRules.Route, crateActiveRatio = 0.3f, trunkActiveRatio = 0.3f, crateTable = lowTable, trunkTable = trunkTable },
            };
            cfg.inactiveMode = mode;
            cfg.inactiveTable = junkTable;
            return cfg;
        }

        private LootContainer NewContainer(string key, string zone, LootSlotKind kind, bool always, ItemDefinitionSO[] contents = null, LootTableSO table = null)
        {
            var go = Track(new GameObject(key));
            var c = go.AddComponent<LootContainer>();
            var so = new SerializedObject(c);
            so.FindProperty("displayName").stringValue = kind == LootSlotKind.Trunk ? "차량 트렁크" : "보급 상자";
            so.FindProperty("gridSize").vector2IntValue = new Vector2Int(6, 4);
            SerializedProperty list = so.FindProperty("contents");
            contents ??= new ItemDefinitionSO[0];
            list.arraySize = contents.Length;
            for (int i = 0; i < contents.Length; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = contents[i];
            so.FindProperty("lootTable").objectReferenceValue = table;
            so.ApplyModifiedPropertiesWithoutUndo();
            var slot = go.AddComponent<LootSpawnSlot>();
            var sso = new SerializedObject(slot);
            sso.FindProperty("slotKey").stringValue = key;
            sso.FindProperty("zoneId").stringValue = zone;
            sso.FindProperty("kind").enumValueIndex = (int)kind;
            sso.FindProperty("alwaysActive").boolValue = always;
            sso.ApplyModifiedPropertiesWithoutUndo();
            return c;
        }

        private LootSpawnDirector NewDirector(LootSpawnConfigSO cfg)
        {
            var go = Track(new GameObject("LootSpawnDirector"));
            var d = go.AddComponent<LootSpawnDirector>();
            var so = new SerializedObject(d);
            so.FindProperty("config").objectReferenceValue = cfg;
            so.ApplyModifiedPropertiesWithoutUndo();
            return d;
        }

        // 구역 HWY 상자 14 + 트렁크 4 + 신호탄 1, ROUTE 상자 10 + 트렁크 6
        private List<LootContainer> World()
        {
            var list = new List<LootContainer>();
            for (int i = 1; i <= 14; i++)
                list.Add(NewContainer($"LootCrate_POI-HWY_{i:000}", "POI-HWY", LootSlotKind.Crate, false, table: poiTable));
            for (int i = 1; i <= 4; i++)
                list.Add(NewContainer($"Trunk_POI-HWY_{i:000}", "POI-HWY", LootSlotKind.Trunk, false));
            list.Add(NewContainer("FlareCrate_POI-HWY_01", "POI-HWY", LootSlotKind.Crate, true, new[] { flare }));
            for (int i = 1; i <= 10; i++)
                list.Add(NewContainer($"LootCrate_ROUTE_{i:000}", LootZoneRules.Route, LootSlotKind.Crate, false));
            for (int i = 1; i <= 6; i++)
                list.Add(NewContainer($"Trunk_ROUTE_{i:000}", LootZoneRules.Route, LootSlotKind.Trunk, false));
            return list;
        }

        private static List<LootSpawnSlot> Slots(List<LootContainer> containers) => containers.Select(c => c.GetComponent<LootSpawnSlot>()).ToList();

        private static List<string> Contents(LootContainer c) => c.EnsureModel(0.5f).Items.Select(p => p.Item.Definition.id).ToList();

        // ── 레이드 루트 시드 (RaidController 한 줄) ──

        [Test]
        public void RaidLootSeed_DerivedFromRaidSeed_Deterministic_NonZero()
        {
            Assert.AreEqual(SeedMath.Mix(1234, SeedMath.StableHash("LOOT")), SeedMath.RaidLootSeed(1234), "설계 §5.3 생성식");
            Assert.AreEqual(SeedMath.RaidLootSeed(77), SeedMath.RaidLootSeed(77));
            Assert.AreNotEqual(SeedMath.RaidLootSeed(77), SeedMath.RaidLootSeed(78));
            for (int s = -100; s <= 100; s++)
                Assert.AreNotEqual(0, SeedMath.RaidLootSeed(s), "0 = 발행자가 주지 않음과 섞이지 않게");
        }

        // ── LootContainer 레이드 설정 ──

        [Test]
        public void ConfigureForRaid_BeforeOpen_SetsTableAndSeed_KeepsFixed_AfterOpenIgnored()
        {
            LootContainer a = NewContainer("A", "POI-HWY", LootSlotKind.Crate, false, new[] { flare });
            Assert.IsTrue(a.ConfigureForRaid(poiTable, 0));
            Assert.AreEqual(1, a.Seed, "시드 0은 1로 (0 = 매번 무작위와 섞이지 않게)");
            Assert.IsTrue(a.ConfigureForRaid(poiTable, 42));
            Assert.AreSame(poiTable, a.LootTable);
            List<string> first = Contents(a);
            Assert.AreEqual(flare.id, first[0], "고정 내용물(신호탄)이 먼저 그대로");
            Assert.IsFalse(a.ConfigureForRaid(lowTable, 7), "열린 뒤에는 무시");
            Assert.IsFalse(a.ConfigureEmpty(), "열린 뒤에는 무시");
            CollectionAssert.AreEqual(first, Contents(a));

            LootContainer b = NewContainer("B", "POI-HWY", LootSlotKind.Crate, false, new[] { flare });
            b.ConfigureForRaid(poiTable, 42);
            CollectionAssert.AreEqual(first, Contents(b), "같은 테이블·시드 = 같은 내용물");
        }

        [Test]
        public void ConfigureEmpty_OpensEmpty_SearchCompleteImmediately_SerializedFieldsKept()
        {
            LootContainer c = NewContainer("E", "ROUTE", LootSlotKind.Crate, false, new[] { flare }, poiTable);
            Assert.IsTrue(c.ConfigureEmpty());
            Assert.IsTrue(c.IsEmptyForRaid);
            Assert.IsNull(c.LootTable, "이번 레이드에 쓸 테이블 없음");
            // 리뷰 L1: 직렬화 원본은 그대로 (지휘자를 다시 적용하거나 씬을 다시 불러도 원본이 남는다)
            Assert.AreSame(poiTable, c.SerializedLootTable);
            CollectionAssert.AreEqual(new[] { flare }, c.FixedContents.ToArray());
            var so = new SerializedObject(c);
            Assert.AreSame(poiTable, so.FindProperty("lootTable").objectReferenceValue);
            Assert.AreEqual(1, so.FindProperty("contents").arraySize);
            LootContainerModel model = c.EnsureModel(0.5f);
            Assert.AreEqual(0, model.Items.Count, "빈 컨테이너 (Q1 A)");
            Assert.IsTrue(model.SearchComplete, "검색할 것이 없어 바로 끝");
            Assert.AreEqual(new Vector2Int(6, 4), model.Size, "그리드·이름은 그대로");
            Assert.AreSame(poiTable, c.SerializedLootTable, "열린 뒤에도 원본 그대로");

            // 다시 적용(다른 레이드 = 새로 불린 컴포넌트)해도 원본에서 시작: 활성이면 고정 내용물·테이블이 살아 있다
            LootContainer again = NewContainer("E2", "ROUTE", LootSlotKind.Crate, false, new[] { flare }, poiTable);
            again.ConfigureEmpty();
            Assert.IsTrue(again.ConfigureEmpty(), "열기 전 다시 적용 가능");
            Assert.AreSame(poiTable, again.SerializedLootTable);
            LootContainer active = NewContainer("E3", "ROUTE", LootSlotKind.Crate, false, new[] { flare }, poiTable);
            active.ConfigureForRaid(active.SerializedLootTable, 9);
            Assert.AreEqual(flare.id, Contents(active)[0], "활성이면 고정 내용물 그대로");
        }

        [Test]
        public void ConfigureEmptyThenTable_JunkOnly_SkipsFixed_KeepsSerialized()
        {
            LootContainer c = NewContainer("J", "ROUTE", LootSlotKind.Crate, false, new[] { flare }, poiTable);
            Assert.IsTrue(LootSpawnAssignment.Apply(c, new LootSpawnAssignment.Result(LootSpawnAssignment.Mode.EmptyThenTable, junkTable, 11)));
            Assert.IsFalse(c.IsEmptyForRaid);
            Assert.AreSame(junkTable, c.LootTable);
            Assert.AreSame(poiTable, c.SerializedLootTable, "직렬화 원본 그대로");
            List<string> items = Contents(c);
            Assert.IsFalse(items.Contains(flare.id), "JunkOnly는 고정 내용물(신호탄)을 쓰지 않는다");
            Assert.IsTrue(items.All(id => id == pipe.id), "비활성 테이블 굴림만");
        }

        // ── 배정 규칙 ──

        [Test]
        public void Resolve_AlwaysActive_Active_Inactive_JunkOnly()
        {
            LootSpawnConfigSO cfg = Config();
            var always = new LootActivationPlanner.Slot("F", "POI-HWY", LootSlotKind.Crate, true);
            var crate = new LootActivationPlanner.Slot("C", "POI-HWY", LootSlotKind.Crate, false);
            var trunk = new LootActivationPlanner.Slot("T", "ROUTE", LootSlotKind.Trunk, false);
            var on = new LootActivationPlanner.Decision("x", true, 99);
            var off = new LootActivationPlanner.Decision("x", false, 99);

            LootSpawnAssignment.Result r = LootSpawnAssignment.Resolve(always, on, cfg, null);
            Assert.AreEqual(LootSpawnAssignment.Mode.Table, r.Mode);
            Assert.IsNull(r.Table, "신호탄 상자 = 직렬화 값(테이블 없음) 그대로");
            Assert.AreEqual(99, r.Seed);
            Assert.AreSame(poiTable, LootSpawnAssignment.Resolve(crate, on, cfg, lowTable).Table, "활성 = 구역 테이블");
            Assert.AreSame(trunkTable, LootSpawnAssignment.Resolve(trunk, on, cfg, null).Table);
            Assert.AreSame(lowTable, LootSpawnAssignment.Resolve(new LootActivationPlanner.Slot("N", "NOWHERE", LootSlotKind.Crate, false), on, cfg, lowTable).Table,
                "구역 설정이 없으면 직렬화 테이블");
            Assert.AreEqual(LootSpawnAssignment.Mode.Empty, LootSpawnAssignment.Resolve(crate, off, cfg, poiTable).Mode, "비활성 = 빈 컨테이너");

            LootSpawnConfigSO junk = Config(InactiveLootMode.JunkOnly);
            r = LootSpawnAssignment.Resolve(crate, off, junk, poiTable);
            Assert.AreEqual(LootSpawnAssignment.Mode.EmptyThenTable, r.Mode);
            Assert.AreSame(junkTable, r.Table);
        }

        // ── 지휘자 (활성 판정은 director.WasActive — 리뷰 L2: JunkOnly여도 깨지지 않게) ──

        [Test]
        public void Director_SameSeed_ShuffledInput_SameActiveSetAndContents_CountsMatchPlan()
        {
            LootSpawnConfigSO cfg = Config();
            List<LootContainer> a = World(), b = World();
            LootSpawnDirector da = NewDirector(cfg), db = NewDirector(cfg);
            int activeA = da.Apply(SeedMath.RaidLootSeed(5), Slots(a));
            // 리뷰 L3: b는 입력 순서를 섞어 넘긴다 (계획은 slotKey 서수 정렬 — 씬 순서와 무관)
            List<LootSpawnSlot> shuffled = Slots(b);
            var rng = new System.Random(123);
            for (int i = shuffled.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
            }
            int activeB = db.Apply(SeedMath.RaidLootSeed(5), shuffled);
            Assert.AreEqual(activeA, activeB);
            // round(14×0.4)=6 + round(4×0.5)=2 + 신호탄 1 + round(10×0.3)=3 + round(6×0.3)=2
            Assert.AreEqual(6 + 2 + 1 + 3 + 2, activeA);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.AreEqual(da.WasActive(a[i].name), db.WasActive(b[i].name), a[i].name);
                CollectionAssert.AreEqual(Contents(a[i]), Contents(b[i]), $"{a[i].name}: 같은 시드 = 같은 내용물");
            }
            LootContainer flareCrate = a.Single(c => c.name.StartsWith("FlareCrate_"));
            Assert.AreEqual(true, da.WasActive(flareCrate.name), "신호탄 상자 항상 활성");
            Assert.Contains(flare.id, Contents(flareCrate));
            foreach (LootContainer c in a.Where(c => da.WasActive(c.name) == false))
            {
                Assert.IsTrue(c.IsEmptyForRaid, $"{c.name}: 비활성(Empty 모드) = 빈 컨테이너");
                Assert.AreEqual(0, c.Model.Items.Count, $"{c.name}: 비활성 = 빈 컨테이너");
            }
            Assert.AreEqual(a.Count(c => da.WasActive(c.name) == true), activeA);
            Assert.IsTrue(a.Where(c => c.name.StartsWith("LootCrate_POI-HWY") && da.WasActive(c.name) == true).All(c => c.LootTable == poiTable));
            Assert.IsTrue(a.Where(c => c.name.StartsWith("Trunk_") && da.WasActive(c.name) == true).All(c => c.LootTable == trunkTable));
        }

        [Test]
        public void Director_DifferentSeeds_DifferentSets_OpenedSkipped()
        {
            LootSpawnConfigSO cfg = Config();
            var sets = new HashSet<string>();
            for (int seed = 1; seed <= 20; seed++)
            {
                List<LootContainer> w = World();
                LootSpawnDirector d = NewDirector(cfg);
                d.Apply(SeedMath.RaidLootSeed(seed), Slots(w));
                sets.Add(string.Join(",", w.Where(c => d.WasActive(c.name) == true).Select(c => c.name)));
                foreach (LootContainer c in w)
                    Object.DestroyImmediate(c.gameObject);
            }
            Assert.Greater(sets.Count, 15, "레이드마다 다른 집합");

            List<LootContainer> world = World();
            LootContainer opened = world[0];
            opened.EnsureModel(0.5f);
            LogAssert.Expect(LogType.Warning, new Regex("이미 열린 컨테이너"));
            LootSpawnDirector director = NewDirector(cfg);
            director.Apply(SeedMath.RaidLootSeed(3), Slots(world));
            Assert.AreEqual(1, director.LastSkippedOpened);
            Assert.IsFalse(opened.IsEmptyForRaid, "연 컨테이너는 건드리지 않는다");
            Assert.AreEqual(director.LastActiveCount, world.Count(c => director.WasActive(c.name) == true));
        }

        // 리뷰 L3: RaidStarted.LootSeed 0(발행자가 시드를 주지 않음) → 지휘자는 아무것도 바꾸지 않는다(직렬화 값 그대로)
        [Test]
        public void Director_ZeroLootSeed_LeavesContainersUntouched()
        {
            List<LootContainer> world = World();
            LootSpawnDirector director = NewDirector(Config());
            Assert.IsFalse(director.HandleRaidStarted(new RaidStarted("SP-W", 1500f)), "기존 생성자 = LootSeed 0");
            Assert.AreEqual(0, director.LastSlotCount);
            Assert.IsNull(director.WasActive(world[0].name));
            Assert.IsTrue(world.All(c => !c.IsEmptyForRaid));
            Assert.AreSame(poiTable, world[0].LootTable, "직렬화 테이블 그대로");
            Assert.AreEqual(0, world[0].Seed, "직렬화 시드(0 = 매번 무작위) 그대로");
        }

        [Test]
        public void Director_NoConfig_LogsError_LeavesContainers()
        {
            List<LootContainer> world = World();
            LogAssert.Expect(LogType.Error, new Regex("LootSpawnConfigSO가 연결되지 않아"));
            Assert.AreEqual(0, NewDirector(null).Apply(SeedMath.RaidLootSeed(1), Slots(world)));
            Assert.IsTrue(world.All(c => !c.IsEmptyForRaid));
            Assert.AreSame(poiTable, world[0].LootTable);
        }

        // ── 상자 배치 생성기 (순수) ──

        private static List<MapDensityLootLayout.Wall> Walls()
        {
            var walls = new List<MapDensityLootLayout.Wall>();
            int i = 0;
            foreach (float x in new[] { -180f, -120f, -60f, 60f, 120f, 180f })
                foreach (float z in new[] { 120f, 180f })
                    walls.Add(new MapDensityLootLayout.Wall($"W{i++}", new Footprint(new Vector2(x, z), new Vector2(3f, 0.25f), 15f)));
            return walls;
        }

        private static MapDensityLayout.SiteContext Context(List<MapDensityLootLayout.Wall> walls)
        {
            var ctx = new MapDensityLayout.SiteContext();
            foreach (MapDensityLootLayout.Wall w in walls)
                ctx.AddObstacle(w.Footprint);
            return ctx;
        }

        [Test]
        public void CandidatesFor_FaceOffsetAndAccessOutward()
        {
            var wall = new MapDensityLootLayout.Wall("W", new Footprint(Vector2.zero, new Vector2(3f, 0.25f), 0f));
            List<MapDensityLootLayout.CratePlan> c = MapDensityLootLayout.CandidatesFor(wall);
            Assert.AreEqual(6, c.Count, "긴 면 2 × (6m − 끝 0.6m×2) / 2m 간격 = 3개씩, 짧은 면(0.5m)은 없음");
            foreach (MapDensityLootLayout.CratePlan p in c)
            {
                Assert.AreEqual(0.25f + MapDensityLootLayout.CrateFaceOffsetM, Mathf.Abs(p.XZ.y), 1e-4f, "면 앞 0.9m");
                Assert.AreEqual(MapDensityLootLayout.AccessDistanceM, Vector2.Distance(p.XZ, p.AccessXZ), 1e-4f);
                Assert.Greater(Mathf.Abs(p.AccessXZ.y), Mathf.Abs(p.XZ.y), "접근점은 벽 반대쪽(열린 면)");
                Assert.Greater(wall.Footprint.Distance(p.Footprint.Center) - MapDensityLootLayout.CrateSizeM * 0.5f, MapDensityLootLayout.CrateObstacleGapM,
                    "붙은 벽과도 간격 규칙(0.3m)을 지킨다");
            }
        }

        [Test]
        public void GenerateCrates_Deterministic_CountsSpacingAndNames()
        {
            List<MapDensityLootLayout.Wall> walls = Walls();
            var want = new Dictionary<string, int> { { LootZoneRules.Route, 5 } };
            var existing = new List<Vector2> { new Vector2(-180f, 121.5f) };
            List<MapDensityLootLayout.CratePlan> a = MapDensityLootLayout.GenerateCrates(Context(walls), walls, want, existing, null, out string report);
            List<MapDensityLootLayout.CratePlan> b = MapDensityLootLayout.GenerateCrates(Context(walls), walls, want, existing, null, out _);
            Assert.AreEqual(5, a.Count, report);
            CollectionAssert.AreEqual(a.Select(p => (p.Name, p.XZ)), b.Select(p => (p.Name, p.XZ)), "같은 입력 = 같은 결과");
            CollectionAssert.AreEqual(Enumerable.Range(1, 5).Select(i => MapDensityLootLayout.CrateName(LootZoneRules.Route, i)), a.Select(p => p.Name));
            foreach (MapDensityLootLayout.CratePlan p in a)
            {
                Assert.AreEqual(LootZoneRules.Route, p.Zone);
                foreach (Vector2 e in existing)
                    Assert.GreaterOrEqual(Vector2.Distance(p.XZ, e), MapDensityLootLayout.CrateMinSpacingM, "기존 컨테이너와 간격");
                foreach (MapDensityLootLayout.CratePlan q in a)
                    if (q != p)
                        Assert.GreaterOrEqual(Vector2.Distance(p.XZ, q.XZ), MapDensityLootLayout.CrateMinSpacingM);
            }
            Assert.LessOrEqual(a.GroupBy(p => p.Source).Max(g => g.Count()), 1, "후보가 넉넉하면 구조물 하나에 1개");
        }

        [Test]
        public void GenerateCrates_AccessCallbackRejects_AndShortfallReported()
        {
            List<MapDensityLootLayout.Wall> walls = Walls();
            var want = new Dictionary<string, int> { { LootZoneRules.Route, 3 } };
            List<MapDensityLootLayout.CratePlan> none = MapDensityLootLayout.GenerateCrates(Context(walls), walls, want, new List<Vector2>(), _ => false, out string report);
            Assert.AreEqual(0, none.Count);
            StringAssert.Contains("ROUTE: 0/3", report);
            List<MapDensityLootLayout.CratePlan> some = MapDensityLootLayout.GenerateCrates(Context(walls), walls, want, new List<Vector2>(), p => p.XZ.x > 0f, out _);
            Assert.IsTrue(some.All(p => p.XZ.x > 0f), "접근 확인 실패 후보는 고르지 않는다");
        }

        // ── 데이터 (Ashfall/Setup/Create Map Density Data) ──

        private static LootSpawnConfigSO LoadConfig()
        {
            var cfg = AssetDatabase.LoadAssetAtPath<LootSpawnConfigSO>(MapDensityDataSetup.LootSpawnConfigPath);
            Assert.IsNotNull(cfg, $"{MapDensityDataSetup.LootSpawnConfigPath} 없음 (Ashfall/Setup/Create Map Density Data)");
            return cfg;
        }

        [Test]
        public void LootSpawnConfig_ZonesTargetsTables_MatchSetup()
        {
            LootSpawnConfigSO cfg = LoadConfig();
            CollectionAssert.AreEqual(LootZoneRules.AllZoneIds().OrderBy(z => z), cfg.zones.Select(z => z.zoneId).OrderBy(z => z), "POI 5 + ROUTE + OUTSKIRT");
            Assert.AreEqual(133, cfg.zones.Sum(z => z.crateTargetCount), "상자 = 기존 21 + 새 112");
            Assert.AreEqual(70, cfg.zones.Sum(z => z.trunkTargetCount), "트렁크 = 승용차 70");
            Assert.AreEqual(InactiveLootMode.Empty, cfg.inactiveMode, "Q1 A 빈 컨테이너");
            Assert.AreEqual(1, cfg.minActivePerZone);
            Assert.AreEqual(new Vector2Int(6, 4), cfg.crateGridSize);
            Assert.AreEqual(new Vector2Int(5, 3), cfg.trunkGridSize);
            var crate = AssetDatabase.LoadAssetAtPath<LootTableSO>(M4DataSetup.LootCratePath);
            var low = AssetDatabase.LoadAssetAtPath<LootTableSO>(MapDensityDataSetup.CrateLowTablePath);
            var trunk = AssetDatabase.LoadAssetAtPath<LootTableSO>(MapDensityDataSetup.TrunkTablePath);
            foreach (LootZoneConfig z in cfg.zones)
            {
                Assert.AreSame(LootZoneRules.IsPoi(z.zoneId) ? crate : low, z.crateTable, z.zoneId);
                Assert.AreSame(trunk, z.trunkTable, z.zoneId);
                var spec = MapDensityDataSetup.Zones.Single(s => s.Zone == z.zoneId);
                Assert.AreEqual(spec.Crates, z.crateTargetCount, z.zoneId);
                Assert.AreEqual(spec.Trunks, z.trunkTargetCount, z.zoneId);
                Assert.AreEqual(spec.CrateRatio, z.crateActiveRatio, 1e-5f, z.zoneId);
                Assert.AreEqual(spec.TrunkRatio, z.trunkActiveRatio, 1e-5f, z.zoneId);
            }
        }

        [Test]
        public void NewLootTables_NoNullEntries_FlareOnlyInCrateTable()
        {
            var flareDef = AssetDatabase.LoadAssetAtPath<ItemDefinitionSO>(M4DataSetup.FlarePath);
            foreach ((string path, string id, Vector2Int rolls, int count) in new[]
                     {
                         (MapDensityDataSetup.CrateLowTablePath, MapDensityDataSetup.CrateLowId, MapDensityDataSetup.CrateLowRolls, MapDensityDataSetup.CrateLowEntries.Length),
                         (MapDensityDataSetup.TrunkTablePath, MapDensityDataSetup.TrunkId, MapDensityDataSetup.TrunkRolls, MapDensityDataSetup.TrunkEntries.Length),
                     })
            {
                var t = AssetDatabase.LoadAssetAtPath<LootTableSO>(path);
                Assert.IsNotNull(t, path);
                Assert.AreEqual(id, t.id);
                Assert.AreEqual(rolls, t.rollCount);
                Assert.AreEqual(count, t.entries.Count, $"{id}: 항목 수(아이템이 없어 빠진 것 없음)");
                Assert.IsTrue(t.entries.All(e => e.item != null && e.weight > 0f), $"{id}: null·가중치 0 없음");
                Assert.IsFalse(t.entries.Any(e => e.item == flareDef), $"{id}: 신호탄은 LT-CRATE에만 (EXT-03 희소성)");
                Assert.IsNull(t.guaranteedFirstRoll);
            }
        }

        // ── 경제 시뮬레이션 (설계 §9, Q2 A) — 커밋된 씬·데이터로. 데이터·배치를 바꾸면 이 테스트가 알려 준다 ──

        [Test]
        public void EconomySimulation_PoiRaid_ContainerValue_WithinTarget_NoCorpseInflation()
        {
            Scene scene = EditorSceneManager.OpenScene(M4SceneSetup.RaidScenePath, OpenSceneMode.Additive);
            try
            {
                LootEconomySimulator.World world = LootEconomySimulator.FromScene(scene);
                Assert.AreEqual(203, world.Containers.Count(c => !c.IsCorpse), "루트 지점 = 상자 133 + 트렁크 70");
                Assert.AreEqual(21, world.Containers.Count(c => c.Baseline && !c.IsCorpse), "지금 맵 기준 = 기존 상자 21");
                List<LootEconomySimulator.Row> rows = LootEconomySimulator.RunAll(world, LootEconomySimulator.SettingsFromData(), LootEconomySimulator.TestRaids);
                string report = LootEconomySimulator.Report(world, rows, LootEconomySimulator.TestRaids);
                double poi = LootEconomySimulator.Find(rows, LootEconomySimulator.Strategy.PoiRaid, LootEconomySimulator.Kit.Standard, corpses: false).Ratio;
                Assert.That(poi, Is.InRange(LootEconomySimulator.TargetMin, LootEconomySimulator.TargetMax), $"① POI 털이·표준·컨테이너만 배수\n{report}");
                foreach (LootEconomySimulator.Row r in rows.Where(r => r.Corpses))
                    Assert.LessOrEqual(r.Ratio, LootEconomySimulator.TargetMax, $"시체 포함 반출 가치가 목표 상한을 넘으면 경제 인플레이션\n{report}");
                Assert.IsTrue(rows.All(r => r.Density.Mia == 0), report);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
