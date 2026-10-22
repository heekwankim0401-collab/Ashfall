#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using Ashfall.Audio;
using Ashfall.Core;
using Ashfall.Inventory;
using Ashfall.Player;
using Ashfall.Raid;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ashfall.Tests.PlayMode
{
    // M6 AC1 레이드 비주얼 스모크 (§10.2, M6 설계 §1.3·§6 2a·§7 AC1, Q2~Q5·Q11·Q29):
    // Ashfall/Setup/M6/Apply Visual(M6VisualSceneApply)이 적용한 Raid_CrimsonBasin을 이름으로 불러
    // 카메라 포스트·Global Volume·Exp² 안개(Dried_Blood, 맑을 때 가시거리)·하늘·Sun·재 파티클·블록아웃 머티리얼·GroundSurface·웅덩이를 본다.
    // 은신처·메인 메뉴는 카메라 배경 Ash_Black(Q29). 콘솔 오류가 나면 테스트 프레임워크가 실패시킨다.
    // 이름은 M6Names 계약 값(테스트 asmdef는 EditorTools를 참조하지 않는다).
    public class RaidVisualSmokeTests
    {
        private const string AtmosphereConfigPath = "Assets/_Project/Data/Raid/AtmosphereConfig.asset";
        private const string VolumeProfilePath = "Assets/_Project/Art/PostProcess/VP_RaidAtmosphere.asset";
        private const string PaletteMaterialFolder = "Assets/_Project/Art/Materials/Palette/";
        private const string SunObject = "Sun";
        private const string GlobalVolumeObject = "M6_GlobalVolume";
        private const string AshParticlesObject = "AshParticles";
        private const string PuddlesObject = "M6_Puddles";
        private const string EnvironmentObject = "Environment";
        private const int ExpectedPuddles = 3;
        private const int IgnoreRaycastLayer = 2;
        private const float ColorEps = 1.5f / 255f;

        private AtmosphereConfigSO cfg;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            GameManager gm = GameManager.Instance;
            if (gm != null)
            {
                gm.Flow.SetWorldPaused(false);
                Time.timeScale = 1f;
            }
            cfg = AssetDatabase.LoadAssetAtPath<AtmosphereConfigSO>(AtmosphereConfigPath);
            Assert.IsNotNull(cfg, $"{AtmosphereConfigPath} 없음 (Ashfall/Setup/Create M6 Data)");
            Assert.IsNotNull(cfg.palette, "AtmosphereConfig.palette");
            yield return null;
        }

        private static void AssertRgb(Color expected, Color actual, string message)
        {
            Assert.AreEqual(expected.r, actual.r, ColorEps, message + " r");
            Assert.AreEqual(expected.g, actual.g, ColorEps, message + " g");
            Assert.AreEqual(expected.b, actual.b, ColorEps, message + " b");
        }

        private static GameObject Root(string name)
        {
            GameObject[] found = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects()
                .Where(g => g.name == name).ToArray();
            Assert.AreEqual(1, found.Length, $"최상위 \"{name}\" 수");
            return found[0];
        }

        [UnityTest]
        public IEnumerator RaidScene_HasPostVolumeFogSkySun()
        {
            yield return M4Raid.LoadScene(SceneNames.RaidCrimsonBasin);
            PaletteSO palette = cfg.palette;

            // 카메라 포스트 켬 (플레이어 카메라 하나)
            Camera camera = Camera.main;
            Assert.IsNotNull(camera, "MainCamera 없음");
            Assert.IsNotNull(camera.GetComponentInParent<PlayerMotor>(), "MainCamera가 플레이어 카메라가 아니다");
            var cameraData = camera.GetComponent<UniversalAdditionalCameraData>();
            Assert.IsNotNull(cameraData, "UniversalAdditionalCameraData 없음");
            Assert.IsTrue(cameraData.renderPostProcessing, "플레이어 카메라 포스트 처리 꺼짐");

            // Global Volume
            var volume = Root(GlobalVolumeObject).GetComponent<Volume>();
            Assert.IsNotNull(volume, "Volume 없음");
            Assert.IsTrue(volume.isGlobal, "Global이 아니다");
            Assert.AreEqual(1f, volume.weight, 1e-4f);
            Assert.AreSame(AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath), volume.sharedProfile, "프로필");
            Assert.AreEqual(1, Object.FindObjectsByType<Volume>(FindObjectsSortMode.None).Length, "볼륨 수");

            // 안개: Exp², Dried_Blood, 맑을 때 가시거리 밀도 (항상 켬 — 위험 R1)
            Assert.IsTrue(RenderSettings.fog, "안개 꺼짐");
            Assert.AreEqual(FogMode.ExponentialSquared, RenderSettings.fogMode);
            AssertRgb(palette.Get(cfg.fogToken), RenderSettings.fogColor, "안개 색 = Dried_Blood");
            float expectedDensity = Mathf.Sqrt(-Mathf.Log(cfg.fogVisibilityThreshold)) / cfg.clearVisibilityM;
            Assert.AreEqual(expectedDensity, RenderSettings.fogDensity, expectedDensity * 1e-3f, "안개 밀도 = 맑을 때 가시거리");
            // 가시거리에서 투과율 = threshold
            float transmittance = Mathf.Exp(-Mathf.Pow(RenderSettings.fogDensity * cfg.clearVisibilityM, 2f));
            Assert.AreEqual(cfg.fogVisibilityThreshold, transmittance, 1e-3f);

            // 하늘 (2c 폭풍이 복제본으로 바꿔도 셰이더·이름 접두어는 같다)
            Material sky = RenderSettings.skybox;
            Assert.IsNotNull(sky, "skybox 없음");
            Assert.AreEqual("Skybox/Panoramic", sky.shader.name);
            StringAssert.StartsWith("M6_Sky", sky.name);
            Assert.AreEqual(AmbientMode.Trilight, RenderSettings.ambientMode, "Trilight 환경광");

            // 태양: "Sun" Directional Light, 하늘 태양과 같은 방향
            Light sun = Root(SunObject).GetComponent<Light>();
            Assert.IsNotNull(sun, "Sun Light 없음");
            Assert.AreEqual(LightType.Directional, sun.type);
            Assert.AreSame(sun, RenderSettings.sun, "RenderSettings.sun");
            Vector3 toSun = -sun.transform.forward;
            Assert.AreEqual(cfg.sunElevationDeg, Mathf.Asin(toSun.y) * Mathf.Rad2Deg, 0.5f, "태양 고도");
            float azimuth = Mathf.Repeat(Mathf.Atan2(toSun.x, toSun.z) * Mathf.Rad2Deg, 360f);
            Assert.AreEqual(cfg.sunAzimuthDeg, azimuth, 0.5f, "태양 방위 (+Z = 0, 시계 방향)");
            AssertRgb(cfg.palette.Get(cfg.sunLightToken), sun.color, "태양 색");
            Assert.AreEqual(1, Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Count(l => l.type == LightType.Directional),
                "Directional Light 수");
        }

        [UnityTest]
        public IEnumerator RaidScene_AshParticles_AroundPlayer_WorldSpace_Playing()
        {
            yield return M4Raid.LoadScene(SceneNames.RaidCrimsonBasin);
            PlayerMotor player = Object.FindObjectsByType<PlayerMotor>(FindObjectsSortMode.None).Single();
            Transform ash = player.transform.Find(AshParticlesObject);
            Assert.IsNotNull(ash, "Player 자식 AshParticles 없음");
            Assert.AreEqual(1, Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)
                .Count(p => p.name == AshParticlesObject), "재 파티클 수");
            var ps = ash.GetComponent<ParticleSystem>();
            Assert.IsNotNull(ps);
            Assert.AreEqual(ParticleSystemSimulationSpace.World, ps.main.simulationSpace, "World 시뮬레이션");
            Assert.AreEqual(cfg.ashMaxParticles, ps.main.maxParticles, "평시 상한");
            Assert.AreEqual(ParticleSystemShapeType.Box, ps.shape.shapeType);
            Assert.AreEqual(cfg.ashRadiusM * 2f, ps.shape.scale.x, 1e-3f, "반경 30m (§10.2)");
            Assert.AreEqual(cfg.ashRadiusM * 2f, ps.shape.scale.z, 1e-3f);
            var renderer = ash.GetComponent<ParticleSystemRenderer>();
            Assert.AreEqual(ShadowCastingMode.Off, renderer.shadowCastingMode, "그림자 없음");
            Assert.AreEqual("M6_AshParticle", renderer.sharedMaterial.name);

            yield return null;
            Assert.IsTrue(ps.isPlaying, "재생 중이 아니다");
            Assert.Greater(ps.particleCount, 0, "입자가 없다(prewarm)");
            Assert.LessOrEqual(ps.particleCount, cfg.ashMaxParticles, "상한 초과");
        }

        [UnityTest]
        public IEnumerator RaidScene_BlockoutPainted_SurfacesAndPuddles()
        {
            yield return M4Raid.LoadScene(SceneNames.RaidCrimsonBasin);

            // 블록아웃·상자: 기본 머티리얼 없음 (전부 팔레트 머티리얼), 트리거 아닌 콜라이더에 GroundSurface
            GameObject environment = Root(EnvironmentObject);
            int renderers = 0;
            foreach (MeshRenderer renderer in environment.GetComponentsInChildren<MeshRenderer>(true))
            {
                string path = AssetDatabase.GetAssetPath(renderer.sharedMaterial);
                StringAssert.StartsWith(PaletteMaterialFolder, path, $"{renderer.transform.parent?.name}/{renderer.name}: 팔레트 머티리얼이 아니다");
                renderers++;
            }
            Assert.Greater(renderers, 100, "블록아웃 렌더러가 너무 적다");
            int surfaces = 0;
            foreach (MeshRenderer renderer in environment.GetComponentsInChildren<MeshRenderer>(true))
            {
                Collider collider = renderer.GetComponent<Collider>();
                if (collider == null || collider.isTrigger)
                    continue;
                Assert.IsNotNull(renderer.GetComponent<GroundSurface>(), $"{renderer.transform.parent?.name}/{renderer.name}: GroundSurface 없음");
                surfaces++;
            }
            Assert.Greater(surfaces, 100, "GroundSurface 수");

            foreach (LootContainer crate in environment.GetComponentsInChildren<LootContainer>(true))
            {
                Assert.AreEqual(SurfaceKind.Metal, crate.GetComponent<GroundSurface>().Kind, $"{crate.name} 재질");
                Assert.AreEqual("M_Crate", crate.GetComponent<MeshRenderer>().sharedMaterial.name);
            }
            PowerSwitch powerSwitch = Object.FindObjectsByType<PowerSwitch>(FindObjectsSortMode.None).Single();
            Assert.AreEqual("M_Accent", powerSwitch.GetComponent<MeshRenderer>().sharedMaterial.name, "스위치 강조색");

            // 차량 = 금속, 벽 = 잔해 (§3.4)
            Assert.IsTrue(environment.GetComponentsInChildren<GroundSurface>(true)
                .Any(s => s.Kind == SurfaceKind.Metal && s.transform.parent.name.StartsWith("Vehicle_")), "차량 금속 없음");
            Assert.IsTrue(environment.GetComponentsInChildren<GroundSurface>(true)
                .Any(s => s.Kind == SurfaceKind.Debris && s.transform.parent.name.StartsWith("LowWall_")), "낮은 벽 잔해 없음");

            // 물웅덩이 3개: 트리거 + Ignore Raycast + Water + M_Puddle, 지면 위로 수면
            Transform puddles = Root(PuddlesObject).transform;
            Assert.AreEqual(ExpectedPuddles, puddles.childCount, "웅덩이 수");
            Terrain terrain = Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None).Single();
            foreach (Transform puddle in puddles)
            {
                var box = puddle.GetComponent<BoxCollider>();
                Assert.IsNotNull(box, $"{puddle.name} 콜라이더");
                Assert.IsTrue(box.isTrigger, $"{puddle.name} 트리거");
                Assert.AreEqual(IgnoreRaycastLayer, puddle.gameObject.layer, $"{puddle.name} Ignore Raycast");
                Assert.AreEqual(SurfaceKind.Water, puddle.GetComponent<GroundSurface>().Kind, $"{puddle.name} Water");
                Assert.AreEqual("M_Puddle", puddle.GetComponent<MeshRenderer>().sharedMaterial.name);
                float ground = terrain.SampleHeight(puddle.position) + terrain.transform.position.y;
                Assert.Greater(box.bounds.max.y, ground, $"{puddle.name} 수면이 지면 아래");
                Assert.Less(box.bounds.max.y - ground, 1.5f, $"{puddle.name} 수면이 너무 높다");
                // 기본 레이캐스트(트리거 무시)에 걸리지 않는다 = 전투·상호작용 무영향
                Vector3 above = new Vector3(puddle.position.x, box.bounds.max.y + 1f, puddle.position.z);
                Assert.IsTrue(Physics.Raycast(above, Vector3.down, out RaycastHit hit, 5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore));
                Assert.AreNotSame(box, hit.collider, $"{puddle.name}이 기본 레이캐스트를 막는다");
            }
        }

        [UnityTest]
        public IEnumerator MainMenu_CameraBackground_IsAshBlack()
        {
            yield return M4Raid.LoadScene(SceneNames.MainMenu);
            Camera camera = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Single();
            Assert.AreEqual(CameraClearFlags.SolidColor, camera.clearFlags);
            AssertRgb(cfg.palette.ashBlack, camera.backgroundColor, "메인 메뉴 배경 = Ash_Black (Q29)");
            Assert.AreEqual(0, Object.FindObjectsByType<Volume>(FindObjectsSortMode.None).Length, "메뉴에 볼륨 없음 (Q5)");
        }
    }
}
#endif
