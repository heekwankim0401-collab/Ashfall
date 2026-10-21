using System.Collections.Generic;
using Ashfall.AI;
using UnityEditor;
using UnityEngine;
using L = Ashfall.EditorTools.CrimsonBasinLayout;

namespace Ashfall.EditorTools
{
    // Crimson Basin 레이드 적 고정 배치 (§7, M4 설계 §1.7, TODO(GDD-?) Q12: 20명 = SCV 12 + RDR 8, 분대 9개).
    // M3 AI를 코드 변경 없이 재사용한다: 적 오브젝트는 M3SceneSetup.CreateEnemy, 순찰 경로는 M3SceneSetup.CreateRoute,
    // 분대 대형 자리는 M3SceneSetup.SquadSlots. 다른 점은 높이뿐 — 지형 위라 시작 위치·웨이포인트를 NavMesh(없으면 지면)에 붙인다.
    // **열린 씬**에 놓는다. NavMesh를 먼저 구워야 한다(M4MapSetup.BuildMap). 적·순찰 경로·AiDirector는 씬 최상위.
    // 엄폐 지점(CoverPoint)은 M4MapSetup이 블록아웃과 함께 만든다(AiDirector가 Awake에서 씬 전체를 모은다).
    public static class M4EnemySetup
    {
        private const string Tag = "[M4EnemySetup]";
        private const float SnapDistanceM = 3f;      // 지면 점 → NavMesh 허용 거리
        private const float OnMeshToleranceM = 0.5f; // 시작 위치 ↔ NavMesh 점 허용 차이(확인용)

        internal sealed class EnemyPlacementResult
        {
            public readonly List<GameObject> Enemies = new List<GameObject>();
            public readonly List<PatrolRoute> Routes = new List<PatrolRoute>();
            public int ScavengerCount;
            public int RaiderCount;
        }

        [MenuItem("Ashfall/Setup/M4/Place Crimson Basin Enemies (Open Scene)")]
        public static void PlaceEnemiesInOpenSceneMenu()
        {
            var terrain = Object.FindFirstObjectByType<Terrain>();
            if (terrain == null)
            {
                Debug.LogError($"{Tag} 열린 씬에 Terrain이 없다. 먼저 'Build Crimson Basin Map (Open Scene)'.");
                return;
            }
            EnsureEnemyMaterials();
            AssetDatabase.SaveAssets();
            AiDirector director = Object.FindFirstObjectByType<AiDirector>();
            if (director == null)
                director = CreateDirector();
            EnemyPlacementResult result = PlaceEnemies(terrain, director);
            ValidateEnemyStarts(result, out _);
        }

        // M3와 같은 적 몸 색 재질(없을 때만). NewScene 전에 불러도 되지만 PlaceEnemies가 경로로 다시 불러온다.
        internal static void EnsureEnemyMaterials()
        {
            M3SceneSetup.CreateColorMaterial(M3SceneSetup.ScvMaterialPath, "M3_EnemySCV", M3SceneSetup.ScvColor);
            M3SceneSetup.CreateColorMaterial(M3SceneSetup.RdrMaterialPath, "M3_EnemyRDR", M3SceneSetup.RdrColor);
        }

        // 최상위 AiDirector (config = M3 AiConfig). 3단계가 이미 만들었으면 그것을 넘긴다.
        internal static AiDirector CreateDirector()
        {
            var director = new GameObject(nameof(AiDirector)).AddComponent<AiDirector>();
            SetupUtil.SetFields(director, ("config", SetupUtil.Load<AiConfigSO>(M3DataSetup.AiConfigPath)));
            return director;
        }

        // CrimsonBasinLayout.Squads를 열린 씬에 놓는다. 적 seed = 배치 순번 + 1(결정적, M3와 같음).
        internal static EnemyPlacementResult PlaceEnemies(Terrain terrain, AiDirector director)
        {
            var result = new EnemyPlacementResult();
            var ctx = new M3SceneSetup.SceneContext
            {
                Scv = SetupUtil.Load<EnemyArchetypeSO>(M3DataSetup.ScvPath),
                Rdr = SetupUtil.Load<EnemyArchetypeSO>(M3DataSetup.RdrPath),
                ScvMaterial = SetupUtil.Load<Material>(M3SceneSetup.ScvMaterialPath),
                RdrMaterial = SetupUtil.Load<Material>(M3SceneSetup.RdrMaterialPath),
                Director = director,
            };

            foreach (L.SquadPlan plan in L.Squads)
            {
                bool raider = plan.Kind == L.EnemyKind.Raider;
                EnemyArchetypeSO archetype = raider ? ctx.Rdr : ctx.Scv;
                if (plan.RouteXZ != null && plan.RouteXZ.Length >= 2)
                    PlaceSquadOnRoute(ctx, terrain, plan, archetype, result);
                else
                    PlaceSquadAtSpots(ctx, terrain, plan, archetype, result);
                if (raider)
                    result.RaiderCount += plan.Count;
                else
                    result.ScavengerCount += plan.Count;
            }

            Debug.Log($"{Tag} 적 {result.Enemies.Count}명 배치(SCV {result.ScavengerCount}, RDR {result.RaiderCount}), " +
                      $"순찰 경로 {result.Routes.Count}");
            return result;
        }

