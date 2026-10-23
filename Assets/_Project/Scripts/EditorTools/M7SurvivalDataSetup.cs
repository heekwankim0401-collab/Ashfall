using System.Collections.Generic;
using System.Linq;
using Ashfall.Core;
using Ashfall.Hideout;
using Ashfall.Player;
using Ashfall.Raid;
using UnityEditor;
using UnityEngine;

namespace Ashfall.EditorTools
{
    // M7 7C 생존·제작 데이터 (M7 설계 §3.6~§3.11, §4.10, Q32~Q48).
    // ① 새 아이템(없을 때만): ITM-WTR-01 물병·ITM-FOD-01 통조림·MED-ANT-01 해독제(MedicalDefinitionSO, Q42),
    //    GEAR-GSM-01 방독면(ArmorDefinitionSO kind Helmet·armorClass 0·covers 빈·blocksStormRadiation, Q37·Q38)
    // ② 희귀도 §3.11 — 전 아이템. 기존 에셋은 rarity가 Scrap(0)일 때만 기록(0 = 미설정, M2 메뉴 기본값). 다른 값은 조정으로 보고 보존.
    // ③ PlayerConfig.asset §3.6 생존 필드 — 파일에 없는 필드만 명시 기록(값 = GDD §4.3 + 설계 임시값)
    // ④ RaidConfig.radiationZones — RAD-REF(§3.10)가 없으면 끝에 추가. 있으면 값 보존.
    // ⑤ CraftingConfig.asset(§3.8 레시피 8개) — 없으면 생성, 있으면 빠진 레시피 id만 끝에 추가.
    // ⑥ TraderConfig.catalog — 물·음식·해독제·방독면이 없으면 끝에 추가(12ga 탄 상자는 7A Q20이 이미 넣었다).
    // ⑦ 루트 테이블 — §3.9(7B-1b 메모 목록) 항목이 없으면 끝에 추가. 이미 있으면 가중치 보존.
    // ⑧ ItemDatabase 재수집(M5DataSetup.CreateItemDatabase).
    // GDD 값은 §4.3 생존 수치(100/0.1/0.5, 100/0.07/−50%, 누적 100 → 최대 HP −20%)뿐이다. 나머지는 전부 TODO(GDD-?) 임시값(설계 §3).
    // 멱등: 값이 바뀔 때만 SetDirty한다. 두 번째 실행은 파일·GUID를 바꾸지 않는다.
    // 배치모드: -executeMethod Ashfall.EditorTools.M7SurvivalDataSetup.CreateData
    public static class M7SurvivalDataSetup
    {
        private const string Tag = "[M7SurvivalDataSetup]";

        // ---------- §3.11 희귀도 표 (TODO(GDD-?) Q48) ----------
        // 고철 = JNK-01~03, 내장 튜브·적 전용·탄약 정의 = Scrap(표시 안 됨) — Scrap 목표는 기록할 것이 없어 표에 넣지 않는다.
        public static readonly (string path, string id, Rarity rarity)[] RarityTable =
        {
            // 일반
            (M3DataSetup.Mag9mm12Path, "MAG-9MM-12", Rarity.Common),
            (M3DataSetup.Mag9mm30Path, "MAG-9MM-30", Rarity.Common),
            (M7Names.Box12Ga8Path, M7Names.Box12Ga8Id, Rarity.Common),
            (M5DataSetup.BandagePath, "MED-BND-01", Rarity.Common),
            (M7Names.WaterPath, M7Names.WaterId, Rarity.Common),
            (M7Names.FoodPath, M7Names.FoodId, Rarity.Common),
            (M4DataSetup.MapItemPath, "ITM-MAP-01", Rarity.Common),
            (M5DataSetup.VestPath, "RIG-01", Rarity.Common),
            // 우수
            (M3DataSetup.SmgPath, "WPN-SMG-01", Rarity.Uncommon),
            (M7Names.ShotgunPath, M7Names.ShotgunId, Rarity.Uncommon),
            (M3DataSetup.Mag556Path, "MAG-556-30", Rarity.Uncommon),
            (M7Names.Mag762_5Path, M7Names.Mag762_5Id, Rarity.Uncommon),
            (M5DataSetup.TourniquetPath, "MED-TRQ-01", Rarity.Uncommon),
            (M5DataSetup.SplintPath, "MED-SPL-01", Rarity.Uncommon),
            (M4DataSetup.FlarePath, "ITM-FLR-01", Rarity.Uncommon),
            (M5DataSetup.BackpackPath, "BAG-01", Rarity.Uncommon),
            (M5DataSetup.HelmetPath, "ARM-HLM-01", Rarity.Uncommon),
            (M7Names.AntiRadPath, M7Names.AntiRadId, Rarity.Uncommon),
            (M7Names.GasMaskPath, M7Names.GasMaskId, Rarity.Uncommon),
            (M3DataSetup.PistolPath, "WPN-PST-01", Rarity.Uncommon),
            // 희귀
            (M3DataSetup.RiflePath, "WPN-RIF-01", Rarity.Rare),
            (M7Names.DmrPath, M7Names.DmrId, Rarity.Rare),
            (M5DataSetup.BodyArmorPath, "ARM-BDY-01", Rarity.Rare),
            // 유물
            (M7Names.RelicPath, M7Names.RelicId, Rarity.Relic),
        };

