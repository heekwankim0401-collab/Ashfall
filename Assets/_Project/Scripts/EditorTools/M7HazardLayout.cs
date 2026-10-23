using System;
using System.Collections.Generic;
using System.Text;
using Ashfall.Raid;
using UnityEngine;
using L = Ashfall.EditorTools.CrimsonBasinLayout;

namespace Ashfall.EditorTools
{
    // 정유소 방사능 구역 표시물 배치 데이터 (M7 설계 §1.5·§2.2·§3.10, Q39·Q40, STAT-RAD). 레벨 데이터지 게임 수치가 아니다.
    // 표시 = 구역 가장자리 둘레의 경고 기둥(+ 바깥을 보는 경고판)·드럼통 + 구역 안(최대 률 영역) 드럼통 무더기. 전부 Toxic 프리미티브, 콜라이더 없음.
    // 위치는 구역 설정(RaidConfigSO.radiationZones의 RAD-REF — 중심·반경·감쇠 폭)에서 계산한다 → 구역 값을 바꾸면 표시도 따라간다.
    // 씬에는 7C-2c M7HazardSceneApply가 놓는다. 이 파일은 데이터와 검사(순수 함수)만 가진다. 지도에는 표시하지 않는다(Q40, RULE-06).
    // 좌표: 월드 XZ(CrimsonBasinLayout과 같음). 각도 = +x에서 +z 쪽으로(반시계, 도). 높이는 지형에서 샘플한다.
    // TODO(GDD-?) Q40: GDD에 표시물 배치가 없다. 각도·무더기 위치는 정유소 블록아웃(M4MapSetup 탱크·배관·외벽 틈)을 피하게 고른 임시값 —
    //   M7HazardSceneApply가 씬의 장애물 콜라이더와 겹치지 않는지 확인하고, 겹치면 예외를 낸다.
    public static class M7HazardLayout
    {
        public enum Shape
        {
            Pole,   // 경고 기둥 (Cylinder)
            Sign,   // 기둥 위 경고판 (Cube, 바깥을 본다)
            Drum,   // 드럼통 (Cylinder, 서 있음)
            DrumLying,   // 쓰러진 드럼통 (Cylinder, 옆으로)
        }

        // 표시물 하나. 위치 = 지면 높이(XZ에서 샘플) + CenterHeightM. 크기 = 프리미티브 localScale.
        public readonly struct MarkerPlan
        {
            public readonly string Name;          // M7Names.ToxicMarkerPrefix 포함
            public readonly Shape Shape;
            public readonly Vector2 XZ;
            public readonly float CenterHeightM;  // 지면에서 오브젝트 중심까지
            public readonly Quaternion Rotation;
            public readonly Vector3 Scale;
            public readonly bool OnEdge;          // true = 가장자리 둘레, false = 구역 안 무더기

            public MarkerPlan(string name, Shape shape, Vector2 xz, float centerHeightM, Quaternion rotation, Vector3 scale, bool onEdge)
            {
                Name = name;
                Shape = shape;
                XZ = xz;
                CenterHeightM = centerHeightM;
                Rotation = rotation;
                Scale = scale;
                OnEdge = onEdge;
            }

            public PrimitiveType Primitive => Shape == Shape.Sign ? PrimitiveType.Cube : PrimitiveType.Cylinder;

            // 월드 축 기준 대략의 반 크기 (장애물 겹침 검사용 — 회전한 상자의 AABB)
            public Vector3 HalfExtents
            {
                get
                {
                    // 프리미티브 기본 크기: Cube 1×1×1, Cylinder 지름 1·높이 2
                    Vector3 local = Primitive == PrimitiveType.Cube
                        ? Scale * 0.5f
                        : new Vector3(Scale.x * 0.5f, Scale.y, Scale.z * 0.5f);
                    Vector3 r0 = Rotation * new Vector3(local.x, 0f, 0f);
                    Vector3 r1 = Rotation * new Vector3(0f, local.y, 0f);
                    Vector3 r2 = Rotation * new Vector3(0f, 0f, local.z);
                    return new Vector3(
                        Mathf.Abs(r0.x) + Mathf.Abs(r1.x) + Mathf.Abs(r2.x),
                        Mathf.Abs(r0.y) + Mathf.Abs(r1.y) + Mathf.Abs(r2.y),
                        Mathf.Abs(r0.z) + Mathf.Abs(r1.z) + Mathf.Abs(r2.z));
                }
            }
        }

        // ── 크기 (자리표시 기하, TODO(GDD-?) Q40) ──
        public static readonly Vector3 PoleScale = new Vector3(0.12f, 1.25f, 0.12f);   // 높이 2.5m
        public const float PoleHeightM = 2.5f;
        public static readonly Vector3 SignScale = new Vector3(0.7f, 0.45f, 0.05f);
        public const float SignCenterHeightM = 2.2f;                                    // 기둥 위쪽
        public const float SignOutwardOffsetM = 0.09f;                                  // 기둥 바깥 면 앞
        public static readonly Vector3 DrumScale = new Vector3(0.6f, 0.45f, 0.6f);     // 높이 0.9m, 지름 0.6m
        public const float DrumHeightM = 0.9f;

