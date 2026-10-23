using System.Collections.Generic;
using Ashfall.AI;
using Ashfall.Core;
using UnityEditor;
using UnityEngine;

namespace Ashfall.EditorTools
{
    // M7 7B AI 데이터 (M7 설계 §3.4·§3.7·§3.9·§3.11, §4.10, Q22~Q31).
    // ① ITM-REL-01 재의 군주 인장(유물, Data/Items/Relics/) — 없을 때만
    // ② 루트 테이블 LT-SNP·LT-MUT·LT-BOS — 없을 때만. LT-BOS는 유물을 guaranteedFirstRoll로 정확히 1개 보장(Q31 "첫 굴림 고정").
    //    §3.9의 해독제·물병·방독면은 7C 아이템이라 아직 없다 — 7C가 이 표들에 "없으면 추가"로 붙인다.
    // ③ 적 유형 AI-SNP·AI-MUT·AI-BOS·AI-BGD — 없을 때만. 무장 = §7(SNP 볼트액션 WPN-DMR-01, MUT 없음, BOS 경기관총 WPN-LMG-01 + MAG-762-100,
    //    BGD 소총). LMG·탄띠는 7A-1b가 만든 것을 참조만 한다. 호위(BGD)는 AI-RDR 값을 복사한 뒤 §3.4 차이만 덮는다.
    // ④ 기존 AI-SCV·AI-RDR weaponDurabilityRange 35~85 — 파일에 필드가 없거나 (100,100)(코드 초기값 = 미설정)일 때만. tactics는 쓰지 않는다(Auto 유지).
    // ⑤ AiConfig.glintAngularSizeDeg·vocalCooldownSec(0.35·4) + 7B 리뷰 수정 근접·둘레 필드 7개 + 7D-4 approachStopDistanceM(2.0) — 하나라도 파일에 없을 때만 초기값 기록.
    // ⑥ ItemDatabase 재수집(M5DataSetup.CreateItemDatabase).
    // GDD 값은 §7 행동·무장뿐이다. 나머지 수치는 전부 TODO(GDD-?) 임시값(설계 §3.4 표). 표에 "—"인 칸은 0(안 씀)이고,
    // 표에 행이 없는 M3 필드(오차 배율 등)는 AI-RDR 값을 쓴다(설계 "RDR 값"의 확장, 7B-1b 구현 메모).
    // 멱등: 값이 바뀔 때만 SetDirty한다. 두 번째 실행은 파일·GUID를 바꾸지 않는다.
    // 배치모드: -executeMethod Ashfall.EditorTools.M7AiDataSetup.CreateData
    public static class M7AiDataSetup
    {
        private const string Tag = "[M7AiDataSetup]";

        // §3.4 기존 AI-SCV·RDR 스폰 무기 내구도 (%, TODO(GDD-?) Q12·Q18)
        public static readonly Vector2 ExistingDurabilityRange = new Vector2(35f, 85f);
        // EnemyArchetypeSO.weaponDurabilityRange 코드 초기값 — 이 값이면 미설정으로 본다
        public static readonly Vector2 UnsetDurabilityRange = new Vector2(100f, 100f);

        private static readonly (string path, string id)[] ExistingArchetypes =
        {
            (M3DataSetup.ScvPath, "AI-SCV"),
            (M3DataSetup.RdrPath, "AI-RDR"),
        };

        [MenuItem("Ashfall/Setup/Create M7 AI Data")]
        public static void CreateData()
        {
            // M1~M7A 데이터(무기 DMR·LMG·탄띠·7.62 탄창, AI-RDR, LT-RDR, ItemDatabase 등)가 먼저 있어야 한다. 이미 있는 에셋은 건너뛴다.
            M7WeaponDataSetup.CreateData();

            WriteM7AiData();
            Debug.Log($"{Tag} 완료");
        }

        // 7B 몫만 (이전 메뉴를 부르지 않는다). 통합 메뉴·테스트가 재실행 바이트 동일을 확인할 때 쓴다.
        public static void WriteM7AiData()
        {
            CreateRelic();
            CreateLootTables();
            AssetDatabase.SaveAssets();
            CreateArchetypes();
            WriteExistingDurability();
            WriteAiConfigFields();
            AssetDatabase.SaveAssets();
            // 새 정의(유물)를 저장한 뒤 DB를 다시 모은다(바뀔 때만 기록).
            M5DataSetup.CreateItemDatabase();
            AssetDatabase.SaveAssets();
        }

        // ---------- ① 유물 (§3.7, §3.11 Relic) ----------

