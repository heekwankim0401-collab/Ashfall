using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Ashfall.AI;
using Ashfall.Inventory;
using Ashfall.Raid;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Ashfall.EditorTools
{
    // 맵 배치 확인 공용 (맵 개선 §6 → 맵 밀도 §7이 재사용, docs/design/MapDensity_design.md §6.2 "공용화").
    // MapPolishSceneApply에 있던 겹침·회피·저격 시선 검사, 내부 막기 볼륨, NavMesh 상태 비교·되돌리기, 엄폐 지점 정리를 그대로 옮겼다.
    // 맵 개선 동작은 바뀌지 않는다(기본 규칙 = 맵 개선 규칙, 재실행 바이트 동일로 확인). 맵 밀도는 PlacementRules로 규칙을 더한다.
    internal static class MapPlacementChecks
    {
        private const string Tag = "[MapPlacementChecks]";

        internal const float CoverOnMeshM = 0.5f;          // 엄폐 지점 ↔ NavMesh 허용 거리
        internal const float AgentOnMeshM = 2f;            // 적 시작·순찰 지점·상자 ↔ NavMesh
        internal const float LocalAgentPathM = 250f;       // 적 근거리 경로 확인 반경 (교전·추적·순찰 거리, 맵 밀도 리뷰 M1)
        internal const float CarvedProbeM = 0.3f;          // 구조물 중심 지면에서 이 안에 NavMesh가 있으면 "구멍 없음"(옛 NavMesh)
        private const float CoverProbeRadiusM = 0.3f;      // 새 엄폐 지점·peek 자리가 다른 콜라이더 안인지 (캡슐)
        private const int OverlapBufferSize = 256;
        internal const string NotWalkableArea = "Not Walkable";
        private const float NavBlockMinSizeM = 1f;         // 세 변이 모두 이 이상인 콜라이더만 내부 막기 볼륨
        private const float NavBlockShrink = 0.98f;        // 볼륨을 콜라이더보다 살짝 작게 — 바깥 지면을 건드리지 않게

        // 맵 개선에 없던 추가 규칙 (맵 밀도 §7 1). 기본값 = 끔(맵 개선과 같은 검사).
        internal struct PlacementRules
        {
            public float PatrolSegmentClearM;   // > 0이면 순찰 구간 선분에서 이 거리(수평, 콜라이더 상자 가장자리 기준)
            public Vector2? KeepOutCenter;      // 보스 거점 등 — 이 원 안에 새 콜라이더 금지
            public float KeepOutRadiusM;
            public string KeepOutName;
            public float PointScale;            // > 0이면 엄폐·상자·적·순찰 지점 최소 거리(2.5m)에 곱한다(맵 밀도 ③ 상자 0.6). 0 = 그대로. 스폰·탈출·스위치는 늘 그대로
        }

        // ---------- 되돌리기 ----------

        internal static byte[] ReadBytes(string assetPath)
        {
            string file = Path.GetFullPath(assetPath);
            return File.Exists(file) ? File.ReadAllBytes(file) : null;
        }

        internal static void RestoreNavMesh(NavMeshSurface surface, string navPath, byte[] backup)
        {
            surface.RemoveData();
            File.WriteAllBytes(Path.GetFullPath(navPath), backup);
            AssetDatabase.ImportAsset(navPath, ImportAssetOptions.ForceUpdate);
            surface.navMeshData = AssetDatabase.LoadAssetAtPath<NavMeshData>(navPath);
            surface.AddData();
            EditorUtility.SetDirty(surface);
        }

        // ---------- NavMesh 수정자 ----------

        internal static void MarkNotWalkable(Transform root, int notWalkable)
        {
            var modifier = root.gameObject.AddComponent<NavMeshModifier>();
            modifier.overrideArea = true;
            modifier.area = notWalkable;
        }

        // Recast는 닫힌 메시 속을 채우지 않아, 사람 키보다 높은 속 찬 상자(컨테이너·창고·버스·교각·동체) 안쪽 지면에 닿을 수 없는 NavMesh 섬이 생긴다.
        // 세 변이 모두 NavBlockMinSizeM 이상인 콜라이더마다 같은 자리·회전의 Not Walkable 볼륨을 둔다(프리팹 밖 형제 오브젝트 — 프리팹을 바꿔도 남지 않게
        // Rebuild가 다시 만든다). 얇은 벽·막대는 가장자리 침식으로 이미 구멍이 난다. 만든 수.
        internal static int AddNavBlocks(Transform[] newRoots, int notWalkable)
        {
            int count = 0;
            var colliders = new List<Collider>();
            foreach (Transform root in newRoots)
                foreach (Collider c in root.GetComponentsInChildren<Collider>(true))
                    if (!c.isTrigger)
                        colliders.Add(c);
            foreach (Collider c in colliders)
            {
                Obb obb = Obb.Of(c);
                Vector3 size = obb.HalfExtents * 2f;
                if (Mathf.Min(size.x, Mathf.Min(size.y, size.z)) < NavBlockMinSizeM)
                    continue;
                Transform owner = c.transform.parent;
                while (owner != null && PrefabUtility.IsPartOfPrefabInstance(owner.gameObject))
                    owner = owner.parent;   // 프리팹 인스턴스 밖(래퍼)에 둔다 — 인스턴스에 덧붙임(override)을 만들지 않게
                var go = new GameObject($"NavBlock_{c.name}");
                go.transform.SetParent(owner, false);
                go.transform.SetPositionAndRotation(obb.Center, obb.Rotation);
                var volume = go.AddComponent<NavMeshModifierVolume>();
                volume.center = Vector3.zero;
                volume.size = size * NavBlockShrink;
                volume.area = notWalkable;
                count++;
            }
            return count;
        }

        // 구조물(속을 채운 도형) 중심의 지면 점 근처에 NavMesh가 없어야 한다 = 구조물이 NavMesh에 구멍으로 반영됐다 (맵 개선 리뷰 H2·M2).
        internal static bool CheckCarved(Transform structureGroup, Terrain terrain, out string report)
        {
            int carved = 0, total = 0;
            var stale = new List<string>();
            foreach (Transform structure in structureGroup)
            {
                total++;
                Vector3 ground = M4MapSetup.GroundPoint(terrain, new Vector2(structure.position.x, structure.position.z));
                if (NavMesh.SamplePosition(ground, out NavMeshHit _, CarvedProbeM, NavMesh.AllAreas))
                    stale.Add(structure.name);
                else
                    carved++;
            }
            report = $"  구조물 자리 NavMesh 구멍 {carved}/{total}" + (stale.Count > 0 ? $" — 구멍 없음: {string.Join(", ", stale)}\n" : "\n");
            return total > 0 && carved == total;
        }

        // ---------- 배치 확인 (맵 개선 §6 1, 맵 밀도 §7 1) ----------

        internal static bool IsUnder(Transform t, Transform[] roots)
        {
            foreach (Transform root in roots)
                if (t.IsChildOf(root))
                    return true;
            return false;
        }

        // 새 콜라이더(OBB)가 기존 콜라이더(지형·트리거 제외)와 겹치지 않는가, 기존 지점들에서 떨어져 있는가, 저격 시선을 막지 않는가.
        internal static void CheckPlacement(Scene scene, Transform[] newRoots, Terrain terrain, StringBuilder problems) =>
            CheckPlacement(scene, newRoots, terrain, problems, default);

        internal static void CheckPlacement(Scene scene, Transform[] newRoots, Terrain terrain, StringBuilder problems, PlacementRules rules)
        {
            var newColliders = new List<Collider>();
            foreach (Transform root in newRoots)
                foreach (Collider c in root.GetComponentsInChildren<Collider>(true))
                    if (!c.isTrigger)
                        newColliders.Add(c);

            // 기존 지점 (이름, 위치, 최소 거리)
            var points = new List<(string name, Vector3 position, float minDistance)>();
            var segments = new List<(string name, Vector3 a, Vector3 b)>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (CoverPoint cover in root.GetComponentsInChildren<CoverPoint>(true))
                    if (!IsUnder(cover.transform, newRoots))
                        points.Add(($"엄폐 {cover.transform.parent?.name}/{cover.name}", cover.transform.position, MapPolishLayout.MinDistanceFromPointsM));
                foreach (LootContainer crate in root.GetComponentsInChildren<LootContainer>(true))
                    if (!IsUnder(crate.transform, newRoots))   // 맵 밀도 ③: 확인 대상인 새 상자 자신은 지점이 아니다(이전 호출자는 새 루트에 상자가 없어 같다)
                        points.Add(($"상자 {crate.name}", crate.transform.position, MapPolishLayout.MinDistanceFromPointsM));
                foreach (EnemyAgent enemy in root.GetComponentsInChildren<EnemyAgent>(true))
                    points.Add(($"적 {enemy.name}", enemy.transform.position, MapPolishLayout.MinDistanceFromPointsM));
                foreach (PatrolRoute route in root.GetComponentsInChildren<PatrolRoute>(true))
                {
                    IReadOnlyList<Vector3> routePoints = route.Points;
                    foreach (Vector3 p in routePoints)
                        points.Add(($"순찰 {route.name}", p, MapPolishLayout.MinDistanceFromPointsM));
                    if (rules.PatrolSegmentClearM > 0f && routePoints.Count >= 2)
                        for (int i = 0; i < routePoints.Count; i++)
                            segments.Add(($"순찰 구간 {route.name}#{i}", routePoints[i], routePoints[(i + 1) % routePoints.Count]));
                }
                foreach (PlayerSpawnPoint spawn in root.GetComponentsInChildren<PlayerSpawnPoint>(true))
                    points.Add(($"스폰 {spawn.Id}", spawn.transform.position, MapPolishLayout.MinDistanceFromSpawnExtM));
                foreach (ExtractionZone zone in root.GetComponentsInChildren<ExtractionZone>(true))
                    points.Add(($"탈출 {zone.Id}", zone.transform.position, MapPolishLayout.MinDistanceFromSpawnExtM));
                foreach (PowerSwitch powerSwitch in root.GetComponentsInChildren<PowerSwitch>(true))
                    points.Add(("전원 스위치", powerSwitch.transform.position, MapPolishLayout.MinDistanceFromSwitchM));
            }

            var hits = new Collider[OverlapBufferSize];
            var reported = new HashSet<string>();
            foreach (Collider c in newColliders)
            {
                Obb obb = Obb.Of(c);
                int count = Physics.OverlapBoxNonAlloc(obb.Center, obb.HalfExtents, hits, obb.Rotation, Physics.AllLayers, QueryTriggerInteraction.Ignore);
                if (count >= hits.Length)
                    problems.AppendLine($"  겹침 검사 버퍼 가득 참({hits.Length}): {Owner(c)} — 결과가 잘렸을 수 있다");
                for (int i = 0; i < count; i++)
                {
                    Collider other = hits[i];
                    if (other is TerrainCollider || IsUnder(other.transform, newRoots))
                        continue;
                    string key = $"{Owner(c)} ↔ {Owner(other)}";
                    if (reported.Add(key))
                        problems.AppendLine($"  겹침: {key}");
                }
                foreach (var point in points)
                {
                    // 지면 높이 지점이 OBB 아래로 빠지지 않게 수평 거리로 본다 (구조물 바닥이 지면 아래로 묻혀 있다)
                    float d = obb.HorizontalDistance(point.position);
                    float min = rules.PointScale > 0f && point.minDistance <= MapPolishLayout.MinDistanceFromPointsM ? point.minDistance * rules.PointScale : point.minDistance;
                    if (d < min)
                    {
                        string key = $"{Owner(c)} ↔ {point.name}";
                        if (reported.Add(key))
                            problems.AppendLine($"  가까움: {key} {d:0.0}m < {min}m (지점 {point.position.x:0.0}, {point.position.z:0.0})");
                    }
                }
                foreach (var segment in segments)
                {
                    float d = obb.HorizontalDistanceToSegment(segment.a, segment.b);
                    if (d < rules.PatrolSegmentClearM)
                    {
                        string key = $"{Owner(c)} ↔ {segment.name}";
                        if (reported.Add(key))
                            problems.AppendLine($"  순찰 통로 위: {key} {d:0.0}m < {rules.PatrolSegmentClearM}m");
                    }
                }
                if (rules.KeepOutCenter.HasValue)
                {
                    Vector2 k = rules.KeepOutCenter.Value;
                    float d = obb.HorizontalDistance(new Vector3(k.x, obb.Center.y, k.y));
                    if (d < rules.KeepOutRadiusM && reported.Add($"{Owner(c)} ↔ {rules.KeepOutName}"))
                        problems.AppendLine($"  {rules.KeepOutName} {rules.KeepOutRadiusM}m 안: {Owner(c)} {d:0.0}m");
                }
            }

            // 저격 시선 (M7 퍼치 → 감시 지점)을 새 콜라이더가 막지 않는다 (3D 레이)
            foreach ((Vector2 from, Vector2 to) in MapPolishLayout.SniperSightLines())
            {
                Vector3 eye = M4MapSetup.GroundPoint(terrain, from) + Vector3.up * MapPolishLayout.SniperEyeHeightM;
                Vector3 target = M4MapSetup.GroundPoint(terrain, to) + Vector3.up * MapPolishLayout.SniperTargetHeightM;
                foreach (RaycastHit hit in Physics.RaycastAll(eye, (target - eye).normalized, Vector3.Distance(eye, target),
                             Physics.AllLayers, QueryTriggerInteraction.Ignore))
                    if (IsUnder(hit.collider.transform, newRoots))
                        problems.AppendLine($"  저격 시선 {from}→{to}을 {Owner(hit.collider)}가 막는다");
            }
        }

        // 맵 밀도 ③이 만든 루트 지점(새 상자 LootCrate_*·트렁크)인가
        internal static bool IsMapDensityLootPoint(LootContainer c)
        {
            var slot = c.GetComponent<LootSpawnSlot>();
            return slot != null && (slot.Kind == LootSlotKind.Trunk || (slot.SlotKey ?? "").StartsWith(MapDensityLootLayout.NamePrefix, System.StringComparison.Ordinal));
        }

        // 구조물 이름 (그룹 바로 아래 조상)
        internal static string Owner(Collider c)
        {
            Transform t = c.transform;
            while (t.parent != null && t.parent.parent != null && t.parent.parent.name != M4MapSetup.EnvironmentName)
                t = t.parent;
            return t.parent != null ? $"{t.parent.name}/{t.name}" : t.name;
        }

        // 새 엄폐 지점 자리 또는 peek 자리가 다른 콜라이더 안이면 뺀다(서 있을 수 없는 자리, 맵 개선 리뷰 L2). 뺀 수.
        internal static int RemoveCoversInsideColliders(List<CoverPoint> covers, string logTag)
        {
            int removed = 0;
            var hits = new Collider[OverlapBufferSize];
            for (int i = covers.Count - 1; i >= 0; i--)
            {
                CoverPoint cover = covers[i];
                Vector3 p = cover.transform.position;
                Vector3 peekLocal = new SerializedObject(cover).FindProperty("peekLocalOffset").vector3Value;
                bool blocked = Blocked(p, hits) || (peekLocal != Vector3.zero && Blocked(cover.transform.TransformPoint(peekLocal), hits));
                if (!blocked)
                    continue;
                Debug.Log($"{logTag} 엄폐 지점 뺌(콜라이더 안): {cover.transform.parent?.name}/{cover.name} {p}");
                Object.DestroyImmediate(cover.gameObject);
                covers.RemoveAt(i);
                removed++;
            }
            return removed;
        }

        private static bool Blocked(Vector3 groundPoint, Collider[] hits)
        {
            int count = Physics.OverlapCapsuleNonAlloc(groundPoint + Vector3.up * (CoverProbeRadiusM + 0.15f), groundPoint + Vector3.up * 1.4f,
                CoverProbeRadiusM, hits, Physics.AllLayers, QueryTriggerInteraction.Ignore);
            for (int h = 0; h < count; h++)
                if (!(hits[h] is TerrainCollider))
                    return true;
            return false;
        }

        // 굽기 뒤 NavMesh 밖이거나 기준 스폰에서 닿을 수 없는 새 엄폐 지점을 뺀다(맵 개선 리뷰 M1). 뺀 수.
        internal static int RemoveCoversOffMesh(List<CoverPoint> covers, Vector3? reference, string logTag)
        {
            int removed = 0;
            var path = new NavMeshPath();
            for (int i = covers.Count - 1; i >= 0; i--)
            {
                Vector3 p = covers[i].transform.position;
                bool ok = NavMesh.SamplePosition(p, out NavMeshHit hit, CoverOnMeshM, NavMesh.AllAreas)
                          && (reference == null || M4MapSetup.TryPath(reference.Value, hit.position, path));
                if (ok)
                    continue;
                Debug.Log($"{logTag} 엄폐 지점 뺌(NavMesh 밖·도달 불가): {covers[i].transform.parent?.name}/{covers[i].name} {p}");
                Object.DestroyImmediate(covers[i].gameObject);
                covers.RemoveAt(i);
                removed++;
            }
            return removed;
        }

        // 회전한 상자 (BoxCollider = 그 상자, MeshCollider(원통) = 메시 경계 상자, 그 밖 = 월드 경계 상자)
        internal readonly struct Obb
        {
            public readonly Vector3 Center;
            public readonly Vector3 HalfExtents;
            public readonly Quaternion Rotation;

            private Obb(Vector3 center, Vector3 halfExtents, Quaternion rotation)
            {
                Center = center;
                HalfExtents = halfExtents;
                Rotation = rotation;
            }

            public static Obb Of(Collider c)
            {
                Transform t = c.transform;
                Vector3 scale = t.lossyScale;
                Vector3 localCenter, localSize;
                if (c is BoxCollider box)
                {
                    localCenter = box.center;
                    localSize = box.size;
                }
                else if (c is MeshCollider mesh && mesh.sharedMesh != null)
                {
                    localCenter = mesh.sharedMesh.bounds.center;
                    localSize = mesh.sharedMesh.bounds.size;
                }
                else
                {
                    return new Obb(c.bounds.center, c.bounds.extents, Quaternion.identity);
                }
                Vector3 half = new Vector3(Mathf.Abs(localSize.x * scale.x), Mathf.Abs(localSize.y * scale.y), Mathf.Abs(localSize.z * scale.z)) * 0.5f;
                return new Obb(t.TransformPoint(localCenter), half, t.rotation);
            }

            // 점에서 상자까지의 수평 거리 (상자의 수평 투영을 상자 높이 전체로 본다)
            public float HorizontalDistance(Vector3 point)
            {
                Vector3 local = Quaternion.Inverse(Rotation) * (point - Center);
                Vector3 clamped = new Vector3(
                    Mathf.Clamp(local.x, -HalfExtents.x, HalfExtents.x),
                    Mathf.Clamp(local.y, -HalfExtents.y, HalfExtents.y),
                    Mathf.Clamp(local.z, -HalfExtents.z, HalfExtents.z));
                Vector3 nearest = Center + Rotation * clamped;
                return Vector2.Distance(new Vector2(point.x, point.z), new Vector2(nearest.x, nearest.z));
            }

            // 선분까지의 수평 거리 (선분을 0.25m 간격으로 샘플 — 확인용이라 근사로 충분)
            public float HorizontalDistanceToSegment(Vector3 a, Vector3 b)
            {
                float length = Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
                int steps = Mathf.Max(1, Mathf.CeilToInt(length / 0.25f));
                float best = float.MaxValue;
                for (int i = 0; i <= steps; i++)
                    best = Mathf.Min(best, HorizontalDistance(Vector3.Lerp(a, b, i / (float)steps)));
                return best;
            }
        }

        // ---------- NavMesh 상태 (맵 개선 §6 2, 맵 밀도 §7 2) ----------

        // 키는 인스턴스 ID (맵 개선 리뷰 L9 — 이름 중복에 영향받지 않게). 도달성 = 기준 스폰(첫 PlayerSpawnPoint)에서 경로 완성(리뷰 M1).
        // 순찰 구간은 경로 길이도 기록한다(맵 밀도 §7 2 "경로 길이 증가 ≤ 20%").
        internal sealed class NavState
        {
            private readonly HashSet<int> allCovers = new HashSet<int>();
            private readonly Dictionary<int, Component> objects = new Dictionary<int, Component>();   // 실패 줄의 이름·위치용
            private readonly HashSet<int> coversOnMesh = new HashSet<int>();     // 굽기 전부터 있던 엄폐 지점 중 NavMesh 위
            private readonly HashSet<int> agentsOk = new HashSet<int>();
            private readonly HashSet<int> agentsReachable = new HashSet<int>();
            private readonly Dictionary<string, float> segmentsOk = new Dictionary<string, float>();   // 키 → 경로 길이
            private readonly HashSet<int> cratesOk = new HashSet<int>();
            private readonly HashSet<int> cratesReachable = new HashSet<int>();
            private readonly HashSet<string> localOk = new HashSet<string>();   // 적 시작점 → 근거리 목표, 한 번의 질의로 완성
            private int coverTotal, agentTotal, segmentTotal, crateTotal, localTotal;

            public Vector3? Reference { get; private set; }
            public int CoverTotal => coverTotal;

            public string Summary =>
                $"엄폐 {coversOnMesh.Count}/{coverTotal} NavMesh 위, 적 {agentsOk.Count}/{agentTotal}(스폰에서 도달 {agentsReachable.Count}), " +
                $"순찰 구간 {segmentsOk.Count}/{segmentTotal} 경로 완성(한 번의 질의), 적 근거리 경로 {localOk.Count}/{localTotal}(≤ {LocalAgentPathM}m, 한 번의 질의), " +
                $"상자 {cratesOk.Count}/{crateTotal}(스폰에서 도달 {cratesReachable.Count})";

            // previous가 있으면 엄폐 지점은 그때 있던 것만 센다(새 엄폐 지점은 따로 정리했다).
            public static NavState Capture(Scene scene, NavState previous)
            {
                var state = new NavState();
                var path = new NavMeshPath();
                foreach (GameObject root in scene.GetRootGameObjects())
                    foreach (PlayerSpawnPoint spawn in root.GetComponentsInChildren<PlayerSpawnPoint>(true))
                        if (state.Reference == null && NavMesh.SamplePosition(spawn.transform.position, out NavMeshHit sh, AgentOnMeshM, NavMesh.AllAreas))
                            state.Reference = sh.position;

                // 스폰 → 적·상자 = 플레이어가 걸어서 닿는가(연결성, 이어 찾기 허용)
                bool Reachable(Vector3 to) => state.Reference != null && M4MapSetup.TryPath(state.Reference.Value, to, path);
                var enemies = new List<(int id, Vector3 p)>();
                var localTargets = new List<(string key, Vector3 p)>();

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (CoverPoint cover in root.GetComponentsInChildren<CoverPoint>(true))
                    {
                        int id = cover.GetInstanceID();
                        if (previous != null && !previous.allCovers.Contains(id))
                            continue;
                        state.allCovers.Add(id);
                        state.objects[id] = cover;
                        state.coverTotal++;
                        if (NavMesh.SamplePosition(cover.transform.position, out NavMeshHit _, CoverOnMeshM, NavMesh.AllAreas))
                            state.coversOnMesh.Add(id);
                    }
                    foreach (EnemyAgent enemy in root.GetComponentsInChildren<EnemyAgent>(true))
                    {
                        state.agentTotal++;
                        state.objects[enemy.GetInstanceID()] = enemy;
                        if (!NavMesh.SamplePosition(enemy.transform.position, out NavMeshHit eh, AgentOnMeshM, NavMesh.AllAreas))
                            continue;
                        state.agentsOk.Add(enemy.GetInstanceID());
                        enemies.Add((enemy.GetInstanceID(), eh.position));
                        localTargets.Add(($"적{enemy.GetInstanceID()}", eh.position));
                        if (Reachable(eh.position))
                            state.agentsReachable.Add(enemy.GetInstanceID());
                    }
                    foreach (PatrolRoute route in root.GetComponentsInChildren<PatrolRoute>(true))
                    {
                        state.objects[route.GetInstanceID()] = route;
                        IReadOnlyList<Vector3> points = route.Points;
                        for (int i = 0; i < points.Count && points.Count >= 2; i++)
                        {
                            state.segmentTotal++;
                            Vector3 a = points[i], b = points[(i + 1) % points.Count];
                            // 순찰은 NavMeshAgent가 실제로 받는 경로 — 한 번의 질의로 완성돼야 한다(부분 경로면 에이전트가 끝에서 멈춘다, 리뷰 M1)
                            if (NavMesh.SamplePosition(a, out NavMeshHit ha, AgentOnMeshM, NavMesh.AllAreas)
                                && NavMesh.SamplePosition(b, out NavMeshHit hb, AgentOnMeshM, NavMesh.AllAreas)
                                && NavPathProbe.SingleQueryComplete(ha.position, hb.position, path, out float length))
                                state.segmentsOk[$"{route.GetInstanceID()}#{i}"] = length;
                            if (NavMesh.SamplePosition(a, out NavMeshHit hp, AgentOnMeshM, NavMesh.AllAreas))
                                localTargets.Add(($"순찰{route.GetInstanceID()}#{i}", hp.position));
                        }
                    }
                    foreach (LootContainer crate in root.GetComponentsInChildren<LootContainer>(true))
                    {
                        // 맵 밀도 ③ 루트 지점(새 상자·트렁크)은 빼고 M4 상자만 — 적 경로 목표가 아니고(트렁크 중심은 차체 안),
                        // 플레이어 도달은 MapDensityLootApply가 접근점에서 확인한다. ③ 전에는 모든 컨테이너가 M4 상자라 결과가 같다.
                        if (IsMapDensityLootPoint(crate))
                            continue;
                        state.crateTotal++;
                        state.objects[crate.GetInstanceID()] = crate;
                        if (!NavMesh.SamplePosition(crate.transform.position, out NavMeshHit ch, AgentOnMeshM, NavMesh.AllAreas))
                            continue;
                        state.cratesOk.Add(crate.GetInstanceID());
                        localTargets.Add(($"상자{crate.GetInstanceID()}", ch.position));
                        if (Reachable(ch.position))
                            state.cratesReachable.Add(crate.GetInstanceID());
                    }
                }
                // 적 시작점 → 근거리(LocalAgentPathM) 다른 적·순찰 지점·상자: 교전·추적·순찰 거리에서 에이전트가 받는 경로 (한 번의 질의)
                foreach ((int id, Vector3 p) enemy in enemies)
                    foreach ((string key, Vector3 p) target in localTargets)
                    {
                        if (target.key == $"적{enemy.id}" || Vector3.Distance(enemy.p, target.p) > LocalAgentPathM)
                            continue;
                        state.localTotal++;
                        if (NavPathProbe.SingleQueryComplete(enemy.p, target.p, path, out _))
                            state.localOk.Add($"{enemy.id}>{target.key}");
                    }
                return state;
            }

            // before에서 되던 것이 지금 안 되면 실패 줄
            public string Regressions(NavState before)
            {
                var sb = new StringBuilder();
                foreach (int id in before.coversOnMesh)
                    if (!coversOnMesh.Contains(id))
                        sb.AppendLine($"  엄폐 지점이 NavMesh에서 떨어졌다: {Describe(id)}");
                Lost(sb, before.agentsOk, agentsOk, "적 시작점이 NavMesh 밖");
                Lost(sb, before.agentsReachable, agentsReachable, "적 시작점에 스폰에서 닿을 수 없다");
                foreach (string key in before.segmentsOk.Keys)
                    if (!segmentsOk.ContainsKey(key))
                        sb.AppendLine($"  순찰 구간 경로 끊김: {DescribeSegment(key)}");
                foreach (string key in before.localOk)
                    if (!localOk.Contains(key))
                        sb.AppendLine($"  적 근거리 경로가 한 번의 질의로 완성되지 않는다(에이전트가 멈춘다): {Describe(int.Parse(key.Substring(0, key.IndexOf('>'))))} → {key.Substring(key.IndexOf('>') + 1)}");
                Lost(sb, before.cratesOk, cratesOk, "상자 옆 NavMesh 없음");
                Lost(sb, before.cratesReachable, cratesReachable, "상자에 스폰에서 닿을 수 없다");
                return sb.ToString();
            }

            // 순찰 구간 경로 길이가 before보다 maxIncrease(비율)를 넘게 늘어난 구간 (맵 밀도 §7 2 — 적이 나무 사이로 이상하게 돌지 않게).
            // 최대 증가 비율을 maxRatio로 돌려준다.
            public string PathLengthRegressions(NavState before, float maxIncrease, out float maxRatio)
            {
                var sb = new StringBuilder();
                maxRatio = 0f;
                foreach (KeyValuePair<string, float> pair in before.segmentsOk)
                {
                    if (!segmentsOk.TryGetValue(pair.Key, out float now) || pair.Value <= 0.01f)
                        continue;
                    float ratio = now / pair.Value - 1f;
                    maxRatio = Mathf.Max(maxRatio, ratio);
                    if (ratio > maxIncrease)
                        sb.AppendLine($"  순찰 구간 경로가 {ratio * 100f:0}% 길어졌다({pair.Value:0.0} → {now:0.0}m): {DescribeSegment(pair.Key)}");
                }
                return sb.ToString();
            }

            private string DescribeSegment(string key) =>
                $"{Describe(int.Parse(key.Substring(0, key.IndexOf('#'))))} 구간 {key.Substring(key.IndexOf('#') + 1)}";

            private void Lost(StringBuilder sb, HashSet<int> before, HashSet<int> now, string message)
            {
                foreach (int id in before)
                    if (!now.Contains(id))
                        sb.AppendLine($"  {message}: {Describe(id)}");
            }

            private string Describe(int instanceId)
            {
                objects.TryGetValue(instanceId, out Component c);
                return c != null ? $"{(c.transform.parent != null ? c.transform.parent.name + "/" : "")}{c.name} {c.transform.position}" : instanceId.ToString();
            }
        }
    }
}
