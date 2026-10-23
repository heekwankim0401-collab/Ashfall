#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using Ashfall.Core;
using Ashfall.Raid;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ashfall.Tests.PlayMode
{
    // M6 AC3 재 폭풍 흐름 (§8.3, M6 설계 §1.5·§7 AC3, Q23~Q28). 실제 Raid_CrimsonBasin(+ "AshStorm", M6StormSceneApply).
    // 시간은 RaidController.AdvanceTime(레이드 경과 시간)으로 당긴다. 재 파티클은 2a가 없을 수 있어 코드로 만든 파티클을 쓴다.
    public class AshStormFlowTests
    {
        private M4Raid raid;
        private AshStormController storm;
        private AshStormPresenter presenter;
        private ParticleSystem testAsh;

        // 시작 직후 Rising 한가운데까지 (전환 20초의 절반)
        private float HalfRise => storm.Config.riseSec * 0.5f;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            raid = new M4Raid();
            yield return M4Raid.Load(raid);
            yield return raid.RemoveEnemies();   // 교전 변수 제거 (선량·사망)
            storm = Object.FindFirstObjectByType<AshStormController>();
            Assert.IsNotNull(storm, "AshStormController 없음 — Ashfall/Setup/M6/Apply Storm 실행 필요");
            presenter = storm.Presenter;
            Assert.IsNotNull(presenter, "AshStormPresenter 연결 없음");
            Assert.AreSame(raid.Controller, storm.Raid, "raid 참조");
            Assert.IsNotNull(storm.Model, "세션이 생기면 시작 시각이 정해져야 한다");
            Assert.AreEqual(AshStormPhase.Pending, storm.Phase);

            if (presenter.AshParticles == null)
            {
                var go = new GameObject("TestAshParticles");
                testAsh = go.AddComponent<ParticleSystem>();
                ParticleSystem.MainModule main = testAsh.main;
                main.maxParticles = 500;
                ParticleSystem.EmissionModule emission = testAsh.emission;
                emission.rateOverTimeMultiplier = 50f;
                presenter.SetAshParticles(testAsh);
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (raid?.Gm != null && raid.Gm.Flow.IsWorldPaused)
                raid.Gm.Flow.SetWorldPaused(false);
        }

        [UnityTest]
        public IEnumerator ServiceRegistered_StartInsideWindow()
        {
            Assert.IsTrue(raid.Gm.Services.TryGet(out IAshStormInfo info), "IAshStormInfo 미등록");
            Assert.AreSame(storm, info);
            Assert.That(storm.StartSec, Is.InRange(storm.Config.startWindowSec.x, storm.Config.startWindowSec.y), "Q23 시작 창");
            Assert.IsTrue(float.IsPositiveInfinity(info.VisibilityM), "시작 전 = 폭풍 없음");
            Assert.AreEqual(0f, info.RadiationPerSec);
            Assert.IsNull(presenter.SkyCopy, "폭풍 전 하늘 복제본 없음");
            yield return null;
        }

        // M7 7D 리뷰 낮음 7: Core 이벤트 AshStormStartRequested(개발 빌드 스모크의 "폭풍 강제 시작" 경로)가 Pending을 벗어나게 한다.
        // 에디터 Play는 Debug.isDebugBuild = true라 처리된다(릴리스는 무시 — 빌드 확인 7D-1 메모). 두 번째 요청은 이미 시작이라 단계가 그대로.
        [UnityTest]
        public IEnumerator StartRequestedEvent_LeavesPending_SecondRequestIgnored()
        {
            Assert.AreEqual(AshStormPhase.Pending, storm.Phase);
            Assert.IsTrue(Debug.isDebugBuild, "에디터 Play = 디버그 빌드");
            using var phases = new EventLog<AshStormChanged>(raid.Events);
            raid.Events.Publish(new AshStormStartRequested());
            yield return M3Scene.WaitUntil(() => storm.Phase != AshStormPhase.Pending, 2f, () => "이벤트 → Pending 벗어남");
            Assert.AreEqual(AshStormPhase.Rising, storm.Phase, "시작 = Rising");
            Assert.LessOrEqual(storm.StartSec, raid.Controller.ElapsedSec + 1e-3f, "시작 시각을 지금으로 당김");
            Assert.IsTrue(phases.Received.Any(e => e.Phase == AshStormPhase.Rising), "AshStormChanged(Rising) 발행");

            raid.Events.Publish(new AshStormStartRequested());
            yield return null;
            Assert.AreEqual(AshStormPhase.Rising, storm.Phase, "두 번째 요청은 무시(이미 시작)");
            Assert.IsFalse(storm.ForceStartNow(), "이미 시작");
        }

        [UnityTest]
        public IEnumerator ForceStart_PhasesInOrder_FogSkyAshSun_ThenRestored()
        {
            Material skyOriginal = RenderSettings.skybox;
            bool fog0 = RenderSettings.fog;
            FogMode mode0 = RenderSettings.fogMode;
            float density0 = RenderSettings.fogDensity;
            Color color0 = RenderSettings.fogColor;
            ParticleSystem ash = presenter.AshParticles;
            float rate0 = ash.emission.rateOverTimeMultiplier;
            int max0 = ash.main.maxParticles;
            float lifetime0 = ash.main.startLifetimeMultiplier;
            Light sun = presenter.Sun;
            float sun0 = sun != null ? sun.intensity : 0f;
            float threshold = AtmosphereThreshold();

            using var phases = new EventLog<AshStormChanged>(raid.Events);
            Assert.IsTrue(storm.ForceStartNow());
            yield return M3Scene.WaitUntil(() => storm.Phase == AshStormPhase.Rising, 2f, () => "Rising");
            Assert.IsFalse(storm.ForceStartNow(), "이미 시작");

            // Rising 중간: 안개가 짙어지고 하늘은 복제본
            raid.Controller.AdvanceTime(HalfRise);
            yield return null;
            yield return null;
            Assert.AreEqual(AshStormPhase.Rising, storm.Phase);
            Assert.That(storm.Intensity, Is.InRange(0.45f, 0.6f));
            Assert.AreEqual(Mathf.Max(BaseDensity(fog0, mode0, density0), FogMath.DensityForVisibility(storm.VisibilityM, threshold)),
                RenderSettings.fogDensity, 1e-5f, "Rising 안개 밀도");
            Assert.Greater(RenderSettings.fogDensity, BaseDensity(fog0, mode0, density0), "맑을 때(꺼져 있었으면 0)보다 짙다");
            Assert.IsTrue(RenderSettings.fog);
            Assert.AreEqual(FogMode.ExponentialSquared, RenderSettings.fogMode);
            if (skyOriginal != null)
            {
                Assert.IsNotNull(presenter.SkyCopy, "하늘 복제본");
                Assert.AreSame(presenter.SkyCopy, RenderSettings.skybox, "공유 에셋 대신 복제본을 쓴다");
                Assert.AreNotSame(skyOriginal, RenderSettings.skybox);
            }

            // Peak: 시야 30m, 방사능 1, 안개 = 30m, 파티클 배율 최대
            raid.Controller.AdvanceTime(storm.Config.riseSec);
            yield return null;
            yield return null;
            Assert.AreEqual(AshStormPhase.Peak, storm.Phase);
            Assert.IsTrue(raid.Gm.Services.TryGet(out IAshStormInfo info));
            Assert.AreEqual(1f, info.Intensity);
            Assert.AreEqual(storm.Config.stormVisibilityM, info.VisibilityM, 1e-4f);
            Assert.AreEqual(30f, info.VisibilityM, 1e-4f, "§8.3 시야 30m");
            Assert.AreEqual(1f, info.RadiationPerSec, 1e-6f, "§8.3 +1/s");
            Assert.AreEqual(FogMath.DensityForVisibility(30f, threshold), RenderSettings.fogDensity, 1e-5f, "Peak 안개 = 30m 가시거리");
            Assert.AreEqual(threshold, FogMath.Transmittance(30f, RenderSettings.fogDensity), 1e-3f);
            // 방출 배율 = 설정을 폭풍 상한이 담는 만큼으로 자른 값(리뷰 낮음 2: 실제 레이드 씬 600/s × 10s, 상한 12000 → ×2)
            int stormMax = Mathf.Max(max0, Atmosphere().ashMaxParticlesStorm);
            float expectedMultiplier = AshStormVisualMath.AshEmissionMultiplier(storm.Config.ashEmissionMultiplier, rate0, lifetime0, stormMax);
            Assert.AreEqual(rate0 * expectedMultiplier, ash.emission.rateOverTimeMultiplier, rate0 * 1e-4f, "방출 배율");
            Assert.LessOrEqual(rate0 * expectedMultiplier * lifetime0, stormMax + 1f, "정상 상태 입자 수(방출 × 수명) ≤ 폭풍 상한");
            Assert.AreEqual(stormMax, ash.main.maxParticles, "Peak 상한");

            // 리뷰 중간 1: Peak 하늘 = 폭풍 안개 색 (지평선 포함 하늘 전체) — 30m 밖 윤곽이 하늘에 비치지 않는다
            if (skyOriginal != null && skyOriginal.HasProperty("_MainTex") && skyOriginal.HasProperty("_Tint"))
            {
                Material copy = presenter.SkyCopy;
                Assert.IsNotNull(copy);
                Assert.IsTrue(presenter.SkyIsFogColor, "Peak = 안개 색 단색 하늘");
                Assert.AreSame(Texture2D.whiteTexture, copy.GetTexture("_MainTex"), "복제본 텍스처 = 흰색");
                Assert.IsNotNull(skyOriginal.GetTexture("_MainTex"), "공유 에셋 텍스처는 그대로");
                Assert.AreNotSame(Texture2D.whiteTexture, skyOriginal.GetTexture("_MainTex"));
                bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
                Color sky = AshStormVisualMath.PanoramicOutput(copy.GetColor("_Tint"), copy.GetFloat("_Exposure"), Color.white, linear);
                Color fog = linear ? RenderSettings.fogColor.linear : RenderSettings.fogColor;
                Debug.Log($"[AshStormFlowTests] Peak 하늘 {sky} / 안개 {fog} (linear {linear})");
                Assert.AreEqual(fog.r, sky.r, 2e-3f, "하늘 R ≈ 안개");
                Assert.AreEqual(fog.g, sky.g, 2e-3f, "하늘 G ≈ 안개");
                Assert.AreEqual(fog.b, sky.b, 2e-3f, "하늘 B ≈ 안개");
                PaletteSO palette = storm.Config.palette != null ? storm.Config.palette : Atmosphere().palette;
                Color stormFog = palette.Get(storm.Config.stormFogToken);
                Assert.AreEqual(stormFog.r, RenderSettings.fogColor.r, 1e-4f, "Peak 안개 = 폭풍 안개 토큰");
                Assert.AreEqual(stormFog.g, RenderSettings.fogColor.g, 1e-4f);
                Assert.AreEqual(stormFog.b, RenderSettings.fogColor.b, 1e-4f);
            }
            Assert.GreaterOrEqual(ash.main.maxParticles, max0, "폭풍 상한");
            if (sun != null)
                Assert.AreEqual(sun0 * storm.Config.sunIntensityFactorAtPeak, sun.intensity, 1e-4f, "태양 어두워짐");

            // Falling → Over
            raid.Controller.AdvanceTime(storm.Config.peakSec);
            yield return null;
            yield return null;
            Assert.AreEqual(AshStormPhase.Falling, storm.Phase);
            raid.Controller.AdvanceTime(storm.Config.fallSec);
            yield return null;
            yield return null;
            Assert.AreEqual(AshStormPhase.Over, storm.Phase);

            CollectionAssert.AreEqual(
                new[] { AshStormPhase.Rising, AshStormPhase.Peak, AshStormPhase.Falling, AshStormPhase.Over },
                phases.Received.Select(e => e.Phase).ToArray(), "AshStormChanged 순서·각 1회");

            // 복원
            Assert.AreEqual(fog0, RenderSettings.fog);
            Assert.AreEqual(mode0, RenderSettings.fogMode);
            Assert.AreEqual(density0, RenderSettings.fogDensity, 1e-7f);
            Assert.AreEqual(color0, RenderSettings.fogColor);
            Assert.AreSame(skyOriginal, RenderSettings.skybox, "하늘 원본");
            Assert.IsNull(presenter.SkyCopy);
            Assert.AreEqual(rate0, ash.emission.rateOverTimeMultiplier, 1e-5f);
            Assert.AreEqual(max0, ash.main.maxParticles);
            if (sun != null)
                Assert.AreEqual(sun0, sun.intensity, 1e-5f);
            Assert.IsTrue(float.IsPositiveInfinity(info.VisibilityM));
            Assert.AreEqual(0f, info.RadiationPerSec);

            // 다시 오지 않는다
            raid.Controller.AdvanceTime(300f);
            yield return null;
            Assert.AreEqual(AshStormPhase.Over, storm.Phase);
            Assert.AreEqual(4, phases.Count);
        }

        // 한 번에 크게 건너뛰어도(AdvanceTime) 사이 단계를 순서대로 한 번씩 알린다
        [UnityTest]
        public IEnumerator BigJump_PublishesSkippedPhasesInOrder()
        {
            using var phases = new EventLog<AshStormChanged>(raid.Events);
            storm.ForceStartNow();
            yield return null;
            raid.Controller.AdvanceTime(storm.Config.riseSec + storm.Config.peakSec + storm.Config.fallSec + 1f);
            yield return null;
            yield return null;
            Assert.AreEqual(AshStormPhase.Over, storm.Phase);
            CollectionAssert.AreEqual(
                new[] { AshStormPhase.Rising, AshStormPhase.Peak, AshStormPhase.Falling, AshStormPhase.Over },
                phases.Received.Select(e => e.Phase).ToArray());
            Assert.IsNull(presenter.SkyCopy);
        }

        // Peak 10초 ≈ 선량 10 (±0.5), 일시정지 중 단계·선량·경과 시간 정지
        [UnityTest]
        public IEnumerator PlayerDose_PeakTenSeconds_AndPauseFreezes()
        {
            Assert.AreEqual(0f, raid.Health.RadiationDose, "레이드 시작 선량 0");
            storm.ForceStartNow();
            yield return null;
            raid.Controller.AdvanceTime(storm.Config.riseSec + 0.5f);
            yield return M3Scene.WaitUntil(() => storm.Phase == AshStormPhase.Peak, 2f, () => "Peak");
            float dose0 = raid.Health.RadiationDose;
            float elapsed0 = raid.Controller.ElapsedSec;
            yield return M3Scene.WaitUntil(() => raid.Controller.ElapsedSec - elapsed0 >= 10f, 20f, () => "Peak 10초");
            float gained = raid.Health.RadiationDose - dose0;
            float elapsed = raid.Controller.ElapsedSec - elapsed0;
            Debug.Log($"[AshStormFlowTests] Peak {elapsed:0.000}초 → 선량 +{gained:0.000}");
            Assert.AreEqual(10f, gained, 0.5f, "§8.3 +1/s × 10초");
            Assert.AreEqual(elapsed, gained, 0.1f, "선량 = 레이드 경과 시간 × 1");
            Assert.IsFalse(raid.Health.IsDead);

            // 일시정지
            raid.Gm.Flow.SetWorldPaused(true);
            yield return null;
            float doseP = raid.Health.RadiationDose;
            float elapsedP = raid.Controller.ElapsedSec;
            AshStormPhase phaseP = storm.Phase;
            yield return new WaitForSecondsRealtime(1f);
            Assert.AreEqual(doseP, raid.Health.RadiationDose, 1e-6f, "일시정지 중 선량 정지");
            Assert.AreEqual(elapsedP, raid.Controller.ElapsedSec, 1e-6f, "일시정지 중 폭풍 시간 정지");
            Assert.AreEqual(phaseP, storm.Phase);
            raid.Gm.Flow.SetWorldPaused(false);
            yield return M2Scene.WaitSeconds(0.5f);
            Assert.Greater(raid.Health.RadiationDose, doseP, "해제 후 다시 쌓임");
        }

        // 폭풍 중 씬을 내려도 공유 하늘 머티리얼은 그대로, 복제본은 파괴, 서비스는 해제
        [UnityTest]
        public IEnumerator UnloadDuringPeak_SharedSkyUntouched_CopyDestroyed_ServiceGone()
        {
            Material skyOriginal = RenderSettings.skybox;
            Assert.IsNotNull(skyOriginal, "레이드 씬 하늘 머티리얼");
            Color[] colors0 = ColorProps(skyOriginal);
            float[] floats0 = FloatProps(skyOriginal);
            Texture texture0 = skyOriginal.HasProperty("_MainTex") ? skyOriginal.GetTexture("_MainTex") : null;

            storm.ForceStartNow();
            yield return null;
            raid.Controller.AdvanceTime(storm.Config.riseSec + 1f);
            yield return M3Scene.WaitUntil(() => storm.Phase == AshStormPhase.Peak, 2f, () => "Peak");
            Material copy = presenter.SkyCopy;
            Assert.IsNotNull(copy);
            CollectionAssert.AreEqual(colors0, ColorProps(skyOriginal), "폭풍 중에도 공유 에셋 색 불변");
            CollectionAssert.AreEqual(floats0, FloatProps(skyOriginal), "폭풍 중에도 공유 에셋 값 불변");

            yield return M4Raid.LoadScene(SceneNames.MainMenu);
            Assert.IsTrue(skyOriginal != null, "공유 하늘 에셋이 파괴되지 않음");
            Assert.IsTrue(copy == null, "복제본 파괴");
            CollectionAssert.AreEqual(colors0, ColorProps(skyOriginal), "언로드 후 공유 에셋 색 불변");
            CollectionAssert.AreEqual(floats0, FloatProps(skyOriginal), "언로드 후 공유 에셋 값 불변");
            if (skyOriginal.HasProperty("_MainTex"))
                Assert.AreSame(texture0, skyOriginal.GetTexture("_MainTex"), "공유 에셋 텍스처 불변(Peak 단색 하늘은 복제본에만)");
            Assert.IsFalse(RenderSettings.skybox != null && RenderSettings.skybox.name.EndsWith("(AshStorm)"), "다음 씬 하늘이 복제본이 아님");
            Assert.IsFalse(GameManager.Instance.Services.TryGet(out IAshStormInfo _), "레이드 밖 IAshStormInfo 없음");
        }

        private float AtmosphereThreshold() => Atmosphere().fogVisibilityThreshold;

        private AtmosphereConfigSO Atmosphere()
        {
            var field = typeof(AshStormPresenter).GetField("atmosphere",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var atmosphere = (AtmosphereConfigSO)field.GetValue(presenter);
            Assert.IsNotNull(atmosphere, "presenter.atmosphere 연결");
            return atmosphere;
        }

        private static float BaseDensity(bool fog, FogMode mode, float density) =>
            fog && mode == FogMode.ExponentialSquared ? density : 0f;

        private static readonly string[] SkyColorProps = { "_Tint", "_SkyTint", "_GroundColor" };
        private static readonly string[] SkyFloatProps = { "_Exposure", "_Rotation", "_SunSize", "_AtmosphereThickness" };

        private static Color[] ColorProps(Material m) =>
            SkyColorProps.Where(m.HasProperty).Select(m.GetColor).ToArray();

        private static float[] FloatProps(Material m) =>
            SkyFloatProps.Where(m.HasProperty).Select(m.GetFloat).ToArray();
    }
}
#endif
