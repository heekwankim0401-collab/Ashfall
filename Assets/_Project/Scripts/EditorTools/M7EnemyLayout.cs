using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using L = Ashfall.EditorTools.CrimsonBasinLayout;

namespace Ashfall.EditorTools
{
    // Crimson Basin M7 적 배치 데이터 (M7 설계 §3.5, Q22·Q25·Q28~Q30). 레벨 데이터지 게임 수치가 아니다.
    // 기존 20명(CrimsonBasinLayout.Squads)은 그대로 두고 14명을 더한다: SNP 2, MUT 무리 2 × 4, BOS 1 + BGD 3 → 레이드 34명.
    // 씬에는 7B-2c M7EnemySceneApply가 놓는다(이름 접두어 M7_, NavMesh 스냅). 이 파일은 데이터와 검사(순수 함수)만 가진다.
    // 좌표: 월드 XZ(CrimsonBasinLayout과 같음, +z = 북). 높이는 지형에서 샘플한다.
    // TODO(GDD-?) Q30: GDD에 좌표가 없다. 설계 §3.5 "대략" 위치에서 스폰 거리(≥ 190m)·저격수 시야 여유·퍼치 높이를 만족하게 고른 임시값.
    public static class M7EnemyLayout
    {
        public enum Kind
        {
            Sniper,      // AI-SNP
            Mutant,      // AI-MUT
            Boss,        // AI-BOS
            Bodyguard,   // AI-BGD
        }

        public static string ArchetypePath(Kind kind)
        {
            switch (kind)
            {
                case Kind.Sniper: return M7Names.SniperArchetypePath;
                case Kind.Mutant: return M7Names.MutantArchetypePath;
                case Kind.Boss: return M7Names.BossArchetypePath;
                default: return M7Names.BodyguardArchetypePath;
            }
        }

        // 분대 하나. 대원마다 유형과 시작 위치가 따로 있다(M4 SquadSlots는 3자리라 4명 무리에 겹치므로 시작 위치를 명시한다).
        // 경로가 있으면 순찰(변이체 무리 배회), 없으면 시작 자리 정지. 저격수는 WatchXZ(내려다보는 지점)가 있고 시선 = 그쪽.
        // 장비 인덱스는 모두 0(유형마다 무장 하나, 변이체는 무장 없음).
        public readonly struct SquadPlan
        {
            public readonly string Name;          // M7_ 접두어 포함. 대원 이름 = Name + 순번(1부터), 경로 이름 = "PatrolRoute_" + Name (M4 규칙)
            public readonly string PoiId;         // 가까운 POI (정보용)
            public readonly int SquadId;
            public readonly Kind[] Members;
            public readonly Vector2[] StartsXZ;   // Members와 같은 수
            public readonly Vector2[] RouteXZ;    // null = 순찰 없음
            public readonly Vector2? WatchXZ;     // 저격수 퍼치만: 감시 지점 (높이 검사·시선)
            private readonly float yawDeg;        // 정지 배치 시선 (WatchXZ·RouteXZ가 있으면 그쪽이 우선)

            public SquadPlan(string name, string poiId, int squadId, Kind[] members, Vector2[] startsXZ,
                Vector2[] routeXZ, Vector2? watchXZ, float yawDeg)
            {
                Name = name;
                PoiId = poiId;
                SquadId = squadId;
                Members = members;
                StartsXZ = startsXZ;
                RouteXZ = routeXZ;
                WatchXZ = watchXZ;
                this.yawDeg = yawDeg;
            }

            public int Count => Members.Length;
            public bool IsPerch => WatchXZ.HasValue;
            public string MemberName(int index) => Name + (index + 1);

            // 시작 시선 (도, +z = 0, 시계 방향): 퍼치 = 감시 지점, 순찰 = 첫 구간 방향, 그 밖 = 지정값
            public float YawDeg
            {
                get
                {
                    if (WatchXZ.HasValue)
                        return YawTowards(StartsXZ[0], WatchXZ.Value);
                    if (RouteXZ != null && RouteXZ.Length >= 2)
                        return YawTowards(RouteXZ[0], RouteXZ[1]);
                    return yawDeg;
                }
            }
        }

