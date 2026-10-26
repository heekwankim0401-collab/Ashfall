using System.Collections.Generic;
using System.Text;
using UnityEngine;
using L = Ashfall.EditorTools.CrimsonBasinLayout;
using LandmarkKind = Ashfall.EditorTools.MapPolishPrefabs.LandmarkKind;
using PropKind = Ashfall.EditorTools.MapPolishPrefabs.PropKind;
using Shape = Ashfall.EditorTools.M4MapSetup.Shape;
using Piece = Ashfall.EditorTools.M4MapSetup.Piece;

namespace Ashfall.EditorTools
{
    // 맵 개선 배치 데이터 (docs/design/MapPolish_design.md §3·§4·§6). 레벨 데이터 — 게임 수치가 아니다(광원 세기·범위는 AtmosphereConfigSO).
    // 좌표는 CrimsonBasinLayout과 같다(월드 XZ, 원점 = 맵 중앙, +z = 북). TODO(GDD-?): GDD §8.1에 랜드마크 좌표가 없어 정한 임시 배치.
    // 순수 검사(CheckLayout)는 데이터끼리만 본다(POI·스폰·탈출·도로·적 경로·상자·저격 시선). 씬 콜라이더·NavMesh 확인은 MapPolishSceneApply.
    public static class MapPolishLayout
    {
        // ── 씬 그룹 이름 (Environment 자식 — NavMesh 굽기 대상) ──
        public const string LandmarkGroup = "MapPolish_Landmarks";
        public const string StructureGroup = "MapPolish_Structures";
        public const string LightGroup = "MapPolish_Lights";
        public const string ModelChild = "Model";     // 래퍼 아래 프리팹 인스턴스
        public const string LightChild = "Light";     // 래퍼 아래 점광원 (LightChild, LightChild_1 …)

        // ── 회피 거리 (§6, TODO(GDD-?)) ──
        public const float MinDistanceFromPointsM = 2.5f;       // 엄폐 지점·상자·적 시작·순찰 지점에서 (발자국 가장자리 기준)
        public const float MinDistanceFromSpawnExtM = 25f;      // 스폰·탈출 중심에서
        public const float MinDistanceFromSwitchM = 10f;        // EXT-02 전원 스위치에서 (데이터·씬 검사 공용, 리뷰 L3)
        public const float MinCoverExposedM = 0.9f;             // 엄폐 지점에서 드러난 구조물 높이가 이보다 낮으면 뺀다 (리뷰 M6)
        public const float MinLightAboveGroundM = 0.8f;         // 점광원은 그 자리 지면보다 이만큼 위 (지형 속 광원 방지)
        public const float RoadClearM = 2f;                     // 도로 띠 가장자리에서
        public const float SniperSightClearM = 2f;              // 저격 시선(퍼치 → 감시 지점)에서
        public const float MaxGroundSpanM = 2.5f;               // 랜드마크 발자국 안 지면 높이 차 (기초 3m가 가린다)
        public const float SniperEyeHeightM = 1.6f;
        public const float SniperTargetHeightM = 1.0f;

        // ── 랜드마크 (§4.1) ──
        public readonly struct LandmarkPlan
        {
            public readonly LandmarkKind Kind;
            public readonly Vector2 XZ;
            public readonly float YawDeg;

            public LandmarkPlan(LandmarkKind kind, Vector2 xz, float yawDeg)
            {
                Kind = kind;
                XZ = xz;
                YawDeg = yawDeg;
            }
        }

