namespace Ashfall.EditorTools
{
    // M6 에셋 경로·씬 오브젝트 이름 (계약, M6 설계 §4.7·§2.2). 2단계 메뉴·테스트가 이 이름으로 서로를 찾는다.
    // 바꾸려면 오케스트레이터를 거친다(이미 만들어진 에셋·씬과 어긋난다).
    public static class M6Names
    {
        // ── 폴더 ──────────────────────────────────────────────
        public const string DataFolder = M2DataSetup.DataFolder;                 // Assets/_Project/Data
        public const string ArtDataFolder = DataFolder + "/Art";
        public const string RaidDataFolder = DataFolder + "/Raid";
        public const string AudioDataFolder = DataFolder + "/Audio";

        public const string ArtFolder = "Assets/_Project/Art";
        public const string GeneratedArtFolder = ArtFolder + "/Generated";
        public const string MaterialFolder = ArtFolder + "/Materials";           // 기존 M2~M4 머티리얼 + M6 하늘·재
        public const string PaletteMaterialFolder = MaterialFolder + "/Palette";
        public const string PostProcessFolder = ArtFolder + "/PostProcess";
        public const string TerrainArtFolder = ArtFolder + "/Terrain";

        public const string AudioFolder = "Assets/_Project/Audio";
        public const string PlaceholderAudioFolder = AudioFolder + "/Placeholder";
        public const string FootstepAudioFolder = PlaceholderAudioFolder + "/Footsteps";
        public const string GunshotAudioFolder = PlaceholderAudioFolder + "/Gunshots";
        public const string AmbienceAudioFolder = PlaceholderAudioFolder + "/Ambience";
        public const string MusicAudioFolder = PlaceholderAudioFolder + "/Music";

        // ── 데이터 에셋 (1b 데이터 메뉴 / 2b 오디오 메뉴) ──────
        public const string PalettePath = ArtDataFolder + "/Palette.asset";
        public const string AtmosphereConfigPath = RaidDataFolder + "/AtmosphereConfig.asset";
        public const string AshStormConfigPath = RaidDataFolder + "/AshStormConfig.asset";
        public const string AudioConfigPath = AudioDataFolder + "/AudioConfig.asset";

        // ── 비주얼 파생 에셋 (2a 아트 메뉴, Q30) ───────────────
        public const string SkyTexturePath = GeneratedArtFolder + "/Sky_CrimsonDusk.png";
        public const string AshDotTexturePath = GeneratedArtFolder + "/AshDot.png";
        public const string GroundNoiseTexturePath = GeneratedArtFolder + "/GroundNoise.png";
        public const string SkyMaterialPath = MaterialFolder + "/M6_Sky.mat";
        public const string AshParticleMaterialPath = MaterialFolder + "/M6_AshParticle.mat";
        public const string TerrainLayerPath = TerrainArtFolder + "/TL_AshSoil.terrainlayer";
        public const string RaidVolumeProfilePath = PostProcessFolder + "/VP_RaidAtmosphere.asset";

        // ── 씬 오브젝트 이름 ──────────────────────────────────
        public const string SunObject = "Sun";                     // Directional Light (레이드)
        public const string GlobalVolumeObject = "M6_GlobalVolume";
        public const string AshParticlesObject = "AshParticles";   // Player 루트의 자식
        public const string PuddlesObject = "M6_Puddles";          // 물웅덩이 3개의 부모
        public const string AshStormObject = "AshStorm";           // AshStormController + AshStormPresenter
        public const string AudioDirectorObject = "AudioDirector"; // 레이드
        public const string HideoutMusicObject = "HideoutMusic";   // 은신처
    }
}
