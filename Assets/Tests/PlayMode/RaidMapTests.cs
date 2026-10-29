#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Ashfall.AI;
using Ashfall.Core;
using Ashfall.Raid;
using Ashfall.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Ashfall.Tests.PlayMode
{
    // M4 AC1 — Crimson Basin 블록아웃(§8.1): 1km Terrain, POI 5곳, 경계 벽(맵 밖 이탈 불가), 스폰 → 활성 탈출구 NavMesh 경로.
    // RULE-06 — 지도(M): 활성 탈출구·POI만, 내 위치 없음.
    public class RaidMapTests
    {
        private static readonly string[] GddPoiNames =
            { "붕괴된 고속도로", "방사능 정유소", "매몰된 마을 외곽", "송전탑 언덕", "말라붙은 저수지" };   // §8.1

        private const float MapHalfM = 500f;
        private const float WallInnerM = 495f;

        private M4Raid raid;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            raid = new M4Raid();
            yield break;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            GameManager gm = GameManager.Instance;
            if (gm != null && gm.Flow != null)
            {
                gm.Flow.AcknowledgeResult();
                gm.Flow.SetWorldPaused(false);
            }
            Time.timeScale = 1f;
            yield return null;
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Blockout_Terrain1km_FivePois_SpawnToActiveExtractionPaths_BoundaryWallsStopPlayer()
        {
            yield return M4Raid.Load(raid);
            yield return raid.RemoveEnemies();

            // 1km Unity Terrain (원점 = 맵 중앙)
            Terrain[] terrains = Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None);
            Assert.AreEqual(1, terrains.Length, "Terrain 1장");
            Terrain terrain = terrains[0];
            Assert.AreEqual(new Vector3(1000f, 120f, 1000f), terrain.terrainData.size, "Terrain 1000×120×1000");
            Assert.AreEqual(new Vector3(-MapHalfM, 0f, -MapHalfM), terrain.transform.position, "원점 = 맵 중앙");
            // TerrainCollider: 테스트 asmdef는 Terrain Physics 모듈(관리 어셈블리)을 참조하지 않아 타입 대신 레이로 확인한다.
            Assert.IsTrue(Physics.Raycast(new Vector3(-200f, 300f, -200f), Vector3.down, out RaycastHit terrainHit, 400f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), "지형 충돌 없음");
            Assert.IsNotNull(terrain.GetComponent("TerrainCollider"), "TerrainCollider 컴포넌트");
            Debug.Log($"[RaidMapTests] (-200,-200) 아래로 레이 → {terrainHit.collider.name} ({terrainHit.collider.GetType().Name})");

            // POI 5곳 (§8.1 이름)
            PointOfInterest[] pois = Object.FindObjectsByType<PointOfInterest>(FindObjectsSortMode.None);
            CollectionAssert.AreEquivalent(GddPoiNames, pois.Select(p => p.DisplayName).ToArray(), "§8.1 POI 5곳");
            foreach (PointOfInterest p in pois)
                Assert.IsTrue(NavMesh.SamplePosition(p.transform.position, out _, 5f, NavMesh.AllAreas), $"{p.Id}: NavMesh 위");
            // 실내 없음: 블록아웃 구조물(엄폐 외벽·잔해·차량)
            Assert.Greater(raid.Director.Covers.Count, 0, "엄폐 지점");

            // 스폰마다 활성 탈출구(spawnRules + EXT-01 항상)까지 NavMesh 경로 Complete
            RaidConfigSO config = raid.Config;
            var report = new List<string>();
            foreach (PlayerSpawnPoint spawn in Object.FindObjectsByType<PlayerSpawnPoint>(FindObjectsSortMode.None))
            {
                var active = new List<string>();
                ActiveExtractionPicker.Pick(config, spawn.Id, new System.Random(1), active);
                Assert.That(active.Count, Is.InRange(2, 3), $"{spawn.Id} 활성 탈출구 수 (§8.2 2~3)");
                CollectionAssert.Contains(active, "EXT-01");
                Assert.IsTrue(NavMesh.SamplePosition(spawn.FeetPosition, out NavMeshHit from, 2f, NavMesh.AllAreas), $"{spawn.Id} NavMesh");
                foreach (string id in active)
                {
                    Assert.IsTrue(NavMesh.SamplePosition(raid.Zone(id).transform.position, out NavMeshHit to, 2f, NavMesh.AllAreas), $"{id} NavMesh");
                    // 맵 밀도: 나무 줄기 구멍으로 NavMesh가 촘촘해져 긴 경로(약 900m)가 A* 노드 한도에서 PathPartial이 될 수 있다.
                    // 이 테스트는 "플레이어가 걸어서 탈출구에 닿는가"(연결성)라 부분 경로 끝에서 이어 찾는다 — 배치 확인과 같은 함수(NavPathProbe),
                    // 진짜로 끊기면 끝이 목표에 가까워지지 않아 실패. 적 경로(한 번의 질의)는 MapDensitySceneSmokeTests가 본다. 근거: 설계 §14.4.
                    Assert.IsTrue(NavPathProbe.TryConnected(from.position, to.position, new NavMeshPath(),
                        out float length, out _), $"{spawn.Id}→{id} 경로");
                    report.Add($"{spawn.Id}→{id} {length:0}m");
                }
            }
            Debug.Log("[RaidMapTests] 스폰→활성 탈출구 경로: " + string.Join(", ", report));

            // 경계 벽: 네 방향 가장자리 안쪽에서 바깥으로 밀어도 ±495m를 넘지 못한다 (CharacterController 실제 이동)
            var cc = raid.Motor.GetComponent<CharacterController>();
            var probes = new (Vector2 xz, Vector3 dir, string label)[]
            {
                (new Vector2(-488f, -150f), Vector3.left, "서"),
                (new Vector2(488f, 120f), Vector3.right, "동"),
                (new Vector2(-80f, 488f), Vector3.forward, "북"),
                (new Vector2(0f, -488f), Vector3.back, "남"),
            };
            foreach ((Vector2 xz, Vector3 dir, string label) in probes)
            {
                float groundY = terrain.SampleHeight(new Vector3(xz.x, 0f, xz.y)) + terrain.transform.position.y;
                raid.Placement.PlaceAt(new Vector3(xz.x, groundY + 0.3f, xz.y), Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg);
                yield return null;
                for (int i = 0; i < 60; i++)
                {
                    cc.Move(dir * 0.5f + Vector3.down * 0.2f);   // 30m 밀기 (벽까지 7m)
                    if (i % 10 == 0)
                        yield return null;
                }
                yield return null;
                Vector3 p = raid.Motor.transform.position;
                float along = Vector3.Dot(p, dir);
                Debug.Log($"[RaidMapTests] 경계 {label}: 밀기 후 {p} (바깥 방향 거리 {along:0.00}m)");
                Assert.Less(along, WallInnerM, $"{label}쪽 경계를 넘었다: {p}");
                // 바깥으로 쏜 레이가 495m 지점의 경계 벽(Bounds)에 맞는다 (지형 경사만이 아니라 벽이 막는다)
                Vector3 eye = new Vector3(xz.x, groundY + 3f, xz.y);
                RaycastHit[] hits = Physics.RaycastAll(eye, dir, 30f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                RaycastHit wall = hits.Where(h => h.collider.transform.root.name == "Bounds").OrderBy(h => h.distance).FirstOrDefault();
                Assert.IsNotNull(wall.collider, $"{label}쪽 경계 벽 없음");
                Assert.AreEqual(WallInnerM, Vector3.Dot(wall.point, dir), 0.1f, $"{label}쪽 벽 위치");
            }

            // 입력으로 걷기: 서쪽 스폰 근처에서 서쪽으로 질주 8초 → 경계 안
            using (var keyboard = new SimKeyboard())
            {
                float gy = terrain.SampleHeight(new Vector3(-470f, 0f, -150f)) + terrain.transform.position.y;
                raid.Placement.PlaceAt(new Vector3(-470f, gy + 0.1f, -150f), 270f);
                yield return null;
                keyboard.Hold(Key.W, Key.LeftShift);
                yield return M2Scene.WaitSeconds(8f);
                keyboard.Release(Key.W, Key.LeftShift);
                yield return null;
                Vector3 p = raid.Motor.transform.position;
                Debug.Log($"[RaidMapTests] 서쪽 질주 8초 후 {p}");
                Assert.Greater(p.x, -WallInnerM, "걸어서 맵 밖으로 나갔다");
                Assert.Less(p.x, -470f, "걷기 입력이 반영되지 않았다");
            }
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Map_Rule06_ShowsPoisAndActiveExtractionsOnly_NoPlayerPosition()
        {
            yield return M4Raid.Load(raid, "SP-N");   // 활성 EXT-01·02·04, 비활성 EXT-03
            yield return raid.RemoveEnemies();

            // 계약: IRaidInfo는 시간·지도 범위·POI·활성 탈출구뿐 (위치 멤버 없음)
            string[] members = typeof(IRaidInfo).GetProperties().Select(p => p.Name).OrderBy(n => n).ToArray();
            CollectionAssert.AreEqual(new[] { "ActiveExtractions", "ElapsedSec", "MapAreaXZ", "PointsOfInterest", "RemainingSec" }, members,
                "IRaidInfo 멤버 (RULE-06: 내 위치 추가 금지)");
            Assert.AreEqual(0, typeof(IRaidInfo).GetMethods().Count(m => !m.IsSpecialName), "IRaidInfo 메서드 없음");

            using (var keyboard = new SimKeyboard())
            {
                yield return keyboard.Tap(Key.M);
                Assert.AreEqual(ScreenKind.Map, raid.Presenter.Screens.Current, "M → 지도 (지도 아이템 소지)");
                Assert.AreEqual(1f, Time.timeScale, "지도는 세계를 멈추지 않는다");
                yield return null;
                List<(string label, string kind, float left, float top)> first = ReadMarkers(raid);

                string[] activeNames = raid.Config.extractions.Where(e => raid.Controller.ActiveExtractionIds.Contains(e.id))
                    .Select(e => e.displayName).ToArray();
                string[] inactiveNames = raid.Config.extractions.Where(e => !raid.Controller.ActiveExtractionIds.Contains(e.id))
                    .Select(e => e.displayName).ToArray();
                Assert.AreEqual(3, activeNames.Length);
                CollectionAssert.AreEquivalent(activeNames, first.Where(m => m.kind == MapScreenView.ExtractionClass).Select(m => m.label),
                    "활성 탈출구만 표시");
                foreach (string n in inactiveNames)
                    Assert.IsFalse(first.Any(m => m.label == n), $"비활성 탈출구 {n} 표시됨");
                CollectionAssert.AreEquivalent(GddPoiNames, first.Where(m => m.kind == MapScreenView.PoiClass).Select(m => m.label));
                Assert.AreEqual(GddPoiNames.Length + activeNames.Length, first.Count, "표식 = POI + 활성 탈출구 (내 위치 표식 없음)");

                // 지도 화면 전체에서 표식 외 위치 요소 없음
                VisualElement screen = raid.UiRoot.Q(MapScreenView.ScreenName);
                Assert.IsTrue(UiQuery.IsShown(screen));
                foreach (VisualElement e in screen.Query<VisualElement>().ToList())
                    foreach (string cls in e.GetClasses())
                        Assert.That(cls.ToLowerInvariant(), Does.Not.Contain("player").And.Not.Contain("self").And.Not.Contain("you"),
                            $"플레이어 표식으로 보이는 클래스 {cls}");
                string timeText = raid.UiRoot.Q<Label>(MapScreenView.TimeName).text;
                Assert.That(timeText, Does.StartWith("남은 시간: 약 ").And.EndWith("분"), "남은 시간 분 단위 대략치 (Q10)");

                // 플레이어를 멀리 옮기고 다시 열어도 표식은 그대로 (위치에 따라 바뀌는 요소 없음)
                yield return keyboard.Tap(Key.M);
                Assert.AreEqual(ScreenKind.None, raid.Presenter.Screens.Current);
                yield return raid.EnterZone("EXT-02");
                yield return keyboard.Tap(Key.M);
                Assert.AreEqual(ScreenKind.Map, raid.Presenter.Screens.Current);
                yield return null;
                CollectionAssert.AreEqual(first, ReadMarkers(raid), "플레이어 위치가 바뀌어도 지도는 같다");
                yield return keyboard.Tap(Key.M);

                // 지도 아이템이 없으면 열리지 않는다
                ItemInstance map = raid.Carried().FirstOrDefault(i => i.Definition.id == "ITM-MAP-01");
                Assert.IsNotNull(map, "시작 키트에 지도 (Q11)");
                Assert.IsTrue(raid.Inventory.Remove(map));
                yield return keyboard.Tap(Key.M);
                Assert.AreEqual(ScreenKind.None, raid.Presenter.Screens.Current, "지도 아이템 없으면 안 열림");
            }
        }

        private static List<(string label, string kind, float left, float top)> ReadMarkers(M4Raid raid)
        {
            VisualElement area = raid.UiRoot.Q(MapScreenView.AreaName);
            var list = new List<(string, string, float, float)>();
            foreach (VisualElement child in area.Children())
            {
                Assert.IsTrue(child.ClassListContains(MapScreenView.MarkerClass), "지도 영역의 자식은 표식만");
                string kind = child.ClassListContains(MapScreenView.ExtractionClass) ? MapScreenView.ExtractionClass
                    : child.ClassListContains(MapScreenView.PoiClass) ? MapScreenView.PoiClass : "?";
                Assert.AreNotEqual("?", kind, "POI·탈출구가 아닌 표식");
                string label = child.Q<Label>()?.text;
                list.Add((label, kind, child.style.left.value.value, child.style.top.value.value));
            }
            return list;
        }
    }
}
#endif