        // ── 가장자리 둘레 (각도, 도) ── 정유소 탱크 (245, −100)·(350, −95)를 피한다(207~227°·322~328° 금지).
        public static readonly float[] EdgePoleAnglesDeg = { 19f, 49f, 79f, 109f, 139f, 169f, 199f, 229f, 259f, 289f, 319f, 349f };
        public static readonly float[] EdgeDrumAnglesDeg = { 1f, 31f, 61f, 91f, 121f, 151f, 181f, 204f, 241f, 271f, 301f, 331f };
        // 둘레 반경 = 구역 반경 − 이 값 (표시가 률 0 경계 바로 안쪽에 선다)
        public const float EdgeInsetM = 0.5f;

        // ── 구역 안 드럼통 무더기 ── 중심 기준 XZ 오프셋(m)·무더기 회전(도). 최대 률 영역(반경 − 감쇠 폭) 안.
        public static readonly Vector2[] PileOffsetsXZ = { new Vector2(-18f, 20f), new Vector2(22f, 2f), new Vector2(-10f, -35f) };
        public static readonly float[] PileYawDeg = { 0f, 40f, 75f };
        // 무더기 한 개의 드럼통 자리 (무더기 기준, 회전 전). 서 있는 4개 + 첫 통 위에 1개 + 쓰러진 1개.
        private static readonly Vector2[] PileStandingXZ = { new Vector2(0f, 0f), new Vector2(0.7f, 0.1f), new Vector2(-0.35f, 0.62f), new Vector2(0.35f, -0.62f) };
        private static readonly Vector2 PileLyingXZ = new Vector2(-0.95f, -0.4f);

        public static readonly char[] PileLetters = { 'A', 'B', 'C' };

        public static int EdgeCount => EdgePoleAnglesDeg.Length * 2 + EdgeDrumAnglesDeg.Length;   // 기둥 + 판 + 드럼통
        public static int PileMarkerCount => PileStandingXZ.Length + 2;                            // 서 있는 4 + 위 1 + 쓰러진 1
        public static int Count => EdgeCount + PileOffsetsXZ.Length * PileMarkerCount;

        // 구역 하나에 대한 표시물 전부 (순서 = 씬 자식 순서, 결정적).
        public static List<MarkerPlan> Build(in RadiationZoneConfig zone)
        {
            var list = new List<MarkerPlan>(Count);
            string p = M7Names.ToxicMarkerPrefix;
            Vector2 c = zone.centerXZ;
            float ring = Mathf.Max(0f, zone.radiusM - EdgeInsetM);

            for (int i = 0; i < EdgePoleAnglesDeg.Length; i++)
            {
                Vector2 dir = Dir(EdgePoleAnglesDeg[i]);
                Vector2 xz = c + dir * ring;
                // 판의 +z(앞면)가 바깥을 본다 — 구역 밖에서 다가오는 사람이 읽는다
                Quaternion facing = Quaternion.Euler(0f, M7EnemyLayout.YawTowards(c, xz), 0f);
                list.Add(new MarkerPlan($"{p}Pole_{i + 1:00}", Shape.Pole, xz, PoleHeightM * 0.5f, Quaternion.identity, PoleScale, true));
                list.Add(new MarkerPlan($"{p}Sign_{i + 1:00}", Shape.Sign, xz + dir * SignOutwardOffsetM, SignCenterHeightM, facing, SignScale, true));
            }
            for (int i = 0; i < EdgeDrumAnglesDeg.Length; i++)
            {
                Vector2 xz = c + Dir(EdgeDrumAnglesDeg[i]) * ring;
                list.Add(new MarkerPlan($"{p}Drum_{i + 1:00}", Shape.Drum, xz, DrumHeightM * 0.5f, Quaternion.identity, DrumScale, true));
            }

            for (int k = 0; k < PileOffsetsXZ.Length; k++)
            {
                Vector2 pileCenter = c + PileOffsetsXZ[k];
                float yaw = PileYawDeg[k];
                Quaternion turn = Quaternion.Euler(0f, yaw, 0f);
                string pile = $"{p}Pile{PileLetters[k]}_";
                for (int j = 0; j < PileStandingXZ.Length; j++)
                {
                    Vector2 xz = pileCenter + Rotate(PileStandingXZ[j], turn);
                    list.Add(new MarkerPlan($"{pile}{j + 1}", Shape.Drum, xz, DrumHeightM * 0.5f, turn, DrumScale, false));
                }
                // 첫 통 위에 하나 (같은 XZ — 같은 지면 높이)
                Vector2 baseXZ = pileCenter + Rotate(PileStandingXZ[0], turn);
                list.Add(new MarkerPlan($"{pile}{PileStandingXZ.Length + 1}", Shape.Drum, baseXZ, DrumHeightM * 1.5f, turn, DrumScale, false));
                // 쓰러진 통: 원통 축(y)을 수평으로
                Vector2 lyingXZ = pileCenter + Rotate(PileLyingXZ, turn);
                Quaternion lying = turn * Quaternion.Euler(90f, 0f, 0f);
                list.Add(new MarkerPlan($"{pile}{PileStandingXZ.Length + 2}", Shape.DrumLying, lyingXZ, DrumScale.x * 0.5f, lying, DrumScale, false));
            }
            return list;
        }

