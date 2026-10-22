using Ashfall.Raid;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ashfall.EditorTools
{
    // M6 재 폭풍 씬 적용 (M6 설계 §1.5·§1.6·§4.7, 2c). 레이드 씬 최상위 "AshStorm"에 AshStormController + AshStormPresenter.
    // 멱등: 이름으로 찾아 있으면 다시 설정, 없으면 만든다. 컴포넌트는 GetComponent ?? AddComponent. 시드는 사람이 정한 값을 유지한다.
    // 참조: raid = 씬의 RaidController, config = AshStormConfig, atmosphere = AtmosphereConfig,
    //       ashParticles = 이름 "AshParticles"(2a가 Player 아래에 만든다 — 없으면 경고 + null), sun = "Sun"(없으면 씬의 첫 Directional Light).
    // 데이터 에셋은 M6DataSetup(1b)이 만든다. 통합 메뉴(M6SceneSetup.ApplyAll, 3단계)가 Apply(scene)를 부른다.
    // 설정 에셋(AshStormConfig·AtmosphereConfig)이 없으면 아무것도 만들지 않고 null을 돌려준다(리뷰 낮음 6 — 호출자가 실패로 본다).
    // (2c 단독 배치 진입점 ApplyRaidScene은 통합 메뉴와 겹쳐 리뷰 수정에서 지웠다 — 낮음 9)
    public static class M6StormSceneApply
    {
        private const string Tag = "[M6StormSceneApply]";

        // 지금 열린(활성) 씬에 적용한다. 저장은 하지 않는다(에디터에서 확인 후 저장).
        [MenuItem("Ashfall/Setup/M6/Apply Storm (Open Scene)")]
        public static void ApplyToOpenScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (Apply(scene) == null)
                return;
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"{Tag} {scene.name}에 적용했다. 저장은 직접(Ctrl+S).");
        }

        // 씬에 "AshStorm"을 만들거나 다시 설정한다. 만든(또는 찾은) 컨트롤러를 돌려준다. 설정 에셋이 없으면 null(에러 로그).
        public static AshStormController Apply(Scene scene)
        {
            var config = SetupUtil.Load<AshStormConfigSO>(M6Names.AshStormConfigPath);
            var atmosphere = SetupUtil.Load<AtmosphereConfigSO>(M6Names.AtmosphereConfigPath);
            if (config == null || atmosphere == null)
            {
                Debug.LogError($"{Tag} 재 폭풍 설정이 없다 — 먼저 Ashfall/Setup/Create M6 Data. 씬을 바꾸지 않았다.");
                return null;
            }

            GameObject root = SetupUtil.FindOrCreateRoot(scene, M6Names.AshStormObject);
            AshStormController controller = SetupUtil.GetOrAdd<AshStormController>(root);
            AshStormPresenter presenter = SetupUtil.GetOrAdd<AshStormPresenter>(root);

            RaidController raid = FindInScene<RaidController>(scene);
            if (raid == null)
                Debug.LogWarning($"{Tag} 씬 {scene.name}에 RaidController가 없다 — 재 폭풍이 오지 않는다.");

            ParticleSystem ash = FindNamed<ParticleSystem>(scene, M6Names.AshParticlesObject);
            if (ash == null)
                Debug.LogWarning($"{Tag} \"{M6Names.AshParticlesObject}\" 파티클이 없다(비주얼 적용 2a 전?) — 폭풍 파티클 배율 없이 연결한다.");

            Light sun = FindNamed<Light>(scene, M6Names.SunObject);
            if (sun == null)
                sun = FindDirectionalLight(scene);
            if (sun == null)
                Debug.LogWarning($"{Tag} 태양(Directional Light)이 없다 — 런타임에 RenderSettings.sun을 쓴다.");

            SetupUtil.SetFields(controller,
                ("raid", raid),
                ("config", config),
                ("atmosphere", atmosphere),
                ("presenter", presenter));
            SetupUtil.SetFields(presenter,
                ("ashParticles", ash),
                ("sun", sun),
                ("atmosphere", atmosphere),
                ("config", config));

            Debug.Log($"{Tag} {scene.name}: {M6Names.AshStormObject} — raid {(raid != null)}, 파티클 {(ash != null)}, " +
                      $"태양 {(sun != null ? sun.name : "없음")}");
            return controller;
        }

        private static T FindInScene<T>(Scene scene) where T : Component
        {
            foreach (GameObject go in scene.GetRootGameObjects())
            {
                T found = go.GetComponentInChildren<T>(true);
                if (found != null)
                    return found;
            }
            return null;
        }

        private static T FindNamed<T>(Scene scene, string name) where T : Component
        {
            foreach (GameObject go in scene.GetRootGameObjects())
                foreach (T c in go.GetComponentsInChildren<T>(true))
                    if (c.name == name)
                        return c;
            return null;
        }

        private static Light FindDirectionalLight(Scene scene)
        {
            foreach (GameObject go in scene.GetRootGameObjects())
                foreach (Light l in go.GetComponentsInChildren<Light>(true))
                    if (l.type == LightType.Directional)
                        return l;
            return null;
        }
    }
}