        public static readonly LandmarkPlan[] Landmarks =
        {
            new LandmarkPlan(LandmarkKind.WaterTower, new Vector2(-385f, 240f), 20f),
            new LandmarkPlan(LandmarkKind.BrokenPylon, new Vector2(60f, 200f), 35f),
            new LandmarkPlan(LandmarkKind.CrashedPlane, new Vector2(-60f, 60f), 125f),
            new LandmarkPlan(LandmarkKind.Billboard, new Vector2(150f, -225f), 210f),
            new LandmarkPlan(LandmarkKind.Billboard, new Vector2(-285f, -195f), 160f),
            new LandmarkPlan(LandmarkKind.BrokenOverpass, new Vector2(-160f, -150f), 10f),
            new LandmarkPlan(LandmarkKind.FlareStack, new Vector2(395f, 30f), 0f),
            new LandmarkPlan(LandmarkKind.RadioMast, new Vector2(60f, 380f), 0f),
        };

        // ── 중간 크기 구조물 (§4.2, POI 사이 길목 군집 12곳) — M4 블록아웃 생성기로 지면에 맞추고 엄폐 지점을 만든다 ──
        private static readonly Vector3 ContainerSize = new Vector3(6f, 2.6f, 2.4f);
        private static readonly Vector3 BusSize = new Vector3(10f, 3f, 2.6f);
        private static readonly Vector3 ShedSize = new Vector3(6f, 3f, 5f);
        private static readonly Vector3 RuinWallLong = new Vector3(10f, 3f, 0.5f);
        private static readonly Vector3 RuinWallShort = new Vector3(7f, 2.6f, 0.5f);
        private static readonly Vector3 LowWallSize = M3SceneSetup.LowWallSize;
        private static readonly Vector3 CarSize = M3SceneSetup.VehicleSize;
        private static readonly Vector3 RubbleLow = new Vector3(5f, 1.2f, 3f);
        private static readonly Vector3 RubbleHigh = new Vector3(5f, 1.6f, 4f);

        private static Piece P(Shape shape, float x, float z, Vector3 size, float yaw = 0f) => new Piece(shape, x, z, size, yaw);

        internal static readonly Piece[] Structures =
        {
            // C1 중앙 ↔ 고속도로
            P(Shape.Container, -40f, -175f, ContainerSize, 15f), P(Shape.HighWall, -54f, -166f, RuinWallLong, 80f), P(Shape.Rubble, -30f, -188f, RubbleLow, 30f),
            // C2 고속도로 북동
            P(Shape.Vehicle, 60f, -190f, BusSize, 30f), P(Shape.LowWall, 74f, -180f, LowWallSize),
            // C3 마을 ↔ 고속도로 ↔ 중앙
            P(Shape.House, -170f, -60f, ShedSize, 10f), P(Shape.Container, -156f, -72f, ContainerSize, 100f),
            // C4 마을 ↔ 저수지 (무너진 L자 담)
            P(Shape.HighWall, -190f, 140f, RuinWallLong, 45f), P(Shape.HighWall, -178f, 151f, RuinWallShort, 135f), P(Shape.Rubble, -202f, 128f, RubbleHigh, 20f),
            // C5 정유소 ↔ 중앙 (컨테이너 야적)
            P(Shape.Container, 150f, -10f, ContainerSize), P(Shape.Container, 150f, -24f, ContainerSize), P(Shape.Vehicle, 162f, 6f, CarSize, 70f),
            // C6 송전탑 언덕 ↔ 중앙
            P(Shape.House, 130f, 120f, ShedSize, -20f), P(Shape.LowWall, 142f, 108f, LowWallSize, 70f),
            // C7 저수지 ↔ 중앙 (버스 잔해)
            P(Shape.Vehicle, -110f, 230f, BusSize, 60f), P(Shape.Rubble, -96f, 242f, RubbleLow),
            // C8 마을 ↔ 고속도로 (무너진 모서리 담)
            P(Shape.HighWall, -330f, -80f, RuinWallLong), P(Shape.HighWall, -337f, -89f, RuinWallShort, 90f), P(Shape.LowWall, -318f, -96f, LowWallSize, 20f),
            // C9 정유소 ↔ 고속도로 ↔ EXT-02
            P(Shape.Container, 250f, -200f, ContainerSize, 40f), P(Shape.Container, 263f, -211f, ContainerSize, 40f), P(Shape.Vehicle, 238f, -216f, CarSize, 10f),
            // C10 서쪽 스폰 앞
            P(Shape.Rubble, -400f, -60f, RubbleHigh, 15f), P(Shape.LowWall, -389f, -71f, LowWallSize, 30f),
            // C11 동쪽 스폰 앞
            P(Shape.House, 380f, 180f, ShedSize, 25f), P(Shape.Vehicle, 368f, 193f, CarSize, 50f),
            // C12 북쪽 스폰 ↔ 저수지 ↔ 중앙
            P(Shape.Container, -20f, 330f, ContainerSize, 70f), P(Shape.HighWall, -32f, 318f, RuinWallShort, 160f),
        };

