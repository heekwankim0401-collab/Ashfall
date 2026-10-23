using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Ashfall.Audio;
using Ashfall.Core;
using Ashfall.EditorTools;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // M6 AC1 팔레트 일관성 (§10.1·§10.2 "모든 머티리얼은 팔레트 토큰 참조", §14 PaletteSO, M6 설계 §3.4, Q6·Q7).
    // USS 변수 7개 = 팔레트, 매핑 표의 모든 머티리얼 _BaseColor = 토큰(에셋은 Ashfall/Setup/M6/Create Art Assets가 만든다),
    // 블록아웃 매핑 표가 M4 블록아웃 Shape를 전부 덮는다.
    public class PaletteConsistencyTests
    {
        private const string UssPath = "Assets/_Project/Art/UI/Ashfall.uss";
        private const float ColorEps = 1.5f / 255f;

        private static readonly (PaletteToken token, string variable)[] UssVariables =
        {
            (PaletteToken.AshBlack, "--ash-black"),
            (PaletteToken.DriedBlood, "--dried-blood"),
            (PaletteToken.RustRed, "--rust-red"),
            (PaletteToken.Ember, "--ember"),
            (PaletteToken.AshGrey, "--ash-grey"),
            (PaletteToken.Bone, "--bone"),
            (PaletteToken.Toxic, "--toxic"),
        };

        private static PaletteSO LoadPalette()
        {
            var palette = AssetDatabase.LoadAssetAtPath<PaletteSO>(M6Names.PalettePath);
            Assert.IsNotNull(palette, $"팔레트 없음: {M6Names.PalettePath} (Ashfall/Setup/Create M6 Data)");
            return palette;
        }

        // 머티리얼 에셋은 2a 메뉴 산출물 — 메뉴를 한 번도 안 돌린 프로젝트(통합 전)에서는 건너뛴다. 하나라도 있으면 전부 있어야 한다.
        private static void RequireArtAssets()
        {
            bool any = PaletteMaterialMap.Materials.Values.Any(s => AssetDatabase.LoadAssetAtPath<Material>(s.Path) != null);
            if (!any)
                Assert.Ignore("M6 아트 에셋이 아직 없다 — Ashfall/Setup/M6/Create Art Assets 실행 후 확인");
        }

        [Test]
        public void Uss_PaletteVariables_MatchPaletteAsset()
        {
            PaletteSO palette = LoadPalette();
            string uss = File.ReadAllText(Path.GetFullPath(UssPath));
            foreach (var (token, variable) in UssVariables)
            {
                Match match = Regex.Match(uss, Regex.Escape(variable) + @"\s*:\s*#([0-9A-Fa-f]{6})\s*;");
                Assert.IsTrue(match.Success, $"{UssPath}에 {variable} 없음");
                Assert.AreEqual(ColorUtility.ToHtmlStringRGB(palette.Get(token)), match.Groups[1].Value.ToUpperInvariant(),
                    $"{variable} ≠ 팔레트 {PaletteSO.GddName(token)}");
            }
        }

        // M7 Q49: USS의 색 상수는 팔레트 7개 + Ash_Black 반투명뿐이고, 팔레트 밖 색은 문서화된 예외 하나(§6 희귀 청색)만.
        // 색 속성은 전부 var(--…)로 그 변수들만 쓴다(리터럴 색 금지).
        private const string RareBlueVariable = "--rarity-rare-blue";

        [Test]
        public void Uss_ColorLiterals_ArePaletteTokens_ExceptRareBlue()
        {
            PaletteSO palette = LoadPalette();
            var paletteHex = new HashSet<string>(UssVariables.Select(v => ColorUtility.ToHtmlStringRGB(palette.Get(v.token))));
            string uss = RarityStyleTests.StripComments(File.ReadAllText(Path.GetFullPath(UssPath)));

            var colorVariables = new HashSet<string>();
            var offPalette = new List<string>();
            foreach (Match m in Regex.Matches(uss, @"(--[a-z0-9-]+)\s*:\s*([^;]+);"))
            {
                string name = m.Groups[1].Value, value = m.Groups[2].Value.Trim();
                Match hex = Regex.Match(value, @"^#([0-9A-Fa-f]{6})$");
                Match rgba = Regex.Match(value, @"^rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*(,\s*[0-9.]+\s*)?\)$");
                if (!hex.Success && !rgba.Success)
                {
                    Assert.IsFalse(value.StartsWith("#") || value.StartsWith("rgb"), $"{name}: 색 형식은 #RRGGBB 또는 rgba만 ({value})");
                    continue;
                }
                colorVariables.Add(name);
                string rgb = hex.Success
                    ? hex.Groups[1].Value.ToUpperInvariant()
                    : $"{int.Parse(rgba.Groups[1].Value):X2}{int.Parse(rgba.Groups[2].Value):X2}{int.Parse(rgba.Groups[3].Value):X2}";
                if (!paletteHex.Contains(rgb))
                    offPalette.Add(name);
            }
            CollectionAssert.AreEqual(new[] { RareBlueVariable }, offPalette, "팔레트 밖 색은 §6 희귀 청색 하나만 (M7 Q49)");

            // 리터럴 색은 변수 정의에만
            string withoutVariables = Regex.Replace(uss, @"--[a-z0-9-]+\s*:\s*[^;]+;", "");
            Assert.IsFalse(Regex.IsMatch(withoutVariables, @"#[0-9A-Fa-f]{3,8}\b|rgba?\("), "색 리터럴은 :root 변수 정의에만 둔다");
            foreach (Match m in Regex.Matches(withoutVariables, @"var\((--[a-z0-9-]+)\)"))
            {
                // 크기 변수(--ash-cell-size 등)는 색이 아니므로 색 속성에 쓰인 것만 본다
                int start = withoutVariables.LastIndexOf(';', m.Index) + 1;
                int brace = withoutVariables.LastIndexOf('{', m.Index) + 1;
                string property = withoutVariables.Substring(Math.Max(start, brace), m.Index - Math.Max(start, brace));
                if (property.Contains("color"))
                    Assert.IsTrue(colorVariables.Contains(m.Groups[1].Value), $"색 속성에 정의 안 된 색 변수: {m.Groups[1].Value}");
            }
        }

        [Test]
        public void PaletteMaterials_BaseColor_EqualsToken()
        {
            RequireArtAssets();
            PaletteSO palette = LoadPalette();
            foreach (PaletteMaterialMap.Spec spec in PaletteMaterialMap.Materials.Values.Concat(PaletteMaterialMap.LegacyMaterials))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(spec.Path);
                Assert.IsNotNull(m, $"머티리얼 없음: {spec.Path}");
                Color c = m.GetColor("_BaseColor");
                Color expected = palette.Get(spec.Token);
                Assert.AreEqual(expected.r, c.r, ColorEps, $"{spec.Name} r ≠ {PaletteSO.GddName(spec.Token)}");
                Assert.AreEqual(expected.g, c.g, ColorEps, $"{spec.Name} g ≠ {PaletteSO.GddName(spec.Token)}");
                Assert.AreEqual(expected.b, c.b, ColorEps, $"{spec.Name} b ≠ {PaletteSO.GddName(spec.Token)}");
                Assert.AreEqual(spec.Alpha, c.a, ColorEps, $"{spec.Name} 알파");
                if (spec.Transparent)
                {
                    Assert.AreEqual((int)UnityEngine.Rendering.RenderQueue.Transparent, m.renderQueue, $"{spec.Name} 투명 큐");
                    Assert.IsTrue(m.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"), $"{spec.Name} 투명 키워드");
                }
                else
                {
                    Assert.Less(m.renderQueue, (int)UnityEngine.Rendering.RenderQueue.Transparent, $"{spec.Name} 불투명 큐");
                }
            }
        }

        [Test]
        public void PaletteMaterials_UseUrpShaders_InPaletteFolder()
        {
            RequireArtAssets();
            foreach (PaletteMaterialMap.Spec spec in PaletteMaterialMap.Materials.Values)
            {
                StringAssert.StartsWith(M6Names.PaletteMaterialFolder + "/M_", spec.Path);
                var m = AssetDatabase.LoadAssetAtPath<Material>(spec.Path);
                Assert.IsNotNull(m, spec.Path);
                Assert.AreEqual(spec.Unlit ? PaletteMaterialMap.UnlitShader : PaletteMaterialMap.LitShader, m.shader.name, spec.Name);
            }
            var accent = AssetDatabase.LoadAssetAtPath<Material>(PaletteMaterialMap.Materials[PaletteMaterialMap.Key.Accent].Path);
            Assert.IsTrue(accent.IsKeywordEnabled("_EMISSION"), "M_Accent 약한 발광");
        }

        [Test]
        public void BlockoutMap_CoversEveryM4Shape()
        {
            Type shapeEnum = typeof(M4MapSetup).GetNestedType("Shape", BindingFlags.NonPublic);
            Assert.IsNotNull(shapeEnum, "M4MapSetup.Shape enum을 찾지 못했다(이름이 바뀌었으면 이 테스트도 고칠 것)");
            var mapped = new HashSet<string>(PaletteMaterialMap.ShapeNames);
            foreach (string shape in Enum.GetNames(shapeEnum))
            {
                Assert.IsTrue(mapped.Contains(shape), $"매핑 표에 M4 Shape '{shape}' 없음");
                Assert.IsTrue(PaletteMaterialMap.TryGetStructureRule(shape + "_01", out _), shape);
            }
            CollectionAssert.IsSubsetOf(mapped, Enum.GetNames(shapeEnum), "표에 M4에 없는 Shape가 있다");
        }

        [Test]
        public void BlockoutMap_ParsesStructureNames_AndSurfaces()
        {
            Assert.AreEqual("VehiclePile", PaletteMaterialMap.ShapeOf("VehiclePile_02"));
            Assert.AreEqual("Vehicle", PaletteMaterialMap.ShapeOf("Vehicle_13"));
            Assert.IsNull(PaletteMaterialMap.ShapeOf("Vehicle"));
            Assert.IsNull(PaletteMaterialMap.ShapeOf("Vehicle_"));
            Assert.IsNull(PaletteMaterialMap.ShapeOf("Crate_POI-HWY_01x"));
            Assert.IsFalse(PaletteMaterialMap.TryGetStructureRule("Crate_POI-HWY_01", out _), "상자는 LootContainer로 판정");
            Assert.IsFalse(PaletteMaterialMap.TryGetStructureRule("Wall_E", out _));

            Assert.IsTrue(PaletteMaterialMap.TryGetStructureRule("LowWall_03", out PaletteMaterialMap.Rule wall));
            Assert.AreEqual(PaletteMaterialMap.Key.Concrete, wall.Material);
            Assert.AreEqual(SurfaceKind.Debris, wall.Surface);
            Assert.IsTrue(PaletteMaterialMap.TryGetStructureRule("Tank_02", out PaletteMaterialMap.Rule tank));
            Assert.AreEqual(PaletteMaterialMap.Key.RustMetal, tank.Material);
            Assert.AreEqual(SurfaceKind.Metal, tank.Surface);
            Assert.IsTrue(PaletteMaterialMap.TryGetStructureRule("Rubble_01", out PaletteMaterialMap.Rule rubble));
            Assert.AreEqual(PaletteMaterialMap.Key.Debris, rubble.Material);
            Assert.AreEqual(SurfaceKind.Water, PaletteMaterialMap.PuddleRule.Surface);
            Assert.AreEqual(SurfaceKind.Metal, PaletteMaterialMap.CrateRule.Surface);
            Assert.AreEqual(PaletteMaterialMap.Key.Accent, PaletteMaterialMap.PowerSwitchRule.Material);
        }

        [Test]
        public void MaterialTable_TokensFollowDesign()
        {
            // 설계 §3.4 표 (토큰만 — 속성은 자리표시)
            Assert.AreEqual(PaletteToken.AshGrey, PaletteMaterialMap.Materials[PaletteMaterialMap.Key.Concrete].Token);
            Assert.AreEqual(PaletteToken.RustRed, PaletteMaterialMap.Materials[PaletteMaterialMap.Key.RustMetal].Token);
            Assert.AreEqual(PaletteToken.AshGrey, PaletteMaterialMap.Materials[PaletteMaterialMap.Key.Debris].Token);
            Assert.AreEqual(PaletteToken.RustRed, PaletteMaterialMap.Materials[PaletteMaterialMap.Key.Crate].Token);
            Assert.AreEqual(PaletteToken.Ember, PaletteMaterialMap.Materials[PaletteMaterialMap.Key.Accent].Token);
            Assert.AreEqual(PaletteToken.AshBlack, PaletteMaterialMap.Materials[PaletteMaterialMap.Key.Puddle].Token);
            Assert.AreEqual(Enum.GetValues(typeof(PaletteMaterialMap.Key)).Length, PaletteMaterialMap.Materials.Count, "키마다 머티리얼 하나");
            Assert.AreEqual(4, PaletteMaterialMap.LegacyMaterials.Count, "기존 M2~M4 머티리얼 4개");
        }
    }
}
