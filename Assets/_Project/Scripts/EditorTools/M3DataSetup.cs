using System.Collections.Generic;
using Ashfall.AI;
using Ashfall.Core;
using Ashfall.Inventory;
using Ashfall.Player;
using UnityEditor;
using UnityEngine;

namespace Ashfall.EditorTools
{
    // M3 데이터 에셋 생성 (M3 설계 §4, §5.6 "2c").
    // - §4.3 무기·탄·탄창: WPN-PST-01, WPN-SMG-01, AMMO-9MM, MAG-9MM-12, MAG-9MM-30 (§5.2 표 + 임시값)
    // - §4.4 루트 테이블: LT-SCV, LT-RDR
    // - §4.1 적 유형: AI-SCV, AI-RDR / §4.2 AiConfig (필드 초기값 = 설계값)
    // - 데이터 이전: WPN-RIF-01.damage 45(§5.2), shotNoiseRadiusM 120(TODO) — 값이 0 이하일 때만
    // - §4.5 PlayerConfig.noisePulseIntervalSec, InventoryConfig.corpseGridSize — 파일에 없을 때만 초기값을 기록
    // 멱등: 이미 있는 에셋은 덮어쓰지 않는다(조정한 값 보존). 여러 번 실행해도 결과가 같다.
    // 배치모드: -executeMethod Ashfall.EditorTools.M3DataSetup.CreateAll
    public static class M3DataSetup
    {
        private const string Tag = "[M3DataSetup]";

        public const string DataFolder = M2DataSetup.DataFolder;
        public const string WeaponsFolder = DataFolder + "/Weapons";
        public const string AiFolder = DataFolder + "/AI";
        public const string LootFolder = DataFolder + "/Loot";

        // M1 에셋 (M1Setup이 만든다)
        public const string RiflePath = WeaponsFolder + "/WPN-RIF-01.asset";
        public const string Ammo556Path = WeaponsFolder + "/AMMO-556.asset";
        public const string Mag556Path = WeaponsFolder + "/MAG-556-30.asset";

        // M3 에셋
        public const string PistolPath = WeaponsFolder + "/WPN-PST-01.asset";
        public const string SmgPath = WeaponsFolder + "/WPN-SMG-01.asset";
        public const string Ammo9mmPath = WeaponsFolder + "/AMMO-9MM.asset";
        public const string Mag9mm12Path = WeaponsFolder + "/MAG-9MM-12.asset";
        public const string Mag9mm30Path = WeaponsFolder + "/MAG-9MM-30.asset";
        public const string LootScvPath = LootFolder + "/LT-SCV.asset";
        public const string LootRdrPath = LootFolder + "/LT-RDR.asset";
        public const string ScvPath = AiFolder + "/AI-SCV.asset";
        public const string RdrPath = AiFolder + "/AI-RDR.asset";
        public const string AiConfigPath = AiFolder + "/AiConfig.asset";

        // 루트 테이블이 참조하는 M2 아이템
        private const string BandagePath = M2DataSetup.MedicalFolder + "/MED-BND-01.asset";
        private const string TourniquetPath = M2DataSetup.MedicalFolder + "/MED-TRQ-01.asset";
        private const string SplintPath = M2DataSetup.MedicalFolder + "/MED-SPL-01.asset";
        private const string PipePath = M2DataSetup.JunkFolder + "/JNK-01.asset";
        private const string WiresPath = M2DataSetup.JunkFolder + "/JNK-03.asset";

        [MenuItem("Ashfall/Setup/Create M3 Data")]
        public static void CreateData()
        {
            // M1·M2 데이터(소총·5.56·아이템·설정)가 먼저 있어야 한다. 둘 다 이미 있는 에셋은 건너뛴다.
            M2DataSetup.CreateData();

            MigrateRifle();
            WriteNewConfigFields();
            CreateWeapons9mm();
            CreateLootTables();
            CreateAiConfig();
            CreateArchetypes();
            AssetDatabase.SaveAssets();
            Debug.Log($"{Tag} 완료");
        }

        // 배치모드용 진입점. CreateData와 같다.
        public static void CreateAll() => CreateData();

        // ---------- 데이터 이전 (M3 설계 §1.2, §4.3) ----------