        public static float YawTowards(Vector2 from, Vector2 to)
        {
            Vector2 d = to - from;
            float yaw = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
            return yaw < 0f ? yaw + 360f : yaw;
        }

        private static Kind[] Repeat(Kind kind, int count)
        {
            var result = new Kind[count];
            for (int i = 0; i < count; i++)
                result[i] = kind;
            return result;
        }

        private const string P = M7Names.M7EnemyPrefix;

        // 분대 번호는 기존(1~9) 다음부터. 같은 번호 = 같은 분대(연락 공유) — 보스와 호위는 한 분대.
        public const int FirstSquadId = 10;

        public static readonly SquadPlan[] Squads =
        {
            // SNP_PYL: 송전탑 언덕 정상 고원(평탄부 반경 75m) — 남서쪽 비탈·정유소 방향을 내려다본다. 변전 잔해(235, 306) 남쪽.
            new SquadPlan(P + "SNP_PYL", "POI-PYL", FirstSquadId, new[] { Kind.Sniper },
                new[] { new Vector2(240f, 296f) }, null, new Vector2(150f, 210f), 0f),

            // SNP_HWY: 고속도로 남동 능선 — 맵 가장자리 상승 비탈(z −430, 바닥보다 약 15m 위)에서 북쪽 고속도로를 본다.
            new SquadPlan(P + "SNP_HWY", "POI-HWY", FirstSquadId + 1, new[] { Kind.Sniper },
                new[] { new Vector2(120f, -430f) }, null, new Vector2(120f, -330f), 0f),

            // MUT_REF: 정유소 북쪽 외곽(북벽 z 0 바깥, 방사능 구역 RAD-REF 가장자리) 배회. SP-E에서 190m 밖이 되게 서쪽으로 붙였다.
            new SquadPlan(P + "MUT_REF", "POI-REF", FirstSquadId + 2, Repeat(Kind.Mutant, 4),
                new[] { new Vector2(232f, 15f), new Vector2(229f, 17f), new Vector2(229f, 13f), new Vector2(226f, 15f) },
                new[] { new Vector2(232f, 15f), new Vector2(275f, 15f), new Vector2(275f, 45f), new Vector2(232f, 45f) },
                null, 0f),

            // MUT_HWY: 고속도로 동쪽 끝(블록아웃 x ≤ 90 바깥) 배회.
            new SquadPlan(P + "MUT_HWY", "POI-HWY", FirstSquadId + 3, Repeat(Kind.Mutant, 4),
                new[] { new Vector2(142f, -290f), new Vector2(139f, -288f), new Vector2(139f, -292f), new Vector2(136f, -290f) },
                new[] { new Vector2(142f, -290f), new Vector2(185f, -290f), new Vector2(185f, -320f), new Vector2(142f, -320f) },
                null, 0f),

            // BOS_VIL: 마을 외곽 북동 구조물(집 (−280, 100)·(−262, 132) 사이 빈터) — 기존 CoverPoint, 거점 25m. 1번 = 보스, 2~4번 = 호위.
            new SquadPlan(P + "BOS_VIL", "POI-VIL", FirstSquadId + 4,
                new[] { Kind.Boss, Kind.Bodyguard, Kind.Bodyguard, Kind.Bodyguard },
                new[] { new Vector2(-270f, 120f), new Vector2(-266f, 116f), new Vector2(-274f, 116f), new Vector2(-270f, 125f) },
                null, null, 225f),
        };

