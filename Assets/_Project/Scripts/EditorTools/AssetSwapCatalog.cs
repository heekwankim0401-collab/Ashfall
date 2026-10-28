using System.Collections.Generic;
using Ashfall.Core;
using UnityEngine;

namespace Ashfall.EditorTools
{
    // 에셋 교체 배정표와 순수 맞춤 함수 (AssetSwap 설계 §4·§5). 레벨 데이터 — 원본 경로는 git에서 뺀 에셋스토어 패키지(설계 §2).
    // 스킨 프리팹(Skins/SK_{id}.prefab) 원점 = 앵커(밑면 중심 또는 총구 끝), 앞 = +z, 위 = +y.
    public static class AssetSwapCatalog
    {
        public const string SkinFolder = "Assets/_Project/Skins";               // git 제외 (EULA 원본의 파생물)
        public const string SkinMaterialFolder = SkinFolder + "/Materials";
        public const string SkinMeshFolder = SkinFolder + "/Meshes";   // 부품을 합친 메시(사람은 Idle 자세로 구움)
        public const string SkinChild = "Skin";                                  // 블록아웃 위에 얹는 자식 이름 (BlockoutSkin 루트)
        public const float TintStrength = 0.35f;                                 // TODO(GDD-?) 설계 §5: 원본 색 → 팔레트 토큰 섞는 비율
        public const float SolidTintStrength = 0.7f;                             // TODO(GDD-?) 설계 §5: 텍스처 없는 단색 원본을 토큰 쪽으로 당기는 비율
        public const float DefaultSmoothness = 0.1f;                             // TODO(GDD-?) 원본에 광택 값이 없을 때
        public const float MaxSmoothness = 0.2f;                                 // TODO(GDD-?) 설계 §5: 먼지 낀 표면
        public const int MaxMeshLods = 4;                                        // TODO(GDD-?) 설계 §5.1: 스킨 자동 Mesh LOD 단계 수(성능)

        public static string SkinPrefabPath(string id) => SkinFolder + "/SK_" + id + ".prefab";

        public enum Anchor
        {
            Bottom,    // 경계 밑면 중심 (사람·소품·구조물)
            FrontTip,  // 앞(+z) 끝 면의 중심 높이 = 총구 (무기)
        }

        // 스킨 원본 하나. Euler = 원본을 "앞 +z, 위 +y"로 돌리는 회전.
        public sealed class Source
        {
            public readonly string Id;
            public readonly string Path;
            public readonly Vector3 Euler;
            public readonly Anchor Anchor;
            public readonly PaletteToken Tint;
            public readonly bool Character;   // 휴머노이드 — Idle 클립 첫 프레임 자세로 정적 메시를 굽는다
            public readonly string[] Extras;  // 같은 원점에 함께 놓는 원본 (예: 폐가 + 지붕)

            public Source(string id, string path, Vector3 euler, Anchor anchor, PaletteToken tint, bool character = false, string[] extras = null)
            {
                Id = id;
                Path = path;
                Euler = euler;
                Anchor = anchor;
                Tint = tint;
                Character = character;
                Extras = extras ?? System.Array.Empty<string>();
            }
        }

        // 상자 fbx는 열린 면이 +z — 열린 면이 위로 오게 세운다
        private static readonly Vector3 CrateUpright = new Vector3(-90f, 0f, 0f);

        private const string Ithappy = "Assets/ithappy/Apocalypse_Free/Prefabs";
        private const string Military = "Assets/ithappy/Military_Free/Prefabs";
        private const string Abandoned = "Assets/Abandoned buildings/Prefab/abandoned_buildings";
        private const string Crates = "Assets/Lowpoly_Crates/Meshes";
        private const string Toxic = "Assets/H&L Assets/Radioactive barrel/URP/Prefabs";

