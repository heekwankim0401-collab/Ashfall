using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Ashfall.AI;
using Ashfall.Core;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Kind = Ashfall.EditorTools.M7EnemyLayout.Kind;
using L = Ashfall.EditorTools.CrimsonBasinLayout;
using Layout = Ashfall.EditorTools.M7EnemyLayout;
using Object = UnityEngine.Object;

namespace Ashfall.EditorTools
{
    // M7 적 배치를 레이드 씬에 적용 (M7 설계 §1.4·§1.7·§3.5, Q22~Q30, SYS-AI). 7B-2c.
    // ApplyRaid(열린 레이드 씬):
    //   ① M7EnemyLayout.CheckAll(지형 높이·저격수 시야·보스 거점) + 모든 시작·웨이포인트 NavMesh 스냅 계산 — 실패하면 예외(씬을 고치지 않는다, 7B 리뷰 9).
    //   ② 머티리얼(EnsureMaterials): M_Mutant·M_Boss(팔레트 표, M6ArtSetup과 같은 생성·색 기록), M_SniperGlint(Unlit Bone × HDR).
    //   ③ 14명(SNP 2, MUT 8, BOS 1, BGD 3)을 M3SceneSetup.CreateEnemy(M4EnemySetup과 같은 적 오브젝트)로 씬 최상위에 놓는다.
    //      이름 = "Enemy_" + M7_분대 + 순번(CreateEnemy가 "Enemy_"를 붙인다). 시작 위치 = 대원별 StartsXZ(M4 SquadSlots 아님) → NavMesh 스냅 3m(실패하면 예외).
    //      변이체 무리는 순찰 경로 "PatrolRoute_" + 분대(웨이포인트도 스냅). 시드 = 기존 20명 다음 순번(21~34, 결정적).
    //   ④ 유형별 몸 머티리얼(PaletteMaterialMap.EnemyBodySpec), 무장 없는 유형(변이체)은 총 모양 큐브를 뺀다.
    //   ⑤ 저격수 자식 "SniperGlint"(Quad, 콜라이더 없음, 그림자 끔, 렌더러 꺼 둠 — AI SniperGlint가 Telegraph일 때 켠다) + SniperGlint 컴포넌트.
    // 멱등: 이름으로 찾아 있으면 위치·회전·참조·머티리얼만 다시 맞추고(값이 같으면 건드리지 않음), 없으면 만든다. 바뀐 것이 있을 때만 씬을 dirty로.
    //   (오브젝트가 깨져 있으면 — Eye·Muzzle·Body·EnemyAgent가 없으면 — 지우고 같은 이름으로 다시 만든다. 씬 루트 수는 그대로.)
    // 기존 20명(M4EnemySetup)·CrimsonBasinLayout은 건드리지 않는다. 씬 저장은 호출자(M7SceneSetup — 7B-3 통합) 몫, 단독 메뉴는 직접 저장.
    // 배치모드(단독 확인): -executeMethod Ashfall.EditorTools.M7EnemySceneApply.ApplyRaidSceneMenu
    public static class M7EnemySceneApply
    {
        private const string Tag = "[M7EnemySceneApply]";
        public const string EnemyNamePrefix = "Enemy_";              // M3SceneSetup.CreateEnemy 규칙
        public const string RouteNamePrefix = "PatrolRoute_";         // M4EnemySetup 규칙

        // 씬 구성 값 (에디터 배치 기하 — 게임 수치 아님, TODO(GDD-?) Q23)
        private const float GlintForwardM = 0.12f;                    // 섬광 쿼드 = 눈 앞 이 거리 (조준경 자리)
        private const float GlintInitialSizeM = 0.05f;                // 처음 크기 (실행 중에는 SniperGlint.Face가 거리 비례로 정한다)
        private const float OnMeshToleranceM = 0.5f;                  // 확인용: 시작 위치 ↔ NavMesh 점 허용 차이 (M4와 같음)
        private const float PositionEps = 1e-4f;
        private const float RotationEpsDeg = 0.01f;

