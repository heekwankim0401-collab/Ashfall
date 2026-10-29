using System.Collections.Generic;
using System.IO;
using Ashfall.Raid;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using L = Ashfall.EditorTools.CrimsonBasinLayout;

namespace Ashfall.EditorTools
{
    // 맵 밀도 미리보기 렌더 (docs/design/MapDensity_design.md §8.5 "에디터 1080p 렌더 비교"). 확인용 도구 — 씬을 저장하지 않는다.
    // 레이드 씬을 열고 플레이어 눈높이(지면 + 1.6m) 시점마다 플레이어 카메라 설정(FOV·클립·포스트)으로 1920×1080 PNG를 쓴다.
    // 배치모드(그래픽 장치 필요 — -nographics 금지):
    //   -batchmode -quit -projectPath . -executeMethod Ashfall.EditorTools.MapDensityPreview.RenderViews -ashfallPreviewOut <폴더> -ashfallPreviewTag before
    public static class MapDensityPreview
    {
        private const string Tag = "[MapDensityPreview]";
        private const int Width = 1920, Height = 1080;
        private const float EyeHeightM = 1.6f;

        // 시점 (이름, XZ, 바라보는 yaw). 스폰 3곳은 맵 중앙, 나머지는 밀집 예정 자리(마을·작은 마을·죽은 숲 가장자리·중앙).
        public static List<(string name, Vector2 xz, float yaw)> Views()
        {
            var views = new List<(string, Vector2, float)>();
            foreach (L.Spawn spawn in L.Spawns)
                views.Add(($"spawn_{spawn.Id}", spawn.PositionXZ, spawn.YawDeg));
            views.Add(("village_POI-VIL", new Vector2(-225f, 60f), 270f));
            var town = MapDensityLayout.Towns[0];
            views.Add(($"town_{town.Id}", town.Center + new Vector2(0f, -45f), 0f));
            views.Add(("center", new Vector2(10f, -20f), 300f));
            (Vector2 grove, float groveYaw) = GroveEdge();
            views.Add(("grove_edge", grove, groveYaw));
            return views;
        }

        // 길목에서 군락 노이즈가 임계값 바로 아래인 자리 → 노이즈가 커지는 쪽을 본다 (설정 기본값 기준, 결정적)
        private static (Vector2, float) GroveEdge()
        {
            var cfg = AssetDatabase.LoadAssetAtPath<MapDensityConfigSO>(MapDensitySceneApply.ConfigPath);
            float scale = cfg != null ? cfg.treeGroveNoiseScaleM : 90f, threshold = cfg != null ? cfg.treeGroveThreshold : 0.45f;
            Vector2 offset = MapDensityLayout.NoiseOffset(MapDensityLayout.TreeSeed);
            Vector2 best = new Vector2(-100f, 120f);
            float bestScore = float.MaxValue;
            for (float x = -300f; x <= 300f; x += 10f)
            for (float z = -200f; z <= 300f; z += 10f)
            {
                var p = new Vector2(x, z);
                if (LootZoneRules.ZoneOf(p) != LootZoneRules.Route)
                    continue;
                float n = MapDensityLayout.GroveNoise(p, scale, offset);
                float ahead = MapDensityLayout.GroveNoise(p + Gradient(p, scale, offset) * 40f, scale, offset);
                float score = Mathf.Abs(n - (threshold - 0.05f)) - Mathf.Max(0f, ahead - threshold);   // 가장자리 + 앞이 짙은 숲
                if (score < bestScore)
                {
                    bestScore = score;
                    best = p;
                }
            }
            Vector2 g = Gradient(best, scale, offset);
            return (best, Mathf.Atan2(g.x, g.y) * Mathf.Rad2Deg);
        }

        private static Vector2 Gradient(Vector2 p, float scale, Vector2 offset)
        {
            float dx = MapDensityLayout.GroveNoise(p + Vector2.right * 5f, scale, offset) - MapDensityLayout.GroveNoise(p - Vector2.right * 5f, scale, offset);
            float dz = MapDensityLayout.GroveNoise(p + Vector2.up * 5f, scale, offset) - MapDensityLayout.GroveNoise(p - Vector2.up * 5f, scale, offset);
            var g = new Vector2(dx, dz);
            return g.sqrMagnitude < 1e-8f ? Vector2.up : g.normalized;
        }

        public static void RenderViews()
        {
            string outDir = WeaponSightProbe.ArgValue("-ashfallPreviewOut");
            string tag = WeaponSightProbe.ArgValue("-ashfallPreviewTag") ?? "view";
            if (string.IsNullOrEmpty(outDir))
                throw new System.ArgumentException("-ashfallPreviewOut <폴더> 필요");
            Directory.CreateDirectory(outDir);
            Scene scene = EditorSceneManager.OpenScene(M4SceneSetup.RaidScenePath, OpenSceneMode.Single);
            SceneManager.SetActiveScene(scene);
            Terrain terrain = Object.FindFirstObjectByType<Terrain>();
            Camera player = null;
            foreach (Camera c in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
                if (c.CompareTag("MainCamera"))
                    player = c;

            // 플레이어(첫 스폰에 서 있다)의 몸·무기는 숨긴다 — 저장하지 않으므로 씬 파일은 그대로
            if (player != null)
                foreach (Renderer r in player.transform.root.GetComponentsInChildren<Renderer>(true))
                    r.enabled = false;
            var go = new GameObject("PreviewCamera");
            var cam = go.AddComponent<Camera>();
            if (player != null)
                cam.CopyFrom(player);
            cam.enabled = false;
            var data = SetupUtil.GetOrAdd<UniversalAdditionalCameraData>(go);
            data.renderPostProcessing = true;
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            cam.targetTexture = rt;
            foreach (var view in Views())
            {
                float y = M4MapSetup.GroundY(terrain, view.xz.x, view.xz.y) + EyeHeightM;
                go.transform.SetPositionAndRotation(new Vector3(view.xz.x, y, view.xz.y), Quaternion.Euler(4f, view.yaw, 0f));
                // 크로스페이드·스킨 숨김 등이 첫 렌더에 맞춰지도록 두 번 그린다
                cam.Render();
                cam.Render();
                RenderTexture prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                string path = Path.Combine(outDir, $"{tag}_{view.name}.png");
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                Debug.Log($"{Tag} {path} ({view.xz.x:0}, {view.xz.y:0}) yaw {view.yaw:0}");
            }
            cam.targetTexture = null;
            rt.Release();
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(go);
            Debug.Log($"{Tag} 완료: {outDir} (씬 저장 안 함)");
        }
    }
}