        // 사람 자세 클립 — 첫 프레임을 정적 메시로 굽는다 (ithappy 휴머노이드 — 같은 휴머노이드 아바타인 모델이면 다른 패키지도 쓴다)
        public const string IdleClipPath = "Assets/ithappy/Apocalypse_Free/Animations/Adult_Animations/AC_Adult_Idle_Relaxed.anim";
        public const string LegacyIdleControllerPath = SkinFolder + "/AC_SkinIdle.controller";   // 예전 버전이 만든 것 — 지운다

        // 회전·경로는 원본 경계·방향 조사(2026-10-26 미리보기 렌더 확인)로 정했다. TODO(GDD-?) 전부 MVP 기본값.
        public static readonly IReadOnlyList<Source> Sources = new[]
        {
            // 무기 (설계 §4)
            new Source("Pistol", "Assets/Low Poly Pistol Weapon Pack 2/Prefabs/Weapons/Pistol_F.prefab", Vector3.zero, Anchor.FrontTip, PaletteToken.RustRed),
            new Source("Smg", "Assets/Low Poly SMG Weapon Pack 2/Prefabs/Weapons/SMG_F.prefab", Vector3.zero, Anchor.FrontTip, PaletteToken.RustRed),
            new Source("Rifle", "Assets/Low Poly AR Weapon Pack 1/Prefabs/Weapons/AR_A_1.prefab", Vector3.zero, Anchor.FrontTip, PaletteToken.RustRed),
            new Source("Shotgun", "Assets/Low Poly ShotGun Weapon Pack 2/Prefabs/Weapons/ShotGun_F.prefab", Vector3.zero, Anchor.FrontTip, PaletteToken.RustRed),
            new Source("Dmr", "Assets/Low Poly Weapon Pack 4_MW_1/Prefabs/Weapons/Recon_P.prefab", Vector3.zero, Anchor.FrontTip, PaletteToken.RustRed),
            // 적 몸
            new Source("Survivor", Ithappy + "/Characters/Adult_Survivor/Adult_Survivor.prefab", Vector3.zero, Anchor.Bottom, PaletteToken.AshGrey, character: true),
            new Source("Woodcutter", Ithappy + "/Characters/Adult_Woodcutter/Adult_Woodcutter.prefab", Vector3.zero, Anchor.Bottom, PaletteToken.AshGrey, character: true),
            new Source("GasMask", "Assets/Shady_3d/PREFAB/Apocalyptic character (Gas Mask +hood).prefab", Vector3.zero, Anchor.Bottom, PaletteToken.AshGrey, character: true),
            new Source("Helmet", "Assets/Shady_3d/PREFAB/Apocalyptic character (Helmet).prefab", Vector3.zero, Anchor.Bottom, PaletteToken.DriedBlood, character: true),
            new Source("Mutant", "Assets/Zombie_Mutant/prefab/SKM_Zombie_Mutant.prefab", Vector3.zero, Anchor.Bottom, PaletteToken.DriedBlood, character: true),
            // 구조물·소품
            new Source("Car", Ithappy + "/Props/Apocalypse_Car_01.prefab", Vector3.zero, Anchor.Bottom, PaletteToken.RustRed),
            new Source("Hummer", Military + "/Vehicles/Hummer_003.prefab", Vector3.zero, Anchor.Bottom, PaletteToken.RustRed),
            new Source("HouseAband", Abandoned + "/house_aband.prefab", Vector3.zero, Anchor.Bottom, PaletteToken.RustRed,
                extras: new[] { Abandoned + "/house_roof.prefab" }),
            new Source("HouseRuined", Abandoned + "/house_ruined.prefab", Vector3.zero, Anchor.Bottom, PaletteToken.RustRed),
            new Source("Container", "Assets/FREE Low Poly Shipping Container/Prefabs/Low Poly Shipping Container.prefab", Vector3.zero, Anchor.Bottom, PaletteToken.RustRed),
            new Source("Barrier", "Assets/LowPolyBarriersPackFree/BarriersAssets/Prefabs/ConcreteBarriers/ConcreteBarrier_08_v2.prefab", Vector3.zero, Anchor.Bottom, PaletteToken.AshGrey),
            new Source("HighBarrier", "Assets/LowPolyBarriersPackFree/BarriersAssets/Prefabs/ConcreteBarriers/ConcreteBarrier_39_v2.prefab", Vector3.zero, Anchor.Bottom, PaletteToken.AshGrey),
            new Source("Crate1", Crates + "/Crate_01.fbx", CrateUpright, Anchor.Bottom, PaletteToken.RustRed),
            new Source("Crate2", Crates + "/Crate_02.fbx", CrateUpright, Anchor.Bottom, PaletteToken.RustRed),
            new Source("Crate5", Crates + "/Crate_05.fbx", CrateUpright, Anchor.Bottom, PaletteToken.RustRed),
            new Source("ToxicDrum1", Toxic + "/barrel_toxic_01.prefab", Vector3.zero, Anchor.Bottom, PaletteToken.Toxic),
            new Source("ToxicDrum2", Toxic + "/barrel_toxic_02.prefab", Vector3.zero, Anchor.Bottom, PaletteToken.Toxic),
            new Source("Helicopter", Military + "/Vehicles/Helicopter_001.prefab", Vector3.zero, Anchor.Bottom, PaletteToken.RustRed),
        };

