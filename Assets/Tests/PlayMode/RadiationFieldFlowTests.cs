#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using Ashfall.Audio;
using Ashfall.Core;
using Ashfall.Raid;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = NUnit.Framework.Is;
using Object = UnityEngine.Object;

namespace Ashfall.Tests.PlayMode
{
    // M7 7C-2c 정유소 방사능 구역 (M7 설계 §1.5·§3.10·§6 "7C-2c", Q39~Q41, STAT-RAD). 실제 Raid_CrimsonBasin
    // (+ M7HazardSceneApply: 최상위 "RadiationField", "M7_ToxicMarkers" 아래 M7_Toxic_* — 통합 = Ashfall/Setup/Apply M7 Content,
    //  단독 = Ashfall/Setup/M7/Apply Radiation Zone (Raid Scene)).
    // 확인: IRadiationField 등록, RAD-REF 률(중심 0.5·밖 0·가장자리 선형), Toxic 표시(콜라이더·NavMeshObstacle 없음, 최상위 묶음, M_Toxic = 팔레트 Toxic),
    // 가이거 입력 = 폭풍 률 + 청취자 위치 구역 률(AudioDirector가 읽은 값), 조회 GC 0.
    // 경로·이름은 M7Names·PaletteMaterialMap 계약 값(테스트 asmdef는 EditorTools를 참조하지 않는다).
    public class RadiationFieldFlowTests
    {
        private const string FieldObject = "RadiationField";             // M7Names.RadiationFieldObject
        private const string MarkersObject = "M7_ToxicMarkers";          // M7Names.ToxicMarkersObject
        private const string MarkerPrefix = "M7_Toxic_";                 // M7Names.ToxicMarkerPrefix
        private const string ToxicMaterialPath = "Assets/_Project/Art/Materials/Palette/M_Toxic.mat";   // M7Names.ToxicMaterialPath
        private const string PalettePath = "Assets/_Project/Data/Art/Palette.asset";
        private const string ZoneId = "RAD-REF";
        private const float ColorEps = 1.5f / 255f;
        private const float Eps = 1e-5f;

        // 설계 §3.10 (TODO(GDD-?) Q39)
        private static readonly Vector2 Center = new Vector2(300f, -60f);
        private const float Radius = 70f;
        private const float Rate = 0.5f;
        private const float Falloff = 15f;
        // M7HazardLayout 계약: 둘레 기둥 12 + 판 12 + 드럼통 12, 무더기 3 × 6
        private const int ExpectedMarkers = 54;

        private M4Raid raid;
        private RadiationField field;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            raid = new M4Raid();
            yield return M4Raid.Load(raid);
            yield return raid.RemoveEnemies();   // 교전 변수 제거 (정유소 RDR 분대가 구역 안에 있다)
            RadiationField[] fields = Object.FindObjectsByType<RadiationField>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Assert.AreEqual(1, fields.Length,
                "RadiationField는 하나 — Ashfall/Setup/M7/Apply Radiation Zone (Raid Scene) 또는 Apply M7 Content 실행 필요");
            field = fields[0];
        }

        [TearDown]
        public void TearDown()
        {
            if (raid?.Gm != null && raid.Gm.Flow.IsWorldPaused)
                raid.Gm.Flow.SetWorldPaused(false);
        }

        private static Vector3 AtDistance(float d, float angleDeg = 30f, float y = 5f)
        {
            float a = angleDeg * Mathf.Deg2Rad;
            return new Vector3(Center.x + Mathf.Cos(a) * d, y, Center.y + Mathf.Sin(a) * d);
        }