        // WPN-RIF-01.asset에는 M3 필드가 없다(로드하면 0). 0 이하일 때만 채운다(이미 조정한 값 보존).
        // AMMO-556.damage(45)는 터렛·기존 테스트 호환을 위해 바꾸지 않는다.
        private static void MigrateRifle()
        {
            var rifle = SetupUtil.Load<WeaponDefinitionSO>(RiflePath);
            if (rifle == null)
                return;

            bool changed = false;
            if (rifle.damage <= 0f)
            {
                rifle.damage = 45f; // §5.2 WPN-RIF-01 피해
                changed = true;
            }
            if (rifle.shotNoiseRadiusM <= 0f)
            {
                rifle.shotNoiseRadiusM = 120f; // TODO(GDD-?): 사격 소음 반경 임시값
                changed = true;
            }
            if (changed)
            {
                EditorUtility.SetDirty(rifle);
                Debug.Log($"{Tag} WPN-RIF-01에 damage {rifle.damage} / shotNoiseRadiusM {rifle.shotNoiseRadiusM} 기록");
            }
        }

        // §4.5: 새 필드는 초기값(= 설계값)이라 값 입력은 필요 없다. 파일에 아직 없을 때만 SetDirty로 기록한다
        // (로드 값과 파일 값이 같게. 이미 기록돼 있으면 조정했을 수 있으니 건드리지 않는다).
        private static void WriteNewConfigFields()
        {
            WriteIfMissing<PlayerConfigSO>(M2DataSetup.PlayerConfigPath, "noisePulseIntervalSec");
            WriteIfMissing<InventoryConfigSO>(M2DataSetup.InventoryConfigPath, "corpseGridSize");
        }

        private static void WriteIfMissing<T>(string path, string fieldName) where T : ScriptableObject
        {
            var asset = SetupUtil.Load<T>(path);
            if (asset == null || SetupUtil.IsFieldSerialized(path, fieldName))
                return;
            EditorUtility.SetDirty(asset);
            Debug.Log($"{Tag} {path}에 {fieldName} 초기값 기록");
        }

        // ---------- 9mm 무기·탄·탄창 (M3 설계 §4.3) ----------
        // §5.2 표 값: 분류·탄·RPM·피해·탄창 용량. 나머지는 TODO(GDD-?) 임시값.

        private static void CreateWeapons9mm()
        {
            SetupUtil.CreateAssetIfMissing<AmmoDefinitionSO>(Ammo9mmPath, a =>
            {
                a.id = "AMMO-9MM"; a.displayName = "9mm 탄"; a.type = AmmoType.Pistol9mm;
                a.pelletCount = 1;
                // TODO(GDD-?): 아래 전부 임시값. damage는 무기 damage가 0일 때만 쓰는 기본값(DamageRules).
                a.damage = 30f;
                a.penetration = 1; a.velocity = 360f; a.maxFlightTimeSec = 1.5f;
                a.size = Vector2Int.one; a.weightKg = 0.01f;
            }, Tag);

            SetupUtil.CreateAssetIfMissing<MagazineDefinitionSO>(Mag9mm12Path, m =>
            {
                m.id = "MAG-9MM-12"; m.displayName = "권총 탄창 (12발)"; m.ammo = AmmoType.Pistol9mm;
                m.capacity = 12; // §5.2 WPN-PST-01 탄창
                m.size = new Vector2Int(1, 1); m.weightKg = 0.15f; // TODO(GDD-?): 크기·무게 임시값
            }, Tag);

            SetupUtil.CreateAssetIfMissing<MagazineDefinitionSO>(Mag9mm30Path, m =>
            {
                m.id = "MAG-9MM-30"; m.displayName = "SMG 탄창 (30발)"; m.ammo = AmmoType.Pistol9mm;
                m.capacity = 30; // §5.2 WPN-SMG-01 탄창
                m.size = new Vector2Int(1, 2); m.weightKg = 0.35f; // TODO(GDD-?): 크기·무게 임시값
            }, Tag);

            SetupUtil.CreateAssetIfMissing<WeaponDefinitionSO>(PistolPath, w =>
            {
                w.id = "WPN-PST-01"; w.displayName = "녹슨 권총"; w.cls = WeaponClass.Pistol;
                w.ammo = AmmoType.Pistol9mm; w.rpm = 400; w.damage = 35f; // §5.2
                // TODO(GDD-?): §5에 무기별 사격 모드가 없다. 권총은 반자동으로 둔다.
                w.modes = new[] { FireMode.Semi };
                // TODO(GDD-?): 아래는 GDD에 없는 임시값.
                w.recoilV = 0.9f; w.recoilH = 0.3f; w.adsTime = 0.2f;
                w.reloadTimeSec = 2.0f; w.fastReloadTimeSec = 1.2f; w.hipSpreadDeg = 2.5f;
                w.shotNoiseRadiusM = 80f;
                w.size = new Vector2Int(2, 1); w.weightKg = 0.9f;
                w.rarity = Rarity.Common; w.basePrice = 0;
            }, Tag);

            SetupUtil.CreateAssetIfMissing<WeaponDefinitionSO>(SmgPath, w =>
            {
                w.id = "WPN-SMG-01"; w.displayName = "개조 SMG"; w.cls = WeaponClass.Smg;
                w.ammo = AmmoType.Pistol9mm; w.rpm = 800; w.damage = 30f; // §5.2
                // TODO(GDD-?): §5에 무기별 사격 모드가 없다. SMG는 자동으로 둔다.
                w.modes = new[] { FireMode.Auto };
                // TODO(GDD-?): 아래는 GDD에 없는 임시값.
                w.recoilV = 0.45f; w.recoilH = 0.35f; w.adsTime = 0.22f;
                w.reloadTimeSec = 2.3f; w.fastReloadTimeSec = 1.4f; w.hipSpreadDeg = 3.0f;
                w.shotNoiseRadiusM = 90f;
                w.size = new Vector2Int(3, 2); w.weightKg = 2.6f;
                w.rarity = Rarity.Common; w.basePrice = 0;
            }, Tag);
        }