        private static void CreateRelic()
        {
            SetupUtil.CreateAssetIfMissing<ItemDefinitionSO>(M7Names.RelicPath, i =>
            {
                i.id = M7Names.RelicId; i.displayName = "재의 군주 인장";
                i.rarity = Rarity.Relic;   // §6 유물 (설계 §3.11, TODO(GDD-?) Q48)
                // TODO(GDD-?): 아래 전부 임시값 (설계 §3.7, 판매용)
                i.size = new Vector2Int(1, 1); i.weightKg = 0.2f; i.basePrice = 8000;
            }, Tag);
        }

        // ---------- ② 루트 테이블 (§3.9, 전부 TODO(GDD-?)) ----------

        private static void CreateLootTables()
        {
            // LT-SNP 1~2회: MAG-762-5 3 (+ 7C: 해독제 1, 물병 1)
            CreateLootTable(M7Names.LootSniperPath, M7Names.LootSniperId, new Vector2Int(1, 2), null,
                (M7Names.Mag762_5Path, 3f));
            // LT-MUT 0~2회: JNK-01 2, JNK-03 2 (+ 7C: 해독제 0.3)
            CreateLootTable(M7Names.LootMutantPath, M7Names.LootMutantId, new Vector2Int(0, 2), null,
                (M5DataSetup.PipePath, 2f), (M5DataSetup.WiresPath, 2f));
            // LT-BOS 3~4회: 첫 굴림 = ITM-REL-01 고정(Q31), 나머지 MAG-762-5 2 (+ 7C: 방독면 1, 해독제 1)
            CreateLootTable(M7Names.LootBossPath, M7Names.LootBossId, new Vector2Int(3, 4), M7Names.RelicPath,
                (M7Names.Mag762_5Path, 2f));
        }

        private static void CreateLootTable(string path, string id, Vector2Int rollCount, string guaranteedPath,
            params (string itemPath, float weight)[] entries)
        {
            SetupUtil.CreateAssetIfMissing<LootTableSO>(path, t =>
            {
                t.id = id;
                t.rollCount = rollCount;
                t.guaranteedFirstRoll = guaranteedPath != null ? SetupUtil.Load<ItemDefinitionSO>(guaranteedPath) : null;
                t.entries = new List<LootEntry>();
                foreach ((string itemPath, float weight) in entries)
                {
                    var item = SetupUtil.Load<ItemDefinitionSO>(itemPath);
                    if (item != null)
                        t.entries.Add(new LootEntry { item = item, weight = weight });
                }
            }, Tag);
        }

        // ---------- ③ 적 유형 (§3.4) ----------