        public static Source Find(string id)
        {
            foreach (Source s in Sources)
                if (s.Id == id)
                    return s;
            return null;
        }

        // ---------- 배정 (설계 §4) ----------

        // 무기 id → 스킨
        public static readonly IReadOnlyDictionary<string, string> WeaponSkins = new Dictionary<string, string>
        {
            { "WPN-PST-01", "Pistol" },
            { "WPN-SMG-01", "Smg" },
            { "WPN-RIF-01", "Rifle" },
            { M7Names.ShotgunId, "Shotgun" },
            { M7Names.DmrId, "Dmr" },
        };

        // 적 유형 id → (몸, 총). 총 null = 무장 없음
        public static readonly IReadOnlyDictionary<string, (string Body, string Gun)> EnemySkins = new Dictionary<string, (string, string)>
        {
            { "AI-SCV", ("Survivor", "Rifle") },
            { "AI-RDR", ("Woodcutter", "Rifle") },
            { "AI-BGD", ("Woodcutter", "Rifle") },
            { "AI-SNP", ("GasMask", "Dmr") },
            { "AI-BOS", ("Helmet", "Rifle") },
            { "AI-MUT", ("Mutant", null) },
        };

        public static readonly string[] CarSkins = { "Car", "Hummer" };
        public static readonly string[] HouseSkins = { "HouseAband", "HouseRuined" };
        public static readonly string[] CrateSkins = { "Crate1", "Crate2", "Crate5" };
        public static readonly string[] DrumSkins = { "ToxicDrum1", "ToxicDrum2" };
        public const string ContainerSkin = "Container";
        public const string LowWallSkin = "Barrier";
        public const string HighWallSkin = "HighBarrier";
        public const string CrashedPlaneSkin = "Helicopter";
        public const string CrashedPlaneLandmark = "LM_CrashedPlane";
        public static readonly Vector3 CrashTiltEuler = new Vector3(6f, 0f, 18f);   // TODO(GDD-?) 추락 자세 (앞으로 숙이고 옆으로 기움)
        public const float CrashSinkM = 0.6f;                                        // TODO(GDD-?) 땅에 박힌 깊이
        public const float CrashMaxScale = 1.2f;                                     // TODO(GDD-?) 헬기는 비행기보다 작다 — 발자국에 맞춰도 실제 크기의 1.2배까지