        // ---------- 루트 테이블 (M3 설계 §4.4, 전부 TODO(GDD-?)) ----------

        private static void CreateLootTables()
        {
            CreateLootTable(LootScvPath, "LT-SCV", new Vector2Int(1, 2),
                (BandagePath, 3f), (WiresPath, 3f), (PipePath, 2f), (TourniquetPath, 1f), (Mag9mm12Path, 1f));
            CreateLootTable(LootRdrPath, "LT-RDR", new Vector2Int(1, 3),
                (BandagePath, 2f), (TourniquetPath, 2f), (SplintPath, 1f), (Mag556Path, 2f), (WiresPath, 1f));
        }

        private static void CreateLootTable(string path, string id, Vector2Int rollCount,
            params (string itemPath, float weight)[] entries)
        {
            SetupUtil.CreateAssetIfMissing<LootTableSO>(path, t =>
            {
                t.id = id;
                t.rollCount = rollCount; // TODO(GDD-?): 굴림 횟수 임시값
                t.entries = new List<LootEntry>();
                foreach ((string itemPath, float weight) in entries)
                {
                    var item = SetupUtil.Load<ItemDefinitionSO>(itemPath);
                    if (item != null)
                        t.entries.Add(new LootEntry { item = item, weight = weight }); // TODO(GDD-?): 가중치 임시값
                }
            }, Tag);
        }

        // ---------- AiConfig (M3 설계 §4.2) ----------

        // 필드 초기값이 설계값이라 값을 넣지 않는다(M2 관례).
        private static void CreateAiConfig()
        {
            SetupUtil.CreateAssetIfMissing<AiConfigSO>(AiConfigPath, _ => { }, Tag);
            // M3 리뷰 수정에서 더한 필드: 이미 있는 에셋에도 초기값을 기록한다 (없을 때만)
            WriteIfMissing<AiConfigSO>(AiConfigPath, nameof(AiConfigSO.lookAheadDistanceM));
            WriteIfMissing<AiConfigSO>(AiConfigPath, nameof(AiConfigSO.coverInvalidatedExcludeSec));
        }

        // ---------- 적 유형 (M3 설계 §4.1) ----------
        // GDD 값: hp(몸통)·headHp·legsHp = §4.2 흉부·머리·다리, sightRange 60·sightAngle 110 = §7,
        // 행동 플래그 = §7 행동, 무장 = §7 무장. 나머지는 TODO(GDD-?) 임시값.