        private static void CreateArchetypes()
        {
            var dmr = SetupUtil.Load<WeaponDefinitionSO>(M7Names.DmrPath);
            var lmg = SetupUtil.Load<WeaponDefinitionSO>(M7Names.LmgPath);
            var ammo762 = SetupUtil.Load<AmmoDefinitionSO>(M7Names.Ammo762Path);
            var mag762x5 = SetupUtil.Load<MagazineDefinitionSO>(M7Names.Mag762_5Path);
            var mag762x100 = SetupUtil.Load<MagazineDefinitionSO>(M7Names.Mag762_100Path);
            var rifle = SetupUtil.Load<WeaponDefinitionSO>(M3DataSetup.RiflePath);
            var ammo556 = SetupUtil.Load<AmmoDefinitionSO>(M3DataSetup.Ammo556Path);
            var mag556 = SetupUtil.Load<MagazineDefinitionSO>(M3DataSetup.Mag556Path);
            var rdr = SetupUtil.Load<EnemyArchetypeSO>(M3DataSetup.RdrPath);
            if (rdr == null)
            {
                Debug.LogError($"{Tag} AI-RDR이 없다 ({M3DataSetup.RdrPath}). M3 데이터를 먼저 만들 것.");
                return;
            }

            // AI-SNP 저격수 — §7 고지대 고정·레이저 반사 신호, 무장 볼트액션
            SetupUtil.CreateAssetIfMissing<EnemyArchetypeSO>(M7Names.SniperArchetypePath, a =>
            {
                a.id = M7Names.SniperArchetypeId; a.displayName = "저격수";
                a.corpseDisplayName = "저격수의 시체";
                a.tactics = EnemyTacticsKind.Sniper;
                a.loot = SetupUtil.Load<LootTableSO>(M7Names.LootSniperPath);
                a.loadouts = new List<EnemyLoadout> { new EnemyLoadout { weapon = dmr, ammo = ammo762, magazine = mag762x5, weight = 1f } };
                // TODO(GDD-?): 아래 전부 임시값 (설계 §3.4, Q22·Q23)
                a.headHp = 35f; a.hp = 80f; a.legsHp = 65f;
                a.sightRange = 150f; a.sightAngle = 70f; a.hearingMultiplier = 1f;
                a.spareMagazines = 3;
                a.patrolSpeed = 0f; a.moveSpeed = 0f; a.sprintSpeed = 0f;   // 이동 없음 (퍼치 고정)
                a.reactionTimeSec = 1.0f;
                a.aimErrorBaseM = 0.05f; a.aimErrorPerMeterM = 0.004f; a.aimSettleSec = 2.0f;
                CopyAimMultipliers(rdr, a);                                   // 표에 없음 → RDR 값
                a.burstErrorGrowthM = 0f;                                     // 반자동 (점사 없음)
                a.burstRounds = Vector2Int.zero; a.burstPauseSec = Vector2.zero;   // 자동 무기 아님 (—)
                a.semiShotIntervalSec = new Vector2(2.0f, 3.5f);
                a.preferredRangeM = Vector2.zero;                             // 이동 없음 (—)
                a.engageMaxRangeM = 200f;
                a.usesCover = false; a.flanks = false; a.investigatesNoise = false;
                a.peekSec = Vector2.zero; a.hideSec = Vector2.zero; a.tacticalReloadBelow = 0f; a.strafeIntervalSec = Vector2.zero;
                a.anchorRadiusM = 0f;
                a.weaponDurabilityRange = new Vector2(50f, 90f);
                a.telegraphSec = 1.5f; a.scanArcDeg = 60f;
            }, Tag);

            // AI-MUT 변이체 — §7 근접 돌진·무리 행동, 무장 없음
            SetupUtil.CreateAssetIfMissing<EnemyArchetypeSO>(M7Names.MutantArchetypePath, a =>
            {
                a.id = M7Names.MutantArchetypeId; a.displayName = "변이체";
                a.corpseDisplayName = "변이체 사체";
                a.tactics = EnemyTacticsKind.Mutant;
                a.loot = SetupUtil.Load<LootTableSO>(M7Names.LootMutantPath);
                a.loadouts = new List<EnemyLoadout>();                        // §7 무장 없음
                a.spareMagazines = 0;
                // TODO(GDD-?): 아래 전부 임시값 (설계 §3.4, Q24·Q25). 사격 관련(오차·연사·교전 거리·반응)은 "—" → 0
                a.headHp = 30f; a.hp = 60f; a.legsHp = 50f;
                a.sightRange = 40f; a.sightAngle = 140f; a.hearingMultiplier = 1.5f;
                a.patrolSpeed = 1.2f; a.moveSpeed = 4.0f; a.sprintSpeed = 5.0f;
                a.reactionTimeSec = 0f;
                a.aimErrorBaseM = 0f; a.aimErrorPerMeterM = 0f; a.aimSettleSec = 0f;
                a.unsettledErrorMultiplier = 0f; a.selfMovingErrorMultiplier = 0f; a.targetMovingErrorMultiplier = 0f;
                a.burstErrorGrowthM = 0f;
                a.burstRounds = Vector2Int.zero; a.burstPauseSec = Vector2.zero; a.semiShotIntervalSec = Vector2.zero;
                a.preferredRangeM = Vector2.zero; a.engageMaxRangeM = 0f;
                a.usesCover = false; a.flanks = false; a.investigatesNoise = true;
                a.peekSec = Vector2.zero; a.hideSec = Vector2.zero; a.tacticalReloadBelow = 0f; a.strafeIntervalSec = Vector2.zero;
                a.anchorRadiusM = 0f;
                a.weaponDurabilityRange = UnsetDurabilityRange;               // 무기 없음 (—)
                a.meleeDamage = 14f; a.meleeRangeM = 1.8f; a.meleeIntervalSec = 1.1f; a.meleeWindupSec = 0.3f;
                a.surroundRadiusM = 2.5f;
            }, Tag);

            // AI-BOS 재의 군주 — §7 호위 3명 동반·고정 거점, 무장 경기관총(적 전용, Q27)
            SetupUtil.CreateAssetIfMissing<EnemyArchetypeSO>(M7Names.BossArchetypePath, a =>
            {
                a.id = M7Names.BossArchetypeId; a.displayName = "재의 군주";
                a.corpseDisplayName = "재의 군주의 시체";
                a.tactics = EnemyTacticsKind.Raider;
                a.loot = SetupUtil.Load<LootTableSO>(M7Names.LootBossPath);
                a.loadouts = new List<EnemyLoadout> { new EnemyLoadout { weapon = lmg, ammo = ammo762, magazine = mag762x100, weight = 1f } };
                CopyRaiderFields(rdr, a);                                     // 표에 행이 없는 필드 = RDR 값
                // TODO(GDD-?): 아래 전부 임시값 (설계 §3.4, Q27~Q29)
                a.headHp = 50f; a.hp = 160f; a.legsHp = 110f;
                a.sightRange = 70f; a.sightAngle = 110f; a.hearingMultiplier = 1f;
                a.spareMagazines = 2;
                a.patrolSpeed = 1.2f; a.moveSpeed = 2.5f; a.sprintSpeed = 3.0f;
                a.reactionTimeSec = 0.6f;
                a.aimErrorBaseM = rdr.aimErrorBaseM * 1.3f;                   // "RDR 오차 × 1.3"
                a.aimErrorPerMeterM = rdr.aimErrorPerMeterM * 1.3f;
                a.burstRounds = new Vector2Int(8, 15); a.burstPauseSec = new Vector2(0.6f, 1.2f);
                a.engageMaxRangeM = 90f;
                a.usesCover = true; a.flanks = false; a.investigatesNoise = false;
                a.anchorRadiusM = 25f;
                a.weaponDurabilityRange = UnsetDurabilityRange;               // 적 전용 무기, 시체에 안 나옴 (—)
            }, Tag);

            // AI-BGD 호위병 — Q28: RDR 값 + 거점, 측면 우회 끔. 시체는 LT-RDR(일반)
            SetupUtil.CreateAssetIfMissing<EnemyArchetypeSO>(M7Names.BodyguardArchetypePath, a =>
            {
                a.id = M7Names.BodyguardArchetypeId; a.displayName = "호위병";
                a.corpseDisplayName = "호위병의 시체";
                a.tactics = EnemyTacticsKind.Raider;
                a.loot = SetupUtil.Load<LootTableSO>(M3DataSetup.LootRdrPath);
                a.loadouts = new List<EnemyLoadout> { new EnemyLoadout { weapon = rifle, ammo = ammo556, magazine = mag556, weight = 1f } };
                CopyRaiderFields(rdr, a);
                a.headHp = rdr.headHp; a.hp = rdr.hp; a.legsHp = rdr.legsHp;  // 35/80/65 (§4.2 축약, RDR과 같음)
                a.sightRange = rdr.sightRange; a.sightAngle = rdr.sightAngle; a.hearingMultiplier = rdr.hearingMultiplier;   // 60/110/1.0
                a.spareMagazines = 3;
                a.patrolSpeed = rdr.patrolSpeed; a.moveSpeed = rdr.moveSpeed; a.sprintSpeed = rdr.sprintSpeed;
                a.reactionTimeSec = rdr.reactionTimeSec;
                a.aimErrorBaseM = rdr.aimErrorBaseM; a.aimErrorPerMeterM = rdr.aimErrorPerMeterM;
                a.burstRounds = rdr.burstRounds; a.burstPauseSec = rdr.burstPauseSec;
                a.engageMaxRangeM = rdr.engageMaxRangeM;
                // TODO(GDD-?): 아래 임시값 (설계 §3.4)
                a.usesCover = true; a.flanks = false; a.investigatesNoise = false;
                a.anchorRadiusM = 25f;
                a.weaponDurabilityRange = new Vector2(35f, 85f);
            }, Tag);
        }

