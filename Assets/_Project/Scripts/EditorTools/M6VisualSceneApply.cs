using System;
using System.Collections.Generic;
using System.Text;
using Ashfall.Audio;
using Ashfall.Core;
using Ashfall.Inventory;
using Ashfall.Player;
using Ashfall.Raid;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using L = Ashfall.EditorTools.CrimsonBasinLayout;
using Object = UnityEngine.Object;

namespace Ashfall.EditorTools
{
    // M6 비주얼을 열린 씬에 적용 (M6 설계 §1.3, §1.6, §3.4, ART-*, Q1~Q9·Q29).
    // ApplyRaid(레이드 씬): Global Volume(VP_RaidAtmosphere), 플레이어 카메라 포스트 켬, RenderSettings(Exp² 안개·Trilight·하늘·태양),
    //   Directional Light → "Sun"(하늘 태양과 같은 방향), Terrain 레이어 TL_AshSoil, 블록아웃·상자·스위치 머티리얼 + GroundSurface,
    //   저수지 물웅덩이 3개(트리거·Ignore Raycast·Water), 재 파티클 "AshParticles"(Player 자식, World 시뮬레이션).
    // ApplyMenuScene(은신처·메인 메뉴): 카메라 배경 Ash_Black만 (Q29).
    // 멱등: 이름으로 찾아 있으면 다시 설정, 없으면 만든다(지우고 다시 만들지 않음). 컴포넌트는 있으면 재사용.
    // 에셋(머티리얼·프로필·하늘)은 M6ArtSetup.CreateAssets()가 먼저 만들어 둬야 한다. 씬 저장은 호출자(M6SceneSetup — 3단계) 몫.
    // 확인 씬(M1~M3)은 건드리지 않는다(Q9).
    public static class M6VisualSceneApply
    {
        private const string Tag = "[M6VisualSceneApply]";

        // 물웅덩이 3개 (저수지 바닥 평탄부, 레벨 데이터 — TODO(GDD-?) 자리·크기 임시). 중심 XZ, 크기 XZ, 회전.
        private static readonly (Vector2 xz, Vector2 size, float yawDeg)[] Puddles =
        {
            (L.ReservoirCenterXZ + new Vector2(-6f, -4f), new Vector2(9f, 6f), 15f),
            (L.ReservoirCenterXZ + new Vector2(-24f, 12f), new Vector2(6f, 4.5f), -30f),
            (L.ReservoirCenterXZ + new Vector2(14f, 20f), new Vector2(5f, 3.5f), 60f),
        };
        private const float PuddleTopAboveGroundM = 0.03f; // 발자국 평면 위 수면 (가장 높은 지면 + 이 값)
        private const float PuddleBelowGroundM = 0.1f;     // 가장 낮은 지면 아래로 묻는 깊이
        private const int IgnoreRaycastLayer = 2;          // 내장 레이어 "Ignore Raycast"

        private const int SingleLayerAlphamapResolution = 16; // 레이어 1개(균일)면 최소 해상도로 충분 — 레이어를 칠할 때 사람이 올린다

        private const int GlobalVolumePriority = 10;       // 파이프라인 기본 프로필 위 (§1.3)
        // 재 파티클 보기 수치(화면 크기 상한·노이즈 주파수·상자 중심 비율)는 AtmosphereConfigSO에 있다(M6 리뷰 낮음 4).

