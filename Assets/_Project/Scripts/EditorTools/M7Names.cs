namespace Ashfall.EditorTools
{
    // M7 에셋 경로·씬 오브젝트 이름 (계약, M7 설계 §4.10·§2.2). 데이터·에셋 메뉴·테스트가 이 이름으로 서로를 찾는다.
    // 7A-1이 만든다. 7B·7C는 각자 계약 단계에서 아래 구역에 추가만 한다(기존 값은 바꾸지 않는다 — 바꾸려면 오케스트레이터를 거친다).
    public static class M7Names
    {
        // ── 폴더 ──────────────────────────────────────────────
        public const string DataFolder = M2DataSetup.DataFolder;                 // Assets/_Project/Data
        public const string WeaponsDataFolder = M3DataSetup.WeaponsFolder;       // Assets/_Project/Data/Weapons
        public const string PrefabFolder = "Assets/_Project/Prefabs";
        public const string WeaponPrefabFolder = PrefabFolder + "/Weapons";      // 자리표시 뷰모델 (Q4)
        public const string WeaponAudioFolder = M6Names.PlaceholderAudioFolder + "/Weapon";   // 헛격발·고장 해제 (7A-2c)

        // ── 7A 무기 데이터 (7A-1b 데이터 메뉴, 설계 §3.1·§3.2) ──
        public const string ShotgunId = "WPN-SHT-01";
        public const string DmrId = "WPN-DMR-01";
        public const string LmgId = "WPN-LMG-01";                // 적 전용 (enemyOnly, 7B가 쓴다)
        public const string Ammo12GaId = "AMMO-12GA";
        public const string Ammo762Id = "AMMO-762";
        public const string Mag762_5Id = "MAG-762-5";
        public const string Mag762_100Id = "MAG-762-100";        // 적 전용 (LMG)
        public const string ShotgunTubeId = "MAG-12GA-TUBE";     // 내장 튜브 (그리드에 놓이지 않음)
        public const string Box12Ga8Id = "BOX-12GA-8";           // 탄 상자 isAmmoBox

        public const string ShotgunPath = WeaponsDataFolder + "/" + ShotgunId + ".asset";
        public const string DmrPath = WeaponsDataFolder + "/" + DmrId + ".asset";
        public const string LmgPath = WeaponsDataFolder + "/" + LmgId + ".asset";
        public const string Ammo12GaPath = WeaponsDataFolder + "/" + Ammo12GaId + ".asset";
        public const string Ammo762Path = WeaponsDataFolder + "/" + Ammo762Id + ".asset";
        public const string Mag762_5Path = WeaponsDataFolder + "/" + Mag762_5Id + ".asset";
        public const string Mag762_100Path = WeaponsDataFolder + "/" + Mag762_100Id + ".asset";
        public const string ShotgunTubePath = WeaponsDataFolder + "/" + ShotgunTubeId + ".asset";
        public const string Box12Ga8Path = WeaponsDataFolder + "/" + Box12Ga8Id + ".asset";
        public const string WeaponHandlingConfigPath = M2DataSetup.WeaponHandlingConfigPath;   // 기존 에셋에 M7 필드(§3.3)

        // ── 7A 자리표시 뷰모델 프리팹 (7A-2a M7WeaponAssets, Q4) ──
        public const string MuzzleChild = "Muzzle";              // 프리팹 자식 이름 = 총구. Weapons WeaponController.MuzzleChildName과 같이 바꿀 것
        public static string ViewModelPrefabPath(string weaponId) => $"{WeaponPrefabFolder}/VM_{weaponId}.prefab";

        // ── 7A 자리표시 소리 (7A-2c, WeaponClicked) ────────────
        public const string DryFireClipPath = WeaponAudioFolder + "/WPN_DryFire.wav";
        public const string JamClearClipPath = WeaponAudioFolder + "/WPN_JamClear.wav";

        // ── 7B AI (7B-1 계약이 추가) ───────────────────────────
        public const string AiDataFolder = M3DataSetup.AiFolder;                 // Assets/_Project/Data/AI
        public const string LootDataFolder = M3DataSetup.LootFolder;             // Assets/_Project/Data/Loot
        public const string RelicFolder = DataFolder + "/Items/Relics";

        // 적 유형 (7B-1b M7AiDataSetup, 설계 §3.4)
        public const string SniperArchetypeId = "AI-SNP";
        public const string MutantArchetypeId = "AI-MUT";
        public const string BossArchetypeId = "AI-BOS";
        public const string BodyguardArchetypeId = "AI-BGD";
        public const string SniperArchetypePath = AiDataFolder + "/" + SniperArchetypeId + ".asset";
        public const string MutantArchetypePath = AiDataFolder + "/" + MutantArchetypeId + ".asset";
        public const string BossArchetypePath = AiDataFolder + "/" + BossArchetypeId + ".asset";
        public const string BodyguardArchetypePath = AiDataFolder + "/" + BodyguardArchetypeId + ".asset";

        // 루트 테이블·유물 (설계 §3.7·§3.9, Q31). 호위는 LT-RDR(기존)을 쓴다.
        public const string LootSniperId = "LT-SNP";
        public const string LootMutantId = "LT-MUT";
        public const string LootBossId = "LT-BOS";
        public const string LootSniperPath = LootDataFolder + "/" + LootSniperId + ".asset";
        public const string LootMutantPath = LootDataFolder + "/" + LootMutantId + ".asset";
        public const string LootBossPath = LootDataFolder + "/" + LootBossId + ".asset";
        public const string RelicId = "ITM-REL-01";                               // 재의 군주 인장 (유물)
        public const string RelicPath = RelicFolder + "/" + RelicId + ".asset";

        // 씬 오브젝트 (7B-2a 섬광, 7B-2c 배치). 런타임(AI EnemyAgent)은 이름이 아니라 자식의 SniperGlint 컴포넌트로 찾는다.
        public const string SniperGlintObject = "SniperGlint";                   // 저격수 자식 Quad (SniperGlint 컴포넌트)
        public const string M7EnemyPrefix = "M7_";                               // M7EnemySceneApply가 추가하는 적·분대 이름 접두어
        public const string SniperGlintMaterialPath = M6Names.MaterialFolder + "/M_SniperGlint.mat";   // Unlit 발광 Bone × HDR

        // 자리표시 소리 (7B-2c, EnemyVocalized). LMG 총성 세트는 M6AudioSetup 파일 이름 규칙("Lmg")을 따른다(7A).
        public const string EnemyAudioFolder = M6Names.PlaceholderAudioFolder + "/Enemy";
        public const string MutantAlertClipPath = EnemyAudioFolder + "/MUT_Alert.wav";
        public const string MutantAttackClipPath = EnemyAudioFolder + "/MUT_Attack.wav";

        // ── 7C 생존·제작 (7C-1 계약이 추가) ────────────────────
        // 새 아이템 (7C-1b M7SurvivalDataSetup, 설계 §3.7). 물·음식·해독제 = MedicalDefinitionSO(Q42) → 의약품 폴더, 방독면 = ArmorDefinitionSO(Helmet, Q37) → 방어구 폴더.
        public const string MedicalDataFolder = M2DataSetup.MedicalFolder;      // Assets/_Project/Data/Items/Medical
        public const string ArmorDataFolder = M2DataSetup.ArmorFolder;          // Assets/_Project/Data/Items/Armor
        public const string WaterId = "ITM-WTR-01";              // 물병 (수분 +50)
        public const string FoodId = "ITM-FOD-01";               // 통조림 (에너지 +60)
        public const string AntiRadId = "MED-ANT-01";            // 해독제 (curesRadiation, Q36)
        public const string GasMaskId = "GEAR-GSM-01";           // 방독면 (Helmet, blocksStormRadiation, Q37·Q38)
        public const string WaterPath = MedicalDataFolder + "/" + WaterId + ".asset";
        public const string FoodPath = MedicalDataFolder + "/" + FoodId + ".asset";
        public const string AntiRadPath = MedicalDataFolder + "/" + AntiRadId + ".asset";
        public const string GasMaskPath = ArmorDataFolder + "/" + GasMaskId + ".asset";
        // 고철 재료 (기존 M2 잡동사니, Q48)
        public const string PipePath = M5DataSetup.PipePath;                     // JNK-01 녹슨 파이프
        public const string EnginePartPath = M5DataSetup.EnginePartPath;         // JNK-02 엔진 부품
        public const string WiresPath = M5DataSetup.WiresPath;                   // JNK-03 전선 뭉치

        // 제작 레시피 (7C-1b, 설계 §3.8, Q47). 에셋 하나에 전부.
        public const string CraftingConfigPath = M5DataSetup.HideoutFolder + "/CraftingConfig.asset";
        public const string RecipeAmmo9mm12Id = "RCP-AMMO-9MM12";
        public const string RecipeAmmo556Id = "RCP-AMMO-556";
        public const string RecipeAmmo762Id = "RCP-AMMO-762";
        public const string RecipeAmmo12GaId = "RCP-AMMO-12GA";
        public const string RecipeBandageId = "RCP-MED-BND";
        public const string RecipeTourniquetId = "RCP-MED-TRQ";
        public const string RecipeSplintId = "RCP-MED-SPL";
        public const string RecipeAntiRadId = "RCP-MED-ANT";

        // 방사능 구역 (7C-1b가 RaidConfig.radiationZones에 기록, 설계 §3.10, Q39). 중심 = POI-REF.
        public const string RefineryZoneId = "RAD-REF";
        public const string RaidConfigPath = M4DataSetup.RaidConfigPath;         // Assets/_Project/Data/Raid/RaidConfig.asset

        // 씬 오브젝트 (7C-2c M7HazardSceneApply, Q40). 런타임(Raid)은 이름이 아니라 컴포넌트(RadiationField)로 찾는다.
        public const string RadiationFieldObject = "RadiationField";             // 레이드 씬 최상위, IRadiationField 등록
        public const string ToxicMarkersObject = "M7_ToxicMarkers";              // 레이드 씬 최상위, Toxic 자리표시 묶음 (콜라이더 없음)
        public const string ToxicMarkerPrefix = "M7_Toxic_";                     // 묶음 아래 경고 기둥·드럼통 이름 접두어
        public const string ToxicMaterialPath = M6Names.PaletteMaterialFolder + "/M_Toxic.mat";   // PaletteToken.Toxic (PaletteMaterialMap Toxic 키, 7C-2c)
    }
}