        // RDR의 반응·명중 배율 (표에 행이 없는 칸)
        private static void CopyAimMultipliers(EnemyArchetypeSO from, EnemyArchetypeSO to)
        {
            to.unsettledErrorMultiplier = from.unsettledErrorMultiplier;
            to.selfMovingErrorMultiplier = from.selfMovingErrorMultiplier;
            to.targetMovingErrorMultiplier = from.targetMovingErrorMultiplier;
        }

        // BOS·BGD 공통: §3.4 표에 행이 없는 M3 필드를 RDR에서 복사한다. 표에 있는 값은 호출자가 덮는다.
        private static void CopyRaiderFields(EnemyArchetypeSO from, EnemyArchetypeSO to)
        {
            to.aimSettleSec = from.aimSettleSec;
            CopyAimMultipliers(from, to);
            to.burstErrorGrowthM = from.burstErrorGrowthM;
            to.semiShotIntervalSec = from.semiShotIntervalSec;
            to.preferredRangeM = from.preferredRangeM;
            to.peekSec = from.peekSec;
            to.hideSec = from.hideSec;
            to.tacticalReloadBelow = from.tacticalReloadBelow;
            to.strafeIntervalSec = from.strafeIntervalSec;
        }

        // ---------- ④ 기존 SCV·RDR 스폰 내구도 (§3.4, TODO(GDD-?) Q12·Q18) ----------