        [MenuItem("Ashfall/Setup/M6/Apply Visual (Open Scene)")]
        public static void ApplyToOpenSceneMenu()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!ApplyByName(scene))
                return;
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"{Tag} {scene.name}에 적용했다. 저장은 직접(Ctrl+S).");
        }

        // (2a 단독 확인용 "Apply Visual (Game Scenes)"는 통합 메뉴 Ashfall/Setup/Apply M6 Art & Audio와 겹쳐 리뷰 수정에서 지웠다 — 낮음 9)

        private static bool ApplyByName(Scene scene)
        {
            if (scene.name == SceneNames.RaidCrimsonBasin)
                return ApplyRaid(scene);
            if (scene.name == SceneNames.Hideout || scene.name == SceneNames.MainMenu)
                return ApplyMenuScene(scene);
            Debug.LogWarning($"{Tag} M6 비주얼 대상 씬이 아니다: {scene.name} (레이드·은신처·메인 메뉴만, Q9)");
            return false;
        }

        // ---------- 은신처·메인 메뉴 (Q29) ----------

        public static bool ApplyMenuScene(Scene scene)
        {
            PaletteSO palette = AssetDatabase.LoadAssetAtPath<PaletteSO>(M6Names.PalettePath);
            if (palette == null)
            {
                Debug.LogError($"{Tag} 팔레트 없음: {M6Names.PalettePath}");
                return false;
            }
            int count = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
            {
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = palette.Get(PaletteToken.AshBlack);
                EditorUtility.SetDirty(camera);
                count++;
            }
            if (count == 0)
                Debug.LogWarning($"{Tag} {scene.name}: 카메라 없음");
            return true;
        }

        // ---------- 레이드 ----------

        public static bool ApplyRaid(Scene scene)
        {
            var cfg = AssetDatabase.LoadAssetAtPath<AtmosphereConfigSO>(M6Names.AtmosphereConfigPath);
            PaletteSO palette = cfg != null && cfg.palette != null ? cfg.palette : AssetDatabase.LoadAssetAtPath<PaletteSO>(M6Names.PalettePath);
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(M6Names.RaidVolumeProfilePath);
            var sky = AssetDatabase.LoadAssetAtPath<Material>(M6Names.SkyMaterialPath);
            var ash = AssetDatabase.LoadAssetAtPath<Material>(M6Names.AshParticleMaterialPath);
            var terrainLayer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(M6Names.TerrainLayerPath);
            if (cfg == null || palette == null || profile == null || sky == null || ash == null || terrainLayer == null)
            {
                Debug.LogError($"{Tag} M6 에셋이 없다 — 먼저 Create M6 Data → Ashfall/Setup/M6/Create Art Assets.");
                return false;
            }
            if (SceneManager.GetActiveScene() != scene)
                SceneManager.SetActiveScene(scene); // RenderSettings는 활성 씬에 기록된다

            var problems = new StringBuilder();
            ApplyVolume(scene, profile);
            ApplyCamera(scene, problems);
            Light sun = ApplySun(scene, cfg, palette);
            ApplyRenderSettings(cfg, palette, sky, sun);
            ApplyTerrain(scene, terrainLayer, problems);
            int painted = ApplyBlockout(scene, problems);
            ApplyPuddles(scene, problems);
            ApplyAshParticles(scene, cfg, ash, problems);

            if (problems.Length > 0)
                Debug.LogWarning($"{Tag} {scene.name} 경고:\n{problems}");
            Debug.Log($"{Tag} {scene.name}: 볼륨·카메라 포스트·안개·하늘·Sun·지형 레이어·블록아웃 {painted}개·웅덩이 {Puddles.Length}·재 파티클 적용");
            return true;
        }

        private static void ApplyVolume(Scene scene, VolumeProfile profile)
        {
            GameObject go = SetupUtil.FindOrCreateRoot(scene, M6Names.GlobalVolumeObject);
            var volume = SetupUtil.GetOrAdd<Volume>(go);
            volume.isGlobal = true;
            volume.priority = GlobalVolumePriority;
            volume.weight = 1f;
            volume.sharedProfile = profile;
            EditorUtility.SetDirty(volume);
        }

        // 플레이어 카메라(Player 아래 MainCamera)에 URP 포스트 처리를 켠다 (기본 false — 설계 서두).
        private static void ApplyCamera(Scene scene, StringBuilder problems)
        {
            int count = 0;
            foreach (Camera camera in AllInScene<Camera>(scene))
            {
                var data = camera.GetComponent<UniversalAdditionalCameraData>();
                if (data == null)
                    data = camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
                data.renderPostProcessing = true;
                EditorUtility.SetDirty(data);
                count++;
            }
            if (count == 0)
                problems.AppendLine("카메라 없음 — 포스트 처리를 켤 수 없다");
        }

        // 기존 Directional Light("Sun" 또는 NewScene 기본 "Directional Light")를 "Sun"으로. 없으면 만든다.
        private static Light ApplySun(Scene scene, AtmosphereConfigSO cfg, PaletteSO palette)
        {
            GameObject go = SetupUtil.FindRoot(scene, M6Names.SunObject);
            if (go == null)
            {
                foreach (Light light in AllInScene<Light>(scene))
                    if (light.type == LightType.Directional && light.transform.parent == null)
                    {
                        go = light.gameObject;
                        break;
                    }
            }
            if (go == null)
            {
                go = new GameObject(M6Names.SunObject);
                SceneManager.MoveGameObjectToScene(go, scene);
            }
            go.name = M6Names.SunObject;
            var sun = SetupUtil.GetOrAdd<Light>(go);
            sun.type = LightType.Directional;
            sun.color = SunLightColor(cfg, palette);
            sun.intensity = cfg.sunLightIntensity;
            if (sun.shadows == LightShadows.None)
                sun.shadows = LightShadows.Soft;
            // 빛은 태양에서 장면으로 — forward = −태양 방향 (V2: 그림자 방향 = 하늘 태양 위치)
            go.transform.SetPositionAndRotation(go.transform.position, Quaternion.LookRotation(-SkyPanorama.SunDirection(cfg)));
            EditorUtility.SetDirty(sun);
            EditorUtility.SetDirty(go.transform);
            return sun;
        }

        // 태양 라이트 색 = lerp(토큰, Bone, sunLightBoneMix) (맵 개선 §2, 섞임 0 = 토큰 그대로). 테스트도 이 식으로 기대값을 만든다.
        public static Color SunLightColor(AtmosphereConfigSO cfg, PaletteSO palette)
        {
            Color c = Color.Lerp(palette.Get(cfg.sunLightToken), palette.Get(PaletteToken.Bone), Mathf.Clamp01(cfg.sunLightBoneMix));
            c.a = 1f;
            return c;
        }

        // Exp² 안개(항상 켬 — 위험 R1 셰이더 변형 유지), 하늘, Trilight 환경광, 태양
        private static void ApplyRenderSettings(AtmosphereConfigSO cfg, PaletteSO palette, Material sky, Light sun)
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = palette.Get(cfg.fogToken);
            // 밀도 = 2c FogMath와 같은 식 (3단계 통합: 2a의 중복 FogDensity를 지우고 FogMath 하나로)
            RenderSettings.fogDensity = FogMath.DensityForVisibility(cfg.clearVisibilityM, cfg.fogVisibilityThreshold);
            RenderSettings.skybox = sky;
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            float k = cfg.ambientIntensity;
            RenderSettings.ambientSkyColor = Scale(palette.Get(cfg.ambientSkyToken), k);
            RenderSettings.ambientEquatorColor = Scale(palette.Get(cfg.ambientEquatorToken), k);
            RenderSettings.ambientGroundColor = Scale(palette.Get(cfg.ambientGroundToken), k);
            RenderSettings.ambientIntensity = 1f;
        }

        private static Color Scale(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, 1f);

        // TL_AshSoil을 지형 레이어에 둔다(이미 있으면 그대로, 사용자가 추가한 레이어는 보존). 처음 넣을 때 알파맵 = 이 레이어 100%.
        private static void ApplyTerrain(Scene scene, TerrainLayer layer, StringBuilder problems)
        {
            int count = 0;
            foreach (Terrain terrain in AllInScene<Terrain>(scene))
            {
                TerrainData data = terrain.terrainData;
                if (data == null)
                    continue;
                count++;
                var layers = new List<TerrainLayer>(data.terrainLayers);
                if (layers.Contains(layer))
                    continue;
                bool wasEmpty = layers.Count == 0;
                if (wasEmpty)
                    data.alphamapResolution = SingleLayerAlphamapResolution;
                layers.Add(layer);
                data.terrainLayers = layers.ToArray();
                if (wasEmpty)
                {
                    int res = data.alphamapResolution;
                    var weights = new float[res, res, 1];
                    for (int z = 0; z < res; z++)
                    for (int x = 0; x < res; x++)
                        weights[z, x, 0] = 1f;
                    data.SetAlphamaps(0, 0, weights);
                }
                EditorUtility.SetDirty(data);
            }
            if (count == 0)
                problems.AppendLine("Terrain 없음");
        }

        // 블록아웃(Environment 아래 "{Shape}_{nn}" 구조물), 루트 상자(LootContainer), 전원 스위치(PowerSwitch)에 팔레트 머티리얼 + GroundSurface.
        // 표에 없는 구조물은 바꾸지 않고 경고로 알린다(경계 벽은 Environment 밖이라 대상 아님).
        private static int ApplyBlockout(Scene scene, StringBuilder problems)
        {
            Dictionary<PaletteMaterialMap.Key, Material> materials = LoadPaletteMaterials();
            int painted = 0;
            GameObject environment = SetupUtil.FindRoot(scene, M4MapSetup.EnvironmentName);
            if (environment == null)
            {
                problems.AppendLine($"{M4MapSetup.EnvironmentName} 루트 없음 — 블록아웃 머티리얼을 칠하지 않았다");
            }
            else
            {
                var unknown = new List<string>();
                foreach (Transform group in environment.transform)
                    painted += PaintGroup(group, materials, unknown);
                if (unknown.Count > 0)
                    problems.AppendLine($"매핑 표에 없는 구조물 {unknown.Count}개(그대로 둠): {string.Join(", ", unknown)}");
            }

            foreach (PowerSwitch powerSwitch in AllInScene<PowerSwitch>(scene))
                painted += Paint(powerSwitch.gameObject, PaletteMaterialMap.PowerSwitchRule, materials);
            return painted;
        }

        internal static Dictionary<PaletteMaterialMap.Key, Material> LoadPaletteMaterials()
        {
            var materials = new Dictionary<PaletteMaterialMap.Key, Material>();
            foreach (KeyValuePair<PaletteMaterialMap.Key, PaletteMaterialMap.Spec> pair in PaletteMaterialMap.Materials)
                materials[pair.Key] = AssetDatabase.LoadAssetAtPath<Material>(pair.Value.Path);
            return materials;
        }

        // Environment 그룹 하나의 구조물("{Shape}_{nn}"·상자)을 칠한다. 프리팹 모델을 가진 구조물(맵 개선 랜드마크·광원 소품)은
        // 프리팹이 자기 팔레트 머티리얼·GroundSurface를 가지므로 건너뛴다(맵 개선 §5). 표에 없는 구조물은 unknown에 더한다.
        internal static int PaintGroup(Transform group, Dictionary<PaletteMaterialMap.Key, Material> materials, List<string> unknown)
        {
            int painted = 0;
            foreach (Transform structure in group)
            {
                if (HasPrefabModel(structure))
                    continue;
                PaletteMaterialMap.Rule rule;
                if (structure.GetComponent<LootContainer>() != null)
                    rule = PaletteMaterialMap.CrateRule;
                else if (!PaletteMaterialMap.TryGetStructureRule(structure.name, out rule))
                {
                    if (structure.GetComponentInChildren<MeshRenderer>(true) != null)
                        unknown.Add($"{group.name}/{structure.name}");
                    continue;
                }
                painted += Paint(structure.gameObject, rule, materials);
            }
            return painted;
        }

        private static bool HasPrefabModel(Transform structure)
        {
            foreach (Transform child in structure)
                if (PrefabUtility.IsOutermostPrefabInstanceRoot(child.gameObject))
                    return true;
            return false;
        }

        // root와 그 아래의 MeshRenderer에 머티리얼, 트리거 아닌 콜라이더가 있는 오브젝트에 GroundSurface. 칠한 렌더러 수.
        private static int Paint(GameObject root, PaletteMaterialMap.Rule rule, Dictionary<PaletteMaterialMap.Key, Material> materials)
        {
            materials.TryGetValue(rule.Material, out Material material);
            int count = 0;
            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (material != null && renderer.sharedMaterial != material)
                {
                    renderer.sharedMaterial = material;
                    EditorUtility.SetDirty(renderer);
                }
                count++;
            }
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
                if (!collider.isTrigger)
                    SetSurface(collider.gameObject, rule.Surface);
            return count;
        }

        private static void SetSurface(GameObject go, SurfaceKind kind)
        {
            var surface = SetupUtil.GetOrAdd<GroundSurface>(go);
            var so = new SerializedObject(surface);
            SerializedProperty property = so.FindProperty("kind");
            if (property.intValue != (int)kind)
            {
                property.intValue = (int)kind;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        // 저수지 바닥 물웅덩이 3개: 얇은 상자(수면 = 발자국 범위의 가장 높은 지면 + 3cm), 트리거 + Ignore Raycast(전투·이동·상호작용 무영향),
        // M_Puddle + GroundSurface(Water). 씬 최상위 "M6_Puddles" 아래(Environment 밖 — NavMesh 굽기 대상 아님).
        private static void ApplyPuddles(Scene scene, StringBuilder problems)
        {
            Terrain terrain = null;
            foreach (Terrain t in AllInScene<Terrain>(scene))
            {
                terrain = t;
                break;
            }
            if (terrain == null)
                problems.AppendLine("Terrain 없음 — 웅덩이를 y=0 기준으로 놓는다");
            var material = AssetDatabase.LoadAssetAtPath<Material>(PaletteMaterialMap.Materials[PaletteMaterialMap.Key.Puddle].Path);

            GameObject root = SetupUtil.FindOrCreateRoot(scene, M6Names.PuddlesObject);
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            root.transform.localScale = Vector3.one;
            for (int i = 0; i < Puddles.Length; i++)
            {
                (Vector2 xz, Vector2 size, float yawDeg) = Puddles[i];
                string name = $"Puddle_{i + 1:00}";
                Transform child = root.transform.Find(name);
                GameObject go;
                if (child != null)
                {
                    go = child.gameObject;
                }
                else
                {
                    go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.name = name;
                    go.transform.SetParent(root.transform, false);
                }
                Quaternion yaw = Quaternion.Euler(0f, yawDeg, 0f);
                GroundSpan(terrain, xz, size, yaw, out float minY, out float maxY);
                float top = maxY + PuddleTopAboveGroundM;
                float bottom = minY - PuddleBelowGroundM;
                go.transform.SetPositionAndRotation(new Vector3(xz.x, (top + bottom) * 0.5f, xz.y), yaw);
                go.transform.localScale = new Vector3(size.x, top - bottom, size.y);
                go.layer = IgnoreRaycastLayer;
                GameObjectUtility.SetStaticEditorFlags(go, 0);

                var box = SetupUtil.GetOrAdd<BoxCollider>(go);
                box.isTrigger = true;
                box.center = Vector3.zero;
                box.size = Vector3.one;
                var renderer = SetupUtil.GetOrAdd<MeshRenderer>(go);
                if (material != null)
                    renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = true;

                var surface = SetupUtil.GetOrAdd<GroundSurface>(go);
                var so = new SerializedObject(surface);
                so.FindProperty("kind").intValue = (int)PaletteMaterialMap.PuddleRule.Surface;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(go);
            }
        }

        // 회전한 사각형 네 모서리 + 중심 + 변 중점의 지면 높이 범위
        private static void GroundSpan(Terrain terrain, Vector2 xz, Vector2 size, Quaternion yaw, out float minY, out float maxY)
        {
            minY = float.MaxValue;
            maxY = float.MinValue;
            for (int sx = -1; sx <= 1; sx++)
            for (int sz = -1; sz <= 1; sz++)
            {
                Vector3 c = yaw * new Vector3(sx * size.x * 0.5f, 0f, sz * size.y * 0.5f);
                float y = M4MapSetup.GroundY(terrain, xz.x + c.x, xz.y + c.z);
                minY = Mathf.Min(minY, y);
                maxY = Mathf.Max(maxY, y);
            }
        }

        // 재 파티클 (§10.2 플레이어 주변 30m, Q2 Particle System). Player 루트 자식, World 시뮬레이션 → 떨어진 재는 제자리, 새 재는 플레이어 주변.
        private static void ApplyAshParticles(Scene scene, AtmosphereConfigSO cfg, Material material, StringBuilder problems)
        {
            PlayerMotor player = null;
            foreach (PlayerMotor motor in AllInScene<PlayerMotor>(scene))
            {
                player = motor;
                break;
            }
            if (player == null)
            {
                problems.AppendLine("Player 없음 — 재 파티클을 만들지 않았다");
                return;
            }

            Transform existing = player.transform.Find(M6Names.AshParticlesObject);
            GameObject go;
            if (existing != null)
            {
                go = existing.gameObject;
            }
            else
            {
                go = new GameObject(M6Names.AshParticlesObject);
                go.transform.SetParent(player.transform, false);
            }
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            var ps = SetupUtil.GetOrAdd<ParticleSystem>(go);
            if (ps.isPlaying)
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); // duration은 멈춘 상태에서만 바꿀 수 있다
            float lifetime = Mathf.Max(0.1f, cfg.ashLifetimeSec);

            ParticleSystem.MainModule main = ps.main;
            main.duration = lifetime;
            main.loop = true;
            main.prewarm = true;
            main.playOnAwake = true;
            main.startDelay = 0f;
            main.startLifetime = lifetime;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(cfg.ashSizeM.x, cfg.ashSizeM.y);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = Color.white; // 색·알파는 머티리얼(M6_AshParticle = ashToken × ashAlpha)
            main.gravityModifier = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = Mathf.Max(1, cfg.ashMaxParticles);
            main.stopAction = ParticleSystemStopAction.None;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = cfg.ashRatePerSec;
            emission.rateOverDistance = 0f;

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.boxThickness = Vector3.zero;
            shape.position = new Vector3(0f, cfg.ashHeightM * cfg.ashBoxCenterRatio, 0f);
            shape.rotation = Vector3.zero;
            shape.scale = new Vector3(cfg.ashRadiusM * 2f, cfg.ashHeightM, cfg.ashRadiusM * 2f);

            ParticleSystem.VelocityOverLifetimeModule velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = new ParticleSystem.MinMaxCurve(-cfg.ashFallSpeedMps.y, -cfg.ashFallSpeedMps.x);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            ParticleSystem.NoiseModule noise = ps.noise;
            noise.enabled = cfg.ashNoiseStrength > 0f;
            noise.strength = cfg.ashNoiseStrength;
            noise.frequency = cfg.ashNoiseFrequency;
            noise.quality = ParticleSystemNoiseQuality.Medium;
            noise.damping = true;

            ParticleSystem.CollisionModule collision = ps.collision;
            collision.enabled = false;
            ParticleSystem.LightsModule lights = ps.lights;
            lights.enabled = false;

            var renderer = SetupUtil.GetOrAdd<ParticleSystemRenderer>(go);
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortMode = ParticleSystemSortMode.None;
            renderer.minParticleSize = 0f;
            renderer.maxParticleSize = cfg.ashMaxScreenSize;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            EditorUtility.SetDirty(ps);
            EditorUtility.SetDirty(renderer);
        }

        private static IEnumerable<T> AllInScene<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            foreach (T component in root.GetComponentsInChildren<T>(true))
                yield return component;
        }
    }
}
