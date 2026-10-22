using Ashfall.Core;
using Ashfall.Raid;
using UnityEngine;

namespace Ashfall.EditorTools
{
    // 생성 하늘 파노라마 (§10.2 "그라데이션 스카이박스 Ash_Black → Rust_Red, 낮게 걸린 흐린 태양", M6 설계 §1.3, Q3·Q4). 순수 함수.
    // equirect(위도·경도) 2:1 텍스처를 내장 Skybox/Panoramic(Latitude Longitude, 회전 0)으로 그린다. 좌표 규약은 그 셰이더와 같다:
    //   u = 0.5 − atan2(z, x) / 2π,  v = 1 − acos(y) / π  (v = 1이 천정, v = 0이 천저)
    // 색 (sRGB 공간에서 섞는다 — 팔레트가 sRGB, 텍스처도 sRGB 임포트):
    //   고도 ≥ 0: 지평선(horizonToken) → 천정(zenithToken)
    //   고도 −band ~ 0: 지평선 아래 띠 — 지평선 색 → 안개색(belowHorizonToken). 원경 지형을 덮는 Dried_Blood 안개와 이어 보이게(Q3)
    //   고도 ≤ −band: belowHorizonToken
    //   + 태양 번짐(sunGlowToken) + 태양 원반(sunCoreToken). sunHaze가 클수록 원반 가장자리가 퍼지고 둘 다 약해진다(1 = 안 보임).
    // TODO(GDD-?) Q4: 곡선 지수(AtmosphereConfigSO.skyGradientExponent 0.6)·번짐 세기(sunGlowStrength 0.8)는 임시값(M6 리뷰 낮음 4로 SO로 옮김).
    public static class SkyPanorama
    {

        // 태양 방향 (월드, 단위 벡터): 방위 +Z = 0°, 시계 방향(+X = 90°), 고도는 지평선 위 각도.
        public static Vector3 SunDirection(AtmosphereConfigSO cfg)
        {
            return DirectionFromAngles(cfg.sunAzimuthDeg, cfg.sunElevationDeg);
        }

        public static Vector3 DirectionFromAngles(float azimuthDeg, float elevationDeg)
        {
            float az = azimuthDeg * Mathf.Deg2Rad;
            float el = elevationDeg * Mathf.Deg2Rad;
            float c = Mathf.Cos(el);
            return new Vector3(Mathf.Sin(az) * c, Mathf.Sin(el), Mathf.Cos(az) * c);
        }

        // 방향 → 파노라마 UV (Skybox/Panoramic Latitude Longitude 규약)
        public static Vector2 DirectionToUv(Vector3 dir)
        {
            Vector3 d = dir.sqrMagnitude > 0f ? dir.normalized : Vector3.up;
            float latitude = Mathf.Acos(Mathf.Clamp(d.y, -1f, 1f));
            float longitude = Mathf.Atan2(d.z, d.x);
            float u = 0.5f - longitude / (2f * Mathf.PI);
            float v = 1f - latitude / Mathf.PI;
            return new Vector2(u, v);
        }

        // UV → 방향 (DirectionToUv의 역)
        public static Vector3 UvToDirection(Vector2 uv)
        {
            float longitude = (0.5f - uv.x) * 2f * Mathf.PI;
            float latitude = (1f - uv.y) * Mathf.PI;
            float s = Mathf.Sin(latitude);
            return new Vector3(s * Mathf.Cos(longitude), Mathf.Cos(latitude), s * Mathf.Sin(longitude));
        }

        // 방향의 하늘 색 (sRGB, 알파 1)
        public static Color Sample(Vector3 dir, AtmosphereConfigSO cfg)
        {
            PaletteSO palette = cfg.palette;
            Color zenith = Token(palette, cfg.zenithToken);
            Color horizon = Token(palette, cfg.horizonToken);
            Color below = Token(palette, cfg.belowHorizonToken);

            Vector3 d = dir.sqrMagnitude > 0f ? dir.normalized : Vector3.up;
            float elevation = Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f)) * Mathf.Rad2Deg;

            Color c;
            if (elevation >= 0f)
            {
                float t = Mathf.Pow(Mathf.Clamp01(elevation / 90f), Mathf.Max(1e-3f, cfg.skyGradientExponent));
                c = Color.Lerp(horizon, zenith, t);
            }
            else
            {
                float band = Mathf.Max(cfg.horizonBandDeg, 1e-3f);
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(-elevation / band));
                c = Color.Lerp(horizon, below, t);
            }

            float haze = Mathf.Clamp01(cfg.sunHaze);
            float visible = 1f - haze;
            if (visible > 0f)
            {
                Vector3 sun = SunDirection(cfg);
                float angle = Vector3.Angle(d, sun);

                float glowRadius = Mathf.Max(cfg.sunGlowDeg, 1e-3f);
                float glow = 1f - Mathf.Clamp01(angle / glowRadius);
                c = Color.Lerp(c, Token(palette, cfg.sunGlowToken), glow * glow * Mathf.Clamp01(cfg.sunGlowStrength) * visible);

                float disk = Mathf.Max(cfg.sunDiskDeg, 1e-3f);
                // 흐릴수록 원반 가장자리가 넓게 퍼진다 (haze 0: 원반 반지름 + 0.5°, haze 1: + 원반 반지름 × 2)
                float edge = disk + 0.5f + haze * disk * 2f;
                float core = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(disk * 0.5f, edge, angle));
                c = Color.Lerp(c, Token(palette, cfg.sunCoreToken), core * visible);
            }
            c.a = 1f;
            return c;
        }

        // 텍스처 픽셀 (x, y) 중심의 색. y = 0이 아래 행(Texture2D 규약).
        public static Color SamplePixel(int x, int y, int width, int height, AtmosphereConfigSO cfg)
        {
            var uv = new Vector2((x + 0.5f) / width, (y + 0.5f) / height);
            return Sample(UvToDirection(uv), cfg);
        }

        // 전체 픽셀 (행 우선, 아래 행부터 — Texture2D.SetPixels32 순서)
        public static Color32[] Render(int width, int height, AtmosphereConfigSO cfg)
        {
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                pixels[y * width + x] = SamplePixel(x, y, width, height, cfg);
            return pixels;
        }

        private static Color Token(PaletteSO palette, PaletteToken token)
        {
            return palette != null ? palette.Get(token) : Color.magenta;
        }
    }
}