        // ---------- §3.6 생존 (PlayerConfigSO) ----------
        // GDD §4.3: 수분 100(−0.1/s), 수분 0 → −0.5 HP/s, 에너지 100(−0.07/s), 에너지 0 → 스태미나 재생 −50%, 방사능 누적 100 → 최대 HP −20%.
        public const float HydrationMax = 100f;
        public const float HydrationDecayPerSec = 0.1f;
        public const float DehydratedHpPerSec = 0.5f;                     // 머리를 뺀 부위마다 해석 TODO(GDD-?) Q33
        public const float StomachDestroyedHydrationMultiplier = 2f;      // TODO(GDD-?) Q34
        public const float EnergyMax = 100f;
        public const float EnergyDecayPerSec = 0.07f;
        public const float StarvingStaminaRegenMultiplier = 0.5f;
        public const float RadiationSicknessDose = 100f;
        public const float RadiationSicknessMaxHpMultiplier = 0.8f;
        public const float RadiationWarnDose = 50f;                       // TODO(GDD-?) Q43
        public const float SurvivalLowRatio = 0.25f;                      // TODO(GDD-?) Q43

        // ---------- §3.10 정유소 구역 (TODO(GDD-?) Q39) ----------
        public const float RefineryRadiusM = 70f;
        public const float RefineryRatePerSec = 0.5f;
        public const float RefineryEdgeFalloffM = 15f;
        public static Vector2 RefineryCenterXZ => CrimsonBasinLayout.Refinery.CenterXZ;   // POI-REF 중심 (300, −60)

        // ---------- §3.8 레시피 (TODO(GDD-?) Q47) ----------
        // 산출 매도가 ≤ 재료 매수가(제작이 탄피 생성기가 되지 않게). 설계 표의 RCP-AMMO-12GA·RCP-MED-SPL은 JNK-01 × 1이면
        // 산출 매도(80·48) > 재료 매수(40)라 JNK-01 × 2로 올렸다(7C-1b 구현 메모·CHANGELOG).
        public readonly struct RecipePlan
        {
            public readonly string Id;
            public readonly string DisplayName;
            public readonly string OutputPath;
            public readonly int OutputCount;
            public readonly (string path, int count)[] Inputs;

            public RecipePlan(string id, string displayName, string outputPath, int outputCount, params (string path, int count)[] inputs)
            {
                Id = id; DisplayName = displayName; OutputPath = outputPath; OutputCount = outputCount; Inputs = inputs;
            }
        }