        public static int StructureCount => Structures.Length;

        // ── 광원 소품 (§3) — POI마다 2~3개 ──
        public readonly struct PropPlan
        {
            public readonly PropKind Kind;
            public readonly string PoiId;
            public readonly Vector2 XZ;
            public readonly float YawDeg;

            public PropPlan(PropKind kind, string poiId, Vector2 xz, float yawDeg)
            {
                Kind = kind;
                PoiId = poiId;
                XZ = xz;
                YawDeg = yawDeg;
            }
        }

        public static readonly PropPlan[] Props =
        {
            new PropPlan(PropKind.FireBarrel, "POI-HWY", new Vector2(-125f, -287f), 0f),
            new PropPlan(PropKind.FireBarrel, "POI-HWY", new Vector2(10f, -322f), 0f),
            new PropPlan(PropKind.StreetLamp, "POI-HWY", new Vector2(-40f, -285f), 270f),
            new PropPlan(PropKind.FireBarrel, "POI-REF", new Vector2(285f, -35f), 0f),
            new PropPlan(PropKind.FireBarrel, "POI-REF", new Vector2(325f, -106f), 0f),
            new PropPlan(PropKind.StreetLamp, "POI-REF", new Vector2(214f, -48f), 0f),
            new PropPlan(PropKind.FireBarrel, "POI-VIL", new Vector2(-305f, 95f), 0f),
            new PropPlan(PropKind.FireBarrel, "POI-VIL", new Vector2(-290f, 55f), 0f),
            new PropPlan(PropKind.StreetLamp, "POI-VIL", new Vector2(-300f, 10f), 90f),
            new PropPlan(PropKind.StreetLamp, "POI-PYL", new Vector2(252f, 276f), 150f),
            new PropPlan(PropKind.FireBarrel, "POI-PYL", new Vector2(205f, 312f), 0f),
            new PropPlan(PropKind.FireBarrel, "POI-RES", new Vector2(-175f, 330f), 0f),
            new PropPlan(PropKind.StreetLamp, "POI-RES", new Vector2(-222f, 247f), 90f),
        };

        // 배치 데이터가 만드는 점광원 수 (소품 + 랜드마크 장착)
        public static int ExpectedLightCount()
        {
            int count = 0;
            foreach (PropPlan prop in Props)
                count += MapPolishPrefabs.Props[prop.Kind].Lights.Length;
            foreach (LandmarkPlan landmark in Landmarks)
                count += MapPolishPrefabs.Landmarks[landmark.Kind].Lights.Length;
            return count;
        }

        // 구조물 발자국 외접원 반지름 (XZ)
        internal static float RadiusOf(Piece piece) => new Vector2(piece.Size.x, piece.Size.z).magnitude * 0.5f;

        // ── 순수 검사 (§6 1의 데이터 부분) ──

        // 검사 항목 하나 (이름, 중심 XZ, 발자국 외접원 반지름, POI 블록아웃 안 허용 — 광원 소품만)
        public readonly struct LayoutItem
        {
            public readonly string Name;
            public readonly Vector2 XZ;
            public readonly float Radius;
            public readonly bool InPoiAllowed;

            public LayoutItem(string name, Vector2 xz, float radius, bool inPoiAllowed)
            {
                Name = name;
                XZ = xz;
                Radius = radius;
                InPoiAllowed = inPoiAllowed;
            }
        }

