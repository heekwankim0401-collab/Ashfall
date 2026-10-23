#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Ashfall.AI;
using Ashfall.Core;
using Ashfall.Raid;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ashfall.Tests.PlayMode
{
    // M7 7B-2c 적 배치 스모크 (M7 설계 §1.4·§3.5·§6 "7B-2c", Q22~Q30, SYS-AI):
    // M7EnemySceneApply.ApplyRaid(7B-3 통합 = Ashfall/Setup/Apply M7 Content, 단독 = Ashfall/Setup/M7/Apply Enemies (Raid Scene))를
    // 적용한 Raid_CrimsonBasin을 이름으로 불러 확인한다: 적 34명(SCV 12·RDR 8·SNP 2·MUT 8·BOS 1·BGD 3), M7 14명 이름 "Enemy_M7_"·최상위·NavMesh 위,
    // 모든 스폰에서 190m 이상, 저격수 섬광 자식(SniperGlint, 콜라이더·그림자 없음, 처음엔 꺼짐, M_SniperGlint = Bone × HDR),
    // 유형별 몸 머티리얼 = 팔레트 토큰(변이체 Ash_Grey, 보스 Ember, 저격수·호위 Bone), 변이체는 총 모양 없음, 몇 초 돌려도 오류·이탈 없음.
    // 경로·이름은 M7Names·PaletteMaterialMap 계약 값(테스트 asmdef는 EditorTools를 참조하지 않는다).
    public class M7EnemySceneSmokeTests
    {
        private const float LoadTimeoutSec = 60f;
        private const int WarmupFrames = 5;
        private const float RunSeconds = 3f;
        private const float ColorEps = 1.5f / 255f;

        // M7EnemyLayout 계약 (설계 §3.5, Q30)
        private const string M7EnemyPrefix = "Enemy_M7_";
        private const int ExpectedTotal = 34;
        private const int ExpectedM7 = 14;
        private const float MinSpawnDistanceM = 190f;
        private static readonly (string id, int count)[] ExpectedByArchetype =
        {
            ("AI-SCV", 12), ("AI-RDR", 8), ("AI-SNP", 2), ("AI-MUT", 8), ("AI-BOS", 1), ("AI-BGD", 3),
        };

        private const string SniperGlintObject = "SniperGlint";
        private const string PalettePath = "Assets/_Project/Data/Art/Palette.asset";
        private const string MutantMaterialPath = "Assets/_Project/Art/Materials/Palette/M_Mutant.mat";
        private const string BossMaterialPath = "Assets/_Project/Art/Materials/Palette/M_Boss.mat";
        private const string RaiderMaterialPath = "Assets/_Project/Art/Materials/M3_EnemyRDR.mat";
        private const string GlintMaterialPath = "Assets/_Project/Art/Materials/M_SniperGlint.mat";
        private const float GlintHdr = 4f;   // PaletteMaterialMap.SniperGlint.Emission

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            GameManager gm = GameManager.Instance;
            if (gm != null)
            {
                gm.Flow.SetWorldPaused(false);
                Time.timeScale = 1f;
            }
            yield return null;
        }

        [UnityTest]
        [Timeout(180000)]
        public IEnumerator RaidScene_34Enemies_M7Placement_Glint_PaletteMaterials()
        {
            yield return LoadScene(SceneNames.RaidCrimsonBasin);

            EnemyAgent[] enemies = Object.FindObjectsByType<EnemyAgent>(FindObjectsSortMode.InstanceID);
            Assert.AreEqual(ExpectedTotal, enemies.Length, "레이드 적 수 = M4 20 + M7 14");
            foreach (var (id, count) in ExpectedByArchetype)
                Assert.AreEqual(count, enemies.Count(e => e.Archetype != null && e.Archetype.id == id), $"{id} 수");

            AiDirector director = Object.FindFirstObjectByType<AiDirector>();
            Assert.IsNotNull(director, "AiDirector 없음");
            Assert.AreEqual(ExpectedTotal, director.Agents.Count, "AiDirector에 등록된 적 수");

            EnemyAgent[] m7 = enemies.Where(e => e.name.StartsWith(M7EnemyPrefix)).ToArray();
            Assert.AreEqual(ExpectedM7, m7.Length, $"이름 {M7EnemyPrefix}* 적 수 — Ashfall/Setup/M7/Apply Enemies (Raid Scene) 또는 Apply M7 Content 실행");
            Assert.AreEqual(m7.Length, m7.Select(e => e.name).Distinct().Count(), "M7 적 이름 중복");

            var spawns = Object.FindObjectsByType<PlayerSpawnPoint>(FindObjectsSortMode.None);
            Assert.AreEqual(3, spawns.Length, "스폰 수");

            PaletteSO palette = AssetDatabase.LoadAssetAtPath<PaletteSO>(PalettePath);
            Assert.IsNotNull(palette, PalettePath);
            var bodyMaterials = new Dictionary<string, (string path, PaletteToken token)>
            {
                { "AI-SNP", (RaiderMaterialPath, PaletteToken.Bone) },
                { "AI-BGD", (RaiderMaterialPath, PaletteToken.Bone) },
                { "AI-MUT", (MutantMaterialPath, PaletteToken.AshGrey) },
                { "AI-BOS", (BossMaterialPath, PaletteToken.Ember) },
            };
            var glintMaterial = AssetDatabase.LoadAssetAtPath<Material>(GlintMaterialPath);
            Assert.IsNotNull(glintMaterial, GlintMaterialPath);
            Assert.AreEqual("Universal Render Pipeline/Unlit", glintMaterial.shader.name, "섬광 = URP Unlit");
            Color glintColor = glintMaterial.GetColor("_BaseColor");
            Color bone = palette.Get(PaletteToken.Bone);
            Assert.AreEqual(bone.r * GlintHdr, glintColor.r, ColorEps * GlintHdr, "섬광 r = Bone × HDR");
            Assert.AreEqual(bone.g * GlintHdr, glintColor.g, ColorEps * GlintHdr, "섬광 g = Bone × HDR");
            Assert.AreEqual(bone.b * GlintHdr, glintColor.b, ColorEps * GlintHdr, "섬광 b = Bone × HDR");
            Assert.Greater(glintColor.maxColorComponent, 1f, "HDR (Bloom이 받는다)");

            int glints = 0;
            foreach (EnemyAgent enemy in m7)
            {
                string id = enemy.Archetype != null ? enemy.Archetype.id : "(없음)";
                Assert.IsTrue(bodyMaterials.ContainsKey(id), $"{enemy.name}: M7 적인데 유형 {id}");
                Assert.IsNull(enemy.transform.parent, $"{enemy.name}: 적 루트는 최상위여야 한다");
                Assert.IsTrue(enemy.GetComponent<NavMeshAgent>().isOnNavMesh, $"{enemy.name}: NavMesh 위에 없다");

                Vector3 p = enemy.transform.position;
                foreach (PlayerSpawnPoint spawn in spawns)
                {
                    float d = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(spawn.FeetPosition.x, spawn.FeetPosition.z));
                    Assert.GreaterOrEqual(d, MinSpawnDistanceM, $"{enemy.name} ↔ {spawn.Id}: {d:0.0}m");
                }

                // 몸 머티리얼 = 팔레트
                var (path, token) = bodyMaterials[id];
                Transform body = enemy.transform.Find("Body");
                Assert.IsNotNull(body, $"{enemy.name}: Body");
                Material m = body.GetComponent<MeshRenderer>().sharedMaterial;
                Assert.AreSame(AssetDatabase.LoadAssetAtPath<Material>(path), m, $"{enemy.name}: 몸 머티리얼 {path}");
                AssertColor(palette.Get(token), m.GetColor("_BaseColor"), $"{enemy.name}: {m.name} = 팔레트 {PaletteSO.GddName(token)}");

                bool armed = enemy.Archetype.loadouts != null && enemy.Archetype.loadouts.Count > 0;
                Transform gun = enemy.transform.Find("Muzzle/Gun");
                Assert.AreEqual(armed, gun != null, $"{enemy.name}: 총 모양 큐브는 무장 유형만");

                Transform glint = enemy.transform.Find(SniperGlintObject);
                if (id != "AI-SNP")
                {
                    Assert.IsNull(glint, $"{enemy.name}: 저격수가 아닌데 섬광");
                    continue;
                }
                glints++;
                Assert.IsNotNull(glint, $"{enemy.name}: 섬광 자식 \"{SniperGlintObject}\" 없음");
                Assert.IsNotNull(glint.GetComponent<SniperGlint>(), $"{enemy.name}: SniperGlint 컴포넌트");
                Assert.IsTrue(glint.gameObject.activeInHierarchy, $"{enemy.name}: 섬광 오브젝트는 켜 둔다(렌더러로 켜고 끈다)");
                Assert.IsNull(glint.GetComponent<Collider>(), $"{enemy.name}: 섬광에 콜라이더");
                var renderer = glint.GetComponent<MeshRenderer>();
                Assert.IsNotNull(renderer, $"{enemy.name}: 섬광 렌더러");
                Assert.AreSame(glintMaterial, renderer.sharedMaterial, $"{enemy.name}: 섬광 머티리얼");
                Assert.AreEqual(ShadowCastingMode.Off, renderer.shadowCastingMode, $"{enemy.name}: 섬광 그림자");
                Assert.IsFalse(renderer.receiveShadows, $"{enemy.name}: 섬광 그림자 받기");
                Assert.IsFalse(renderer.enabled, $"{enemy.name}: 시작 직후(표적 150m 밖) 섬광은 꺼져 있다");
                Assert.Greater(glint.localPosition.y, 1f, $"{enemy.name}: 섬광은 눈 높이");
            }
            Assert.AreEqual(2, glints, "저격수 2명 모두 섬광");

            // 몇 초 돌린다 — 예외·LogError 없음(프레임워크), 아무도 NavMesh를 벗어나지 않는다
            float end = Time.time + RunSeconds;
            while (Time.time < end)
                yield return null;
            foreach (EnemyAgent enemy in m7)
                Assert.IsTrue(enemy.GetComponent<NavMeshAgent>().isOnNavMesh, $"{enemy.name}: 실행 후 NavMesh 밖");
        }

        private static void AssertColor(Color expected, Color actual, string message)
        {
            Assert.AreEqual(expected.r, actual.r, ColorEps, message + " r");
            Assert.AreEqual(expected.g, actual.g, ColorEps, message + " g");
            Assert.AreEqual(expected.b, actual.b, ColorEps, message + " b");
        }

        private static IEnumerator LoadScene(string sceneName)
        {
            AsyncOperation op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            Assert.IsNotNull(op, $"{sceneName} 로드 실패 (빌드 설정 확인)");
            float deadline = Time.realtimeSinceStartup + LoadTimeoutSec;
            while (!op.isDone)
            {
                Assert.Less(Time.realtimeSinceStartup, deadline, $"{sceneName} 로드 시간 초과");
                yield return null;
            }
            for (int i = 0; i < WarmupFrames; i++)
                yield return null;
            Assert.AreEqual(sceneName, SceneManager.GetActiveScene().name);
        }
    }
}
#endif