        // ── 수 (설계 §3.5: 14명 → 레이드 34명) ──
        public const int Count = 14;
        public const int SniperCount = 2;
        public const int MutantCount = 8;
        public const int BossCount = 1;
        public const int BodyguardCount = 3;
        // 레이드 씬 총수 = M4 고정 배치 20 + M7 14 (CrimsonBasinLayout.ExpectedEnemyCount는 "M4 고정 배치 20" 의미 그대로)
        public const int ExpectedTotalEnemyCount = L.ExpectedEnemyCount + Count;

        // ── 검사 기준 (TODO(GDD-?) Q22·Q30) ──
        public const float MinDistanceFromSpawnM = 190f;        // 모든 시작 위치·순찰 지점 ↔ 모든 스폰
        public const float SniperSightRangeM = 150f;            // AI-SNP sightRange (데이터 테스트가 에셋 값과 같은지 확인)
        public const float SniperSpawnMarginM = 50f;            // 저격수는 스폰이 시야 + 이 여유 밖
        public const float MinPerchAdvantageM = 5f;             // 퍼치 지면이 감시 지점 지면보다 이만큼 높다
        public const float MapEdgeMarginM = 10f;                // 맵 경계 벽 안쪽 여유
        public const float NavMeshSnapDistanceM = 3f;           // 씬 적용(2c)의 NavMesh 스냅 허용 거리 (M4와 같음)

        public static int CountOf(Kind kind)
        {
            int n = 0;
            foreach (SquadPlan plan in Squads)
                foreach (Kind k in plan.Members)
                    if (k == kind)
                        n++;
            return n;
        }

        public static int TotalCount()
        {
            int n = 0;
            foreach (SquadPlan plan in Squads)
                n += plan.Count;
            return n;
        }

        // 가장 가까운 스폰까지 수평 거리
        public static float MinDistanceToSpawns(Vector2 xz, out string spawnId)
        {
            float best = float.MaxValue;
            spawnId = null;
            foreach (L.Spawn spawn in L.Spawns)
            {
                float d = Vector2.Distance(xz, spawn.PositionXZ);
                if (d < best)
                {
                    best = d;
                    spawnId = spawn.Id;
                }
            }
            return best;
        }

        // 분대가 차지하는 모든 점 (시작 위치 + 순찰 지점)
        public static IEnumerable<(string label, Vector2 xz)> Points(SquadPlan plan)
        {
            for (int i = 0; i < plan.StartsXZ.Length; i++)
                yield return (plan.MemberName(i), plan.StartsXZ[i]);
            if (plan.RouteXZ != null)
                for (int i = 0; i < plan.RouteXZ.Length; i++)
                    yield return ($"PatrolRoute_{plan.Name}/{i}", plan.RouteXZ[i]);
        }

        // ---------- 검사 (순수, 씬·에셋 없음) ----------

        // 수: 대원·시작 위치 수가 맞고, 유형별 합이 설계 §3.5와 같다.
        public static bool CheckCounts(out string report)
        {
            var sb = new StringBuilder();
            bool ok = true;
            foreach (SquadPlan plan in Squads)
                if (plan.Members == null || plan.StartsXZ == null || plan.Members.Length != plan.StartsXZ.Length || plan.Count == 0)
                {
                    sb.AppendLine($"  {plan.Name}: 대원 {plan.Members?.Length} ≠ 시작 위치 {plan.StartsXZ?.Length}");
                    ok = false;
                }
            int total = TotalCount();
            ok &= total == Count && CountOf(Kind.Sniper) == SniperCount && CountOf(Kind.Mutant) == MutantCount
                  && CountOf(Kind.Boss) == BossCount && CountOf(Kind.Bodyguard) == BodyguardCount;
            sb.AppendLine($"  M7 적 {total}/{Count} (SNP {CountOf(Kind.Sniper)}, MUT {CountOf(Kind.Mutant)}, " +
                          $"BOS {CountOf(Kind.Boss)}, BGD {CountOf(Kind.Bodyguard)}), 레이드 합계 {L.ExpectedEnemyCount + total}");
            report = sb.ToString();
            return ok;
        }

