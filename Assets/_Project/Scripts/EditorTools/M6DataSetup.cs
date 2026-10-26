using Ashfall.AI;
using Ashfall.Audio;
using Ashfall.Core;
using Ashfall.Player;
using Ashfall.Raid;
using UnityEditor;
using UnityEngine;

namespace Ashfall.EditorTools
{
    // M6 데이터 에셋 생성 (M6 설계 §3, §4.7 "1b", Q8·Q30).
    // ① Data/Art/Palette.asset — §10.1 HEX (없을 때만)
    // ② Data/Raid/AtmosphereConfig.asset·AshStormConfig.asset — 설계 §3.2·§3.3 (없을 때만, palette 연결. 비어 있으면 palette만 채움)
    // ③ Data/Audio/AudioConfig.asset — 설계 §3.5 수치 (없을 때만). 클립 필드는 비워 둔다 — 2b 오디오 메뉴가 빈 필드만 자리표시로 채운다(Q21).
    // ④ 기존 PlayerConfig·AiConfig에 §3.6 보폭 값 기록(파일에 없을 때만 — 조정한 값 보존),
    //    AiConfig.showStateIndicator = false (Q8, 매번 기록 — 인스펙터에서 켜도 이 메뉴를 다시 돌리면 꺼진다)
    // ⑤ 리뷰 수정에서 추가한 필드(Atmosphere 보기 수치 7개, AshStorm skyFogSwapIntensity, Audio 레이 높이·재조회 주기 3개)는
    //    이미 있는 에셋에도 파일에 없을 때만 기록한다(값 = 옮기기 전 상수, 조정 보존).
    // 멱등: 값이 바뀔 때만 SetDirty한다. 두 번째 실행은 파일·GUID를 바꾸지 않는다.
    // 수치 근거: §10.1·§10.2·§8.3에 있는 값은 GDD, 나머지는 전부 TODO(GDD-?) 임시값(설계 §3).
    // 배치모드: -executeMethod Ashfall.EditorTools.M6DataSetup.CreateData
    public static class M6DataSetup
    {
        private const string Tag = "[M6DataSetup]";

        public const string PalettePath = M6Names.PalettePath;
        public const string AtmosphereConfigPath = M6Names.AtmosphereConfigPath;
        public const string AshStormConfigPath = M6Names.AshStormConfigPath;
        public const string AudioConfigPath = M6Names.AudioConfigPath;
        public const string PlayerConfigPath = M2DataSetup.PlayerConfigPath;
        public const string AiConfigPath = M3DataSetup.AiConfigPath;

        [MenuItem("Ashfall/Setup/Create M6 Data")]
        public static void CreateData()
        {
            // M1~M5 데이터(PlayerConfig·AiConfig 등)가 먼저 있어야 한다. 이미 있는 에셋은 건너뛴다.
            M5DataSetup.CreateData();

            WriteM6Data();
            Debug.Log($"{Tag} 완료");
        }

        // M6 몫만 (M5 이전 메뉴를 부르지 않는다). 테스트가 재실행 바이트 동일을 확인할 때 쓴다.
        public static void WriteM6Data()
        {
            PaletteSO palette = CreatePalette();
            CreateAtmosphereConfig(palette);
            CreateAshStormConfig(palette);
            CreateAudioConfig();
            WritePlayerStrides();
            WriteAiConfig();
            AssetDatabase.SaveAssets();
        }

        // ---------- ① Palette (§10.1, GDD 값) ----------

        private static PaletteSO CreatePalette()
        {
            return SetupUtil.CreateAssetIfMissing<PaletteSO>(PalettePath, p =>
            {
                // 필드 초기값도 같은 HEX지만 기준(GddHex)에서 다시 넣는다(sRGB).
                p.ashBlack = HexColor(PaletteToken.AshBlack);
                p.driedBlood = HexColor(PaletteToken.DriedBlood);
                p.rustRed = HexColor(PaletteToken.RustRed);
                p.ember = HexColor(PaletteToken.Ember);
                p.ashGrey = HexColor(PaletteToken.AshGrey);
                p.bone = HexColor(PaletteToken.Bone);
                p.toxic = HexColor(PaletteToken.Toxic);
            }, Tag);
        }

