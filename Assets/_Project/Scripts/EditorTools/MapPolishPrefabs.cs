using System;
using System.Collections.Generic;
using Ashfall.Audio;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Ashfall.EditorTools
{
    // 맵 개선 랜드마크·광원 소품 프리팹 (docs/design/MapPolish_design.md §3·§4.1). 프리미티브 블록아웃 자리표시 — 모양·크기 전부 TODO(GDD-?).
    // 프리팹은 **없을 때만** 만든다: 같은 경로에 실제 모델 프리팹을 덮어쓰면 다음 실행에도 유지된다(§2.3 에셋 교체 방식과 같다).
    // 원점 = 지면 중심(씬 래퍼가 발자국의 가장 낮은 지면에 놓는다), +z = 앞. 기초는 지면 아래로 내려가 경사 틈을 막는다.
    // 렌더러는 팔레트 머티리얼(M6 레이드 시각 테스트: Environment 아래 렌더러 = 팔레트 폴더), 트리거 아닌 콜라이더에는 GroundSurface.
    // 발광면(M_EmberGlow·M_LampGlow)은 콜라이더·그림자 없음. 라이트는 프리팹에 넣지 않는다 — 씬 래퍼의 "Light"가 SO 값으로 만든다(§3).
    public static class MapPolishPrefabs
    {
        private const string Tag = "[MapPolishPrefabs]";

        public const string LandmarkFolder = M7Names.PrefabFolder + "/Landmarks";
        public const string PropFolder = M7Names.PrefabFolder + "/Props";

        public enum LandmarkKind
        {
            WaterTower,
            BrokenPylon,
            CrashedPlane,
            Billboard,
            BrokenOverpass,
            FlareStack,
            RadioMast,
        }

        public enum PropKind
        {
            FireBarrel,
            StreetLamp,
        }

        public enum LightKind
        {
            Fire,   // AtmosphereConfigSO.fireLight
            Lamp,   // AtmosphereConfigSO.lampLight
        }

        // 부품 하나 (원점 기준 로컬). 실린더는 프리미티브 그대로(지름 1·높이 2 → scale.y = 높이/2).
        public readonly struct Part
        {
            public readonly string Name;
            public readonly PrimitiveType Shape;
            public readonly Vector3 Position;
            public readonly Vector3 Scale;
            public readonly Vector3 Euler;
            public readonly PaletteMaterialMap.Key Material;

            public Part(string name, PrimitiveType shape, Vector3 position, Vector3 scale, Vector3 euler, PaletteMaterialMap.Key material)
            {
                Name = name;
                Shape = shape;
                Position = position;
                Scale = scale;
                Euler = euler;
                Material = material;
            }

            // 발광면은 콜라이더·그림자 없음
            public bool IsGlow => Material == PaletteMaterialMap.Key.EmberGlow || Material == PaletteMaterialMap.Key.LampGlow;
        }

        // 엄폐 상자 (원점 기준 로컬 중심 XZ·크기·회전). 씬 적용이 M4 엄폐 규칙(BlockoutBuilder)으로 엄폐 지점을 만든다.
        // 길이 = size.x, 엄폐 지점은 로컬 ±z 면. Around = 4방향 Low(차량 규칙).
        public readonly struct CoverBox
        {
            public readonly Vector2 CenterXZ;
            public readonly Vector3 Size;
            public readonly float YawDeg;
            public readonly bool Around;

            public CoverBox(Vector2 centerXZ, Vector3 size, float yawDeg, bool around = false)
            {
                CenterXZ = centerXZ;
                Size = size;
                YawDeg = yawDeg;
                Around = around;
            }
        }

        // 프리팹 래퍼에 붙는 점광원 (원점 기준 로컬 위치)
        public readonly struct LightMount
        {
            public readonly LightKind Kind;
            public readonly Vector3 Local;

            public LightMount(LightKind kind, Vector3 local)
            {
                Kind = kind;
                Local = local;
            }
        }

        public sealed class Spec
        {
            public readonly string Name;            // 프리팹 이름 (LM_*/LP_*)
            public readonly string Path;
            public readonly Vector2 FootprintXZ;    // 지면 샘플·배치 검사 발자국 (로컬 x·z, 회전 전)
            public readonly CoverBox[] Covers;
            public readonly LightMount[] Lights;
            public readonly Part[] Parts;

            public Spec(string name, string folder, Vector2 footprintXZ, CoverBox[] covers, LightMount[] lights, params Part[] parts)
            {
                Name = name;
                Path = folder + "/" + name + ".prefab";
                FootprintXZ = footprintXZ;
                Covers = covers ?? Array.Empty<CoverBox>();
                Lights = lights ?? Array.Empty<LightMount>();
                Parts = parts;
            }

            // 발자국 외접원 반지름 (XZ)
            public float FootprintRadiusM => FootprintXZ.magnitude * 0.5f;
        }

        private const PaletteMaterialMap.Key Rust = PaletteMaterialMap.Key.RustMetal;
        private const PaletteMaterialMap.Key Concrete = PaletteMaterialMap.Key.Concrete;
        private const PaletteMaterialMap.Key Debris = PaletteMaterialMap.Key.Debris;
        private const PaletteMaterialMap.Key Ember = PaletteMaterialMap.Key.EmberGlow;
        private const PaletteMaterialMap.Key Lamp = PaletteMaterialMap.Key.LampGlow;
        private const float FoundationDepthM = 3f;   // 기초가 지면 아래로 내려가는 깊이 (경사 틈 막기)

        private static Part Box(string name, Vector3 position, Vector3 scale, PaletteMaterialMap.Key material, Vector3 euler = default) =>
            new Part(name, PrimitiveType.Cube, position, scale, euler, material);

        // 세운 원통: 중심, 지름, 높이
        private static Part Cyl(string name, Vector3 position, float diameter, float height, PaletteMaterialMap.Key material, Vector3 euler = default) =>
            new Part(name, PrimitiveType.Cylinder, position, new Vector3(diameter, height * 0.5f, diameter), euler, material);

        // a → b를 잇는 각진 막대 (격자 철탑 다리)
        private static Part Strut(string name, Vector3 a, Vector3 b, float thickness, PaletteMaterialMap.Key material)
        {
            Vector3 d = b - a;
            Quaternion rotation = Quaternion.FromToRotation(Vector3.up, d.normalized);
            return new Part(name, PrimitiveType.Cube, (a + b) * 0.5f, new Vector3(thickness, d.magnitude, thickness), rotation.eulerAngles, material);
        }

        // ── 랜드마크 (§4.1) ──

        private static Spec WaterTower()
        {
            var parts = new List<Part>();
            float legTop = 24f;
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
            {
                parts.Add(Box($"Footing_{parts.Count}", new Vector3(sx * 4f, -FoundationDepthM * 0.5f + 0.25f, sz * 4f), new Vector3(1.6f, FoundationDepthM + 0.5f, 1.6f), Concrete));
                parts.Add(Box($"Leg_{parts.Count}", new Vector3(sx * 4f, legTop * 0.5f, sz * 4f), new Vector3(0.7f, legTop, 0.7f), Rust));
            }
            foreach (float y in new[] { 9f, 17f })
            {
                parts.Add(Box($"Brace_X_{y}", new Vector3(0f, y, -4f), new Vector3(8.7f, 0.3f, 0.3f), Rust));
                parts.Add(Box($"Brace_X2_{y}", new Vector3(0f, y, 4f), new Vector3(8.7f, 0.3f, 0.3f), Rust));
                parts.Add(Box($"Brace_Z_{y}", new Vector3(-4f, y, 0f), new Vector3(0.3f, 0.3f, 8.7f), Rust));
                parts.Add(Box($"Brace_Z2_{y}", new Vector3(4f, y, 0f), new Vector3(0.3f, 0.3f, 8.7f), Rust));
            }
            parts.Add(Box("Deck", new Vector3(0f, legTop + 0.2f, 0f), new Vector3(11f, 0.4f, 11f), Rust));
            parts.Add(Cyl("Tank", new Vector3(0f, legTop + 0.4f + 3.5f, 0f), 10f, 7f, Rust));
            parts.Add(Cyl("Roof", new Vector3(0f, legTop + 7.9f + 0.6f, 0f), 7f, 1.2f, Rust));
            parts.Add(Box("Ladder", new Vector3(0f, legTop * 0.5f, -4.6f), new Vector3(0.6f, legTop, 0.12f), Rust));
            return new Spec("LM_WaterTower", LandmarkFolder, new Vector2(11f, 11f), null, null, parts.ToArray());
        }

        // 기울어진 격자 송전탑. "Tower" 부품만 기울이고(원점 기준 z축 −15°) 기초 4개는 곧게 둔다 — 들린 쪽 다리 밑을 기초가 가린다.
        private static Spec BrokenPylon()
        {
            var parts = new List<Part>();
            Quaternion tilt = Quaternion.Euler(0f, 0f, -15f);
            Vector3 T(Vector3 p) => tilt * p;
            float h = 28f;
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
            {
                parts.Add(Box($"Footing_{parts.Count}", new Vector3(sx * 3.5f, -FoundationDepthM * 0.5f + 0.75f, sz * 3.5f), new Vector3(1.8f, FoundationDepthM + 1.5f, 1.8f), Concrete));
                parts.Add(Strut($"Leg_{parts.Count}", T(new Vector3(sx * 3.5f, 0f, sz * 3.5f)), T(new Vector3(sx * 1f, h, sz * 1f)), 0.45f, Rust));
            }
            foreach ((float y, float half) in new[] { (8f, 2.9f), (16f, 2.2f), (22f, 1.6f) })
            {
                parts.Add(Strut($"Ring_{y}_a", T(new Vector3(-half, y, -half)), T(new Vector3(half, y, -half)), 0.25f, Rust));
                parts.Add(Strut($"Ring_{y}_b", T(new Vector3(-half, y, half)), T(new Vector3(half, y, half)), 0.25f, Rust));
                parts.Add(Strut($"Ring_{y}_c", T(new Vector3(-half, y, -half)), T(new Vector3(-half, y, half)), 0.25f, Rust));
                parts.Add(Strut($"Ring_{y}_d", T(new Vector3(half, y, -half)), T(new Vector3(half, y, half)), 0.25f, Rust));
            }
            parts.Add(Strut("Arm_Low", T(new Vector3(-6f, 20f, 0f)), T(new Vector3(6f, 20f, 0f)), 0.5f, Rust));
            parts.Add(Strut("Arm_High", T(new Vector3(-4.5f, 25f, 0f)), T(new Vector3(4.5f, 25f, 0f)), 0.5f, Rust));
            // 꺾인 꼭대기 (위쪽 6m가 옆으로 접혔다)
            parts.Add(Strut("BentTop", T(new Vector3(0f, h, 0f)), T(new Vector3(4.5f, h + 3f, 1f)), 0.5f, Rust));
            parts.Add(Box("Beacon", T(new Vector3(4.6f, h + 3.2f, 1f)), new Vector3(0.7f, 0.7f, 0.7f), Ember));
            return new Spec("LM_BrokenPylon", LandmarkFolder, new Vector2(9f, 9f), null, null, parts.ToArray());
        }

        // 추락한 항공기: 동체(z축 원통) + 떨어져 나간 조종석 + 부러진 날개 + 꼬리 날개, 잔불(발광면 + 점광원 1)
        private static Spec CrashedPlane()
        {
            var alongZ = new Vector3(90f, 0f, 0f);
            var parts = new List<Part>
            {
                Cyl("Fuselage", new Vector3(0f, 1.6f, 0f), 4.5f, 26f, Rust, new Vector3(92f, 0f, 6f)),
                Cyl("Cockpit", new Vector3(4f, 1.4f, 18f), 4f, 6f, Rust, new Vector3(90f, 28f, 0f)),
                Box("Wing_L", new Vector3(-9.5f, 0.9f, 1f), new Vector3(15f, 0.6f, 4f), Rust, new Vector3(0f, 6f, -5f)),
                Box("Wing_R_Broken", new Vector3(9f, 0.4f, -4f), new Vector3(6f, 0.6f, 3.5f), Rust, new Vector3(0f, -30f, 9f)),
                Cyl("Engine", new Vector3(-7f, 0.9f, 2.5f), 2f, 3.5f, Rust, alongZ),
                Box("TailFin", new Vector3(0f, 5.6f, -12f), new Vector3(0.5f, 6f, 4f), Rust, new Vector3(0f, 0f, 8f)),
                Box("Stabilizer", new Vector3(0f, 3.3f, -12.2f), new Vector3(8f, 0.4f, 2.5f), Rust),
                Box("Debris_A", new Vector3(-4f, 0.4f, 12f), new Vector3(3f, 0.8f, 2f), Debris, new Vector3(0f, 35f, 0f)),
                Box("Debris_B", new Vector3(5f, 0.3f, -15f), new Vector3(2.5f, 0.6f, 2f), Debris, new Vector3(0f, -20f, 0f)),
                Box("Embers_A", new Vector3(3.2f, 0.12f, 5f), new Vector3(2f, 0.24f, 1.4f), Ember, new Vector3(0f, 15f, 0f)),
                Box("Embers_B", new Vector3(-3.4f, 0.12f, -6f), new Vector3(1.6f, 0.24f, 1.2f), Ember, new Vector3(0f, -10f, 0f)),
                Box("Flame", new Vector3(3.2f, 0.6f, 5f), new Vector3(0.6f, 0.9f, 0.6f), Ember),
            };
            var covers = new[] { new CoverBox(Vector2.zero, new Vector3(26f, 4f, 4.5f), 90f) };
            var lights = new[] { new LightMount(LightKind.Fire, new Vector3(3.2f, 1.8f, 5f)) };
            return new Spec("LM_CrashedPlane", LandmarkFolder, new Vector2(30f, 38f), covers, lights, parts.ToArray());
        }

        private static Spec Billboard()
        {
            var parts = new[]
            {
                Box("Post_L", new Vector3(-5f, 5f, 0f), new Vector3(0.6f, 13f, 0.6f), Rust),
                Box("Post_R", new Vector3(5f, 5f, 0f), new Vector3(0.6f, 13f, 0.6f), Rust),
                Box("Footing_L", new Vector3(-5f, -1f, 0f), new Vector3(1.4f, 2.6f, 1.4f), Concrete),
                Box("Footing_R", new Vector3(5f, -1f, 0f), new Vector3(1.4f, 2.6f, 1.4f), Concrete),
                Box("Board", new Vector3(0f, 13.2f, 0f), new Vector3(14f, 6f, 0.4f), Rust, new Vector3(0f, 0f, 4f)),
                Box("Frame", new Vector3(0f, 10.1f, -0.35f), new Vector3(13f, 0.3f, 0.3f), Concrete),
                Box("FallenPanel", new Vector3(3f, 0.15f, 3f), new Vector3(4f, 0.3f, 3f), Rust, new Vector3(0f, 20f, 0f)),
            };
            return new Spec("LM_Billboard", LandmarkFolder, new Vector2(15f, 7f), null, null, parts);
        }

        // 끊어진 고가: 교각 3 + 상판 2(가운데 10m 끊김) + 땅에 떨어진 상판. 상판 위는 닿을 수 없다(경사로 없음).
        private static Spec BrokenOverpass()
        {
            var parts = new List<Part>();
            float[] pierZ = { -30f, 0f, 30f };
            foreach (float z in pierZ)
                parts.Add(Box($"Pier_{z}", new Vector3(0f, 3.5f, z), new Vector3(3f, 13f, 2.5f), Concrete));
            parts.Add(Box("Deck_S", new Vector3(0f, 10.6f, -15f), new Vector3(12f, 1.2f, 32f), Concrete));
            parts.Add(Box("Deck_N", new Vector3(0f, 10.6f, 21f), new Vector3(12f, 1.2f, 20f), Concrete));
            parts.Add(Box("Rail_S_L", new Vector3(-5.85f, 11.7f, -15f), new Vector3(0.3f, 1f, 32f), Concrete));
            parts.Add(Box("Rail_S_R", new Vector3(5.85f, 11.7f, -15f), new Vector3(0.3f, 1f, 32f), Concrete));
            parts.Add(Box("Rail_N_L", new Vector3(-5.85f, 11.7f, 21f), new Vector3(0.3f, 1f, 20f), Concrete));
            parts.Add(Box("Rail_N_R", new Vector3(5.85f, 11.7f, 21f), new Vector3(0.3f, 1f, 20f), Concrete));
            parts.Add(Box("FallenDeck", new Vector3(2f, 1.2f, 6f), new Vector3(12f, 1.2f, 9f), Concrete, new Vector3(12f, 10f, 7f)));
            parts.Add(Box("Rubble_A", new Vector3(-4f, 0.5f, 9f), new Vector3(3f, 1f, 2.5f), Debris, new Vector3(0f, 25f, 0f)));
            parts.Add(Box("Rubble_B", new Vector3(5f, 0.4f, 1f), new Vector3(2.5f, 0.8f, 2f), Debris, new Vector3(0f, -15f, 0f)));
            var covers = new List<CoverBox>();
            foreach (float z in pierZ)
                covers.Add(new CoverBox(new Vector2(0f, z), new Vector3(3f, 3f, 2.5f), 0f));
            return new Spec("LM_BrokenOverpass", LandmarkFolder, new Vector2(14f, 64f), covers.ToArray(), null, parts.ToArray());
        }

        private static Spec FlareStack()
        {
            var parts = new[]
            {
                Box("Base", new Vector3(0f, -1f, 0f), new Vector3(5f, 4f, 5f), Concrete),
                Cyl("Stack", new Vector3(0f, 1f + 22f, 0f), 2.5f, 44f, Rust),
                Cyl("Band_Low", new Vector3(0f, 15f, 0f), 2.9f, 0.4f, Rust),
                Cyl("Band_High", new Vector3(0f, 30f, 0f), 2.9f, 0.4f, Rust),
                Cyl("Tip", new Vector3(0f, 45.6f, 0f), 2.7f, 1.2f, Ember),
                Box("Flame", new Vector3(0f, 47.2f, 0f), new Vector3(1.2f, 2f, 1.2f), Ember),
            };
            var covers = new[] { new CoverBox(Vector2.zero, new Vector3(5f, 1f, 5f), 0f, around: true) };
            return new Spec("LM_FlareStack", LandmarkFolder, new Vector2(5f, 5f), covers, null, parts);
        }

        private static Spec RadioMast()
        {
            var parts = new[]
            {
                Box("Base", new Vector3(0f, -1.25f, 0f), new Vector3(3f, 3.5f, 3f), Concrete),
                Box("Mast", new Vector3(0f, 25.5f, 0f), new Vector3(0.8f, 50f, 0.8f), Rust),
                Box("Arm_20", new Vector3(0f, 20f, 0f), new Vector3(3f, 0.2f, 0.2f), Rust),
                Box("Arm_35", new Vector3(0f, 35f, 0f), new Vector3(0.2f, 0.2f, 3f), Rust),
                Box("Arm_48", new Vector3(0f, 48f, 0f), new Vector3(2.4f, 0.2f, 0.2f), Rust),
                Cyl("Dish", new Vector3(0f, 30f, 0.7f), 2f, 0.2f, Rust, new Vector3(90f, 0f, 0f)),
                Box("Beacon_Top", new Vector3(0f, 51f, 0f), new Vector3(0.9f, 0.9f, 0.9f), Ember),
                Box("Beacon_Mid", new Vector3(1.5f, 35.3f, 0f), new Vector3(0.5f, 0.5f, 0.5f), Ember),
            };
            return new Spec("LM_RadioMast", LandmarkFolder, new Vector2(3f, 3f), null, null, parts);
        }

        // ── 광원 소품 (§3) ──

        private static Spec FireBarrel()
        {
            var parts = new[]
            {
                Cyl("Drum", new Vector3(0f, 0.45f, 0f), 0.6f, 0.9f, Rust),
                Cyl("Embers", new Vector3(0f, 0.92f, 0f), 0.5f, 0.06f, Ember),
                Box("Flame", new Vector3(0f, 1.15f, 0f), new Vector3(0.32f, 0.45f, 0.32f), Ember, new Vector3(0f, 45f, 0f)),
            };
            var lights = new[] { new LightMount(LightKind.Fire, new Vector3(0f, 1.6f, 0f)) };
            return new Spec("LP_FireBarrel", PropFolder, new Vector2(0.7f, 0.7f), null, lights, parts);
        }

        private static Spec StreetLamp()
        {
            var parts = new[]
            {
                Box("Footing", new Vector3(0f, -0.4f, 0f), new Vector3(0.5f, 1.2f, 0.5f), Concrete),
                Box("Pole", new Vector3(0f, 3.1f, 0f), new Vector3(0.2f, 6.4f, 0.2f), Rust, new Vector3(0f, 0f, -4f)),
                Box("Arm", new Vector3(1.05f, 6.3f, 0f), new Vector3(1.7f, 0.12f, 0.12f), Rust),
                Box("Head", new Vector3(1.8f, 6.2f, 0f), new Vector3(0.6f, 0.2f, 0.35f), Lamp),
            };
            var lights = new[] { new LightMount(LightKind.Lamp, new Vector3(1.8f, 5.8f, 0f)) };
            return new Spec("LP_StreetLamp", PropFolder, new Vector2(2.2f, 0.6f), null, lights, parts);
        }

        public static readonly IReadOnlyDictionary<LandmarkKind, Spec> Landmarks = new Dictionary<LandmarkKind, Spec>
        {
            { LandmarkKind.WaterTower, WaterTower() },
            { LandmarkKind.BrokenPylon, BrokenPylon() },
            { LandmarkKind.CrashedPlane, CrashedPlane() },
            { LandmarkKind.Billboard, Billboard() },
            { LandmarkKind.BrokenOverpass, BrokenOverpass() },
            { LandmarkKind.FlareStack, FlareStack() },
            { LandmarkKind.RadioMast, RadioMast() },
        };

        public static readonly IReadOnlyDictionary<PropKind, Spec> Props = new Dictionary<PropKind, Spec>
        {
            { PropKind.FireBarrel, FireBarrel() },
            { PropKind.StreetLamp, StreetLamp() },
        };

        public static IEnumerable<Spec> AllSpecs()
        {
            foreach (Spec spec in Landmarks.Values)
                yield return spec;
            foreach (Spec spec in Props.Values)
                yield return spec;
        }

        [MenuItem("Ashfall/Setup/Map Polish/Create Landmark Prefabs")]
        public static void CreatePrefabsMenu()
        {
            if (!CreatePrefabs())
                throw new InvalidOperationException($"{Tag} 실패 — 위 오류 로그 확인");
        }

        // 없는 프리팹만 만든다. 팔레트 머티리얼이 없으면 false(먼저 M6 아트 — 맵 개선 메뉴가 M6ArtSetup.CreateAssets를 먼저 부른다).
        public static bool CreatePrefabs()
        {
            Dictionary<PaletteMaterialMap.Key, Material> materials = M6VisualSceneApply.LoadPaletteMaterials();
            foreach (KeyValuePair<PaletteMaterialMap.Key, Material> pair in materials)
                if (pair.Value == null)
                {
                    Debug.LogError($"{Tag} 팔레트 머티리얼 없음: {PaletteMaterialMap.Materials[pair.Key].Path} — 먼저 Ashfall/Setup/Apply M6 Art & Audio");
                    return false;
                }
            SetupUtil.EnsureFolder(LandmarkFolder);
            SetupUtil.EnsureFolder(PropFolder);
            bool ok = true;
            foreach (Spec spec in AllSpecs())
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(spec.Path) != null)
                    continue;
                ok &= Build(spec, materials) != null;
            }
            return ok;
        }

        private static GameObject Build(Spec spec, Dictionary<PaletteMaterialMap.Key, Material> materials)
        {
            var root = new GameObject(spec.Name);
            try
            {
                foreach (Part part in spec.Parts)
                {
                    GameObject go = GameObject.CreatePrimitive(part.Shape);
                    go.name = part.Name;
                    go.transform.SetParent(root.transform, false);
                    go.transform.localPosition = part.Position;
                    go.transform.localRotation = Quaternion.Euler(part.Euler);
                    go.transform.localScale = part.Scale;
                    GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
                    var renderer = go.GetComponent<MeshRenderer>();
                    renderer.sharedMaterial = materials[part.Material];
                    Object.DestroyImmediate(go.GetComponent<Collider>());
                    if (part.IsGlow)
                    {
                        renderer.shadowCastingMode = ShadowCastingMode.Off;
                        continue;
                    }
                    // 원통은 캡슐 대신 메시 콜라이더 (M4 탱크와 같음 — 끝까지 막히게)
                    if (part.Shape == PrimitiveType.Cylinder)
                        go.AddComponent<MeshCollider>().sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
                    else
                        go.AddComponent<BoxCollider>();
                    SetSurface(go.AddComponent<GroundSurface>(), part.Material == Concrete || part.Material == Debris ? SurfaceKind.Debris : SurfaceKind.Metal);
                }

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, spec.Path, out bool success);
                if (!success || saved == null)
                {
                    Debug.LogError($"{Tag} 프리팹 저장 실패: {spec.Path}");
                    return null;
                }
                Debug.Log($"{Tag} 프리팹 생성: {spec.Path} (부품 {spec.Parts.Length})");
                return saved;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void SetSurface(GroundSurface surface, SurfaceKind kind)
        {
            var so = new SerializedObject(surface);
            so.FindProperty("kind").intValue = (int)kind;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
