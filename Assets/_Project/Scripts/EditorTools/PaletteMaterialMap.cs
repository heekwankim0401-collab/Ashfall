using System.Collections.Generic;
using Ashfall.Audio;
using Ashfall.Core;

namespace Ashfall.EditorTools
{
    // 블록아웃 머티리얼·지면 재질 표 (M6 설계 §3.4, ART-*, Q6·Q7·Q11). 레벨 데이터 — 전부 TODO(GDD-?) 자리표시.
    // 머티리얼 이름·토큰·속성과 "구조물 이름 → 머티리얼·SurfaceKind"를 이 표 한 곳에서만 정한다.
    // M6ArtSetup이 머티리얼 에셋을 만들고(색 = PaletteSO 토큰), M6VisualSceneApply가 레이드 씬에 칠한다. 테스트가 같은 표로 확인한다.
    public static class PaletteMaterialMap
    {
        // 팔레트 머티리얼 종류 (Art/Materials/Palette/M_*.mat)
        public enum Key
        {
            Concrete,
            RustMetal,
            Debris,
            Crate,
            Accent,
            Puddle,
            Mutant,   // M7 7B-2c: AI-MUT 몸
            Boss,     // M7 7B-2c: AI-BOS 몸
            Toxic,    // M7 7C-2c: 정유소 방사능 구역 표시(드럼통·경고 기둥)
            EmberGlow, // 맵 개선 §3: 불타는 드럼통·잔불·신호 비컨 (강한 발광 — Bloom이 받는다)
            LampGlow,  // 맵 개선 §3: 깨진 가로등 등
        }

        // 머티리얼 한 개의 사양. 색은 토큰(+ 알파), 셰이더 속성은 URP Lit/Unlit 이름 그대로.
        public sealed class Spec
        {
            public readonly string Name;          // 에셋 이름 (확장자 없음)
            public readonly string Path;          // 에셋 경로
            public readonly PaletteToken Token;   // _BaseColor RGB
            public readonly float Alpha;          // _BaseColor A (1 = 불투명)
            public readonly bool Transparent;     // URP Surface Type = Transparent(Alpha 블렌드)
            public readonly bool Unlit;           // URP Unlit (false = URP Lit)
            public readonly float Metallic;
            public readonly float Smoothness;
            public readonly float Emission;       // _EmissionColor = 토큰 × 이 값 (0 = 발광 없음)

            public Spec(string name, string path, PaletteToken token, float alpha = 1f, bool transparent = false, bool unlit = false,
                float metallic = 0f, float smoothness = 0.5f, float emission = 0f)
            {
                Name = name;
                Path = path;
                Token = token;
                Alpha = alpha;
                Transparent = transparent;
                Unlit = unlit;
                Metallic = metallic;
                Smoothness = smoothness;
                Emission = emission;
            }
        }

        // 구조물 → 머티리얼·지면 재질
        public readonly struct Rule
        {
            public readonly Key Material;
            public readonly SurfaceKind Surface;

            public Rule(Key material, SurfaceKind surface)
            {
                Material = material;
                Surface = surface;
            }
        }

        public const string LitShader = "Universal Render Pipeline/Lit";
        public const string UnlitShader = "Universal Render Pipeline/Unlit";

        private static string PalettePath(string name) => M6Names.PaletteMaterialFolder + "/" + name + ".mat";

        // §3.4 팔레트 머티리얼 (TODO(GDD-?) 속성값 전부 임시)
        public static readonly IReadOnlyDictionary<Key, Spec> Materials = new Dictionary<Key, Spec>
        {
            { Key.Concrete, new Spec("M_Concrete", PalettePath("M_Concrete"), PaletteToken.AshGrey, smoothness: 0.1f) },
            { Key.RustMetal, new Spec("M_RustMetal", PalettePath("M_RustMetal"), PaletteToken.RustRed, metallic: 0.3f, smoothness: 0.25f) },
            { Key.Debris, new Spec("M_Debris", PalettePath("M_Debris"), PaletteToken.AshGrey, smoothness: 0.05f) },
            { Key.Crate, new Spec("M_Crate", PalettePath("M_Crate"), PaletteToken.RustRed, smoothness: 0.2f) },
            { Key.Accent, new Spec("M_Accent", PalettePath("M_Accent"), PaletteToken.Ember, smoothness: 0.3f, emission: 0.6f) },
            { Key.Puddle, new Spec("M_Puddle", PalettePath("M_Puddle"), PaletteToken.AshBlack, smoothness: 0.95f) },
            // M7 설계 §2.2 (7B-2c): 변이체 = Ash_Grey 거친 면, 보스 = Ember 강조(약한 발광). TODO(GDD-?) 자리표시
            { Key.Mutant, new Spec("M_Mutant", PalettePath("M_Mutant"), PaletteToken.AshGrey, smoothness: 0.02f) },
            { Key.Boss, new Spec("M_Boss", PalettePath("M_Boss"), PaletteToken.Ember, smoothness: 0.35f, emission: 0.3f) },
            // M7 설계 §2.2 (7C-2c, Q40): Toxic(#7A8A2A) 단색 + 약한 발광 — 정유소 드럼통·경고 기둥. 경로 = M7Names.ToxicMaterialPath. TODO(GDD-?) 자리표시
            { Key.Toxic, new Spec("M_Toxic", M7Names.ToxicMaterialPath, PaletteToken.Toxic, smoothness: 0.3f, emission: 0.25f) },
            // 맵 개선 설계 §3: 광원 소품·랜드마크 신호등 발광면. TODO(GDD-?) 발광 배율 자리표시
            { Key.EmberGlow, new Spec("M_EmberGlow", PalettePath("M_EmberGlow"), PaletteToken.Ember, smoothness: 0.2f, emission: 1.6f) },
            { Key.LampGlow, new Spec("M_LampGlow", PalettePath("M_LampGlow"), PaletteToken.Bone, smoothness: 0.2f, emission: 1.5f) },
        };

