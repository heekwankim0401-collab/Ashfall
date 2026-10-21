using UnityEngine;

namespace Ashfall.EditorTools
{
    // Crimson Basin 블록아웃 레벨 데이터 (§8.1·§8.2, M4 설계 §2). 씬 생성 메뉴(M4MapSetup·M4EnemySetup)가 읽는다.
    // 게임 수치가 아니라 레벨 배치 데이터다 (게임 수치 — 탈출 대기·반경·제한 시간 — 는 RaidConfigSO).
    // 좌표: 월드 XZ, 원점 = 맵 중앙, +z = 북. 높이는 지형에서 샘플한다 (terrain.SampleHeight).
    // TODO(GDD-?) Q1·Q2·Q3·Q5·Q7·Q12·Q17·Q20: GDD에 좌표·배치가 없어 설계 §2.3 초안 그대로 정한 임시값.
    public static class CrimsonBasinLayout
    {
        // ── 지형 (설계 §2.2) ──────────────────────────────────

        public static readonly Vector3 TerrainSize = new Vector3(1000f, 120f, 1000f);
        public const int HeightmapResolution = 513;
        public static readonly Vector3 TerrainPosition = new Vector3(-500f, 0f, -500f);   // 원점이 맵 중앙

        public const float BasinFloorM = 15f;          // 분지 바닥 높이
        public const float EdgeRiseStartM = 380f;      // max(|x|,|z|)가 이 값부터
        public const float EdgeRiseEndM = 500f;        // 이 값까지 smoothstep 상승
        public const float EdgeRiseM = 45f;
        public const float NoiseAmplitudeM = 4f;       // Perlin 기복 ±
        public const float NoiseWavelengthM = 120f;
        public const int NoiseSeed = 4004;             // 결정적 생성 (재실행 시 같은 높이)

        public static readonly Vector2 HillCenterXZ = new Vector2(230f, 300f);      // 송전탑 언덕
        public const float HillHeightM = 35f;
        public const float HillRadiusM = 90f;                                        // 가우시안 반경
        public static readonly Vector2 ReservoirCenterXZ = new Vector2(-200f, 330f); // 말라붙은 저수지 웅덩이
        public const float ReservoirDepthM = 8f;
        public const float ReservoirRadiusM = 70f;

        public const float PadMaxSlopeDeg = 5f;        // 평탄 패드 (POI·탈출·스폰·도로)

        // ── 맵 경계 (Q20: 보이지 않는 벽, NavMesh 굽기 제외) ─────────

        public const float BoundaryInsetM = 495f;      // 중심에서 벽 안쪽 면까지
        public const float BoundaryHeightM = 150f;
        public const float BoundaryThicknessM = 2f;

        // ── 구역 (§8.1 POI 5곳) ─────────────────────────────────

        public readonly struct Poi
        {
            public readonly string Id;
            public readonly string DisplayName;
            public readonly Vector2 CenterXZ;
            public readonly Vector2 FootprintXZ;   // 블록아웃·평탄 패드 범위 (가로 x, 세로 z)

            public Poi(string id, string displayName, Vector2 centerXZ, Vector2 footprintXZ)
            {
                Id = id;
                DisplayName = displayName;
                CenterXZ = centerXZ;
                FootprintXZ = footprintXZ;
            }
        }

        public static readonly Poi Highway = new Poi("POI-HWY", "붕괴된 고속도로", new Vector2(-60f, -300f), new Vector2(320f, 80f));
        public static readonly Poi Refinery = new Poi("POI-REF", "방사능 정유소", new Vector2(300f, -60f), new Vector2(150f, 120f));
        public static readonly Poi Village = new Poi("POI-VIL", "매몰된 마을 외곽", new Vector2(-300f, 80f), new Vector2(120f, 120f));
        public static readonly Poi Pylons = new Poi("POI-PYL", "송전탑 언덕", new Vector2(230f, 300f), new Vector2(100f, 100f));
        public static readonly Poi Reservoir = new Poi("POI-RES", "말라붙은 저수지", new Vector2(-200f, 330f), new Vector2(160f, 140f));

        public static readonly Poi[] Pois = { Highway, Refinery, Village, Pylons, Reservoir };

        // ── 탈출 지점 (§8.2 EXT-01~04) ─────────────────────────
        // 반경·조건은 RaidConfigSO. 여기는 위치와 블록아웃 패드 크기만.

        public readonly struct Extraction
        {
            public readonly string Id;           // RaidConfigSO.extractions[].id, ExtractionZone.id
            public readonly string DisplayName;
            public readonly Vector2 CenterXZ;
            public readonly Vector2 PadSizeXZ;

            public Extraction(string id, string displayName, Vector2 centerXZ, Vector2 padSizeXZ)
            {
                Id = id;
                DisplayName = displayName;
                CenterXZ = centerXZ;
                PadSizeXZ = padSizeXZ;
            }
        }

