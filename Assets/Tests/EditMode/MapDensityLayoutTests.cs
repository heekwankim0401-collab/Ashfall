using System.Collections.Generic;
using System.Linq;
using Ashfall.EditorTools;
using NUnit.Framework;
using UnityEngine;
using Category = Ashfall.EditorTools.MapDensityLayout.Category;
using ScatterKind = Ashfall.EditorTools.MapDensityLayout.ScatterKind;

namespace Ashfall.Tests.EditMode
{
    // 맵 밀도 ①② 배치 데이터·생성기·메시 (docs/design/MapDensity_design.md §3·§6·§7 1·§10). 씬 없이 데이터만(평지 가정)으로 생성한다 —
    // 씬 콜라이더·지형·NavMesh 확인은 MapDensitySceneApply가 적용 때, PlayMode MapDensitySceneSmokeTests가 결과를 본다.
    public class MapDensityLayoutTests
    {
        private static MapDensityLayout.ScatterSettings DefaultScatter(int trees = 1500, int bushes = 400, int logs = 120) =>
            new MapDensityLayout.ScatterSettings(trees, bushes, logs, 5f, 90f, 0.45f, 0.25f, 30f, new Vector2(0.8f, 1.3f),
                MapDensityPrefabs.TreeTrunkRadii(), MapDensityPrefabs.LogLengthM);

        // ── 구역 ──

        [Test]
        public void ZoneOf_PoiRouteOutskirt()
        {
            Assert.AreEqual(LootZoneRules.Route, LootZoneRules.ZoneOf(Vector2.zero));
            Assert.AreEqual("POI-VIL", LootZoneRules.ZoneOf(new Vector2(-300f, 80f)));
            Assert.AreEqual("POI-VIL", LootZoneRules.ZoneOf(new Vector2(-300f, 80f + 60f + 39f)), "발자국 + 40m 안");
            Assert.AreEqual(LootZoneRules.Route, LootZoneRules.ZoneOf(new Vector2(-300f, 80f + 60f + 41f)));
            Assert.AreEqual("POI-HWY", LootZoneRules.ZoneOf(new Vector2(0f, -300f)));
            Assert.AreEqual(LootZoneRules.Outskirt, LootZoneRules.ZoneOf(new Vector2(450f, 300f)));
            Assert.AreEqual(LootZoneRules.Outskirt, LootZoneRules.ZoneOf(new Vector2(0f, -381f)), "HWY 구역(z ≥ −380) 밖 + 가장자리");
            Assert.AreEqual("POI-REF", LootZoneRules.ZoneOf(new Vector2(400f, 0f)), "POI 구역이 외곽보다 먼저");
            Assert.AreEqual(7, LootZoneRules.AllZoneIds().Count(), "POI 5 + ROUTE + OUTSKIRT");
        }

        // ── 기하 ──

        [Test]
        public void Footprint_DistanceOverlapSegment()
        {
            var a = new Footprint(Vector2.zero, new Vector2(2f, 1f), 0f);
            Assert.AreEqual(0f, a.Distance(new Vector2(1f, 0.5f)));
            Assert.AreEqual(3f, a.Distance(new Vector2(5f, 0f)), 1e-4f);
            var rotated = new Footprint(Vector2.zero, new Vector2(2f, 1f), 90f);   // 로컬 x → 월드 −z
            Assert.AreEqual(0f, rotated.Distance(new Vector2(0f, -1.9f)), 1e-4f);
            Assert.AreEqual(1f, rotated.Distance(new Vector2(2f, 0f)), 1e-4f);
            var b = new Footprint(new Vector2(4.5f, 0f), new Vector2(2f, 1f), 0f);
            Assert.IsFalse(a.Overlaps(b));
            Assert.AreEqual(0.5f, a.Distance(b), 1e-4f);
            Assert.IsTrue(a.Overlaps(new Footprint(new Vector2(3f, 0f), new Vector2(2f, 1f), 45f)));
            Assert.IsTrue(a.SegmentIntersects(new Vector2(-5f, 0f), new Vector2(5f, 0f)));
            Assert.AreEqual(0f, a.DistanceToSegment(new Vector2(-5f, 0f), new Vector2(5f, 0f)));
            Assert.AreEqual(2f, a.DistanceToSegment(new Vector2(-5f, 3f), new Vector2(5f, 3f)), 1e-4f);
            a.Aabb(out Vector2 min, out Vector2 max);
            Assert.AreEqual(new Vector2(-2f, -1f), min);
            Assert.AreEqual(new Vector2(2f, 1f), max);
        }