        // 스폰 거리: 모든 점이 모든 스폰에서 MinDistanceFromSpawnM 이상, 저격수 시작 위치는 sniperSightRangeM + 여유 이상.
        public static bool CheckSpawnDistances(float sniperSightRangeM, out string report)
        {
            var sb = new StringBuilder();
            bool ok = true;
            float nearest = float.MaxValue;
            string nearestPair = "";
            float sniperLimit = sniperSightRangeM + SniperSpawnMarginM;
            foreach (SquadPlan plan in Squads)
            {
                foreach ((string label, Vector2 xz) in Points(plan))
                {
                    float d = MinDistanceToSpawns(xz, out string spawnId);
                    if (d < nearest)
                    {
                        nearest = d;
                        nearestPair = $"{label} ↔ {spawnId}";
                    }
                    if (d < MinDistanceFromSpawnM)
                    {
                        sb.AppendLine($"  {label}: {spawnId}에서 {d:0.0}m < {MinDistanceFromSpawnM}m");
                        ok = false;
                    }
                }
                for (int i = 0; i < plan.Count; i++)
                {
                    if (plan.Members[i] != Kind.Sniper)
                        continue;
                    float d = MinDistanceToSpawns(plan.StartsXZ[i], out string spawnId);
                    if (d < sniperLimit)
                    {
                        sb.AppendLine($"  {plan.MemberName(i)}(저격수): {spawnId}에서 {d:0.0}m < 시야 {sniperSightRangeM} + 여유 {SniperSpawnMarginM}m");
                        ok = false;
                    }
                }
            }
            sb.AppendLine($"  스폰과 가장 가까운 M7 점: {nearest:0.0}m ({nearestPair}), 기준 ≥ {MinDistanceFromSpawnM}m, 저격수 ≥ {sniperLimit}m");
            report = sb.ToString();
            return ok;
        }

        public static bool CheckSpawnDistances(out string report) => CheckSpawnDistances(SniperSightRangeM, out report);

        // 맵 안: 모든 점이 경계 벽 안쪽 MapEdgeMarginM 이상.
        public static bool CheckInsideMap(out string report)
        {
            var sb = new StringBuilder();
            bool ok = true;
            float limit = L.BoundaryInsetM - MapEdgeMarginM;
            foreach (SquadPlan plan in Squads)
                foreach ((string label, Vector2 xz) in Points(plan))
                    if (Mathf.Abs(xz.x) > limit || Mathf.Abs(xz.y) > limit)
                    {
                        sb.AppendLine($"  {label}: {xz} 맵 경계 {limit}m 밖");
                        ok = false;
                    }
            report = sb.ToString();
            return ok;
        }

        // 분대 번호: 분대마다 다르고 기존 배치(CrimsonBasinLayout.Squads)와 겹치지 않는다.
        public static bool CheckSquadIds(out string report)
        {
            var sb = new StringBuilder();
            var used = new HashSet<int>();
            foreach (L.SquadPlan old in L.Squads)
                used.Add(old.SquadId);
            bool ok = true;
            foreach (SquadPlan plan in Squads)
                if (!used.Add(plan.SquadId))
                {
                    sb.AppendLine($"  {plan.Name}: 분대 번호 {plan.SquadId} 중복");
                    ok = false;
                }
            report = sb.ToString();
            return ok;
        }