        public static readonly Extraction[] Extractions =
        {
            new Extraction("EXT-01", "남쪽 도로", new Vector2(0f, -470f), new Vector2(30f, 20f)),
            new Extraction("EXT-02", "열차 신호소", new Vector2(470f, -250f), new Vector2(30f, 30f)),
            new Extraction("EXT-03", "헬기 착륙장", new Vector2(60f, 460f), new Vector2(20f, 20f)),
            new Extraction("EXT-04", "하수 배출구", new Vector2(-470f, 200f), new Vector2(20f, 20f)),
        };

        // ── 플레이어 스폰 (Q2: 고정 3곳 중 무작위) ──────────────────
        // 시선은 맵 중앙을 향한다. 활성 탈출구 세트(Q3)는 RaidConfigSO.spawnRules.

        public readonly struct Spawn
        {
            public readonly string Id;           // PlayerSpawnPoint.id, SpawnRule.spawnId
            public readonly Vector2 PositionXZ;

            public Spawn(string id, Vector2 positionXZ)
            {
                Id = id;
                PositionXZ = positionXZ;
            }

            // 맵 중앙을 보는 yaw (도, +z = 0, 시계 방향)
            public float YawDeg => Mathf.Atan2(-PositionXZ.x, -PositionXZ.y) * Mathf.Rad2Deg;
        }

        public static readonly Spawn[] Spawns =
        {
            new Spawn("SP-W", new Vector2(-460f, -150f)),
            new Spawn("SP-E", new Vector2(460f, 120f)),
            new Spawn("SP-N", new Vector2(-80f, 470f)),
        };

        public const float SpawnPadSizeM = 12f;
        // 모든 적 시작 위치는 모든 스폰에서 이 거리 이상 (설계 §1.7, 시야 60m·소총 소음 120m 밖)
        public const float MinEnemyDistanceFromSpawnM = 150f;

        // ── EXT-02 전원 스위치 (Q5: 송전탑 언덕, 레이드 끝까지 유지) ────────

        public const string PowerSwitchId = "SW-PWR";
        public const string PowerSwitchDisplayName = "전원 스위치";
        public static readonly Vector2 PowerSwitchXZ = new Vector2(245f, 285f);
        public const float PowerSwitchYawDeg = 180f;

        // ── 도로 띠 (평탄화·블록아웃) ──────────────────────────────

        public readonly struct RoadSegment
        {
            public readonly Vector2 FromXZ;
            public readonly Vector2 ToXZ;
            public readonly float WidthM;

            public RoadSegment(Vector2 fromXZ, Vector2 toXZ, float widthM)
            {
                FromXZ = fromXZ;
                ToXZ = toXZ;
                WidthM = widthM;
            }
        }

        public static readonly RoadSegment[] Roads =
        {
            new RoadSegment(new Vector2(-220f, -300f), new Vector2(100f, -300f), 14f),   // 고가 아래 동서 도로
            new RoadSegment(new Vector2(0f, -300f), new Vector2(0f, -470f), 10f),        // 고속도로 → EXT-01
            new RoadSegment(new Vector2(470f, -420f), new Vector2(470f, -80f), 8f),      // 동쪽 철로 둑 (EXT-02)
        };

        // ── 흩어진 잔해·차량 (POI 사이 길목, 설계 §2.3) ─────────────────

        public const int ScatterDebrisCount = 60;
        public const int ScatterDebrisSeed = 4041;
        public const float ScatterMinDistanceFromPoiM = 40f;   // POI 패드 가장자리에서

        // ── 루트 상자 (Q7·Q17) ──────────────────────────────────
        // 일반 상자는 상자 루트 테이블을 굴린다. 신호탄 상자 2곳은 고정 내용물(신호탄 1)을 가진다.

        public readonly struct Crate
        {
            public readonly string PoiId;
            public readonly Vector2 PositionXZ;

            public Crate(string poiId, Vector2 positionXZ)
            {
                PoiId = poiId;
                PositionXZ = positionXZ;
            }
        }

        public static readonly Crate[] LootCrates =
        {
            new Crate("POI-HWY", new Vector2(-130f, -290f)), new Crate("POI-HWY", new Vector2(-40f, -315f)),
            new Crate("POI-HWY", new Vector2(40f, -290f)), new Crate("POI-HWY", new Vector2(-10f, -330f)),
            new Crate("POI-REF", new Vector2(260f, -90f)), new Crate("POI-REF", new Vector2(320f, -100f)),
            new Crate("POI-REF", new Vector2(340f, -20f)), new Crate("POI-REF", new Vector2(270f, -20f)),
            new Crate("POI-VIL", new Vector2(-330f, 50f)), new Crate("POI-VIL", new Vector2(-270f, 60f)),
            new Crate("POI-VIL", new Vector2(-320f, 115f)), new Crate("POI-VIL", new Vector2(-280f, 110f)),
            new Crate("POI-PYL", new Vector2(210f, 280f)), new Crate("POI-PYL", new Vector2(255f, 320f)),
            new Crate("POI-PYL", new Vector2(215f, 325f)),
            new Crate("POI-RES", new Vector2(-250f, 300f)), new Crate("POI-RES", new Vector2(-150f, 310f)),
            new Crate("POI-RES", new Vector2(-230f, 360f)), new Crate("POI-RES", new Vector2(-180f, 280f)),
        };