        [Test]
        public void PoissonDisk_MinimumSpacing_Deterministic()
        {
            List<Vector2> a = PoissonDisk.Sample(new Vector2(-50f, -50f), new Vector2(50f, 50f), 5f, new System.Random(1));
            List<Vector2> b = PoissonDisk.Sample(new Vector2(-50f, -50f), new Vector2(50f, 50f), 5f, new System.Random(1));
            CollectionAssert.AreEqual(a, b);
            Assert.Greater(a.Count, 150, "100×100m에 5m 간격이면 수백 개");
            for (int i = 0; i < a.Count; i++)
            for (int j = i + 1; j < a.Count; j++)
                Assert.GreaterOrEqual((a[i] - a[j]).magnitude, 5f - 1e-4f);
        }

        // ── 구조물 (①) ──

        [Test]
        public void Structures_FromData_HitTargets_Deterministic_PassChecks()
        {
            MapDensityLayout.SiteContext ctx = MapDensityLayout.SiteContext.FromLayoutData();
            List<MapDensityLayout.StructurePlan> a = MapDensityLayout.GenerateStructures(ctx, out string report);
            List<MapDensityLayout.StructurePlan> b = MapDensityLayout.GenerateStructures(MapDensityLayout.SiteContext.FromLayoutData(), out _);
            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.AreEqual(a[i].Piece.XZ, b[i].Piece.XZ, $"#{i} 위치");
                Assert.AreEqual(a[i].Piece.YawDeg, b[i].Piece.YawDeg, $"#{i} 방향");
                Assert.AreEqual(a[i].Template, b[i].Template);
            }
            Assert.AreEqual(MapDensityLayout.BuildingTarget, a.Count(p => p.Category == Category.Building), report);
            Assert.AreEqual(MapDensityLayout.SmallStructureTarget, a.Count(p => p.Category == Category.SmallStructure), report);
            Assert.AreEqual(MapDensityLayout.CarTarget, a.Count(p => p.Category == Category.Car), report);
            Assert.AreEqual(MapDensityLayout.LargeVehicleTarget, a.Count(p => p.Category == Category.LargeVehicle), report);
            Assert.IsTrue(a.Where(p => p.Category == Category.Car).All(p => p.IsPassengerCar), "승용차 = 길이 ≤ 5m (③ 트렁크)");
            Assert.IsTrue(a.Where(p => p.Category == Category.LargeVehicle).All(p => !p.IsPassengerCar), "큰 차량은 트렁크 없음");
            Assert.IsTrue(MapDensityLayout.CheckStructures(a, ctx, out string check), check);
            // 분배: POI·길목·외곽 모두에 건물이 있다
            Assert.Greater(a.Count(p => p.Category == Category.Building && LootZoneRules.IsPoi(p.Zone)), 20);
            Assert.Greater(a.Count(p => p.Category == Category.Building && p.Zone == LootZoneRules.Route), 20);
            Assert.Greater(a.Count(p => p.Category == Category.Building && p.Zone == LootZoneRules.Outskirt), 5);
        }

