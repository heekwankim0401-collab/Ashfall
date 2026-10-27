#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using Ashfall.AI;
using Ashfall.Core;
using Ashfall.Inventory;
using Ashfall.Weapons;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ashfall.Tests.PlayMode
{
    // 에셋 교체 스모크 (docs/design/AssetSwap_design.md §3·§7): Ashfall/Setup/Apply Asset Swap이 얹은 스킨을 레이드 씬·무기 뷰모델에서 본다.
    // 패키지(git 제외)가 없는 clone이면 스킨 프리팹이 없으므로 Ignore — 그때 블록아웃이 숨겨지지 않는지는 RaidVisualSmokeTests가 본다.
    // 이름은 AssetSwapCatalog 계약 값(테스트 asmdef는 EditorTools를 참조하지 않는다).
    public class AssetSwapSceneSmokeTests
    {
        private const string SkinFolder = "Assets/_Project/Skins/";
        private const string RifleSkin = SkinFolder + "SK_Rifle.prefab";
        private const string ViewModelFolder = "Assets/_Project/Prefabs/Weapons/";
        private static readonly string[] WeaponIds = { "WPN-PST-01", "WPN-SMG-01", "WPN-RIF-01", "WPN-SHT-01", "WPN-DMR-01" };

        // Ignore는 일반 OneTimeSetUp에서 — [UnitySetUp] 코루틴 안에서 던지면 배치모드 테스트 러너가 멈춘다(10/27 clone 확인)
        [OneTimeSetUp]
        public void RequireSkins()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(RifleSkin) == null)
                Assert.Ignore("스킨 없음(에셋스토어 패키지 미포함 clone) — Ashfall/Setup/Apply Asset Swap 전");
        }

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

        // 불러온 레이드 씬(AudioDirector 등)이 다음 테스트로 새지 않게 빈 씬만 남긴다
        // (남기면 AudioFlowTests.WorldPause_PausesAudioListener가 씬의 AudioDirector 때문에 실패)
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            GameManager gm = GameManager.Instance;
            if (gm != null && gm.Flow.IsWorldPaused)
                gm.Flow.SetWorldPaused(false);
            Scene fresh = SceneManager.CreateScene("AssetSwapSmoke_" + (++sceneCounter));
            SceneManager.SetActiveScene(fresh);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if (s != fresh && s.isLoaded)
                    yield return SceneManager.UnloadSceneAsync(s);
            }
            yield return null;
        }

        private static int sceneCounter;

        private static void AssertSkin(BlockoutSkin skin, string label)
        {
            Assert.IsNotNull(skin, $"{label}: 스킨 없음");
            Assert.IsTrue(skin.isActiveAndEnabled, $"{label}: 스킨이 꺼져 있다");
            Assert.AreEqual(0, skin.GetComponentsInChildren<Collider>(true).Length, $"{label}: 스킨에 콜라이더 (판정은 블록아웃)");
            if (skin.name == "Skin")   // 여러 스킨(벽 타일·차량 더미)은 첫 "Skin"만 숨김 목록을 갖는다
                Assert.Greater(skin.Hidden.Length, 0, $"{label}: 숨길 블록아웃 없음");
            foreach (Renderer r in skin.Hidden)
            {
                Assert.IsNotNull(r, $"{label}: 숨김 대상 없음");
                Assert.IsTrue(r.enabled, $"{label}/{r.name}: 블록아웃 렌더러 직렬화 값은 켜진 그대로");
                Assert.IsTrue(r.forceRenderingOff, $"{label}/{r.name}: 실행 중 숨김");
            }
            Assert.Greater(skin.GetComponentsInChildren<Renderer>(true).Length, 0, $"{label}: 스킨 렌더러 없음");
        }

        [Test]
        public void ViewModels_HaveSkin_MuzzleKept()
        {
            foreach (string id in WeaponIds)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ViewModelFolder + "VM_" + id + ".prefab");
                Assert.IsNotNull(prefab, id);
                var go = Object.Instantiate(prefab);
                try
                {
                    Assert.IsNotNull(go.transform.Find("Muzzle"), $"{id}: Muzzle");
                    AssertSkin(go.transform.Find("Skin")?.GetComponent<BlockoutSkin>(), id);
                    Bounds skinBounds = Bounds(go.transform.Find("Skin"));
                    Vector3 muzzle = go.transform.Find("Muzzle").position;
                    Assert.Less(Mathf.Abs(skinBounds.max.z - muzzle.z), 0.02f, $"{id}: 스킨 앞 끝 = 총구");
                }
                finally
                {
                    Object.DestroyImmediate(go);
                }
            }
        }

        [UnityTest]
        public IEnumerator RaidScene_SkinsOnEnemiesStructuresCrates()
        {
            yield return M4Raid.LoadScene(SceneNames.RaidCrimsonBasin);

            EnemyAgent[] enemies = Object.FindObjectsByType<EnemyAgent>(FindObjectsSortMode.None);
            Assert.Greater(enemies.Length, 30);
            foreach (EnemyAgent enemy in enemies)
            {
                Transform body = enemy.transform.Find("Body");
                BlockoutSkin skin = body.Find("Skin")?.GetComponent<BlockoutSkin>();
                AssertSkin(skin, enemy.name);
                var animator = skin.GetComponentInChildren<Animator>();
                Assert.IsNotNull(animator, $"{enemy.name}: 사람 스킨 Animator");
                Assert.IsNotNull(animator.runtimeAnimatorController, $"{enemy.name}: Idle 컨트롤러");
                Assert.IsTrue(animator.avatar != null && animator.avatar.isHuman, $"{enemy.name}: 휴머노이드 아바타 (없으면 Idle 클립이 안 돈다)");
                float feet = Bounds(skin.transform).min.y;
                Assert.Less(Mathf.Abs(feet - enemy.transform.position.y), 0.35f, $"{enemy.name}: 발이 지면 근처");
            }

            GameObject environment = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects()
                .Single(g => g.name == "Environment");
            BlockoutSkin[] skins = environment.GetComponentsInChildren<BlockoutSkin>(true);
            Assert.Greater(skins.Length, 100, "구조물·상자 스킨 수");
            foreach (BlockoutSkin s in skins)
                AssertSkin(s, s.transform.parent.name);
            foreach (LootContainer crate in environment.GetComponentsInChildren<LootContainer>(true))
            {
                AssertSkin(crate.transform.Find("Skin")?.GetComponent<BlockoutSkin>(), crate.name);
                Assert.IsNotNull(crate.GetComponent<Collider>(), $"{crate.name}: 상자 콜라이더(상호작용)는 블록아웃");
            }
        }

        [UnityTest]
        public IEnumerator HeldWeapon_ShowsSkin()
        {
            yield return M4Raid.LoadScene(SceneNames.RaidCrimsonBasin);
            WeaponController weapon = Object.FindObjectsByType<WeaponController>(FindObjectsSortMode.None).Single();
            yield return null;
            Transform view = weapon.CurrentViewModel;
            Assert.IsNotNull(view, "든 뷰모델");
            AssertSkin(view.Find("Skin")?.GetComponent<BlockoutSkin>(), view.name);
        }

        private static Bounds Bounds(Transform t)
        {
            Renderer[] rs = t.GetComponentsInChildren<Renderer>(true);
            Bounds b = rs[0].bounds;
            foreach (Renderer r in rs)
                b.Encapsulate(r.bounds);
            return b;
        }
    }
}
#endif