        public static readonly Crate[] FlareCrates =
        {
            new Crate("POI-REF", new Vector2(300f, -40f)),
            new Crate("POI-RES", new Vector2(-210f, 300f)),
        };

        // ── 적 (Q12: 20명 고정 배치 = SCV 12 + RDR 8, M3 구성 그대로) ──────────

        public enum EnemyKind
        {
            Scavenger,   // AI-SCV (M3 데이터 ScvPath)
            Raider,      // AI-RDR (M3 데이터 RdrPath)
        }

        // 분대 하나. 경로가 있으면 리더가 첫 지점에서 시작해 순찰(M3SceneSetup.CreateSquad 방식, 대원은 대형 슬롯),
        // 경로가 없으면 Spots에 정지 배치(M3 RDR 방식).
        public readonly struct SquadPlan
        {
            public readonly string Name;
            public readonly string PoiId;
            public readonly EnemyKind Kind;
            public readonly int SquadId;
            public readonly int[] Loadouts;       // EnemyArchetypeSO 장비 인덱스 (SCV: 0 권총, 1 SMG / RDR: 0 소총)
            public readonly Vector2[] RouteXZ;    // null = 순찰 없음
            public readonly Vector2[] SpotsXZ;    // RouteXZ가 null일 때 대원별 시작 위치 (Loadouts와 같은 수)
            public readonly float YawDeg;         // 정지 배치의 시선

            public SquadPlan(string name, string poiId, EnemyKind kind, int squadId, int[] loadouts,
                Vector2[] routeXZ, Vector2[] spotsXZ, float yawDeg)
            {
                Name = name;
                PoiId = poiId;
                Kind = kind;
                SquadId = squadId;
                Loadouts = loadouts;
                RouteXZ = routeXZ;
                SpotsXZ = spotsXZ;
                YawDeg = yawDeg;
            }

            public int Count => Loadouts.Length;
        }

        public static readonly SquadPlan[] Squads =
        {
            // POI-HWY: SCV 3 (도로 따라 순찰) + RDR 2 (차량 무더기)
            new SquadPlan("HWY_S", "POI-HWY", EnemyKind.Scavenger, 1, new[] { 0, 0, 1 },
                new[] { new Vector2(-140f, -315f), new Vector2(20f, -315f), new Vector2(20f, -355f), new Vector2(-140f, -355f) },
                null, 0f),
            new SquadPlan("HWY_R", "POI-HWY", EnemyKind.Raider, 2, new[] { 0, 0 },
                null, new[] { new Vector2(-92f, -285f), new Vector2(-88f, -285f) }, 180f),

            // POI-REF: RDR 2 + RDR 2
            new SquadPlan("REF_R1", "POI-REF", EnemyKind.Raider, 3, new[] { 0, 0 },
                null, new[] { new Vector2(280f, -90f), new Vector2(284f, -90f) }, 270f),
            new SquadPlan("REF_R2", "POI-REF", EnemyKind.Raider, 4, new[] { 0, 0 },
                null, new[] { new Vector2(330f, -25f), new Vector2(334f, -25f) }, 315f),

            // POI-VIL: SCV 3 + SCV 2
            new SquadPlan("VIL_S1", "POI-VIL", EnemyKind.Scavenger, 5, new[] { 0, 0, 1 },
                new[] { new Vector2(-345f, 40f), new Vector2(-345f, 120f), new Vector2(-255f, 120f), new Vector2(-255f, 40f) },
                null, 0f),
            new SquadPlan("VIL_S2", "POI-VIL", EnemyKind.Scavenger, 6, new[] { 0, 1 },
                new[] { new Vector2(-300f, 25f), new Vector2(-235f, 25f), new Vector2(-235f, 85f), new Vector2(-300f, 85f) },
                null, 0f),

            // POI-PYL: RDR 2 (언덕 위 변전 설비)
            new SquadPlan("PYL_R", "POI-PYL", EnemyKind.Raider, 7, new[] { 0, 0 },
                null, new[] { new Vector2(220f, 290f), new Vector2(224f, 290f) }, 225f),

            // POI-RES: SCV 2 + SCV 2 (SP-N에서 150m 밖이 되게 웅덩이 남서쪽)
            new SquadPlan("RES_S1", "POI-RES", EnemyKind.Scavenger, 8, new[] { 0, 1 },
                new[] { new Vector2(-260f, 300f), new Vector2(-200f, 280f), new Vector2(-140f, 300f), new Vector2(-200f, 320f) },
                null, 0f),
            new SquadPlan("RES_S2", "POI-RES", EnemyKind.Scavenger, 9, new[] { 0, 1 },
                new[] { new Vector2(-280f, 330f), new Vector2(-250f, 380f), new Vector2(-220f, 360f), new Vector2(-260f, 310f) },
                null, 0f),
        };

        public const int ExpectedEnemyCount = 20;
        public const int ExpectedScavengerCount = 12;
        public const int ExpectedRaiderCount = 8;
    }
}