        public static readonly RecipePlan[] Recipes =
        {
            new RecipePlan(M7Names.RecipeAmmo9mm12Id, "권총 탄창 (12발)", M3DataSetup.Mag9mm12Path, 1,
                (M7Names.PipePath, 1), (M7Names.WiresPath, 1)),
            new RecipePlan(M7Names.RecipeAmmo556Id, "5.56 탄창 (30발)", M3DataSetup.Mag556Path, 1,
                (M7Names.PipePath, 2), (M7Names.WiresPath, 1)),
            new RecipePlan(M7Names.RecipeAmmo762Id, "7.62 탄창 (5발)", M7Names.Mag762_5Path, 1,
                (M7Names.PipePath, 1), (M7Names.EnginePartPath, 1)),
            new RecipePlan(M7Names.RecipeAmmo12GaId, "12ga 탄 상자 (8발)", M7Names.Box12Ga8Path, 1,
                (M7Names.PipePath, 2)),                                    // 설계 × 1 → × 2 (가치 확인)
            new RecipePlan(M7Names.RecipeBandageId, "붕대 × 2", M5DataSetup.BandagePath, 2,
                (M7Names.WiresPath, 1)),
            new RecipePlan(M7Names.RecipeTourniquetId, "지혈대", M5DataSetup.TourniquetPath, 1,
                (M7Names.WiresPath, 2)),
            new RecipePlan(M7Names.RecipeSplintId, "부목", M5DataSetup.SplintPath, 1,
                (M7Names.PipePath, 2)),                                    // 설계 × 1 → × 2 (가치 확인)
            new RecipePlan(M7Names.RecipeAntiRadId, "해독제", M7Names.AntiRadPath, 1,
                (M7Names.EnginePartPath, 1), (M7Names.WiresPath, 1)),
        };

        // ---------- 상인·루트 추가분 (TODO(GDD-?) 가중치) ----------

        // 상인 판매 목록 추가분 (이 순서로 끝에). 12ga 탄 상자는 7A(M7WeaponDataSetup.TraderAdditionPaths)가 넣는다.
        public static readonly string[] TraderAdditionPaths =
        {
            M7Names.WaterPath, M7Names.FoodPath, M7Names.AntiRadPath, M7Names.GasMaskPath,
        };

        // §3.9 루트 테이블 추가분 (표마다 이 순서로 끝에). 7A·7B가 넣은 항목은 앞에 있다.
        public static readonly (string tablePath, (string itemPath, float weight)[] entries)[] LootAdditions =
        {
            (M4DataSetup.LootCratePath, new[]
            {
                (M7Names.WaterPath, 2f), (M7Names.FoodPath, 2f), (M7Names.AntiRadPath, 0.5f), (M7Names.GasMaskPath, 0.5f),   // 방독면: 설계 0.3 → 0.5 (M4 Q7-A 신호탄 0.5가 최저 가중치)
                (M7Names.EnginePartPath, 0.5f),
            }),
            (M3DataSetup.LootScvPath, new[] { (M7Names.WaterPath, 1f), (M7Names.FoodPath, 1f), (M7Names.Box12Ga8Path, 0.5f) }),
            (M3DataSetup.LootRdrPath, new[] { (M7Names.WaterPath, 1f), (M7Names.FoodPath, 1f) }),
            (M7Names.LootSniperPath, new[] { (M7Names.AntiRadPath, 1f), (M7Names.WaterPath, 1f) }),
            (M7Names.LootMutantPath, new[] { (M7Names.AntiRadPath, 0.3f) }),
            (M7Names.LootBossPath, new[] { (M7Names.GasMaskPath, 1f), (M7Names.AntiRadPath, 1f) }),
        };

        [MenuItem("Ashfall/Setup/Create M7 Survival Data")]
        public static void CreateData()
        {
            // M1~M7B 데이터(잡동사니·의약품·탄창·상인·루트 테이블·RaidConfig·PlayerConfig 등)가 먼저 있어야 한다. 이미 있는 에셋은 건너뛴다.
            M7AiDataSetup.CreateData();

            WriteM7SurvivalData();
            Debug.Log($"{Tag} 완료");
        }

        // 7C 몫만 (이전 메뉴를 부르지 않는다). 통합 메뉴·테스트가 재실행 바이트 동일을 확인할 때 쓴다.
        public static void WriteM7SurvivalData()
        {
            CreateItems();
            AssetDatabase.SaveAssets();
            WriteRarities();
            WritePlayerConfig();
            WriteRadiationZones();
            CreateCraftingConfig();
            AppendTraderCatalog();
            AppendLoot();
            AssetDatabase.SaveAssets();
            // 새 정의를 저장한 뒤 DB를 다시 모은다(바뀔 때만 기록).
            M5DataSetup.CreateItemDatabase();
            AssetDatabase.SaveAssets();
        }

        // ---------- ① 새 아이템 (§3.7, 전부 TODO(GDD-?)) ----------

