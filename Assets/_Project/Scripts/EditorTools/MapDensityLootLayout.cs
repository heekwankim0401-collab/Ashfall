using System;
using System.Collections.Generic;
using System.Text;
using Ashfall.Core;
using UnityEngine;

namespace Ashfall.EditorTools
{
    // 맵 밀도 ③ 새 상자 배치 생성기 (docs/design/MapDensity_design.md §6.1 "새 상자 112", SYS-INV §6). 순수 — 씬 입력은 SiteContext·벽 목록·콜백으로 받는다.
    // 후보: 구조물 면(벽·건물·컨테이너·차량 등 콜라이더 발자국)마다 면 앞 CrateFaceOffsetM에 상자 중심, 면을 따라 CandidateStepM 간격.
    // 거르기: MapDensityLayout.IsClear(CrateRule — 맵 경계·스폰·탈출·스위치·도로 띠·저격 시선·보스 거점·순찰 구간·기존 경로 통로·콜라이더·지점·경사·지면 높이 차)
    //   + 접근점(열린 면 앞 AccessDistanceM)이 콜라이더 밖 + accessOk 콜백(씬: NavMesh·스폰에서 도달·눈높이 레이).
    // 고르기: 구역마다 seed 고정(Mix(CrateSeed, StableHash(zone))) 섞기 → 기존 컨테이너·고른 상자와 CrateMinSpacingM 이상, 구조물 하나에 1개(부족하면 2개)까지.
    // 같은 입력이면 같은 결과(결정적). 이름 = LootCrate_{zone}_{nnn} (구역 안 서수, 생성 순서) = LootSpawnSlot.slotKey.
    public static class MapDensityLootLayout
    {
        public const float CrateSizeM = 1f;               // M4 상자와 같은 1m 상자
        public const float CrateFaceOffsetM = 0.9f;       // 구조물 면 → 상자 중심 (설계 §6.1)
        // 레벨 상수 — 전부 TODO(GDD-?) 임시값(맵 밀도 기본값, 플레이 검증 전). 설계 §6.1 값과 다른 것은 "설계와 다름"으로 표시(리뷰 L11).
        public const float CandidateStepM = 2f;           // 면을 따라 후보 간격 (설계에 값 없음)
        public const float CratePointScale = 0.6f;        // TODO(GDD-?) 설계와 다름(설계 = 2.5m 그대로, 외곽 후보 부족으로 0.6배) — 엄폐·적·순찰·상자 지점 최소 거리 2.5m × 0.6 = 1.5m (상자 1m는 엄폐 자리를 막지 않는다 — 스폰·탈출·스위치는 그대로)
        public const float CandidateEdgeInsetM = 0.6f;    // 면 끝에서 들여쓰기
        public const float MinFaceLengthM = 1.5f;         // 이보다 짧은 면에는 두지 않는다
        public const float CrateMinSpacingM = 8f;         // 상자 ↔ 상자·트렁크 (설계 §6.1)
        public const float CrateFallbackSpacingM = 6f;    // TODO(GDD-?) 설계에 없음: 구역 후보가 모자랄 때만 마지막 단계에서 쓰는 간격 (Rebuild 뒤 외곽 25/26 부족 대응)
        public const float AccessDistanceM = 1.2f;        // 상자 중심 → 접근점 (열린 면 앞, 눈높이 레이가 interactRangeM 안)
        public const float AccessColliderClearM = 0.45f;  // 접근점 ↔ 콜라이더 발자국 (사람이 서는 자리)
        public const float CrateObstacleGapM = 0.3f;      // 상자 ↔ 다른 콜라이더 (붙은 면은 면 앞 0.9 − 0.5 = 0.4m라 통과)
        public const float CrateMaxSlopeDeg = 15f;
        public const float CrateMaxGroundSpanM = 0.35f;   // 상자 발자국 안 지면 높이 차

        public const string NamePrefix = "LootCrate_";
        public const string TrunkPrefix = "Trunk_";

