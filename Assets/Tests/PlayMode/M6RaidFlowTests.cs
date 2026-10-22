#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using Ashfall.Audio;
using Ashfall.Core;
using Ashfall.Raid;
using Ashfall.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Ashfall.Tests.PlayMode
{
    // M6 4단계 AC3 흐름 (M6 설계 §1.5 "표시 복원"·§6 4단계·§7 AC3, Q19·Q27·Q28):
    // MainMenu [은신처] → Hideout(새 프로필, 키트 권총 장착) → [레이드 시작](흐름 레이드) → 재 폭풍 강제 + AdvanceTime으로 Peak
    // → Peak 중 EXT-01 탈출 → 종료 오버레이(세계 정지 = AudioListener.pause) → Hideout:
    //   AudioListener.pause = false, 안개 = 은신처 씬 원래 값, skybox = 은신처 원래 하늘(복제본 아님), 공유 M6_Sky 머티리얼 값 불변, IAshStormInfo 없음
    // → [확인] → 다음 [레이드 시작]: 선량 0, 폭풍 Pending, 레이드 안개 = 씬 원래 값(맑을 때 Exp²), skybox = 같은 공유 M6_Sky 에셋.
    // 세이브는 SaveSandbox 새 폴더(실제 persistentDataPath 없음).
    public class M6RaidFlowTests
    {
        private FileSaveStorage storage;

        private struct FogState
        {
            public bool Fog;
            public FogMode Mode;
            public float Density;
            public Color Color;
            public Material Skybox;

            public static FogState Capture() => new FogState
            {
                Fog = RenderSettings.fog,
                Mode = RenderSettings.fogMode,
                Density = RenderSettings.fogDensity,
                Color = RenderSettings.fogColor,
                Skybox = RenderSettings.skybox,
            };

            public override string ToString() =>
                $"fog {Fog} {Mode} 밀도 {Density:0.000000} 색 {Color} 하늘 {(Skybox != null ? Skybox.name : "null")}";
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Assert.IsNotNull(GameManager.Instance, "GameManager 없음");
            M5Flow.ResetFlow();
            storage = SaveSandbox.UseFreshStorage("m6-raid-flow");
            Assert.IsFalse(SaveSandbox.IsUnderPersistentDataPath(storage.FilePath), "샌드박스 경로");
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return M5Flow.TearDown("after-m6-raid-flow");
            AudioListener.pause = false;
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator FlowRaid_StormPeak_ExtractDuringPeak_HideoutRestored_NextRaidDoseZero()
        {
            GameManager gm = GameManager.Instance;
            using (var pointer = new UiPointer())
            {
                // ── 메인 메뉴 → 은신처 (새 프로필) ──
                var menu = new M4Menu();
                yield return M4Raid.LoadScene(SceneNames.MainMenu);
                yield return M4Menu.Attach(menu);
                yield return pointer.Click(menu.Start);
                var hideout = new M5Hideout();
                yield return M5Hideout.Attach(hideout, 60f);
                Assert.IsFalse(AudioListener.pause, "은신처 진입 시 소리 정지 없음");
                FogState hideoutScene = FogState.Capture();
                yield return hideout.ClickKit(pointer);
                yield return hideout.SelectTab(pointer, HideoutTab.Loadout);
                hideout.Equip("WPN-PST-01", EquipSlot.Secondary);
                hideout.QuickMove("MAG-9MM-12");

                // ── 흐름 레이드 1 ──
                var raid = new M4Raid();
                yield return hideout.StartRaid(pointer, raid);
                Assert.IsTrue(gm.Flow.HasRaidInFlow, "흐름 레이드");
                AshStormController storm = Object.FindFirstObjectByType<AshStormController>();
                AudioDirector audio = Object.FindFirstObjectByType<AudioDirector>();
                Assert.IsNotNull(storm, "AshStormController");
                Assert.IsNotNull(audio, "AudioDirector");
                AshStormPresenter presenter = storm.Presenter;
                Assert.IsNotNull(presenter);
                FogState raidScene = FogState.Capture();
                Material sharedSky = raidScene.Skybox;
                Assert.IsNotNull(sharedSky, "레이드 하늘 머티리얼");
                StringAssert.StartsWith("M6_Sky", sharedSky.name, "레이드 하늘 = M6_Sky 공유 에셋");
                Assert.IsTrue(raidScene.Fog, "레이드 씬은 항상 안개(R1)");
                Assert.AreEqual(FogMode.ExponentialSquared, raidScene.Mode);
                Color[] skyColors0 = ColorProps(sharedSky);
                float[] skyFloats0 = FloatProps(sharedSky);
                Assert.AreEqual(0f, raid.Health.RadiationDose, "레이드 1 시작 선량 0");

                // 폭풍 → Peak (레이드 경과 시간 기준, Q28)
                Assert.IsTrue(storm.ForceStartNow());
                yield return M3Scene.WaitUntil(() => storm.Phase == AshStormPhase.Rising, 3f, () => "Rising");
                raid.Controller.AdvanceTime(storm.Config.riseSec + 0.5f);
                yield return M3Scene.WaitUntil(() => storm.Phase == AshStormPhase.Peak, 3f, () => "Peak");
                yield return M2Scene.WaitSeconds(1f);
                Assert.AreEqual(30f, storm.VisibilityM, 1e-4f, "§8.3 시야 30m");
                Assert.AreNotEqual(raidScene.Density, RenderSettings.fogDensity, "Peak 안개가 짙다");
                Assert.IsNotNull(presenter.SkyCopy, "Peak 하늘 = 복제본");
                Assert.AreSame(presenter.SkyCopy, RenderSettings.skybox);
                Material skyCopy = presenter.SkyCopy;
                Assert.IsTrue(audio.Ambient.StormLoopActive, "폭풍 루프");
                Assert.Greater(raid.Health.RadiationDose, 0.5f, "Peak 중 선량이 쌓인다");
                CollectionAssert.AreEqual(skyColors0, ColorProps(sharedSky), "Peak 중 공유 하늘 색 불변");
                CollectionAssert.AreEqual(skyFloats0, FloatProps(sharedSky), "Peak 중 공유 하늘 값 불변");

                // ── Peak 중 EXT-01 탈출 ──
                RaidResult extracted;
                bool pausedDuringOverlay = false;
                using (var ended = new EventLog<RaidEnded>(raid.Events))
                {
                    yield return raid.EnterZone("EXT-01");
                    yield return M3Scene.WaitUntil(() => ended.Count > 0, 25f, () => $"EXT-01 탈출 (폭풍 {storm.Phase})");
                    Assert.AreEqual(AshStormPhase.Peak, storm.Phase, "Peak 중에 탈출했다");
                    // 종료 오버레이: 세계 정지 → AudioDirector가 소리 정지 (Q19)
                    yield return M3Scene.WaitUntil(() => AudioListener.pause, 3f, () => "종료 오버레이 중 AudioListener.pause");
                    pausedDuringOverlay = AudioListener.pause && gm.Flow.IsWorldPaused;
                    extracted = raid.Controller.LastResult;
                }
                Assert.IsTrue(pausedDuringOverlay, "종료 오버레이 중 세계 정지 + 소리 정지");
                Assert.AreEqual(RaidOutcome.Extracted, extracted.Outcome);

                // ── 은신처: 소리·안개·하늘 원상 ──
                hideout = new M5Hideout();
                yield return M5Hideout.Attach(hideout);
                Assert.IsFalse(AudioListener.pause, "은신처 진입 시 AudioListener.pause = false");
                Assert.IsFalse(gm.Flow.IsWorldPaused);
                FogState hideoutAfter = FogState.Capture();
                Assert.AreEqual(hideoutScene.Fog, hideoutAfter.Fog, $"은신처 안개 켜짐 원래대로 ({hideoutScene} → {hideoutAfter})");
                Assert.AreEqual(hideoutScene.Mode, hideoutAfter.Mode, "은신처 안개 모드");
                Assert.AreEqual(hideoutScene.Density, hideoutAfter.Density, 1e-7f, "은신처 안개 밀도");
                Assert.AreEqual(hideoutScene.Color, hideoutAfter.Color, "은신처 안개 색");
                Assert.AreSame(hideoutScene.Skybox, hideoutAfter.Skybox, "은신처 하늘 = 원래 에셋");
                Assert.IsFalse(hideoutAfter.Skybox != null && hideoutAfter.Skybox.name.EndsWith("(AshStorm)"), "은신처 하늘이 폭풍 복제본이 아님");
                Assert.IsTrue(skyCopy == null, "폭풍 하늘 복제본 파괴");
                Assert.IsTrue(sharedSky != null, "공유 하늘 에셋은 살아 있다");
                CollectionAssert.AreEqual(skyColors0, ColorProps(sharedSky), "공유 M6_Sky 색 불변");
                CollectionAssert.AreEqual(skyFloats0, FloatProps(sharedSky), "공유 M6_Sky 값 불변");
                Assert.IsFalse(gm.Services.TryGet(out IAshStormInfo _), "은신처에 IAshStormInfo 없음");
                Assert.AreEqual(0, Object.FindObjectsByType<AudioDirector>(FindObjectsSortMode.None).Length, "은신처에 AudioDirector 없음");
                Assert.AreEqual(1, Object.FindObjectsByType<HideoutMusic>(FindObjectsSortMode.None).Length, "은신처 음악");
                hideout.AssertResultPanel(extracted, expectCarriedOut: true);
                yield return hideout.ConfirmResult(pointer);

                // ── 흐름 레이드 2: 선량 0, 폭풍 처음부터, 레이드 씬 원래 안개·하늘 ──
                var raid2 = new M4Raid();
                yield return hideout.StartRaid(pointer, raid2);
                Assert.AreEqual(0f, raid2.Health.RadiationDose, "다음 레이드 선량 0 (이월 없음, Q27)");
                AshStormController storm2 = Object.FindFirstObjectByType<AshStormController>();
                Assert.IsNotNull(storm2);
                Assert.AreNotSame(storm, storm2);
                Assert.AreEqual(AshStormPhase.Pending, storm2.Phase, "다음 레이드 폭풍 Pending");
                Assert.That(storm2.StartSec, Is.InRange(storm2.Config.startWindowSec.x, storm2.Config.startWindowSec.y), "새 시작 시각");
                FogState raid2Scene = FogState.Capture();
                Assert.AreEqual(raidScene.Fog, raid2Scene.Fog, "레이드 2 안개 켜짐");
                Assert.AreEqual(raidScene.Mode, raid2Scene.Mode, "레이드 2 안개 모드");
                Assert.AreEqual(raidScene.Density, raid2Scene.Density, 1e-7f, "레이드 2 안개 밀도 = 맑을 때");
                Assert.AreEqual(raidScene.Color, raid2Scene.Color, "레이드 2 안개 색");
                Assert.AreSame(sharedSky, raid2Scene.Skybox, "레이드 2 하늘 = 같은 공유 M6_Sky");
                Assert.IsNull(storm2.Presenter.SkyCopy);
                Assert.IsFalse(AudioListener.pause);
                yield return M2Scene.WaitSeconds(1f);
                Assert.AreEqual(0f, raid2.Health.RadiationDose, "폭풍 전에는 선량이 쌓이지 않는다");
                Assert.IsTrue(gm.Services.TryGet(out IAshStormInfo info2) && ReferenceEquals(info2, storm2), "레이드 2 IAshStormInfo = 새 AshStorm");
                Assert.IsFalse(SceneManager.GetActiveScene().name != SceneNames.RaidCrimsonBasin);
            }
        }

        private static readonly string[] SkyColorProps = { "_Tint", "_SkyTint", "_GroundColor" };
        private static readonly string[] SkyFloatProps = { "_Exposure", "_Rotation", "_SunSize", "_AtmosphereThickness" };

        private static Color[] ColorProps(Material m) => SkyColorProps.Where(m.HasProperty).Select(m.GetColor).ToArray();

        private static float[] FloatProps(Material m) => SkyFloatProps.Where(m.HasProperty).Select(m.GetFloat).ToArray();
    }
}
#endif
