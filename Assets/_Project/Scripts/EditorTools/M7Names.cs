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

        // ── 7C 생존·제작 (7C-1 계약이 추가) ────────────────────
    }
}