        private const string BodyChild = "Body";
        private const string EyeChild = "Eye";
        private const string MuzzleChild = "Muzzle";
        private const string GunChild = "Gun";

        // ---------- 메뉴 (단독 확인용) ----------

        [MenuItem("Ashfall/Setup/M7/Apply Enemies (Raid Scene)")]
        public static void ApplyRaidSceneMenu()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            string path = M4SceneSetup.RaidScenePath;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
                throw new InvalidOperationException($"{Tag} 레이드 씬 없음: {path} — 먼저 Ashfall/Setup/Create M4 Raid Scenes");
            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            ApplyRaid(scene);
            AssetDatabase.SaveAssets();
            if (!scene.isDirty)
            {
                Debug.Log($"{Tag} 변경 없음: {path}");
                return;
            }
            if (!EditorSceneManager.SaveScene(scene, path))
                throw new InvalidOperationException($"{Tag} 씬 저장 실패: {path}");
            Debug.Log($"{Tag} 적용·저장: {path}");
        }

        // ---------- 머티리얼 ----------

        public readonly struct Materials
        {
            public readonly Material Mutant;
            public readonly Material Boss;
            public readonly Material Raider;   // 저격수·호위 (기존 M3_EnemyRDR)
            public readonly Material Glint;

            public Materials(Material mutant, Material boss, Material raider, Material glint)
            {
                Mutant = mutant;
                Boss = boss;
                Raider = raider;
                Glint = glint;
            }

            public Material Body(Kind kind)
            {
                switch (kind)
                {
                    case Kind.Mutant: return Mutant;
                    case Kind.Boss: return Boss;
                    default: return Raider;
                }
            }
        }

        // M_Mutant·M_Boss(팔레트 폴더, M6ArtSetup과 같은 방식 — 있으면 색·속성만 다시 기록, GUID 유지)와 M_SniperGlint를 맞춘다.
        // M3_EnemyRDR은 기존 머티리얼이라 만들지 않는다(없으면 M4EnemySetup이 만드는 것과 같은 방식으로 만든 뒤 팔레트 색). 실패하면 예외.
        public static Materials EnsureMaterials()
        {
            PaletteSO palette = AssetDatabase.LoadAssetAtPath<PaletteSO>(M6Names.PalettePath);
            if (palette == null)
                throw new InvalidOperationException($"{Tag} 팔레트 없음: {M6Names.PalettePath} — 먼저 Ashfall/Setup/Apply M6 Art & Audio");

            Material mutant = EnsurePaletteMaterial(PaletteMaterialMap.Materials[PaletteMaterialMap.Key.Mutant], palette);
            Material boss = EnsurePaletteMaterial(PaletteMaterialMap.Materials[PaletteMaterialMap.Key.Boss], palette);

            PaletteMaterialMap.Spec raiderSpec = PaletteMaterialMap.EnemyBodySpec(Kind.Sniper);
            var raider = AssetDatabase.LoadAssetAtPath<Material>(raiderSpec.Path);
            if (raider == null)
            {
                M4EnemySetup.EnsureEnemyMaterials();
                raider = AssetDatabase.LoadAssetAtPath<Material>(raiderSpec.Path);
                if (raider == null)
                    throw new InvalidOperationException($"{Tag} 머티리얼 생성 실패: {raiderSpec.Path}");
                M6ArtSetup.ApplySpec(raider, raiderSpec, palette);
            }

            Material glint = EnsurePaletteMaterial(PaletteMaterialMap.SniperGlint, palette);
            ApplyHdrUnlit(glint, PaletteMaterialMap.SniperGlint, palette);
            return new Materials(mutant, boss, raider, glint);
        }

