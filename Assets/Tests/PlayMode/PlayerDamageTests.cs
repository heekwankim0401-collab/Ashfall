#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using System.Reflection;
using Ashfall.Core;
using Ashfall.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace Ashfall.Tests.PlayMode
{
    // M2 AC1 — §4.2 부위 체력, §4.3 출혈·골절 (SYS-HP-01, STAT-BLEED/HBLEED/FRAC). M2 씬 통합.
    // 피격은 실제 ProjectileSystem 탄이 플레이어 CharacterController 캡슐을 맞히는 경로로 확인한다.
    public class PlayerDamageTests
    {
        // 상태이상 굴림 최소 피해(10)보다 작게 해서 결과를 결정적으로 만든다.
        private const float LowDamage = 5f;
        private const float ShotDistanceM = 4f;

        private M2Scene scene;
        private SimKeyboard keyboard;
        private AmmoDefinitionSO ammo;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            scene = new M2Scene();
            yield return M2Scene.Load(scene);
        }

        [TearDown]
        public void TearDown()
        {
            keyboard?.Dispose();
            Time.captureDeltaTime = 0f;
            keyboard = null;
            if (ammo != null)
                Object.Destroy(ammo);
        }

        private AmmoDefinitionSO Ammo(float damage)
        {
            if (ammo != null)
                Object.Destroy(ammo);
            ammo = ScriptableObject.CreateInstance<AmmoDefinitionSO>();
            ammo.damage = damage; ammo.velocity = 900f; ammo.maxFlightTimeSec = 1f; ammo.penetration = 3; ammo.pelletCount = 1;
            return ammo;
        }

        // 플레이어 정면에서 수평 사격. heightRatio·localX는 컨트롤러 크기(height·radius) 기준 좌표.
        // 물리 쿼리가 맞히는 캡슐은 컨트롤러보다 작으므로(PhysX CCT kinematic actor) 실제 캡슐 꼭대기를 재서 같은 비율로 줄여 겨눈다.
        private IEnumerator ShootPlayer(float heightRatio, float localX, float damage, Transform shooterRoot = null)
        {
            Transform player = scene.Motor.transform;
            var controller = player.GetComponent<CharacterController>();
            Vector3 center = player.TransformPoint(controller.center);
            Assert.IsTrue(Physics.Raycast(center + Vector3.up * 3f, Vector3.down, out RaycastHit top, 3f), "캡슐 꼭대기 측정 실패");
            float physicsScale = (top.point.y - center.y) / (controller.height * 0.5f);
            Assert.That(physicsScale, Is.InRange(0.5f, 1.01f), "물리 캡슐 배율");

            Vector3 target = center
                             + Vector3.up * ((heightRatio - 0.5f) * controller.height * physicsScale)
                             + player.right * (localX * physicsScale);
            Vector3 origin = target + player.forward * ShotDistanceM;
            scene.Projectiles.Fire(origin, -player.forward, Ammo(damage), shooterRoot);
            yield return M2Scene.WaitUntil(() => scene.Projectiles.LiveCount == 0, 3f);
        }

        private float[] Snapshot() =>
            System.Enum.GetValues(typeof(BodyPart)).Cast<BodyPart>().Select(p => scene.Body.GetHp(p)).ToArray();

        private void AssertOnlyPartDamaged(float[] before, BodyPart part, float damage)
        {
            float[] after = Snapshot();
            foreach (BodyPart p in System.Enum.GetValues(typeof(BodyPart)))
            {
                float expected = p == part ? before[(int)p] - damage : before[(int)p];
                Assert.AreEqual(expected, after[(int)p], 1e-3f, $"{part} 피격 후 {p} HP");
            }
        }

        [UnityTest]
        public IEnumerator Projectile_HitsCapsule_DamagesPartByHeightAndSide()
        {
            // (높이 비율, 좌우 m, 기대 부위). 구간 경계: 머리 0.87 / 흉부 0.62 / 복부 0.45 / 아래 다리, 팔 = 반지름×0.6 바깥
            var cases = new (float ratio, float x, BodyPart part)[]
            {
                (0.93f, 0f, BodyPart.Head),
                (0.75f, 0f, BodyPart.Thorax),
                (0.53f, 0f, BodyPart.Stomach),
                (0.68f, -0.25f, BodyPart.LeftArm),
                (0.68f, 0.25f, BodyPart.RightArm),
                (0.25f, -0.1f, BodyPart.LeftLeg),
                (0.25f, 0.1f, BodyPart.RightLeg),
            };
            foreach (var c in cases)
            {
                float[] before = Snapshot();
                yield return ShootPlayer(c.ratio, c.x, LowDamage);
                AssertOnlyPartDamaged(before, c.part, LowDamage);
            }
            Assert.IsFalse(scene.Body.IsDead);
            for (int p = 0; p < 7; p++)
                Assert.IsFalse(scene.Body.HasStatus((BodyPart)p, StatusEffect.Bleed), "피해 < 최소값이면 상태이상 굴림 없음");
        }

        [UnityTest]
        public IEnumerator Projectile_FromPlayerItself_DoesNotHit()
        {
            float[] before = Snapshot();
            yield return ShootPlayer(0.75f, 0f, LowDamage, scene.Motor.transform.root);
            AssertOnlyPartDamaged(before, BodyPart.Thorax, 0f);
        }

        [UnityTest]
        public IEnumerator HeadShot_KillsPlayer_PublishesDiedAndBlocksControl()
        {
            using var died = new EventLog<PlayerDied>(scene.Events);
            yield return ShootPlayer(0.93f, 0f, 45f);   // WPN-RIF-01 탄 피해 45 > 머리 35
            Assert.IsTrue(scene.Body.IsDead);
            Assert.AreEqual(BodyPart.Head, scene.Body.DeathCause);
            Assert.AreEqual(1, died.Count);
            Assert.AreEqual(BodyPart.Head, died.Last.Cause);
            Assert.IsTrue(scene.Motor.InputBlocked, "사망 후 이동 입력 차단");
            Assert.IsTrue(scene.Inventory.IsDeadLocked, "사망 후 인벤토리 잠금");
            Assert.IsTrue(scene.Gm.Services.TryGet(out IBodyStatusView view) && view.IsDead);
        }

        // 출혈 -1/s, 중출혈 -3/s → 퀵슬롯 키 4(붕대)로 출혈 해제, 서비스 TryUse(지혈대)로 중출혈 해제, 부목으로 골절 해제
        [UnityTest]
        public IEnumerator Bleeding_DamagesOverTime_AndMedicalItemsCure()
        {
            var body = scene.Body;
            IInventoryService inventory = scene.Inventory;
            var bandage = (MedicalItem)inventory.GetQuickSlot(0);
            var tourniquet = (MedicalItem)inventory.GetQuickSlot(1);
            var splint = (MedicalItem)inventory.GetQuickSlot(2);
            Assert.AreEqual("MED-BND-01", bandage.Definition.id);
            Assert.AreEqual("MED-TRQ-01", tourniquet.Definition.id);
            Assert.AreEqual("MED-SPL-01", splint.Definition.id);

            using var statusChanged = new EventLog<PlayerBodyStatusChanged>(scene.Events);
            using var started = new EventLog<MedicalUseStarted>(scene.Events);
            using var completed = new EventLog<MedicalUseCompleted>(scene.Events);
            using var rejected = new EventLog<MedicalUseRejected>(scene.Events);

            // 필요 없는 의약품은 거부
            Assert.IsTrue(inventory.TryUse(splint));
            Assert.AreEqual(1, rejected.Count);
            Assert.AreEqual(MedicalUseFailReason.NotNeeded, rejected.Last.Reason);

            body.AddStatus(BodyPart.LeftLeg, StatusEffect.Bleed);
            body.AddStatus(BodyPart.Stomach, StatusEffect.HeavyBleed);
            Assert.GreaterOrEqual(statusChanged.Count, 2, "상태이상 추가 → PlayerBodyStatusChanged");
            int statusEventsAfterAdd = statusChanged.Count;

            // 1초 동안 지속 피해 (PlayerHealth.Update가 Time.deltaTime으로 Tick)
            float legBefore = body.GetHp(BodyPart.LeftLeg), stomachBefore = body.GetHp(BodyPart.Stomach);
            float elapsed = 0f;
            while (elapsed < 1f)
            {
                yield return null;
                elapsed += Time.deltaTime;
            }
            Assert.AreEqual(legBefore - 1f * elapsed, body.GetHp(BodyPart.LeftLeg), 0.02f, "출혈 -1 HP/s");
            Assert.AreEqual(stomachBefore - 3f * elapsed, body.GetHp(BodyPart.Stomach), 0.05f, "중출혈 -3 HP/s");
            Assert.AreEqual(statusEventsAfterAdd, statusChanged.Count, "등급이 안 바뀌면 출혈 틱마다 발행하지 않는다");

            // 키 4 = 퀵슬롯 0(붕대) → MedicalUseRequested → PlayerHealth 사용 시작
            keyboard = new SimKeyboard();
            yield return keyboard.Tap(Key.Digit4);
            yield return M2Scene.WaitUntil(() => started.Count == 1, 2f);
            Assert.AreSame(bandage, started.Last.Item);
            Assert.AreEqual(BodyPart.LeftLeg, started.Last.Target);
            Assert.IsTrue(scene.Inventory.IsLocked(bandage), "사용 중 의약품 잠금");
            Assert.IsTrue(body.HasStatus(BodyPart.LeftLeg, StatusEffect.Bleed), "사용 시간 동안은 효과 없음");

            float useElapsed = 0f;
            yield return M2Scene.WaitUntil(() => completed.Count == 1, 6f, e => useElapsed = e);
            Assert.AreEqual(bandage.Definition.useTime, useElapsed, 0.25f, "붕대 사용 시간");
            Assert.IsFalse(body.HasStatus(BodyPart.LeftLeg, StatusEffect.Bleed), "붕대 → 출혈 해제");
            Assert.IsTrue(body.HasStatus(BodyPart.Stomach, StatusEffect.HeavyBleed), "붕대는 중출혈을 풀지 않는다");
            yield return null;
            Assert.IsFalse(scene.Inventory.IsInStorage(bandage), "사용 횟수 1 → 제거");
            Assert.IsNull(inventory.GetQuickSlot(0), "퀵슬롯 비움");

            // 서비스 TryUse(지혈대) → 중출혈 해제
            Assert.IsTrue(inventory.TryUse(tourniquet));
            yield return M2Scene.WaitUntil(() => completed.Count == 2, 7f);
            Assert.IsFalse(body.HasStatus(BodyPart.Stomach, StatusEffect.HeavyBleed), "지혈대 → 중출혈 해제");
            float stomachAfter = body.GetHp(BodyPart.Stomach);
            yield return M2Scene.WaitSeconds(0.3f);
            Assert.AreEqual(stomachAfter, body.GetHp(BodyPart.Stomach), 1e-4f, "해제 후 지속 피해 없음");

            // 골절 → 이동 패널티, 부목 → 해제
            body.AddStatus(BodyPart.RightLeg, StatusEffect.Fracture);
            Assert.Less(scene.Motor.Condition.MoveSpeedMultiplier, 1f, "골절 다리 → 이동 패널티");
            Assert.IsTrue(inventory.TryUse(splint));
            yield return M2Scene.WaitUntil(() => completed.Count == 3, 8f);
            Assert.IsFalse(body.HasStatus(BodyPart.RightLeg, StatusEffect.Fracture), "부목 → 골절 해제");
            Assert.AreEqual(1f, scene.Motor.Condition.MoveSpeedMultiplier, 1e-5f);
            Assert.IsFalse(body.IsDead);
        }

        // 다리 파괴 → 질주 불가·이동 -40%: 가상 키보드로 W+Shift를 눌러 실제 이동 속도를 잰다.
        [UnityTest]
        public IEnumerator LegDestroyed_BlocksSprint_AndSlowsActualMovement()
        {
            keyboard = new SimKeyboard();
            // 배치모드는 프레임이 매우 짧아(≈1ms) 한 프레임 이동량이 CharacterController.minMoveDistance(1mm)보다 작아 무시된다.
            // 실제 게임 프레임처럼 dt를 1/60초로 고정해 잰다.
            Time.captureDeltaTime = 1f / 60f;
            Transform player = scene.Motor.transform;
            float kg = scene.Motor.CarriedWeightKg;
            float sprintSpeed = scene.Motor.Locomotion.Speed(true, kg, ConditionModifiers.Healthy);

            float healthy = 0f;
            yield return MeasureSpeed(player, v => healthy = v);
            Assert.AreEqual(sprintSpeed, healthy, sprintSpeed * 0.05f, "정상: 질주 속도");

            using var condition = new EventLog<PlayerConditionChanged>(scene.Events);
            scene.Body.ApplyDamage(BodyPart.LeftLeg, 65f);   // 다리 HP 65
            Assert.IsTrue(scene.Body.IsDestroyed(BodyPart.LeftLeg));
            Assert.AreEqual(1, condition.Count);
            Assert.IsFalse(condition.Last.Modifiers.SprintAllowed);
            Assert.AreEqual(0.6f, condition.Last.Modifiers.MoveSpeedMultiplier, 1e-5f);

            float walkSpeed = scene.Motor.Locomotion.Speed(false, kg, ConditionModifiers.Healthy);
            float destroyed = 0f;
            yield return MeasureSpeed(player, v => destroyed = v);
            Assert.IsFalse(scene.Motor.IsSprinting, "다리 파괴 → 질주 불가");
            Assert.AreEqual(walkSpeed * 0.6f, destroyed, walkSpeed * 0.6f * 0.05f, "다리 파괴 → 걷기 × 0.6");
        }

        private IEnumerator MeasureSpeed(Transform player, System.Action<float> result)
        {
            keyboard.Hold(Key.W, Key.LeftShift);
            for (int i = 0; i < 3; i++)
                yield return null;   // 입력이 들어오고 질주가 잡힐 때까지
            Vector3 start = player.position;
            float elapsed = 0f;
            while (elapsed < 0.5f)
            {
                yield return null;
                elapsed += Time.deltaTime;
            }
            Vector3 d = player.position - start;
            d.y = 0f;
            keyboard.Release(Key.W, Key.LeftShift);
            for (int i = 0; i < 3; i++)
                yield return null;
            result(d.magnitude / elapsed);
        }

        // 팔 파괴 → PlayerConditionChanged → WeaponController 조준 흔들림 ×2가 무기 회전에 실제로 반영된다.
        [UnityTest]
        public IEnumerator ArmDestroyed_DoublesAimSway_OnWeapon()
        {
            using var condition = new EventLog<PlayerConditionChanged>(scene.Events);
            scene.Body.ApplyDamage(BodyPart.RightArm, 60f);   // 팔 HP 60
            Assert.AreEqual(1, condition.Count, "팔 파괴 → PlayerConditionChanged");
            Assert.AreEqual(2f, condition.Last.Modifiers.AimSwayMultiplier, 1e-5f, "§4.2 조준 흔들림 +100%");

            yield return null;   // WeaponController.Update 이후 (LateUpdate 전)
            var handling = (WeaponHandlingConfigSO)Field(scene.Weapon, "handling");
            int seed = (int)Field(scene.Weapon, "swaySeed");
            var baseRotation = (Quaternion)Field(scene.Weapon, "baseLocalRotation");
            Assert.AreEqual(2f, (float)Field(scene.Weapon, "swayMultiplier"), 1e-5f, "이벤트가 Weapons에 도달");

            Vector2 expected = AimSway.Offset(Time.time, handling.aimSwayBaseDeg, 2f, handling.aimSwayFrequencyHz, seed);
            Quaternion expectedRotation = baseRotation * Quaternion.Euler(-expected.y, expected.x, 0f);
            Assert.Less(Quaternion.Angle(expectedRotation, scene.Weapon.transform.localRotation), 1e-3f,
                "무기 루트 회전 = 기본 흔들림 × 2");

            scene.Body.ApplyDamage(BodyPart.LeftArm, 60f);
            Assert.AreEqual(3f, condition.Last.Modifiers.AimSwayMultiplier, 1e-5f, "팔 2개 파괴 → ×3");
        }

        private static object Field(object target, string name)
        {
            FieldInfo f = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(f, $"{target.GetType().Name}.{name} 필드 없음 (테스트 갱신 필요)");
            return f.GetValue(target);
        }
    }
}
#endif