        // M7 저격 섬광 (설계 §1.4·§2.2, Q23): URP Unlit, _BaseColor = Bone × Emission(HDR — Bloom이 받는다), 그림자 없음.
        // 경로는 계약 M7Names.SniperGlintMaterialPath(Art/Materials — 팔레트 폴더 밖)라 Materials 표에 넣지 않는다.
        // Unlit에는 _EmissionColor가 없어 M7EnemySceneApply가 HDR 배율을 _BaseColor에 곱한다. TODO(GDD-?) Q23 배율 4
        public static readonly Spec SniperGlint =
            new Spec("M_SniperGlint", M7Names.SniperGlintMaterialPath, PaletteToken.Bone, unlit: true, smoothness: 0f, emission: 4f);

        // M7 적 유형 → 몸 머티리얼 (7B-2c). 저격수·호위는 기존 M3_EnemyRDR(Bone, 레이더 계열), 변이체·보스는 새 키.
        public static Spec EnemyBodySpec(M7EnemyLayout.Kind kind)
        {
            switch (kind)
            {
                case M7EnemyLayout.Kind.Mutant: return Materials[Key.Mutant];
                case M7EnemyLayout.Kind.Boss: return Materials[Key.Boss];
                default: return LegacySpec(M3SceneSetup.RdrMaterialPath);
            }
        }

        private static Spec LegacySpec(string path)
        {
            foreach (Spec spec in LegacyMaterials)
                if (spec.Path == path)
                    return spec;
            return null;
        }

        // §3.4 기존 M2~M4 머티리얼 재도색 (Q7, GUID 유지 — 셰이더는 바꾸지 않고 색·투명만)
        public static readonly IReadOnlyList<Spec> LegacyMaterials = new[]
        {
            new Spec("M4_ExtractionZone", M4MapSetup.ZoneMaterialPath, PaletteToken.Bone, alpha: 0.25f, transparent: true, unlit: true),
            new Spec("M3_EnemySCV", M3SceneSetup.ScvMaterialPath, PaletteToken.AshGrey),
            new Spec("M3_EnemyRDR", M3SceneSetup.RdrMaterialPath, PaletteToken.Bone),
            new Spec("M2_TurretZone", M2SceneSetup.ZoneMaterialPath, PaletteToken.Ember, alpha: 0.3f, transparent: true),
        };

        // M4 블록아웃 구조물 이름 "{Shape}_{nn}"의 Shape → 규칙 (M4MapSetup.Shape 전부, 테스트가 확인)
        private static readonly Dictionary<string, Rule> ShapeRules = new Dictionary<string, Rule>
        {
            { "LowWall", new Rule(Key.Concrete, SurfaceKind.Debris) },
            { "HighWall", new Rule(Key.Concrete, SurfaceKind.Debris) },
            { "House", new Rule(Key.Concrete, SurfaceKind.Debris) },
            { "Deck", new Rule(Key.Concrete, SurfaceKind.Debris) },
            { "Ramp", new Rule(Key.Concrete, SurfaceKind.Debris) },
            { "Rubble", new Rule(Key.Debris, SurfaceKind.Debris) },
            { "Vehicle", new Rule(Key.RustMetal, SurfaceKind.Metal) },
            { "VehiclePile", new Rule(Key.RustMetal, SurfaceKind.Metal) },
            { "Tank", new Rule(Key.RustMetal, SurfaceKind.Metal) },
            { "Post", new Rule(Key.RustMetal, SurfaceKind.Metal) },
            { "Flat", new Rule(Key.RustMetal, SurfaceKind.Metal) },
            { "Container", new Rule(Key.RustMetal, SurfaceKind.Metal) },   // 맵 개선 §4.2
        };

        public static IEnumerable<string> ShapeNames => ShapeRules.Keys;

        // 루트 상자(LootContainer가 있는 큐브), EXT-02 전원 스위치(PowerSwitch_*), 물웅덩이
        public static readonly Rule CrateRule = new Rule(Key.Crate, SurfaceKind.Metal);
        public static readonly Rule PowerSwitchRule = new Rule(Key.Accent, SurfaceKind.Metal);
        public static readonly Rule PuddleRule = new Rule(Key.Puddle, SurfaceKind.Water);

        // "{Shape}_{숫자}" 형식의 구조물 이름이면 그 Shape의 규칙. 숫자 꼬리가 없거나 표에 없는 Shape면 false.
        public static bool TryGetStructureRule(string structureName, out Rule rule)
        {
            rule = default;
            string shape = ShapeOf(structureName);
            return shape != null && ShapeRules.TryGetValue(shape, out rule);
        }

        // "VehiclePile_02" → "VehiclePile". 마지막 '_' 뒤가 숫자 1자 이상이 아니면 null.
        public static string ShapeOf(string structureName)
        {
            if (string.IsNullOrEmpty(structureName))
                return null;
            int underscore = structureName.LastIndexOf('_');
            if (underscore <= 0 || underscore == structureName.Length - 1)
                return null;
            for (int i = underscore + 1; i < structureName.Length; i++)
                if (!char.IsDigit(structureName[i]))
                    return null;
            return structureName.Substring(0, underscore);
        }
    }
}
