#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Ashfall.AI;
using Ashfall.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ashfall.Tests.PlayMode
{
    // M7 7B-2a: AI-SNP 저격수 흐름 (M7 설계 §1.4, §6 7B-2 표 2a, Q22·Q23). 코드로 만든 장면:
    // 빈 씬(다른 씬은 내림) + 높이 3000m에 퍼치 발판 NavMesh(런타임 굽기) + AiDirector(AiConfig 에셋) + 저격수(AI-SNP 에셋,
    // 자식 Quad "SniperGlint") + 120m 앞 서 있는 무적 표적(TestTarget) + 가짜 IProjectileService(발사만 센다).
    // 섬광 머티리얼(M_SniperGlint)은 배치 메뉴(2c) 몫이라 여기서는 기본 Quad 머티리얼 그대로 쓴다.
    public class SniperFlowTests
    {
        private const string AiConfigPath = "Assets/_Project/Data/AI/AiConfig.asset";
        private const string SniperPath = "Assets/_Project/Data/AI/AI-SNP.asset";
        private const string GlintName = "SniperGlint";   // M7Names.SniperGlintObject (테스트 asmdef는 EditorTools를 참조하지 않는다)

        private static readonly Vector3 Origin = new Vector3(0f, 3000f, 0f);
        private const float PerchHeightM = 20f;
        private const float TargetDistanceM = 120f;
        private const float TimeTol = 0.05f;
        private const int GcFrames = 120;

        private static int sceneCounter;

        private AiConfigSO config;
        private EnemyArchetypeSO sniperArchetype;
        private NavMeshDataInstance navInstance;
        private NavMeshData navData;
        private GameObject root;
        private AiDirector director;
        private EnemyAgent sniper;
        private SniperGlint glint;
        private Renderer glintRenderer;
        private TestTarget target;
        private FakeProjectiles projectiles;
        private FakeStorm storm;
        private Action<ShotFired> shotHandler;
        private readonly List<float> enemyShotTimes = new List<float>();
        private Vector3 perchPosition;

        private static GameManager Gm => GameManager.Instance;

        private sealed class FakeProjectiles : IProjectileService
        {
            public int Fired;
            public void Fire(Vector3 origin, Vector3 direction, AmmoDefinitionSO ammo, float damage, Transform shooterRoot) => Fired++;
        }

        private sealed class FakeStorm : IAshStormInfo
        {
            public AshStormPhase Phase { get; set; } = AshStormPhase.Peak;
            public float Intensity { get; set; } = 1f;
            public float VisibilityM { get; set; } = 30f;
            public float RadiationPerSec { get; set; }
        }

        // ---------- 준비 ----------

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Assert.IsNotNull(Gm, "GameManager 없음");
            Gm.Flow.SetWorldPaused(false);

            // 앞선 테스트의 씬(적·플레이어·AiDirector·NavMesh)과 섞이지 않게 빈 씬만 남긴다
            Scene fresh = SceneManager.CreateScene("SniperFlowTest_" + (++sceneCounter));
            SceneManager.SetActiveScene(fresh);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if (s != fresh && s.isLoaded)
                    yield return SceneManager.UnloadSceneAsync(s);
            }
            yield return null;
            ClearService<IPlayerTarget>();
            ClearService<IProjectileService>();
            ClearService<IAshStormInfo>();

            config = AssetDatabase.LoadAssetAtPath<AiConfigSO>(AiConfigPath);
            sniperArchetype = AssetDatabase.LoadAssetAtPath<EnemyArchetypeSO>(SniperPath);
            Assert.IsNotNull(config, AiConfigPath);
            Assert.IsNotNull(sniperArchetype, SniperPath + " (Ashfall/Setup/Create M7 AI Data)");
            Assert.AreEqual(EnemyTacticsKind.Sniper, sniperArchetype.tactics);
            Assert.Greater(sniperArchetype.telegraphSec, 0f);

            enemyShotTimes.Clear();
            shotHandler = OnShotFired;
            Gm.Events.Subscribe(shotHandler);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (Gm != null)
            {
                if (shotHandler != null)
                    Gm.Events.Unsubscribe(shotHandler);
                if (projectiles != null)
                    Gm.Services.Unregister<IProjectileService>(projectiles);
                if (storm != null)
                    Gm.Services.Unregister<IAshStormInfo>(storm);
            }
            shotHandler = null;
            projectiles = null;
            storm = null;
            if (target != null)
                Object.Destroy(target.gameObject);   // OnDisable에서 IPlayerTarget 해제
            if (sniper != null)
                Object.Destroy(sniper.gameObject);
            if (root != null)
                Object.Destroy(root);
            sniper = null;
            target = null;
            root = null;
            yield return null;
            if (navInstance.valid)
                navInstance.Remove();
            if (navData != null)
                Object.Destroy(navData);
            navData = null;
        }

        private static void ClearService<T>() where T : class
        {
            if (Gm.Services.TryGet(out T existing))
                Gm.Services.Unregister(existing);
        }

        private void OnShotFired(ShotFired e)
        {
            if (e.Source == SoundSource.Enemy)
                enemyShotTimes.Add(Time.time);
        }

        // 퍼치 발판 NavMesh(10×10m 상자 윗면)만 굽는다. 표적 쪽은 NavMesh가 필요 없다.
        private void BakePerch(Vector3 perchTop)
        {
            Vector3 center = perchTop - new Vector3(0f, 0.5f, 0f);
            var source = new NavMeshBuildSource
            {
                shape = NavMeshBuildSourceShape.Box,
                size = new Vector3(10f, 1f, 10f),
                transform = Matrix4x4.TRS(center, Quaternion.identity, Vector3.one),
                area = 0,
            };
            NavMeshBuildSettings settings = NavMesh.GetSettingsByID(0);
            navData = NavMeshBuilder.BuildNavMeshData(settings, new List<NavMeshBuildSource> { source },
                new Bounds(center, new Vector3(20f, 10f, 20f)), Vector3.zero, Quaternion.identity);
            Assert.IsNotNull(navData, "NavMesh 굽기 실패");
            navInstance = NavMesh.AddNavMeshData(navData);
            Assert.IsTrue(navInstance.valid);
        }

        private static void SetRef(SerializedObject so, string name, Object value)
        {
            SerializedProperty p = so.FindProperty(name);
            Assert.IsNotNull(p, name);
            p.objectReferenceValue = value;
        }

        // 표적·발사 서비스·(선택) 폭풍을 등록하고 AiDirector·저격수를 만든다. 저격수는 +z(표적 쪽)를 본다.
        private IEnumerator Build(float? stormVisibilityM)
        {
            root = new GameObject("SniperFlowTestRoot");
            perchPosition = Origin + Vector3.up * PerchHeightM;
            BakePerch(perchPosition);

            target = TestTarget.Create(Origin + new Vector3(0f, 0f, TargetDistanceM));
            target.Register();
            projectiles = new FakeProjectiles();
            Gm.Services.Register<IProjectileService>(projectiles);
            if (stormVisibilityM.HasValue)
            {
                storm = new FakeStorm { VisibilityM = stormVisibilityM.Value };
                Gm.Services.Register<IAshStormInfo>(storm);
            }

            var directorGo = new GameObject("AiDirector");
            directorGo.SetActive(false);
            directorGo.transform.SetParent(root.transform);
            director = directorGo.AddComponent<AiDirector>();
            var dso = new SerializedObject(director);
            SetRef(dso, "config", config);
            dso.ApplyModifiedPropertiesWithoutUndo();
            directorGo.SetActive(true);

            // 적 루트는 씬 최상위 (EnemyAgent 규칙)
            var go = new GameObject("Enemy_SNP_Test");
            go.SetActive(false);
            go.transform.SetPositionAndRotation(perchPosition, Quaternion.identity);
            go.AddComponent<NavMeshAgent>();
            var capsule = go.AddComponent<CapsuleCollider>();
            capsule.radius = 0.35f;
            capsule.height = config.standHeightM;
            capsule.center = new Vector3(0f, config.standHeightM * 0.5f, 0f);
            go.AddComponent<Rigidbody>();
            var eye = new GameObject("Eye").transform;
            eye.SetParent(go.transform, false);
            eye.localPosition = new Vector3(0f, config.standEyeHeightM, 0f);
            var muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(go.transform, false);
            muzzle.localPosition = new Vector3(0.15f, config.standEyeHeightM - 0.1f, 0.4f);
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);   // MeshCollider 포함 → SniperGlint가 끈다
            quad.name = GlintName;
            quad.transform.SetParent(go.transform, false);
            quad.transform.localPosition = new Vector3(0f, config.standEyeHeightM, 0.25f);
            quad.AddComponent<SniperGlint>();

            sniper = go.AddComponent<EnemyAgent>();
            var so = new SerializedObject(sniper);
            SetRef(so, "archetype", sniperArchetype);
            SetRef(so, "director", director);
            SetRef(so, "eye", eye);
            SetRef(so, "muzzle", muzzle);
            so.FindProperty("seed").intValue = 11;
            so.FindProperty("loadoutIndex").intValue = 0;
            so.ApplyModifiedPropertiesWithoutUndo();
            go.SetActive(true);

            glint = quad.GetComponent<SniperGlint>();
            glintRenderer = quad.GetComponent<Renderer>();

            AiDirector d = director;
            EnemyAgent s = sniper;
            yield return M2Scene.WaitUntil(() => s.Brain != null && s.Weapon != null && ReferenceEquals(d.Target, target), 3f);
            Assert.AreEqual(EnemyTacticsKind.Sniper, sniper.Brain.TacticsKind);
            Assert.IsTrue(sniper.GetComponent<NavMeshAgent>().isOnNavMesh, "퍼치가 NavMesh 위");
            perchPosition = sniper.transform.position;   // NavMesh 스냅 뒤 위치
        }

        private void AssertStaysOnPerch()
        {
            Vector3 d = sniper.transform.position - perchPosition;
            Assert.Less(d.magnitude, 0.05f, $"저격수 위치 불변 (이동 {d}, 상태 {sniper.State})");
        }

        private void AssertGlintConsistent()
        {
            Assert.AreEqual(glint.IsOn, glintRenderer.enabled, "섬광 켜짐 = 렌더러 켜짐");
            Assert.AreEqual(ShadowCastingMode.Off, glintRenderer.shadowCastingMode, "섬광 그림자 없음");
            Assert.IsFalse(glintRenderer.receiveShadows);
            Collider c = glint.GetComponent<Collider>();
            Assert.IsTrue(c == null || !c.enabled, "섬광 콜라이더 없음(꺼짐)");
        }

        private void AssertGlintFacesTarget()
        {
            Vector3 eye = target.EyePosition;
            Vector3 pos = glint.transform.position;
            Vector3 fromEye = (pos - eye).normalized;
            Assert.Greater(Vector3.Dot(glint.transform.forward, fromEye), 0.999f, "쿼드 앞면(-Z)이 표적 눈 쪽");
            float distance = Vector3.Distance(pos, eye);
            float expected = distance * Mathf.Tan(config.glintAngularSizeDeg * Mathf.Deg2Rad);
            Assert.AreEqual(expected, glint.WorldSize, expected * 0.02f, "크기 = 거리 × tan(각)");
            Assert.AreEqual(expected, glint.transform.lossyScale.x, expected * 0.02f);
            Assert.AreEqual(expected, glint.transform.lossyScale.y, expected * 0.02f);
        }

        // ---------- 테스트 ----------

        // 120m 서 있는 표적: 발견 → 섬광 켜짐(표적 쪽, 거리 비례 크기) → telegraphSec(1.5초) 뒤 첫 ShotFired. 그동안 자리 고정.
        [UnityTest]
        public IEnumerator Standing120m_GlintOnFacingTarget_ShotAfterTelegraph_StaysOnPerch()
        {
            yield return Build(null);
            Assert.IsFalse(glint.IsOn);
            Assert.IsFalse(glintRenderer.enabled, "평시 섬광 꺼짐");

            float deadline = Time.realtimeSinceStartup + 10f;
            while (!glint.IsOn)
            {
                Assert.Less(Time.realtimeSinceStartup, deadline, $"10초 안에 섬광이 켜지지 않았다 (상태 {sniper.State}, 게이지 {sniper.Brain.Awareness})");
                Assert.AreEqual(0, enemyShotTimes.Count, "섬광 전 사격 없음");
                AssertStaysOnPerch();
                AssertGlintConsistent();
                yield return null;
            }
            float glintOnAt = Time.time;
            Assert.IsTrue(sniper.State == EnemyState.Alert || sniper.State == EnemyState.Combat, $"교전 상태: {sniper.State}");
            AssertGlintConsistent();
            AssertGlintFacesTarget();

            float telegraph = sniperArchetype.telegraphSec;
            deadline = Time.realtimeSinceStartup + telegraph + 3f;
            while (enemyShotTimes.Count == 0)
            {
                Assert.Less(Time.realtimeSinceStartup, deadline, $"경고 뒤 사격이 없다 (상태 {sniper.State})");
                Assert.IsTrue(glint.IsOn, "경고 중 섬광 유지 (표적이 계속 보임)");
                AssertGlintConsistent();
                AssertGlintFacesTarget();
                AssertStaysOnPerch();
                yield return null;
            }
            float delay = enemyShotTimes[0] - glintOnAt;
            Assert.GreaterOrEqual(delay, telegraph - TimeTol, $"섬광 {telegraph}초 전에는 쏘지 않는다 (실제 {delay:0.000}초)");
            Assert.LessOrEqual(delay, telegraph + 0.5f, $"경고가 끝나면 곧 쏜다 (실제 {delay:0.000}초)");
            Assert.AreEqual(1, projectiles.Fired, "ShotFired = 실제 발사 1발");
            Assert.AreEqual(EnemyState.Combat, sniper.State);

            // 사격 사이에도 조준 중이면 섬광 유지, 계속 자리 고정
            for (int k = 0; k < 60; k++)
            {
                yield return null;
                Assert.IsTrue(glint.IsOn, "사격 사이 섬광 유지");
                AssertStaysOnPerch();
            }
        }

        // 폭풍 Peak(가짜 IAshStormInfo, 시야 상한 30m): 120m 표적을 못 본다 → 섬광 꺼짐·사격 없음·자리 고정.
        // 폭풍이 걷히면(상한 +∞) 같은 자리에서 본다(장면이 유효함을 확인).
        [UnityTest]
        public IEnumerator StormCap30_DoesNotSee_GlintOff_ThenClears_Sees()
        {
            yield return Build(30f);
            AiDirector d = director;
            FakeStorm st = storm;
            yield return M2Scene.WaitUntil(() => ReferenceEquals(d.Storm, st), 3f);
            Assert.AreEqual(30f, director.VisibilityCapM, 1e-4f);

            float end = Time.time + 5f;
            while (Time.time < end)
            {
                Assert.IsTrue(sniper.State == EnemyState.Idle || sniper.State == EnemyState.Patrol, $"폭풍 중 발견됨: {sniper.State}");
                Assert.AreEqual(0f, sniper.Brain.Awareness, 1e-4f, "인지 게이지 0");
                Assert.IsFalse(glint.IsOn, "못 보면 섬광 꺼짐");
                AssertGlintConsistent();
                Assert.AreEqual(0, enemyShotTimes.Count, "사격 없음");
                AssertStaysOnPerch();
                yield return null;
            }

            storm.VisibilityM = float.PositiveInfinity;
            storm.Intensity = 0f;
            storm.Phase = AshStormPhase.Over;
            float deadline = Time.realtimeSinceStartup + 10f;
            while (!glint.IsOn)
            {
                Assert.Less(Time.realtimeSinceStartup, deadline, $"폭풍이 걷힌 뒤 섬광이 켜지지 않았다 (상태 {sniper.State})");
                AssertStaysOnPerch();
                yield return null;
            }
            AssertGlintFacesTarget();
        }

        // 교전(섬광 켜짐·사격 반복) 중 AiDirector.Update 경로 GC 0 — AiPerformanceTests와 같은 방식(director를 끄고 직접 호출).
        [UnityTest]
        public IEnumerator Engaged_DirectorUpdate_NoGcAlloc()
        {
            yield return Build(null);
            EnemyAgent s = sniper;
            yield return M2Scene.WaitUntil(() => s.State == EnemyState.Combat && glint.IsOn, 10f);

            MethodInfo method = typeof(AiDirector).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method);
            var update = (Action)Delegate.CreateDelegate(typeof(Action), director, method);
            director.enabled = false;
            Recorder recorder = Recorder.Get("GC.Alloc");
            Action empty = () => { };
            int allocFrames = 0, allocSamples = 0, noisy = 0, glintFrames = 0;
            try
            {
                Sample(recorder, empty);
                Sample(recorder, update);   // 첫 호출(JIT) 제외
                for (int i = 0; i < GcFrames; i++)
                {
                    yield return null;
                    int n = Sample(recorder, update);
                    if (glint.IsOn)
                        glintFrames++;
                    if (n > 0)
                    {
                        if (Sample(recorder, empty) > 0)
                        {
                            noisy++;
                            continue;
                        }
                        allocFrames++;
                        allocSamples += n;
                    }
                }
            }
            finally
            {
                director.enabled = true;
            }
            Assert.Greater(glintFrames, GcFrames / 2, "측정 중 섬광 켜짐(교전 지속)");
            Assert.Less(noisy, GcFrames / 2, "GC 측정 잡음이 너무 많다");
            Assert.AreEqual(0, allocSamples, $"저격수 교전 중 AiDirector.Update GC 0 (할당 프레임 {allocFrames}, 잡음 {noisy})");
            AssertStaysOnPerch();
        }

        private static int Sample(Recorder recorder, Action action)
        {
            recorder.enabled = false;
            recorder.FilterToCurrentThread();
            recorder.enabled = true;
            try
            {
                action();
            }
            finally
            {
                recorder.enabled = false;
                recorder.CollectFromAllThreads();
            }
            return recorder.sampleBlockCount;
        }
    }
}
#endif
