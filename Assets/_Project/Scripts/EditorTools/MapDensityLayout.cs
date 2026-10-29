using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using L = Ashfall.EditorTools.CrimsonBasinLayout;
using Piece = Ashfall.EditorTools.M4MapSetup.Piece;
using Shape = Ashfall.EditorTools.M4MapSetup.Shape;

namespace Ashfall.EditorTools
{
    // 루트·산포 구역 (docs/design/MapDensity_design.md §3). 순수 함수 — 루트(③)와 산포(①②)가 같이 쓴다.
    // POI = POI 발자국 + ScatterMinDistanceFromPoiM(40m) 안, 외곽 = max(|x|,|z|) ≥ EdgeRiseStartM(380m, 가장자리 경사), 길목 = 나머지.
    public static class LootZoneRules
    {
        public const string Route = "ROUTE";
        public const string Outskirt = "OUTSKIRT";

        public static string ZoneOf(Vector2 xz)
        {
            foreach (L.Poi poi in L.Pois)
                if (InsideRect(xz, poi.CenterXZ, poi.FootprintXZ * 0.5f + Vector2.one * L.ScatterMinDistanceFromPoiM))
                    return poi.Id;
            return Mathf.Max(Mathf.Abs(xz.x), Mathf.Abs(xz.y)) >= L.EdgeRiseStartM ? Outskirt : Route;
        }

        // POI 블록아웃 발자국 안 (구역 반경 40m 띠는 아님)
        public static bool InsidePoiFootprint(Vector2 xz, out string poiId)
        {
            foreach (L.Poi poi in L.Pois)
                if (InsideRect(xz, poi.CenterXZ, poi.FootprintXZ * 0.5f))
                {
                    poiId = poi.Id;
                    return true;
                }
            poiId = null;
            return false;
        }

        public static IEnumerable<string> AllZoneIds()
        {
            foreach (L.Poi poi in L.Pois)
                yield return poi.Id;
            yield return Route;
            yield return Outskirt;
        }

        public static bool IsPoi(string zoneId) => zoneId != Route && zoneId != Outskirt;

        internal static bool InsideRect(Vector2 p, Vector2 center, Vector2 half) =>
            Mathf.Abs(p.x - center.x) < half.x && Mathf.Abs(p.y - center.y) < half.y;
    }

