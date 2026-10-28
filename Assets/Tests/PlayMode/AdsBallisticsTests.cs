#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Ashfall.Core;
using Ashfall.Weapons;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ashfall.Tests.PlayMode
{
    // 정조준·탄도 (AB-Q1~Q3, docs/design/AdsBallistics_design.md §5). 무기 5종의 실제 데이터·VM 프리팹(스킨 포함)으로:
    // (a) 정조준(AimProgress 1, 흔들림 0)에서 SightAnchor와 그 시선 앞 점이 1920×1080 화면 중앙 2px 안.
    // (b) 정조준 사격의 탄착 vs 카메라 중앙 광선 교점: 영점 거리는 ≤ 1cm, 다른 거리는 독립 기준(RK4, 작은 dt, 자체 영점 해법)과 비교.
    // 산탄은 펠릿 퍼짐 0(무기 정의 복제본)으로 묶음 중심을 본다. 장면은 코드로 만든다(카메라 → 무기 컨트롤러, 인벤토리 없음 = M1 지급 경로).
    // 환경 변수 ASHFALL_ADS_SHOTS=<폴더>이면 무기별 정조준 화면을 1920×1080 PNG로 저장한다(눈 확인용).
    public class AdsBallisticsTests
    {
        private const string Folder = "Assets/_Project/Data/Weapons/";
        private const float ScreenW = 1920f, ScreenH = 1080f;
        private const float MaxSightErrorPx = 2f;
        private const float ZeroToleranceM = 0.01f;
        private static readonly float[] Distances = { 10f, 25f, 50f, 100f, 200f };

        private static readonly (string Weapon, string Magazine)[] Cases =
        {
            ("WPN-PST-01", "MAG-9MM-12"),
            ("WPN-SMG-01", "MAG-9MM-30"),
            ("WPN-RIF-01", "MAG-556-30"),
            ("WPN-SHT-01", "BOX-12GA-8"),
            ("WPN-DMR-01", "MAG-762-5"),
        };

        // 결과 표 (모든 케이스가 끝나면 로그)
        private static readonly StringBuilder Report = new StringBuilder();

        private GameObject rig, world;
        private SimInput input;
        private RenderTexture rt;

        [OneTimeTearDown]
        public void LogReport()
        {
            if (Report.Length > 0)
                Debug.Log("[AdsBallisticsTests]\n" + Report);
            Report.Length = 0;
        }

        [TearDown]
        public void TearDown()
        {
            input?.Dispose();
            input = null;
            Object.Destroy(rig);
            Object.Destroy(world);
            if (rt != null)
                rt.Release();
        }

        private static T Load<T>(string id) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(Folder + id + ".asset");
            Assert.IsNotNull(asset, Folder + id);
            return asset;
        }

        private sealed class Rig
        {
            public Camera Camera;
            public WeaponController Weapon;
            public WeaponDefinitionSO Definition;
            public AmmoDefinitionSO Ammo;
        }

        // 카메라(1920×1080 RT, 수평) → 무기 컨트롤러. 흔들림 0 핸들링 복제본, 산탄 펠릿 퍼짐 0 무기 복제본.
        private Rig Build(string weaponId, string magazineId)
        {
            var handlingAsset = Load<WeaponHandlingConfigSO>("WeaponHandlingConfig");
            var handling = Object.Instantiate(handlingAsset);
            handling.aimSwayBaseDeg = 0f;
            var definition = Object.Instantiate(Load<WeaponDefinitionSO>(weaponId));
            definition.pelletSpreadDeg = 0f;
            Assert.IsTrue(handling.TryGetAmmo(definition.ammo, out AmmoDefinitionSO ammo), "탄약 표");

            rig = new GameObject("AdsRig");
            rig.transform.position = new Vector3(0f, 100f, 0f);
            var projectiles = rig.AddComponent<ProjectileSystem>();
            var camGo = new GameObject("Camera", typeof(Camera));
            camGo.transform.SetParent(rig.transform, false);
            var cam = camGo.GetComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            cam.fieldOfView = 60f;
            rt = new RenderTexture((int)ScreenW, (int)ScreenH, 24);
            cam.targetTexture = rt;

            var weaponGo = new GameObject("Weapon");
            weaponGo.SetActive(false);
            weaponGo.transform.SetParent(camGo.transform, false);
            var weapon = weaponGo.AddComponent<WeaponController>();
            weapon.Configure(definition, ammo, Load<MagazineDefinitionSO>(magazineId), 6, handling, projectiles, 1);
            var so = new SerializedObject(weapon);
            so.FindProperty("hipLocalPosition").vector3Value = new Vector3(0.2f, -0.18f, 0.45f);
            so.FindProperty("adsLocalPosition").vector3Value = new Vector3(0f, -0.09f, 0.35f);
            so.ApplyModifiedPropertiesWithoutUndo();
            weaponGo.SetActive(true);

            world = new GameObject("AdsWorld");
            return new Rig { Camera = cam, Weapon = weapon, Definition = definition, Ammo = ammo };
        }

        private IEnumerator AimFully(Rig r)
        {
            input = new SimInput();
            yield return null;
            input.Mouse.SetRight(true);
            float deadline = Time.realtimeSinceStartup + 5f;
            while (r.Weapon.AimProgress < 1f && Time.realtimeSinceStartup < deadline)
                yield return null;
            yield return null;
            Assert.AreEqual(1f, r.Weapon.AimProgress, "정조준 완료");
        }

        private static float PixelError(Camera cam, Vector3 world)
        {
            Vector3 s = cam.WorldToScreenPoint(world);
            float scale = ScreenH / cam.pixelHeight;   // 1080p 환산 (RT가 1080p라 1)
            return new Vector2((s.x - cam.pixelWidth * 0.5f) * scale, (s.y - cam.pixelHeight * 0.5f) * scale).magnitude;
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator Ads_SightAnchorAtScreenCenter_Within2px([ValueSource(nameof(WeaponIds))] string weaponId)
        {
            Rig r = Build(weaponId, MagazineFor(weaponId));
            yield return AimFully(r);
            Transform anchor = r.Weapon.CurrentSightAnchor;
            Assert.IsNotNull(anchor, $"{weaponId}: VM 프리팹에 SightAnchor 없음 (Ashfall/Setup/Weapons/Fit Sight Anchors)");
            float rear = PixelError(r.Camera, anchor.position);
            float front = PixelError(r.Camera, anchor.position + anchor.forward * 0.5f);
            float far = PixelError(r.Camera, anchor.position + anchor.forward * 100f);
            Report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "(a) {0}: 가늠자 {1:0.000}px, 시선 0.5m {2:0.000}px, 100m {3:0.000}px", weaponId, rear, front, far));
            SaveShot(r, weaponId);
            Assert.LessOrEqual(rear, MaxSightErrorPx, "가늠자 기준점");
            Assert.LessOrEqual(front, MaxSightErrorPx, "시선 앞 0.5m(가늠쇠 쪽)");
            Assert.LessOrEqual(far, MaxSightErrorPx, "시선 100m");
        }

        public static IEnumerable<string> WeaponIds()
        {
            foreach (var c in Cases)
                yield return c.Weapon;
        }

        private static string MagazineFor(string weaponId)
        {
            foreach (var c in Cases)
                if (c.Weapon == weaponId)
                    return c.Magazine;
            return null;
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Ads_ImpactMatchesZeroAndIndependentDrop([ValueSource(nameof(WeaponIds))] string weaponId)
        {
            Rig r = Build(weaponId, MagazineFor(weaponId));
            yield return AimFully(r);
            Assert.IsTrue(r.Weapon.IsZeroed, $"{weaponId}: 영점이 적용되지 않았다");
            Camera cam = r.Camera;
            Transform ct = cam.transform;
            float zero = r.Definition.zeroRangeM;
            float shotInterval = 60f / Mathf.Max(1, r.Definition.rpm) + 0.1f;

            // 독립 기준: 카메라 좌표의 총구 → RK4로 자체 영점(이분법) → 거리별 예상 높이
            Vector3 muzzleEye = ct.InverseTransformPoint(r.Weapon.MuzzlePosition);
            float g = Physics.gravity.magnitude;
            float pitch = ReferenceZeroPitch(muzzleEye, zero, r.Ammo.velocity, r.Ammo.dragPerM, g);

            foreach (float d in Distances)
            {
                var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
                plate.name = $"Plate_{d}m";
                plate.transform.SetParent(world.transform, false);
                plate.transform.position = ct.position + ct.forward * d;
                plate.transform.rotation = ct.rotation;
                plate.transform.localScale = new Vector3(20f, 20f, 0.05f);
                var target = plate.AddComponent<HitTarget>();
                Physics.SyncTransforms();
                yield return new WaitForFixedUpdate();

                input.Mouse.SetLeft(true);
                yield return null;
                yield return null;
                input.Mouse.SetLeft(false);
                float deadline = Time.realtimeSinceStartup + 5f;
                while (target.HitCount == 0 && Time.realtimeSinceStartup < deadline)
                    yield return new WaitForFixedUpdate();
                Assert.Greater(target.HitCount, 0, $"{weaponId} {d}m: 표적에 맞지 않았다");

                // 판 앞면(카메라 쪽) 교점 기준이라 두께 절반만큼 앞이다 — 앞면 평면 z = d - 0.025
                Vector3 hitEye = ct.InverseTransformPoint(target.LastHit.Point);
                float planeZ = hitEye.z;
                bool reached = ReferenceHeight(muzzleEye, zero, pitch, planeZ, r.Ammo.velocity, r.Ammo.dragPerM, g, out float expectedY, out float expectedX);
                Assert.IsTrue(reached, "기준 탄도가 표적에 닿아야 한다");
                float errY = hitEye.y - expectedY;
                float errX = hitEye.x - expectedX;
                bool isZero = Mathf.Abs(d - zero) < 0.01f;
                // 허용: 영점 1cm(시선 교점과 직접). 그 외 = 1cm + 낙차(시선 기준) 2% — 게임 반암시적 오일러(dt 0.02) vs RK4 차이.
                // 근거: 중력 낙차의 오일러 오차(g·t·dt/2)는 시간에 선형이라 영점 각이 흡수하고, 남는 것은 드래그 결합 항(낙차의 1% 미만, EditMode 실측).
                float tol = isZero ? ZeroToleranceM : ZeroToleranceM + Mathf.Abs(expectedY) * 0.02f;
                Report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "(b) {0} {1,5:0}m{2}: 탄착 y {3,8:0.0000} x {4,7:0.0000} | 기준 y {5,8:0.0000} | 오차 y {6,7:0.0000} x {7,7:0.0000} (허용 {8:0.0000})",
                    weaponId, d, isZero ? "*" : " ", hitEye.y, hitEye.x, expectedY, errY, errX, tol));
                if (isZero)
                    Assert.LessOrEqual(new Vector2(hitEye.x, hitEye.y).magnitude, ZeroToleranceM, $"{weaponId} 영점 {d}m: 조준점에서 벗어남");
                Assert.LessOrEqual(Mathf.Abs(errY), tol, $"{weaponId} {d}m 높이");
                Assert.LessOrEqual(Mathf.Abs(errX), ZeroToleranceM, $"{weaponId} {d}m 좌우");

                Object.Destroy(plate);
                yield return new WaitForSeconds(shotInterval);
            }
        }

        // ---------- 독립 기준 탄도 (게임 Ballistics를 쓰지 않는다): RK4, dt 0.0002 s, 이분법 영점 ----------

        private const double RefDt = 0.0002;

        private static float ReferenceZeroPitch(Vector3 muzzle, float zero, float speed, float drag, float g)
        {
            double lo = -0.05, hi = 0.1;   // 라디안
            for (int i = 0; i < 60; i++)
            {
                double mid = (lo + hi) * 0.5;
                ReferenceHeight(muzzle, zero, (float)mid, zero, speed, drag, g, out float y, out _);
                if (y > 0f) hi = mid; else lo = mid;
            }
            return (float)((lo + hi) * 0.5);
        }

        // 수평 방향은 총구 → 시선 위 영점 점 (0, zero) 직선 (게임과 같은 정의). 높이·좌우를 z = planeZ에서 돌려준다.
        private static bool ReferenceHeight(Vector3 muzzle, float zero, float pitch, float planeZ, float speed, float drag, float g,
            out float y, out float x)
        {
            // 좌우 방향: 총구 → 영점 점(0, zero) 직선 (게임 정의와 같음, 드래그는 수평 방향을 바꾸지 않는다)
            double hx = -muzzle.x, hz = zero - muzzle.z;
            double hn = System.Math.Sqrt(hx * hx + hz * hz);
            hx /= hn; hz /= hn;
            double c = System.Math.Cos(pitch), s = System.Math.Sin(pitch);
            double[] st = { muzzle.x, muzzle.y, muzzle.z, hx * c * speed, s * speed, hz * c * speed };
            double t = 0;
            while (t < 3.0)
            {
                double[] prev = (double[])st.Clone();
                Rk4(st, drag, g);
                t += RefDt;
                if (st[2] >= planeZ)
                {
                    double f = (planeZ - prev[2]) / (st[2] - prev[2]);
                    y = (float)(prev[1] + (st[1] - prev[1]) * f);
                    x = (float)(prev[0] + (st[0] - prev[0]) * f);
                    return true;
                }
            }
            y = x = 0f;
            return false;
        }

        private static void Rk4(double[] st, double k, double g)
        {
            double[] k1 = Deriv(st, k, g);
            double[] k2 = Deriv(Add(st, k1, RefDt / 2), k, g);
            double[] k3 = Deriv(Add(st, k2, RefDt / 2), k, g);
            double[] k4 = Deriv(Add(st, k3, RefDt), k, g);
            for (int i = 0; i < 6; i++)
                st[i] += RefDt / 6 * (k1[i] + 2 * k2[i] + 2 * k3[i] + k4[i]);
        }

        private static double[] Add(double[] a, double[] b, double h)
        {
            var r = new double[6];
            for (int i = 0; i < 6; i++)
                r[i] = a[i] + b[i] * h;
            return r;
        }

        private static double[] Deriv(double[] st, double k, double g)
        {
            double vx = st[3], vy = st[4], vz = st[5];
            double sp = System.Math.Sqrt(vx * vx + vy * vy + vz * vz);
            return new[] { vx, vy, vz, -k * sp * vx, -g - k * sp * vy, -k * sp * vz };
        }

        // ---------- 스크린샷 ----------

        private void SaveShot(Rig r, string weaponId)
        {
            string dir = System.Environment.GetEnvironmentVariable("ASHFALL_ADS_SHOTS");
            if (string.IsNullOrEmpty(dir))
                return;
            System.IO.Directory.CreateDirectory(dir);
            // 25m 표적 판 + 십자 (중앙 확인용)
            var board = GameObject.CreatePrimitive(PrimitiveType.Cube);
            board.transform.SetParent(world.transform, false);
            Transform ct = r.Camera.transform;
            board.transform.SetPositionAndRotation(ct.position + ct.forward * 25f, ct.rotation);
            board.transform.localScale = new Vector3(2f, 2f, 0.05f);
            var light = new GameObject("Light", typeof(Light));
            light.transform.SetParent(world.transform, false);
            light.GetComponent<Light>().type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(40f, 20f, 0f);
            r.Camera.clearFlags = CameraClearFlags.SolidColor;
            r.Camera.backgroundColor = new Color(0.62f, 0.7f, 0.78f);
            r.Camera.Render();
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            // 화면 중앙 표시 (1px 빨간 점 3×3 바깥 테두리만 — 가늠쇠 끝을 가리지 않게 중앙에서 6px 떨어진 눈금)
            int cx = rt.width / 2, cy = rt.height / 2;
            for (int i = 6; i < 16; i++)
            {
                tex.SetPixel(cx + i, cy, Color.red);
                tex.SetPixel(cx - i, cy, Color.red);
                tex.SetPixel(cx, cy + i, Color.red);
                tex.SetPixel(cx, cy - i, Color.red);
            }
            tex.Apply();
            RenderTexture.active = prev;
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, $"ADS_{weaponId}.png"), tex.EncodeToPNG());
            Object.Destroy(tex);
            Object.Destroy(board);
        }
    }
}
#endif
