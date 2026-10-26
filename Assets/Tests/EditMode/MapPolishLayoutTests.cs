using System.Collections.Generic;
using System.Linq;
using Ashfall.Core;
using Ashfall.EditorTools;
using Ashfall.Raid;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // 맵 개선 (docs/design/MapPolish_design.md §2~§6): 배치 데이터 검사, 프리팹 사양, 광원 상한, 밝기 개정, 발광 머티리얼 표.
    // 씬 결과(겹침·NavMesh)는 메뉴가 적용 중에 확인하고 PlayMode MapPolishSceneSmokeTests가 다시 본다.
    public class MapPolishLayoutTests
    {
        // 코드 초기값 (ScriptableObject는 new 대신 CreateInstance — 경고 로그 없이)
        private static TValue Default<TAsset, TValue>(System.Func<TAsset, TValue> read) where TAsset : ScriptableObject
        {
            var asset = ScriptableObject.CreateInstance<TAsset>();
            try { return read(asset); }
            finally { Object.DestroyImmediate(asset); }
        }

        [Test]
        public void Layout_PassesDataChecks_WithDefaultLightCap()
        {
            int cap = (int)Default<AtmosphereConfigSO, float>(c => c.maxMapLights);
            Assert.IsTrue(MapPolishLayout.CheckLayout(cap, out string report), report);
        }

        [Test]
        public void Layout_Counts_FollowDesign()
        {
            Assert.AreEqual(8, MapPolishLayout.Landmarks.Length, "§4.1 랜드마크 8");
            Assert.AreEqual(7, MapPolishLayout.Landmarks.Select(l => l.Kind).Distinct().Count(), "프리팹 7종(광고판 ×2)");
            Assert.AreEqual(14, MapPolishLayout.ExpectedLightCount(), "§3 점광원 14 (소품 13 + 추락기 잔불 1)");
            Assert.LessOrEqual(MapPolishLayout.ExpectedLightCount(), Default<AtmosphereConfigSO, float>(c => c.maxMapLights));
            Assert.GreaterOrEqual(MapPolishLayout.StructureCount, 24, "§4.2 군집 12곳 × 2~3");
            // POI마다 광원 소품 2개 이상
            foreach (CrimsonBasinLayout.Poi poi in CrimsonBasinLayout.Pois)
                Assert.GreaterOrEqual(MapPolishLayout.Props.Count(p => p.PoiId == poi.Id), 2, $"{poi.Id} 광원");
        }

        [Test]
        public void Layout_Check_DetectsConflicts()
        {
            // 검사 함수가 실제로 걸러내는지 (리뷰 M3): 빈 자리 1개는 통과, 위반 자리마다 실패
            var clear = new MapPolishLayout.LayoutItem("빈 자리", new Vector2(-60f, -60f), 2f, false);
            Assert.IsTrue(MapPolishLayout.CheckItems(new[] { clear }, out string okReport), okReport);

            var line = MapPolishLayout.SniperSightLines();
            Assert.AreEqual(2, line.Count, "M7 저격수 퍼치 2");
            var bad = new (string why, MapPolishLayout.LayoutItem item)[]
            {
                ("도로 띠", new MapPolishLayout.LayoutItem("도로", new Vector2(-100f, -300f), 1f, true)),
                ("스폰", new MapPolishLayout.LayoutItem("스폰", CrimsonBasinLayout.Spawns[0].PositionXZ + new Vector2(10f, 0f), 1f, false)),
                ("탈출", new MapPolishLayout.LayoutItem("탈출", CrimsonBasinLayout.Extractions[2].CenterXZ, 1f, false)),
                ("POI 안", new MapPolishLayout.LayoutItem("POI", CrimsonBasinLayout.Village.CenterXZ, 1f, false)),
                ("저격 시선", new MapPolishLayout.LayoutItem("시선", (line[0].from + line[0].to) * 0.5f, 1f, true)),
                ("상자", new MapPolishLayout.LayoutItem("상자", CrimsonBasinLayout.LootCrates[0].PositionXZ + new Vector2(1f, 0f), 0.5f, true)),
                ("전원 스위치", new MapPolishLayout.LayoutItem("스위치", CrimsonBasinLayout.PowerSwitchXZ + new Vector2(5f, 0f), 1f, true)),
                ("맵 경계", new MapPolishLayout.LayoutItem("경계", new Vector2(490f, 0f), 2f, false)),
            };
            foreach (var (why, item) in bad)
                Assert.IsFalse(MapPolishLayout.CheckItems(new[] { item }, out _), $"{why} 위 항목을 걸러내지 못했다");
            // 항목끼리 겹침
            Assert.IsFalse(MapPolishLayout.CheckItems(new[] { clear, new MapPolishLayout.LayoutItem("겹침", new Vector2(-61f, -60f), 2f, false) }, out _), "겹침");

            Assert.AreEqual(1f, MapPolishLayout.SegmentDistance(new Vector2(2f, 0f), new Vector2(-1f, 0f), new Vector2(1f, 0f)), 1e-4f);
            Assert.Greater(MapPolishLayout.EnemyAndCratePoints().Count, 34 + 21, "적 34 + 순찰 지점 + 상자 21");
        }

        [Test]
        public void Prefabs_Specs_AreWellFormed()
        {
            var names = new HashSet<string>();
            foreach (MapPolishPrefabs.Spec spec in MapPolishPrefabs.AllSpecs())
            {
                Assert.IsTrue(names.Add(spec.Name), $"이름 중복 {spec.Name}");
                StringAssert.StartsWith("Assets/_Project/Prefabs/", spec.Path);
                StringAssert.EndsWith(spec.Name + ".prefab", spec.Path);
                Assert.Greater(spec.Parts.Length, 0, spec.Name);
                Assert.Greater(spec.FootprintXZ.x, 0f, spec.Name);
                Assert.Greater(spec.FootprintXZ.y, 0f, spec.Name);
                Assert.IsTrue(spec.Parts.Any(p => !p.IsGlow), $"{spec.Name}: 콜라이더 부품 없음");
                foreach (MapPolishPrefabs.Part part in spec.Parts)
                    Assert.IsTrue(PaletteMaterialMap.Materials.ContainsKey(part.Material), $"{spec.Name}/{part.Name}");
            }
            foreach (MapPolishPrefabs.Spec spec in MapPolishPrefabs.Landmarks.Values)
                StringAssert.StartsWith("LM_", spec.Name);
            foreach (MapPolishPrefabs.Spec spec in MapPolishPrefabs.Props.Values)
            {
                StringAssert.StartsWith("LP_", spec.Name);
                Assert.AreEqual(1, spec.Lights.Length, $"{spec.Name}: 광원 소품은 점광원 1");
                Assert.IsTrue(spec.Parts.Any(p => p.IsGlow), $"{spec.Name}: 발광면 없음");
            }
        }

        [Test]
        public void Landmarks_AreTallEnoughToSeeFromAfar()
        {
            // 방향 표지 4종은 25m 이상 (부품 윗면 대략값 = 중심 + 반높이)
            foreach (MapPolishPrefabs.LandmarkKind kind in new[]
                     {
                         MapPolishPrefabs.LandmarkKind.WaterTower, MapPolishPrefabs.LandmarkKind.BrokenPylon,
                         MapPolishPrefabs.LandmarkKind.FlareStack, MapPolishPrefabs.LandmarkKind.RadioMast,
                     })
            {
                float top = MapPolishPrefabs.Landmarks[kind].Parts.Max(p => p.Position.y);
                Assert.Greater(top, 25f, $"{kind} 높이");
            }
        }

        [Test]
        public void LightNames_ParseKind()
        {
            Assert.IsTrue(MapPolishSceneApply.TryKindOf("Light_Fire", out MapPolishPrefabs.LightKind fire));
            Assert.AreEqual(MapPolishPrefabs.LightKind.Fire, fire);
            Assert.IsTrue(MapPolishSceneApply.TryKindOf("Light_Lamp_1", out MapPolishPrefabs.LightKind lamp));
            Assert.AreEqual(MapPolishPrefabs.LightKind.Lamp, lamp);
            Assert.IsFalse(MapPolishSceneApply.TryKindOf("Sun", out _));
            Assert.IsFalse(MapPolishSceneApply.TryKindOf("Light_Laser", out _));
        }

        [Test]
        public void GlowMaterials_InPaletteTable()
        {
            PaletteMaterialMap.Spec ember = PaletteMaterialMap.Materials[PaletteMaterialMap.Key.EmberGlow];
            PaletteMaterialMap.Spec lamp = PaletteMaterialMap.Materials[PaletteMaterialMap.Key.LampGlow];
            Assert.AreEqual(PaletteToken.Ember, ember.Token, "§10.1 Ember = 강조");
            Assert.AreEqual(PaletteToken.Bone, lamp.Token);
            Assert.Greater(ember.Emission, 1f, "Bloom이 받는 발광");
            Assert.Greater(lamp.Emission, 1f);
            Assert.IsTrue(PaletteMaterialMap.TryGetStructureRule("Container_03", out PaletteMaterialMap.Rule container));
            Assert.AreEqual(PaletteMaterialMap.Key.RustMetal, container.Material);
        }

        [Test]
        public void SunLightColor_MixesTowardBone()
        {
            var palette = ScriptableObject.CreateInstance<PaletteSO>();
            var cfg = ScriptableObject.CreateInstance<AtmosphereConfigSO>();
            try
            {
                cfg.sunLightToken = PaletteToken.Ember;
                cfg.sunLightBoneMix = 0f;
                Assert.AreEqual(palette.Get(PaletteToken.Ember), M6VisualSceneApply.SunLightColor(cfg, palette), "섞임 0 = 토큰 그대로(이전과 같음)");
                cfg.sunLightBoneMix = 1f;
                Assert.AreEqual(palette.Get(PaletteToken.Bone), M6VisualSceneApply.SunLightColor(cfg, palette));
                cfg.sunLightBoneMix = 0.35f;
                Color mixed = M6VisualSceneApply.SunLightColor(cfg, palette);
                Assert.Greater(mixed.g, palette.Get(PaletteToken.Ember).g, "Bone 쪽으로 밝아진다");
                Assert.Greater(mixed.r, mixed.g, "여전히 붉은 쪽");
            }
            finally
            {
                Object.DestroyImmediate(cfg);
                Object.DestroyImmediate(palette);
            }
        }

        [Test]
        public void BrightnessRevision_KeepsGddFixedPost_AndStormRule()
        {
            // 개정 1 값은 M6 값(태양 0.9·환경광 1.0·가시거리 450)보다 밝고, SO 필드 초기값과 같다(리뷰 M5 — 새 에셋도 밝다)
            Assert.Greater(MapPolishSetup.SunLightIntensity, 0.9f, "태양을 밝힌다");
            Assert.Greater(MapPolishSetup.AmbientIntensity, 1.0f, "환경광을 밝힌다");
            Assert.Greater(MapPolishSetup.ClearVisibilityM, 450f, "맑을 때 더 멀리");
            Assert.LessOrEqual(MapPolishSetup.SunElevationDeg, 15f, "§10.2 낮게 걸린 태양");
            Assert.AreEqual(MapPolishSetup.SunLightIntensity, Default<AtmosphereConfigSO, float>(c => c.sunLightIntensity), 1e-5f);
            Assert.AreEqual(MapPolishSetup.SunLightBoneMix, Default<AtmosphereConfigSO, float>(c => c.sunLightBoneMix), 1e-5f);
            Assert.AreEqual(MapPolishSetup.SunElevationDeg, Default<AtmosphereConfigSO, float>(c => c.sunElevationDeg), 1e-5f);
            Assert.AreEqual(MapPolishSetup.AmbientIntensity, Default<AtmosphereConfigSO, float>(c => c.ambientIntensity), 1e-5f);
            Assert.AreEqual((float)MapPolishSetup.AmbientGroundToken, Default<AtmosphereConfigSO, float>(c => (float)c.ambientGroundToken), 1e-5f);
            Assert.AreEqual(MapPolishSetup.PostExposureEv, Default<AtmosphereConfigSO, float>(c => c.postExposureEv), 1e-5f);
            Assert.AreEqual(MapPolishSetup.ClearVisibilityM, Default<AtmosphereConfigSO, float>(c => c.clearVisibilityM), 1e-5f);
            Assert.AreEqual(30f, Default<AshStormConfigSO, float>(c => c.stormVisibilityM), "§8.3 폭풍 시야 30m 그대로");

            var cfg = AssetDatabase.LoadAssetAtPath<AtmosphereConfigSO>(M6Names.AtmosphereConfigPath);
            if (cfg == null || cfg.lightingRevision < MapPolishSetup.LightingRevision)
                Assert.Ignore("맵 개선 메뉴(Ashfall/Setup/Apply Map Polish)를 아직 안 돌렸다");
            Assert.AreEqual(-30f, cfg.saturation, 1e-4f, "§10.2 채도 -30");
            Assert.AreEqual(0.3f, cfg.filmGrainIntensity, 1e-4f, "§10.2 Film Grain 0.3");
            Assert.AreEqual(0.35f, cfg.vignetteIntensity, 1e-4f, "§10.2 Vignette 0.35");
            Assert.AreEqual(PaletteToken.DriedBlood, cfg.fogToken, "§10.2 안개 Dried_Blood");
            Assert.LessOrEqual(MapPolishLayout.ExpectedLightCount(), cfg.maxMapLights);
            var storm = AssetDatabase.LoadAssetAtPath<AshStormConfigSO>(M6Names.AshStormConfigPath);
            if (storm != null)
                Assert.AreEqual(30f, storm.stormVisibilityM, 1e-4f, "§8.3 폭풍 시야 30m");
        }
    }
}
