using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ashfall.Core;
using Ashfall.EditorTools;
using Ashfall.Raid;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Ashfall.Tests.EditMode
{
    // M6 AC1 비주얼 파생 에셋 (§10.2, M6 설계 §1.3·§2.2·§3.2, Q2~Q5·Q30, 위험 R2):
    // VP_RaidAtmosphere 값 = AtmosphereConfig, 하늘 머티리얼 = Skybox/Panoramic + 생성 파노라마, 재 머티리얼 = URP Particles/Unlit 투명,
    // TL_AshSoil = 노이즈 × Dried_Blood, 메뉴 재실행 시 GUID·바이트 불변.
    // 에셋은 Ashfall/Setup/M6/Create Art Assets(M6ArtSetup)가 만든다 — 통합 전(한 번도 안 돌림)이면 건너뛴다.
    public class AtmosphereAssetTests
    {
        private const float Eps = 1e-4f;
        private const float ColorEps = 1.5f / 255f;

        private static readonly string[] DerivedPaths =
        {
            M6Names.SkyTexturePath, M6Names.AshDotTexturePath, M6Names.GroundNoiseTexturePath,
            M6Names.SkyMaterialPath, M6Names.AshParticleMaterialPath, M6Names.TerrainLayerPath, M6Names.RaidVolumeProfilePath,
        };

        private static IEnumerable<string> AllGeneratedPaths =>
            DerivedPaths.Concat(PaletteMaterialMap.Materials.Values.Select(s => s.Path))
                .Concat(PaletteMaterialMap.LegacyMaterials.Select(s => s.Path).Where(p => File.Exists(Path.GetFullPath(p))));

        private AtmosphereConfigSO cfg;
        private PaletteSO palette;

        [SetUp]
        public void SetUp()
        {
            if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(M6Names.RaidVolumeProfilePath) == null)
                Assert.Ignore("M6 아트 에셋이 아직 없다 — Ashfall/Setup/M6/Create Art Assets 실행 후 확인");
            cfg = AssetDatabase.LoadAssetAtPath<AtmosphereConfigSO>(M6Names.AtmosphereConfigPath);
            Assert.IsNotNull(cfg, M6Names.AtmosphereConfigPath);
            palette = cfg.palette;
            Assert.IsNotNull(palette, "AtmosphereConfig.palette");
        }

        private static T Load<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.IsNotNull(asset, $"에셋 없음: {path}");
            return asset;
        }

        private static T Component<T>(VolumeProfile profile) where T : VolumeComponent
        {
            Assert.IsTrue(profile.TryGet(out T component), $"{typeof(T).Name} 없음");
            Assert.IsTrue(component.active, $"{typeof(T).Name} 꺼짐");
            return component;
        }

        // ---------- 볼륨 프로필 (§10.2) ----------

        [Test]
        public void VolumeProfile_MatchesGddPost()
        {
            var profile = Load<VolumeProfile>(M6Names.RaidVolumeProfilePath);

            var ca = Component<ColorAdjustments>(profile);
            Assert.IsTrue(ca.saturation.overrideState);
            Assert.AreEqual(-30f, ca.saturation.value, Eps, "§10.2 채도 -30");
            Assert.IsTrue(ca.colorFilter.overrideState, "붉은 틴트");
            Color tint = ca.colorFilter.value;
            Assert.Greater(tint.r, tint.g, "틴트 적색 우세");
            Assert.Greater(tint.r, tint.b, "틴트 적색 우세");
            Assert.Less(tint.g, 1f, "틴트가 흰색이면 효과 없음");
            Assert.AreEqual(cfg.contrast, ca.contrast.value, Eps);

            var grain = Component<FilmGrain>(profile);
            Assert.IsTrue(grain.intensity.overrideState);
            Assert.AreEqual(0.3f, grain.intensity.value, Eps, "§10.2 Film Grain 0.3");
            Assert.AreEqual(M6ArtSetup.ToUrp(cfg.filmGrainType), grain.type.value);

            var vignette = Component<Vignette>(profile);
            Assert.IsTrue(vignette.intensity.overrideState);
            Assert.AreEqual(0.35f, vignette.intensity.value, Eps, "§10.2 Vignette 0.35");
            Assert.AreEqual(cfg.vignetteSmoothness, vignette.smoothness.value, Eps);

            var bloom = Component<Bloom>(profile);
            Assert.IsTrue(bloom.intensity.overrideState, "Bloom을 명시해 기본 프로필을 덮는다");
            Assert.LessOrEqual(bloom.intensity.value, 0.3f, "§10.2 Bloom 낮음");
            Assert.AreEqual(cfg.bloomIntensity, bloom.intensity.value, Eps);
            Assert.AreEqual(cfg.bloomThreshold, bloom.threshold.value, Eps);

            var tonemapping = Component<Tonemapping>(profile);
            Assert.IsTrue(tonemapping.mode.overrideState);
            Assert.AreEqual(TonemappingMode.Neutral, tonemapping.mode.value);
        }

        [Test]
        public void VolumeProfile_ComponentsAreSubAssets_NoDuplicates()
        {
            var profile = Load<VolumeProfile>(M6Names.RaidVolumeProfilePath);
            Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(M6Names.RaidVolumeProfilePath);
            foreach (VolumeComponent component in profile.components)
                CollectionAssert.Contains(subAssets, component, $"{component.GetType().Name}가 프로필 에셋 안에 저장되지 않았다");
            var types = profile.components.Select(c => c.GetType()).ToList();
            CollectionAssert.AllItemsAreUnique(types, "같은 종류 구성 요소가 두 번 들어 있다");
        }

        [Test]
        public void ConverterEnums_MapToUrp()
        {
            Assert.AreEqual(TonemappingMode.Neutral, M6ArtSetup.ToUrp(TonemapChoice.Neutral));
            Assert.AreEqual(TonemappingMode.ACES, M6ArtSetup.ToUrp(TonemapChoice.Aces));
            Assert.AreEqual(TonemappingMode.None, M6ArtSetup.ToUrp(TonemapChoice.None));
            foreach (FilmGrainChoice choice in System.Enum.GetValues(typeof(FilmGrainChoice)))
                Assert.AreEqual(choice.ToString(), M6ArtSetup.ToUrp(choice).ToString(), "이름이 같은 URP 값");
        }

        // ---------- 하늘 ----------

        [Test]
        public void SkyMaterial_IsPanoramic_WithGeneratedTexture()
        {
            var sky = Load<Material>(M6Names.SkyMaterialPath);
            Assert.AreEqual(M6ArtSetup.SkyShaderName, sky.shader.name);
            Texture expected = cfg.skyTextureOverride != null ? cfg.skyTextureOverride : Load<Texture2D>(M6Names.SkyTexturePath);
            Assert.AreSame(expected, sky.GetTexture("_MainTex"), "하늘 텍스처");
            Assert.AreEqual(1f, sky.GetFloat("_Mapping"), Eps, "Latitude Longitude");
            Assert.AreEqual(0f, sky.GetFloat("_Rotation"), Eps, "회전 0 (태양 방향 = Sun 라이트)");
        }

        [Test]
        public void SkyTexture_SizeImportAndColors()
        {
            if (cfg.skyTextureOverride != null)
                Assert.Ignore("사용자 하늘 텍스처 사용 중");
            var importer = (TextureImporter)AssetImporter.GetAtPath(M6Names.SkyTexturePath);
            Assert.IsNotNull(importer);
            Assert.IsFalse(importer.mipmapEnabled, "하늘 밉맵 없음");
            Assert.IsTrue(importer.sRGBTexture);
            Assert.AreEqual(TextureWrapMode.Clamp, importer.wrapModeV);

            // PNG를 직접 읽어 원본 픽셀을 본다 (임포트 압축과 무관)
            var png = new Texture2D(2, 2);
            try
            {
                Assert.IsTrue(png.LoadImage(File.ReadAllBytes(Path.GetFullPath(M6Names.SkyTexturePath))));
                Assert.AreEqual(cfg.skyTextureSize.x, png.width);
                Assert.AreEqual(cfg.skyTextureSize.y, png.height);
                Color top = png.GetPixel(png.width / 2, png.height - 1);
                Color bottom = png.GetPixel(png.width / 2, 0);
                Assert.AreEqual(palette.ashBlack.r, top.r, 3f / 255f, "맨 위 = 천정 Ash_Black");
                Assert.AreEqual(palette.ashBlack.g, top.g, 3f / 255f);
                Assert.AreEqual(palette.driedBlood.r, bottom.r, ColorEps, "맨 아래 = Dried_Blood");
                Assert.AreEqual(palette.driedBlood.g, bottom.g, ColorEps);
                // 태양 방향 픽셀 = 텍스처에서 가장 밝다
                Vector2 uv = SkyPanorama.DirectionToUv(SkyPanorama.SunDirection(cfg));
                Color sun = png.GetPixelBilinear(uv.x, uv.y);
                Color away = png.GetPixelBilinear(Mathf.Repeat(uv.x + 0.5f, 1f), uv.y);
                Assert.Greater(sun.r + sun.g + sun.b, away.r + away.g + away.b + 0.1f, "태양 위치가 밝지 않다");
            }
            finally
            {
                Object.DestroyImmediate(png);
            }
        }

        // ---------- 재 파티클 머티리얼 (위험 R2) ----------

        [Test]
        public void AshMaterial_IsUrpParticlesUnlit_Transparent()
        {
            var ash = Load<Material>(M6Names.AshParticleMaterialPath);
            Assert.AreEqual(M6ArtSetup.AshShaderName, ash.shader.name);
            Assert.AreEqual((int)RenderQueue.Transparent, ash.renderQueue, "투명 큐");
            Assert.IsTrue(ash.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"), "투명 키워드");
            Assert.AreEqual(1f, ash.GetFloat("_Surface"), Eps);
            Assert.AreEqual(0f, ash.GetFloat("_ZWrite"), Eps, "ZWrite 끔");
            Assert.AreEqual((float)BlendMode.SrcAlpha, ash.GetFloat("_SrcBlend"), Eps);
            Assert.AreEqual((float)BlendMode.OneMinusSrcAlpha, ash.GetFloat("_DstBlend"), Eps);
            Assert.AreEqual("Transparent", ash.GetTag("RenderType", false));
            Assert.IsNotNull(ash.GetTexture("_BaseMap"), "재 텍스처");
            Color c = ash.GetColor("_BaseColor");
            Color token = palette.Get(cfg.ashToken);
            Assert.AreEqual(token.r, c.r, ColorEps, "재 색 = ashToken");
            Assert.AreEqual(cfg.ashAlpha, c.a, ColorEps, "재 알파");
        }

        [Test]
        public void AshDot_HasAlphaFalloff()
        {
            Color32[] dot = M6ArtSetup.RenderAshDot(M6ArtSetup.AshDotSize);
            int n = M6ArtSetup.AshDotSize;
            Assert.Greater(dot[(n / 2) * n + n / 2].a, 240, "가운데 불투명");
            Assert.AreEqual(0, dot[0].a, "모서리 투명");
        }

        // ---------- 지형 ----------

        [Test]
        public void TerrainLayer_IsNoiseTimesDriedBlood()
        {
            var layer = Load<TerrainLayer>(M6Names.TerrainLayerPath);
            Assert.AreSame(Load<Texture2D>(M6Names.GroundNoiseTexturePath), layer.diffuseTexture);
            Color linear = palette.driedBlood.linear;
            Assert.AreEqual(linear.r, layer.diffuseRemapMax.x, Eps, "remapMax = Dried_Blood(선형)");
            Assert.AreEqual(linear.g, layer.diffuseRemapMax.y, Eps);
            Assert.AreEqual(linear.b, layer.diffuseRemapMax.z, Eps);
            Assert.AreEqual(cfg.groundTileM, layer.tileSize.x, Eps, "타일 크기 = AtmosphereConfig.groundTileM");
            Assert.AreEqual(cfg.groundTileM, layer.tileSize.y, Eps);
        }

        [Test]
        public void GroundNoise_IsSeamless_AndDeterministic()
        {
            int n = M6ArtSetup.GroundNoiseSize;
            Color32[] a = M6ArtSetup.RenderGroundNoise(n, M6ArtSetup.GroundNoiseSeed, cfg.groundNoiseMin);
            CollectionAssert.AreEqual(a, M6ArtSetup.RenderGroundNoise(n, M6ArtSetup.GroundNoiseSeed, cfg.groundNoiseMin));
            int maxEdgeJump = 0, maxInnerJump = 0;
            for (int y = 0; y < n; y++)
            {
                maxEdgeJump = Mathf.Max(maxEdgeJump, Mathf.Abs(a[y * n + n - 1].r - a[y * n].r));
                maxInnerJump = Mathf.Max(maxInnerJump, Mathf.Abs(a[y * n + n / 2].r - a[y * n + n / 2 - 1].r));
            }
            for (int x = 0; x < n; x++)
                maxEdgeJump = Mathf.Max(maxEdgeJump, Mathf.Abs(a[(n - 1) * n + x].r - a[x].r));
            Assert.LessOrEqual(maxEdgeJump, maxInnerJump + 2, "타일 이음매가 내부보다 크게 끊긴다");
            byte min = a.Min(p => p.r);
            Assert.GreaterOrEqual(min, (byte)Mathf.FloorToInt(cfg.groundNoiseMin * 255f) - 1, "회색 하한");
        }

        // ---------- 재실행 멱등 (Q30) ----------
        // 주의(M6 리뷰 낮음 5): 이 테스트는 실제 커밋된 파생 에셋(PNG·머티리얼·프로필·지형 레이어)에 M6ArtSetup.CreateAssets()를 다시 돌린다.
        // 메뉴가 멱등이라 정상이면 아무 파일도 바뀌지 않지만, SO(AtmosphereConfig·Palette)를 고친 뒤 메뉴를 돌리지 않은 상태에서
        // 테스트를 돌리면 테스트가 파생물을 새 값으로 다시 쓴다(테스트는 실패하고 작업 트리에 변경이 남는다 — 의도한 결과라면 커밋).
        // 테스트 도중 에디터가 죽어도 쓰는 내용은 메뉴 결과와 같다. 실행 전후 git status로 확인할 것.

        [Test]
        public void CreateAssets_Rerun_KeepsGuidsAndBytes()
        {
            List<string> paths = AllGeneratedPaths.ToList();
            foreach (string path in paths)
                Assert.IsTrue(File.Exists(Path.GetFullPath(path)), $"에셋 없음: {path}");
            Dictionary<string, byte[]> before = paths.ToDictionary(p => p, p => File.ReadAllBytes(Path.GetFullPath(p)));
            Dictionary<string, string> guids = paths.ToDictionary(p => p, p => AssetDatabase.AssetPathToGUID(p));

            Assert.IsTrue(M6ArtSetup.CreateAssets(), "메뉴 실패");
            AssetDatabase.SaveAssets();

            foreach (string path in paths)
            {
                Assert.AreEqual(guids[path], AssetDatabase.AssetPathToGUID(path), $"GUID 바뀜: {path}");
                CollectionAssert.AreEqual(before[path], File.ReadAllBytes(Path.GetFullPath(path)), $"재실행으로 바뀜: {path}");
            }
        }
    }
}
