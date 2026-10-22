#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using Ashfall.Audio;
using Ashfall.Core;
using Ashfall.Player;
using Ashfall.Raid;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ashfall.Tests.PlayMode
{
    // M6 3단계 통합 스모크 (M6 설계 §1.6·§6 "3단계", ART·AUD·SYS-MAP 재 폭풍):
    // Ashfall/Setup/Apply M6 Art & Audio(M6SceneSetup.ApplyAll)가 적용한 Raid_CrimsonBasin을 이름으로 불러
    // AudioDirector·AshStorm·Global Volume·AshParticles·Sun이 하나씩 있고 서로 연결됐는지, IAshStormInfo가 등록되는지 본다.
    // 콘솔 오류가 나면 테스트 프레임워크가 실패시킨다. 이름은 M6Names 계약 값(테스트 asmdef는 EditorTools를 참조하지 않는다).
    public class M6RaidSceneSmokeTests
    {
        private const string AudioConfigPath = "Assets/_Project/Data/Audio/AudioConfig.asset";
        private const string AshStormConfigPath = "Assets/_Project/Data/Raid/AshStormConfig.asset";
        private const string AtmosphereConfigPath = "Assets/_Project/Data/Raid/AtmosphereConfig.asset";
        private const string VolumeProfilePath = "Assets/_Project/Art/PostProcess/VP_RaidAtmosphere.asset";
        private const string SunObject = "Sun";
        private const string GlobalVolumeObject = "M6_GlobalVolume";
        private const string AshParticlesObject = "AshParticles";
        private const string AshStormObject = "AshStorm";
        private const string AudioDirectorObject = "AudioDirector";
        // AiDirector가 서비스를 다시 조회하는 주기(targetResolveRetrySec 1초) + 여유
        private const float StormResolveWaitSec = 2f;
        private const float RunSeconds = 2f;

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
        [Timeout(120000)]
        public IEnumerator RaidScene_HasM6Objects_Wired_StormServiceRegistered()
        {
            var raid = new M4Raid();
            yield return M4Raid.Load(raid);
            GameManager gm = raid.Gm;

            // ── 하나씩 ──
            AudioDirector audio = Single<AudioDirector>();
            AshStormController storm = Single<AshStormController>();
            AshStormPresenter presenter = Single<AshStormPresenter>();
            Volume volume = Single<Volume>();
            ParticleSystem[] ashes = Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)
                .Where(p => p.name == AshParticlesObject).ToArray();
            Assert.AreEqual(1, ashes.Length, $"\"{AshParticlesObject}\" 파티클 수");
            ParticleSystem ash = ashes[0];
            Light[] directionals = Object.FindObjectsByType<Light>(FindObjectsSortMode.None)
                .Where(l => l.type == LightType.Directional).ToArray();
            Assert.AreEqual(1, directionals.Length, "Directional Light 수");
            Light sun = directionals[0];

            // ── 이름·위치 (M6Names 계약) ──
            Assert.AreSame(Root(AudioDirectorObject), audio.gameObject, "AudioDirector 최상위");
            Assert.AreSame(Root(AshStormObject), storm.gameObject, "AshStorm 최상위");
            Assert.AreSame(storm.gameObject, presenter.gameObject, "컨트롤러·프레젠터 한 오브젝트");
            Assert.AreSame(Root(GlobalVolumeObject), volume.gameObject, "Global Volume 최상위");
            Assert.AreSame(Root(SunObject), sun.gameObject, "Sun 최상위");
            Assert.AreSame(raid.Motor.transform, ash.transform.parent, "AshParticles는 Player 자식");
            Assert.AreEqual(0, Object.FindObjectsByType<HideoutMusic>(FindObjectsSortMode.None).Length, "레이드에 음악 없음 (§11)");

            // ── 참조 연결 ──
            Assert.AreSame(AssetDatabase.LoadAssetAtPath<AudioConfigSO>(AudioConfigPath), audio.Config, "AudioDirector.config");
            Assert.IsTrue(audio.enabled, "AudioDirector 꺼짐");
            Assert.IsTrue(audio.IsInitialized, "AudioDirector 초기화 안 됨");
            Assert.AreSame(AssetDatabase.LoadAssetAtPath<AshStormConfigSO>(AshStormConfigPath), storm.Config, "AshStormController.config");
            Assert.AreSame(raid.Controller, storm.Raid, "AshStormController.raid");
            Assert.AreSame(presenter, storm.Presenter, "AshStormController.presenter");
            Assert.IsTrue(storm.enabled, "AshStormController 꺼짐");
            Assert.AreSame(ash, presenter.AshParticles, "presenter.ashParticles = AshParticles");
            Assert.AreSame(sun, presenter.Sun, "presenter.sun = Sun");
            Assert.AreSame(sun, RenderSettings.sun, "RenderSettings.sun = Sun");
            Assert.IsTrue(volume.isGlobal, "Global이 아니다");
            Assert.AreSame(AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath), volume.sharedProfile, "볼륨 프로필");
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<AtmosphereConfigSO>(AtmosphereConfigPath), "AtmosphereConfig 없음");

            // ── 서비스: IAshStormInfo = 씬의 AshStorm, AI가 1초 안에 잡는다 ──
            Assert.IsTrue(gm.Services.TryGet(out IAshStormInfo info), "IAshStormInfo 미등록");
            Assert.AreSame(storm, info);
            yield return null; // 세션이 생긴 프레임 다음에는 컨트롤러 Update가 시작 시각을 정했다
            Assert.AreEqual(AshStormPhase.Pending, info.Phase, "레이드 시작 직후 폭풍 대기");
            Assert.IsFalse(float.IsNaN(storm.StartSec), "세션이 생긴 뒤 시작 시각이 정해져야 한다");
            float until = Time.realtimeSinceStartup + StormResolveWaitSec;
            while (raid.Director.Storm == null && Time.realtimeSinceStartup < until)
                yield return null;
            Assert.AreSame(storm, raid.Director.Storm, "AiDirector가 IAshStormInfo를 잡지 못했다");

            // ── 몇 초 돌려도 오류 없음(콘솔 오류는 프레임워크가 실패 처리) ──
            float end = Time.realtimeSinceStartup + RunSeconds;
            while (Time.realtimeSinceStartup < end)
                yield return null;
            Assert.AreEqual(SceneNames.RaidCrimsonBasin, SceneManager.GetActiveScene().name);
        }

        private static T Single<T>() where T : Object
        {
            T[] found = Object.FindObjectsByType<T>(FindObjectsSortMode.None);
            Assert.AreEqual(1, found.Length, $"{typeof(T).Name} 수");
            return found[0];
        }

        private static GameObject Root(string name)
        {
            GameObject[] found = SceneManager.GetActiveScene().GetRootGameObjects().Where(g => g.name == name).ToArray();
            Assert.AreEqual(1, found.Length, $"최상위 \"{name}\" 수");
            return found[0];
        }
    }
}
#endif