        public static string CrateName(string zone, int ordinal) => $"{NamePrefix}{zone}_{ordinal:000}";
        public static string TrunkName(string zone, int ordinal) => $"{TrunkPrefix}{zone}_{ordinal:000}";

        internal static MapDensityLayout.Clearance CrateRule => new MapDensityLayout.Clearance
        {
            ObstacleGapM = CrateObstacleGapM,
            PointScale = CratePointScale,
            MaxSlopeDeg = CrateMaxSlopeDeg,
            MaxGroundSpanM = CrateMaxGroundSpanM,
            AllowPoiFootprint = true,
        };

        // 상자를 붙일 구조물 면 하나의 원천 (콜라이더 발자국). Source = 같은 구조물 판정용 이름.
        public readonly struct Wall
        {
            public readonly string Source;
            public readonly Footprint Footprint;

            public Wall(string source, Footprint footprint)
            {
                Source = source;
                Footprint = footprint;
            }
        }

        public sealed class CratePlan
        {
            public string Name;
            public Vector2 XZ;
            public float YawDeg;
            public string Zone;
            public Vector2 AccessXZ;     // 접근점 (열린 면 앞)
            public string Source;

            public Footprint Footprint => new Footprint(XZ, Vector2.one * (CrateSizeM * 0.5f), YawDeg);
        }

        // 벽 하나의 후보 (면 4개 × 면을 따라). 순서 = 면 +x, −x, +z, −z → 면 위치 순.
        public static List<CratePlan> CandidatesFor(Wall wall)
        {
            var list = new List<CratePlan>();
            Footprint f = wall.Footprint;
            Vector2 ax = f.AxisX, az = f.AxisZ;
            var sides = new (Vector2 normal, Vector2 tangent, float halfNormal, float halfTangent)[]
            {
                (ax, az, f.Half.x, f.Half.y), (-ax, az, f.Half.x, f.Half.y), (az, ax, f.Half.y, f.Half.x), (-az, ax, f.Half.y, f.Half.x),
            };
            foreach (var side in sides)
            {
                float length = side.halfTangent * 2f;
                if (length < MinFaceLengthM)
                    continue;
                Vector2 mid = f.Center + side.normal * side.halfNormal;
                float span = Mathf.Max(0f, side.halfTangent - CandidateEdgeInsetM);
                int steps = Mathf.Max(0, Mathf.FloorToInt(span * 2f / CandidateStepM));
                float start = -steps * CandidateStepM * 0.5f;
                for (int i = 0; i <= steps; i++)
                {
                    Vector2 onFace = mid + side.tangent * (start + i * CandidateStepM);
                    Vector2 center = onFace + side.normal * CrateFaceOffsetM;
                    list.Add(new CratePlan
                    {
                        XZ = center,
                        YawDeg = f.YawDeg,
                        AccessXZ = center + side.normal * AccessDistanceM,
                        Source = wall.Source,
                    });
                }
            }
            return list;
        }

