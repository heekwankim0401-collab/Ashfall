using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Ashfall.AI;
using Ashfall.Core;
using Ashfall.Hideout;
using Ashfall.Inventory;
using Ashfall.Player;
using Ashfall.Raid;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using L = Ashfall.EditorTools.CrimsonBasinLayout;
using Object = UnityEngine.Object;

namespace Ashfall.EditorTools
{
    // 맵 밀도 ③ 루트 경제 시뮬레이션 (docs/design/MapDensity_design.md §9, 사용자 결정 Q2 A: 레이드당 기대 판매가치 = 지금 맵의 1.3~1.5배).
    // 레이드 seed 1..N(고정)마다: 스폰·활성 탈출구(RaidController와 같은 ActiveExtractionPicker 규칙) → 루트 활성화(SeedMath.RaidLootSeed → LootActivationPlanner,
    // LootSpawnAssignment — 실제 지휘자와 같은 함수) → 컨테이너 내용물(LootContainer.CreateContents, 시체 = CorpseLootBuilder) → 플레이어 모델 → 판매가치(PriceRules).
    // 비교 기준(Baseline) = 지금 맵: 기존 상자 21(전부 활성, 열 때마다 무작위 → 여기서는 seed 파생) + 시체 34. 새 맵(Density) = 루트 지점 200(레이드마다 일부 활성) + 같은 시체.
    // 플레이어 모델 (설계 §9 — 모델 상수는 게임 수치가 아니라 시뮬레이션 가정이다):
    //   ① POI 털이: 스폰에서 가장 가까운 POI의 컨테이너(상자·트렁크·시체, 활성 여부는 모름 — 빈 것도 연다)를 가까운 순으로 전부 → 탈출
    //   ② 탐욕 순회: 가장 가까운 미방문 컨테이너(구역 무관), 직선 × PathFactor 경로, 루팅 예산 GreedyBudgetSec → 탈출
    //   장비 2종: 부랑자 키트 = 주머니만(1×1 × 4), 표준 = 주머니 + 전술조끼(RIG-01) + 배낭(BAG-01) 칸. 칸은 넓이로 보고(모양은 들어갈 그리드가 있는지만),
    //   칸이 차면 탄피/칸이 더 높은 아이템으로 바꿔 담는다(값이 늘 때만) — 칸이 루트 양을 묶는다.
    //   시간: 이동 = 거리 × PathFactor / PlayerConfig.walkSpeed, 컨테이너 = OpenSec + 아이템 수 × InventoryConfig.searchTimePerItemSec. 제한 시간을 넘기면 MIA(가치 0).
    //   전투·위험·무게는 보지 않는다(두 맵에 같게 빠진다 — 배수 비교용).
    public static class LootEconomySimulator
    {
        private const string Tag = "[LootEconomySimulator]";
        public const int DefaultRaids = 1000;
        public const int TestRaids = 200;
        public const float TargetMin = 1.3f, TargetMax = 1.5f;   // Q2 A

        public enum Strategy { PoiRaid, Greedy }
        public enum Kit { Vagrant, Standard }
        public enum MapMode { Baseline, Density }

        public sealed class Settings
        {
            public float MoveSpeed = 3f;           // PlayerConfig.walkSpeed
            public float PathFactor = 1.3f;        // 설계 §9
            public float OpenSec = 1f;             // 열기·닫기 동작 (가정)
            public float SearchPerItemSec = 0.5f;  // InventoryConfig.searchTimePerItemSec
            public float GreedyBudgetSec = 900f;   // 설계 §9 "15분 예산"
            public float TimeLimitSec = 1500f;     // RaidConfig.timeLimitSec
            public int PocketCells = 4;
            public Vector2Int RigGrid = new Vector2Int(4, 2);
            public Vector2Int BagGrid = new Vector2Int(4, 5);
        }