        // M6ArtSetup.EnsureMaterial + ApplySpec와 같은 결과 (M6 아트 메뉴를 나중에 돌려도 바이트가 같다)
        private static Material EnsurePaletteMaterial(PaletteMaterialMap.Spec spec, PaletteSO palette)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(spec.Path);
            if (m == null)
            {
                string shaderName = spec.Unlit ? PaletteMaterialMap.UnlitShader : PaletteMaterialMap.LitShader;
                Shader shader = Shader.Find(shaderName);
                if (shader == null)
                    throw new InvalidOperationException($"{Tag} 셰이더를 찾지 못했다: {shaderName} ({spec.Path})");
                SetupUtil.EnsureFolder(Path.GetDirectoryName(spec.Path).Replace('\\', '/'));
                m = new Material(shader) { name = spec.Name };
                AssetDatabase.CreateAsset(m, spec.Path);
                Debug.Log($"{Tag} 머티리얼 생성: {spec.Path} — TODO(GDD-?) 자리표시");
            }
            M6ArtSetup.ApplySpec(m, spec, palette);
            return m;
        }

        // Unlit 발광: _BaseColor = 토큰 × Emission (HDR). Unlit 셰이더에는 _EmissionColor가 없다.
        internal static void ApplyHdrUnlit(Material m, PaletteMaterialMap.Spec spec, PaletteSO palette)
        {
            Color c = HdrColor(spec, palette);
            M6ArtSetup.SetBaseColor(m, c);
            EditorUtility.SetDirty(m);
        }

        public static Color HdrColor(PaletteMaterialMap.Spec spec, PaletteSO palette)
        {
            Color token = palette.Get(spec.Token);
            float k = spec.Emission > 0f ? spec.Emission : 1f;
            return new Color(token.r * k, token.g * k, token.b * k, spec.Alpha);
        }

        // ---------- 레이드 씬 ----------

        // 성공하면 true. 데이터·씬 구성이 모자라거나 검사·스냅이 실패하면 예외(반쯤 적용된 씬을 저장하지 않게 — M6 리뷰 6번).
        public static bool ApplyRaid(Scene scene)
        {
            if (!scene.IsValid())
                throw new InvalidOperationException($"{Tag} 씬이 유효하지 않다");

            var archetypes = new Dictionary<Kind, EnemyArchetypeSO>();
            foreach (Kind kind in (Kind[])Enum.GetValues(typeof(Kind)))
            {
                var a = AssetDatabase.LoadAssetAtPath<EnemyArchetypeSO>(Layout.ArchetypePath(kind));
                if (a == null)
                    throw new InvalidOperationException($"{Tag} 적 유형 없음: {Layout.ArchetypePath(kind)} — 먼저 Ashfall/Setup/Create M7 AI Data");
                archetypes[kind] = a;
            }

            Terrain terrain = FindInScene<Terrain>(scene);
            if (terrain == null)
                throw new InvalidOperationException($"{Tag} {scene.name}: Terrain 없음 (레이드 씬이 아니다)");
            AiDirector director = FindInScene<AiDirector>(scene);
            if (director == null)
                throw new InvalidOperationException($"{Tag} {scene.name}: AiDirector 없음 (M4 레이드 씬 메뉴가 만든다)");

            // ① 배치 검사 (레벨 데이터 — 실제 지형으로)
            if (!Layout.CheckAll(xz => M4MapSetup.GroundY(terrain, xz.x, xz.y), archetypes[Kind.Sniper].sightRange,
                    archetypes[Kind.Boss].anchorRadiusM, out string report))
                throw new InvalidOperationException($"{Tag} M7 배치 검사 실패\n{report}");

            EnsureNavMeshLoaded(scene);

            // ①' NavMesh 스냅을 씬을 바꾸기 전에 모두 계산한다 (7B 리뷰 9: 중간 실패로 씬이 반쯤 바뀌지 않게). 실패하면 여기서 예외.
            var routePoints = new Dictionary<string, Vector3[]>();
            var memberPoints = new Dictionary<string, Vector3>();
            foreach (Layout.SquadPlan plan in Layout.Squads)
            {
                if (plan.RouteXZ != null && plan.RouteXZ.Length >= 2)
                {
                    var points = new Vector3[plan.RouteXZ.Length];
                    for (int k = 0; k < points.Length; k++)
                        points[k] = Snap(terrain, plan.RouteXZ[k], $"{RouteNamePrefix}{plan.Name}/{k}");
                    routePoints[plan.Name] = points;
                }
                for (int i = 0; i < plan.Count; i++)
                    memberPoints[plan.MemberName(i)] = Snap(terrain, plan.StartsXZ[i], plan.MemberName(i));
            }

            Materials materials = EnsureMaterials();

            if (SceneManager.GetActiveScene() != scene)
                SceneManager.SetActiveScene(scene);   // 새 오브젝트는 활성 씬에 생긴다

            var ctx = new M3SceneSetup.SceneContext
            {
                Scv = null,
                Rdr = null,   // CreateEnemy는 archetype == Rdr일 때만 RdrMaterial — 여기서는 항상 ScvMaterial 자리에 유형 머티리얼을 넣는다
                Director = director,
            };

            bool changed = false;
            int order = L.ExpectedEnemyCount;   // 기존 20명 다음 순번 (seed = order + 1)
            int created = 0, updated = 0;
            var placed = new List<GameObject>();
            foreach (Layout.SquadPlan plan in Layout.Squads)
            {
                PatrolRoute route = plan.RouteXZ != null && plan.RouteXZ.Length >= 2
                    ? EnsureRoute(scene, plan, routePoints[plan.Name], ref changed)
                    : null;
                for (int i = 0; i < plan.Count; i++, order++)
                {
                    Kind kind = plan.Members[i];
                    EnemyArchetypeSO archetype = archetypes[kind];
                    string memberName = plan.MemberName(i);
                    Vector3 position = memberPoints[memberName];
                    Quaternion rotation = Quaternion.Euler(0f, plan.YawDeg, 0f);

                    GameObject enemy = SetupUtil.FindRoot(scene, EnemyNamePrefix + memberName);
                    if (enemy != null && !IsIntact(enemy))
                    {
                        Debug.LogWarning($"{Tag} {enemy.name}: 구성이 깨져 다시 만든다");
                        Object.DestroyImmediate(enemy);
                        enemy = null;
                    }
                    if (enemy == null)
                    {
                        ctx.ScvMaterial = materials.Body(kind);
                        ctx.EnemyCount = order;
                        enemy = M3SceneSetup.CreateEnemy(ctx, memberName, archetype, position, plan.YawDeg, route, plan.SquadId, 0);
                        created++;
                        changed = true;
                    }
                    else
                    {
                        bool c = SetTransform(enemy.transform, position, rotation);
                        c |= SetAgentFields(enemy.GetComponent<EnemyAgent>(), archetype, director, route, plan.SquadId, 0, order + 1);
                        if (c)
                            updated++;
                        changed |= c;
                    }

                    changed |= SetBodyMaterial(enemy, materials.Body(kind));
                    if (archetype.loadouts == null || archetype.loadouts.Count == 0)
                        changed |= RemoveGunVisual(enemy);
                    if (kind == Kind.Sniper)
                        changed |= EnsureGlint(enemy, materials.Glint);
                    placed.Add(enemy);
                }
            }

            if (!Validate(placed, out string validation))
                throw new InvalidOperationException($"{Tag} M7 적 배치 확인 실패\n{validation}");

            if (changed)
                EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"{Tag} {scene.name}: M7 적 {placed.Count}명 (새로 {created}, 다시 맞춤 {updated}){(changed ? "" : " — 변경 없음")}\n{report}{validation}");
            return true;
        }

        // ---------- 적 오브젝트 ----------

        // CreateEnemy가 만든 구성(EnemyAgent + Eye·Muzzle·Body 자식)이 있는가
        private static bool IsIntact(GameObject enemy) =>
            enemy.GetComponent<EnemyAgent>() != null
            && enemy.transform.Find(EyeChild) != null
            && enemy.transform.Find(MuzzleChild) != null
            && enemy.transform.Find(BodyChild) != null;

        private static bool SetTransform(Transform t, Vector3 position, Quaternion rotation)
        {
            if (Vector3.Distance(t.position, position) <= PositionEps && Quaternion.Angle(t.rotation, rotation) <= RotationEpsDeg)
                return false;
            t.SetPositionAndRotation(position, rotation);
            return true;
        }

        // CreateEnemy와 같은 직렬화 필드를 다시 맞춘다. 바뀌었으면 true.
        private static bool SetAgentFields(EnemyAgent agent, EnemyArchetypeSO archetype, AiDirector director, PatrolRoute route,
            int squadId, int loadoutIndex, int seed)
        {
            Transform root = agent.transform;
            var so = new SerializedObject(agent);
            SetRef(so, "archetype", archetype);
            SetRef(so, "director", director);
            SetRef(so, "patrolRoute", route);
            SetRef(so, "eye", root.Find(EyeChild));
            SetRef(so, "muzzle", root.Find(MuzzleChild));
            SetRef(so, "bodyVisual", root.Find(BodyChild));
            SetInt(so, "squadId", squadId);
            SetInt(so, "loadoutIndex", loadoutIndex);
            SetInt(so, "seed", seed);
            return so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetRef(SerializedObject so, string field, Object value)
        {
            SerializedProperty p = so.FindProperty(field);
            if (p == null)
                throw new InvalidOperationException($"{Tag} {so.targetObject.GetType().Name}에 직렬화 필드 '{field}'가 없다");
            if (p.objectReferenceValue != value)
                p.objectReferenceValue = value;
        }

        private static void SetInt(SerializedObject so, string field, int value)
        {
            SerializedProperty p = so.FindProperty(field);
            if (p == null)
                throw new InvalidOperationException($"{Tag} {so.targetObject.GetType().Name}에 직렬화 필드 '{field}'가 없다");
            if (p.intValue != value)
                p.intValue = value;
        }

        private static bool SetBodyMaterial(GameObject enemy, Material material)
        {
            Transform body = enemy.transform.Find(BodyChild);
            var renderer = body != null ? body.GetComponent<MeshRenderer>() : null;
            if (renderer == null || renderer.sharedMaterial == material)
                return false;
            renderer.sharedMaterial = material;
            EditorUtility.SetDirty(renderer);
            return true;
        }

        // 무장 없는 유형(변이체): CreateEnemy가 Muzzle 아래에 만든 총 모양 큐브를 뺀다(Muzzle 자체는 EnemyAgent가 쓴다).
        private static bool RemoveGunVisual(GameObject enemy)
        {
            Transform muzzle = enemy.transform.Find(MuzzleChild);
            Transform gun = muzzle != null ? muzzle.Find(GunChild) : null;
            if (gun == null)
                return false;
            Object.DestroyImmediate(gun.gameObject);
            return true;
        }

        // 저격수 자식 "SniperGlint": Quad(콜라이더 없음) + SniperGlint, 머티리얼 M_SniperGlint, 그림자 끔, 렌더러 꺼 둠.
        private static bool EnsureGlint(GameObject sniper, Material glintMaterial)
        {
            bool changed = false;
            Transform root = sniper.transform;
            Transform glint = root.Find(M7Names.SniperGlintObject);
            if (glint == null)
            {
                GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = M7Names.SniperGlintObject;
                Object.DestroyImmediate(quad.GetComponent<Collider>());
                quad.transform.SetParent(root, false);
                Transform eye = root.Find(EyeChild);
                Vector3 eyeLocal = eye != null ? eye.localPosition : Vector3.up * 1.6f;
                quad.transform.localPosition = eyeLocal + Vector3.forward * GlintForwardM;
                quad.transform.localRotation = Quaternion.identity;
                quad.transform.localScale = Vector3.one * GlintInitialSizeM;
                glint = quad.transform;
                changed = true;
            }

            foreach (Collider c in glint.GetComponents<Collider>())
            {
                Object.DestroyImmediate(c);
                changed = true;
            }
            if (glint.GetComponent<MeshFilter>() == null)
            {
                GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Quad);
                glint.gameObject.AddComponent<MeshFilter>().sharedMesh = temp.GetComponent<MeshFilter>().sharedMesh;
                Object.DestroyImmediate(temp);
                changed = true;
            }
            MeshRenderer renderer = glint.GetComponent<MeshRenderer>();
            if (renderer == null)
            {
                renderer = glint.gameObject.AddComponent<MeshRenderer>();
                changed = true;
            }
            if (renderer.sharedMaterial != glintMaterial)
            {
                renderer.sharedMaterial = glintMaterial;
                changed = true;
            }
            if (renderer.shadowCastingMode != ShadowCastingMode.Off)
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                changed = true;
            }
            if (renderer.receiveShadows)
            {
                renderer.receiveShadows = false;
                changed = true;
            }
            if (renderer.lightProbeUsage != LightProbeUsage.Off)
            {
                renderer.lightProbeUsage = LightProbeUsage.Off;
                changed = true;
            }
            if (renderer.reflectionProbeUsage != ReflectionProbeUsage.Off)
            {
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                changed = true;
            }
            // 처음엔 꺼 둔다 — AI SniperGlint가 Telegraph일 때 Renderer.enabled로 켠다(오브젝트는 켜 둠)
            if (renderer.enabled)
            {
                renderer.enabled = false;
                changed = true;
            }
            if (!glint.gameObject.activeSelf)
            {
                glint.gameObject.SetActive(true);
                changed = true;
            }
            if (glint.GetComponent<SniperGlint>() == null)
            {
                glint.gameObject.AddComponent<SniperGlint>();
                changed = true;
            }
            if (changed)
                EditorUtility.SetDirty(glint.gameObject);
            return changed;
        }

        // ---------- 순찰 경로 ----------

        // "PatrolRoute_" + 분대: 있고 웨이포인트 수가 같으면 위치만 다시 스냅, 아니면 지우고 M3SceneSetup.CreateRoute로 다시 만든다.
        private static PatrolRoute EnsureRoute(Scene scene, Layout.SquadPlan plan, Vector3[] snapped, ref bool changed)
        {
            string name = RouteNamePrefix + plan.Name;
            GameObject existing = SetupUtil.FindRoot(scene, name);
            PatrolRoute route = existing != null ? existing.GetComponent<PatrolRoute>() : null;
            if (existing != null && (route == null || existing.transform.childCount != plan.RouteXZ.Length || !WaypointsMatchChildren(route)))
            {
                Object.DestroyImmediate(existing);
                existing = null;
                route = null;
            }
            if (route == null)
            {
                route = M3SceneSetup.CreateRoute(name, plan.RouteXZ, Vector3.zero);
                changed = true;
            }
            Transform t = route.transform;
            if (t.position != Vector3.zero || t.rotation != Quaternion.identity || t.localScale != Vector3.one)
            {
                t.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                t.localScale = Vector3.one;
                changed = true;
            }
            for (int i = 0; i < plan.RouteXZ.Length; i++)
            {
                Transform wp = t.GetChild(i);
                Vector3 p = snapped[i];
                if (Vector3.Distance(wp.position, p) > PositionEps)
                {
                    wp.position = p;
                    changed = true;
                }
            }
            return route;
        }

        // PatrolRoute.waypoints 배열이 자식 순서와 같은가
        private static bool WaypointsMatchChildren(PatrolRoute route)
        {
            var so = new SerializedObject(route);
            SerializedProperty array = so.FindProperty("waypoints");
            if (array == null || array.arraySize != route.transform.childCount)
                return false;
            for (int i = 0; i < array.arraySize; i++)
                if (array.GetArrayElementAtIndex(i).objectReferenceValue != route.transform.GetChild(i))
                    return false;
            return true;
        }

        // ---------- NavMesh ----------

        // 지면 점 → NavMesh(3m 안). 실패하면 예외 (설계 §1.4 "퍼치 높이는 Terrain 샘플 + NavMesh 스냅(3m) — 실패하면 메뉴가 예외").
        private static Vector3 Snap(Terrain terrain, Vector2 xz, string label)
        {
            Vector3 ground = M4MapSetup.GroundPoint(terrain, xz);
            if (!NavMesh.SamplePosition(ground, out NavMeshHit hit, Layout.NavMeshSnapDistanceM, NavMesh.AllAreas))
                throw new InvalidOperationException(
                    $"{Tag} {label}: NavMesh 스냅 실패 ({xz}, 지면 {ground.y:0.0}m, 허용 {Layout.NavMeshSnapDistanceM}m) — 배치(M7EnemyLayout) 또는 NavMesh 확인");
            return hit.position;
        }

        // 씬을 막 연 경우에도 NavMeshSurface 데이터가 들어와 있게 (ExecuteAlways OnEnable이 넣지만, 비활성·지연을 대비). 이미 있으면 아무것도 안 한다.
        private static void EnsureNavMeshLoaded(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (NavMeshSurface surface in root.GetComponentsInChildren<NavMeshSurface>(true))
                    if (surface.navMeshData != null)
                        surface.AddData();
            if (NavMesh.CalculateTriangulation().vertices.Length == 0)
                throw new InvalidOperationException($"{Tag} {scene.name}: NavMesh 없음 — 먼저 Ashfall/Setup/Create M4 Raid Scenes(NavMesh 굽기)");
        }

        // ---------- 확인 ----------

        // 수(14 = SNP 2·MUT 8·BOS 1·BGD 3), 최상위, NavMesh 위, 스폰 거리 ≥ 190m, 저격수 섬광 자식.
        public static bool Validate(IReadOnlyList<GameObject> enemies, out string report)
        {
            var sb = new StringBuilder();
            bool ok = enemies.Count == Layout.Count;
            sb.AppendLine($"  M7 적 {enemies.Count}/{Layout.Count}");
            float nearest = float.MaxValue;
            string nearestName = "";
            foreach (GameObject enemy in enemies)
            {
                Vector3 p = enemy.transform.position;
                if (enemy.transform.parent != null)
                {
                    sb.AppendLine($"  {enemy.name}: 최상위가 아니다");
                    ok = false;
                }
                if (!(NavMesh.SamplePosition(p, out NavMeshHit hit, 1f, NavMesh.AllAreas) && Vector3.Distance(hit.position, p) <= OnMeshToleranceM))
                {
                    sb.AppendLine($"  {enemy.name}: NavMesh 밖 {p}");
                    ok = false;
                }
                float d = Layout.MinDistanceToSpawns(new Vector2(p.x, p.z), out _);
                if (d < nearest)
                {
                    nearest = d;
                    nearestName = enemy.name;
                }
                if (d < Layout.MinDistanceFromSpawnM)
                {
                    sb.AppendLine($"  {enemy.name}: 스폰에서 {d:0.0}m < {Layout.MinDistanceFromSpawnM}m");
                    ok = false;
                }
                var agent = enemy.GetComponent<EnemyAgent>();
                if (agent != null && agent.Archetype != null && agent.Archetype.tactics == EnemyTacticsKind.Sniper)
                {
                    Transform glint = enemy.transform.Find(M7Names.SniperGlintObject);
                    if (glint == null || glint.GetComponent<SniperGlint>() == null)
                    {
                        sb.AppendLine($"  {enemy.name}: 섬광 자식 없음");
                        ok = false;
                    }
                }
            }
            sb.AppendLine($"  스폰과 가장 가까운 M7 적: {nearest:0.0}m ({nearestName}), 기준 ≥ {Layout.MinDistanceFromSpawnM}m");
            report = sb.ToString();
            return ok;
        }

        private static T FindInScene<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                T found = root.GetComponentInChildren<T>(true);
                if (found != null)
                    return found;
            }
            return null;
        }
    }
}