        private static void CreateItems()
        {
            // 물병 — STAT-DEHY 해제 수단 "물"(§4.3). 1회 사용 = 수분 +50, 2회.
            SetupUtil.CreateAssetIfMissing<MedicalDefinitionSO>(M7Names.WaterPath, m =>
            {
                m.id = M7Names.WaterId; m.displayName = "물병";
                m.cures = new StatusEffect[0]; m.healAmount = 0f;
                m.hydrationRestore = 50f; m.energyRestore = 0f; m.curesRadiation = false;
                // TODO(GDD-?): 아래 전부 임시값 (설계 §3.7)
                m.size = new Vector2Int(1, 2); m.weightKg = 0.6f;
                m.useTime = 3f; m.uses = 2;
                m.basePrice = 150; m.rarity = Rarity.Common;
            }, Tag);

            // 통조림 — STAT-HUNG 해제 수단 "음식"(§4.3). 에너지 +60.
            SetupUtil.CreateAssetIfMissing<MedicalDefinitionSO>(M7Names.FoodPath, m =>
            {
                m.id = M7Names.FoodId; m.displayName = "통조림";
                m.cures = new StatusEffect[0]; m.healAmount = 0f;
                m.hydrationRestore = 0f; m.energyRestore = 60f; m.curesRadiation = false;
                // TODO(GDD-?): 아래 전부 임시값 (설계 §3.7)
                m.size = new Vector2Int(1, 1); m.weightKg = 0.4f;
                m.useTime = 4f; m.uses = 1;
                m.basePrice = 200; m.rarity = Rarity.Common;
            }, Tag);

            // 해독제 — STAT-RAD 해제 수단(§4.3). 선량 0 + 최대 HP 원래대로, HP 회복 없음(Q36).
            SetupUtil.CreateAssetIfMissing<MedicalDefinitionSO>(M7Names.AntiRadPath, m =>
            {
                m.id = M7Names.AntiRadId; m.displayName = "해독제";
                m.cures = new StatusEffect[0]; m.healAmount = 0f;
                m.hydrationRestore = 0f; m.energyRestore = 0f; m.curesRadiation = true;
                // TODO(GDD-?): 아래 전부 임시값 (설계 §3.7)
                m.size = new Vector2Int(1, 1); m.weightKg = 0.1f;
                m.useTime = 3f; m.uses = 1;
                m.basePrice = 600; m.rarity = Rarity.Uncommon;
            }, Tag);

            // 방독면 — §8.3 "방독면으로 무효". 헬멧 슬롯 공유(Q37), 폭풍 방사능만 막음(Q38), 방어 기능 없음.
            SetupUtil.CreateAssetIfMissing<ArmorDefinitionSO>(M7Names.GasMaskPath, a =>
            {
                a.id = M7Names.GasMaskId; a.displayName = "방독면";
                a.kind = ArmorKind.Helmet; a.armorClass = 0; a.covers = new Ashfall.Core.BodyPart[0];
                a.blocksStormRadiation = true;
                a.maxDurability = 0f;   // 방어 기능 없음 (§5.3 내구도는 M7 범위 밖, Q19)
                // TODO(GDD-?): 아래 전부 임시값 (설계 §3.7)
                a.size = new Vector2Int(2, 2); a.weightKg = 0.8f;
                a.basePrice = 1500; a.rarity = Rarity.Uncommon;
            }, Tag);
        }

        // ---------- ② 희귀도 (§3.11, TODO(GDD-?) Q48) ----------

        private static void WriteRarities()
        {
            foreach (var (path, id, rarity) in RarityTable)
            {
                var item = SetupUtil.Load<ItemDefinitionSO>(path);
                if (item == null)
                    continue;
                if (item.id != id)
                {
                    Debug.LogError($"{Tag} {path}의 id가 {item.id}다 (기대 {id}). 희귀도를 기록하지 않는다.", item);
                    continue;
                }
                if (!ShouldWriteRarity(item.rarity, rarity))
                    continue;
                item.rarity = rarity;
                EditorUtility.SetDirty(item);
                Debug.Log($"{Tag} {id}.rarity = {rarity}");
            }
        }