        // 배치 데이터의 모든 항목 (랜드마크 → 구조물 → 광원 소품)
        public static List<LayoutItem> DataItems()
        {
            var items = new List<LayoutItem>();
            for (int i = 0; i < Landmarks.Length; i++)
                items.Add(new LayoutItem($"{Landmarks[i].Kind}#{i + 1}", Landmarks[i].XZ, MapPolishPrefabs.Landmarks[Landmarks[i].Kind].FootprintRadiusM, false));
            for (int i = 0; i < Structures.Length; i++)
                items.Add(new LayoutItem($"{Structures[i].Shape}#{i + 1}", Structures[i].XZ, RadiusOf(Structures[i]), false));
            for (int i = 0; i < Props.Length; i++)
                items.Add(new LayoutItem($"{Props[i].Kind}#{i + 1}({Props[i].PoiId})", Props[i].XZ, MapPolishPrefabs.Props[Props[i].Kind].FootprintRadiusM, true));
            return items;
        }

        // 데이터 전체 검사: 항목 검사 + 점광원 상한. maxLights = AtmosphereConfigSO.maxMapLights.
        public static bool CheckLayout(int maxLights, out string report)
        {
            bool ok = CheckItems(DataItems(), out string itemReport);
            int lights = ExpectedLightCount();
            var sb = new StringBuilder($"  랜드마크 {Landmarks.Length}, 구조물 {Structures.Length}, 광원 소품 {Props.Length}, 점광원 {lights}/{maxLights}\n");
            sb.Append(itemReport);
            if (lights > maxLights)
                ok &= Fail(sb, $"점광원 {lights}개 > 상한 {maxLights}");
            report = sb.ToString();
            return ok;
        }

        // 항목끼리·기존 레벨 데이터(맵 경계·POI·스폰·탈출·스위치·도로·적·순찰·상자·저격 시선)와의 회피를 본다. 테스트가 항목을 주입한다(리뷰 M3).
        public static bool CheckItems(IReadOnlyList<LayoutItem> items, out string report)
        {
            var sb = new StringBuilder();
            bool ok = true;
            List<(string name, Vector2 xz)> points = EnemyAndCratePoints();
            List<(Vector2 from, Vector2 to)> sightLines = SniperSightLines();
            foreach (LayoutItem item in items)
            {
                if (Mathf.Abs(item.XZ.x) + item.Radius > L.BoundaryInsetM - 10f || Mathf.Abs(item.XZ.y) + item.Radius > L.BoundaryInsetM - 10f)
                    ok &= Fail(sb, $"{item.Name}: 맵 경계에 너무 가깝다 {item.XZ}");
                if (!item.InPoiAllowed)
                    foreach (L.Poi poi in L.Pois)
                        if (InsideRect(item.XZ, poi.CenterXZ, poi.FootprintXZ * 0.5f + Vector2.one * item.Radius))
                            ok &= Fail(sb, $"{item.Name}: POI {poi.Id} 블록아웃 범위 안 {item.XZ}");
                foreach (L.Spawn spawn in L.Spawns)
                    if (Vector2.Distance(item.XZ, spawn.PositionXZ) < MinDistanceFromSpawnExtM + item.Radius)
                        ok &= Fail(sb, $"{item.Name}: 스폰 {spawn.Id}에서 {Vector2.Distance(item.XZ, spawn.PositionXZ):0.0}m");
                foreach (L.Extraction ext in L.Extractions)
                    if (Vector2.Distance(item.XZ, ext.CenterXZ) < MinDistanceFromSpawnExtM + item.Radius)
                        ok &= Fail(sb, $"{item.Name}: 탈출 {ext.Id}에서 {Vector2.Distance(item.XZ, ext.CenterXZ):0.0}m");
                if (Vector2.Distance(item.XZ, L.PowerSwitchXZ) < MinDistanceFromSwitchM + item.Radius)
                    ok &= Fail(sb, $"{item.Name}: 전원 스위치에서 {Vector2.Distance(item.XZ, L.PowerSwitchXZ):0.0}m");
                foreach (L.RoadSegment road in L.Roads)
                    if (SegmentDistance(item.XZ, road.FromXZ, road.ToXZ) < road.WidthM * 0.5f + RoadClearM + item.Radius)
                        ok &= Fail(sb, $"{item.Name}: 도로 띠 위 {item.XZ}");
                foreach (var point in points)
                    if (Vector2.Distance(item.XZ, point.xz) < MinDistanceFromPointsM + item.Radius)
                        ok &= Fail(sb, $"{item.Name}: {point.name}에서 {Vector2.Distance(item.XZ, point.xz):0.0}m");
                foreach (var line in sightLines)
                    if (SegmentDistance(item.XZ, line.from, line.to) < SniperSightClearM + item.Radius)
                        ok &= Fail(sb, $"{item.Name}: 저격 시선 {line.from}→{line.to} 위");
            }
            // 서로 겹침 (외접원)
            for (int i = 0; i < items.Count; i++)
            for (int j = i + 1; j < items.Count; j++)
                if (Vector2.Distance(items[i].XZ, items[j].XZ) < items[i].Radius + items[j].Radius + 1f)
                    ok &= Fail(sb, $"{items[i].Name} ↔ {items[j].Name} 겹침");
            report = sb.ToString();
            return ok;
        }