        private static void WriteExistingDurability()
        {
            foreach (var (path, id) in ExistingArchetypes)
            {
                var archetype = SetupUtil.Load<EnemyArchetypeSO>(path);
                if (archetype == null)
                    continue;
                if (archetype.id != id)
                {
                    Debug.LogError($"{Tag} {path}의 id가 {archetype.id}다 (기대 {id}). 내구도를 기록하지 않는다.", archetype);
                    continue;
                }
                // 파일에 필드가 있고 (100,100)이 아니면 조정했을 수 있으니 보존한다.
                // (100,100)은 코드 초기값이라 미설정으로 본다 — 다른 메뉴가 먼저 유형을 저장하면 초기값이 직렬화되기 때문 (7A 마모 0 규칙과 같음).
                // TODO(GDD-?) Q12: 기존 유형을 의도적으로 100~100으로 두려면 이 규칙이 덮어쓴다 — 99.9~100처럼 쓸 것.
                if (!ShouldWriteExistingDurability(
                        SetupUtil.IsFieldSerialized(path, nameof(EnemyArchetypeSO.weaponDurabilityRange)),
                        archetype.weaponDurabilityRange))
                    continue;
                archetype.weaponDurabilityRange = ExistingDurabilityRange;
                EditorUtility.SetDirty(archetype);
                Debug.Log($"{Tag} {id}.weaponDurabilityRange = {ExistingDurabilityRange}");
            }
        }

        // 기존 유형 내구도를 기록할지: 파일에 필드가 없거나 값이 초기값 (100,100)이면 true.
        public static bool ShouldWriteExistingDurability(bool fieldSerialized, Vector2 current) =>
            !fieldSerialized || current == UnsetDurabilityRange;

        // ---------- ⑤ AiConfig 새 필드 (§3.4 끝, 코드 초기값 = 설계값) ----------

        // 7B 리뷰 수정(1·3·5·15)에서 AiConfigSO에 더한 필드. 값 = 코드 초기값(설계·임시값), 파일에 없을 때만 기록.
        public static readonly string[] AiConfigM7Fields =
        {
            nameof(AiConfigSO.glintAngularSizeDeg), nameof(AiConfigSO.vocalCooldownSec),
            nameof(AiConfigSO.meleePartWeightThorax), nameof(AiConfigSO.meleePartWeightStomach),
            nameof(AiConfigSO.meleePartWeightLeftArm), nameof(AiConfigSO.meleePartWeightRightArm),
            nameof(AiConfigSO.meleeMaxHeightDiffM), nameof(AiConfigSO.mutantCloseRadiusRatio),
            nameof(AiConfigSO.mutantHoldInsideRangeRatio),
            nameof(AiConfigSO.approachStopDistanceM),   // 7D-4 (7D-3 발견 1) 접근 정지 거리
        };

        private static void WriteAiConfigFields()
        {
            string path = M3DataSetup.AiConfigPath;
            var config = SetupUtil.Load<AiConfigSO>(path);
            if (config == null)
                return;
            // 값은 코드 초기값이 설계값(TODO(GDD-?) 임시값 포함)이라 넣지 않는다. 하나라도 파일에 없을 때만 SetDirty로 기록(조정 보존).
            bool allSerialized = true;
            foreach (string field in AiConfigM7Fields)
                allSerialized &= SetupUtil.IsFieldSerialized(path, field);
            if (allSerialized)
                return;
            EditorUtility.SetDirty(config);
            Debug.Log($"{Tag} AiConfig에 M7 필드 기록: glintAngularSizeDeg {config.glintAngularSizeDeg} / vocalCooldownSec {config.vocalCooldownSec} / " +
                      $"근접 부위 가중치 {config.meleePartWeightThorax}·{config.meleePartWeightStomach}·{config.meleePartWeightLeftArm}·{config.meleePartWeightRightArm} / " +
                      $"높이 차 {config.meleeMaxHeightDiffM} / 둘레 비율 {config.mutantCloseRadiusRatio}·{config.mutantHoldInsideRangeRatio} / " +
                      $"접근 정지 {config.approachStopDistanceM}m");
        }
    }
}