        public sealed class Container
        {
            public string Key;
            public Vector2 XZ;
            public string Zone;
            public bool IsCorpse;
            public bool Baseline;                  // 지금 맵에 있는 것 (기존 상자·시체)
            public LootActivationPlanner.Slot Slot; // 루트 지점 (시체는 의미 없음)
            public ItemDefinitionSO[] Fixed = Array.Empty<ItemDefinitionSO>();
            public LootTableSO Table;              // 직렬화된 테이블
            public EnemyArchetypeSO Archetype;     // 시체
            public int LoadoutIndex = -1;
        }

        public sealed class World
        {
            public readonly List<Container> Containers = new List<Container>();
            public readonly List<(string Id, Vector2 XZ)> Spawns = new List<(string, Vector2)>();       // id 서수 정렬 (RaidController와 같다)
            public readonly List<(string Id, Vector2 XZ)> Exits = new List<(string, Vector2)>();
            public readonly List<(string Id, Vector2 XZ)> Pois = new List<(string, Vector2)>();
            public RaidConfigSO Raid;
            public LootSpawnConfigSO Spawn;
            public TraderConfigSO Trader;
            public ItemDefinitionSO Flare;
        }

        public sealed class Stats
        {
            public readonly List<int> Values = new List<int>();
            public int Mia;
            public double FlaresInMap, FlaresCarried, ActiveContainers, Opened;
            public readonly Dictionary<string, double> FoundById = new Dictionary<string, double>(StringComparer.Ordinal);

            public double Mean => Values.Count == 0 ? 0 : Values.Average();
            public double Median => Percentile(0.5);
            public double P90 => Percentile(0.9);

            public double Percentile(double q)
            {
                if (Values.Count == 0)
                    return 0;
                var sorted = Values.OrderBy(v => v).ToList();
                int i = Math.Min(sorted.Count - 1, Math.Max(0, (int)Math.Ceiling(q * sorted.Count) - 1));
                return sorted[i];
            }
        }

        // ---------- 순수 시뮬레이션 ----------