        // 적 시작(M4 분대 + M7 분대)·순찰 지점·상자 위치 (이름, XZ)
        public static List<(string name, Vector2 xz)> EnemyAndCratePoints()
        {
            var points = new List<(string, Vector2)>();
            foreach (L.SquadPlan squad in L.Squads)
            {
                if (squad.RouteXZ != null)
                    foreach (Vector2 p in squad.RouteXZ)
                        points.Add(($"순찰 {squad.Name}", p));
                if (squad.SpotsXZ != null)
                    foreach (Vector2 p in squad.SpotsXZ)
                        points.Add(($"적 {squad.Name}", p));
            }
            foreach (M7EnemyLayout.SquadPlan squad in M7EnemyLayout.Squads)
            {
                foreach (Vector2 p in squad.StartsXZ)
                    points.Add(($"적 {squad.Name}", p));
                if (squad.RouteXZ != null)
                    foreach (Vector2 p in squad.RouteXZ)
                        points.Add(($"순찰 {squad.Name}", p));
            }
            foreach (L.Crate crate in L.LootCrates)
                points.Add(($"상자 {crate.PoiId}", crate.PositionXZ));
            foreach (L.Crate crate in L.FlareCrates)
                points.Add(($"신호탄 상자 {crate.PoiId}", crate.PositionXZ));
            return points;
        }

        // M7 저격수 퍼치 → 감시 지점
        public static List<(Vector2 from, Vector2 to)> SniperSightLines()
        {
            var lines = new List<(Vector2, Vector2)>();
            foreach (M7EnemyLayout.SquadPlan squad in M7EnemyLayout.Squads)
                if (squad.IsPerch)
                    lines.Add((squad.StartsXZ[0], squad.WatchXZ.Value));
            return lines;
        }

        public static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = ab.sqrMagnitude < 1e-6f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + ab * t);
        }

        private static bool InsideRect(Vector2 p, Vector2 center, Vector2 half) =>
            Mathf.Abs(p.x - center.x) < half.x && Mathf.Abs(p.y - center.y) < half.y;

        private static bool Fail(StringBuilder sb, string message)
        {
            sb.AppendLine("  " + message);
            return false;
        }
    }
}