        // M3SceneSetup.CreateSquad와 같은 배치: 리더 = 경로 첫 지점, 대원 = 진행 방향 기준 뒤쪽 좌우 대형 자리.
        private static void PlaceSquadOnRoute(M3SceneSetup.SceneContext ctx, Terrain terrain, L.SquadPlan plan,
            EnemyArchetypeSO archetype, EnemyPlacementResult result)
        {
            PatrolRoute route = M3SceneSetup.CreateRoute($"PatrolRoute_{plan.Name}", plan.RouteXZ, Vector3.zero);
            // M3 CreateRoute는 y 0에 둔다 → 웨이포인트를 NavMesh에 붙인다(waypoints 배열은 같은 Transform을 가리킨다).
            Transform routeRoot = route.transform;
            for (int i = 0; i < routeRoot.childCount; i++)
            {
                Vector3 p = routeRoot.GetChild(i).position;
                routeRoot.GetChild(i).position = M4MapSetup.SnapToNavMesh(terrain, new Vector2(p.x, p.z), SnapDistanceM);
            }
            result.Routes.Add(route);

            Vector2 start = plan.RouteXZ[0];
            Vector2 next = plan.RouteXZ[1];
            var heading = new Vector3(next.x - start.x, 0f, next.y - start.y);
            Quaternion facing = Quaternion.LookRotation(heading.normalized, Vector3.up);
            for (int i = 0; i < plan.Count; i++)
            {
                Vector2 slot = M3SceneSetup.SquadSlots[i % M3SceneSetup.SquadSlots.Length];
                Vector3 offset = facing * new Vector3(slot.x, 0f, slot.y);
                Vector3 position = M4MapSetup.SnapToNavMesh(terrain, start + new Vector2(offset.x, offset.z), SnapDistanceM);
                result.Enemies.Add(M3SceneSetup.CreateEnemy(ctx, $"{plan.Name}{i + 1}", archetype, position,
                    facing.eulerAngles.y, route, plan.SquadId, plan.Loadouts[i]));
            }
        }

        // M3 RDR 방식: 경로 없이 정해진 자리에 정지 배치.
        private static void PlaceSquadAtSpots(M3SceneSetup.SceneContext ctx, Terrain terrain, L.SquadPlan plan,
            EnemyArchetypeSO archetype, EnemyPlacementResult result)
        {
            if (plan.SpotsXZ == null || plan.SpotsXZ.Length < plan.Count)
            {
                Debug.LogError($"{Tag} 분대 {plan.Name}: 경로도 자리도 부족하다.");
                return;
            }
            for (int i = 0; i < plan.Count; i++)
            {
                Vector3 position = M4MapSetup.SnapToNavMesh(terrain, plan.SpotsXZ[i], SnapDistanceM);
                result.Enemies.Add(M3SceneSetup.CreateEnemy(ctx, $"{plan.Name}{i + 1}", archetype, position,
                    plan.YawDeg, null, plan.SquadId, plan.Loadouts[i]));
            }
        }

        // 확인(설계 §1.7·§2.4): 수(20 = 12 + 8), 모든 시작 위치·웨이포인트가 NavMesh 위, 모든 스폰에서 MinEnemyDistanceFromSpawnM 이상.
        internal static bool ValidateEnemyStarts(EnemyPlacementResult placement, out string report)
        {
            var sb = new System.Text.StringBuilder();
            bool ok = placement.Enemies.Count == L.ExpectedEnemyCount
                      && placement.ScavengerCount == L.ExpectedScavengerCount
                      && placement.RaiderCount == L.ExpectedRaiderCount;
            sb.AppendLine($"  적 {placement.Enemies.Count}/{L.ExpectedEnemyCount} (SCV {placement.ScavengerCount}, RDR {placement.RaiderCount})");

            float nearest = float.MaxValue;
            string nearestPair = "";
            foreach (GameObject enemy in placement.Enemies)
            {
                Vector3 p = enemy.transform.position;
                if (!IsOnNavMesh(p))
                {
                    sb.AppendLine($"  {enemy.name}: NavMesh 밖 {p}");
                    ok = false;
                }
                if (enemy.transform.parent != null)
                {
                    sb.AppendLine($"  {enemy.name}: 최상위가 아니다");
                    ok = false;
                }
                foreach (L.Spawn spawn in L.Spawns)
                {
                    float d = Vector2.Distance(new Vector2(p.x, p.z), spawn.PositionXZ);
                    if (d < nearest)
                    {
                        nearest = d;
                        nearestPair = $"{enemy.name} ↔ {spawn.Id}";
                    }
                }
            }
            if (nearest < L.MinEnemyDistanceFromSpawnM)
                ok = false;
            sb.AppendLine($"  스폰과 가장 가까운 적: {nearest:0}m ({nearestPair}), 기준 ≥ {L.MinEnemyDistanceFromSpawnM}m");

            foreach (PatrolRoute route in placement.Routes)
                for (int i = 0; i < route.transform.childCount; i++)
                {
                    Vector3 p = route.transform.GetChild(i).position;
                    if (!IsOnNavMesh(p))
                    {
                        sb.AppendLine($"  {route.name}/{route.transform.GetChild(i).name}: NavMesh 밖 {p}");
                        ok = false;
                    }
                }

            report = sb.ToString();
            if (ok)
                Debug.Log($"{Tag} 적 배치 확인 통과\n{report}");
            else
                Debug.LogError($"{Tag} 적 배치 확인 실패\n{report}");
            return ok;
        }

        private static bool IsOnNavMesh(Vector3 p) =>
            UnityEngine.AI.NavMesh.SamplePosition(p, out UnityEngine.AI.NavMeshHit hit, 1f, UnityEngine.AI.NavMesh.AllAreas)
            && Vector3.Distance(hit.position, p) <= OnMeshToleranceM;
    }
}