        // §10.1 HEX → sRGB 색 (ColorUtility 기준 — 인스펙터와 같은 공간)
        public static Color HexColor(PaletteToken token)
        {
            string hex = PaletteSO.GddHex(token);
            if (!ColorUtility.TryParseHtmlString(hex, out Color color))
                throw new System.InvalidOperationException($"{Tag} HEX 해석 실패: {token} {hex}");
            return color;
        }

        // ---------- ② AtmosphereConfig (설계 §3.2) ----------

        private static void CreateAtmosphereConfig(PaletteSO palette)
        {
            var c = SetupUtil.CreateAssetIfMissing<AtmosphereConfigSO>(AtmosphereConfigPath, a =>
            {
                a.palette = palette;

                // 안개: 색 = §10.2 Dried_Blood. 가시거리·임계값 TODO(GDD-?)
                a.fogToken = PaletteToken.DriedBlood;
                a.clearVisibilityM = 450f;
                a.fogVisibilityThreshold = 0.05f;

                // 하늘: 천정 Ash_Black → 지평선 Rust_Red = §10.2. 나머지 TODO(GDD-?)
                a.zenithToken = PaletteToken.AshBlack;
                a.horizonToken = PaletteToken.RustRed;
                a.belowHorizonToken = PaletteToken.DriedBlood;
                a.horizonBandDeg = 10f;
                a.skyTextureSize = new Vector2Int(1024, 512);
                a.skyTextureOverride = null;

                // 태양: "낮게 걸린 흐린 태양"(§10.2). 수치 전부 TODO(GDD-?)
                a.sunAzimuthDeg = 245f;
                a.sunElevationDeg = 8f;
                a.sunDiskDeg = 2.5f;
                a.sunGlowDeg = 18f;
                a.sunHaze = 0.6f;
                a.sunCoreToken = PaletteToken.Bone;
                a.sunGlowToken = PaletteToken.Ember;
                a.sunLightToken = PaletteToken.Ember;
                a.sunLightIntensity = 0.9f;

                // 환경광 Trilight: TODO(GDD-?)
                a.ambientSkyToken = PaletteToken.RustRed;
                a.ambientEquatorToken = PaletteToken.DriedBlood;
                a.ambientGroundToken = PaletteToken.AshBlack;
                a.ambientIntensity = 1.0f;

                // 포스트: 채도 −30·그레인 0.3·비네트 0.35 = §10.2. 틴트·대비·Bloom 수치·톤 매핑 TODO(GDD-?)
                a.saturation = -30f;
                a.tintToken = PaletteToken.Ember;
                a.tintStrength = 0.15f;
                a.contrast = 10f;
                a.filmGrainIntensity = 0.3f;
                a.filmGrainType = FilmGrainChoice.Medium1; // 설계 "Medium" = Medium1 (1a 메모)
                a.vignetteIntensity = 0.35f;
                a.vignetteSmoothness = 0.4f;
                a.vignetteToken = PaletteToken.AshBlack;
                a.bloomIntensity = 0.15f;
                a.bloomThreshold = 1.1f;
                a.tonemapping = TonemapChoice.Neutral;

                // 재 파티클: 반경 30m = §10.2. 나머지 TODO(GDD-?)
                a.ashRadiusM = 30f;
                a.ashHeightM = 20f;
                a.ashRatePerSec = 600f;
                a.ashLifetimeSec = 10f;
                a.ashMaxParticles = 6000;
                a.ashMaxParticlesStorm = 12000;
                a.ashSizeM = new Vector2(0.03f, 0.08f);
                a.ashFallSpeedMps = new Vector2(0.3f, 0.8f);
                a.ashNoiseStrength = 0.3f;
                a.ashToken = PaletteToken.AshGrey;
                a.ashAlpha = 0.7f;
                a.ashTextureOverride = null;
                a.ashNoiseFrequency = 0.4f;
                a.ashBoxCenterRatio = 0.3f;
                a.ashMaxScreenSize = 0.01f;

                // 하늘 곡선·태양 번짐, 지형 레이어 (리뷰 낮음 4로 SO에 옮김): TODO(GDD-?)
                a.skyGradientExponent = 0.6f;
                a.sunGlowStrength = 0.8f;
                a.groundNoiseMin = 0.7f;
                a.groundTileM = 12f;

                // 맵 개선 밝기 개정 1 (MapPolish 설계 §2): 위 M6 값 중 밝기 7개를 덮고 개정 번호 1 — 새 에셋도 처음부터 밝다
                MapPolishSetup.WriteLightingRevision(a);
            }, Tag);

            // 이미 있는데 팔레트가 비어 있으면(에디터 Create 메뉴로 만든 경우 등) 연결만 한다. 수치는 보존.
            if (c.palette == null && palette != null)
            {
                c.palette = palette;
                EditorUtility.SetDirty(c);
                Debug.Log($"{Tag} AtmosphereConfig.palette 연결");
            }

            // M6 리뷰 낮음 4: 메뉴 상수였던 보기 수치를 SO로 옮겼다. 기존 에셋에는 파일에 없을 때만 같은 값을 기록(조정 보존). TODO(GDD-?)
            bool changed = false;
            changed |= WriteIfMissing(AtmosphereConfigPath, nameof(AtmosphereConfigSO.skyGradientExponent), () => c.skyGradientExponent = 0.6f);
            changed |= WriteIfMissing(AtmosphereConfigPath, nameof(AtmosphereConfigSO.sunGlowStrength), () => c.sunGlowStrength = 0.8f);
            changed |= WriteIfMissing(AtmosphereConfigPath, nameof(AtmosphereConfigSO.ashNoiseFrequency), () => c.ashNoiseFrequency = 0.4f);
            changed |= WriteIfMissing(AtmosphereConfigPath, nameof(AtmosphereConfigSO.ashBoxCenterRatio), () => c.ashBoxCenterRatio = 0.3f);
            changed |= WriteIfMissing(AtmosphereConfigPath, nameof(AtmosphereConfigSO.ashMaxScreenSize), () => c.ashMaxScreenSize = 0.01f);
            changed |= WriteIfMissing(AtmosphereConfigPath, nameof(AtmosphereConfigSO.groundNoiseMin), () => c.groundNoiseMin = 0.7f);
            changed |= WriteIfMissing(AtmosphereConfigPath, nameof(AtmosphereConfigSO.groundTileM), () => c.groundTileM = 12f);
            if (changed)
            {
                EditorUtility.SetDirty(c);
                Debug.Log($"{Tag} AtmosphereConfig에 보기 수치 기록(리뷰 낮음 4)");
            }
        }

