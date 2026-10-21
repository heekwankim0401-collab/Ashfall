#if UNITY_EDITOR
using System.Collections;
using Ashfall.Player;
using Ashfall.Weapons;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Ashfall.Tests.PlayMode
{
    // M1 그레이박스 씬이 오류 없이 돌아가는지 확인한다 (참조 누락·Awake 예외).
    // 씬은 빌드 설정에 없으므로 에디터에서만 경로로 불러온다.
    public class GreyboxSceneTests
    {
        private const string ScenePath = "Assets/_Project/Scenes/Test/M1_Greybox.unity";
        private const int FramesToRun = 30;

        [UnityTest]
        public IEnumerator Greybox_RunsWithoutErrors_PlayerAndWeaponReady()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            for (int i = 0; i < FramesToRun; i++)
                yield return null;

            var motor = Object.FindFirstObjectByType<PlayerMotor>();
            var weapon = Object.FindFirstObjectByType<WeaponController>();
            Assert.IsNotNull(motor, "PlayerMotor 없음");
            Assert.IsNotNull(weapon, "WeaponController 없음");
            Assert.AreEqual(100f, motor.Stamina.Current, 1e-3f, "입력 없이 서 있으면 스태미나 최대");
            Assert.AreEqual(Stance.Stand, motor.Locomotion.Stance);
            Assert.AreEqual(30, weapon.Model.Current.Rounds);
            Assert.AreEqual(3, weapon.Model.Spares.Count, "시작 탄창 4개 = 장착 1 + 예비 3");
            Assert.AreEqual(4, Object.FindObjectsByType<HitTarget>(FindObjectsSortMode.None).Length);
            Assert.IsNotNull(Object.FindFirstObjectByType<Terrain>(), "테스트 지형 없음");
            Assert.AreEqual(new Vector3(200f, 20f, 200f), Terrain.activeTerrain.terrainData.size);
            // 접지: 플레이어가 지형 위에 서 있다 (떨어지거나 파묻히지 않음)
            Assert.AreEqual(0f, motor.transform.position.y, 0.2f);
        }
    }
}
#endif