        private static Vector2 Dir(float angleDeg)
        {
            float a = angleDeg * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        }

        private static Vector2 Rotate(Vector2 local, Quaternion turn)
        {
            Vector3 r = turn * new Vector3(local.x, 0f, local.y);
            return new Vector2(r.x, r.z);
        }

        // 구역 설정이 표시를 놓을 수 있는가 (반경·감쇠 폭).
        public static bool IsPlaceable(in RadiationZoneConfig zone) =>
            zone.radiusM > 0f && !float.IsInfinity(zone.radiusM) && zone.ratePerSec > 0f && zone.edgeFalloffM >= 0f && zone.edgeFalloffM < zone.radiusM;

        // 데이터 검사 (순수): 수·이름 중복, 둘레 표시는 감쇠 띠(반경 − 감쇠 폭 ~ 반경) 안, 무더기는 최대 률 영역(반경 − 감쇠 폭) 안,
        // 모두 맵 안, 스폰과 거리 ≥ 스폰 패드. 금지 각도는 씬 장애물 검사(M7HazardSceneApply)가 실제로 본다.
        public static bool Check(in RadiationZoneConfig zone, IReadOnlyList<MarkerPlan> plans, out string report)
        {
            var sb = new StringBuilder();
            bool ok = true;
            if (!IsPlaceable(zone))
            {
                sb.AppendLine($"  구역 {zone.id}: 반경 {zone.radiusM}·률 {zone.ratePerSec}·감쇠 {zone.edgeFalloffM} — 표시를 놓을 수 없다");
                report = sb.ToString();
                return false;
            }
            if (plans.Count != Count)
            {
                sb.AppendLine($"  표시 수 {plans.Count} ≠ {Count}");
                ok = false;
            }
            if (EdgePoleAnglesDeg.Length != EdgeDrumAnglesDeg.Length || PileOffsetsXZ.Length != PileYawDeg.Length || PileOffsetsXZ.Length > PileLetters.Length)
            {
                sb.AppendLine("  표 길이가 맞지 않는다");
                ok = false;
            }
            float inner = zone.radiusM - zone.edgeFalloffM;
            float halfX = L.TerrainSize.x * 0.5f, halfZ = L.TerrainSize.z * 0.5f;
            var names = new HashSet<string>();
            int edge = 0, inside = 0;
            foreach (MarkerPlan m in plans)
            {
                if (!names.Add(m.Name))
                {
                    sb.AppendLine($"  {m.Name}: 이름 중복");
                    ok = false;
                }
                if (!m.Name.StartsWith(M7Names.ToxicMarkerPrefix, StringComparison.Ordinal))
                {
                    sb.AppendLine($"  {m.Name}: 접두어 {M7Names.ToxicMarkerPrefix} 아님");
                    ok = false;
                }
                float d = Vector2.Distance(m.XZ, zone.centerXZ);
                if (m.OnEdge)
                {
                    edge++;
                    if (d < inner || d > zone.radiusM)
                    {
                        sb.AppendLine($"  {m.Name}: 중심에서 {d:0.0}m — 감쇠 띠 {inner:0.#}~{zone.radiusM:0.#}m 밖");
                        ok = false;
                    }
                }
                else
                {
                    inside++;
                    float reach = d + Mathf.Max(m.HalfExtents.x, m.HalfExtents.z);
                    if (reach > inner)
                    {
                        sb.AppendLine($"  {m.Name}: 중심에서 {reach:0.0}m — 최대 률 영역 {inner:0.#}m 밖");
                        ok = false;
                    }
                }
                if (Mathf.Abs(m.XZ.x) > halfX || Mathf.Abs(m.XZ.y) > halfZ)
                {
                    sb.AppendLine($"  {m.Name}: 맵 밖 {m.XZ}");
                    ok = false;
                }
                foreach (L.Spawn s in L.Spawns)
                    if (Vector2.Distance(m.XZ, s.PositionXZ) < L.SpawnPadSizeM)
                    {
                        sb.AppendLine($"  {m.Name}: 스폰 {s.Id} 패드 위");
                        ok = false;
                    }
            }
            sb.AppendLine($"  Toxic 표시 {plans.Count}개 (둘레 {edge}, 구역 안 {inside}), 구역 {zone.id} 중심 {zone.centerXZ}·반경 {zone.radiusM}m·감쇠 {zone.edgeFalloffM}m");
            report = sb.ToString();
            return ok;
        }
    }
}
