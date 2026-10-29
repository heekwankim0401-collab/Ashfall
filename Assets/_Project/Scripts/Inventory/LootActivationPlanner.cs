using System;
using System.Collections.Generic;
using Ashfall.Core;

namespace Ashfall.Inventory
{
    // 레이드 시드 → 루트 지점 활성 계획 (docs/design/MapDensity_design.md §5.3, ⓪ 계약). 순수 — Unity 객체 없음.
    // 구역·종류마다: alwaysActive가 아닌 지점을 slotKey 서수 정렬(씬 순서 무관) → System.Random(Mix(lootSeed, StableHash(GroupKey(zoneId, kind))))
    //   Fisher–Yates → 앞 k개 활성. k = clamp(round(n × 비율), 최소, n) (베르누이가 아닌 표본 추출 — 레이드마다 총량 편차가 작다).
    // alwaysActive 지점(신호탄 상자)은 항상 활성이고 n에서 빠진다. 내용물 시드 = Mix(lootSeed, StableHash(slotKey)) (0 아님).
    // 구역 설정에 없는 zoneId의 지점은 활성 0(최소 활성도 적용 안 함) — ③ 배치 검사가 구역 누락을 따로 막는다.
    // 같은 slotKey가 둘이면 결과가 같이 묶인다 — 키는 ③ 배치가 고유하게 만든다(오브젝트 이름).
    public static class LootActivationPlanner
    {
        public readonly struct Slot
        {
            public readonly string Key;
            public readonly string ZoneId;
            public readonly LootSlotKind Kind;
            public readonly bool AlwaysActive;

            public Slot(string key, string zoneId, LootSlotKind kind, bool alwaysActive)
            {
                Key = key;
                ZoneId = zoneId;
                Kind = kind;
                AlwaysActive = alwaysActive;
            }
        }

        public readonly struct Decision
        {
            public readonly string Key;
            public readonly bool Active;
            public readonly int ContentSeed;   // LootContainer 굴림 시드 (비활성도 값은 있다 — JunkOnly 굴림용)

            public Decision(string key, bool active, int contentSeed)
            {
                Key = key;
                Active = active;
                ContentSeed = contentSeed;
            }
        }

        // ratioOf(zoneId, kind) → 활성 비율 (구역이 없으면 null). 결과는 slots 입력 순서와 같은 순서.
        public static Decision[] Plan(IReadOnlyList<Slot> slots, Func<string, LootSlotKind, float?> ratioOf, int minActivePerZone, int lootSeed)
        {
            if (slots == null)
                return Array.Empty<Decision>();
            var active = new HashSet<string>(StringComparer.Ordinal);
            var groups = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
            var groupKinds = new Dictionary<string, (string zone, LootSlotKind kind)>(StringComparer.Ordinal);
            for (int i = 0; i < slots.Count; i++)
            {
                Slot s = slots[i];
                if (s.AlwaysActive)
                {
                    active.Add(s.Key);
                    continue;
                }
                string groupKey = GroupKey(s.ZoneId, s.Kind);
                if (!groups.TryGetValue(groupKey, out List<string> keys))
                {
                    keys = new List<string>();
                    groups[groupKey] = keys;
                    groupKinds[groupKey] = (s.ZoneId, s.Kind);
                }
                keys.Add(s.Key);
            }

            foreach (KeyValuePair<string, List<string>> pair in groups)
            {
                (string zone, LootSlotKind kind) = groupKinds[pair.Key];
                List<string> keys = pair.Value;
                keys.Sort(StringComparer.Ordinal);
                float? ratio = ratioOf?.Invoke(zone, kind);
                int k = ActiveCount(keys.Count, ratio, minActivePerZone);
                var random = new Random(SeedMath.Mix(lootSeed, SeedMath.StableHash(pair.Key)));
                for (int i = keys.Count - 1; i > 0; i--)
                {
                    int j = random.Next(i + 1);
                    (keys[i], keys[j]) = (keys[j], keys[i]);
                }
                for (int i = 0; i < k; i++)
                    active.Add(keys[i]);
            }

            var result = new Decision[slots.Count];
            for (int i = 0; i < slots.Count; i++)
                result[i] = new Decision(slots[i].Key, active.Contains(slots[i].Key), ContentSeed(lootSeed, slots[i].Key));
            return result;
        }

        // 구역·종류 하나의 활성 수: round(n × 비율)(.5는 올림), 지점이 있으면 최소 minActive, 최대 n. 비율 없음(구역 없음) = 0.
        public static int ActiveCount(int n, float? ratio, int minActive)
        {
            if (n <= 0 || ratio == null)
                return 0;
            int k = (int)Math.Floor(n * Math.Max(0f, Math.Min(1f, ratio.Value)) + 0.5);
            return Math.Min(n, Math.Max(k, Math.Max(0, minActive)));
        }

        public static int ContentSeed(int lootSeed, string slotKey) => SeedMath.Mix(lootSeed, SeedMath.StableHash(slotKey));

        // 구역 id 길이를 앞에 붙여 구분자가 id 안에 있어도 서로 다른 (구역, 종류)가 같은 키가 되지 않게 한다(리뷰 L8). 예: "7:POI-HWY|Crate"
        public static string GroupKey(string zoneId, LootSlotKind kind) => $"{(zoneId ?? "").Length}:{zoneId}|{kind}";
    }
}