        // 구역별 want개(없거나 0이면 건너뜀)를 고른다. existing = 기존 상자·트렁크 위치(간격용). accessOk = 씬 확인(없으면 통과).
        // 부족하면 report에 적고 고른 만큼만 돌려준다(호출자가 개수를 확인해 실패로 만든다).
        public static List<CratePlan> GenerateCrates(MapDensityLayout.SiteContext ctx, IReadOnlyList<Wall> walls, IReadOnlyDictionary<string, int> want,
            IReadOnlyList<Vector2> existing, Func<CratePlan, bool> accessOk, out string report)
        {
            var sb = new StringBuilder();
            var scratch = new List<int>();
            var byZone = new Dictionary<string, List<CratePlan>>(StringComparer.Ordinal);
            int total = 0, clear = 0;
            var rejected = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (Wall wall in walls)
                foreach (CratePlan c in CandidatesFor(wall))
                {
                    total++;
                    if (!MapDensityLayout.IsClear(c.Footprint, ctx, CrateRule, scratch, out string why))
                    {
                        string reason = why.Split(' ')[0];   // 이유 종류만 (엄폐 '이름' → 엄폐)
                        rejected[reason] = (rejected.TryGetValue(reason, out int n) ? n : 0) + 1;
                        continue;
                    }
                    if (!AccessClear(c.AccessXZ, ctx, scratch))
                    {
                        rejected["접근점 콜라이더"] = (rejected.TryGetValue("접근점 콜라이더", out int n2) ? n2 : 0) + 1;
                        continue;
                    }
                    clear++;
                    c.Zone = LootZoneRules.ZoneOf(c.XZ);
                    if (!byZone.TryGetValue(c.Zone, out List<CratePlan> list))
                        byZone[c.Zone] = list = new List<CratePlan>();
                    list.Add(c);
                }
            sb.Append($"  상자 후보 {total}, 규칙 통과 {clear} (거절:");
            var reasons = new List<string>(rejected.Keys);
            reasons.Sort(StringComparer.Ordinal);
            foreach (string r in reasons)
                sb.Append($" {r} {rejected[r]}");
            sb.AppendLine(")");

            var chosen = new List<CratePlan>();
            var taken = new List<Vector2>(existing ?? Array.Empty<Vector2>());
            var zones = new List<string>(want.Keys);
            zones.Sort(StringComparer.Ordinal);
            int accessFails = 0;
            foreach (string zone in zones)
            {
                int need = want[zone];
                if (need <= 0)
                    continue;
                byZone.TryGetValue(zone, out List<CratePlan> pool);
                pool = pool != null ? new List<CratePlan>(pool) : new List<CratePlan>();
                var random = new System.Random(SeedMath.Mix(MapDensityLayout.CrateSeed, SeedMath.StableHash(zone)));
                for (int i = pool.Count - 1; i > 0; i--)
                {
                    int j = random.Next(i + 1);
                    (pool[i], pool[j]) = (pool[j], pool[i]);
                }
                var perSource = new Dictionary<string, int>(StringComparer.Ordinal);
                var failedAccess = new HashSet<CratePlan>();
                int got = 0;
                // 단계: 구조물당 1개 → 2개 → (모자랄 때만) 3개 → 3개 + 간격 CrateFallbackSpacingM. 앞 단계로 채워지면 뒤 단계는 돌지 않는다(기존 배치 그대로).
                for (int stage = 1; stage <= 4 && got < need; stage++)
                    foreach (CratePlan c in pool)
                    {
                        if (got >= need)
                            break;
                        if (c.Name != null || failedAccess.Contains(c))
                            continue;
                        int limit = Math.Min(stage, 3);
                        if ((perSource.TryGetValue(c.Source ?? "", out int used) ? used : 0) >= limit)
                            continue;
                        if (TooClose(c.XZ, taken, stage == 4 ? CrateFallbackSpacingM : CrateMinSpacingM))
                            continue;
                        if (accessOk != null && !accessOk(c))
                        {
                            failedAccess.Add(c);
                            accessFails++;
                            continue;
                        }
                        got++;
                        c.Name = CrateName(zone, got);
                        perSource[c.Source ?? ""] = used + 1;
                        taken.Add(c.XZ);
                        chosen.Add(c);
                    }
                sb.AppendLine($"  {zone}: {got}/{need} (후보 {pool.Count})");
            }
            sb.AppendLine($"  접근 확인 실패로 건너뛴 후보 {accessFails}");
            report = sb.ToString();
            return chosen;
        }

        private static bool AccessClear(Vector2 access, MapDensityLayout.SiteContext ctx, List<int> scratch)
        {
            Footprint probe = Footprint.Circle(access, 0f);
            foreach (int id in ctx.Obstacles.Query(probe, AccessColliderClearM, scratch))
                if (ctx.Obstacles[id].Distance(access) < AccessColliderClearM)
                    return false;
            return true;
        }

        private static bool TooClose(Vector2 p, List<Vector2> others, float spacing)
        {
            float s2 = spacing * spacing;
            foreach (Vector2 o in others)
                if ((o - p).sqrMagnitude < s2)
                    return true;
            return false;
        }
    }
}