        [Test]
        public void CheckStructures_InjectedViolations_Fail()
        {
            MapDensityLayout.SiteContext ctx = MapDensityLayout.SiteContext.FromLayoutData();
            var onRoad = new MapDensityLayout.StructurePlan
            {
                Piece = new M4MapSetup.Piece(M4MapSetup.Shape.House, 0f, -400f, new Vector3(7f, 3f, 6f)), Category = Category.Building,
                Zone = LootZoneRules.ZoneOf(new Vector2(0f, -400f)), Template = "Road",
            };
            Assert.IsFalse(MapDensityLayout.CheckStructures(new[] { onRoad }, ctx, out string r1), "도로 띠 위");
            StringAssert.Contains("도로", r1);
            Vector2 boss = MapDensityLayout.BossCenter().Value;
            var atBoss = new MapDensityLayout.StructurePlan
            {
                Piece = new M4MapSetup.Piece(M4MapSetup.Shape.House, boss.x + 10f, boss.y - 12f, new Vector3(3f, 3f, 3f)), Category = Category.Building,
                Zone = LootZoneRules.ZoneOf(boss + new Vector2(10f, -12f)), Template = "Boss",
            };
            Assert.IsFalse(MapDensityLayout.CheckStructures(new[] { atBoss }, ctx, out _), "보스 거점 25m 안");
            Vector2 spawn = CrimsonBasinLayout.Spawns[0].PositionXZ + new Vector2(20f, 0f);
            var atSpawn = new MapDensityLayout.StructurePlan
            {
                Piece = new M4MapSetup.Piece(M4MapSetup.Shape.Sandbag, spawn.x, spawn.y, new Vector3(3f, 1f, 1f)), Category = Category.SmallStructure,
                Zone = LootZoneRules.ZoneOf(spawn), Template = "Spawn",
            };
            Assert.IsFalse(MapDensityLayout.CheckStructures(new[] { atSpawn }, ctx, out _), "스폰 25m 안");
            // 순찰 구간 선분 3m (HWY_S 첫 구간 (−140,−315)→(20,−315))
            var onPatrol = new MapDensityLayout.StructurePlan
            {
                Piece = new M4MapSetup.Piece(M4MapSetup.Shape.Sandbag, -60f, -317f, new Vector3(3f, 1f, 1f)), Category = Category.SmallStructure,
                Zone = "POI-HWY", Template = "Patrol",
            };
            Assert.IsFalse(MapDensityLayout.CheckStructures(new[] { onPatrol }, ctx, out _), "순찰 통로");
        }

        // ── 산포 (②) ──

        [Test]
        public void Scatter_FromData_HitsCounts_Deterministic_SpacingAndZones()
        {
            MapDensityLayout.SiteContext ctx = MapDensityLayout.SiteContext.FromLayoutData();
            List<MapDensityLayout.StructurePlan> structures = MapDensityLayout.GenerateStructures(ctx, out _);
            foreach (MapDensityLayout.StructurePlan p in structures)
                ctx.AddObstacle(p.Footprint);
            List<MapDensityLayout.ScatterPlan> a = MapDensityLayout.GenerateScatter(ctx, DefaultScatter(), structures, out string report);
            List<MapDensityLayout.ScatterPlan> b = MapDensityLayout.GenerateScatter(ctx, DefaultScatter(), structures, out _);
            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
                Assert.IsTrue(a[i].XZ == b[i].XZ && a[i].Kind == b[i].Kind && a[i].Variant == b[i].Variant && a[i].Scale == b[i].Scale, $"#{i}");

            List<MapDensityLayout.ScatterPlan> trees = a.Where(p => p.Kind == ScatterKind.Tree).ToList();
            Assert.AreEqual(1500, trees.Count, report);
            Assert.AreEqual(400, a.Count(p => p.Kind == ScatterKind.Bush), report);
            Assert.AreEqual(120, a.Count(p => p.Kind == ScatterKind.Log), report);
            (int route, int outskirt, int poi) quota = MapDensityLayout.TreeQuotas(1500);
            Assert.AreEqual((900, 500, 100), quota);
            Assert.AreEqual(quota.route, trees.Count(t => t.Zone == LootZoneRules.Route), report);
            Assert.AreEqual(quota.outskirt, trees.Count(t => t.Zone == LootZoneRules.Outskirt), report);
            Assert.IsTrue(trees.Where(t => LootZoneRules.IsPoi(t.Zone)).All(t => MapDensityLayout.TreePoiIds.Contains(t.Zone)), "POI 나무는 마을·저수지만");
            Assert.IsTrue(trees.All(t => !LootZoneRules.InsidePoiFootprint(t.XZ, out _)), "POI 발자국 안 나무 없음");
            Assert.Greater(trees.Count(t => t.InGrove), trees.Count / 2, "대부분 군락");
            Assert.IsTrue(trees.All(t => t.Scale >= 0.8f && t.Scale <= 1.3f));
            // 최소 간격 (격자로 이웃만)
            var grid = new Dictionary<Vector2Int, List<Vector2>>();
            foreach (var t in trees)
            {
                var cell = new Vector2Int(Mathf.FloorToInt(t.XZ.x / 10f), Mathf.FloorToInt(t.XZ.y / 10f));
                for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                    if (grid.TryGetValue(cell + new Vector2Int(dx, dz), out List<Vector2> near))
                        foreach (Vector2 n in near)
                            Assert.GreaterOrEqual((n - t.XZ).magnitude, 5f - 1e-3f, "나무 간격");
                if (!grid.TryGetValue(cell, out List<Vector2> list))
                    grid[cell] = list = new List<Vector2>();
                list.Add(t.XZ);
            }
            // 보스 거점·스폰 25m 안 없음
            Vector2 boss = MapDensityLayout.BossCenter().Value;
            Assert.IsTrue(a.All(p => (p.XZ - boss).magnitude >= MapDensityLayout.BossKeepOutM), "보스 거점");
            foreach (CrimsonBasinLayout.Spawn spawn in CrimsonBasinLayout.Spawns)
                Assert.IsTrue(a.All(p => (p.XZ - spawn.PositionXZ).magnitude >= 25f), $"스폰 {spawn.Id}");
        }