        public static Stats Run(World world, MapMode map, Strategy strategy, Kit kit, Settings s, int raids, bool includeCorpses = true)
        {
            var stats = new Stats();
            var candidates = world.Containers.Where(c => (map == MapMode.Density || c.Baseline) && (includeCorpses || !c.IsCorpse)).ToList();
            var slots = candidates.Where(c => !c.IsCorpse).ToList();
            var plannerSlots = slots.Select(c => c.Slot).ToList();
            var spawnIds = world.Spawns.Select(p => p.Id).ToList();
            for (int seed = 1; seed <= raids; seed++)
            {
                var random = new System.Random(seed);
                int spawnIndex = ActiveExtractionPicker.PickSpawnIndex(world.Spawns.Count, random);
                (string spawnId, Vector2 start) = world.Spawns[Math.Max(0, spawnIndex)];
                var active = new List<string>();
                ActiveExtractionPicker.Pick(world.Raid, spawnId, random, active);

                // 컨테이너 내용물 (열기 전에 정해 두지만, 플레이어 모델은 연 것만 본다)
                var contents = new Dictionary<Container, List<ItemInstance>>();
                int lootSeed = SeedMath.RaidLootSeed(seed);
                if (map == MapMode.Density)
                {
                    LootActivationPlanner.Decision[] plan = LootActivationPlanner.Plan(plannerSlots, world.Spawn.RatioOf, world.Spawn.minActivePerZone, lootSeed);
                    for (int i = 0; i < slots.Count; i++)
                    {
                        LootSpawnAssignment.Result r = LootSpawnAssignment.Resolve(plannerSlots[i], plan[i], world.Spawn, slots[i].Table);
                        if (plan[i].Active)
                            stats.ActiveContainers++;
                        IReadOnlyList<ItemDefinitionSO> fixedItems = r.Mode == LootSpawnAssignment.Mode.Table ? slots[i].Fixed : null;
                        LootTableSO table = r.Mode == LootSpawnAssignment.Mode.Empty ? null : r.Table;
                        contents[slots[i]] = LootContainer.CreateContents(fixedItems, table, new System.Random(SeedMath.NonZero(r.Seed)));
                    }
                }
                else
                {
                    foreach (Container c in slots)
                    {
                        stats.ActiveContainers++;
                        contents[c] = LootContainer.CreateContents(c.Fixed, c.Table, new System.Random(LootActivationPlanner.ContentSeed(lootSeed, c.Key)));
                    }
                }
                foreach (Container c in candidates.Where(c => c.IsCorpse))
                    contents[c] = CorpseItems(c, new System.Random(LootActivationPlanner.ContentSeed(lootSeed, c.Key)));
                foreach (List<ItemInstance> items in contents.Values)
                    foreach (ItemInstance item in items)
                        if (item.Definition == world.Flare)
                            stats.FlaresInMap++;

                // 방문 순서
                var pack = new Pack(kit, s);
                Vector2 at = start;
                float time = 0f;
                var remaining = strategy == Strategy.PoiRaid ? candidates.Where(c => c.Zone == NearestPoi(world, start)).ToList() : new List<Container>(candidates);
                while (remaining.Count > 0)
                {
                    Container next = remaining.OrderBy(c => (c.XZ - at).sqrMagnitude).ThenBy(c => c.Key, StringComparer.Ordinal).First();
                    float travel = Vector2.Distance(at, next.XZ) * s.PathFactor / Mathf.Max(0.1f, s.MoveSpeed);
                    List<ItemInstance> items = contents[next];
                    float open = s.OpenSec + items.Count * s.SearchPerItemSec;
                    if (strategy == Strategy.Greedy && time + travel + open > s.GreedyBudgetSec)
                        break;
                    time += travel + open;
                    at = next.XZ;
                    remaining.Remove(next);
                    stats.Opened++;
                    foreach (ItemInstance item in items)
                    {
                        string id = item.Definition != null ? item.Definition.id : "?";
                        stats.FoundById[id] = (stats.FoundById.TryGetValue(id, out double n) ? n : 0) + 1;
                        pack.Offer(item, Math.Max(0, PriceRules.SellPrice(item, world.Trader)));
                    }
                }
                // 탈출: 가장 가까운 활성 탈출구 (조건은 보지 않는다 — 시간만)
                float exitDistance = float.MaxValue;
                foreach ((string id, Vector2 xz) in world.Exits)
                    if (active.Contains(id))
                        exitDistance = Mathf.Min(exitDistance, Vector2.Distance(at, xz));
                if (exitDistance < float.MaxValue)
                    time += exitDistance * s.PathFactor / Mathf.Max(0.1f, s.MoveSpeed);
                if (time > s.TimeLimitSec)
                {
                    stats.Mia++;
                    stats.Values.Add(0);
                    continue;
                }
                stats.Values.Add(pack.Value);
                stats.FlaresCarried += pack.Count(world.Flare);
            }
            return stats;
        }

        private static string NearestPoi(World world, Vector2 from)
        {
            string best = null;
            float bestD = float.MaxValue;
            foreach ((string id, Vector2 xz) in world.Pois)
            {
                float d = Vector2.Distance(from, xz);
                if (d < bestD)
                {
                    bestD = d;
                    best = id;
                }
            }
            return best;
        }

        // 시체 = CorpseLootBuilder (무기·탄창·루트, 적 전용 무기 제외) — 게임과 같은 함수
        private static List<ItemInstance> CorpseItems(Container c, System.Random random)
        {
            EnemyArchetypeSO a = c.Archetype;
            if (a == null)
                return new List<ItemInstance>();
            EnemyWeapon weapon = null;
            int index = EnemyWeapon.PickLoadout(a.loadouts, c.LoadoutIndex, random);
            if (index >= 0 && EnemyWeapon.IsValidLoadout(a.loadouts[index], out _))
                weapon = new EnemyWeapon(a.loadouts[index], a.spareMagazines, a, null, random);
            return CorpseLootBuilder.Build(weapon, a.loot, random).ToList();
        }

        // 넓이 기준 가방 (모양은 들어갈 그리드가 있는지만 본다). 차면 탄피/칸이 높은 것으로 바꾼다(값이 늘 때만).
        private sealed class Pack
        {
            private readonly List<(ItemInstance item, int value, int area)> carried = new List<(ItemInstance, int, int)>();
            private readonly int capacity;
            private readonly Vector2Int[] grids;
            private int used;

