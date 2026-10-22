using Ashfall.Audio;
using Ashfall.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ashfall.EditorTools
{
    // M6 오디오 씬 적용 (M6 설계 §1.6·§4.7 "2b"). 열린 씬을 멱등 수정한다(이름으로 찾아 "있으면 다시 설정, 없으면 생성").
    // - ApplyRaid: 레이드 씬 최상위 "AudioDirector"(AudioDirector, config). 음악 소스는 두지 않는다(§11 레이드 BGM 없음).
    // - ApplyHideout: 은신처 씬 최상위 "HideoutMusic"(HideoutMusic + AudioSource, config) — Q18 자리표시 BGM.
    //   AudioSource를 씬에 같이 둬서 은신처에 들어올 때마다 런타임 AddComponent가 없게 한다(리뷰 낮음 11).
    // 저장은 호출자(M6SceneSetup.ApplyAll)가 한다. 메뉴 항목도 다른 "(Open Scene)" 메뉴처럼 저장하지 않는다(리뷰 낮음 6).
    // 실패(설정 에셋 없음·config 필드 없음)는 false — 호출자가 실패로 처리한다.
    // (2b 단독 확인용 "Apply Audio (Raid + Hideout)"는 통합 메뉴와 겹쳐 리뷰 수정에서 지웠다 — 낮음 9)
    public static class M6AudioSceneApply
    {
        private const string Tag = "[M6AudioSceneApply]";

        // 레이드 씬. 설정이 없으면 false(에러 로그).
        public static bool ApplyRaid(Scene scene)
        {
            var config = SetupUtil.Load<AudioConfigSO>(M6Names.AudioConfigPath);
            if (config == null)
                return false;
            GameObject go = FindOrCreateRoot(scene, M6Names.AudioDirectorObject);
            var director = SetupUtil.GetOrAdd<AudioDirector>(go);
            return SetConfig(director, config);
        }

        // 은신처 씬. 설정이 없으면 false(에러 로그).
        public static bool ApplyHideout(Scene scene)
        {
            var config = SetupUtil.Load<AudioConfigSO>(M6Names.AudioConfigPath);
            if (config == null)
                return false;
            GameObject go = FindOrCreateRoot(scene, M6Names.HideoutMusicObject);
            var music = SetupUtil.GetOrAdd<HideoutMusic>(go);
            ConfigureMusicSource(SetupUtil.GetOrAdd<AudioSource>(go));
            return SetConfig(music, config);
        }

        [MenuItem("Ashfall/Setup/M6/Apply Audio (Open Scene)")]
        public static void ApplyOpenScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            bool applied;
            if (scene.name == SceneNames.RaidCrimsonBasin)
                applied = ApplyRaid(scene);
            else if (scene.name == SceneNames.Hideout)
                applied = ApplyHideout(scene);
            else
            {
                Debug.LogWarning($"{Tag} 레이드·은신처 씬이 아니다: {scene.name} — 아무것도 하지 않음");
                return;
            }
            if (!applied)
            {
                Debug.LogError($"{Tag} 적용 실패: {scene.name} (위 오류 로그 확인)");
                return;
            }
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"{Tag} {scene.name}에 적용했다. 저장은 직접(Ctrl+S).");
        }

        // HideoutMusic.Awake가 런타임에 같은 값을 다시 넣는다(ignoreListenerPause는 직렬화되지 않는 속성이라 거기서만).
        // 씬 파일에는 재생 전 상태만 둔다: 자동 재생 끔(클립은 런타임에 config에서), 루프, 2D. 값이 다를 때만 기록.
        private static void ConfigureMusicSource(AudioSource source)
        {
            bool dirty = false;
            if (source.playOnAwake) { source.playOnAwake = false; dirty = true; }
            if (!source.loop) { source.loop = true; dirty = true; }
            if (source.spatialBlend != 0f) { source.spatialBlend = 0f; dirty = true; }
            if (source.dopplerLevel != 0f) { source.dopplerLevel = 0f; dirty = true; }
            if (dirty)
                EditorUtility.SetDirty(source);
        }

        private static GameObject FindOrCreateRoot(Scene scene, string name)
        {
            GameObject go = SetupUtil.FindRoot(scene, name);
            if (go != null)
                return go;
            Debug.Log($"{Tag} 생성: {name} ({scene.name})");
            return SetupUtil.FindOrCreateRoot(scene, name);
        }

        // 직렬화 필드 config가 다를 때만 기록 (두 번째 실행에서 씬이 바뀌지 않게). 필드가 없으면 false.
        private static bool SetConfig(Object target, AudioConfigSO config)
        {
            var so = new SerializedObject(target);
            SerializedProperty property = so.FindProperty("config");
            if (property == null)
            {
                Debug.LogError($"{Tag} {target.GetType().Name}에 config 필드가 없다", target);
                return false;
            }
            if (property.objectReferenceValue == config)
                return true;
            property.objectReferenceValue = config;
            so.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }
    }
}
