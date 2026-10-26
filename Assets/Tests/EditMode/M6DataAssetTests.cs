using System.IO;
using System.Linq;
using Ashfall.AI;
using Ashfall.Audio;
using Ashfall.Core;
using Ashfall.Player;
using Ashfall.Raid;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // M6 데이터 에셋 값 = GDD §10.1(팔레트 HEX)·§10.2·§8.3 + M6 설계 §3 임시값(TODO(GDD-?)).
    // 에셋은 Ashfall/Setup/Create M6 Data(M6DataSetup)가 만든다. 없으면 실패한다.
    public class M6DataAssetTests
    {
        private const string MenuPath = "Ashfall/Setup/Create M6 Data";
        private const string Data = "Assets/_Project/Data";
        private const string PalettePath = Data + "/Art/Palette.asset";
        private const string AtmospherePath = Data + "/Raid/AtmosphereConfig.asset";
        private const string AshStormPath = Data + "/Raid/AshStormConfig.asset";
        private const string AudioConfigPath = Data + "/Audio/AudioConfig.asset";
        private const string PlayerConfigPath = Data + "/Player/PlayerConfig.asset";
        private const string AiConfigPath = Data + "/AI/AiConfig.asset";
        private const float Eps = 1e-5f;

        // 멱등 검사 대상: M6 데이터 메뉴가 만들거나 기록하는 파일 전부
        private static readonly string[] M6AssetPaths =
        {
            PalettePath, AtmospherePath, AshStormPath, AudioConfigPath, PlayerConfigPath, AiConfigPath,
        };

        private static readonly PaletteToken[] AllTokens =
        {
            PaletteToken.AshBlack, PaletteToken.DriedBlood, PaletteToken.RustRed, PaletteToken.Ember,
            PaletteToken.AshGrey, PaletteToken.Bone, PaletteToken.Toxic,
        };

        private static T Load<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.IsNotNull(asset, $"에셋이 없다: {path}. 메뉴 {MenuPath}를 실행할 것.");
            return asset;
        }

        // ---------- 경로 계약 (M6Names) ----------

        [Test]
        public void Paths_MatchM6Names()
        {
            Assert.AreEqual(PalettePath, Ashfall.EditorTools.M6Names.PalettePath);
            Assert.AreEqual(AtmospherePath, Ashfall.EditorTools.M6Names.AtmosphereConfigPath);
            Assert.AreEqual(AshStormPath, Ashfall.EditorTools.M6Names.AshStormConfigPath);
            Assert.AreEqual(AudioConfigPath, Ashfall.EditorTools.M6Names.AudioConfigPath);
        }

        // ---------- 팔레트 (§10.1, GDD 값) ----------

        [Test]
        public void Palette_MatchesGddHex()
        {
            var p = Load<PaletteSO>(PalettePath);
            // §10.1 표 HEX를 테스트에 직접 적는다(GddHex 오타도 잡게)
            var expected = new (PaletteToken token, string hex)[]
            {
                (PaletteToken.AshBlack, "0D0807"),
                (PaletteToken.DriedBlood, "3A0B0B"),
                (PaletteToken.RustRed, "6E1A14"),
                (PaletteToken.Ember, "B2321C"),
                (PaletteToken.AshGrey, "5A524E"),
                (PaletteToken.Bone, "C9BBA8"),
                (PaletteToken.Toxic, "7A8A2A"),
            };
            foreach (var (token, hex) in expected)
            {
                Color c = p.Get(token);
                Assert.AreEqual(hex, ColorUtility.ToHtmlStringRGB(c), PaletteSO.GddName(token));
                Assert.AreEqual(1f, c.a, Eps, $"{PaletteSO.GddName(token)} 불투명");
                Assert.AreEqual("#" + hex, PaletteSO.GddHex(token));
            }
            Assert.AreEqual(AllTokens.Length, expected.Length);
        }

        // ---------- AtmosphereConfig (설계 §3.2) ----------

        [Test]
        public void Atmosphere_MatchesDesign()
        {
            var a = Load<AtmosphereConfigSO>(AtmospherePath);
            Assert.AreSame(Load<PaletteSO>(PalettePath), a.palette, "팔레트 연결");

            // 안개 (§10.2 Dried_Blood / 나머지 TODO)
            Assert.AreEqual(PaletteToken.DriedBlood, a.fogToken);
            // 맵 개선 밝기 개정 1 (MapPolish 설계 §2): 가시거리 450 → 600, 태양 0.9 → 2.4·고도 8 → 12·Bone 0.4, 환경광 1 → 3.5·지면 Dried_Blood, 노출 +0.8
            Assert.AreEqual(1, a.lightingRevision, "밝기 개정 1 적용");
            Assert.AreEqual(600f, a.clearVisibilityM, Eps);
            Assert.AreEqual(0.05f, a.fogVisibilityThreshold, Eps);

            // 하늘 (§10.2 Ash_Black → Rust_Red)
            Assert.AreEqual(PaletteToken.AshBlack, a.zenithToken);
            Assert.AreEqual(PaletteToken.RustRed, a.horizonToken);
            Assert.AreEqual(PaletteToken.DriedBlood, a.belowHorizonToken);
            Assert.AreEqual(10f, a.horizonBandDeg, Eps);
            Assert.AreEqual(new Vector2Int(1024, 512), a.skyTextureSize);
            Assert.IsNull(a.skyTextureOverride);

            // 태양
            Assert.AreEqual(245f, a.sunAzimuthDeg, Eps);
            Assert.AreEqual(12f, a.sunElevationDeg, Eps);
            Assert.AreEqual(2.5f, a.sunDiskDeg, Eps);
            Assert.AreEqual(18f, a.sunGlowDeg, Eps);
            Assert.AreEqual(0.6f, a.sunHaze, Eps);
            Assert.AreEqual(PaletteToken.Bone, a.sunCoreToken);
            Assert.AreEqual(PaletteToken.Ember, a.sunGlowToken);
            Assert.AreEqual(PaletteToken.Ember, a.sunLightToken);
            Assert.AreEqual(2.4f, a.sunLightIntensity, Eps);
            Assert.AreEqual(0.4f, a.sunLightBoneMix, Eps);

            // 환경광
            Assert.AreEqual(PaletteToken.RustRed, a.ambientSkyToken);
            Assert.AreEqual(PaletteToken.DriedBlood, a.ambientEquatorToken);
            Assert.AreEqual(PaletteToken.DriedBlood, a.ambientGroundToken);
            Assert.AreEqual(3.5f, a.ambientIntensity, Eps);

            // 포스트 (§10.2 채도 −30·그레인 0.3·비네트 0.35)
            Assert.AreEqual(-30f, a.saturation, Eps);
            Assert.AreEqual(0.3f, a.filmGrainIntensity, Eps);
            Assert.AreEqual(0.35f, a.vignetteIntensity, Eps);
            Assert.AreEqual(PaletteToken.Ember, a.tintToken);
            Assert.AreEqual(0.15f, a.tintStrength, Eps);
            Assert.AreEqual(10f, a.contrast, Eps);
            Assert.AreEqual(0.8f, a.postExposureEv, Eps);
            Assert.AreEqual(FilmGrainChoice.Medium1, a.filmGrainType);
            Assert.AreEqual(0.4f, a.vignetteSmoothness, Eps);
            Assert.AreEqual(PaletteToken.AshBlack, a.vignetteToken);
            Assert.AreEqual(0.15f, a.bloomIntensity, Eps);
            Assert.AreEqual(1.1f, a.bloomThreshold, Eps);
            Assert.AreEqual(TonemapChoice.Neutral, a.tonemapping);

            // 재 파티클 (§10.2 반경 30m)
            Assert.AreEqual(30f, a.ashRadiusM, Eps);
            Assert.AreEqual(20f, a.ashHeightM, Eps);
            Assert.AreEqual(600f, a.ashRatePerSec, Eps);
            Assert.AreEqual(10f, a.ashLifetimeSec, Eps);
            Assert.AreEqual(6000, a.ashMaxParticles);
            Assert.AreEqual(12000, a.ashMaxParticlesStorm);
            Assert.AreEqual(0.03f, a.ashSizeM.x, Eps);
            Assert.AreEqual(0.08f, a.ashSizeM.y, Eps);
            Assert.AreEqual(0.3f, a.ashFallSpeedMps.x, Eps);
            Assert.AreEqual(0.8f, a.ashFallSpeedMps.y, Eps);
            Assert.AreEqual(0.3f, a.ashNoiseStrength, Eps);
            Assert.AreEqual(PaletteToken.AshGrey, a.ashToken);
            Assert.AreEqual(0.7f, a.ashAlpha, Eps);
            Assert.IsNull(a.ashTextureOverride);

            // 리뷰 낮음 4: 메뉴 상수에서 옮긴 보기 수치 (값 = 옮기기 전 상수, 파일에 기록됨)
            Assert.AreEqual(0.6f, a.skyGradientExponent, Eps);
            Assert.AreEqual(0.8f, a.sunGlowStrength, Eps);
            Assert.AreEqual(0.4f, a.ashNoiseFrequency, Eps);
            Assert.AreEqual(0.3f, a.ashBoxCenterRatio, Eps);
            Assert.AreEqual(0.01f, a.ashMaxScreenSize, Eps);
            Assert.AreEqual(0.7f, a.groundNoiseMin, Eps);
            Assert.AreEqual(12f, a.groundTileM, Eps);
            AssertSerialized(AtmospherePath, "skyGradientExponent", "sunGlowStrength", "ashNoiseFrequency", "ashBoxCenterRatio",
                "ashMaxScreenSize", "groundNoiseMin", "groundTileM");
        }

        // ---------- AshStormConfig (설계 §3.3) ----------

        [Test]
        public void AshStorm_MatchesDesign()
        {
            var s = Load<AshStormConfigSO>(AshStormPath);
            Assert.AreSame(Load<PaletteSO>(PalettePath), s.palette, "팔레트 연결");

            Assert.AreEqual(180f, s.startWindowSec.x, Eps);
            Assert.AreEqual(1200f, s.startWindowSec.y, Eps);
            Assert.AreEqual(20f, s.riseSec, Eps);
            Assert.AreEqual(180f, s.peakSec, Eps, "§8.3 3분 = Peak (Q24)");
            Assert.AreEqual(20f, s.fallSec, Eps);
            Assert.AreEqual(30f, s.stormVisibilityM, Eps, "§8.3 시야 30m");
            Assert.AreEqual(1f, s.radiationPerSec, Eps, "§8.3 방사능 +1/s");
            Assert.AreEqual(PaletteToken.AshGrey, s.stormFogToken);
            Assert.AreEqual(PaletteToken.AshGrey, s.skyTintToken);
            Assert.AreEqual(0.35f, s.skyExposureAtPeak, Eps);
            Assert.AreEqual(0.5f, s.skyFogSwapIntensity, Eps, "리뷰 중간 1: 이 강도부터 하늘 = 안개 색");
            AssertSerialized(AshStormPath, "skyFogSwapIntensity");
            Assert.AreEqual(0.25f, s.sunIntensityFactorAtPeak, Eps);
            Assert.AreEqual(4f, s.ashEmissionMultiplier, Eps);
            Assert.AreEqual(3f, s.ashSpeedMultiplier, Eps);
            Assert.AreEqual(1.5f, s.ashSizeMultiplier, Eps);
        }

        // ---------- AudioConfig (설계 §3.5 수치, 클립은 2b가 채운다) ----------

        [Test]
        public void AudioConfig_NumbersMatchDesign()
        {
            var a = Load<AudioConfigSO>(AudioConfigPath);

            Assert.AreEqual(1.5f, a.surfaceProbeLengthM, Eps);
            Assert.AreEqual(0.5f, a.surfaceProbeLiftM, Eps, "리뷰 낮음 4: SurfaceProbe.LiftM 상수에서 옮김");
            Assert.AreEqual(0.5f, a.serviceRetrySec, Eps, "리뷰 낮음 4: AudioDirector.ServiceRetrySec 상수에서 옮김");
            Assert.AreEqual(5f, a.listenerSearchRetrySec, Eps, "리뷰 낮음 8");
            AssertSerialized(AudioConfigPath, "surfaceProbeLiftM", "serviceRetrySec", "listenerSearchRetrySec");
            Assert.AreEqual((1 << 0) | (1 << 2), a.surfaceMask.value, "Default + Ignore Raycast");
            Assert.AreEqual(SurfaceKind.Dirt, a.terrainSurface);
            Assert.AreEqual(SurfaceKind.Debris, a.untaggedSurface);

            CollectionAssert.AreEqual(
                new[] { SurfaceKind.Dirt, SurfaceKind.Metal, SurfaceKind.Debris, SurfaceKind.Water },
                a.footsteps.Select(f => f.surface).ToArray());
            Assert.IsTrue(a.footsteps.All(f => Mathf.Approximately(f.volume, 1f)));
            AssertGait(a.gaitVolume, 0.7f, 1.0f, 0.35f, 0.2f);
            AssertGait(a.gaitAudibleM, 25f, 40f, 10f, 5f);
            Assert.AreEqual(0.6f, a.playerSelfVolume, Eps);
            Assert.AreEqual(0.06f, a.pitchJitter, Eps);

            CollectionAssert.AreEqual(
                new[] { WeaponClass.Pistol, WeaponClass.Smg, WeaponClass.AssaultRifle, WeaponClass.Shotgun, WeaponClass.Sniper, WeaponClass.MachineGun },   // M7 7A-2c: 끝에 Shotgun·Sniper, 7B-2c: MachineGun
                a.gunshots.Select(g => g.weaponClass).ToArray());
            Assert.IsTrue(a.gunshots.All(g => Mathf.Approximately(g.volume, 1f)));
            Assert.AreEqual(WeaponClass.AssaultRifle, a.fallbackGunshotClass);
            Assert.AreEqual(50f, a.nearMaxM, Eps);
            Assert.AreEqual(250f, a.midMaxM, Eps);
            Assert.AreEqual(1200f, a.farMaxM, Eps);
            Assert.AreEqual(1.0f, a.gainAt0, Eps);
            Assert.AreEqual(0.7f, a.gainAtNear, Eps);
            Assert.AreEqual(0.35f, a.gainAtMid, Eps);
            Assert.AreEqual(0.1f, a.gainAtFar, Eps);

            Assert.AreEqual(24, a.voiceCount);

            Assert.AreEqual(0.35f, a.windVolume, Eps);
            Assert.AreEqual(0.8f, a.windStormVolume, Eps);
            Assert.AreEqual(0.7f, a.stormLoopMaxVolume, Eps);
            Assert.AreEqual(new Vector2(40f, 120f), a.collapseIntervalSec);
            Assert.AreEqual(new Vector2(150f, 400f), a.collapseDistanceM);
            Assert.AreEqual(0.6f, a.collapseVolume, Eps);
            Assert.AreEqual(6f, a.geigerClicksPerRad, Eps);
            Assert.AreEqual(20f, a.geigerMaxClicksPerSec, Eps);
            Assert.AreEqual(0.5f, a.geigerVolume, Eps);
            Assert.AreEqual(0.4f, a.hideoutMusicVolume, Eps);
        }

        // ---------- 보폭 (설계 §3.6) ·상태 표시 (Q8) ----------

        [Test]
        public void PlayerConfig_StridesMatchDesign_AndAreSerialized()
        {
            var c = Load<PlayerConfigSO>(PlayerConfigPath);
            Assert.AreEqual(1.5f, c.strideWalkM, Eps);
            Assert.AreEqual(2.2f, c.strideSprintM, Eps);
            Assert.AreEqual(0.8f, c.strideCrouchM, Eps);
            Assert.AreEqual(0.6f, c.strideProneM, Eps);
            string file = File.ReadAllText(Path.GetFullPath(PlayerConfigPath));
            foreach (string field in new[] { "strideWalkM:", "strideSprintM:", "strideCrouchM:", "strideProneM:" })
                StringAssert.Contains(field, file, "파일에 기록됨 (메뉴가 기존 에셋에도 기록)");
        }

        [Test]
        public void AiConfig_FootstepsMatchDesign_StateIndicatorOff()
        {
            var c = Load<AiConfigSO>(AiConfigPath);
            Assert.AreEqual(1.5f, c.footstepStrideWalkM, Eps);
            Assert.AreEqual(2.2f, c.footstepStrideRunM, Eps);
            Assert.AreEqual(3.5f, c.footstepRunSpeedMps, Eps);
            Assert.AreEqual(0.3f, c.footstepMinSpeedMps, Eps);
            Assert.IsFalse(c.showStateIndicator, "Q8: M6에서 상태 색 표시를 끈다");
            string file = File.ReadAllText(Path.GetFullPath(AiConfigPath));
            foreach (string field in new[] { "footstepStrideWalkM:", "footstepStrideRunM:", "footstepRunSpeedMps:", "footstepMinSpeedMps:" })
                StringAssert.Contains(field, file, "파일에 기록됨 (메뉴가 기존 에셋에도 기록)");
            StringAssert.Contains("showStateIndicator: 0", file);
        }

        // ---------- 멱등 ----------
        // 주의(M6 리뷰 낮음 5): 실제 데이터 에셋에 메뉴를 다시 돌린다. 메뉴가 멱등이라 정상이면 아무 파일도 바뀌지 않는다.
        // 메뉴가 새 필드를 기록해야 하는 상태(코드만 바뀌고 메뉴를 안 돌림)라면 이 테스트가 에셋을 고치고 실패한다 — 변경이 작업 트리에 남는다.

        [Test]
        public void CreateM6Data_RunAgain_DoesNotChangeAssetsOrGuids()
        {
            Assume.That(M6AssetPaths.All(p => AssetDatabase.LoadMainAssetAtPath(p) != null),
                $"M6 에셋이 아직 없다. 메뉴 {MenuPath}를 먼저 실행할 것.");

            var before = M6AssetPaths.ToDictionary(p => p, p => File.ReadAllBytes(Path.GetFullPath(p)));
            var guids = M6AssetPaths.ToDictionary(p => p, p => AssetDatabase.AssetPathToGUID(p));
            int countBefore = AssetDatabase.FindAssets("t:ScriptableObject", new[] { Data }).Length;

            Assert.IsTrue(EditorApplication.ExecuteMenuItem(MenuPath), $"메뉴 없음: {MenuPath}");

            foreach (var pair in before)
            {
                CollectionAssert.AreEqual(pair.Value, File.ReadAllBytes(Path.GetFullPath(pair.Key)), $"재실행으로 바뀜: {pair.Key}");
                Assert.AreEqual(guids[pair.Key], AssetDatabase.AssetPathToGUID(pair.Key), $"GUID 바뀜: {pair.Key}");
            }
            Assert.AreEqual(countBefore, AssetDatabase.FindAssets("t:ScriptableObject", new[] { Data }).Length);
        }

        // ---------- 도우미 ----------

        // 필드가 에셋 파일에 실제로 기록됐는지 (필드 초기값으로만 읽히는 것이 아니라)
        private static void AssertSerialized(string path, params string[] fields)
        {
            string file = File.ReadAllText(Path.GetFullPath(path));
            foreach (string f in fields)
                StringAssert.Contains($"\n  {f}: ", file, $"{path}에 {f} 기록 없음 — 메뉴 {MenuPath} 실행 필요");
        }

        private static void AssertGait(GaitValues g, float walk, float sprint, float crouch, float prone)
        {
            Assert.AreEqual(walk, g.walk, Eps, "walk");
            Assert.AreEqual(sprint, g.sprint, Eps, "sprint");
            Assert.AreEqual(crouch, g.crouch, Eps, "crouch");
            Assert.AreEqual(prone, g.prone, Eps, "prone");
        }
    }
}