        // 기존 아이템 희귀도를 기록할지: 현재 Scrap(0 = 미설정 — M2 메뉴 기본값)이고 목표가 Scrap이 아닐 때만.
        // TODO(GDD-?) Q48: Scrap 아닌 값은 조정으로 보고 보존한다(M3·M4가 Common으로 만든 권총·SMG·신호탄은 §3.11 '우수'와 다르게 남는다).
        public static bool ShouldWriteRarity(Rarity current, Rarity target) => current == Rarity.Scrap && target != Rarity.Scrap;

        // ---------- ③ PlayerConfig 생존 필드 (§3.6) ----------

        private static void WritePlayerConfig()
        {
            string path = M2DataSetup.PlayerConfigPath;
            var c = SetupUtil.Load<PlayerConfigSO>(path);
            if (c == null)
                return;

            // 파일에 없는 필드만 (모두 판정한 뒤 기록 — 중간 저장 없음)
            bool changed = false;
            changed |= WriteIfMissing(path, nameof(PlayerConfigSO.hydrationMax), () => c.hydrationMax = HydrationMax);
            changed |= WriteIfMissing(path, nameof(PlayerConfigSO.hydrationDecayPerSec), () => c.hydrationDecayPerSec = HydrationDecayPerSec);
            changed |= WriteIfMissing(path, nameof(PlayerConfigSO.dehydratedHpPerSec), () => c.dehydratedHpPerSec = DehydratedHpPerSec);
            changed |= WriteIfMissing(path, nameof(PlayerConfigSO.stomachDestroyedHydrationMultiplier),
                () => c.stomachDestroyedHydrationMultiplier = StomachDestroyedHydrationMultiplier);
            changed |= WriteIfMissing(path, nameof(PlayerConfigSO.energyMax), () => c.energyMax = EnergyMax);
            changed |= WriteIfMissing(path, nameof(PlayerConfigSO.energyDecayPerSec), () => c.energyDecayPerSec = EnergyDecayPerSec);
            changed |= WriteIfMissing(path, nameof(PlayerConfigSO.starvingStaminaRegenMultiplier),
                () => c.starvingStaminaRegenMultiplier = StarvingStaminaRegenMultiplier);
            changed |= WriteIfMissing(path, nameof(PlayerConfigSO.radiationSicknessDose), () => c.radiationSicknessDose = RadiationSicknessDose);
            changed |= WriteIfMissing(path, nameof(PlayerConfigSO.radiationSicknessMaxHpMultiplier),
                () => c.radiationSicknessMaxHpMultiplier = RadiationSicknessMaxHpMultiplier);
            changed |= WriteIfMissing(path, nameof(PlayerConfigSO.radiationWarnDose), () => c.radiationWarnDose = RadiationWarnDose);
            changed |= WriteIfMissing(path, nameof(PlayerConfigSO.survivalLowRatio), () => c.survivalLowRatio = SurvivalLowRatio);

            if (changed)
            {
                EditorUtility.SetDirty(c);
                Debug.Log($"{Tag} PlayerConfig 생존 필드 기록: {path}");
            }
        }

        // ---------- ④ 방사능 구역 (§3.10, TODO(GDD-?) Q39) ----------

        private static void WriteRadiationZones()
        {
            var raid = SetupUtil.Load<RaidConfigSO>(M7Names.RaidConfigPath);
            if (raid == null)
                return;
            if (raid.radiationZones == null)
                raid.radiationZones = new List<RadiationZoneConfig>();
            // 같은 id가 있으면 조정했을 수 있으니 보존한다.
            if (raid.radiationZones.Any(z => z.id == M7Names.RefineryZoneId))
                return;
            raid.radiationZones.Add(new RadiationZoneConfig
            {
                id = M7Names.RefineryZoneId,
                centerXZ = RefineryCenterXZ,
                radiusM = RefineryRadiusM,
                ratePerSec = RefineryRatePerSec,
                edgeFalloffM = RefineryEdgeFalloffM,
            });
            EditorUtility.SetDirty(raid);
            Debug.Log($"{Tag} RaidConfig.radiationZones += {M7Names.RefineryZoneId}");
        }

        // ---------- ⑤ 제작 레시피 (§3.8, TODO(GDD-?) Q47) ----------