        // ---------- ② AshStormConfig (설계 §3.3) ----------

        private static void CreateAshStormConfig(PaletteSO palette)
        {
            var c = SetupUtil.CreateAssetIfMissing<AshStormConfigSO>(AshStormConfigPath, s =>
            {
                // 일정: "레이드 중 1회 랜덤"(§8.3) — 시작 범위 TODO(GDD-?) Q23
                s.startWindowSec = new Vector2(180f, 1200f);
                // "3분" = Peak(Q24). 전환 20초 TODO(GDD-?)
                s.riseSec = 20f;
                s.peakSec = 180f;
                s.fallSec = 20f;

                // §8.3 시야 30m, 방사능 +1/s
                s.stormVisibilityM = 30f;
                s.radiationPerSec = 1f;

                // 표시: TODO(GDD-?)
                s.palette = palette;
                s.stormFogToken = PaletteToken.AshGrey;
                s.skyTintToken = PaletteToken.AshGrey;
                s.skyExposureAtPeak = 0.35f;
                s.skyFogSwapIntensity = 0.5f; // 리뷰 중간 1: Peak 하늘 = 안개 색
                s.sunIntensityFactorAtPeak = 0.25f;

                // 재 파티클 배율: TODO(GDD-?)
                s.ashEmissionMultiplier = 4f;
                s.ashSpeedMultiplier = 3f;
                s.ashSizeMultiplier = 1.5f;
            }, Tag);

            if (c.palette == null && palette != null)
            {
                c.palette = palette;
                EditorUtility.SetDirty(c);
                Debug.Log($"{Tag} AshStormConfig.palette 연결");
            }

            // M6 리뷰 중간 1: 새 필드. 파일에 없을 때만 기록(조정 보존). TODO(GDD-?)
            if (WriteIfMissing(AshStormConfigPath, nameof(AshStormConfigSO.skyFogSwapIntensity), () => c.skyFogSwapIntensity = 0.5f))
            {
                EditorUtility.SetDirty(c);
                Debug.Log($"{Tag} AshStormConfig.skyFogSwapIntensity 기록(리뷰 중간 1)");
            }
        }