            public Pack(Kit kit, Settings s)
            {
                if (kit == Kit.Vagrant)
                {
                    capacity = s.PocketCells;
                    grids = new[] { Vector2Int.one };
                }
                else
                {
                    capacity = s.PocketCells + s.RigGrid.x * s.RigGrid.y + s.BagGrid.x * s.BagGrid.y;
                    grids = new[] { Vector2Int.one, s.RigGrid, s.BagGrid };
                }
            }

            public int Value => carried.Sum(c => c.value);
            public int Count(ItemDefinitionSO def) => def == null ? 0 : carried.Count(c => c.item.Definition == def);

            private bool Fits(Vector2Int size)
            {
                foreach (Vector2Int g in grids)
                    if ((size.x <= g.x && size.y <= g.y) || (size.y <= g.x && size.x <= g.y))
                        return true;
                return false;
            }

            public void Offer(ItemInstance item, int value)
            {
                if (item?.Definition == null || value <= 0)
                    return;
                Vector2Int size = item.Definition.size;
                int area = Math.Max(1, size.x * size.y);
                if (!Fits(size) || area > capacity)
                    return;
                // 부랑자 키트(주머니만)는 1×1만 — Fits가 막는다
                if (used + area <= capacity)
                {
                    carried.Add((item, value, area));
                    used += area;
                    return;
                }
                double density = (double)value / area;
                var cheaper = carried.Where(c => (double)c.value / c.area < density).OrderBy(c => (double)c.value / c.area).ToList();
                int freed = capacity - used, lost = 0;
                var drop = new List<(ItemInstance, int, int)>();
                foreach (var c in cheaper)
                {
                    if (freed >= area)
                        break;
                    drop.Add(c);
                    freed += c.area;
                    lost += c.value;
                }
                if (freed < area || lost >= value)
                    return;
                foreach (var d in drop)
                {
                    carried.Remove(d);
                    used -= d.Item3;
                }
                carried.Add((item, value, area));
                used += area;
            }
        }

        // ---------- 씬 → World ----------

        public static World FromScene(Scene scene)
        {
            var world = new World
            {
                Raid = AssetDatabase.LoadAssetAtPath<RaidConfigSO>(M4MapSetup.DefaultRaidConfigPath),
                Spawn = AssetDatabase.LoadAssetAtPath<LootSpawnConfigSO>(MapDensityDataSetup.LootSpawnConfigPath),
                Trader = AssetDatabase.LoadAssetAtPath<TraderConfigSO>(M5DataSetup.TraderConfigPath),
                Flare = AssetDatabase.LoadAssetAtPath<ItemDefinitionSO>(M4DataSetup.FlarePath),
            };
            if (world.Raid == null || world.Spawn == null || world.Trader == null)
                throw new InvalidOperationException($"{Tag} RaidConfig·LootSpawnConfig·TraderConfig 중 없음 — Ashfall/Setup/Apply Map Density");
            foreach (L.Spawn sp in L.Spawns.OrderBy(p => p.Id, StringComparer.Ordinal))
                world.Spawns.Add((sp.Id, sp.PositionXZ));
            foreach (L.Extraction e in L.Extractions)
                world.Exits.Add((e.Id, e.CenterXZ));
            foreach (L.Poi poi in L.Pois)
                world.Pois.Add((poi.Id, poi.CenterXZ));

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (LootSpawnSlot slot in root.GetComponentsInChildren<LootSpawnSlot>(true))
                {
                    var container = slot.GetComponent<LootContainer>();
                    if (container == null)
                        continue;
                    world.Containers.Add(new Container
                    {
                        Key = slot.SlotKey,
                        XZ = new Vector2(slot.transform.position.x, slot.transform.position.z),
                        Zone = slot.ZoneId,
                        Slot = slot.ToPlannerSlot(),
                        Fixed = container.FixedContents != null ? container.FixedContents.ToArray() : Array.Empty<ItemDefinitionSO>(),
                        Table = container.SerializedLootTable,
                        Baseline = slot.Kind == LootSlotKind.Crate && !slot.SlotKey.StartsWith(MapDensityLootLayout.NamePrefix, StringComparison.Ordinal),
                    });
                }
                foreach (EnemyAgent enemy in root.GetComponentsInChildren<EnemyAgent>(true))
                {
                    var so = new SerializedObject(enemy);
                    Vector3 p = enemy.transform.position;
                    world.Containers.Add(new Container
                    {
                        Key = $"Corpse_{enemy.name}_{world.Containers.Count}",
                        XZ = new Vector2(p.x, p.z),
                        Zone = LootZoneRules.ZoneOf(new Vector2(p.x, p.z)),
                        IsCorpse = true,
                        Baseline = true,
                        Archetype = enemy.Archetype,
                        LoadoutIndex = so.FindProperty("loadoutIndex")?.intValue ?? -1,
                    });
                }
            }
            return world;
        }