        [UnityTest]
        public IEnumerator Field_Registered_ZoneRates_CenterEdgeOutside()
        {
            Assert.AreEqual(FieldObject, field.name);
            Assert.IsNull(field.transform.parent, "씬 최상위");
            Assert.IsTrue(field.isActiveAndEnabled);
            Assert.IsTrue(raid.Gm.Services.TryGet(out IRadiationField service), "IRadiationField 미등록");
            Assert.AreSame(field, service);
            Assert.AreSame(raid.Config, field.Config, "RaidController와 같은 RaidConfigSO");

            RadiationZoneConfig zone = field.Config.radiationZones.Single(z => z.id == ZoneId);
            Assert.AreEqual(Center, zone.centerXZ);
            Assert.AreEqual(Radius, zone.radiusM);
            Assert.AreEqual(Rate, zone.ratePerSec);
            Assert.AreEqual(Falloff, zone.edgeFalloffM);

            // 중심·안쪽 = 최대, 감쇠 띠 = 선형, 경계·밖 = 0. 높이는 보지 않는다.
            Assert.AreEqual(0.5f, service.ZoneRatePerSec(new Vector3(Center.x, 0f, Center.y)), Eps, "중심 0.5/s");
            Assert.AreEqual(0.5f, service.ZoneRatePerSec(new Vector3(Center.x, 500f, Center.y)), Eps, "높이 무관");
            Assert.AreEqual(0.5f, service.ZoneRatePerSec(AtDistance(Radius - Falloff)), Eps, "감쇠 시작 = 최대");
            Assert.AreEqual(0.25f, service.ZoneRatePerSec(AtDistance(Radius - Falloff * 0.5f)), Eps, "감쇠 가운데 = 절반");
            Assert.AreEqual(0.5f * 3f / 15f, service.ZoneRatePerSec(AtDistance(Radius - 3f, 200f)), Eps, "가장자리 3m 안 = 선형");
            Assert.AreEqual(0f, service.ZoneRatePerSec(AtDistance(Radius)), Eps, "경계 = 0");
            Assert.AreEqual(0f, service.ZoneRatePerSec(AtDistance(Radius + 0.5f)), "밖 0");
            Assert.AreEqual(0f, service.ZoneRatePerSec(AtDistance(300f)), "멀리 0");
            // 거리에 따라 단조 감소
            float prev = float.MaxValue;
            for (float d = 0f; d <= Radius + 5f; d += 2.5f)
            {
                float r = service.ZoneRatePerSec(AtDistance(d, 110f));
                Assert.LessOrEqual(r, prev + Eps, $"{d}m");
                prev = r;
            }
            // 스폰 3곳은 구역 밖 (데이터 테스트와 같은 조건을 실제 씬 위치로)
            foreach (PlayerSpawnPoint sp in Object.FindObjectsByType<PlayerSpawnPoint>(FindObjectsSortMode.None))
                Assert.AreEqual(0f, service.ZoneRatePerSec(sp.transform.position), $"스폰 {sp.name}");

            // 조회 GC 0 (선량 누적·가이거가 매 프레임 부른다)
            Vector3 probe = AtDistance(60f);
            float sink = 0f;
            AssertNoAlloc(() =>
            {
                for (int i = 0; i < 1000; i++)
                    sink += service.ZoneRatePerSec(probe);
            });
            Assert.Greater(sink, 0f);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Field_Unregisters_OnDisable_AndReregisters()
        {
            field.enabled = false;
            Assert.IsFalse(raid.Gm.Services.TryGet(out IRadiationField _), "꺼지면 해제");
            yield return null;
            field.enabled = true;
            Assert.IsTrue(raid.Gm.Services.TryGet(out IRadiationField again));
            Assert.AreSame(field, again);
        }

        [UnityTest]
        public IEnumerator ToxicMarkers_NoColliders_NotOnNavMeshPath_ToxicMaterial()
        {
            GameObject root = GameObject.Find(MarkersObject);
            Assert.IsNotNull(root, $"{MarkersObject} 없음");
            Assert.IsNull(root.transform.parent, "씬 최상위 (NavMeshSurface 자식 수집 밖)");
            Assert.AreEqual(ExpectedMarkers, root.transform.childCount, "표시 수");
            Assert.AreEqual(0, root.GetComponentsInChildren<Collider>(true).Length, "콜라이더 없음 → NavMesh(PhysicsColliders)·전투 무관");
            Assert.AreEqual(0, root.GetComponentsInChildren<NavMeshObstacle>(true).Length, "NavMeshObstacle 없음");
            Assert.AreEqual(0, root.GetComponentsInChildren<Rigidbody>(true).Length);

            PaletteSO palette = AssetDatabase.LoadAssetAtPath<PaletteSO>(PalettePath);
            var toxic = AssetDatabase.LoadAssetAtPath<Material>(ToxicMaterialPath);
            Assert.IsNotNull(palette, PalettePath);
            Assert.IsNotNull(toxic, ToxicMaterialPath);
            Color c = toxic.GetColor("_BaseColor");
            Color expected = palette.Get(PaletteToken.Toxic);
            Assert.AreEqual(expected.r, c.r, ColorEps, "M_Toxic = 팔레트 Toxic");
            Assert.AreEqual(expected.g, c.g, ColorEps);
            Assert.AreEqual(expected.b, c.b, ColorEps);

            int edge = 0, inside = 0;
            Assert.IsTrue(raid.Gm.Services.TryGet(out IRadiationField service));
            foreach (Transform t in root.transform)
            {
                StringAssert.StartsWith(MarkerPrefix, t.name);
                var r = t.GetComponent<MeshRenderer>();
                Assert.IsNotNull(r, t.name);
                Assert.IsTrue(r.enabled, t.name);
                Assert.AreSame(toxic, r.sharedMaterial, t.name);
                Vector3 p = t.position;
                float d = Vector2.Distance(new Vector2(p.x, p.z), Center);
                if (t.name.Contains("_Pile"))
                {
                    inside++;
                    Assert.AreEqual(Rate, service.ZoneRatePerSec(p), Eps, $"{t.name}: 무더기는 최대 률 영역 안");
                }
                else
                {
                    edge++;
                    Assert.That(d, Is.InRange(Radius - Falloff, Radius), $"{t.name}: 둘레 표시는 감쇠 띠 안");
                }
            }
            Assert.AreEqual(36, edge, "둘레 기둥·판·드럼통");
            Assert.AreEqual(18, inside, "무더기 3 × 6");

            // 무더기 바로 옆 바닥에 NavMesh가 그대로 있다 (표시는 굽기·경로와 무관)
            foreach (string pile in new[] { "PileA_1", "PileB_1", "PileC_1" })
            {
                Transform t = root.transform.Find(MarkerPrefix + pile);
                Assert.IsNotNull(t, pile);
                Assert.IsTrue(NavMesh.SamplePosition(t.position, out NavMeshHit hit, 2f, NavMesh.AllAreas), $"{pile} 아래 NavMesh");
                Assert.Less(Vector2.Distance(new Vector2(hit.position.x, hit.position.z), new Vector2(t.position.x, t.position.z)), 0.5f,
                    $"{pile}: NavMesh가 통 자리에 뚫리지 않았다");
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator Geiger_Input_IsStormPlusZone_AtListener()
        {
            AudioDirector audio = Object.FindFirstObjectByType<AudioDirector>();
            Assert.IsNotNull(audio, "AudioDirector 없음");
            AshStormController storm = Object.FindFirstObjectByType<AshStormController>();
            Assert.IsNotNull(storm, "AshStormController 없음");
            Assert.IsTrue(raid.Gm.Services.TryGet(out IRadiationField service));

            // 스폰(구역 밖): 구역 몫 0, 폭풍 전 = 가이거 입력 0
            audio.ResolveServices();
            yield return null;
            yield return null;
            Assert.IsTrue(audio.HasRadiationField, "AudioDirector가 IRadiationField를 잡았다");
            Assert.AreEqual(0f, audio.ZoneRadiationPerSec, "스폰은 구역 밖");
            Assert.AreEqual(0f, audio.Ambient.GeigerInputPerSec, "폭풍 전·구역 밖 = 0");

            // 정유소 중심으로 → 구역 몫 = 청취자 위치 률 (0.5)
            Vector3 feet = GroundAt(Center);
            raid.Placement.PlaceAt(feet, 0f);
            yield return null;
            yield return null;
            Vector3 listener = audio.ListenerPosition;
            Assert.Less(Vector2.Distance(new Vector2(listener.x, listener.z), Center), 1f, "청취자 = 플레이어 눈");
            Assert.AreEqual(service.ZoneRatePerSec(listener), audio.ZoneRadiationPerSec, Eps);
            Assert.AreEqual(Rate, audio.ZoneRadiationPerSec, Eps, "구역 중심 0.5/s");
            Assert.AreEqual(0f, audio.StormRadiationPerSec);
            Assert.AreEqual(Rate, audio.Ambient.GeigerInputPerSec, Eps, "가이거 = 폭풍 0 + 구역 0.5");
            int clicks0 = audio.Ambient.GeigerClicks;
            float until = Time.realtimeSinceStartup + 3f;   // 0.5/s × geigerClicksPerRad — 3초면 0클릭일 확률 무시할 만함
            while (Time.realtimeSinceStartup < until)
                yield return null;
            Assert.Greater(audio.Ambient.GeigerClicks, clicks0, "구역 안 → 가이거 클릭 (폭풍 없이)");

            // 폭풍 Peak와 겹치면 합 (방독면과 무관 — 환경 률)
            Assert.IsTrue(storm.ForceStartNow());
            yield return M3Scene.WaitUntil(() => storm.Phase == AshStormPhase.Rising, 2f, () => "Rising");
            raid.Controller.AdvanceTime(storm.Config.riseSec + 1f);
            yield return null;
            yield return null;
            Assert.AreEqual(AshStormPhase.Peak, storm.Phase);
            Assert.AreEqual(storm.RadiationPerSec, audio.StormRadiationPerSec, Eps);
            Assert.AreEqual(1f, audio.StormRadiationPerSec, Eps, "§8.3 Peak +1/s");
            Assert.AreEqual(1f + Rate, audio.Ambient.GeigerInputPerSec, Eps, "가이거 = 폭풍 1 + 구역 0.5");
            Assert.AreEqual(audio.StormRadiationPerSec + audio.ZoneRadiationPerSec, audio.Ambient.GeigerInputPerSec, Eps);

            // 구역 밖으로 → 폭풍 몫만
            raid.Placement.PlaceAt(GroundAt(new Vector2(Center.x, Center.y + Radius + 20f)), 0f);
            yield return null;
            yield return null;
            Assert.AreEqual(0f, audio.ZoneRadiationPerSec);
            Assert.AreEqual(1f, audio.Ambient.GeigerInputPerSec, Eps, "구역 밖 = 폭풍만");
        }

        // 위에서 아래로 쏴서 지면(구조물 포함) 점 — 플레이어를 바닥에 놓는다
        private static Vector3 GroundAt(Vector2 xz)
        {
            var top = new Vector3(xz.x, 400f, xz.y);
            RaycastHit[] hits = Physics.RaycastAll(top, Vector3.down, 800f, ~0, QueryTriggerInteraction.Ignore);
            Assert.Greater(hits.Length, 0, $"{xz} 지면 없음");
            float y = hits.Max(h => h.point.y);
            return new Vector3(xz.x, y + 0.05f, xz.y);
        }

        // 워밍업 + 측정 잡음 재시도 (AudioFlowTests와 같은 방식)
        private static void AssertNoAlloc(TestDelegate act, int attempts = 6)
        {
            act();
            for (int i = 0; i < attempts - 1; i++)
            {
                if (((NUnit.Framework.Constraints.IResolveConstraint)Is.Not.AllocatingGCMemory()).Resolve().ApplyTo(act).IsSuccess)
                    return;
                System.Threading.Thread.Sleep(50 * (i + 1));
            }
            Assert.That(act, Is.Not.AllocatingGCMemory());
        }
    }
}
#endif