        // ---------- ③ AudioConfig (설계 §3.5, 전부 TODO(GDD-?)) ----------
        // 클립 필드(footsteps[].clips, gunshots[].near/mid/far, windLoop, stormLoop, collapseClips, geigerClick, hideoutMusic)는
        // 비워 둔다. 2b M6AudioSetup이 WAV를 만든 뒤 빈 필드만 채운다(Q21 — 사용자 교체 보호).

        private static void CreateAudioConfig()
        {
            var c = SetupUtil.CreateAssetIfMissing<AudioConfigSO>(AudioConfigPath, a =>
            {
                // 지면 판정
                a.surfaceProbeLengthM = 1.5f;
                a.surfaceProbeLiftM = 0.5f;
                a.serviceRetrySec = 0.5f;
                a.listenerSearchRetrySec = 5f;
                a.surfaceMask = (1 << 0) | (1 << 2); // Default + Ignore Raycast
                a.terrainSurface = SurfaceKind.Dirt;
                a.untaggedSurface = SurfaceKind.Debris;

                // 발소리
                a.footsteps = new[]
                {
                    new SurfaceFootsteps(SurfaceKind.Dirt, 1f),
                    new SurfaceFootsteps(SurfaceKind.Metal, 1f),
                    new SurfaceFootsteps(SurfaceKind.Debris, 1f),
                    new SurfaceFootsteps(SurfaceKind.Water, 1f),
                };
                a.gaitVolume = new GaitValues(0.7f, 1.0f, 0.35f, 0.2f);
                a.gaitAudibleM = new GaitValues(25f, 40f, 10f, 5f);
                a.playerSelfVolume = 0.6f;
                a.pitchJitter = 0.06f;

                // 총성 거리 레이어 (Q15)
                a.gunshots = new[]
                {
                    new GunshotSet(WeaponClass.Pistol, 1f),
                    new GunshotSet(WeaponClass.Smg, 1f),
                    new GunshotSet(WeaponClass.AssaultRifle, 1f),
                };
                a.fallbackGunshotClass = WeaponClass.AssaultRifle;
                a.nearMaxM = 50f;
                a.midMaxM = 250f;
                a.farMaxM = 1200f;
                a.gainAt0 = 1.0f;
                a.gainAtNear = 0.7f;
                a.gainAtMid = 0.35f;
                a.gainAtFar = 0.1f;

                // voice 풀
                a.voiceCount = 24;

                // 환경음
                a.windVolume = 0.35f;
                a.windStormVolume = 0.8f;
                a.stormLoopMaxVolume = 0.7f;
                a.collapseClips = new AudioClip[0];
                a.collapseIntervalSec = new Vector2(40f, 120f);
                a.collapseDistanceM = new Vector2(150f, 400f);
                a.collapseVolume = 0.6f;
                a.geigerClicksPerRad = 6f;
                a.geigerMaxClicksPerSec = 20f;
                a.geigerVolume = 0.5f;

                // 은신처 BGM (Q18)
                a.hideoutMusicVolume = 0.4f;
            }, Tag);

            // M6 리뷰 낮음 4·8: 코드 상수였던 레이 시작 높이·서비스 재조회 주기를 SO로, 청취자 재탐색 주기 새 필드. 파일에 없을 때만 기록.
            bool changed = false;
            changed |= WriteIfMissing(AudioConfigPath, nameof(AudioConfigSO.surfaceProbeLiftM), () => c.surfaceProbeLiftM = 0.5f);
            changed |= WriteIfMissing(AudioConfigPath, nameof(AudioConfigSO.serviceRetrySec), () => c.serviceRetrySec = 0.5f);
            changed |= WriteIfMissing(AudioConfigPath, nameof(AudioConfigSO.listenerSearchRetrySec), () => c.listenerSearchRetrySec = 5f);
            if (changed)
            {
                EditorUtility.SetDirty(c);
                Debug.Log($"{Tag} AudioConfig에 조회·판정 수치 기록(리뷰 낮음 4·8)");
            }
        }

