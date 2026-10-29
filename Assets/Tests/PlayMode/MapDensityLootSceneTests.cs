#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Ashfall.AI;
using Ashfall.Audio;
using Ashfall.Core;
using Ashfall.Inventory;
using Ashfall.Player;
using Ashfall.Raid;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ashfall.Tests.PlayMode
{
    // 맵 밀도 ③ 상자·트렁크·루트 (docs/design/MapDensity_design.md §5.3·§5.4·§7 3·§10, SYS-INV §6): Ashfall/Setup/Apply Map Density가 적용한 Raid_CrimsonBasin에서
    // 루트 지점 개수·구조(상자 133·트렁크 70), 레이드 시작 때 지휘자가 고정 시드로 계획대로 활성(결정적), 비활성 = 빈 컨테이너(Q1 A),
    // 승용차 뒤에서 F 레이캐스트로 트렁크 열기, 레이드 중 세이브 파일 변화 없음(RULE-03).
    // 이름·경로는 MapDensityLootApply·MapDensityDataSetup 계약 값(테스트 asmdef는 EditorTools를 참조하지 않는다).
    public class MapDensityLootSceneTests
    {
        private const string LootConfigPath = "Assets/_Project/Data/Inventory/LootSpawnConfig.asset";
        private const string LootGroup = "MapDensity_Loot";
        private const string DirectorName = "LootSpawnDirector";
        private const int ExpectedCrates = 133, ExpectedTrunks = 70, ExpectedNewCrates = 112, FlareCrates = 2;
        private const string SpawnWest = "SP-W";
        private const float TrunkAccessM = 0.3f + 1.2f;   // 트렁크 중심 → 바깥 면(깊이 0.6/2) → 접근점 1.2m
        private static readonly Regex VehicleName = new Regex(@"^Vehicle_\d+$");

        private LootSpawnConfigSO cfg;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            GameManager gm = GameManager.Instance;
            if (gm != null)
            {
                gm.Flow.SetWorldPaused(false);
                Time.timeScale = 1f;
            }
            cfg = AssetDatabase.LoadAssetAtPath<LootSpawnConfigSO>(LootConfigPath);
            Assert.IsNotNull(cfg, $"{LootConfigPath} 없음 (Ashfall/Setup/Apply Map Density)");
            yield return null;
        }

        private static List<LootSpawnSlot> Slots() =>
            Object.FindObjectsByType<LootSpawnSlot>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID).ToList();

        private static LootSpawnDirector Director()
        {
            LootSpawnDirector[] all = Object.FindObjectsByType<LootSpawnDirector>(FindObjectsSortMode.None);
            Assert.AreEqual(1, all.Length, "LootSpawnDirector는 씬에 1개");
            Assert.IsNull(all[0].transform.parent, "씬 최상위");
            Assert.AreEqual(DirectorName, all[0].name);
            return all[0];
        }

        [UnityTest]
        public IEnumerator RaidScene_LootPoints_CountsAndStructure()
        {
            yield return M4Raid.LoadScene(SceneNames.RaidCrimsonBasin);
            List<LootSpawnSlot> slots = Slots();
            Assert.AreEqual(ExpectedCrates + ExpectedTrunks, slots.Count, "루트 지점 약 200 (상자 133 + 트렁크 70)");
            Assert.AreEqual(slots.Count, slots.Select(s => s.SlotKey).Distinct().Count(), "slotKey 고유");
            foreach (LootZoneConfig z in cfg.zones)
            {
                Assert.AreEqual(z.crateTargetCount, slots.Count(s => s.ZoneId == z.zoneId && s.Kind == LootSlotKind.Crate), $"{z.zoneId} 상자");
                Assert.AreEqual(z.trunkTargetCount, slots.Count(s => s.ZoneId == z.zoneId && s.Kind == LootSlotKind.Trunk), $"{z.zoneId} 트렁크");
            }
            Assert.AreEqual(FlareCrates, slots.Count(s => s.AlwaysActive), "신호탄 상자 2개만 항상 활성");
            Assert.IsTrue(slots.Where(s => s.AlwaysActive).All(s => s.name.StartsWith("FlareCrate_")));
            Assert.AreSame(cfg, Director().Config);

            // 모든 컨테이너에 슬롯 (시체는 레이드 중에만 생긴다)
            foreach (LootContainer c in Object.FindObjectsByType<LootContainer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Assert.IsNotNull(c.GetComponent<LootSpawnSlot>(), $"{c.name}: 슬롯 없음");

            // 새 상자: Environment/MapDensity_Loot, 1m 큐브, 팔레트 상자 머티리얼, 컬 LODGroup, 금속 재질
            Transform group = GameObject.Find("Environment").transform.Find(LootGroup);
            Assert.IsNotNull(group, $"Environment/{LootGroup} 없음");
            Assert.AreEqual(ExpectedNewCrates, group.childCount, "새 상자 112");
            foreach (Transform crate in group)
            {
                StringAssert.StartsWith("LootCrate_", crate.name);
                Assert.AreEqual("M_Crate", crate.GetComponent<MeshRenderer>().sharedMaterial.name, crate.name);
                Assert.IsNotNull(crate.GetComponent<LODGroup>(), $"{crate.name}: 컬 LODGroup");
                Assert.AreEqual(SurfaceKind.Metal, crate.GetComponent<GroundSurface>().Kind, crate.name);
                var lc = crate.GetComponent<LootContainer>();
                Assert.AreEqual(cfg.crateDisplayName, lc.DisplayName);
                Assert.AreEqual(cfg.crateGridSize, lc.GridSize);
                Assert.IsNull(lc.SerializedLootTable, $"{crate.name}: 새 지점은 직렬화 테이블 없음(지휘자가 못 돌면 루트 적음 쪽, 리뷰 L4)");
            }

            // 트렁크: 승용차("Vehicle_nn", 블록 길이 ≤ 5m) 래퍼의 자식, 렌더러 없음, 비트리거 상자 콜라이더 (설계 §5.4)
            foreach (LootSpawnSlot trunk in slots.Where(s => s.Kind == LootSlotKind.Trunk))
            {
                Assert.IsTrue(VehicleName.IsMatch(trunk.transform.parent.name), $"{trunk.name}: 부모 {trunk.transform.parent.name}");
                Assert.IsNull(trunk.GetComponent<Renderer>(), $"{trunk.name}: 렌더러 없음 (차량 스킨 그대로)");
                Assert.IsNull(trunk.transform.Find("Skin"), $"{trunk.name}: 상자 스킨 없음");
                var box = trunk.GetComponent<BoxCollider>();
                Assert.IsNotNull(box);
                Assert.IsFalse(box.isTrigger, "상호작용 레이캐스트는 트리거를 무시한다");
                var lc = trunk.GetComponent<LootContainer>();
                Assert.AreEqual(cfg.trunkDisplayName, lc.DisplayName);
                Assert.AreEqual(cfg.trunkGridSize, lc.GridSize);
                Assert.IsNull(lc.SerializedLootTable, $"{trunk.name}: 새 지점은 직렬화 테이블 없음(리뷰 L4)");
                Transform block = trunk.transform.parent.Find("Block");
                Assert.LessOrEqual(Mathf.Max(block.lossyScale.x, block.lossyScale.z), 5f, "승용차만");
            }
            int cars = GameObject.Find("Environment").GetComponentsInChildren<Transform>(true)
                .Count(t => VehicleName.IsMatch(t.name) && t.Find("Block") != null && Mathf.Max(t.Find("Block").lossyScale.x, t.Find("Block").lossyScale.z) <= 5f);
            Assert.AreEqual(ExpectedTrunks, cars, "승용차마다 트렁크 1개");
        }

        private static LootActivationPlanner.Decision[] ExpectedPlan(List<LootSpawnSlot> slots, LootSpawnConfigSO cfg, int lootSeed) =>
            LootActivationPlanner.Plan(slots.Select(s => s.ToPlannerSlot()).ToList(), cfg.RatioOf, cfg.minActivePerZone, lootSeed);

        [UnityTest]
        public IEnumerator RaidStart_FixedSeed_ActivatesPlannedSet_InactiveAreEmpty_Deterministic()
        {
            var raid = new M4Raid();
            yield return M4Raid.Load(raid, SpawnWest);
            LootSpawnDirector director = Director();
            int lootSeed = raid.Controller.LootSeed;
            Assert.AreNotEqual(0, lootSeed);
            Assert.AreEqual(SeedMath.RaidLootSeed(raid.Controller.UsedSeed), lootSeed, "RaidStarted.LootSeed = Mix(쓴 시드, StableHash(\"LOOT\"))");
            Assert.AreEqual(lootSeed, director.LastLootSeed, "지휘자가 RaidStarted를 받았다");

            List<LootSpawnSlot> slots = Slots();
            LootActivationPlanner.Decision[] plan = ExpectedPlan(slots, cfg, lootSeed);
            int expectedActive = plan.Count(d => d.Active);
            Assert.AreEqual(expectedActive, director.LastActiveCount, "활성 수 = 계획");
            Assert.AreEqual(slots.Count, director.LastSlotCount);
            // 레이드당 활성 = 구역·종류별 round(n × 비율) 합 + 신호탄 2 (표본 추출 — 레이드마다 같다)
            Assert.That(expectedActive, Is.InRange(60, 90), "레이드당 활성 약 75");
            var activeKeys = new HashSet<string>();
            for (int i = 0; i < slots.Count; i++)
            {
                LootSpawnSlot s = slots[i];
                var c = s.GetComponent<LootContainer>();
                Assert.AreEqual(plan[i].Active, director.WasActive(s.SlotKey), s.SlotKey);
                if (cfg.inactiveMode == InactiveLootMode.Empty)   // 리뷰 L2: 활성 판정은 WasActive, 빈 컨테이너는 Empty 모드일 때만
                    Assert.AreEqual(!plan[i].Active, c.IsEmptyForRaid, $"{s.SlotKey}: 비활성 = 빈 컨테이너");
                if (plan[i].Active)
                {
                    activeKeys.Add(s.SlotKey);
                    Assert.AreEqual(plan[i].ContentSeed, c.Seed, $"{s.SlotKey}: 내용물 시드 = Mix(lootSeed, StableHash(slotKey))");
                }
                if (plan[i].Active && !s.AlwaysActive)
                {
                    cfg.TryGetZone(s.ZoneId, out LootZoneConfig zone);
                    Assert.AreSame(zone.TableOf(s.Kind), c.LootTable, $"{s.SlotKey}: 구역 테이블");
                }
            }
            Assert.IsTrue(slots.Where(s => s.AlwaysActive).All(s => activeKeys.Contains(s.SlotKey)), "신호탄 상자 항상 활성 (EXT-03)");

            // 같은 스폰(= 같은 시드)으로 다시 시작하면 같은 집합·같은 내용물
            LootContainer sample = slots.First(s => activeKeys.Contains(s.SlotKey) && s.Kind == LootSlotKind.Crate && !s.AlwaysActive).GetComponent<LootContainer>();
            string sampleKey = sample.GetComponent<LootSpawnSlot>().SlotKey;
            List<string> sampleItems = sample.EnsureModel(0.5f).Items.Select(p => p.Item.Definition.id).ToList();
            Assert.Greater(sampleItems.Count, 0, "활성 상자는 굴림 1회 이상");

            var again = new M4Raid();
            yield return M4Raid.Load(again, SpawnWest);
            Assert.AreEqual(lootSeed, again.Controller.LootSeed);
            List<LootSpawnSlot> slots2 = Slots();
            LootSpawnDirector director2 = Director();
            CollectionAssert.AreEquivalent(activeKeys, slots2.Where(s => director2.WasActive(s.SlotKey) == true).Select(s => s.SlotKey), "같은 시드 = 같은 활성 집합");
            LootContainer sample2 = slots2.Single(s => s.SlotKey == sampleKey).GetComponent<LootContainer>();
            CollectionAssert.AreEqual(sampleItems, sample2.EnsureModel(0.5f).Items.Select(p => p.Item.Definition.id).ToList(), "같은 시드 = 같은 내용물");
        }

        [UnityTest]
        public IEnumerator InactiveContainer_OpensEmpty_SearchCompletesImmediately()
        {
            var raid = new M4Raid();
            yield return M4Raid.Load(raid, SpawnWest);
            Assume.That(cfg.inactiveMode, Is.EqualTo(InactiveLootMode.Empty), "Q1 A(빈 컨테이너)일 때만");
            LootSpawnDirector director = Director();
            LootContainer empty = Slots().Where(s => director.WasActive(s.SlotKey) == false).Select(s => s.GetComponent<LootContainer>()).First();
            Assert.IsTrue(empty.IsEmptyForRaid);
            empty.Interact(raid.InventoryBehaviour);   // F 상호작용과 같은 호출
            ILootView loot = raid.Inventory.ActiveLoot;
            Assert.IsNotNull(loot, "빈 컨테이너도 열린다 (겉모습·프롬프트 그대로)");
            Assert.AreEqual(0, loot.Items.Count);
            Assert.IsTrue(loot.SearchComplete, "검색할 것이 없어 바로 끝");
            raid.Inventory.CloseLoot();
            yield return null;
        }

        [UnityTest]
        public IEnumerator Trunk_StandBehindCar_RaycastF_OpensTrunk()
        {
            var raid = new M4Raid();
            yield return M4Raid.Load(raid, SpawnWest);
            yield return raid.RemoveEnemies();
            // 스폰에서 가까운 트렁크 (적이 없어도 멀리 가지 않게)
            Vector3 spawn = raid.Motor.transform.position;
            LootSpawnSlot trunk = Slots().Where(s => s.Kind == LootSlotKind.Trunk).OrderBy(s => (s.transform.position - spawn).sqrMagnitude).First();
            Collider trunkCollider = trunk.GetComponent<Collider>();
            Vector3 outward = Vector3.ProjectOnPlane(trunk.transform.right, Vector3.up).normalized;
            Vector3 stand = trunk.transform.position + outward * TrunkAccessM;
            Assert.IsTrue(Physics.Raycast(stand + Vector3.up * 5f, Vector3.down, out RaycastHit ground, 20f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore));
            Vector3 feet = ground.point + Vector3.up * 0.05f;
            Vector3 target = trunkCollider.bounds.center;

            using (var prompts = new EventLog<InteractionPromptChanged>(raid.Events))
            using (var keyboard = new SimKeyboard())
            {
                Vector3 look = target - feet;
                raid.Placement.PlaceAt(feet, Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg);
                yield return null;
                yield return null;
                Vector3 toTarget = target - raid.Motor.EyePosition;
                raid.LookDown(Mathf.Atan2(-toTarget.y, new Vector2(toTarget.x, toTarget.z).magnitude) * Mathf.Rad2Deg);
                yield return null;
                yield return null;
                yield return M3Scene.WaitUntil(() => prompts.Count > 0 && prompts.Last.Visible && prompts.Last.Kind == InteractionKind.OpenContainer,
                    3f, () => $"트렁크 프롬프트 없음 ({trunk.SlotKey}, 마지막 {(prompts.Count > 0 ? prompts.Last.TargetName + "/" + prompts.Last.Visible : "없음")})");
                Assert.AreEqual(cfg.trunkDisplayName, prompts.Last.TargetName, "프롬프트 \"열기: 차량 트렁크\"");
                yield return keyboard.Tap(Key.F);
                ILootView loot = raid.Inventory.ActiveLoot;
                Assert.IsNotNull(loot, "F → 트렁크 열림");
                Assert.AreEqual(cfg.trunkDisplayName, loot.DisplayName);
                Assert.AreSame(trunk.GetComponent<LootContainer>().Model, loot, "그 트렁크의 모델");
                Assert.AreEqual(cfg.trunkGridSize, trunk.GetComponent<LootContainer>().Model.Size, "트렁크 그리드 5×3");
                raid.Inventory.CloseLoot();
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator Raid_LootActivationAndOpening_DoNotTouchSaveFiles()
        {
            FileSaveStorage storage = SaveSandbox.UseFreshStorage("loot-density");
            var raid = new M4Raid();
            yield return M4Raid.Load(raid, SpawnWest);
            Dictionary<string, byte[]> before = Snapshot(storage.DirectoryPath);
            // 활성·빈 컨테이너 몇 개를 열고 검색을 끝까지
            foreach (LootContainer c in Slots().Select(s => s.GetComponent<LootContainer>()).Take(6))
            {
                c.Interact(raid.InventoryBehaviour);
                ILootView loot = raid.Inventory.ActiveLoot;
                Assert.IsNotNull(loot);
                yield return M3Scene.WaitUntil(() => loot.SearchComplete, 10f, () => $"{c.name} 검색");
                raid.Inventory.CloseLoot();
                yield return null;
            }
            yield return M2Scene.WaitSeconds(0.5f);
            Dictionary<string, byte[]> after = Snapshot(storage.DirectoryPath);
            CollectionAssert.AreEquivalent(before.Keys, after.Keys, "레이드 중 세이브 파일이 생기거나 지워지지 않는다 (RULE-03)");
            foreach (KeyValuePair<string, byte[]> pair in before)
                CollectionAssert.AreEqual(pair.Value, after[pair.Key], $"{pair.Key}: 레이드 중 변경 없음");
        }

        // ── 리뷰 M2: 저장된 레이드 씬에서 루트 지점 203곳 전부 도달 (설계 §7 3) ──
        // 접근점 계약 값(MapDensityLootLayout.AccessDistanceM, MapDensityLootApply.TrunkSizeM.x/2 + TrunkAccessDistanceM·AccessOnMeshM) 복사.
        private const float CrateAccessM = 1.2f;              // 상자 중심 → 네 면 앞
        private const float AccessOnMeshM = 1.0f;             // 접근점 ↔ NavMesh (수평)
        private const float AccessSampleRadiusM = 2.5f;       // SamplePosition 반경 (AccessOnMeshM + 1.5 — 적용 확인과 같다)
        private const string PlayerConfigPath = "Assets/_Project/Data/Player/PlayerConfig.asset";
        private const string InventoryConfigPath = "Assets/_Project/Data/Inventory/InventoryConfig.asset";

        [UnityTest]
        public IEnumerator RaidScene_AllLootPoints_ReachableFromSpawn_EyeRayHitsContainerFirst()
        {
            yield return M4Raid.LoadScene(SceneNames.RaidCrimsonBasin);
            var player = AssetDatabase.LoadAssetAtPath<PlayerConfigSO>(PlayerConfigPath);
            var inventory = AssetDatabase.LoadAssetAtPath<InventoryConfigSO>(InventoryConfigPath);
            Assert.IsNotNull(player);
            Assert.IsNotNull(inventory);
            float eyeHeight = player.standHeight - player.eyeBelowTop;
            float range = inventory.interactRangeM;
            PlayerSpawnPoint spawn = Object.FindObjectsByType<PlayerSpawnPoint>(FindObjectsSortMode.None).Single(s => s.Id == SpawnWest);
            Assert.IsTrue(NavMesh.SamplePosition(spawn.FeetPosition, out NavMeshHit reference, 2f, NavMesh.AllAreas), "기준 스폰 NavMesh");
            Transform playerRoot = Object.FindFirstObjectByType<PlayerMotor>()?.transform.root;
            Physics.SyncTransforms();

            var watch = System.Diagnostics.Stopwatch.StartNew();
            var path = new NavMeshPath();
            var failures = new List<string>();
            List<LootSpawnSlot> slots = Slots();
            Assert.AreEqual(ExpectedCrates + ExpectedTrunks, slots.Count);
            foreach (LootSpawnSlot slot in slots)
            {
                Collider target = slot.GetComponent<Collider>();
                Assert.IsNotNull(target, slot.SlotKey);
                Vector3 p = slot.transform.position;
                Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z).normalized;
                Vector3[] access = slot.Kind == LootSlotKind.Trunk
                    ? new[] { p + Flat(slot.transform.right) * TrunkAccessM }
                    : new[] { p + Flat(slot.transform.right) * CrateAccessM, p - Flat(slot.transform.right) * CrateAccessM,
                              p + Flat(slot.transform.forward) * CrateAccessM, p - Flat(slot.transform.forward) * CrateAccessM };
                string why = "접근점 없음";
                bool ok = false;
                foreach (Vector3 a in access)
                {
                    if (!NavMesh.SamplePosition(a, out NavMeshHit hit, AccessSampleRadiusM, NavMesh.AllAreas)
                        || new Vector2(hit.position.x - a.x, hit.position.z - a.z).magnitude > AccessOnMeshM)
                    {
                        why = $"접근점 ({a.x:0.0}, {a.z:0.0}) {AccessOnMeshM}m 안 NavMesh 없음";
                        continue;
                    }
                    // 플레이어 연결성(걸어서 닿는가) — 부분 경로 끝에서 이어 찾기 허용 (NavPathProbe.TryConnected)
                    if (!NavPathProbe.TryConnected(reference.position, hit.position, path))
                    {
                        why = $"접근점 ({a.x:0.0}, {a.z:0.0}) 기준 스폰에서 닿지 않음";
                        continue;
                    }
                    Vector3 eye = hit.position + Vector3.up * eyeHeight;
                    Vector3 dir = (target.bounds.center - eye).normalized;
                    Collider first = null;
                    float best = float.MaxValue;
                    foreach (RaycastHit h in Physics.RaycastAll(eye, dir, range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                        if (h.distance < best && (playerRoot == null || h.collider.transform.root != playerRoot))
                        {
                            best = h.distance;
                            first = h.collider;
                        }
                    if (first != target)
                    {
                        why = $"접근점 ({a.x:0.0}, {a.z:0.0}) 눈높이 레이 첫 충돌 {(first != null ? first.name : "없음")}";
                        continue;
                    }
                    ok = true;
                    break;
                }
                if (!ok)
                    failures.Add($"{slot.SlotKey} ({p.x:0.0}, {p.z:0.0}): {why}");
            }
            Debug.Log($"[MapDensityLootSceneTests] 루트 지점 도달 {slots.Count - failures.Count}/{slots.Count}, 처리 {watch.Elapsed.TotalSeconds:0.00}s");
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        private static Dictionary<string, byte[]> Snapshot(string dir)
        {
            var files = new Dictionary<string, byte[]>();
            if (Directory.Exists(dir))
                foreach (string f in Directory.GetFiles(dir))
                    files[Path.GetFileName(f)] = File.ReadAllBytes(f);
            return files;
        }
    }
}
#endif
