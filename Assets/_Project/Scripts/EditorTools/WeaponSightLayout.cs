using System.Collections.Generic;
using UnityEngine;

namespace Ashfall.EditorTools
{
    // 무기별 가늠자 시선 배치표 (정조준·탄도 AB-Q1, docs/design/AdsBallistics_design.md §2). 레벨 데이터 — 런타임 수치가 아니다.
    // 시선 = 뒤쪽 점(Rear, 가늠자 구멍 중심·가늠자 홈 윗면) → 앞쪽 점(Front, 가늠쇠 끝). VM 프리팹 자식 SightAnchor가 Rear에 놓이고 +z가 Front를 향한다.
    // 스킨 좌표 = 원본 패키지 프리팹 루트 로컬(m, 스킨 맞춤 배율 적용 전) — Apply Asset Swap이 스킨 크기·위치를 바꿔도 그대로 맞는다.
    //   값은 WeaponSightProbe(Report Sight Geometry·ReportVertices) 정점 조사로 정했다(2026-10-28, 부품 이름·정점 높이는 각 줄 주석).
    // 블록아웃 좌표 = VM 루트 로컬(m). 스킨이 없을 때(패키지 없는 clone, Remove Skins) 자리표시 모양 윗면에 둔다(M7WeaponAssets.Specs).
    // TODO(GDD-?) AB-Q1: 사람 플레이 확인 전 값. 보정이 필요하면 아래 숫자만 고치고 Ashfall/Setup/Weapons/Fit Sight Anchors.
    public static class WeaponSightLayout
    {
        public const string AnchorChild = "SightAnchor";   // Weapons WeaponController.SightAnchorChildName과 같은 값 — 바꿀 때 같이 바꿀 것

        public sealed class Entry
        {
            public readonly string WeaponId;
            public readonly Vector3 SkinRear;
            public readonly Vector3 SkinFront;
            public readonly Vector3 BlockoutRear;
            public readonly Vector3 BlockoutFront;
            public readonly string RearPart;    // 원본 부품 이름 — Fit Sight Anchors가 앵커 근처 정점으로 검증
            public readonly string FrontPart;

            public Entry(string weaponId, string rearPart, string frontPart, Vector3 skinRear, Vector3 skinFront, Vector3 blockoutRear, Vector3 blockoutFront)
            {
                WeaponId = weaponId;
                SkinRear = skinRear;
                SkinFront = skinFront;
                BlockoutRear = blockoutRear;
                BlockoutFront = blockoutFront;
                RearPart = rearPart;
                FrontPart = frontPart;
            }
        }

        public static readonly IReadOnlyList<Entry> Entries = new[]
        {
            // 권총 Pistol_F: 가늠자 = BarrelGuard.001 뒤 블록(z -0.0825..-0.0700) 윗면 y 0.0663(홈 바닥 0.0629),
            //   가늠쇠 = 같은 부품 앞 기둥(z 0.1320..0.1443, x ±0.0013) 끝 y 0.0658 — "가늠쇠 끝 = 가늠자 윗면" 정렬.
            new Entry("WPN-PST-01", "Pistol_F_BarrelGuard.001", "Pistol_F_BarrelGuard.001",
                new Vector3(0f, 0.0663f, -0.0762f), new Vector3(0f, 0.0658f, 0.1382f),
                new Vector3(0f, 0.0225f, -0.07f), new Vector3(0f, 0.0225f, 0.11f)),
            // SMG SMG_F: 가늠자 = Sight_Back_Part 구멍(z -0.0245..-0.0132) 안쪽 위 0.1340·아래 0.1198 → 중심 0.1269,
            //   가늠쇠 = Sight_Front_Part 기둥(z 0.3118..0.3230, x ±0.0005) 끝 y 0.1281 (덮개 고리 0.1353은 기둥 앞뒤 면에만 있다).
            new Entry("WPN-SMG-01", "SMG_F_Sight_Back_Part", "SMG_F_Sight_Front_Part",
                new Vector3(0f, 0.1269f, -0.0189f), new Vector3(0f, 0.1281f, 0.3174f),
                new Vector3(0f, 0.035f, -0.16f), new Vector3(0f, 0.035f, 0.16f)),
            // 소총 AR_A_1: 가늠자 = AR_A_Sight(운반 손잡이) 구멍(z -0.0366..-0.0216) 안쪽 위 0.1805·아래 0.1638 → 중심 0.1722,
            //   가늠쇠 = AR_A_Barrel 기둥(z 0.5183..0.5333, x ±0.0013) 끝 y 0.1717 (보호 귀 0.1779는 기둥 좌우).
            new Entry("WPN-RIF-01", "AR_A_Sight", "AR_A_Barrel",
                new Vector3(0f, 0.1722f, -0.0291f), new Vector3(0f, 0.1717f, 0.5258f),
                new Vector3(0f, 0.04f, -0.3f), new Vector3(0f, 0.04f, 0.3f)),
            // 산탄총 ShotGun_F: 가늠자 = BarrelGuard 고리(z 0.1801..0.1842) 안쪽 위 0.0834·아래 0.0727 → 중심 0.0781,
            //   가늠쇠 = ShotGun_F_Barrel 기둥(z 0.6170..0.6307, x ±0.0014) 끝 y 0.0777.
            new Entry(M7Names.ShotgunId, "ShotGun_F_BarrelGuard", "ShotGun_F_Barrel",
                new Vector3(0f, 0.0781f, 0.1822f), new Vector3(0f, 0.0777f, 0.6239f),
                new Vector3(0f, 0.04f, -0.2f), new Vector3(0f, 0.04f, 0.6f)),
            // 지정사수 Recon_P: 조준경이 아니라 가늠자(Recon_P_Sight, z 0.2125..0.2230 귀 윗면 0.1088, 홈 바닥 0.1025) + 총구 가늠쇠
            //   (Recon_P_Barrel z 0.8923..0.8988, x ±0.0005, 끝 y 0.1000 — 보호 귀 0.1078은 좌우). 가늠쇠가 홈 바닥보다 낮아
            //   시선이 총열 쪽으로 약 0.7° 기운다(정조준 때 뷰모델을 그만큼 들어 시선을 카메라 정면에 맞춘다).
            //   블록아웃은 조준경 원통(지름 0.04, 중심 y 0.065) 윗면을 따라 본다(원통 안은 막혀 있어서).
            new Entry(M7Names.DmrId, "Recon_P_Sight", "Recon_P_Barrel",
                new Vector3(0f, 0.1088f, 0.2178f), new Vector3(0f, 0.1000f, 0.8955f),
                new Vector3(0f, 0.085f, -0.11f), new Vector3(0f, 0.085f, 0.11f)),
        };

        public static Entry Find(string weaponId)
        {
            foreach (Entry e in Entries)
                if (e.WeaponId == weaponId)
                    return e;
            return null;
        }

        // 시선 두 점(같은 좌표계) → 앵커 회전 (+z = 뒤 → 앞, 위 = +y 쪽)
        public static Quaternion LineRotation(Vector3 rear, Vector3 front)
        {
            Vector3 forward = front - rear;
            return forward.sqrMagnitude > 1e-10f ? Quaternion.LookRotation(forward.normalized, Vector3.up) : Quaternion.identity;
        }
    }
}