        // ── 풀 밀도 (순수) ──

        [Test]
        public void GrassLayers_CoverageAndExclusion()
        {
            const int res = 128;
            var excluded = new bool[res, res];
            for (int z = 0; z < res; z++)
            for (int x = 0; x < 20; x++)
                excluded[z, x] = true;
            MapDensitySceneApply.GrassLayers(excluded, 1f, 0.4f, 1.2f, 1f, 40f, 5054, out int[,] s, out int[,] t, out int covered, out long instances);
            int walkable = res * (res - 20);
            Assert.AreEqual(0.4f, covered / (float)walkable, 0.01f, "덮는 비율");
            for (int z = 0; z < res; z++)
            for (int x = 0; x < 20; x++)
                Assert.AreEqual(0, s[z, x] + t[z, x], "제외 셀");
            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                Assert.LessOrEqual(s[z, x], 2);
                Assert.IsTrue(s[z, x] == 0 || t[z, x] == 0, "셀마다 한 층");
            }
            Assert.Greater(instances, (long)(covered * 0.5f), "셀당 기대 0.6~1.2");
            Assert.LessOrEqual(instances, (long)(covered * 1.2f) + covered / 10);
            MapDensitySceneApply.GrassLayers(excluded, 1f, 0.4f, 1.2f, 1f, 40f, 5054, out int[,] s2, out int[,] t2, out _, out _);
            CollectionAssert.AreEqual(s, s2, "결정적");
            CollectionAssert.AreEqual(t, t2);
        }

        // ── LOD 거리 ↔ 비율 ──

        [Test]
        public void Lod_DistanceRelativeHeight_RoundTrip_Decreasing()
        {
            foreach (float d in new[] { 60f, 120f, 260f, 450f })
            {
                float h = MapDensityPrefabs.RelativeHeight(d, 8f, 60f, 2f);
                Assert.AreEqual(d, MapDensityPrefabs.DistanceOf(h, 8f, 60f, 2f), d * 1e-3f);
            }
            Assert.AreEqual(MapDensityPrefabs.RelativeHeight(100f, 8f, 60f, 1f) * 2f, MapDensityPrefabs.RelativeHeight(100f, 8f, 60f, 2f), 1e-5f, "lodBias 2 = 같은 비율이 2배 거리");
            float[] t = MapDensityPrefabs.Thresholds(new[] { 60f, 260f }, 9f, 60f, 2f);
            Assert.Greater(t[0], t[1]);
            Assert.Greater(t[1], 0f);
            Assert.LessOrEqual(t[0], 1f);
        }

        // ── 메시·머티리얼 표 ──

        [Test]
        public void ScatterMeshes_TriangleBudgets_BushHeight()
        {
            foreach (MapDensityPrefabs.TreeSpec spec in MapDensityPrefabs.Trees)
            {
                MapDensityPrefabs.BuildTreeMeshes(spec, out Mesh lod0, out Mesh lod1);
                try
                {
                    Assert.LessOrEqual(lod0.triangles.Length / 3, MapDensityPrefabs.TreeLod0MaxTriangles, "나무 LOD0");
                    Assert.LessOrEqual(lod1.triangles.Length / 3, MapDensityPrefabs.TreeLod1MaxTriangles, "나무 LOD1");
                    Assert.Greater(lod0.triangles.Length, lod1.triangles.Length * 2, "LOD1이 훨씬 가볍다");
                    Assert.AreEqual(spec.HeightM, lod0.bounds.max.y, spec.HeightM * 0.1f, "키");
                }
                finally
                {
                    Object.DestroyImmediate(lod0);
                    Object.DestroyImmediate(lod1);
                }
            }
            for (int i = 0; i < MapDensityPrefabs.BushVariants; i++)
            {
                Mesh bush = MapDensityPrefabs.BuildBushMesh(201 + i, i == 0 ? 14 : 18);
                Assert.LessOrEqual(bush.triangles.Length / 3, MapDensityPrefabs.BushMaxTriangles);
                Assert.LessOrEqual(bush.bounds.max.y * 1.15f, 1.0f + 1e-3f, "덤불 ≤ 1.0m (배율 1.15까지, §8.4)");
                Object.DestroyImmediate(bush);
            }
            Mesh log = MapDensityPrefabs.BuildLogMesh();
            Assert.LessOrEqual(log.triangles.Length / 3, MapDensityPrefabs.LogMaxTriangles);
            Object.DestroyImmediate(log);
            foreach (bool tall in new[] { false, true })
            {
                Mesh grass = MapDensityPrefabs.BuildGrassMesh(301, tall);
                Assert.LessOrEqual(grass.triangles.Length / 3, MapDensityPrefabs.GrassMaxTriangles, "풀포기 약 12 삼각형");
                Object.DestroyImmediate(grass);
            }
        }

        [Test]
        public void PaletteTable_HasScatterMaterials_AndNewShapes()
        {
            foreach (PaletteMaterialMap.Key key in new[] { PaletteMaterialMap.Key.DeadWood, PaletteMaterialMap.Key.CharredWood,
                         PaletteMaterialMap.Key.DryGrass, PaletteMaterialMap.Key.DryGrassTall })
            {
                PaletteMaterialMap.Spec spec = PaletteMaterialMap.Materials[key];
                StringAssert.StartsWith(M6Names.PaletteMaterialFolder + "/", spec.Path);
                Assert.IsTrue(spec.Instancing, $"{spec.Name}: 인스턴싱");
            }
            Assert.IsTrue(PaletteMaterialMap.Materials[PaletteMaterialMap.Key.DryGrass].DoubleSided);
            Assert.IsFalse(PaletteMaterialMap.Materials[PaletteMaterialMap.Key.Concrete].Instancing, "기존 머티리얼 속성 그대로");
            foreach (string shape in new[] { "Sandbag", "Fence", "Pole", "Debris" })
                Assert.IsTrue(PaletteMaterialMap.TryGetStructureRule(shape + "_07", out _), shape);
            foreach (MapDensityLayout.Template t in MapDensityLayout.Buildings.Concat(MapDensityLayout.SmallStructures).Concat(MapDensityLayout.LargeVehicles))
                Assert.IsTrue(PaletteMaterialMap.TryGetStructureRule(t.Shape + "_01", out _), $"{t.Name}: 칠하기 규칙");
        }

        [Test]
        public void PrefabPaths_InScatterFolder()
        {
            List<string> paths = MapDensityPrefabs.AllPrefabPaths().ToList();
            Assert.AreEqual(MapDensityPrefabs.Trees.Length + MapDensityPrefabs.BushVariants + 3, paths.Count);
            Assert.IsTrue(paths.All(p => p.StartsWith("Assets/_Project/Prefabs/Scatter/SC_") && p.EndsWith(".prefab")));
            Assert.AreEqual(paths.Count, paths.Distinct().Count());
        }
    }
}
