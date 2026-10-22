#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using Ashfall.Core;
using Ashfall.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ashfall.Tests.PlayMode
{
    // M7 7A-2a 고장·내구도 (§5.1, Q9·Q10): 코드 장면(인벤토리 없음) + 실제 키(가상 키보드·마우스).
    // 결정적으로 고장 내려고 확률/점만 0.02로 올린 handling 사본(내구도 0 → 50 × 0.02 = 100%)을 쓴다. 해제 1.2초·교체 0.5초는 기본값.
    public class JamFlowTests
    {
        private const float Dt = 1f / 60f;
        private const int Seed = 777;

        private WeaponCodeScene scene;
        private SimInput input;
        private WeaponHandlingConfigSO handling;

        private WeaponController Weapon => scene.Weapon;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            handling = ScriptableObject.CreateInstance<WeaponHandlingConfigSO>();
            handling.jamChancePerDurabilityPoint = 0.02f;   // 시험용: 내구도 0 = 고장 100%
            scene = new WeaponCodeScene();
            yield return WeaponCodeScene.Create(scene,
                M7WeaponTestAssets.Load<WeaponDefinitionSO>(M7WeaponTestAssets.RiflePath),
                M7WeaponTestAssets.Load<AmmoDefinitionSO>(M7WeaponTestAssets.Ammo556Path),
                M7WeaponTestAssets.Load<MagazineDefinitionSO>(M7WeaponTestAssets.Mag556Path), 3, handling, Seed);
            input = new SimInput();
            Time.captureDeltaTime = Dt;
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            input?.Dispose();
            input = null;
            Time.captureDeltaTime = 0f;
            scene?.Destroy();
            if (handling != null)
                Object.Destroy(handling);
        }

        [UnityTest]
        public IEnumerator JamOnZeroDurability_DryFire_ShortTNoClear_HoldClears_RoundsKept()
        {
            WeaponModel model = Weapon.Model;
            Assert.IsTrue(model.WearEnabled, "플레이어 무기는 마모·고장 옵션");
            model.Item.Durability = 0f;
            int rounds = model.Current.Rounds;
            using var clicks = new EventLog<WeaponClicked>(scene.Events);
            using var shots = new EventLog<ShotFired>(scene.Events);
            using var checks = new EventLog<MagazineCheckStarted>(scene.Events);

            // 고장 순간: 탄 소모·발사 없음, WeaponClicked(Jammed)
            yield return input.Mouse.ClickLeft();
            Assert.IsTrue(model.IsJammed, "내구도 0 → 고장");
            Assert.AreEqual(0, shots.Count, "고장 = 발사 없음");
            Assert.AreEqual(rounds, model.Current.Rounds, "고장 = 탄 소모 없음");
            Assert.AreEqual(1, clicks.Count);
            Assert.AreEqual(WeaponClickKind.Jammed, clicks.Last.Kind);
            Assert.Less(Vector3.Distance(clicks.Last.Position, Weapon.MuzzlePosition), 1e-3f, "총구 위치");

            // 고장 중 방아쇠 = 헛격발 (새로 당길 때만)
            yield return input.Mouse.ClickLeft();
            Assert.AreEqual(2, clicks.Count);
            Assert.AreEqual(WeaponClickKind.DryFire, clicks.Last.Kind);
            input.Mouse.SetLeft(true);
            for (int i = 0; i < 10; i++)
                yield return null;
            input.Mouse.SetLeft(false);
            yield return null;
            Assert.AreEqual(3, clicks.Count, "누르고 있는 동안은 한 번");
            Assert.AreEqual(0, shots.Count);

            // 고장 중 재장전 불가
            model.Current.TakeRound();
            rounds = model.Current.Rounds;
            yield return input.Keyboard.Tap(Key.R);
            Assert.IsFalse(model.IsReloading, "고장 중 R 거부");

            // T 짧게 = 해제 안 됨, 고장 중엔 탄창 확인도 없음
            yield return input.Keyboard.Tap(Key.T);
            Assert.IsTrue(model.IsJammed, "T 짧게는 해제 아님");
            Assert.AreEqual(0, checks.Count, "고장 중 탄창 확인 불가");
            Assert.AreEqual(0f, Weapon.JamClearProgress, "떼면 처음부터");

            // 1.0초 누르고 떼면 처음부터
            input.Keyboard.Hold(Key.T);
            yield return M2Scene.WaitSeconds(1.0f);
            Assert.IsTrue(model.IsJammed);
            Assert.Greater(Weapon.JamClearProgress, 0.7f);
            input.Keyboard.Release(Key.T);
            yield return null;
            yield return null;
            Assert.AreEqual(0f, Weapon.JamClearProgress);
            Assert.IsTrue(model.IsJammed);

            // 1.2초 계속 누름 = 해제 + WeaponClicked(JamCleared)
            int startFrame = Time.frameCount;
            input.Keyboard.Hold(Key.T);
            yield return M2Scene.WaitUntil(() => !model.IsJammed, 4f);
            float heldSec = (Time.frameCount - startFrame) * Dt;
            input.Keyboard.Release(Key.T);
            yield return null;
            Assert.AreEqual(handling.jamClearHoldSec, heldSec, 3f * Dt, "해제 1.2초 (TODO(GDD-?) Q9)");
            Assert.AreEqual(WeaponClickKind.JamCleared, clicks.Last.Kind);
            Assert.AreEqual(1, clicks.Received.Count(c => c.Kind == WeaponClickKind.JamCleared));
            Assert.AreEqual(rounds, model.Current.Rounds, "해제 = 탄 수 보존");
            Assert.AreEqual(0, checks.Count, "해제 T는 탄창 확인이 아니다");
            Assert.AreEqual(0, shots.Count);

            // 해제 뒤: 내구도를 올리면 정상 사격, T 짧게 = RULE-04 탄창 확인
            model.Item.Durability = 100f;
            yield return input.Mouse.ClickLeft();
            Assert.AreEqual(1, shots.Count, "해제 뒤 사격");
            Assert.AreEqual(rounds - 1, model.Current.Rounds);
            Assert.IsFalse(model.IsJammed);
            yield return input.Keyboard.Tap(Key.T);
            Assert.AreEqual(1, checks.Count, "고장 아님 → T 짧게 = 탄창 확인");
            Assert.IsTrue(Weapon.MagazineCheck.IsChecking);
        }

        // 빈 탄창에서 방아쇠 = 헛격발 (고장 아님)
        [UnityTest]
        public IEnumerator EmptyMagazine_Trigger_DryFire()
        {
            WeaponModel model = Weapon.Model;
            while (model.Current.TakeRound()) { }
            using var clicks = new EventLog<WeaponClicked>(scene.Events);
            using var shots = new EventLog<ShotFired>(scene.Events);
            yield return input.Mouse.ClickLeft();
            Assert.AreEqual(1, clicks.Count);
            Assert.AreEqual(WeaponClickKind.DryFire, clicks.Last.Kind);
            Assert.AreEqual(0, shots.Count);
            Assert.IsFalse(model.IsJammed, "빈 탄창은 고장을 굴리지 않는다");
        }

        // 내구도 ≥ 50: 고장 없음, 발당 마모 = durabilityLossPerShot
        [UnityTest]
        public IEnumerator HighDurability_NoJam_WearPerShot()
        {
            WeaponModel model = Weapon.Model;
            float loss = model.Item.Definition.durabilityLossPerShot;
            Assert.Greater(loss, 0f, "WPN-RIF-01 durabilityLossPerShot (M7 데이터)");
            model.Item.Durability = 50f;
            using var clicks = new EventLog<WeaponClicked>(scene.Events);
            using var shots = new EventLog<ShotFired>(scene.Events);
            yield return input.Mouse.ClickLeft();
            yield return M2Scene.WaitSeconds(0.2f);
            Assert.AreEqual(1, shots.Count);
            Assert.AreEqual(0, clicks.Count);
            Assert.AreEqual(50f - loss, model.Item.Durability, 1e-4f, "발당 마모");
        }
    }
}
#endif
