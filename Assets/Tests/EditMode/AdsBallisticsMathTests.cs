using Ashfall.Weapons;
using NUnit.Framework;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // 정조준·탄도 순수 계산 (AB-Q1~Q3, docs/design/AdsBallistics_design.md): 드래그 적분, 영점 해법 수렴, 정조준 자세.
    public class AdsBallisticsMathTests
    {
        private const float Dt = 0.02f;
        private static readonly Vector3 G = new Vector3(0f, -9.81f, 0f);

        [Test]
        public void Step_ZeroDrag_MatchesPreviousSemiImplicitEuler()
        {
            // 기대값은 예전 식(v += g·dt; p += v·dt)을 여기서 직접 계산한다 — 저항 0이면 비트 단위로 같아야 한다.
            Vector3 p = Vector3.zero, v = new Vector3(3f, 40f, 900f);
            Vector3 pOld = p, vOld = v, pNew = p, vNew = v;
            for (int i = 0; i < 100; i++)
            {
                vOld += G * Dt;
                pOld += vOld * Dt;
                Ballistics.Step(ref pNew, ref vNew, G, 0f, Dt);
            }
            Assert.AreEqual(pOld.x, pNew.x);
            Assert.AreEqual(pOld.y, pNew.y);
            Assert.AreEqual(pOld.z, pNew.z);
            Assert.AreEqual(vOld, vNew);
            // 저항 없는 오버로드도 같은 식
            Vector3 p3 = Vector3.zero, v3 = v;
            for (int i = 0; i < 100; i++)
                Ballistics.Step(ref p3, ref v3, G, Dt);
            Assert.AreEqual(pOld.y, p3.y);
        }

        [Test]
        public void Step_Drag_SpeedDecaysAsExponentialOfDistance()
        {
            // 중력 없이 이차 저항만: v(s) = v0·e^(-k·s). 작은 dt로 적분해 해석해와 비교.
            const float k = 0.0011f, v0 = 900f, dt = 0.0005f;
            Vector3 p = Vector3.zero, v = new Vector3(0f, 0f, v0);
            while (p.z < 200f)
                Ballistics.Step(ref p, ref v, Vector3.zero, k, dt);
            float expected = v0 * Mathf.Exp(-k * p.z);
            Assert.AreEqual(expected, v.z, expected * 0.002f);
            Assert.Less(v.z, v0 * 0.81f, "200m에서 약 20% 감속");
        }

        [Test]
        public void Step_Drag_OnlyAlongVelocity_KeepsHorizontalHeading()
        {
            Vector3 p = Vector3.zero, v = new Vector3(30f, 10f, 400f);
            for (int i = 0; i < 40; i++)
                Ballistics.Step(ref p, ref v, G, 0.004f, Dt);
            Assert.AreEqual(30f / 400f, v.x / v.z, 1e-5f);
        }

        [TestCase(25f, 360f, 0.002f, 1.5f)]
        [TestCase(100f, 900f, 0.0011f, 3f)]
        [TestCase(200f, 820f, 0.0008f, 3f)]
        [TestCase(25f, 400f, 0.004f, 0.8f)]
        public void Zero_Converges_AndHitsZeroPoint(float zero, float speed, float drag, float maxTime)
        {
            var muzzle = new Vector3(0.01f, -0.08f, 0.4f);
            Assert.IsTrue(BallisticZero.TrySolve(muzzle, zero, speed, 9.81f, drag, Dt, maxTime, out Vector3 dir, out int iterations));
            Assert.LessOrEqual(iterations, 8, "할선법이 몇 번 안에 수렴");
            // 같은 적분기로 쏴서 z = zero 평면 교점이 시선 위(0,0)
            Vector3 p = muzzle, v = dir * speed, from = p;
            while (p.z < zero)
            {
                from = p;
                Ballistics.Step(ref p, ref v, G, drag, Dt);
            }
            float t = (zero - from.z) / (p.z - from.z);
            Vector3 hit = Vector3.Lerp(from, p, t);
            Assert.AreEqual(0f, hit.y, 1e-4f);
            Assert.AreEqual(0f, hit.x, 1e-4f);
            Assert.Greater(dir.y, 0f, "총구가 시선 아래이므로 위로 쏜다");
        }

        [Test]
        public void Zero_Unreachable_ReturnsFalse()
        {
            Assert.IsFalse(BallisticZero.TrySolve(new Vector3(0f, -0.05f, 0.3f), 500f, 400f, 9.81f, 0.004f, Dt, 0.8f, out _, out _));
        }

        [Test]
        public void AdsPose_PutsAnchorOnEyeAxis_WithAnchorForwardAlongView()
        {
            var anchor = new Vector3(0.002f, 0.11f, -0.2f);
            Quaternion anchorRot = Quaternion.Euler(-0.7f, 0f, 0f);
            AdsAlignment.Pose pose = AdsAlignment.ViewRootPose(Vector3.zero, Quaternion.identity, anchor, anchorRot, 0.12f);
            Vector3 eyeAnchor = AdsAlignment.ToEye(Vector3.zero, Quaternion.identity, pose, anchor);
            Assert.AreEqual(0f, Vector3.Distance(eyeAnchor, new Vector3(0f, 0f, 0.12f)), 1e-5f);
            Vector3 eyeForward = pose.Rotation * anchorRot * Vector3.forward;
            Assert.AreEqual(0f, Vector3.Angle(eyeForward, Vector3.forward), 1e-3f);
        }

        [Test]
        public void EyeFromController_NonIdentity_MatchesTransformHierarchy()
        {
            // 실제 Transform 계층(눈 → 컨트롤러, 오프셋·회전·눈 배율 있음)과 순수 함수 결과가 같다
            var eye = new GameObject("Eye").transform;
            var controller = new GameObject("Controller").transform;
            try
            {
                eye.SetPositionAndRotation(new Vector3(3f, 1.7f, -2f), Quaternion.Euler(-12f, 35f, 4f));
                eye.localScale = new Vector3(1f, 1f, 1f);
                controller.SetParent(eye, false);
                controller.localPosition = new Vector3(0.05f, -0.03f, 0.02f);
                controller.localRotation = Quaternion.Euler(2f, -3f, 1f);
                AdsAlignment.EyeFromController(eye.position, eye.rotation, eye.lossyScale, controller.position, controller.rotation,
                    out Vector3 position, out Quaternion rotation);
                Assert.Less(Vector3.Distance(controller.localPosition, position), 1e-5f);
                Assert.Less(Quaternion.Angle(controller.localRotation, rotation), 1e-3f);
            }
            finally
            {
                Object.DestroyImmediate(controller.gameObject);
                Object.DestroyImmediate(eye.gameObject);
            }
        }

        [Test]
        public void Solve_NonIdentityEyeOffset_AnchorOnAxis_AndZeroHitsAimPoint()
        {
            // 컨트롤러가 눈에서 비켜 있고 기울어 있어도: 앵커 = 눈 축 위 eyeRelief, 영점 방향(눈 좌표)으로 쏜 탄이 영점 점을 지난다
            var input = new AdsAlignment.Input
            {
                EyeFromControllerPosition = new Vector3(0.04f, -0.02f, 0.03f),
                EyeFromControllerRotation = Quaternion.Euler(3f, -4f, 2f),
                FallbackPosition = new Vector3(0f, -0.09f, 0.35f),
                HasAnchor = true,
                AnchorInRoot = new Vector3(-0.006f, 0.115f, -0.207f),
                AnchorRotationInRoot = Quaternion.Euler(-0.74f, 0f, 0f),
                EyeReliefM = 0.12f,
                HasMuzzle = true,
                MuzzleInRoot = new Vector3(0f, 0f, 0.3f),
                ZeroRangeM = 100f,
                Speed = 900f, Gravity = 9.81f, DragPerM = 0.0011f, FixedDt = Dt, MaxFlightSec = 3f,
            };
            AdsAlignment.Result r = AdsAlignment.Solve(input);
            Vector3 anchorEye = AdsAlignment.ToEye(input.EyeFromControllerPosition, input.EyeFromControllerRotation, r.Root, input.AnchorInRoot);
            Assert.Less(Vector3.Distance(anchorEye, new Vector3(0f, 0f, 0.12f)), 1e-5f);
            Quaternion anchorEyeRot = input.EyeFromControllerRotation * r.Root.Rotation * input.AnchorRotationInRoot;
            Assert.Less(Quaternion.Angle(anchorEyeRot, Quaternion.identity), 1e-3f);

            Assert.IsTrue(r.HasZero);
            Vector3 muzzleEye = AdsAlignment.ToEye(input.EyeFromControllerPosition, input.EyeFromControllerRotation, r.Root, input.MuzzleInRoot);
            Vector3 p = muzzleEye, v = input.EyeFromControllerRotation * r.ZeroDirection * input.Speed, from = p;
            while (p.z < input.ZeroRangeM)
            {
                from = p;
                Ballistics.Step(ref p, ref v, G, input.DragPerM, Dt);
            }
            Vector3 hit = Vector3.Lerp(from, p, (input.ZeroRangeM - from.z) / (p.z - from.z));
            Assert.Less(new Vector2(hit.x, hit.y).magnitude, 1e-4f);
        }

        [Test]
        public void Solve_NoAnchorOrRelief_UsesFallback_NoZeroWithoutMuzzle()
        {
            var input = new AdsAlignment.Input
            {
                EyeFromControllerRotation = Quaternion.identity,
                FallbackPosition = new Vector3(0f, -0.09f, 0.35f),
                HasAnchor = true, AnchorRotationInRoot = Quaternion.identity, EyeReliefM = 0f,
                ZeroRangeM = 50f,
            };
            AdsAlignment.Result r = AdsAlignment.Solve(input);
            Assert.AreEqual(input.FallbackPosition, r.Root.Position);
            Assert.AreEqual(Quaternion.identity, r.Root.Rotation);
            Assert.IsFalse(r.HasZero);
            Assert.IsFalse(r.ZeroFailed);
        }

        // ---------- 데이터 회귀 (AB-Q1~Q3 임시값이 기록돼 있는지) ----------

        private const string WeaponsFolder = "Assets/_Project/Data/Weapons/";

        [TestCase("WPN-PST-01", 0.30f, 25f)]
        [TestCase("WPN-SMG-01", 0.13f, 50f)]
        [TestCase("WPN-RIF-01", 0.12f, 100f)]
        [TestCase("WPN-SHT-01", 0.15f, 25f)]
        [TestCase("WPN-DMR-01", 0.16f, 200f)]
        public void Data_Weapon_AdsEyeReliefAndZero(string id, float eyeRelief, float zero)
        {
            var w = UnityEditor.AssetDatabase.LoadAssetAtPath<Ashfall.Core.WeaponDefinitionSO>(WeaponsFolder + id + ".asset");
            Assert.IsNotNull(w, id);
            Assert.AreEqual(eyeRelief, w.adsEyeReliefM, 1e-6f, $"{id}.adsEyeReliefM");
            Assert.AreEqual(zero, w.zeroRangeM, 1e-6f, $"{id}.zeroRangeM");
        }

        [TestCase("AMMO-9MM", 0.0020f)]
        [TestCase("AMMO-556", 0.0011f)]
        [TestCase("AMMO-762", 0.0008f)]
        [TestCase("AMMO-12GA", 0.0040f)]
        public void Data_Ammo_Drag(string id, float drag)
        {
            var a = UnityEditor.AssetDatabase.LoadAssetAtPath<Ashfall.Core.AmmoDefinitionSO>(WeaponsFolder + id + ".asset");
            Assert.IsNotNull(a, id);
            Assert.AreEqual(drag, a.dragPerM, 1e-7f, $"{id}.dragPerM");
        }
    }
}
