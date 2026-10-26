#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using System.Text.RegularExpressions;
using Ashfall.AI;
using Ashfall.Audio;
using Ashfall.Core;
using Ashfall.Raid;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ashfall.Tests.PlayMode
{
    // 맵 개선 스모크 (docs/design/MapPolish_design.md §6): Ashfall/Setup/Apply Map Polish가 적용한 Raid_CrimsonBasin을 불러
    // 랜드마크 8(프리팹 인스턴스·높이)·중간 구조물·광원 14(그림자 없음·SO 값)·새 엄폐 지점 NavMesh를 본다.
    // 이름은 MapPolishLayout 계약 값(테스트 asmdef는 EditorTools를 참조하지 않는다).
    public class MapPolishSceneSmokeTests
    {
        private const string AtmosphereConfigPath = "Assets/_Project/Data/Raid/AtmosphereConfig.asset";
        private const string EnvironmentObject = "Environment";
        private const string LandmarkGroup = "MapPolish_Landmarks";
        private const string StructureGroup = "MapPolish_Structures";
        private const string LightGroup = "MapPolish_Lights";
        private const string PaletteMaterialFolder = "Assets/_Project/Art/Materials/Palette/";
        private const string LandmarkPrefabFolder = "Assets/_Project/Prefabs/Landmarks/";
        private const string RaidScenePath = "Assets/_Project/Scenes/Raid_CrimsonBasin.unity";
        private const int ExpectedLandmarks = 8;
        private const int ExpectedLightProps = 13;
        private const int ExpectedLights = 14;
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
            Assert.IsNotNull(cfg, AtmosphereConfigPath);
            yield return null;
        }

        private static Transform Group(string name)
        {
            GameObject environment = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects()
                .Single(g => g.name == EnvironmentObject);
            Transform group = environment.transform.Find(name);
            Assert.IsNotNull(group, $"Environment/{name} 없음 (Ashfall/Setup/Apply Map Polish)");
            return group;
        }

        [UnityTest]
        public IEnumerator RaidScene_LandmarksStructuresLights_Placed()
        {
            yield return M4Raid.LoadScene(SceneNames.RaidCrimsonBasin);
            Terrain terrain = Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None).Single();

            // 랜드마크: 래퍼마다 프리팹 인스턴스 "Model" 하나, 팔레트 머티리얼, 지면에 붙어 있다
            string[] sceneDependencies = AssetDatabase.GetDependencies(RaidScenePath, false);
            Transform landmarks = Group(LandmarkGroup);
            Assert.AreEqual(ExpectedLandmarks, landmarks.childCount, "랜드마크 수");
            float tallest = 0f;
            foreach (Transform wrapper in landmarks)
            {
                StringAssert.StartsWith("LM_", wrapper.name);
                Transform model = wrapper.Find("Model");
                Assert.IsNotNull(model, $"{wrapper.name}/Model");
                // 런타임 LoadScene으로 연 씬은 프리팹 연결 정보가 없어 GetCorrespondingObjectFromSource가 null이다 — 씬 에셋의 직접 의존성으로 본다
                string prefabPath = LandmarkPrefabFolder + Regex.Replace(wrapper.name, @"_\d+$", "") + ".prefab";
                CollectionAssert.Contains(sceneDependencies, prefabPath, $"{wrapper.name}: 씬이 랜드마크 프리팹을 참조하지 않는다 (프리팹 인스턴스가 아니다)");
                Bounds bounds = model.GetComponentsInChildren<Renderer>().Select(r => r.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; });
                float ground = terrain.SampleHeight(wrapper.position) + terrain.transform.position.y;
                Assert.Less(Mathf.Abs(wrapper.position.y - ground), 3f, $"{wrapper.name}: 지면에서 떨어져 있다");
                Assert.Greater(bounds.max.y - ground, 5f, $"{wrapper.name}: 너무 낮다");
                tallest = Mathf.Max(tallest, bounds.max.y - ground);
                foreach (MeshRenderer renderer in model.GetComponentsInChildren<MeshRenderer>())
                    StringAssert.StartsWith(PaletteMaterialFolder, AssetDatabase.GetAssetPath(renderer.sharedMaterial), $"{wrapper.name}/{renderer.name}");
                foreach (Collider collider in model.GetComponentsInChildren<Collider>())
                    Assert.IsNotNull(collider.GetComponent<GroundSurface>(), $"{wrapper.name}/{collider.name}: GroundSurface 없음");
            }
            Assert.Greater(tallest, 40f, "멀리서 보이는 랜드마크(플레어 스택·라디오 마스트)");

            // 중간 구조물: 블록아웃 도형 + 엄폐 지점
            Transform structures = Group(StructureGroup);
            Assert.GreaterOrEqual(structures.childCount, 24, "중간 구조물 수");
            Assert.Greater(structures.GetComponentsInChildren<CoverPoint>().Length, 30, "중간 구조물 엄폐 지점");

            // 광원 소품
            Transform props = Group(LightGroup);
            Assert.AreEqual(ExpectedLightProps, props.childCount, "광원 소품 수");
        }

        [UnityTest]
        public IEnumerator RaidScene_PointLights_NoShadows_FromConfig()
        {
            yield return M4Raid.LoadScene(SceneNames.RaidCrimsonBasin);
            // 맵 개선 광원은 두 그룹 안에서만 센다(리뷰 L7 — 나중에 다른 광원이 씬에 들어와도), 상한은 씬 전체 점광원으로
            Light[] points = Group(LandmarkGroup).GetComponentsInChildren<Light>().Concat(Group(LightGroup).GetComponentsInChildren<Light>()).ToArray();
            int sceneLights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Count(l => l.type != LightType.Directional);
            Assert.AreEqual(ExpectedLights, points.Length, "맵 개선 점광원 수 (§3)");
            Assert.LessOrEqual(sceneLights, cfg.maxMapLights, "씬 점광원 상한");
            PaletteSO palette = cfg.palette;
            foreach (Light light in points)
            {
                Assert.AreEqual(LightType.Point, light.type, light.name);
                Assert.AreEqual(LightShadows.None, light.shadows, $"{light.name}: 그림자 없음");
                MapLightStyle style = light.name.StartsWith("Light_Fire") ? cfg.fireLight : cfg.lampLight;
                Assert.AreEqual(style.intensity, light.intensity, 1e-4f, light.name);
                Assert.AreEqual(style.rangeM, light.range, 1e-4f, light.name);
                Color expected = Color.Lerp(palette.Get(style.token), palette.Get(PaletteToken.Bone), style.boneMix);
                Assert.AreEqual(expected.r, light.color.r, ColorEps, light.name);
                Assert.AreEqual(expected.g, light.color.g, ColorEps, light.name);
                Assert.AreEqual(expected.b, light.color.b, ColorEps, light.name);
            }
            Assert.IsTrue(points.Any(l => l.name.StartsWith("Light_Fire")), "불 광원");
            Assert.IsTrue(points.Any(l => l.name.StartsWith("Light_Lamp")), "가로등 광원");
        }

        [UnityTest]
        public IEnumerator RaidScene_NewCoverPoints_OnNavMesh()
        {
            yield return M4Raid.LoadScene(SceneNames.RaidCrimsonBasin);
            int count = 0;
            foreach (string group in new[] { LandmarkGroup, StructureGroup })
                foreach (CoverPoint cover in Group(group).GetComponentsInChildren<CoverPoint>())
                {
                    Assert.IsTrue(NavMesh.SamplePosition(cover.transform.position, out NavMeshHit _, 0.5f, NavMesh.AllAreas),
                        $"{cover.transform.parent.name}/{cover.name}: NavMesh 밖 {cover.transform.position}");
                    count++;
                }
            Assert.Greater(count, 30, "새 엄폐 지점 수");
        }

        // 구조물 자리가 NavMesh에 구멍으로 반영됐다 = NavMesh를 새 구조물과 함께 다시 구웠다 (리뷰 M2 — 옛 NavMesh면 실패)
        [UnityTest]
        public IEnumerator RaidScene_NavMesh_BakedWithNewStructures()
        {
            yield return M4Raid.LoadScene(SceneNames.RaidCrimsonBasin);
            Terrain terrain = Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None).Single();
            int total = 0;
            foreach (Transform structure in Group(StructureGroup))
            {
                Vector3 p = structure.position;
                var ground = new Vector3(p.x, terrain.SampleHeight(p) + terrain.transform.position.y, p.z);
                Assert.IsFalse(NavMesh.SamplePosition(ground, out NavMeshHit hit, 0.3f, NavMesh.AllAreas),
                    $"{structure.name}: 구조물 자리에 NavMesh가 있다 {hit.position} (다시 굽지 않았다)");
                total++;
            }
            Assert.GreaterOrEqual(total, 24);
            // 그룹 루트는 Not Walkable 수정자 — 구조물 윗면에 NavMesh 섬이 없다
            foreach (string group in new[] { LandmarkGroup, StructureGroup, LightGroup })
                Assert.IsNotNull(Group(group).GetComponent<Unity.AI.Navigation.NavMeshModifier>(), $"{group}: NavMeshModifier 없음");
        }
    }
}
#endif