        // 보스 거점: 보스 분대의 모든 대원이 보스 시작 위치에서 anchorRadiusM 안 (호위가 거점 경계 밖에서 시작하지 않게).
        public static bool CheckBossGroup(float anchorRadiusM, out string report)
        {
            var sb = new StringBuilder();
            bool ok = true;
            int bossSquads = 0;
            foreach (SquadPlan plan in Squads)
            {
                int boss = Array.IndexOf(plan.Members, Kind.Boss);
                if (boss < 0)
                    continue;
                bossSquads++;
                for (int i = 0; i < plan.Count; i++)
                {
                    float d = Vector2.Distance(plan.StartsXZ[i], plan.StartsXZ[boss]);
                    if (d > anchorRadiusM)
                    {
                        sb.AppendLine($"  {plan.MemberName(i)}: 보스에서 {d:0.0}m > 거점 {anchorRadiusM}m");
                        ok = false;
                    }
                }
                int guards = 0;
                foreach (Kind k in plan.Members)
                    if (k == Kind.Bodyguard)
                        guards++;
                if (guards != BodyguardCount)
                {
                    sb.AppendLine($"  {plan.Name}: 호위 {guards} ≠ {BodyguardCount} (§7 호위 3명 동반)");
                    ok = false;
                }
            }
            ok &= bossSquads == BossCount;
            report = sb.ToString();
            return ok;
        }

        // 퍼치 높이: 저격수 시작 지면이 감시 지점 지면보다 MinPerchAdvantageM 이상 높다(§7 "고지대 고정").
        // groundHeightM = 월드 XZ → 지면 높이(m). 씬 적용은 Terrain 샘플을, 테스트는 TerrainData 에셋(GroundFromTerrainData)을 넘긴다.
        public static bool CheckPerchHeights(Func<Vector2, float> groundHeightM, out string report)
        {
            var sb = new StringBuilder();
            bool ok = true;
            if (groundHeightM == null)
            {
                report = "  지면 높이 함수 없음";
                return false;
            }
            foreach (SquadPlan plan in Squads)
            {
                if (!plan.IsPerch)
                    continue;
                float perch = groundHeightM(plan.StartsXZ[0]);
                float watch = groundHeightM(plan.WatchXZ.Value);
                float advantage = perch - watch;
                bool pass = advantage >= MinPerchAdvantageM;
                ok &= pass;
                sb.AppendLine($"  {plan.MemberName(0)}: 퍼치 {perch:0.0}m − 감시 지점 {watch:0.0}m = {advantage:0.0}m " +
                              $"{(pass ? "≥" : "<")} {MinPerchAdvantageM}m");
            }
            report = sb.ToString();
            return ok;
        }

        // 전부 (2c가 씬 적용 전에 부른다 — false면 예외로 멈출 것)
        public static bool CheckAll(Func<Vector2, float> groundHeightM, float sniperSightRangeM, float anchorRadiusM, out string report)
        {
            bool ok = true;
            var sb = new StringBuilder();
            ok &= Append(sb, "수", CheckCounts(out string r1), r1);
            ok &= Append(sb, "스폰 거리", CheckSpawnDistances(sniperSightRangeM, out string r2), r2);
            ok &= Append(sb, "맵 안", CheckInsideMap(out string r3), r3);
            ok &= Append(sb, "분대 번호", CheckSquadIds(out string r4), r4);
            ok &= Append(sb, "보스 거점", CheckBossGroup(anchorRadiusM, out string r5), r5);
            ok &= Append(sb, "퍼치 높이", CheckPerchHeights(groundHeightM, out string r6), r6);
            report = sb.ToString();
            return ok;
        }

        private static bool Append(StringBuilder sb, string title, bool ok, string detail)
        {
            sb.AppendLine($"[{(ok ? "통과" : "실패")}] {title}");
            sb.Append(detail);
            return ok;
        }

        // TerrainData 에셋으로 지면 높이를 읽는 함수 (씬 없이 — 테스트·메뉴 사전 검사용). 지형 위치 = CrimsonBasinLayout.TerrainPosition.
        public static Func<Vector2, float> GroundFromTerrainData(TerrainData data)
        {
            if (data == null)
                return null;
            Vector3 size = data.size;
            return xz =>
            {
                float nx = Mathf.Clamp01((xz.x - L.TerrainPosition.x) / size.x);
                float nz = Mathf.Clamp01((xz.y - L.TerrainPosition.z) / size.z);
                return data.GetInterpolatedHeight(nx, nz) + L.TerrainPosition.y;
            };
        }
    }
}
