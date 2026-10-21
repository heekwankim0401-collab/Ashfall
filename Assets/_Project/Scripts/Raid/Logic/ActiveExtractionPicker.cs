using System.Collections.Generic;

namespace Ashfall.Raid
{
    // 스폰·활성 탈출구 선택 (§8.2 "스폰 위치 기준 2~3개만 활성", M4 설계 §3.2). 레이드 시작 때 한 번만 쓴다.
    public static class ActiveExtractionPicker
    {
        // TODO(GDD-?) Q2: 고정 스폰 중 무작위 하나. 스폰이 없으면 -1.
        public static int PickSpawnIndex(int spawnCount, System.Random random)
        {
            if (spawnCount <= 0)
                return -1;
            return random != null ? random.Next(spawnCount) : 0;
        }

        // 활성 탈출구 id를 into에 더하고 더한 수를 돌려준다. config.extractions에 없는 id는 넣지 않는다. 중복 없음.
        // TODO(GDD-?) Q3: spawnRules에 spawnId 규칙(비어 있지 않은 목록)이 있으면 그 목록 + alwaysActiveIds(빠졌으면 뒤에).
        //   없으면 alwaysActiveIds + 나머지에서 무작위로 골라 총 randomActiveCount.x~y(포함)개.
        public static int Pick(RaidConfigSO config, string spawnId, System.Random random, List<string> into)
        {
            if (config == null || into == null || config.extractions == null)
                return 0;
            int start = into.Count;

            string[] ruleIds = FindRule(config, spawnId);
            if (ruleIds != null)
            {
                AddAll(config, ruleIds, into, start);
                AddAll(config, config.alwaysActiveIds, into, start);
                return into.Count - start;
            }

            AddAll(config, config.alwaysActiveIds, into, start);

            var candidates = new List<string>();
            foreach (ExtractionPointConfig e in config.extractions)
                if (!string.IsNullOrEmpty(e.id) && !Contains(into, start, e.id) && !candidates.Contains(e.id))
                    candidates.Add(e.id);

            int min = config.randomActiveCount.x;
            int max = config.randomActiveCount.y < min ? min : config.randomActiveCount.y;
            int target = random != null ? random.Next(min, max + 1) : min;
            while (into.Count - start < target && candidates.Count > 0)
            {
                int index = random != null ? random.Next(candidates.Count) : 0;
                into.Add(candidates[index]);
                candidates.RemoveAt(index);
            }
            return into.Count - start;
        }

        private static string[] FindRule(RaidConfigSO config, string spawnId)
        {
            if (string.IsNullOrEmpty(spawnId) || config.spawnRules == null)
                return null;
            foreach (SpawnRule rule in config.spawnRules)
                if (rule.spawnId == spawnId && rule.activeExtractionIds != null && rule.activeExtractionIds.Length > 0)
                    return rule.activeExtractionIds;
            return null;
        }

        private static void AddAll(RaidConfigSO config, string[] ids, List<string> into, int start)
        {
            if (ids == null)
                return;
            foreach (string id in ids)
                if (!string.IsNullOrEmpty(id) && Exists(config, id) && !Contains(into, start, id))
                    into.Add(id);
        }

        private static bool Exists(RaidConfigSO config, string id)
        {
            foreach (ExtractionPointConfig e in config.extractions)
                if (e.id == id)
                    return true;
            return false;
        }

        private static bool Contains(List<string> list, int start, string id)
        {
            for (int i = start; i < list.Count; i++)
                if (list[i] == id)
                    return true;
            return false;
        }
    }
}