        private static void CreateArchetypes()
        {
            var ammo9 = SetupUtil.Load<AmmoDefinitionSO>(Ammo9mmPath);
            var pistol = SetupUtil.Load<WeaponDefinitionSO>(PistolPath);
            var smg = SetupUtil.Load<WeaponDefinitionSO>(SmgPath);
            var mag12 = SetupUtil.Load<MagazineDefinitionSO>(Mag9mm12Path);
            var mag30 = SetupUtil.Load<MagazineDefinitionSO>(Mag9mm30Path);
            var rifle = SetupUtil.Load<WeaponDefinitionSO>(RiflePath);
            var ammo556 = SetupUtil.Load<AmmoDefinitionSO>(Ammo556Path);
            var mag556 = SetupUtil.Load<MagazineDefinitionSO>(Mag556Path);

            SetupUtil.CreateAssetIfMissing<EnemyArchetypeSO>(ScvPath, a =>
            {
                a.id = "AI-SCV"; a.displayName = "약탈자";
                a.corpseDisplayName = "약탈자 시체"; // TODO(GDD-?)
                SetBodyAndSenses(a);
                a.investigatesNoise = true; a.usesCover = false; a.flanks = false; // §7 소리 추적
                a.loot = SetupUtil.Load<LootTableSO>(LootScvPath);
                // §7 무장 권총/SMG. TODO(GDD-?): 가중치 2:1 임시값
                a.loadouts = new List<EnemyLoadout>
                {
                    new EnemyLoadout { weapon = pistol, ammo = ammo9, magazine = mag12, weight = 2f },
                    new EnemyLoadout { weapon = smg, ammo = ammo9, magazine = mag30, weight = 1f },
                };

                // TODO(GDD-?): 아래는 GDD에 없는 임시값.
                a.spareMagazines = 2;
                a.patrolSpeed = 1.5f; a.moveSpeed = 3.0f; a.sprintSpeed = 5.0f;
                a.reactionTimeSec = 0.6f;
                a.aimErrorBaseM = 0.3f; a.aimErrorPerMeterM = 0.04f;
                a.aimSettleSec = 1.5f; a.unsettledErrorMultiplier = 2.0f;
                a.selfMovingErrorMultiplier = 2.0f; a.targetMovingErrorMultiplier = 1.5f;
                a.burstErrorGrowthM = 0.08f;
                a.burstRounds = new Vector2Int(3, 6); a.burstPauseSec = new Vector2(0.6f, 1.2f);
                a.semiShotIntervalSec = new Vector2(0.35f, 0.6f);
                a.preferredRangeM = new Vector2(8f, 20f); a.engageMaxRangeM = 50f;
                a.peekSec = Vector2.zero; a.hideSec = Vector2.zero; // 엄폐 안 씀
                a.tacticalReloadBelow = 0f;                          // 안 함
                a.strafeIntervalSec = new Vector2(2f, 4f);
            }, Tag);

            SetupUtil.CreateAssetIfMissing<EnemyArchetypeSO>(RdrPath, a =>
            {
                a.id = "AI-RDR"; a.displayName = "레이더";
                a.corpseDisplayName = "레이더 시체"; // TODO(GDD-?)
                SetBodyAndSenses(a);
                a.usesCover = true; a.flanks = true; a.investigatesNoise = false; // §7 엄폐 활용, 측면 우회
                a.loot = SetupUtil.Load<LootTableSO>(LootRdrPath);
                // §7 무장 소총
                a.loadouts = new List<EnemyLoadout>
                {
                    new EnemyLoadout { weapon = rifle, ammo = ammo556, magazine = mag556, weight = 1f },
                };

                // TODO(GDD-?): 아래는 GDD에 없는 임시값.
                a.spareMagazines = 3;
                a.patrolSpeed = 1.5f; a.moveSpeed = 3.2f; a.sprintSpeed = 5.5f;
                a.reactionTimeSec = 0.4f;
                a.aimErrorBaseM = 0.2f; a.aimErrorPerMeterM = 0.025f;
                a.aimSettleSec = 1.0f; a.unsettledErrorMultiplier = 2.0f;
                a.selfMovingErrorMultiplier = 2.0f; a.targetMovingErrorMultiplier = 1.3f;
                a.burstErrorGrowthM = 0.05f;
                a.burstRounds = new Vector2Int(3, 5); a.burstPauseSec = new Vector2(0.5f, 1.0f);
                a.semiShotIntervalSec = new Vector2(0.35f, 0.6f);
                a.preferredRangeM = new Vector2(15f, 40f); a.engageMaxRangeM = 60f;
                a.peekSec = new Vector2(1.5f, 3f); a.hideSec = new Vector2(1f, 2.5f);
                a.tacticalReloadBelow = 0.3f;
                a.strafeIntervalSec = Vector2.zero; // 옆 이동 안 함
            }, Tag);
        }

        // 두 유형 공통: 3부위 체력과 감각.
        private static void SetBodyAndSenses(EnemyArchetypeSO a)
        {
            // §4.2 축약: 몸통 = 흉부 80, 머리 35, 다리 65 (두 다리를 하나로). 적용은 TODO(GDD-?) 해석.
            a.hp = 80f; a.headHp = 35f; a.legsHp = 65f;
            a.sightRange = 60f; a.sightAngle = 110f; // §7 시야 60m, 콘 110°(전체 각)
            a.hearingMultiplier = 1f;                // §4.1 반경 그대로 (해석)
        }
    }
}