        private static void CreateCraftingConfig()
        {
            bool created = false;
            var config = SetupUtil.CreateAssetIfMissing<CraftingConfigSO>(M7Names.CraftingConfigPath, c =>
            {
                c.recipes = BuildRecipes(new HashSet<string>()).ToArray();
                created = true;
            }, Tag);
            if (created)
                return;

            // 이미 있으면 빠진 레시피 id만 끝에 추가 (있는 레시피는 조정 보존)
            var existing = new HashSet<string>((config.recipes ?? new CraftingRecipe[0]).Where(r => r != null).Select(r => r.id));
            List<CraftingRecipe> missing = BuildRecipes(existing);
            if (missing.Count == 0)
                return;
            config.recipes = (config.recipes ?? new CraftingRecipe[0]).Concat(missing).ToArray();
            EditorUtility.SetDirty(config);
            foreach (CraftingRecipe r in missing)
                Debug.Log($"{Tag} CraftingConfig.recipes += {r.id}");
        }

        // skipIds에 없는 레시피를 표 순서로 만든다. 재료·산출 에셋이 하나라도 없으면 그 레시피는 빼고 에러 로그(Load).
        private static List<CraftingRecipe> BuildRecipes(HashSet<string> skipIds)
        {
            var result = new List<CraftingRecipe>();
            foreach (RecipePlan plan in Recipes)
            {
                if (skipIds.Contains(plan.Id))
                    continue;
                var output = SetupUtil.Load<ItemDefinitionSO>(plan.OutputPath);
                var inputs = plan.Inputs.Select(i => new CraftingIngredient { item = SetupUtil.Load<ItemDefinitionSO>(i.path), count = i.count }).ToArray();
                if (output == null || inputs.Any(i => i.item == null))
                {
                    Debug.LogError($"{Tag} 레시피 {plan.Id}의 아이템이 없어 건너뛴다.");
                    continue;
                }
                result.Add(new CraftingRecipe
                {
                    id = plan.Id, displayName = plan.DisplayName,
                    inputs = inputs, output = output, outputCount = plan.OutputCount,
                });
            }
            return result;
        }

        // ---------- ⑥ 상인 (설계 §6 7C-1b) ----------

        private static void AppendTraderCatalog()
        {
            var trader = SetupUtil.Load<TraderConfigSO>(M5DataSetup.TraderConfigPath);
            if (trader == null)
                return;
            var catalog = new List<ItemDefinitionSO>(trader.catalog ?? new ItemDefinitionSO[0]);
            bool changed = false;
            foreach (string path in TraderAdditionPaths)
            {
                var item = SetupUtil.Load<ItemDefinitionSO>(path);
                if (item == null || catalog.Contains(item))
                    continue;
                catalog.Add(item);
                changed = true;
                Debug.Log($"{Tag} TraderConfig.catalog += {item.id}");
            }
            if (!changed)
                return;
            trader.catalog = catalog.ToArray();
            EditorUtility.SetDirty(trader);
        }

        // ---------- ⑦ 루트 테이블 (§3.9, TODO(GDD-?)) ----------

        private static void AppendLoot()
        {
            foreach (var (tablePath, entries) in LootAdditions)
            {
                var table = SetupUtil.Load<LootTableSO>(tablePath);
                if (table == null)
                    continue;
                if (table.entries == null)
                    table.entries = new List<LootEntry>();
                bool changed = false;
                foreach (var (itemPath, weight) in entries)
                {
                    var item = SetupUtil.Load<ItemDefinitionSO>(itemPath);
                    if (item == null || table.entries.Any(e => e.item == item))
                        continue;   // 이미 있으면 가중치를 조정했을 수 있으니 보존
                    table.entries.Add(new LootEntry { item = item, weight = weight });
                    changed = true;
                    Debug.Log($"{Tag} {table.id} += {item.id} ({weight})");
                }
                if (changed)
                    EditorUtility.SetDirty(table);
            }
        }

        // ---------- 도우미 ----------

        // 필드가 파일에 없으면 set을 부르고 true. 있으면 아무것도 안 한다 (M6DataSetup·M7WeaponDataSetup과 같음).
        private static bool WriteIfMissing(string path, string fieldName, System.Action set)
        {
            if (SetupUtil.IsFieldSerialized(path, fieldName))
                return false;
            set();
            return true;
        }
    }
}