        public const float MinFitHeightM = 0.1f;         // TODO(GDD-?) 맞춤 높이 하한 (블록 윗면이 지면보다 낮게 계산될 때)
        public const float EnemyGunLengthM = 0.85f;       // TODO(GDD-?) 적 총 스킨 길이 (자리표시 큐브 0.5m보다 실제 소총에 가깝게)
        public const float MaxWallThicknessM = 1f;        // TODO(GDD-?) 이보다 두꺼운 벽(둑 등)은 블록아웃 유지

        // 이름 끝 번호로 고르는 결정적 순환 선택 (같은 씬이면 매번 같은 스킨)
        public static string Pick(string[] options, string name)
        {
            int n = 0;
            for (int i = name.Length - 1; i >= 0 && char.IsDigit(name[i]); i--)
                n = n * 10 + (name[i] - '0');
            int hash = 0;
            foreach (char c in name)
                hash = hash * 31 + c;
            return options[(int)((uint)(n + hash) % (uint)options.Length)];
        }

        // ---------- 순수 맞춤 함수 ----------

        // 앵커 점 (경계 기준). Bottom = 밑면 중심, FrontTip = +z 면 중심.
        public static Vector3 AnchorPoint(Bounds bounds, Anchor anchor) =>
            anchor == Anchor.FrontTip
                ? new Vector3(bounds.center.x, bounds.center.y, bounds.max.z)
                : new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);

        // 균등 비율: 원본 크기 size를 target 상자 안에 넣는다(축마다 0이면 그 축은 제한 없음).
        public static float FitInside(Vector3 size, Vector3 target)
        {
            float s = float.PositiveInfinity;
            if (target.x > 0f && size.x > 0f) s = Mathf.Min(s, target.x / size.x);
            if (target.y > 0f && size.y > 0f) s = Mathf.Min(s, target.y / size.y);
            if (target.z > 0f && size.z > 0f) s = Mathf.Min(s, target.z / size.z);
            return float.IsPositiveInfinity(s) ? 1f : s;
        }

        // 블록(로컬 x 길이 blockX, 높이, z 폭 blockZ)과 원본(size)의 긴 수평 축을 맞춘다.
        // 반환: 원본에 더할 yaw(0 또는 90 — 90이면 원본 +z가 블록 +x로)와, 원본 축 기준 목표 크기.
        public static (float YawDeg, Vector3 Target) AlignLongAxis(Vector3 size, float blockX, float height, float blockZ)
        {
            bool blockLongX = blockX >= blockZ;
            bool sourceLongX = size.x >= size.z;
            return blockLongX == sourceLongX
                ? (0f, new Vector3(blockX, height, blockZ))
                : (90f, new Vector3(blockZ, height, blockX));
        }

        // 축마다 채우는 배율 (상자를 정확히 채운다 — 건물·컨테이너·벽처럼 콜라이더와 보이는 모양을 맞출 때)
        public static Vector3 FillScale(Vector3 size, Vector3 target) =>
            new Vector3(Ratio(target.x, size.x), Ratio(target.y, size.y), Ratio(target.z, size.z));

        private static float Ratio(float target, float size) => size > 1e-4f && target > 0f ? target / size : 1f;

        // 길이 length를 모듈(길이 module)로 채울 개수와, 개수에 맞춘 길이 방향 배율.
        public static (int Count, float Stretch) Tile(float length, float module)
        {
            if (module <= 0f || length <= 0f)
                return (1, 1f);
            int count = Mathf.Max(1, Mathf.RoundToInt(length / module));
            return (count, length / (count * module));
        }

        // 타일 i(0부터)의 중심 x (길이 length를 count칸으로 나눈 가운데)
        public static float TileCenter(int i, int count, float length) => (i + 0.5f) / count * length - length * 0.5f;

        // 원본 색 × 팔레트 틴트
        public static Color Tinted(Color source, Color token, float strength = TintStrength)
        {
            Color tint = Color.Lerp(Color.white, token, strength);
            return new Color(source.r * tint.r, source.g * tint.g, source.b * tint.b, source.a);
        }
    }
}