        public static Settings SettingsFromData()
        {
            var s = new Settings();
            var player = AssetDatabase.LoadAssetAtPath<PlayerConfigSO>(M2DataSetup.PlayerConfigPath);
            var inventory = AssetDatabase.LoadAssetAtPath<InventoryConfigSO>(M2DataSetup.InventoryConfigPath);
            var raid = AssetDatabase.LoadAssetAtPath<RaidConfigSO>(M4MapSetup.DefaultRaidConfigPath);
            var rig = AssetDatabase.LoadAssetAtPath<ContainerDefinitionSO>(M2DataSetup.DataFolder + "/Items/Gear/RIG-01.asset");
            var bag = AssetDatabase.LoadAssetAtPath<ContainerDefinitionSO>(M2DataSetup.DataFolder + "/Items/Gear/BAG-01.asset");
            if (player != null)
                s.MoveSpeed = player.walkSpeed;
            if (inventory != null)
            {
                s.SearchPerItemSec = inventory.searchTimePerItemSec;
                s.PocketCells = inventory.pocketCount * inventory.pocketSize.x * inventory.pocketSize.y;
            }
            if (raid != null)
                s.TimeLimitSec = raid.timeLimitSec;
            if (rig != null)
                s.RigGrid = rig.gridSize;
            if (bag != null)
                s.BagGrid = bag.gridSize;
            return s;
        }

        // ---------- 보고 ----------

        public readonly struct Row
        {
            public readonly Strategy Strategy;
            public readonly Kit Kit;
            public readonly bool Corpses;           // 시체 포함 (사용자 기준 "현재 맵 21상자 + 시체")
            public readonly Stats Baseline, Density;

            public Row(Strategy strategy, Kit kit, bool corpses, Stats baseline, Stats density)
            {
                Strategy = strategy;
                Kit = kit;
                Corpses = corpses;
                Baseline = baseline;
                Density = density;
            }

            public double Ratio => Baseline.Mean > 0 ? Density.Mean / Baseline.Mean : 0;
        }

        // 두 측정: 시체 포함(전체 반출 가치) / 컨테이너만(상자·트렁크가 주는 몫 — 활성 비율·테이블이 움직이는 값). 각 전략 2 × 장비 2.
        public static List<Row> RunAll(World world, Settings s, int raids)
        {
            var rows = new List<Row>();
            foreach (bool corpses in new[] { true, false })
                foreach (Strategy strategy in new[] { Strategy.PoiRaid, Strategy.Greedy })
                    foreach (Kit kit in new[] { Kit.Standard, Kit.Vagrant })
                        rows.Add(new Row(strategy, kit, corpses, Run(world, MapMode.Baseline, strategy, kit, s, raids, corpses),
                            Run(world, MapMode.Density, strategy, kit, s, raids, corpses)));
            return rows;
        }

        public static Row Find(List<Row> rows, Strategy strategy, Kit kit, bool corpses) =>
            rows.First(r => r.Strategy == strategy && r.Kit == kit && r.Corpses == corpses);