    // XZ 평면의 회전한 직사각형 (yaw는 Unity Quaternion.Euler(0, yaw, 0)과 같은 방향: 로컬 +x → (cos, −sin), +z → (sin, cos)).
    public readonly struct Footprint
    {
        public readonly Vector2 Center;
        public readonly Vector2 Half;
        public readonly float YawDeg;

        public Footprint(Vector2 center, Vector2 half, float yawDeg)
        {
            Center = center;
            Half = new Vector2(Mathf.Abs(half.x), Mathf.Abs(half.y));
            YawDeg = yawDeg;
        }

        public static Footprint Circle(Vector2 center, float radius) => new Footprint(center, new Vector2(radius, radius), 0f);

        public Vector2 AxisX
        {
            get
            {
                float r = YawDeg * Mathf.Deg2Rad;
                return new Vector2(Mathf.Cos(r), -Mathf.Sin(r));
            }
        }

        public Vector2 AxisZ
        {
            get
            {
                float r = YawDeg * Mathf.Deg2Rad;
                return new Vector2(Mathf.Sin(r), Mathf.Cos(r));
            }
        }

        public float Radius => Half.magnitude;

        public Vector2 ToLocal(Vector2 p)
        {
            Vector2 d = p - Center;
            return new Vector2(Vector2.Dot(d, AxisX), Vector2.Dot(d, AxisZ));
        }

        public Vector2 ToWorld(Vector2 local) => Center + AxisX * local.x + AxisZ * local.y;

        public Vector2 Corner(int i) => ToWorld(new Vector2((i == 0 || i == 3 ? -1 : 1) * Half.x, (i < 2 ? -1 : 1) * Half.y));

        // 경계 상자 (축 정렬, 최소·최대)
        public void Aabb(out Vector2 min, out Vector2 max)
        {
            Vector2 ax = AxisX * Half.x, az = AxisZ * Half.y;
            var ext = new Vector2(Mathf.Abs(ax.x) + Mathf.Abs(az.x), Mathf.Abs(ax.y) + Mathf.Abs(az.y));
            min = Center - ext;
            max = Center + ext;
        }

        // 점까지 거리 (안이면 0)
        public float Distance(Vector2 p)
        {
            Vector2 l = ToLocal(p);
            float dx = Mathf.Max(Mathf.Abs(l.x) - Half.x, 0f), dz = Mathf.Max(Mathf.Abs(l.y) - Half.y, 0f);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        // 선분까지 거리 (선분이 사각형을 지나면 0)
        public float DistanceToSegment(Vector2 a, Vector2 b)
        {
            if (SegmentIntersects(a, b))
                return 0f;
            float best = Mathf.Min(Distance(a), Distance(b));
            for (int i = 0; i < 4; i++)
                best = Mathf.Min(best, MapPolishLayout.SegmentDistance(Corner(i), a, b));
            return best;
        }

        // 다른 사각형까지 거리 (겹치면 0). 볼록 다각형이라 겹치지 않으면 최소 거리는 한쪽 꼭짓점 ↔ 다른 쪽 변.
        public float Distance(Footprint other)
        {
            if (Overlaps(other))
                return 0f;
            float best = float.MaxValue;
            for (int i = 0; i < 4; i++)
            {
                best = Mathf.Min(best, other.Distance(Corner(i)));
                best = Mathf.Min(best, Distance(other.Corner(i)));
            }
            return best;
        }

        // 분리축 검사 (경계 접촉은 겹침 아님)
        public bool Overlaps(Footprint other)
        {
            Vector2[] axes = { AxisX, AxisZ, other.AxisX, other.AxisZ };
            Vector2 d = other.Center - Center;
            foreach (Vector2 axis in axes)
            {
                float ra = Half.x * Mathf.Abs(Vector2.Dot(AxisX, axis)) + Half.y * Mathf.Abs(Vector2.Dot(AxisZ, axis));
                float rb = other.Half.x * Mathf.Abs(Vector2.Dot(other.AxisX, axis)) + other.Half.y * Mathf.Abs(Vector2.Dot(other.AxisZ, axis));
                if (Mathf.Abs(Vector2.Dot(d, axis)) >= ra + rb)
                    return false;
            }
            return true;
        }

        // Liang–Barsky: 로컬 좌표 선분이 [−hx, hx]×[−hz, hz]를 지나는가
        public bool SegmentIntersects(Vector2 a, Vector2 b)
        {
            Vector2 p = ToLocal(a), q = ToLocal(b);
            Vector2 d = q - p;
            float t0 = 0f, t1 = 1f;
            return Clip(-d.x, p.x + Half.x, ref t0, ref t1) && Clip(d.x, Half.x - p.x, ref t0, ref t1)
                   && Clip(-d.y, p.y + Half.y, ref t0, ref t1) && Clip(d.y, Half.y - p.y, ref t0, ref t1);
        }

        private static bool Clip(float p, float q, ref float t0, ref float t1)
        {
            if (Mathf.Approximately(p, 0f))
                return q >= 0f;
            float r = q / p;
            if (p < 0f)
            {
                if (r > t1)
                    return false;
                if (r > t0)
                    t0 = r;
            }
            else
            {
                if (r < t0)
                    return false;
                if (r < t1)
                    t1 = r;
            }
            return true;
        }
    }

    // 고정 격자 공간 색인 (생성기 후보 검사 가속). 항목 = 발자국 + 꼬리표.
    internal sealed class FootprintIndex
    {
        private const float CellM = 16f;
        private const float OriginM = -560f;
        private const int Cells = 70;   // 70 × 16 = 1120m
        private readonly List<int>[] cells = new List<int>[Cells * Cells];
        private readonly List<Footprint> items = new List<Footprint>();
        private readonly List<int> stamps = new List<int>();
        private int stamp;

        public int Count => items.Count;
        public Footprint this[int i] => items[i];

        public int Add(Footprint f)
        {
            int id = items.Count;
            items.Add(f);
            stamps.Add(0);
            f.Aabb(out Vector2 min, out Vector2 max);
            ForCells(min, max, c => (cells[c] ?? (cells[c] = new List<int>())).Add(id));
            return id;
        }

        // f에서 margin 안에 있을 수 있는 항목 번호 (중복 없음)
        public List<int> Query(Footprint f, float margin, List<int> into)
        {
            into.Clear();
            stamp++;
            f.Aabb(out Vector2 min, out Vector2 max);
            min -= Vector2.one * margin;
            max += Vector2.one * margin;
            ForCells(min, max, c =>
            {
                List<int> list = cells[c];
                if (list == null)
                    return;
                foreach (int id in list)
                    if (stamps[id] != stamp)
                    {
                        stamps[id] = stamp;
                        into.Add(id);
                    }
            });
            return into;
        }

        private static void ForCells(Vector2 min, Vector2 max, Action<int> action)
        {
            int x0 = Mathf.Clamp(Mathf.FloorToInt((min.x - OriginM) / CellM), 0, Cells - 1);
            int x1 = Mathf.Clamp(Mathf.FloorToInt((max.x - OriginM) / CellM), 0, Cells - 1);
            int z0 = Mathf.Clamp(Mathf.FloorToInt((min.y - OriginM) / CellM), 0, Cells - 1);
            int z1 = Mathf.Clamp(Mathf.FloorToInt((max.y - OriginM) / CellM), 0, Cells - 1);
            for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
                action(z * Cells + x);
        }
    }

    // 맵 밀도 배치 데이터·생성기 (docs/design/MapDensity_design.md §3·§6·§7 1). 레벨 데이터 — 게임 수치가 아니다(산포 수·간격·컬링은 MapDensityConfigSO).
    // 좌표는 CrimsonBasinLayout과 같다(월드 XZ, 원점 = 맵 중앙, +z = 북). TODO(GDD-?): GDD §8.1에 배치가 없어 정한 임시 배치.
    // 생성은 전부 System.Random(고정 seed) + 고정 순회 — 같은 입력이면 같은 결과(EditMode 두 번 생성 비교).
    // 입력(SiteContext)은 씬에서 모은 기존 콜라이더 발자국·지점·순찰 구간(MapDensitySceneApply) 또는 데이터만(테스트 FromLayoutData).
    // 설계 §6.1과 다른 점(구현 메모): POI 안도 좌표를 손으로 쓰지 않고 POI 구역마다 seed 고정 생성 — 에디터를 볼 수 없는 상태에서
    // 손 좌표는 기존 구조물·엄폐·순찰과 겹쳐 실패가 반복된다. 위치 의도는 구역(영역)·마을 중심·방향으로만 준다.
    public static class MapDensityLayout
    {
        // ── 씬 그룹 (Environment 자식 — NavMesh 굽기 대상) ──
        public const string BuildingGroup = "MapDensity_Buildings";
        public const string StructureGroup = "MapDensity_Structures";
        public const string VegetationGroup = "MapDensity_Vegetation";
        public const string LootGroup = "MapDensity_Loot";   // ③ 상자·트렁크 (이번 단계에는 만들지 않는다)

        // ── seed (설계 §6.1) ──
        public const int TreeSeed = 5051;
        public const int StructureSeed = 5052;
        public const int CrateSeed = 5053;
        public const int GrassSeed = 5054;

        // ── 목표 수 (설계 §3.1 — 구조물은 레벨 데이터, 산포 수는 MapDensityConfigSO) ──
        public const int BuildingTarget = 80;
        public const int SmallStructureTarget = 150;
        public const int CarTarget = 30;
        public const int LargeVehicleTarget = 10;

        // ── 회피 (설계 §7 1, TODO(GDD-?)) — 맵 개선 값(MapPolishLayout)에 더한 것 ──
        public const float PatrolSegmentClearM = 3f;       // 순찰 구간 선분에서
        public const float PathCorridorClearM = 2f;        // 기존 NavMesh 경로 선에서 (SiteContext.Corridors)
        public const float BossKeepOutM = 25f;             // 보스 거점(BOS_VIL 보스 시작점) 안 새 콜라이더 금지
        public const float BoundaryClearM = 10f;           // 맵 경계 안쪽 면에서
        public const float StructureGapM = 2.5f;           // 새 구조물 ↔ 모든 콜라이더 (통로 — 에이전트가 지나간다)
        public const float TreeColliderGapM = 1.0f;        // 나무 줄기 ↔ 콜라이더
        public const float BushCoverClearM = 3f;           // 엄폐 지점 3m 안 덤불 없음 (§8.4)
        public const float BuildingMaxSlopeDeg = 8f;
        public const float StructureMaxSlopeDeg = 25f;
        public const float MaxGroundSpanM = 2.5f;          // 발자국 안 지면 높이 차
        public const float FarmMinSpacingM = 60f;
        public const float RouteSmallMinSpacingM = 15f;
        public const float TownRadiusM = 34f;
        public const float TownKeepOffM = 45f;             // 길목 흩어진 구조물이 마을 중심에서 떨어지는 거리
        public const float OutskirtBandM = 22f;            // 외곽 농가 띠 폭 (EdgeRiseStartM부터 — 바깥은 경사가 커 건물이 서지 않는다)

        // ── 나무 구역 비율 (설계 §3.1: 길목 900·외곽 500·POI 100 / 1500) ──
        public const float TreeRouteShare = 0.6f;
        public const float TreeOutskirtShare = 1f / 3f;
        public static readonly string[] TreePoiIds = { "POI-VIL", "POI-RES" };   // POI는 마을·저수지 가장자리(발자국 밖 40m 띠)만

        // ── 템플릿 (블록아웃 도형·크기·가중치) ──
        public readonly struct Template
        {
            public readonly string Name;
            public readonly Shape Shape;
            public readonly Vector3 Size;
            public readonly float Weight;

            public Template(string name, Shape shape, Vector3 size, float weight)
            {
                Name = name;
                Shape = shape;
                Size = size;
                Weight = weight;
            }
        }

        // 건물 = 속 찬 House 상자 (실내 없음 §1·§17). 텐트·초소도 콜라이더는 속 찬 상자.
        public static readonly Template[] Buildings =
        {
            new Template("Cottage", Shape.House, new Vector3(7f, 3.2f, 6f), 3f),
            new Template("LongHouse", Shape.House, new Vector3(10f, 3.5f, 6f), 2f),
            new Template("Shed", Shape.House, new Vector3(5f, 2.8f, 4f), 3f),
            new Template("Warehouse", Shape.House, new Vector3(12f, 4.5f, 8f), 1f),
            new Template("Tent", Shape.House, new Vector3(5f, 2.4f, 4f), 1.5f),
            new Template("Office", Shape.House, new Vector3(8f, 4f, 7f), 1.5f),
            new Template("GuardPost", Shape.House, new Vector3(3f, 2.6f, 3f), 1f),
        };

        public static readonly Template[] SmallStructures =
        {
            new Template("Sandbag", Shape.Sandbag, new Vector3(3.2f, 1.0f, 0.9f), 3f),
            new Template("Fence", Shape.Fence, new Vector3(6f, 1.8f, 0.15f), 2f),
            new Template("Pole", Shape.Pole, new Vector3(0.35f, 8f, 0.35f), 2f),
            new Template("Debris", Shape.Debris, new Vector3(3f, 1.1f, 2.2f), 2f),
            new Template("Container", Shape.Container, new Vector3(6f, 2.6f, 2.4f), 1.5f),
            new Template("LowWall", Shape.LowWall, new Vector3(4f, 1.1f, 0.4f), 2f),
            new Template("RuinWall", Shape.HighWall, new Vector3(6f, 2.6f, 0.5f), 1.5f),
            new Template("Rubble", Shape.Rubble, new Vector3(4f, 1.2f, 3f), 1.5f),
            new Template("Barrier", Shape.LowWall, new Vector3(3f, 0.9f, 0.6f), 1.5f),
        };

        // 외곽 농가 곁 소형 구조물 (울타리·전봇대·잡동사니)
        public static readonly Template[] FarmSmallStructures =
        {
            new Template("Fence", Shape.Fence, new Vector3(6f, 1.8f, 0.15f), 3f),
            new Template("Pole", Shape.Pole, new Vector3(0.35f, 8f, 0.35f), 2f),
            new Template("Debris", Shape.Debris, new Vector3(3f, 1.1f, 2.2f), 2f),
            new Template("Sandbag", Shape.Sandbag, new Vector3(3.2f, 1.0f, 0.9f), 1f),
        };

        // 승용차 = 길이 ≤ 5m Vehicle (③ 트렁크 규칙 "승용차마다 트렁크 1개" — PassengerCarMaxLengthM)
        public static readonly Template Car = new Template("Car", Shape.Vehicle, M3SceneSetup.VehicleSize, 1f);
        public const float PassengerCarMaxLengthM = 5f;

        public static readonly Template[] LargeVehicles =
        {
            new Template("Bus", Shape.Vehicle, new Vector3(10f, 3f, 2.6f), 1f),
            new Template("Truck", Shape.Vehicle, new Vector3(8f, 2.8f, 2.5f), 1.5f),
            new Template("TankWreck", Shape.Vehicle, new Vector3(7f, 2.4f, 3.4f), 1f),
        };

        public enum Category
        {
            Building,
            SmallStructure,
            Car,
            LargeVehicle,
        }

        // ── 영역 (위치 의도) ──
        internal enum RegionKind
        {
            Poi,        // POI 구역 사각형 (발자국 + 40m)
            Town,       // 길목 "작은 마을" (원, 방향 정렬)
            Farm,       // 외곽 외딴 농가 (원, 생성)
            Route,      // 길목 전체 (마을 근처 제외)
            Roadside,   // 도로 띠 옆 (차량)
        }

        internal readonly struct Region
        {
            public readonly string Id;
            public readonly RegionKind Kind;
            public readonly Vector2 Center;
            public readonly Vector2 Half;      // Poi: 사각형 반크기, Town·Farm: (반경, 반경)
            public readonly float YawDeg;      // Town: 거리 방향
            public readonly int Buildings, Small, Cars, Large;

            public Region(string id, RegionKind kind, Vector2 center, Vector2 half, float yawDeg, int buildings, int small, int cars, int large)
            {
                Id = id;
                Kind = kind;
                Center = center;
                Half = half;
                YawDeg = yawDeg;
                Buildings = buildings;
                Small = small;
                Cars = cars;
                Large = large;
            }
        }

        // 길목 작은 마을 6곳 — 중심·방향은 손(설계 §6.1), 안의 건물은 seed 생성. 맵 개선 군집·랜드마크에서 80m 이상 떨어진 빈 길목.
        public static readonly (string Id, Vector2 Center, float YawDeg, int Buildings, int Small, int Cars, int Large)[] Towns =
        {
            ("T1", new Vector2(-60f, -110f), 15f, 5, 5, 2, 1),
            ("T2", new Vector2(70f, 55f), -10f, 5, 5, 1, 1),
            ("T3", new Vector2(-150f, 40f), 30f, 5, 5, 2, 0),
            ("T4", new Vector2(250f, 135f), 0f, 5, 5, 1, 1),
            ("T5", new Vector2(-20f, 225f), 45f, 5, 5, 1, 0),
            ("T6", new Vector2(110f, -110f), -25f, 5, 5, 1, 0),
        };

        // POI 구역별 (건물, 소형, 승용차, 큰 차량) — 설계 §3.1 분배
        internal static readonly (string PoiId, int Buildings, int Small, int Cars, int Large)[] PoiQuotas =
        {
            ("POI-HWY", 8, 12, 5, 2),
            ("POI-REF", 6, 10, 2, 1),
            ("POI-VIL", 14, 12, 3, 1),
            ("POI-PYL", 4, 8, 1, 0),
            ("POI-RES", 6, 8, 2, 0),
        };

        public const int FarmCount = 12;
        public const int FarmSmallTotal = 30;
        public const int FarmCars = 4;
        public const int RouteSmall = 40;
        public const int RoadsideCars = 5;
        public const int RouteLarge = 3;

        // ── 계획 결과 ──
        public sealed class StructurePlan
        {
            public Piece Piece;
            public Category Category;
            public string Zone;
            public string Region;
            public string Template;

            public Footprint Footprint => new Footprint(Piece.XZ, new Vector2(Piece.Size.x, Piece.Size.z) * 0.5f, Piece.YawDeg);
            public bool IsPassengerCar => Piece.Shape == Shape.Vehicle && Mathf.Max(Piece.Size.x, Piece.Size.z) <= PassengerCarMaxLengthM;
        }

        public enum ScatterKind
        {
            Tree,
            Bush,
            Log,
        }

        public sealed class ScatterPlan
        {
            public ScatterKind Kind;
            public int Variant;
            public Vector2 XZ;
            public float YawDeg;
            public float Scale;
            public string Zone;
            public bool InGrove;
        }

        // ── 생성 입력 ──
        public sealed class SiteContext
        {
            public Func<Vector2, float> GroundY = _ => 0f;
            public Func<Vector2, float> SlopeDeg = _ => 0f;
            internal readonly FootprintIndex Obstacles = new FootprintIndex();          // 기존 콜라이더 발자국
            internal readonly FootprintIndex Points = new FootprintIndex();             // 지점 (원 = 반경 0, 최소 거리는 PointMin)
            internal readonly List<float> PointMin = new List<float>();
            internal readonly List<string> PointNames = new List<string>();
            public readonly List<(Vector2 a, Vector2 b)> PatrolSegments = new List<(Vector2, Vector2)>();
            public readonly List<(Vector2 a, Vector2 b)> SightLines = new List<(Vector2, Vector2)>();
            public Vector2? BossCenter;
            // 기존 NavMesh 경로 통로 (스폰 → 탈출·적·상자, 순찰 구간의 실제 경로 꼭짓점 선분) — 새 콜라이더가 이 선에서 PathCorridorClearM 밖이면
            // 굽기 전 경로가 그대로 남아 도달성·순찰 경로 길이가 나빠지지 않는다(설계 §7 2를 생성 단계에서 보장).
            internal readonly FootprintIndex Corridors = new FootprintIndex();

            public void AddCorridor(Vector2 a, Vector2 b)
            {
                Vector2 d = b - a;
                float length = d.magnitude;
                if (length < 1e-3f)
                    return;
                Corridors.Add(new Footprint((a + b) * 0.5f, new Vector2(length * 0.5f, 0f), Mathf.Atan2(-d.y, d.x) * Mathf.Rad2Deg));
            }

            public int CorridorCount => Corridors.Count;

            public void AddObstacle(Footprint f) => Obstacles.Add(f);

            public void AddPoint(string name, Vector2 xz, float minDistance)
            {
                Points.Add(Footprint.Circle(xz, 0f));
                PointMin.Add(minDistance);
                PointNames.Add(name);
            }

            public int ObstacleCount => Obstacles.Count;
            public int PointCount => Points.Count;

            // 데이터만으로 만든 입력 (EditMode 테스트, 씬 없이): 스폰·탈출·스위치는 Check에서 데이터로 보고, 여기는 적·순찰·상자·M4 POI 블록아웃·맵 개선 구조물·랜드마크.
            public static SiteContext FromLayoutData()
            {
                var ctx = new SiteContext();
                foreach (var point in MapPolishLayout.EnemyAndCratePoints())
                    ctx.AddPoint(point.name, point.xz, MapPolishLayout.MinDistanceFromPointsM);
                foreach (L.SquadPlan squad in L.Squads)
                    AddRoute(ctx, squad.RouteXZ);
                foreach (M7EnemyLayout.SquadPlan squad in M7EnemyLayout.Squads)
                    AddRoute(ctx, squad.RouteXZ);
                foreach (Piece piece in M4MapSetup.FixedPieces())
                    ctx.AddObstacle(new Footprint(piece.XZ, new Vector2(piece.Size.x, piece.Size.z) * 0.5f, piece.YawDeg));
                foreach (Piece piece in MapPolishLayout.Structures)
                    ctx.AddObstacle(new Footprint(piece.XZ, new Vector2(piece.Size.x, piece.Size.z) * 0.5f, piece.YawDeg));
                foreach (MapPolishLayout.LandmarkPlan landmark in MapPolishLayout.Landmarks)
                    ctx.AddObstacle(new Footprint(landmark.XZ, MapPolishPrefabs.Landmarks[landmark.Kind].FootprintXZ * 0.5f, landmark.YawDeg));
                foreach (MapPolishLayout.PropPlan prop in MapPolishLayout.Props)
                    ctx.AddObstacle(new Footprint(prop.XZ, MapPolishPrefabs.Props[prop.Kind].FootprintXZ * 0.5f, prop.YawDeg));
                ctx.SightLines.AddRange(MapPolishLayout.SniperSightLines());
                ctx.BossCenter = BossCenter();
                return ctx;
            }

            private static void AddRoute(SiteContext ctx, Vector2[] route)
            {
                if (route == null || route.Length < 2)
                    return;
                for (int i = 0; i < route.Length; i++)
                    ctx.PatrolSegments.Add((route[i], route[(i + 1) % route.Length]));
            }
        }

        // 보스 거점 중심 = M7 BOS_VIL 1번(보스) 시작점
        public static Vector2? BossCenter()
        {
            foreach (M7EnemyLayout.SquadPlan squad in M7EnemyLayout.Squads)
                if (squad.Members.Length > 0 && squad.Members[0] == M7EnemyLayout.Kind.Boss)
                    return squad.StartsXZ[0];
            return null;
        }

        // ── 검사 규칙 (생성기와 확인이 같이 쓴다) ──
        public struct Clearance
        {
            public float ObstacleGapM;     // 기존 콜라이더 발자국에서
            public float PointScale;       // 지점 최소 거리 배율 (1 = 그대로)
            public float PointExtraM;      // 지점 최소 거리에 더함 (덤불: 엄폐 지점 3m)
            public float MaxSlopeDeg;
            public float MaxGroundSpanM;   // 0 = 보지 않음
            public bool AllowPoiFootprint;
        }

        internal static Clearance BuildingRule => new Clearance { ObstacleGapM = StructureGapM, PointScale = 1f, MaxSlopeDeg = BuildingMaxSlopeDeg, MaxGroundSpanM = MaxGroundSpanM, AllowPoiFootprint = true };
        internal static Clearance StructureRule => new Clearance { ObstacleGapM = StructureGapM, PointScale = 1f, MaxSlopeDeg = StructureMaxSlopeDeg, MaxGroundSpanM = MaxGroundSpanM, AllowPoiFootprint = true };
        internal static Clearance TreeRule(float maxSlope) => new Clearance { ObstacleGapM = TreeColliderGapM, PointScale = 1f, MaxSlopeDeg = maxSlope };
        internal static Clearance BushRule(float maxSlope) => new Clearance { ObstacleGapM = 0.3f, PointScale = 1f, PointExtraM = 0f, MaxSlopeDeg = maxSlope };
        internal static Clearance LogRule => new Clearance { ObstacleGapM = TreeColliderGapM, PointScale = 1f, MaxSlopeDeg = StructureMaxSlopeDeg };

        // 발자국 하나가 규칙을 지키는가 (데이터: 경계·스폰·탈출·스위치·도로·저격 시선·보스 / 입력: 콜라이더·지점·순찰 구간 / 지형: 경사·높이 차).
        // why = 처음 걸린 이유 (보고·테스트용).
        public static bool IsClear(Footprint f, SiteContext ctx, Clearance rule, List<int> scratch, out string why)
        {
            float r = f.Radius;
            if (Mathf.Abs(f.Center.x) + r > L.BoundaryInsetM - BoundaryClearM || Mathf.Abs(f.Center.y) + r > L.BoundaryInsetM - BoundaryClearM)
                return Reject("맵 경계", out why);
            foreach (L.Spawn spawn in L.Spawns)
                if (f.Distance(spawn.PositionXZ) < MapPolishLayout.MinDistanceFromSpawnExtM)
                    return Reject($"스폰 {spawn.Id}", out why);
            foreach (L.Extraction ext in L.Extractions)
                if (f.Distance(ext.CenterXZ) < MapPolishLayout.MinDistanceFromSpawnExtM)
                    return Reject($"탈출 {ext.Id}", out why);
            if (f.Distance(L.PowerSwitchXZ) < MapPolishLayout.MinDistanceFromSwitchM)
                return Reject("전원 스위치", out why);
            foreach (L.RoadSegment road in L.Roads)
                if (f.DistanceToSegment(road.FromXZ, road.ToXZ) < road.WidthM * 0.5f + MapPolishLayout.RoadClearM)
                    return Reject("도로 띠", out why);
            if (!rule.AllowPoiFootprint && LootZoneRules.InsidePoiFootprint(f.Center, out string poi))
                return Reject($"POI {poi} 발자국 안", out why);
            if (ctx.BossCenter.HasValue && f.Distance(ctx.BossCenter.Value) < BossKeepOutM)
                return Reject("보스 거점", out why);
            foreach ((Vector2 a, Vector2 b) in ctx.SightLines)
                if (f.DistanceToSegment(a, b) < MapPolishLayout.SniperSightClearM)
                    return Reject("저격 시선", out why);
            foreach ((Vector2 a, Vector2 b) in ctx.PatrolSegments)
                if (f.DistanceToSegment(a, b) < PatrolSegmentClearM)
                    return Reject("순찰 구간", out why);

            foreach (int id in ctx.Corridors.Query(f, PathCorridorClearM, scratch))
                if (f.Distance(ctx.Corridors[id]) < PathCorridorClearM)
                    return Reject("기존 경로 통로", out why);
            foreach (int id in ctx.Obstacles.Query(f, rule.ObstacleGapM, scratch))
                if (f.Distance(ctx.Obstacles[id]) < rule.ObstacleGapM)
                    return Reject("콜라이더", out why);
            float maxPoint = MapPolishLayout.MinDistanceFromSpawnExtM + rule.PointExtraM;
            foreach (int id in ctx.Points.Query(f, maxPoint, scratch))
            {
                float min = ctx.PointMin[id] * rule.PointScale + rule.PointExtraM;
                if (f.Distance(ctx.Points[id].Center) < min)
                    return Reject(ctx.PointNames[id], out why);
            }

            if (ctx.SlopeDeg(f.Center) > rule.MaxSlopeDeg)
                return Reject("경사", out why);
            if (rule.MaxGroundSpanM > 0f)
            {
                float lo = ctx.GroundY(f.Center), hi = lo;
                for (int i = 0; i < 4; i++)
                {
                    float y = ctx.GroundY(f.Corner(i));
                    lo = Mathf.Min(lo, y);
                    hi = Mathf.Max(hi, y);
                }
                if (hi - lo > rule.MaxGroundSpanM)
                    return Reject("지면 높이 차", out why);
            }
            why = null;
            return true;
        }

        private static bool Reject(string reason, out string why)
        {
            why = reason;
            return false;
        }

        // ── 구조물 생성 (①) ──

        // 결과 순서 = 생성 순서(건물 → 차량 → 소형). 부족한 영역은 같은 종류의 예비 영역(마을·길목)으로 넘긴다.
        public static List<StructurePlan> GenerateStructures(SiteContext ctx, out string report)
        {
            var random = new System.Random(StructureSeed);
            var plans = new List<StructurePlan>();
            var placed = new FootprintIndex();          // 새 구조물끼리
            var scratch = new List<int>();
            var sb = new StringBuilder();
            var shortfall = new Dictionary<Category, int>();

            List<Region> regions = BuildRegions(ctx, random);
            foreach (Category category in new[] { Category.Building, Category.Car, Category.LargeVehicle, Category.SmallStructure })
            {
                int missing = 0;
                foreach (Region region in regions)
                {
                    int want = CountOf(region, category);
                    if (want <= 0)
                        continue;
                    int got = PlaceInRegion(ctx, region, category, want, random, plans, placed, scratch, attempts: 600 * want);
                    if (got < want)
                    {
                        missing += want - got;
                        sb.AppendLine($"  {region.Id} {category}: {got}/{want}");
                    }
                }
                // 예비: 건물 → 마을(반경 1.4배), 그 밖 → 길목 전체
                if (missing > 0)
                {
                    foreach (Region spare in SpareRegions(category, regions))
                    {
                        if (missing <= 0)
                            break;
                        int got = PlaceInRegion(ctx, spare, category, missing, random, plans, placed, scratch, attempts: 2000 * missing);
                        missing -= got;
                        sb.AppendLine($"  예비 {spare.Id} {category}: +{got}");
                    }
                }
                if (missing > 0)
                    shortfall[category] = missing;
            }

            var counts = new Dictionary<Category, int>();
            foreach (StructurePlan p in plans)
                counts[p.Category] = (counts.TryGetValue(p.Category, out int c) ? c : 0) + 1;
            string summary = $"  건물 {Get(counts, Category.Building)}/{BuildingTarget}, 소형 {Get(counts, Category.SmallStructure)}/{SmallStructureTarget}, " +
                             $"승용차 {Get(counts, Category.Car)}/{CarTarget}, 큰 차량 {Get(counts, Category.LargeVehicle)}/{LargeVehicleTarget}\n";
            foreach (KeyValuePair<Category, int> pair in shortfall)
                summary += $"  부족: {pair.Key} {pair.Value}\n";
            report = summary + ZoneSummary(plans) + sb;
            return plans;
        }

        private static int Get(Dictionary<Category, int> d, Category c) => d.TryGetValue(c, out int v) ? v : 0;

        public static string ZoneSummary(List<StructurePlan> plans)
        {
            var sb = new StringBuilder("  구역별:");
            foreach (string zone in LootZoneRules.AllZoneIds())
            {
                int b = 0, s = 0, car = 0, big = 0;
                foreach (StructurePlan p in plans)
                {
                    if (p.Zone != zone)
                        continue;
                    if (p.Category == Category.Building) b++;
                    else if (p.Category == Category.SmallStructure) s++;
                    else if (p.Category == Category.Car) car++;
                    else big++;
                }
                sb.Append($" {zone}(건물 {b}·소형 {s}·승용차 {car}·큰 차량 {big})");
            }
            return sb.AppendLine().ToString();
        }

        private static int CountOf(Region r, Category c) =>
            c == Category.Building ? r.Buildings : c == Category.SmallStructure ? r.Small : c == Category.Car ? r.Cars : r.Large;

        private static IEnumerable<Region> SpareRegions(Category category, List<Region> regions)
        {
            foreach (Region r in regions)
                if (r.Kind == RegionKind.Town)
                    yield return new Region(r.Id + "+", RegionKind.Town, r.Center, r.Half * 1.4f, r.YawDeg, 0, 0, 0, 0);
            if (category != Category.Building)
                foreach (Region r in regions)
                    if (r.Kind == RegionKind.Route)
                        yield return r;
        }

        // 영역 목록: POI 5 → 마을 6 → 외곽 농가(생성) → 길목 흩어짐 → 도로 옆
        internal static List<Region> BuildRegions(SiteContext ctx, System.Random random)
        {
            var regions = new List<Region>();
            foreach (var q in PoiQuotas)
            {
                L.Poi poi = Array.Find(L.Pois, p => p.Id == q.PoiId);
                regions.Add(new Region(poi.Id, RegionKind.Poi, poi.CenterXZ, poi.FootprintXZ * 0.5f + Vector2.one * L.ScatterMinDistanceFromPoiM,
                    0f, q.Buildings, q.Small, q.Cars, q.Large));
            }
            foreach (var t in Towns)
                regions.Add(new Region(t.Id, RegionKind.Town, t.Center, Vector2.one * TownRadiusM, t.YawDeg, t.Buildings, t.Small, t.Cars, t.Large));
            foreach (Region farm in FarmSites(ctx, random))
                regions.Add(farm);
            regions.Add(new Region("ROUTE", RegionKind.Route, Vector2.zero, Vector2.one * L.EdgeRiseStartM, 0f, 0, RouteSmall, 0, RouteLarge));
            regions.Add(new Region("ROADSIDE", RegionKind.Roadside, Vector2.zero, Vector2.zero, 0f, 0, 0, RoadsideCars, 0));
            return regions;
        }

        // 외곽 농가 자리: 외곽 띠(EdgeRiseStartM ~ +OutskirtBandM)에서 서로 FarmMinSpacingM, 스폰·탈출 60m 밖. 건물 1 + 소형 2~3 (+ 일부 승용차).
        private static List<Region> FarmSites(SiteContext ctx, System.Random random)
        {
            var sites = new List<Vector2>();
            for (int attempt = 0; attempt < 4000 && sites.Count < FarmCount * 2; attempt++)
            {
                float side = (float)random.NextDouble() * 4f;
                float along = (float)(random.NextDouble() * 2.0 - 1.0) * (L.EdgeRiseStartM + OutskirtBandM);
                float depth = L.EdgeRiseStartM + 4f + (float)random.NextDouble() * (OutskirtBandM - 8f);
                Vector2 p = side < 1f ? new Vector2(along, depth) : side < 2f ? new Vector2(along, -depth)
                    : side < 3f ? new Vector2(depth, along) : new Vector2(-depth, along);
                if (LootZoneRules.ZoneOf(p) != LootZoneRules.Outskirt)
                    continue;
                bool ok = true;
                foreach (Vector2 s in sites)
                    ok &= Vector2.Distance(s, p) >= FarmMinSpacingM;
                foreach (L.Spawn spawn in L.Spawns)
                    ok &= Vector2.Distance(spawn.PositionXZ, p) >= 60f;
                foreach (L.Extraction ext in L.Extractions)
                    ok &= Vector2.Distance(ext.CenterXZ, p) >= 60f;
                if (ok)
                    sites.Add(p);
            }
            // 앞 FarmCount곳 = 농가, 나머지 = 예비(건물이 안 서면 다음 자리)
            var regions = new List<Region>();
            int smallLeft = FarmSmallTotal, carsLeft = FarmCars;
            for (int i = 0; i < sites.Count; i++)
            {
                bool primary = i < FarmCount;
                int small = primary ? Mathf.Min(smallLeft, i % 2 == 0 ? 3 : 2) : 0;
                int cars = primary && carsLeft > 0 && i % 3 == 0 ? 1 : 0;
                smallLeft -= small;
                carsLeft -= cars;
                float yaw = Mathf.Abs(sites[i].x) > Mathf.Abs(sites[i].y) ? 90f : 0f;
                regions.Add(new Region($"FARM{i + 1:00}", RegionKind.Farm, sites[i], Vector2.one * 18f, yaw, primary ? 1 : 0, small, cars, 0));
            }
            return regions;
        }

        private static int PlaceInRegion(SiteContext ctx, Region region, Category category, int want, System.Random random,
            List<StructurePlan> plans, FootprintIndex placed, List<int> scratch, int attempts)
        {
            int got = 0;
            Clearance rule = category == Category.Building ? BuildingRule : StructureRule;
            Template[] pool = category == Category.Building ? Buildings
                : category == Category.Car ? new[] { Car }
                : category == Category.LargeVehicle ? LargeVehicles
                : region.Kind == RegionKind.Farm ? FarmSmallStructures : SmallStructures;
            for (int attempt = 0; attempt < attempts && got < want; attempt++)
            {
                Template t = Pick(pool, random);
                if (!Sample(region, random, out Vector2 xz, out float baseYaw))
                    continue;
                bool aligned = region.Kind == RegionKind.Town || region.Kind == RegionKind.Farm || region.Kind == RegionKind.Roadside;
                float yaw = Mathf.Repeat(baseYaw + (float)(random.NextDouble() * 2.0 - 1.0) * (aligned ? 8f : 180f), 360f);
                if (region.Kind == RegionKind.Town || region.Kind == RegionKind.Farm)
                    yaw = Mathf.Repeat(yaw + 90f * random.Next(4), 360f);   // 마을 격자: 거리 방향 ± 직각
                string zone = LootZoneRules.ZoneOf(xz);
                if (!ZoneFits(region, zone))
                    continue;
                if (region.Kind == RegionKind.Route)
                {
                    bool nearTown = false;
                    foreach (var town in Towns)
                        nearTown |= Vector2.Distance(town.Center, xz) < TownKeepOffM;
                    if (nearTown)
                        continue;
                }
                var piece = new Piece(t.Shape, Round(xz.x), Round(xz.y), t.Size, Round(yaw));
                var f = new Footprint(piece.XZ, new Vector2(t.Size.x, t.Size.z) * 0.5f, piece.YawDeg);
                if (!IsClear(f, ctx, rule, scratch, out _))
                    continue;
                // 새 구조물끼리 StructureGapM, 길목 흩어진 구조물은 중심 간격 RouteSmallMinSpacingM(M4 흩어진 잔해와 같은 15m)
                float centerSpacing = region.Kind == RegionKind.Route ? RouteSmallMinSpacingM : 0f;
                bool clash = false;
                foreach (int id in placed.Query(f, Mathf.Max(StructureGapM, centerSpacing), scratch))
                    if (f.Distance(placed[id]) < StructureGapM || Vector2.Distance(f.Center, placed[id].Center) < centerSpacing)
                    {
                        clash = true;
                        break;
                    }
                if (clash)
                    continue;
                placed.Add(f);
                plans.Add(new StructurePlan { Piece = piece, Category = category, Zone = zone, Region = region.Id, Template = t.Name });
                got++;
            }
            return got;
        }

        private static bool ZoneFits(Region region, string zone)
        {
            switch (region.Kind)
            {
                case RegionKind.Poi: return zone == region.Id;
                case RegionKind.Town:
                case RegionKind.Route: return zone == LootZoneRules.Route;
                case RegionKind.Farm: return zone == LootZoneRules.Outskirt;
                default: return true;   // 도로 옆: 어디든 (구역은 위치로 정해진다)
            }
        }

        private static float Round(float v) => Mathf.Round(v * 10f) / 10f;   // 씬 값이 깔끔하게 (0.1m·0.1°)

        private static bool Sample(Region region, System.Random random, out Vector2 xz, out float baseYaw)
        {
            baseYaw = region.YawDeg;
            switch (region.Kind)
            {
                case RegionKind.Poi:
                case RegionKind.Route:
                    xz = region.Center + new Vector2((float)(random.NextDouble() * 2.0 - 1.0) * region.Half.x, (float)(random.NextDouble() * 2.0 - 1.0) * region.Half.y);
                    baseYaw = 0f;
                    return true;
                case RegionKind.Town:
                case RegionKind.Farm:
                {
                    double angle = random.NextDouble() * Math.PI * 2.0;
                    float radius = region.Half.x * Mathf.Sqrt((float)random.NextDouble());
                    xz = region.Center + new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * radius;
                    return true;
                }
                case RegionKind.Roadside:
                {
                    L.RoadSegment road = L.Roads[random.Next(L.Roads.Length)];
                    float t = (float)random.NextDouble();
                    Vector2 along = (road.ToXZ - road.FromXZ).normalized;
                    var normal = new Vector2(-along.y, along.x);
                    float offset = road.WidthM * 0.5f + MapPolishLayout.RoadClearM + 2.5f + (float)random.NextDouble() * 3f;
                    xz = Vector2.Lerp(road.FromXZ, road.ToXZ, t) + normal * (random.Next(2) == 0 ? offset : -offset);
                    baseYaw = Mathf.Atan2(along.x, along.y) * Mathf.Rad2Deg - 90f;   // 차 길이(로컬 x)를 도로 방향으로
                    return true;
                }
            }
            xz = default;
            return false;
        }

        private static Template Pick(Template[] pool, System.Random random)
        {
            float total = 0f;
            foreach (Template t in pool)
                total += t.Weight;
            double roll = random.NextDouble() * total;
            foreach (Template t in pool)
            {
                roll -= t.Weight;
                if (roll < 0)
                    return t;
            }
            return pool[pool.Length - 1];
        }

        // ── 산포 생성 (②) ──

        public readonly struct ScatterSettings
        {
            public readonly int TreeCount, BushCount, LogCount;
            public readonly float TreeMinSpacingM, GroveNoiseScaleM, GroveThreshold, ScatterShare, TreeMaxSlopeDeg;
            public readonly Vector2 TreeScaleRange;
            public readonly float[] TreeTrunkRadiusM;   // 변형별 줄기 반지름 (배율 1)
            public readonly float LogLengthM;

            public ScatterSettings(int treeCount, int bushCount, int logCount, float treeMinSpacingM, float groveNoiseScaleM, float groveThreshold,
                float scatterShare, float treeMaxSlopeDeg, Vector2 treeScaleRange, float[] treeTrunkRadiusM, float logLengthM)
            {
                TreeCount = treeCount;
                BushCount = bushCount;
                LogCount = logCount;
                TreeMinSpacingM = treeMinSpacingM;
                GroveNoiseScaleM = groveNoiseScaleM;
                GroveThreshold = groveThreshold;
                ScatterShare = scatterShare;
                TreeMaxSlopeDeg = treeMaxSlopeDeg;
                TreeScaleRange = treeScaleRange;
                TreeTrunkRadiusM = treeTrunkRadiusM;
                LogLengthM = logLengthM;
            }
        }

        // 군락 노이즈 (0~1). seed로 오프셋.
        public static float GroveNoise(Vector2 xz, float scaleM, Vector2 offset) =>
            Mathf.PerlinNoise(offset.x + xz.x / Mathf.Max(1f, scaleM), offset.y + xz.y / Mathf.Max(1f, scaleM));

        public static Vector2 NoiseOffset(int seed)
        {
            var random = new System.Random(seed);
            return new Vector2((float)(random.NextDouble() * 1000.0), (float)(random.NextDouble() * 1000.0));
        }

        // 나무 구역 목표 (길목·외곽·POI)
        public static (int route, int outskirt, int poi) TreeQuotas(int total)
        {
            int route = Mathf.RoundToInt(total * TreeRouteShare);
            int outskirt = Mathf.RoundToInt(total * TreeOutskirtShare);
            return (route, outskirt, Mathf.Max(0, total - route - outskirt));
        }

        // 나무(군락 마스크 × 포아송 디스크) → 통나무(군락 안) → 덤불(군락 가장자리·건물 둘레). structures = ① 결과(새 구조물 발자국은 ctx에 이미 장애물로 넣는다).
        public static List<ScatterPlan> GenerateScatter(SiteContext ctx, ScatterSettings s, IReadOnlyList<StructurePlan> structures, out string report)
        {
            var result = new List<ScatterPlan>();
            var scratch = new List<int>();
            var random = new System.Random(TreeSeed);
            Vector2 offset = NoiseOffset(TreeSeed);
            float limit = L.BoundaryInsetM - BoundaryClearM;
            List<Vector2> candidates = PoissonDisk.Sample(new Vector2(-limit, -limit), new Vector2(limit, limit), s.TreeMinSpacingM, random);
            Shuffle(candidates, random);

            (int route, int outskirt, int poi) quota = TreeQuotas(s.TreeCount);
            // 버킷: (구역 종류, 군락 여부) → 남은 수
            int Want(string kind, bool grove)
            {
                int zoneTotal = kind == "R" ? quota.route : kind == "O" ? quota.outskirt : quota.poi;
                int groveCount = Mathf.RoundToInt(zoneTotal * (1f - s.ScatterShare));
                return grove ? groveCount : zoneTotal - groveCount;
            }
            var remaining = new Dictionary<string, int>();
            foreach (string kind in new[] { "R", "O", "P" })
            {
                remaining[kind + "G"] = Want(kind, true);
                remaining[kind + "S"] = Want(kind, false);
            }

            var trees = new FootprintIndex();
            var accepted = new List<(Vector2 xz, string kind, bool grove)>();
            var deferred = new List<(Vector2 xz, string kind, bool grove)>();
            int rejected = 0;
            foreach (Vector2 c in candidates)
            {
                string zone = LootZoneRules.ZoneOf(c);
                string kind = zone == LootZoneRules.Route ? "R" : zone == LootZoneRules.Outskirt ? "O" : Array.IndexOf(TreePoiIds, zone) >= 0 ? "P" : null;
                if (kind == null)
                    continue;
                if (kind == "P" && LootZoneRules.InsidePoiFootprint(c, out _))
                    continue;
                bool grove = GroveNoise(c, s.GroveNoiseScaleM, offset) >= s.GroveThreshold;
                string bucket = kind + (grove ? "G" : "S");
                if (remaining[bucket] <= 0)
                {
                    deferred.Add((c, kind, grove));
                    continue;
                }
                if (!TreeFits(c, ctx, s, scratch))
                {
                    rejected++;
                    continue;
                }
                remaining[bucket]--;
                accepted.Add((c, kind, grove));
            }
            // 군락/흩어짐 한쪽이 모자라면 같은 구역의 다른 쪽 후보로 채운다
            foreach (var d in deferred)
            {
                string other = d.kind + (d.grove ? "S" : "G");
                if (remaining[other] <= 0 || !TreeFits(d.xz, ctx, s, scratch))
                    continue;
                remaining[other]--;
                accepted.Add(d);
            }

            foreach (var a in accepted)
            {
                int variant = random.Next(s.TreeTrunkRadiusM.Length);
                float scale = Mathf.Lerp(s.TreeScaleRange.x, s.TreeScaleRange.y, (float)random.NextDouble());
                float yaw = Round((float)(random.NextDouble() * 360.0));
                result.Add(new ScatterPlan { Kind = ScatterKind.Tree, Variant = variant, XZ = a.xz, YawDeg = yaw, Scale = Mathf.Round(scale * 100f) / 100f, Zone = LootZoneRules.ZoneOf(a.xz), InGrove = a.grove });
                trees.Add(Footprint.Circle(a.xz, s.TreeTrunkRadiusM[variant] * scale));
            }
            int treeShort = 0;
            foreach (int v in remaining.Values)
                treeShort += Mathf.Max(0, v);

            // 통나무: 군락 나무 곁 3~6m, 눕힌 원통 발자국
            var groveTrees = result.FindAll(p => p.Kind == ScatterKind.Tree && p.InGrove);
            var logs = new FootprintIndex();
            int logCount = 0;
            for (int attempt = 0; attempt < s.LogCount * 60 && logCount < s.LogCount && groveTrees.Count > 0; attempt++)
            {
                ScatterPlan near = groveTrees[random.Next(groveTrees.Count)];
                Vector2 xz = near.XZ + RandomDir(random) * (3f + (float)random.NextDouble() * 3f);
                float yaw = Round((float)(random.NextDouble() * 360.0));
                var f = new Footprint(xz, new Vector2(s.LogLengthM * 0.5f, 0.4f), yaw);
                if (!IsClear(f, ctx, LogRule, scratch, out _) || Clash(f, trees, 0.8f, scratch) || Clash(f, logs, 2f, scratch))
                    continue;
                logs.Add(f);
                result.Add(new ScatterPlan { Kind = ScatterKind.Log, Variant = 0, XZ = xz, YawDeg = yaw, Scale = 1f, Zone = LootZoneRules.ZoneOf(xz), InGrove = true });
                logCount++;
            }

            // 덤불: 70% 군락 나무 곁 2~6m, 30% 새 건물 둘레 1~4m
            var buildings = new List<StructurePlan>();
            if (structures != null)
                foreach (StructurePlan p in structures)
                    if (p.Category == Category.Building)
                        buildings.Add(p);
            var bushes = new FootprintIndex();
            int bushCount = 0;
            for (int attempt = 0; attempt < s.BushCount * 60 && bushCount < s.BushCount; attempt++)
            {
                Vector2 xz;
                bool nearBuilding = buildings.Count > 0 && random.NextDouble() < 0.3;
                if (nearBuilding)
                {
                    Footprint b = buildings[random.Next(buildings.Count)].Footprint;
                    Vector2 local = new Vector2((float)(random.NextDouble() * 2.0 - 1.0) * (b.Half.x + 2.5f), (b.Half.y + 1f + (float)random.NextDouble() * 3f) * (random.Next(2) == 0 ? 1f : -1f));
                    if (random.Next(2) == 0)
                        local = new Vector2(Mathf.Sign(local.y) * (b.Half.x + 1f + (float)random.NextDouble() * 3f), (float)(random.NextDouble() * 2.0 - 1.0) * (b.Half.y + 2.5f));
                    xz = b.ToWorld(local);
                }
                else
                {
                    List<ScatterPlan> pool = groveTrees.Count > 0 ? groveTrees : result;
                    if (pool.Count == 0)
                        break;
                    xz = pool[random.Next(pool.Count)].XZ + RandomDir(random) * (2f + (float)random.NextDouble() * 4f);
                }
                var f = Footprint.Circle(xz, 0.6f);
                var rule = BushRule(s.TreeMaxSlopeDeg);
                if (!IsClear(f, ctx, rule, scratch, out _) || !CoverClear(xz, ctx, scratch) || Clash(f, trees, 0.5f, scratch)
                    || Clash(f, logs, 0.5f, scratch) || Clash(f, bushes, 0.8f, scratch))
                    continue;
                bushes.Add(f);
                result.Add(new ScatterPlan
                {
                    Kind = ScatterKind.Bush, Variant = random.Next(2), XZ = xz, YawDeg = Round((float)(random.NextDouble() * 360.0)),
                    Scale = Mathf.Round(Mathf.Lerp(0.8f, 1.15f, (float)random.NextDouble()) * 100f) / 100f, Zone = LootZoneRules.ZoneOf(xz), InGrove = !nearBuilding,
                });
                bushCount++;
            }

            var zones = new StringBuilder("  나무 구역별:");
            foreach (string zone in LootZoneRules.AllZoneIds())
            {
                int n = result.FindAll(p => p.Kind == ScatterKind.Tree && p.Zone == zone).Count;
                if (n > 0)
                    zones.Append($" {zone} {n}");
            }
            int groveTotal = result.FindAll(p => p.Kind == ScatterKind.Tree && p.InGrove).Count;
            report = $"  후보 {candidates.Count}(간격 {s.TreeMinSpacingM}m), 나무 {accepted.Count}/{s.TreeCount}(군락 {groveTotal}, 부족 {treeShort}, 규칙 탈락 {rejected}), " +
                     $"통나무 {logCount}/{s.LogCount}, 덤불 {bushCount}/{s.BushCount}\n{zones}\n";
            return result;
        }

        private static bool TreeFits(Vector2 c, SiteContext ctx, ScatterSettings s, List<int> scratch)
        {
            float trunk = s.TreeTrunkRadiusM.Length > 0 ? Max(s.TreeTrunkRadiusM) * s.TreeScaleRange.y : 0.5f;
            return IsClear(Footprint.Circle(c, trunk), ctx, TreeRule(s.TreeMaxSlopeDeg), scratch, out _);
        }

        // 덤불은 엄폐 지점에서 BushCoverClearM (지점 이름이 "엄폐"로 시작)
        private static bool CoverClear(Vector2 xz, SiteContext ctx, List<int> scratch)
        {
            foreach (int id in ctx.Points.Query(Footprint.Circle(xz, 0f), BushCoverClearM, scratch))
                if (ctx.PointNames[id].StartsWith("엄폐") && Vector2.Distance(ctx.Points[id].Center, xz) < BushCoverClearM)
                    return false;
            return true;
        }

        private static bool Clash(Footprint f, FootprintIndex index, float gap, List<int> scratch)
        {
            foreach (int id in index.Query(f, gap, scratch))
                if (f.Distance(index[id]) < gap)
                    return true;
            return false;
        }

        private static float Max(float[] values)
        {
            float m = 0f;
            foreach (float v in values)
                m = Mathf.Max(m, v);
            return m;
        }

        private static Vector2 RandomDir(System.Random random)
        {
            double a = random.NextDouble() * Math.PI * 2.0;
            return new Vector2((float)Math.Cos(a), (float)Math.Sin(a));
        }

        internal static void Shuffle<T>(List<T> list, System.Random random)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        // ── 확인 (데이터 부분, §7 1) — 생성 결과를 생성기와 독립적으로 다시 본다(테스트가 항목을 주입한다) ──

        public static bool CheckStructures(IReadOnlyList<StructurePlan> plans, SiteContext ctx, out string report)
        {
            var sb = new StringBuilder();
            var scratch = new List<int>();
            bool ok = true;
            for (int i = 0; i < plans.Count; i++)
            {
                StructurePlan p = plans[i];
                Footprint f = p.Footprint;
                Clearance rule = p.Category == Category.Building ? BuildingRule : StructureRule;
                if (!IsClear(f, ctx, rule, scratch, out string why))
                {
                    sb.AppendLine($"  {p.Template}#{i + 1} {p.Piece.XZ}: {why}");
                    ok = false;
                }
                if (p.Zone != LootZoneRules.ZoneOf(p.Piece.XZ))
                {
                    sb.AppendLine($"  {p.Template}#{i + 1}: 구역 표시 {p.Zone} ≠ {LootZoneRules.ZoneOf(p.Piece.XZ)}");
                    ok = false;
                }
                for (int j = i + 1; j < plans.Count; j++)
                    if (f.Distance(plans[j].Footprint) < StructureGapM - 0.01f)
                    {
                        sb.AppendLine($"  {p.Template}#{i + 1} ↔ {plans[j].Template}#{j + 1} 간격 {f.Distance(plans[j].Footprint):0.0}m < {StructureGapM}m");
                        ok = false;
                    }
            }
            report = sb.ToString();
            return ok;
        }

        // 생성 결과 → 블록아웃 조각 (그룹별)
        public static Piece[] PiecesOf(IReadOnlyList<StructurePlan> plans, bool buildings)
        {
            var list = new List<Piece>();
            foreach (StructurePlan p in plans)
                if ((p.Category == Category.Building) == buildings)
                    list.Add(p.Piece);
            return list.ToArray();
        }
    }

    // Bridson 포아송 디스크 (2D, 최소 간격 r). 같은 random 상태면 같은 결과.
    public static class PoissonDisk
    {
        public static List<Vector2> Sample(Vector2 min, Vector2 max, float r, System.Random random, int k = 20)
        {
            var points = new List<Vector2>();
            if (r <= 0f)
                return points;
            float cell = r / Mathf.Sqrt(2f);
            int w = Mathf.CeilToInt((max.x - min.x) / cell), h = Mathf.CeilToInt((max.y - min.y) / cell);
            var grid = new int[w * h];
            for (int i = 0; i < grid.Length; i++)
                grid[i] = -1;
            var active = new List<int>();

            int CellX(Vector2 p) => Mathf.Clamp((int)((p.x - min.x) / cell), 0, w - 1);
            int CellY(Vector2 p) => Mathf.Clamp((int)((p.y - min.y) / cell), 0, h - 1);
            void Add(Vector2 p)
            {
                points.Add(p);
                active.Add(points.Count - 1);
                grid[CellY(p) * w + CellX(p)] = points.Count - 1;
            }
            bool Far(Vector2 p)
            {
                int cx = CellX(p), cy = CellY(p);
                for (int y = Mathf.Max(0, cy - 2); y <= Mathf.Min(h - 1, cy + 2); y++)
                for (int x = Mathf.Max(0, cx - 2); x <= Mathf.Min(w - 1, cx + 2); x++)
                {
                    int id = grid[y * w + x];
                    if (id >= 0 && (points[id] - p).sqrMagnitude < r * r)
                        return false;
                }
                return true;
            }

            Add(new Vector2(Mathf.Lerp(min.x, max.x, (float)random.NextDouble()), Mathf.Lerp(min.y, max.y, (float)random.NextDouble())));
            while (active.Count > 0)
            {
                int ai = random.Next(active.Count);
                Vector2 origin = points[active[ai]];
                bool found = false;
                for (int i = 0; i < k; i++)
                {
                    double angle = random.NextDouble() * Math.PI * 2.0;
                    float dist = r * (1f + (float)random.NextDouble());
                    var p = origin + new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * dist;
                    if (p.x < min.x || p.x >= max.x || p.y < min.y || p.y >= max.y || !Far(p))
                        continue;
                    Add(p);
                    found = true;
                    break;
                }
                if (!found)
                {
                    active[ai] = active[active.Count - 1];
                    active.RemoveAt(active.Count - 1);
                }
            }
            return points;
        }
    }
}
