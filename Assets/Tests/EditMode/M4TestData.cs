using System.Collections.Generic;
using Ashfall.Core;
using Ashfall.Raid;
using UnityEngine;

namespace Ashfall.Tests.EditMode
{
    // M4 테스트 도우미 (M4 설계 §6 1단계). TestData.cs·M3TestData.cs는 고치지 않는다.
    // RaidConfig 값은 §8.2 표 + M4 설계 임시값(TODO(GDD-?))과 같다. 에셋 값이 바뀌어도 규칙 검증은 이 값 기준.
    internal static class M4TestData
    {
        public const string Ext01 = "EXT-01";
        public const string Ext02 = "EXT-02";
        public const string Ext03 = "EXT-03";
        public const string Ext04 = "EXT-04";
        public const string PowerSwitchId = "SW-PWR";
        public const string SpawnWest = "SP-W";
        public const string SpawnEast = "SP-E";
        public const string SpawnNorth = "SP-N";

        // ── 아이템 (Q7 신호탄, Q11 지도. 크기·무게 TODO(GDD-?)) ─────────

        public static ItemDefinitionSO Flare() =>
            TestData.Item("ITM-FLR-01", "신호탄", new Vector2Int(1, 2), 0.3f, Rarity.Common);

        public static ItemDefinitionSO MapItem() =>
            TestData.Item("ITM-MAP-01", "종이 지도", new Vector2Int(1, 1), 0.1f, Rarity.Common);

        // ── RaidConfig (§8.2, §14) ─────────────────────────────

        // EXT-01~04 (§8.2 소요·조건 + 반경 8/8/10/5m 임시값), 스폰별 활성 세트(설계 §2.3 표, Q3).
        // flare null이면 새로 만든다.
        public static RaidConfigSO RaidConfig(ItemDefinitionSO flare = null)
        {
            var c = ScriptableObject.CreateInstance<RaidConfigSO>();
            c.timeLimitSec = 1500f;
            c.extractions = new[]
            {
                Extraction(Ext01, "남쪽 도로", 10f, 8f),
                Extraction(Ext02, "열차 신호소", 15f, 8f, requiredSwitchId: PowerSwitchId),
                Extraction(Ext03, "헬기 착륙장", 20f, 10f, availableAfterSec: 900f, consumedItem: flare ? flare : Flare()),
                Extraction(Ext04, "하수 배출구", 7f, 5f, forbidsBackpack: true),
            };
            c.spawnRules = new[]
            {
                SpawnRule(SpawnWest, Ext01, Ext02, Ext03),
                SpawnRule(SpawnEast, Ext01, Ext04, Ext03),
                SpawnRule(SpawnNorth, Ext01, Ext02, Ext04),
            };
            c.randomActiveCount = new Vector2Int(2, 3);
            c.alwaysActiveIds = new[] { Ext01 };
            c.endDelaySec = 3f;
            c.zoneHeightToleranceM = 3f;
            return c;
        }

        public static ExtractionPointConfig Extraction(string id, string displayName, float waitSec, float radiusM,
            string requiredSwitchId = "", float availableAfterSec = 0f, ItemDefinitionSO consumedItem = null,
            bool forbidsBackpack = false) =>
            new ExtractionPointConfig
            {
                id = id,
                displayName = displayName,
                waitSec = waitSec,
                radiusM = radiusM,
                requiredSwitchId = requiredSwitchId,
                availableAfterSec = availableAfterSec,
                consumedItem = consumedItem,
                forbidsBackpack = forbidsBackpack,
            };

        public static SpawnRule SpawnRule(string spawnId, params string[] activeExtractionIds) =>
            new SpawnRule { spawnId = spawnId, activeExtractionIds = activeExtractionIds };

        // 이름으로 찾기 (없으면 default)
        public static ExtractionPointConfig Find(RaidConfigSO config, string id)
        {
            foreach (ExtractionPointConfig e in config.extractions)
                if (e.id == id)
                    return e;
            return default;
        }

        // ── 결과 ───────────────────────────────────────────

        public static RaidResult Result(RaidOutcome outcome, string extractionId = null, string extractionName = null,
            IReadOnlyList<ItemDefinitionSO> broughtIn = null, IReadOnlyList<ItemInstance> carriedOut = null,
            IReadOnlyList<ItemDefinitionSO> lost = null) =>
            new RaidResult(outcome, extractionId, extractionName, outcome == RaidOutcome.Killed, BodyPart.Head,
                600f, 2, broughtIn, carriedOut, lost);
    }

    // GameFlow 대리자 기록 (씬 로드·timeScale)
    internal sealed class FlowRecorder
    {
        public readonly List<string> Loads = new List<string>();
        public readonly List<float> TimeScales = new List<float>();
        // false면 로드 요청 실패 (빌드 설정에 없는 씬 등). 요청은 Loads에 그대로 기록한다.
        public bool LoadSucceeds = true;

        public GameFlow Create() => new GameFlow(name =>
        {
            Loads.Add(name);
            return LoadSucceeds;
        }, TimeScales.Add);
    }

    // IPlayerTarget 가짜 (Raid 탈출 구역 판정·사망). 값은 테스트가 직접 바꾼다.
    internal sealed class FakePlayerTarget : IPlayerTarget
    {
        public Transform Root { get; set; }
        public Vector3 Position { get; set; }
        public Vector3 EyePosition => Position + Vector3.up * 1.6f;
        public Vector3 CenterPosition => Position + Vector3.up * 0.9f;
        public Vector3 Velocity { get; set; }
        public TargetPosture Posture { get; set; }
        public bool IsDead { get; set; }
    }

    // IPlayerPlacement 가짜 (마지막 배치 기록 + FakePlayerTarget 위치 갱신)
    internal sealed class FakePlayerPlacement : IPlayerPlacement
    {
        private readonly FakePlayerTarget target;

        public FakePlayerPlacement(FakePlayerTarget target = null)
        {
            this.target = target;
        }

        public int Calls { get; private set; }
        public Vector3 LastPosition { get; private set; }
        public float LastYawDeg { get; private set; }

        public void PlaceAt(Vector3 feetPosition, float yawDeg)
        {
            Calls++;
            LastPosition = feetPosition;
            LastYawDeg = yawDeg;
            if (target != null)
                target.Position = feetPosition;
        }
    }
}
