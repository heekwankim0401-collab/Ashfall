#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using Ashfall.Core;
using Ashfall.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace Ashfall.Tests.PlayMode
{
    // M6 AC2 플레이어 소리 이벤트 발행 (§11, M6 설계 §1.2·§4.4·§4.5, Q12·Q14). M2 확인 씬 + 가상 키보드·마우스.
    // 발소리 FootstepTaken은 보폭마다(걸음 간격 ≈ 보폭/속도), 사격 ShotFired(Player)는 발당 1회.
    public class PlayerSoundEventTests
    {
        // M2 바닥(60×60m) 왼쪽 빈 곳 — 상자(x −4)·표적(x 0)·터렛 구역(x 15)에서 떨어져 +z로 십수 m 걸을 수 있다
        private static readonly Vector3 OpenStart = new Vector3(-20f, 0.05f, -25f);
        private const float StrideToleranceM = 0.25f;   // 프레임 단위 누적 오차 여유

        private M2Scene scene;
        private SimInput input;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            scene = new M2Scene();
            yield return M2Scene.Load(scene);
            ((IPlayerPlacement)scene.Motor).PlaceAt(OpenStart, 0f);
            for (int i = 0; i < 5; i++)
                yield return null;
            input = new SimInput();
        }

        [TearDown]
        public void TearDown()
        {
            input?.Dispose();
            input = null;
        }

        private PlayerConfigSO Config => (PlayerConfigSO)typeof(PlayerMotor)
            .GetField("config", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .GetValue(scene.Motor);

        [UnityTest]
        public IEnumerator Walk_FootstepsEveryStride_ThenStopSilent()
        {
            using var steps = new EventLog<FootstepTaken>(scene.Events);
            Vector3 start = scene.Motor.transform.position;
            input.Keyboard.Hold(Key.W);
            yield return M2Scene.WaitSeconds(2.5f);
            input.Keyboard.Release(Key.W);
            yield return null;
            Vector3 end = scene.Motor.transform.position;
            int countAtStop = steps.Count;

            float stride = PlayerMotor.StrideLength(Config, FootstepGait.Walk);
            float distance = Horizontal(end - start);
            Debug.Log($"[PlayerSoundEventTests] 걷기 {distance:0.00}m → 걸음 {steps.Count}");
            Assert.Greater(distance, 4f, "실제로 걸었다");
            Assert.That(steps.Count, Is.InRange(Mathf.FloorToInt(distance / stride + 0.5f) - 1, Mathf.FloorToInt(distance / stride + 0.5f) + 1),
                "첫 걸음 = 보폭 절반, 이후 보폭마다");
            Assert.IsTrue(steps.Received.All(s => s.Gait == FootstepGait.Walk && s.Source == SoundSource.Player));
            for (int i = 1; i < steps.Count; i++)
                Assert.AreEqual(stride, Horizontal(steps.Received[i].Position - steps.Received[i - 1].Position), StrideToleranceM,
                    $"걸음 {i} 간격 = 보폭 (시간 간격 = 보폭/속도)");
            Assert.Less(Horizontal(steps.Received[0].Position - start), stride * 0.5f + StrideToleranceM, "첫 걸음은 보폭 절반 근처");

            // 정지 → 발소리 없음
            yield return M2Scene.WaitSeconds(1f);
            Assert.AreEqual(countAtStop, steps.Count, "멈추면 걸음 없음");
        }

        [UnityTest]
        public IEnumerator Sprint_FootstepsAreSprint_LongerStride()
        {
            using var steps = new EventLog<FootstepTaken>(scene.Events);
            input.Keyboard.Hold(Key.W, Key.LeftShift);
            yield return M2Scene.WaitSeconds(1.5f);
            input.Keyboard.Release(Key.W, Key.LeftShift);
            yield return null;

            Assert.IsTrue(steps.Count >= 3, $"질주 걸음 수 {steps.Count}");
            var sprintSteps = steps.Received.Where(s => s.Gait == FootstepGait.Sprint).ToList();
            Assert.GreaterOrEqual(sprintSteps.Count, steps.Count - 1, "질주 걸음 (첫 프레임 제외)");
            float stride = PlayerMotor.StrideLength(Config, FootstepGait.Sprint);
            for (int i = 1; i < sprintSteps.Count; i++)
                Assert.AreEqual(stride, Horizontal(sprintSteps[i].Position - sprintSteps[i - 1].Position), StrideToleranceM, "질주 보폭");
        }

        [UnityTest]
        public IEnumerator Crouch_FootstepsAreCrouch()
        {
            yield return input.Keyboard.Tap(Key.C);
            yield return M2Scene.WaitSeconds(0.5f);
            Assert.AreEqual(TargetPosture.Crouching, scene.Motor.Posture);
            using var steps = new EventLog<FootstepTaken>(scene.Events);
            input.Keyboard.Hold(Key.W);
            yield return M2Scene.WaitSeconds(2f);
            input.Keyboard.Release(Key.W);
            yield return null;
            Assert.Greater(steps.Count, 0);
            Assert.IsTrue(steps.Received.All(s => s.Gait == FootstepGait.Crouch), "웅크림 걸음");
        }

        [UnityTest]
        public IEnumerator Standing_NoFootsteps()
        {
            using var steps = new EventLog<FootstepTaken>(scene.Events);
            yield return M2Scene.WaitSeconds(1.5f);
            Assert.AreEqual(0, steps.Count);
        }

        [UnityTest]
        public IEnumerator FireOneShot_OneShotFiredPlayer()
        {
            Assert.IsTrue(scene.Weapon.IsArmed, "M2 시작 무기");
            using var shots = new EventLog<ShotFired>(scene.Events);
            using var noises = new EventLog<NoiseEmitted>(scene.Events);
            yield return input.Mouse.ClickLeft();
            yield return null;
            int gunshots = noises.Received.Count(n => n.Kind == NoiseKind.Gunshot);
            Assert.AreEqual(1, shots.Count, "한 번 클릭 = 1발");
            Assert.AreEqual(gunshots, shots.Count, "AI 사격 소음과 같은 수");
            ShotFired shot = shots.Last;
            Assert.AreEqual(SoundSource.Player, shot.Source);
            Assert.AreSame(scene.Weapon.Model.Item.Definition, shot.Weapon, "무기 정의");
            Assert.Less(Vector3.Distance(shot.Position, scene.Motor.EyePosition), 1.5f, "총구 위치");
        }

        private static float Horizontal(Vector3 v)
        {
            v.y = 0f;
            return v.magnitude;
        }
    }
}
#endif
