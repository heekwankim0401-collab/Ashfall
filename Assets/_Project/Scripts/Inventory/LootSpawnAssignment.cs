using Ashfall.Core;

namespace Ashfall.Inventory
{
    // 루트 지점 하나를 이번 레이드에 어떻게 둘지 (docs/design/MapDensity_design.md §5.3, ③). 순수 — LootSpawnDirector가 결과를 LootContainer에 적용한다.
    // - 항상 활성(신호탄 상자): 직렬화된 테이블·고정 내용물 그대로, 내용물 시드만 결정적으로(같은 레이드 시드 = 같은 내용물).
    // - 활성: 구역 설정의 종류별 테이블(없으면 직렬화된 테이블), 내용물 시드.
    // - 비활성: inactiveMode = Empty → 빈 컨테이너(Q1 A, 기본) / JunkOnly → 비우고 inactiveTable 굴림.
    public static class LootSpawnAssignment
    {
        public enum Mode
        {
            Table,       // ConfigureForRaid(Table, Seed) — 고정 내용물 유지
            Empty,       // ConfigureEmpty()
            EmptyThenTable,   // ConfigureEmpty() 뒤 ConfigureForRaid(Table, Seed) — JunkOnly
        }

        public readonly struct Result
        {
            public readonly Mode Mode;
            public readonly LootTableSO Table;
            public readonly int Seed;

            public Result(Mode mode, LootTableSO table, int seed)
            {
                Mode = mode;
                Table = table;
                Seed = seed;
            }
        }

        public static Result Resolve(LootActivationPlanner.Slot slot, LootActivationPlanner.Decision decision, LootSpawnConfigSO config,
            LootTableSO serializedTable)
        {
            if (slot.AlwaysActive)
                return new Result(Mode.Table, serializedTable, decision.ContentSeed);
            if (decision.Active)
            {
                LootTableSO table = serializedTable;
                if (config != null && config.TryGetZone(slot.ZoneId, out LootZoneConfig zone) && zone.TableOf(slot.Kind) != null)
                    table = zone.TableOf(slot.Kind);
                return new Result(Mode.Table, table, decision.ContentSeed);
            }
            if (config != null && config.inactiveMode == InactiveLootMode.JunkOnly && config.inactiveTable != null)
                return new Result(Mode.EmptyThenTable, config.inactiveTable, decision.ContentSeed);
            return new Result(Mode.Empty, null, decision.ContentSeed);
        }

        // 결과를 컨테이너에 적용. 이미 열린 컨테이너(Model 있음)면 false.
        public static bool Apply(LootContainer container, in Result result)
        {
            if (container == null || container.Model != null)
                return false;
            switch (result.Mode)
            {
                case Mode.Empty:
                    return container.ConfigureEmpty();
                case Mode.EmptyThenTable:
                    return container.ConfigureEmpty() && container.ConfigureForRaid(result.Table, result.Seed);
                default:
                    return container.ConfigureForRaid(result.Table, result.Seed);
            }
        }
    }
}
