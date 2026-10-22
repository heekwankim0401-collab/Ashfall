using System;
using System.Collections.Generic;
using Ashfall.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Ashfall.EditorTools
{
    // M7 7A 자리표시 1인칭 뷰모델 프리팹 (M7 설계 §2.1·§2.2·§4.10, Q4, SYS-WPN). 전부 TODO(GDD-?) 자리표시 — 실제 모델은 §2.3.
    // 무기 5종(PST·SMG·RIF·SHT·DMR, 적 전용 LMG 제외)마다 프리미티브 조합(길이·굵기로 구분) + 팔레트 머티리얼(M_RustMetal·M_Debris),
    // 자식 "Muzzle"(M7Names.MuzzleChild) = 총구. 콜라이더 없음, 그림자 없음. 루트 원점 = 손잡이 근처(컨트롤러가 hip/ads 위치에 둔다).
    // 규칙(M6 WAV와 같음): 같은 경로에 프리팹이 있으면 덮지 않는다(사용자 교체 보존 — 다시 만들려면 파일을 지운다).
    //   WeaponDefinitionSO.viewModel은 비어 있을 때만 채운다. 두 번째 실행은 아무 파일도 바꾸지 않는다.
    // 먼저 무기 데이터(Ashfall/Setup/Create M7 Weapon Data)와 팔레트 머티리얼(M6 Apply)이 있어야 한다. 머티리얼이 없으면 기본 머티리얼 + 경고.
    // 배치모드: -executeMethod Ashfall.EditorTools.M7WeaponAssets.CreateViewModels
    public static class M7WeaponAssets
    {
        private const string Tag = "[M7WeaponAssets]";

        // 부품 하나 (루트 기준 로컬 좌표, z+ = 총구 쪽). 실린더는 축이 z가 되도록 X 90° 회전, 길이 = scale.y × 2.
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
        }

        public sealed class Spec
        {
            public readonly string WeaponId;
            public readonly string WeaponPath;
            public readonly Vector3 Muzzle;
            public readonly Part[] Parts;

            public Spec(string weaponId, string weaponPath, Vector3 muzzle, params Part[] parts)
            {
                WeaponId = weaponId;
                WeaponPath = weaponPath;
                Muzzle = muzzle;
                Parts = parts;
            }

            public string PrefabPath => M7Names.ViewModelPrefabPath(WeaponId);
        }

        private const PaletteMaterialMap.Key Metal = PaletteMaterialMap.Key.RustMetal;
        private const PaletteMaterialMap.Key Grip = PaletteMaterialMap.Key.Debris;
        private static readonly Vector3 AlongZ = new Vector3(90f, 0f, 0f);

        private static Part Box(string name, Vector3 position, Vector3 scale, PaletteMaterialMap.Key material, Vector3 euler = default) =>
            new Part(name, PrimitiveType.Cube, position, scale, euler, material);

        // 축 z 실린더: 지름 d, 길이 len
        private static Part Tube(string name, Vector3 position, float d, float len, PaletteMaterialMap.Key material) =>
            new Part(name, PrimitiveType.Cylinder, position, new Vector3(d, len * 0.5f, d), AlongZ, material);

        // TODO(GDD-?) Q4: 크기·모양 전부 자리표시. 소총은 M1/M2 직렬화 큐브(0.06×0.08×0.6, 총구 z 0.3)와 같은 몸통·총구.
        public static readonly IReadOnlyList<Spec> Specs = new[]
        {
            new Spec("WPN-PST-01", M3DataSetup.PistolPath, new Vector3(0f, 0f, 0.11f),
                Box("Slide", new Vector3(0f, 0f, 0.02f), new Vector3(0.035f, 0.045f, 0.18f), Metal),
                Box("Grip", new Vector3(0f, -0.06f, -0.045f), new Vector3(0.03f, 0.09f, 0.04f), Grip, new Vector3(15f, 0f, 0f))),
            new Spec("WPN-SMG-01", M3DataSetup.SmgPath, new Vector3(0f, 0.01f, 0.26f),
                Box("Body", Vector3.zero, new Vector3(0.05f, 0.07f, 0.32f), Metal),
                Box("Magazine", new Vector3(0f, -0.09f, 0.05f), new Vector3(0.03f, 0.12f, 0.04f), Grip),
                Tube("Barrel", new Vector3(0f, 0.01f, 0.21f), 0.025f, 0.1f, Metal)),
            new Spec("WPN-RIF-01", M3DataSetup.RiflePath, new Vector3(0f, 0f, 0.3f),
                Box("Body", Vector3.zero, new Vector3(0.06f, 0.08f, 0.6f), Metal),
                Box("Magazine", new Vector3(0f, -0.1f, 0.08f), new Vector3(0.035f, 0.14f, 0.06f), Grip, new Vector3(-10f, 0f, 0f)),
                Box("Stock", new Vector3(0f, -0.02f, -0.36f), new Vector3(0.05f, 0.09f, 0.18f), Grip)),
            new Spec(M7Names.ShotgunId, M7Names.ShotgunPath, new Vector3(0f, 0.015f, 0.6f),
                Box("Receiver", new Vector3(0f, 0f, -0.05f), new Vector3(0.06f, 0.08f, 0.3f), Metal),
                Tube("Barrel", new Vector3(0f, 0.015f, 0.35f), 0.035f, 0.5f, Metal),
                Tube("Pump", new Vector3(0f, -0.025f, 0.3f), 0.045f, 0.25f, Grip),
                Box("Stock", new Vector3(0f, -0.02f, -0.3f), new Vector3(0.05f, 0.09f, 0.2f), Grip)),
            new Spec(M7Names.DmrId, M7Names.DmrPath, new Vector3(0f, 0.01f, 0.55f),
                Box("Body", new Vector3(0f, 0f, 0.05f), new Vector3(0.05f, 0.07f, 0.7f), Metal),
                Tube("Barrel", new Vector3(0f, 0.01f, 0.475f), 0.025f, 0.15f, Metal),
                Tube("Scope", new Vector3(0f, 0.065f, 0f), 0.04f, 0.22f, Grip),
                Box("Magazine", new Vector3(0f, -0.06f, 0.08f), new Vector3(0.04f, 0.06f, 0.07f), Grip),
                Box("Stock", new Vector3(0f, -0.02f, -0.4f), new Vector3(0.05f, 0.1f, 0.22f), Grip)),
        };

        [MenuItem("Ashfall/Setup/M7/Create Weapon View Models")]
        public static void CreateViewModelsMenu()
        {
            if (!CreateViewModels())
                throw new InvalidOperationException($"{Tag} 실패 — 위 오류 로그 확인");
        }

        // 성공하면 true. 무기 정의가 하나라도 없으면 false (먼저 Ashfall/Setup/Create M7 Weapon Data).
        public static bool CreateViewModels()
        {
            SetupUtil.EnsureFolder(M7Names.WeaponPrefabFolder);
            bool ok = true;
            bool dirty = false;
            foreach (Spec spec in Specs)
            {
                var definition = AssetDatabase.LoadAssetAtPath<WeaponDefinitionSO>(spec.WeaponPath);
                if (definition == null)
                {
                    Debug.LogError($"{Tag} {spec.WeaponPath} 없음 — 먼저 Ashfall/Setup/Create M7 Weapon Data.");
                    ok = false;
                    continue;
                }

                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(spec.PrefabPath);
                if (prefab == null)
                {
                    prefab = Build(spec);
                    if (prefab == null)
                    {
                        ok = false;
                        continue;
                    }
                }

                // 빈 필드만 채운다 (사용자가 다른 프리팹을 연결했으면 그대로)
                if (definition.viewModel == null)
                {
                    definition.viewModel = prefab;
                    EditorUtility.SetDirty(definition);
                    dirty = true;
                    Debug.Log($"{Tag} {definition.id}.viewModel = {spec.PrefabPath}");
                }
            }
            if (dirty)
                AssetDatabase.SaveAssets();
            return ok;
        }

        private static GameObject Build(Spec spec)
        {
            var root = new GameObject(System.IO.Path.GetFileNameWithoutExtension(spec.PrefabPath));
            try
            {
                foreach (Part part in spec.Parts)
                {
                    GameObject go = GameObject.CreatePrimitive(part.Shape);
                    go.name = part.Name;
                    Object.DestroyImmediate(go.GetComponent<Collider>());
                    go.transform.SetParent(root.transform, false);
                    go.transform.localPosition = part.Position;
                    go.transform.localRotation = Quaternion.Euler(part.Euler);
                    go.transform.localScale = part.Scale;
                    var renderer = go.GetComponent<MeshRenderer>();
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    Material material = LoadPaletteMaterial(part.Material);
                    if (material != null)
                        renderer.sharedMaterial = material;
                }
                var muzzle = new GameObject(M7Names.MuzzleChild).transform;
                muzzle.SetParent(root.transform, false);
                muzzle.localPosition = spec.Muzzle;

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, spec.PrefabPath, out bool success);
                if (!success || saved == null)
                {
                    Debug.LogError($"{Tag} 프리팹 저장 실패: {spec.PrefabPath}");
                    return null;
                }
                Debug.Log($"{Tag} 프리팹 생성: {spec.PrefabPath}");
                return saved;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static readonly HashSet<PaletteMaterialMap.Key> warnedMaterials = new HashSet<PaletteMaterialMap.Key>();

        private static Material LoadPaletteMaterial(PaletteMaterialMap.Key key)
        {
            string path = PaletteMaterialMap.Materials[key].Path;
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null && warnedMaterials.Add(key))
                Debug.LogWarning($"{Tag} 팔레트 머티리얼 {path} 없음 — 기본 머티리얼을 쓴다 (먼저 Ashfall/Setup/Apply M6 Art & Audio).");
            return material;
        }
    }
}