        // ---------- ④ 보폭 (설계 §3.6, TODO(GDD-?)) ----------

        // 파일에 아직 없을 때만 설계값을 넣고 기록한다(이미 있으면 조정했을 수 있으니 보존 — M3 WriteIfMissing 관례).
        private static void WritePlayerStrides()
        {
            var c = SetupUtil.Load<PlayerConfigSO>(PlayerConfigPath);
            if (c == null)
                return;
            bool changed = false;
            changed |= WriteIfMissing(PlayerConfigPath, nameof(PlayerConfigSO.strideWalkM), () => c.strideWalkM = 1.5f);
            changed |= WriteIfMissing(PlayerConfigPath, nameof(PlayerConfigSO.strideSprintM), () => c.strideSprintM = 2.2f);
            changed |= WriteIfMissing(PlayerConfigPath, nameof(PlayerConfigSO.strideCrouchM), () => c.strideCrouchM = 0.8f);
            changed |= WriteIfMissing(PlayerConfigPath, nameof(PlayerConfigSO.strideProneM), () => c.strideProneM = 0.6f);
            if (changed)
            {
                EditorUtility.SetDirty(c);
                Debug.Log($"{Tag} PlayerConfig에 보폭 기록: {PlayerConfigPath}");
            }
        }

        private static void WriteAiConfig()
        {
            var c = SetupUtil.Load<AiConfigSO>(AiConfigPath);
            if (c == null)
                return;
            bool changed = false;
            changed |= WriteIfMissing(AiConfigPath, nameof(AiConfigSO.footstepStrideWalkM), () => c.footstepStrideWalkM = 1.5f);
            changed |= WriteIfMissing(AiConfigPath, nameof(AiConfigSO.footstepStrideRunM), () => c.footstepStrideRunM = 2.2f);
            changed |= WriteIfMissing(AiConfigPath, nameof(AiConfigSO.footstepRunSpeedMps), () => c.footstepRunSpeedMps = 3.5f);
            changed |= WriteIfMissing(AiConfigPath, nameof(AiConfigSO.footstepMinSpeedMps), () => c.footstepMinSpeedMps = 0.3f);

            // Q8: 적 머리 위 상태 색 표시를 끈다(전역, 확인 씬 포함). 매번 기록 — 값이 다를 때만 SetDirty.
            if (c.showStateIndicator)
            {
                c.showStateIndicator = false;
                changed = true;
                Debug.Log($"{Tag} AiConfig.showStateIndicator = false (Q8)");
            }
            if (changed)
            {
                EditorUtility.SetDirty(c);
                Debug.Log($"{Tag} AiConfig 기록: {AiConfigPath}");
            }
        }

        // 필드가 파일에 없으면 set을 부르고 true. 있으면 아무것도 안 한다.
        private static bool WriteIfMissing(string path, string fieldName, System.Action set)
        {
            if (SetupUtil.IsFieldSerialized(path, fieldName))
                return false;
            set();
            return true;
        }
    }
}
