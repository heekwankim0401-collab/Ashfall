using System;
using System.Collections.Generic;
using Ashfall.AI;
using Ashfall.Core;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // M3 테스트용 데이터·가짜 객체 (M3 설계 §4, §7 "1. 계약").
    // 값은 GDD 표(§5.2·§7)와 M3 설계 §4의 임시값(TODO(GDD-?))을 그대로 넣는다. 에셋 값이 바뀌어도 규칙 검증은 표 기준.
    // TestData.cs는 고치지 않는다. M3 1단계 이후 추가 도우미는 각자 새 파일에.
    internal static class M3TestData
    {
        // ── 무기 (§5.2 + M3 설계 §4.3) ─────────────────────────

        // WPN-PST-01 녹슨 권총: 9mm, 400rpm, 반자동, 피해 35 (§5.2)
        public static WeaponDefinitionSO Pistol9()
        {
            var w = ScriptableObject.CreateInstance<WeaponDefinitionSO>();
            w.id = "WPN-PST-01"; w.displayName = "녹슨 권총"; w.size = new Vector2Int(2, 1); w.weightKg = 0.9f;
            w.rarity = Rarity.Common;
            w.cls = WeaponClass.Pistol; w.ammo = AmmoType.Pistol9mm; w.rpm = 400; w.modes = new[] { FireMode.Semi };
            w.damage = 35f;
            w.recoilV = 0.9f; w.recoilH = 0.3f; w.adsTime = 0.2f;
            w.reloadTimeSec = 2.0f; w.fastReloadTimeSec = 1.2f; w.hipSpreadDeg = 2.5f;
            w.shotNoiseRadiusM = 80f;
            return w;
        }

        // WPN-SMG-01 개조 SMG: 9mm, 800rpm, 자동, 피해 30 (§5.2)
        public static WeaponDefinitionSO Smg9()
        {
            var w = ScriptableObject.CreateInstance<WeaponDefinitionSO>();
            w.id = "WPN-SMG-01"; w.displayName = "개조 SMG"; w.size = new Vector2Int(3, 2); w.weightKg = 2.6f;
            w.rarity = Rarity.Common;
            w.cls = WeaponClass.Smg; w.ammo = AmmoType.Pistol9mm; w.rpm = 800; w.modes = new[] { FireMode.Auto };
            w.damage = 30f;
            w.recoilV = 0.45f; w.recoilH = 0.35f; w.adsTime = 0.22f;
            w.reloadTimeSec = 2.3f; w.fastReloadTimeSec = 1.4f; w.hipSpreadDeg = 3.0f;
            w.shotNoiseRadiusM = 90f;
            return w;
        }

        // WPN-RIF-01에 M3 데이터 이전 값(damage 45 §5.2, 소음 120 TODO)을 넣은 것.
        public static WeaponDefinitionSO RifleWithDamage()
        {
            WeaponDefinitionSO w = TestData.Rifle();
            w.damage = 45f;
            w.shotNoiseRadiusM = 120f;
            return w;
        }

        // AMMO-9MM: 기본 피해 30(무기 없이 쏠 때만), 나머지 TODO(GDD-?)
        public static AmmoDefinitionSO Ammo9()
        {
            var a = ScriptableObject.CreateInstance<AmmoDefinitionSO>();
            a.id = "AMMO-9MM"; a.displayName = "9mm 탄"; a.size = Vector2Int.one; a.weightKg = 0.01f;
            a.type = AmmoType.Pistol9mm; a.damage = 30f; a.penetration = 1; a.velocity = 360f;
            a.maxFlightTimeSec = 1.5f; a.pelletCount = 1;
            return a;
        }

        // AMMO-556 (M1 에셋과 같은 값)
        public static AmmoDefinitionSO Ammo556()
        {
            var a = ScriptableObject.CreateInstance<AmmoDefinitionSO>();
            a.id = "AMMO-556"; a.displayName = "5.56 탄"; a.size = Vector2Int.one;
            a.type = AmmoType.Rifle556; a.damage = 45f; a.penetration = 3; a.velocity = 900f;
            a.maxFlightTimeSec = 3f; a.pelletCount = 1;
            return a;
        }

        // MAG-9MM-12 권총 탄창 (§5.2 12발)
        public static MagazineDefinitionSO Mag9_12()
        {
            var m = ScriptableObject.CreateInstance<MagazineDefinitionSO>();
            m.id = "MAG-9MM-12"; m.displayName = "권총 탄창 (12발)"; m.size = new Vector2Int(1, 1); m.weightKg = 0.15f;
            m.ammo = AmmoType.Pistol9mm; m.capacity = 12;
            return m;
        }

        // MAG-9MM-30 SMG 탄창 (§5.2 30발)
        public static MagazineDefinitionSO Mag9_30()
        {
            var m = ScriptableObject.CreateInstance<MagazineDefinitionSO>();
            m.id = "MAG-9MM-30"; m.displayName = "SMG 탄창 (30발)"; m.size = new Vector2Int(1, 2); m.weightKg = 0.35f;
            m.ammo = AmmoType.Pistol9mm; m.capacity = 30;
            return m;
        }

        // ── 아키타입 (§7 + M3 설계 §4.1) ───────────────────────

        // AI-SCV 약탈자: 권총(가중 2)/SMG(가중 1), 소리 추적
        public static EnemyArchetypeSO Scv()
        {
            var a = ScriptableObject.CreateInstance<EnemyArchetypeSO>();
            a.id = "AI-SCV"; a.displayName = "약탈자"; a.corpseDisplayName = "약탈자 시체";
            a.hp = 80f; a.headHp = 35f; a.legsHp = 65f;
            a.sightRange = 60f; a.sightAngle = 110f; a.hearingMultiplier = 1f;
            AmmoDefinitionSO ammo = Ammo9();
            a.loadouts = new List<EnemyLoadout>
            {
                new EnemyLoadout { weapon = Pistol9(), ammo = ammo, magazine = Mag9_12(), weight = 2f },
                new EnemyLoadout { weapon = Smg9(), ammo = ammo, magazine = Mag9_30(), weight = 1f },
            };
            a.spareMagazines = 2;
            a.patrolSpeed = 1.5f; a.moveSpeed = 3.0f; a.sprintSpeed = 5.0f;
            a.reactionTimeSec = 0.6f;
            a.aimErrorBaseM = 0.3f; a.aimErrorPerMeterM = 0.04f;
            a.aimSettleSec = 1.5f; a.unsettledErrorMultiplier = 2.0f;
            a.selfMovingErrorMultiplier = 2.0f; a.targetMovingErrorMultiplier = 1.5f;
            a.burstErrorGrowthM = 0.08f;
            a.burstRounds = new Vector2Int(3, 6); a.burstPauseSec = new Vector2(0.6f, 1.2f);
            a.semiShotIntervalSec = new Vector2(0.35f, 0.6f);
            a.preferredRangeM = new Vector2(8f, 20f); a.engageMaxRangeM = 50f;
            a.usesCover = false; a.flanks = false; a.investigatesNoise = true;
            a.peekSec = Vector2.zero; a.hideSec = Vector2.zero;
            a.tacticalReloadBelow = 0f;
            a.strafeIntervalSec = new Vector2(2f, 4f);
            a.loot = LootTable(new Vector2Int(1, 2),
                (TestData.Bandage(), 3f), (TestData.Wires(), 3f), (TestData.Pipe(), 2f),
                (TestData.Tourniquet(), 1f), (Mag9_12(), 1f));
            a.loot.id = "LT-SCV";
            return a;
        }

        // AI-RDR 레이더: 소총, 엄폐 활용·측면 우회
        public static EnemyArchetypeSO Rdr()
        {
            var a = ScriptableObject.CreateInstance<EnemyArchetypeSO>();
            a.id = "AI-RDR"; a.displayName = "레이더"; a.corpseDisplayName = "레이더 시체";
            a.hp = 80f; a.headHp = 35f; a.legsHp = 65f;
            a.sightRange = 60f; a.sightAngle = 110f; a.hearingMultiplier = 1f;
            a.loadouts = new List<EnemyLoadout>
            {
                new EnemyLoadout { weapon = RifleWithDamage(), ammo = Ammo556(), magazine = TestData.Magazine30(), weight = 1f },
            };
            a.spareMagazines = 3;
            a.patrolSpeed = 1.5f; a.moveSpeed = 3.2f; a.sprintSpeed = 5.5f;
            a.reactionTimeSec = 0.4f;
            a.aimErrorBaseM = 0.2f; a.aimErrorPerMeterM = 0.025f;
            a.aimSettleSec = 1.0f; a.unsettledErrorMultiplier = 2.0f;
            a.selfMovingErrorMultiplier = 2.0f; a.targetMovingErrorMultiplier = 1.3f;
            a.burstErrorGrowthM = 0.05f;
            a.burstRounds = new Vector2Int(3, 5); a.burstPauseSec = new Vector2(0.5f, 1.0f);
            a.semiShotIntervalSec = new Vector2(0.35f, 0.6f);
            a.preferredRangeM = new Vector2(15f, 40f); a.engageMaxRangeM = 60f;
            a.usesCover = true; a.flanks = true; a.investigatesNoise = false;
            a.peekSec = new Vector2(1.5f, 3f); a.hideSec = new Vector2(1f, 2.5f);
            a.tacticalReloadBelow = 0.3f;
            a.strafeIntervalSec = Vector2.zero;
            a.loot = LootTable(new Vector2Int(1, 3),
                (TestData.Bandage(), 2f), (TestData.Tourniquet(), 2f), (TestData.Splint(), 1f),
                (TestData.Magazine30(), 2f), (TestData.Wires(), 1f));
            a.loot.id = "LT-RDR";
            return a;
        }

        // AiConfigSO는 필드 초기값이 설계값(M3 설계 §4.2)이다.
        public static AiConfigSO AiConfig() => ScriptableObject.CreateInstance<AiConfigSO>();

        // ── 루트 테이블 ────────────────────────────────────

        public static LootTableSO LootTable(params (ItemDefinitionSO item, float weight)[] entries) =>
            LootTable(Vector2Int.one, entries);

        public static LootTableSO LootTable(Vector2Int rollCount, params (ItemDefinitionSO item, float weight)[] entries)
        {
            var t = ScriptableObject.CreateInstance<LootTableSO>();
            t.id = "LT-TEST";
            t.rollCount = rollCount;
            t.entries = new List<LootEntry>();
            foreach ((ItemDefinitionSO item, float weight) in entries)
                t.entries.Add(new LootEntry { item = item, weight = weight });
            return t;
        }
    }

    // IProjectileService 가짜: 발사를 기록만 한다. 할당 테스트를 위해 기록 목록을 미리 크게 잡는다.
    internal sealed class FakeProjectileService : IProjectileService
    {
        public struct Shot
        {
            public Vector3 Origin;
            public Vector3 Direction;
            public AmmoDefinitionSO Ammo;
            public float Damage;
            public Transform ShooterRoot;
        }

        public readonly List<Shot> Shots;

        public FakeProjectileService(int capacity = 4096)
        {
            Shots = new List<Shot>(capacity);
        }

        public int Count => Shots.Count;
        public Shot Last => Shots[Shots.Count - 1];

        public void Fire(Vector3 origin, Vector3 direction, AmmoDefinitionSO ammo, float damage, Transform shooterRoot)
        {
            Shots.Add(new Shot { Origin = origin, Direction = direction, Ammo = ammo, Damage = damage, ShooterRoot = shooterRoot });
        }

        public void Clear() => Shots.Clear();
    }

    // ILineOfSight 가짜. BlockedRule(from, to)이 true면 막힘. null이면 아무것도 막지 않는다.
    // 규칙 람다는 테스트 준비 단계에서만 만든다(측정 구간에서 새로 만들지 않아 할당 테스트에 섞이지 않게).
    internal sealed class FakeLineOfSight : ILineOfSight
    {
        public Func<Vector3, Vector3, bool> BlockedRule;
        public int CanSeeCalls;
        public int IsBlockedCalls;

        public FakeLineOfSight(Func<Vector3, Vector3, bool> blockedRule = null)
        {
            BlockedRule = blockedRule;
        }

        public bool CanSee(Vector3 from, Vector3 to, Transform targetRoot, Transform ignoreRoot)
        {
            CanSeeCalls++;
            return BlockedRule == null || !BlockedRule(from, to);
        }

        public bool IsBlocked(Vector3 from, Vector3 to, Transform ignoreA, Transform ignoreB)
        {
            IsBlockedCalls++;
            return BlockedRule != null && BlockedRule(from, to);
        }

        public int TotalCalls => CanSeeCalls + IsBlockedCalls;
        public void ResetCounts() => CanSeeCalls = IsBlockedCalls = 0;
    }

    // IEnemyWorld 가짜. 등록한 엄폐 중 자기에게 가장 가까운 비예약 지점을 준다(점수·LOS 없음).
    // ForcedCoverResult를 정하면 그 결과를 그대로 돌려준다(Deferred·None 시험). NavMesh 점은 기본으로 그 자리를 준다.
    internal sealed class FakeEnemyWorld : IEnemyWorld
    {
        private readonly List<CoverSpot> covers = new List<CoverSpot>();
        private readonly List<int> occupants = new List<int>();

        public float Now { get; set; }
        public CoverResult? ForcedCoverResult;
        public Func<Vector3, Vector3, bool> BlockedRule;     // null이면 막힘 없음
        public bool NavSampleSucceeds = true;
        public Func<Vector3, float, Vector3> NavPointRule;   // null이면 around 그대로

        public int TryFindCoverCalls;
        public CoverQuery LastCoverQuery;
        public int ReleaseCoverCalls;
        public int IsBlockedCalls;
        public int SampleNavCalls;

        public int CoverCount => covers.Count;

        public int AddCover(Vector3 position, Vector3 forward, CoverHeight height = CoverHeight.Low, Vector3? peekPosition = null)
        {
            int id = covers.Count;
            covers.Add(new CoverSpot
            {
                Id = id, Position = position, Forward = forward, Height = height,
                PeekPosition = peekPosition ?? position,
            });
            occupants.Add(-1);
            return id;
        }

        public CoverSpot GetCover(int id) => covers[id];
        public int OccupantOf(int id) => occupants[id];

        public int ReservedBy(int agentId)
        {
            for (int i = 0; i < occupants.Count; i++)
                if (occupants[i] == agentId)
                    return i;
            return -1;
        }

        public CoverResult TryFindCover(in CoverQuery query, out CoverSpot spot)
        {
            TryFindCoverCalls++;
            LastCoverQuery = query;
            spot = default;
            if (ForcedCoverResult.HasValue && ForcedCoverResult.Value != CoverResult.Found)
                return ForcedCoverResult.Value;

            int best = -1;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < covers.Count; i++)
            {
                if (occupants[i] != -1 && occupants[i] != query.AgentId)
                    continue;
                if (query.HasExcludedCover && i == query.ExcludedCoverId)
                    continue;
                float sqr = (covers[i].Position - query.Self).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = i;
                }
            }
            if (best < 0)
                return CoverResult.None;

            ReleaseAll(query.AgentId);
            occupants[best] = query.AgentId;
            spot = covers[best];
            return CoverResult.Found;
        }

        public void ReleaseCover(int agentId)
        {
            ReleaseCoverCalls++;
            ReleaseAll(agentId);
        }

        public bool IsBlocked(Vector3 from, Vector3 to, int ignoreAgentId)
        {
            IsBlockedCalls++;
            return BlockedRule != null && BlockedRule(from, to);
        }

        public bool TrySampleNavPoint(Vector3 around, float radiusM, out Vector3 point)
        {
            SampleNavCalls++;
            if (!NavSampleSucceeds)
            {
                point = around;
                return false;
            }
            point = NavPointRule != null ? NavPointRule(around, radiusM) : around;
            return true;
        }

        private void ReleaseAll(int agentId)
        {
            for (int i = 0; i < occupants.Count; i++)
                if (occupants[i] == agentId)
                    occupants[i] = -1;
        }
    }
}
