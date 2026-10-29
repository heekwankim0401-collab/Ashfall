using System;
using System.IO;
using System.Linq;
using Ashfall.Core;
using Ashfall.Raid;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Ashfall.EditorTools
{
    // M6 비주얼 자리표시 에셋 생성 (M6 설계 §1.3, §2, §3.2·§3.4, ART-*, Q2~Q7·Q30). 전부 TODO(GDD-?) 자리표시 — 실제 에셋 교체 위치는 설계 §2.3.
    // 원본 수치: Data/Raid/AtmosphereConfig.asset(+ Palette). 여기서 만드는 것은 그 파생물이라 매 실행 다시 기록한다(Q30):
    //   텍스처 3개(Art/Generated: 하늘 파노라마·재 점·지면 노이즈) — 바이트가 같으면 쓰지 않는다
    //   팔레트 머티리얼 6개(Art/Materials/Palette/M_*.mat) + 기존 M2~M4 머티리얼 4개 재도색(GUID 유지, Q7)
    //   하늘 머티리얼 M6_Sky(Skybox/Panoramic), 재 머티리얼 M6_AshParticle(URP Particles/Unlit 투명)
    //   지형 레이어 TL_AshSoil(회색 노이즈 × Dried_Blood), 볼륨 프로필 VP_RaidAtmosphere(§10.2 포스트)
    // 사람이 볼륨 프로필·머티리얼 색을 인스펙터에서 바꾸면 다음 실행에 덮인다 — 조정은 AtmosphereConfig·Palette에서 한다.
    // 하늘·재 텍스처는 AtmosphereConfig.skyTextureOverride/ashTextureOverride가 있으면 생성물 대신 그것을 머티리얼에 넣는다(§2.3).
    // 멱등: 있으면 같은 에셋을 고쳐 쓴다(GUID 유지). 같은 입력이면 두 번째 실행에서 파일 바이트가 같다.
    // 배치모드: -executeMethod Ashfall.EditorTools.M6ArtSetup.CreateAssetsMenu
    public static class M6ArtSetup
    {
        private const string Tag = "[M6ArtSetup]";

        public const string SkyShaderName = "Skybox/Panoramic";
        public const string AshShaderName = "Universal Render Pipeline/Particles/Unlit";

        // 자리표시 텍스처 크기·시드 (에셋 사양, TODO(GDD-?)). 보기 튜닝 값(노이즈 회색 하한·타일 크기)은 AtmosphereConfigSO(리뷰 낮음 4).
        public const int AshDotSize = 64;
        public const int GroundNoiseSize = 256;
        public const int GroundNoiseSeed = 6006;

        [MenuItem("Ashfall/Setup/M6/Create Art Assets")]
        public static void CreateAssetsMenu()
        {
            if (!CreateAssets())
                throw new InvalidOperationException($"{Tag} 실패 — 위 오류 로그 확인");
        }

        // 성공하면 true. AtmosphereConfig가 없으면 false(먼저 Ashfall/Setup/Create M6 Data).
        public static bool CreateAssets()
        {
            var cfg = AssetDatabase.LoadAssetAtPath<AtmosphereConfigSO>(M6Names.AtmosphereConfigPath);
            if (cfg == null)
            {
                Debug.LogError($"{Tag} {M6Names.AtmosphereConfigPath} 없음 — 먼저 Ashfall/Setup/Create M6 Data.");
                return false;
            }
            PaletteSO palette = cfg.palette != null ? cfg.palette : AssetDatabase.LoadAssetAtPath<PaletteSO>(M6Names.PalettePath);
            if (palette == null)
            {
                Debug.LogError($"{Tag} 팔레트 없음 ({M6Names.PalettePath})");
                return false;
            }
            if (cfg.palette == null)
                Debug.LogWarning($"{Tag} AtmosphereConfig.palette가 비어 {M6Names.PalettePath}를 쓴다.");

            SetupUtil.EnsureFolder(M6Names.GeneratedArtFolder);
            SetupUtil.EnsureFolder(M6Names.PaletteMaterialFolder);
            SetupUtil.EnsureFolder(M6Names.PostProcessFolder);
            SetupUtil.EnsureFolder(M6Names.TerrainArtFolder);

            // ── 텍스처 ──
            Texture2D sky = cfg.skyTextureOverride;
            if (sky == null)
            {
                Vector2Int size = cfg.skyTextureSize;
                if (size.x <= 0 || size.y <= 0)
                    size = new Vector2Int(1024, 512);
                // 하늘은 팔레트가 비어 있으면 안 되므로 임시 사본에 팔레트를 연결해 그린다(SO 원본은 건드리지 않는다)
                AtmosphereConfigSO source = WithPalette(cfg, palette, out bool temporary);
                sky = WriteTexture(M6Names.SkyTexturePath, size.x, size.y, SkyPanorama.Render(size.x, size.y, source),
                    new TextureSpec { Srgb = true, Mipmaps = false, WrapU = TextureWrapMode.Repeat, WrapV = TextureWrapMode.Clamp, Alpha = false });
                if (temporary)
                    Object.DestroyImmediate(source);
            }
            Texture2D ashDot = cfg.ashTextureOverride;
            if (ashDot == null)
                ashDot = WriteTexture(M6Names.AshDotTexturePath, AshDotSize, AshDotSize, RenderAshDot(AshDotSize),
                    new TextureSpec { Srgb = true, Mipmaps = true, WrapU = TextureWrapMode.Clamp, WrapV = TextureWrapMode.Clamp, Alpha = true });
            Texture2D groundNoise = WriteTexture(M6Names.GroundNoiseTexturePath, GroundNoiseSize, GroundNoiseSize,
                RenderGroundNoise(GroundNoiseSize, GroundNoiseSeed, Mathf.Clamp01(cfg.groundNoiseMin)),
                new TextureSpec { Srgb = false, Mipmaps = true, WrapU = TextureWrapMode.Repeat, WrapV = TextureWrapMode.Repeat, Alpha = false });

            // ── 머티리얼 ──
            foreach (PaletteMaterialMap.Spec spec in PaletteMaterialMap.Materials.Values)
            {
                Material m = EnsureMaterial(spec.Path, spec.Unlit ? PaletteMaterialMap.UnlitShader : PaletteMaterialMap.LitShader, spec.Name);
                if (m != null)
                    ApplySpec(m, spec, palette);
            }
            foreach (PaletteMaterialMap.Spec spec in PaletteMaterialMap.LegacyMaterials)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(spec.Path);
                if (m == null)
                {
                    // 기존 머티리얼은 M2~M4 메뉴 몫 — 없으면 만들지 않는다
                    Debug.LogWarning($"{Tag} 기존 머티리얼 없음(건너뜀): {spec.Path}");
                    continue;
                }
                ApplySpec(m, spec, palette);
            }

            Material skyMaterial = EnsureMaterial(M6Names.SkyMaterialPath, SkyShaderName, "M6_Sky");
            if (skyMaterial != null)
                ApplySkyMaterial(skyMaterial, sky);
            Material ashMaterial = EnsureMaterial(M6Names.AshParticleMaterialPath, AshShaderName, "M6_AshParticle");
            if (ashMaterial != null)
                ApplyAshMaterial(ashMaterial, ashDot, palette.Get(cfg.ashToken), cfg.ashAlpha);

            // ── 지형 레이어 · 볼륨 프로필 ──
            EnsureTerrainLayer(groundNoise, palette.Get(PaletteToken.DriedBlood), cfg.groundTileM);
            EnsureVolumeProfile(cfg, palette);

            AssetDatabase.SaveAssets();
            Debug.Log($"{Tag} 완료: 텍스처·머티리얼 {PaletteMaterialMap.Materials.Count}+{PaletteMaterialMap.LegacyMaterials.Count}+2·" +
                      $"{M6Names.TerrainLayerPath}·{M6Names.RaidVolumeProfilePath} (파생물 — 수치는 AtmosphereConfig에서 조정, Q30)");
            return true;
        }

        // ---------- 텍스처 ----------

        public struct TextureSpec
        {
            public bool Srgb;
            public bool Mipmaps;
            public TextureWrapMode WrapU;
            public TextureWrapMode WrapV;
            public bool Alpha;
        }

        // 64×64 부드러운 원 (RGB 흰색, 알파 = 가운데 1 → 가장자리 0). 재 입자용.
        public static Color32[] RenderAshDot(int size)
        {
            var pixels = new Color32[size * size];
            float c = (size - 1) * 0.5f;
            float radius = size * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float r = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / radius;
                float a = Mathf.Clamp01(1f - r);
                a = a * a * (3f - 2f * a);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
            return pixels;
        }

        // 이음매 없는 회색 값 노이즈 (주기 = 텍스처 크기). 회색 값은 [min, 1]. 같은 seed면 같은 결과.
        public static Color32[] RenderGroundNoise(int size, int seed, float min)
        {
            int[] periods = { 4, 8, 16, 32 };
            float[] amps = { 0.45f, 0.3f, 0.15f, 0.1f };
            var random = new System.Random(seed);
            var lattices = new float[periods.Length][];
            for (int o = 0; o < periods.Length; o++)
            {
                lattices[o] = new float[periods[o] * periods[o]];
                for (int i = 0; i < lattices[o].Length; i++)
                    lattices[o][i] = (float)random.NextDouble();
            }

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float v = 0f, total = 0f;
                for (int o = 0; o < periods.Length; o++)
                {
                    int p = periods[o];
                    float fx = (float)x / size * p, fy = (float)y / size * p;
                    int x0 = Mathf.FloorToInt(fx), y0 = Mathf.FloorToInt(fy);
                    float tx = fx - x0, ty = fy - y0;
                    tx = tx * tx * (3f - 2f * tx);
                    ty = ty * ty * (3f - 2f * ty);
                    float[] l = lattices[o];
                    int xa = x0 % p, xb = (x0 + 1) % p, ya = y0 % p, yb = (y0 + 1) % p;
                    float a = Mathf.Lerp(l[ya * p + xa], l[ya * p + xb], tx);
                    float b = Mathf.Lerp(l[yb * p + xa], l[yb * p + xb], tx);
                    v += Mathf.Lerp(a, b, ty) * amps[o];
                    total += amps[o];
                }
                byte g = (byte)Mathf.RoundToInt(Mathf.Lerp(min, 1f, v / total) * 255f);
                pixels[y * size + x] = new Color32(g, g, g, 255);
            }
            return pixels;
        }

        // PNG로 인코딩해 바이트가 다를 때만 쓰고, 임포트 설정을 맞춘다(다를 때만 다시 임포트).
        private static Texture2D WriteTexture(string path, int width, int height, Color32[] pixels, TextureSpec spec)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply(false);
            byte[] png = texture.EncodeToPNG();
            Object.DestroyImmediate(texture);

            string full = Path.GetFullPath(path);
            bool exists = File.Exists(full);
            if (!exists || !File.ReadAllBytes(full).SequenceEqual(png))
            {
                File.WriteAllBytes(full, png);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                Debug.Log($"{Tag} 텍스처 {(exists ? "갱신" : "생성")}: {path} ({width}×{height}) — TODO(GDD-?) 자리표시");
            }

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                Debug.LogError($"{Tag} 텍스처 임포터 없음: {path}");
                return null;
            }
            bool changed = false;
            if (importer.textureType != TextureImporterType.Default) { importer.textureType = TextureImporterType.Default; changed = true; }
            if (importer.sRGBTexture != spec.Srgb) { importer.sRGBTexture = spec.Srgb; changed = true; }
            if (importer.mipmapEnabled != spec.Mipmaps) { importer.mipmapEnabled = spec.Mipmaps; changed = true; }
            if (importer.wrapModeU != spec.WrapU) { importer.wrapModeU = spec.WrapU; changed = true; }
            if (importer.wrapModeV != spec.WrapV) { importer.wrapModeV = spec.WrapV; changed = true; }
            if (importer.filterMode != FilterMode.Bilinear) { importer.filterMode = FilterMode.Bilinear; changed = true; }
            if (importer.npotScale != TextureImporterNPOTScale.None) { importer.npotScale = TextureImporterNPOTScale.None; changed = true; }
            // 그라데이션 띠(밴딩)를 피하려고 압축하지 않는다 (하늘 1024×512 RGBA32 = 2MB, TODO(GDD-?) 실제 에셋 때 재검토)
            if (importer.textureCompression != TextureImporterCompression.Uncompressed) { importer.textureCompression = TextureImporterCompression.Uncompressed; changed = true; }
            TextureImporterAlphaSource alphaSource = spec.Alpha ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
            if (importer.alphaSource != alphaSource) { importer.alphaSource = alphaSource; changed = true; }
            if (importer.alphaIsTransparency != spec.Alpha) { importer.alphaIsTransparency = spec.Alpha; changed = true; }
            int maxSize = Mathf.NextPowerOfTwo(Mathf.Max(width, height));
            if (importer.maxTextureSize < maxSize) { importer.maxTextureSize = maxSize; changed = true; }
            if (changed)
                importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static AtmosphereConfigSO WithPalette(AtmosphereConfigSO cfg, PaletteSO palette, out bool temporary)
        {
            temporary = cfg.palette == null;
            if (!temporary)
                return cfg;
            AtmosphereConfigSO copy = Object.Instantiate(cfg);
            copy.palette = palette;
            return copy;
        }

        // ---------- 머티리얼 ----------

        private static Material EnsureMaterial(string path, string shaderName, string name)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
                return existing;
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogError($"{Tag} 셰이더를 찾지 못했다: {shaderName} ({path})");
                return null;
            }
            SetupUtil.EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            var material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, path);
            Debug.Log($"{Tag} 머티리얼 생성: {path} — TODO(GDD-?) 자리표시");
            return material;
        }

        // 팔레트 색·속성 기록 (셰이더는 그대로 — 기존 머티리얼은 GUID·셰이더 유지)
        internal static void ApplySpec(Material m, PaletteMaterialMap.Spec spec, PaletteSO palette)
        {
            Color color = palette.Get(spec.Token);
            color.a = spec.Alpha;
            SetBaseColor(m, color);
            if (m.HasProperty("_Metallic"))
                m.SetFloat("_Metallic", spec.Metallic);
            if (m.HasProperty("_Smoothness"))
                m.SetFloat("_Smoothness", spec.Smoothness);
            if (m.HasProperty("_EmissionColor"))
            {
                if (spec.Emission > 0f)
                {
                    Color e = palette.Get(spec.Token) * spec.Emission;
                    e.a = 1f;
                    m.SetColor("_EmissionColor", e);
                    m.EnableKeyword("_EMISSION");
                    m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
                else
                {
                    m.SetColor("_EmissionColor", Color.black);
                    m.DisableKeyword("_EMISSION");
                    m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                }
            }
            SetSurface(m, spec.Transparent);
            // 맵 밀도 §8.1 산포물 속성 — 기존 머티리얼은 기본값(인스턴싱 끔·한면)과 같아 바뀌지 않는다
            if (m.enableInstancing != spec.Instancing)
                m.enableInstancing = spec.Instancing;
            float cull = spec.DoubleSided ? (float)CullMode.Off : (float)CullMode.Back;
            if (m.HasProperty("_Cull") && !Mathf.Approximately(m.GetFloat("_Cull"), cull))
                m.SetFloat("_Cull", cull);
            EditorUtility.SetDirty(m);
        }

        // _BaseColor + 구형 호환 _Color. URP가 다음 저장 때 _Color를 _BaseColor에서 (선형 왕복값으로) 맞춰 쓰므로
        // 처음부터 같은 값을 넣어 두 번째 실행에서 바이트가 바뀌지 않게 한다.
        internal static void SetBaseColor(Material m, Color color)
        {
            m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Color"))
            {
                Color roundTrip = color.linear.gamma;
                roundTrip.a = color.a;
                m.SetColor("_Color", roundTrip);
            }
        }

        // URP Surface Type (BaseShaderGUI.SetupMaterialBlendMode와 같은 조합, 위험 R2 — Alpha 블렌드만)
        internal static void SetSurface(Material m, bool transparent)
        {
            if (m.HasProperty("_Surface"))
                m.SetFloat("_Surface", transparent ? 1f : 0f);
            if (m.HasProperty("_Blend"))
                m.SetFloat("_Blend", 0f); // Alpha
            if (transparent && m.HasProperty("_BlendModePreserveSpecular"))
                m.SetFloat("_BlendModePreserveSpecular", 0f);
            m.SetFloat("_SrcBlend", (float)(transparent ? BlendMode.SrcAlpha : BlendMode.One));
            m.SetFloat("_DstBlend", (float)(transparent ? BlendMode.OneMinusSrcAlpha : BlendMode.Zero));
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)(transparent ? BlendMode.OneMinusSrcAlpha : BlendMode.Zero));
            m.SetFloat("_ZWrite", transparent ? 0f : 1f);
            if (transparent)
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            else
                m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.DisableKeyword("_ALPHAMODULATE_ON");
            m.SetOverrideTag("RenderType", transparent ? "Transparent" : "Opaque");
            m.renderQueue = transparent ? (int)RenderQueue.Transparent : -1; // -1 = 셰이더 기본(Geometry)
            m.SetShaderPassEnabled("DepthOnly", !transparent);
            m.SetShaderPassEnabled("ShadowCaster", !transparent);
        }

        internal static void ApplySkyMaterial(Material m, Texture2D sky)
        {
            m.SetTexture("_MainTex", sky);
            m.SetColor("_Tint", new Color(0.5f, 0.5f, 0.5f, 0.5f)); // 회색 0.5 = 원색 그대로 (내장 스카이박스 규약)
            m.SetFloat("_Exposure", 1f);
            m.SetFloat("_Rotation", 0f);
            m.SetFloat("_Mapping", 1f);      // Latitude Longitude Layout
            m.SetFloat("_ImageType", 0f);    // 360°
            m.SetFloat("_MirrorOnBack", 0f);
            m.SetFloat("_Layout", 0f);       // 3D 레이아웃 없음
            m.DisableKeyword("_MAPPING_6_FRAMES_LAYOUT");
            EditorUtility.SetDirty(m);
        }

        // 재 입자: URP Particles/Unlit, 투명 Alpha 블렌드, 텍스처 알파 × 색(Ash_Grey, ashAlpha). 그림자·소프트 파티클 없음.
        internal static void ApplyAshMaterial(Material m, Texture2D dot, Color ash, float alpha)
        {
            ash.a = alpha;
            m.SetTexture("_BaseMap", dot);
            SetBaseColor(m, ash);
            m.SetFloat("_ColorMode", 0f); // Multiply
            m.SetFloat("_Cull", (float)CullMode.Back);
            m.SetFloat("_AlphaClip", 0f);
            m.DisableKeyword("_ALPHATEST_ON");
            m.SetFloat("_SoftParticlesEnabled", 0f);
            m.SetFloat("_CameraFadingEnabled", 0f);
            m.SetFloat("_FlipbookBlending", 0f);
            // ParticleGUI.SetMaterialKeywords가 꺼진 기능에 쓰는 값과 같게 (다음 저장 때 URP가 다시 써서 바이트가 바뀌지 않게)
            m.SetVector("_SoftParticleFadeParams", Vector4.zero);
            m.SetVector("_CameraFadeParams", new Vector4(0f, Mathf.Infinity, 0f, 0f));
            m.DisableKeyword("_COLOROVERLAY_ON");
            m.DisableKeyword("_COLORCOLOR_ON");
            m.DisableKeyword("_COLORADDSUBDIFF_ON");
            m.DisableKeyword("_DISTORTION_ON");
            m.DisableKeyword("_SOFTPARTICLES_ON");
            m.DisableKeyword("_FADING_ON");
            m.DisableKeyword("_FLIPBOOKBLENDING_ON");
            SetSurface(m, true);
            EditorUtility.SetDirty(m);
        }

        // ---------- 지형 레이어 ----------

        // TL_AshSoil: 회색 노이즈(선형) × diffuseRemapMax. URP TerrainLit은 선형 공간에서 곱하므로 Dried_Blood의 선형값을 넣는다.
        internal static TerrainLayer EnsureTerrainLayer(Texture2D noise, Color driedBlood, float tileM)
        {
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(M6Names.TerrainLayerPath);
            if (layer == null)
            {
                layer = new TerrainLayer { name = "TL_AshSoil" };
                AssetDatabase.CreateAsset(layer, M6Names.TerrainLayerPath);
                Debug.Log($"{Tag} 지형 레이어 생성: {M6Names.TerrainLayerPath} — TODO(GDD-?) 자리표시");
            }
            Color linear = driedBlood.linear;
            layer.diffuseTexture = noise;
            layer.tileSize = new Vector2(tileM, tileM);
            layer.tileOffset = Vector2.zero;
            layer.diffuseRemapMin = Vector4.zero;
            layer.diffuseRemapMax = new Vector4(linear.r, linear.g, linear.b, 1f);
            layer.metallic = 0f;
            layer.smoothness = 0f;
            layer.specular = Color.black;
            EditorUtility.SetDirty(layer);
            return layer;
        }

        // ---------- 볼륨 프로필 (§10.2) ----------

        internal static VolumeProfile EnsureVolumeProfile(AtmosphereConfigSO cfg, PaletteSO palette)
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(M6Names.RaidVolumeProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                profile.name = "VP_RaidAtmosphere";
                AssetDatabase.CreateAsset(profile, M6Names.RaidVolumeProfilePath);
                Debug.Log($"{Tag} 볼륨 프로필 생성: {M6Names.RaidVolumeProfilePath}");
            }

            var colorAdjustments = GetOrAdd<ColorAdjustments>(profile);
            colorAdjustments.saturation.Override(cfg.saturation);
            colorAdjustments.contrast.Override(cfg.contrast);
            colorAdjustments.postExposure.Override(cfg.postExposureEv); // 맵 개선 §2 (기본 0 = 이전과 같음)
            colorAdjustments.colorFilter.Override(Color.Lerp(Color.white, palette.Get(cfg.tintToken), cfg.tintStrength));

            var filmGrain = GetOrAdd<FilmGrain>(profile);
            filmGrain.type.Override(ToUrp(cfg.filmGrainType));
            filmGrain.intensity.Override(cfg.filmGrainIntensity);

            var vignette = GetOrAdd<Vignette>(profile);
            vignette.color.Override(palette.Get(cfg.vignetteToken));
            vignette.intensity.Override(cfg.vignetteIntensity);
            vignette.smoothness.Override(cfg.vignetteSmoothness);

            var bloom = GetOrAdd<Bloom>(profile);
            bloom.intensity.Override(cfg.bloomIntensity);
            bloom.threshold.Override(cfg.bloomThreshold);

            // 파이프라인 기본 프로필(SampleSceneProfile)의 Tonemapping을 덮도록 항상 명시한다 (§1.3)
            var tonemapping = GetOrAdd<Tonemapping>(profile);
            tonemapping.mode.Override(ToUrp(cfg.tonemapping));

            foreach (VolumeComponent component in profile.components)
                EditorUtility.SetDirty(component);
            EditorUtility.SetDirty(profile);
            return profile;
        }

        private static T GetOrAdd<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (profile.TryGet(out T existing))
            {
                existing.active = true;
                return existing;
            }
            T component = profile.Add<T>(false);
            component.name = typeof(T).Name;
            component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            component.active = true;
            AssetDatabase.AddObjectToAsset(component, profile);
            return component;
        }

        public static FilmGrainLookup ToUrp(FilmGrainChoice choice)
        {
            return Enum.TryParse(choice.ToString(), out FilmGrainLookup value) ? value : FilmGrainLookup.Medium1;
        }

        public static TonemappingMode ToUrp(TonemapChoice choice)
        {
            switch (choice)
            {
                case TonemapChoice.None: return TonemappingMode.None;
                case TonemapChoice.Aces: return TonemappingMode.ACES;
                default: return TonemappingMode.Neutral;
            }
        }
    }
}