        public static string Report(World world, List<Row> rows, int raids)
        {
            var ci = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            int baseCount = world.Containers.Count(c => c.Baseline && !c.IsCorpse), corpses = world.Containers.Count(c => c.IsCorpse);
            int all = world.Containers.Count(c => !c.IsCorpse);
            sb.AppendLine($"루트 경제 시뮬레이션 (레이드 seed 1..{raids}, 판매가치 = 탄피(PriceRules), 반출 = 칸 제한 안에서 고른 것)");
            sb.AppendLine($"  지금 맵: 상자 {baseCount}(전부 활성) + 시체 {corpses} / 새 맵: 루트 지점 {all}(레이드마다 일부 활성) + 같은 시체");
            sb.AppendLine("| 측정 | 전략 | 장비 | 지금 평균 | 지금 중앙값 | 지금 P90 | 새 평균 | 새 중앙값 | 새 P90 | 배수(평균) | 연 컨테이너/레이드(지금→새) |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (Row r in rows)
                sb.AppendLine(string.Format(ci, "| {0} | {1} | {2} | {3:0} | {4:0} | {5:0} | {6:0} | {7:0} | {8:0} | **{9:0.00}** | {10:0.0}→{11:0.0} |",
                    r.Corpses ? "시체 포함" : "컨테이너만", r.Strategy == Strategy.PoiRaid ? "① POI 털이" : "② 탐욕 순회", r.Kit == Kit.Standard ? "표준(조끼+배낭)" : "부랑자(주머니)",
                    r.Baseline.Mean, r.Baseline.Median, r.Baseline.P90, r.Density.Mean, r.Density.Median, r.Density.P90, r.Ratio,
                    r.Baseline.Opened / raids, r.Density.Opened / raids));
            Row any = rows[0];
            sb.AppendLine(string.Format(ci, "  레이드당 활성 상자·트렁크: 지금 {0:0.0}, 새 {1:0.0}. 맵 전체 신호탄(ITM-FLR-01)/레이드(시체 포함): 지금 {2:0.00}, 새 {3:0.00}. MIA: {4}",
                any.Baseline.ActiveContainers / raids, any.Density.ActiveContainers / raids, any.Baseline.FlaresInMap / raids, any.Density.FlaresInMap / raids,
                rows.Sum(r => r.Baseline.Mia + r.Density.Mia)));
            foreach (Row r in rows.Where(r => r.Kit == Kit.Standard && !r.Corpses))
            {
                sb.Append($"  {(r.Strategy == Strategy.PoiRaid ? "①" : "②")} 컨테이너만 — 연 상자·트렁크에서 나온 아이템/레이드 (지금 → 새):");
                foreach (string id in r.Density.FoundById.Keys.Union(r.Baseline.FoundById.Keys).OrderBy(k => k, StringComparer.Ordinal))
                    sb.Append(string.Format(ci, " {0} {1:0.00}→{2:0.00}", id,
                        r.Baseline.FoundById.TryGetValue(id, out double b) ? b / raids : 0, r.Density.FoundById.TryGetValue(id, out double d) ? d / raids : 0));
                sb.AppendLine();
            }
            return sb.ToString();
        }

        // 조정 도구(배치 전용, 메뉴 없음): 설정·테이블 사본의 활성 비율·테이블을 바꿔 가며 표준 장비 ①·② 배수(컨테이너만)를 한 줄씩 로그(에셋은 바꾸지 않는다).
        // -batchmode -quit -projectPath . -executeMethod Ashfall.EditorTools.LootEconomySimulator.SweepRatios -logFile sweep.log
        public static void SweepRatios()
        {
            Scene scene = EditorSceneManager.OpenScene(M4SceneSetup.RaidScenePath, OpenSceneMode.Single);
            World world = FromScene(scene);
            Settings s = SettingsFromData();
            LootSpawnConfigSO original = world.Spawn;
            const int n = 200;
            var crate = AssetDatabase.LoadAssetAtPath<LootTableSO>(M4DataSetup.LootCratePath);
            LootTableSO Clone(LootTableSO t, Vector2Int rolls)
            {
                LootTableSO c = Object.Instantiate(t);
                c.rollCount = rolls;
                return c;
            }
            LootZoneConfig poiZone = original.zones.First(z => LootZoneRules.IsPoi(z.zoneId));
            LootZoneConfig lowZone = original.zones.First(z => !LootZoneRules.IsPoi(z.zoneId));
            var tables = new (string name, LootTableSO low, LootTableSO trunk)[]
            {
                ("지금 LOW·TRUNK", lowZone.crateTable, lowZone.trunkTable),
                ("LOW=CRATE(1~2)", Clone(crate, new Vector2Int(1, 2)), lowZone.trunkTable),
                ("LOW=CRATE(1~2), TRUNK=CRATE(0~2)", Clone(crate, new Vector2Int(1, 2)), Clone(crate, new Vector2Int(0, 2))),
                ("LOW=CRATE, TRUNK=CRATE(0~2)", crate, Clone(crate, new Vector2Int(0, 2))),
                ("LOW=CRATE, TRUNK=CRATE", crate, crate),
            };
            var baseline = new Dictionary<Strategy, Stats>();
            foreach (Strategy st in new[] { Strategy.PoiRaid, Strategy.Greedy })
                baseline[st] = Run(world, MapMode.Baseline, st, Kit.Standard, s, n, false);
            var sb = new StringBuilder($"{Tag} 비율·테이블 훑기 (N = {n}, 표준 장비, 컨테이너만, 지금 평균 ① {baseline[Strategy.PoiRaid].Mean:0} ② {baseline[Strategy.Greedy].Mean:0})\n" +
                                       "| 테이블 | POI 상자 | 길목·외곽 상자 | 트렁크 | 활성/레이드 | ① | ② |\n|---|---|---|---|---|---|---|\n");
            foreach (var t in tables)
                foreach (float poi in new[] { 0.35f, 0.4f, 0.45f })
                    foreach (float low in new[] { 0.3f, 0.4f, 0.5f })
                        foreach (float trunk in new[] { 0.3f, 0.5f })
                        {
                            LootSpawnConfigSO copy = Object.Instantiate(original);
                            for (int i = 0; i < copy.zones.Length; i++)
                            {
                                bool isPoi = LootZoneRules.IsPoi(copy.zones[i].zoneId);
                                copy.zones[i].crateActiveRatio = isPoi ? poi : low;
                                copy.zones[i].trunkActiveRatio = trunk;
                                copy.zones[i].crateTable = isPoi ? poiZone.crateTable : t.low;
                                copy.zones[i].trunkTable = t.trunk;
                            }
                            world.Spawn = copy;
                            double R(Strategy st) => Run(world, MapMode.Density, st, Kit.Standard, s, n, false).Mean / Math.Max(1, baseline[st].Mean);
                            Stats active = Run(world, MapMode.Density, Strategy.PoiRaid, Kit.Standard, s, 1, false);
                            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "| {0} | {1:0.00} | {2:0.00} | {3:0.00} | {4:0} | {5:0.00} | {6:0.00} |",
                                t.name, poi, low, trunk, active.ActiveContainers, R(Strategy.PoiRaid), R(Strategy.Greedy)));
                            Object.DestroyImmediate(copy);
                        }
            world.Spawn = original;
            Debug.Log(sb.ToString());
        }

        // 메뉴·배치: 레이드 씬을 열어(저장 안 함) N = DefaultRaids로 돌리고 표를 로그로 남긴다.
        // -batchmode -quit -projectPath . -executeMethod Ashfall.EditorTools.LootEconomySimulator.SimulateMenu -logFile sim.log
        [MenuItem("Ashfall/Tools/Simulate Loot Economy")]
        public static void SimulateMenu()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            Scene scene = EditorSceneManager.OpenScene(M4SceneSetup.RaidScenePath, OpenSceneMode.Single);
            World world = FromScene(scene);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            List<Row> rows = RunAll(world, SettingsFromData(), DefaultRaids);
            Debug.Log($"{Tag} ({watch.Elapsed.TotalSeconds:0.0}s, 목표 배수 {TargetMin}~{TargetMax})\n{Report(world, rows, DefaultRaids)}");
        }
    }
}
