using System;
using System.Collections.Generic;

namespace Ashfall.Core
{
    // 레이드 종료 사유 (§2 코어 루프, RULE-01·02, M4 설계 §1.2).
    public enum RaidOutcome
    {
        Extracted,
        Killed,
        MissingInAction,   // RULE-02 25분 초과 = 사망 처리
        Abandoned,         // 일시정지 메뉴에서 포기 = 사망과 같은 RULE-01 (TODO(GDD-?) Q16)
    }

    // 레이드 한 판의 결과 (§2 코어 루프, RULE-01). Raid가 만들고 GameFlow가 씬을 넘어 들고 간다.
    // M5 경계: 은신처가 CarriedOut을 보관함에 넣는다. 사망·MIA·포기면 CarriedOut은 비어 있다.
    // 목록은 생성 시 복사한다 (호출자가 재사용하는 스냅샷 리스트와 분리). null → 빈 목록.
    public sealed class RaidResult
    {
        public RaidResult(RaidOutcome outcome, string extractionId, string extractionName,
            bool hasDeathCause, BodyPart deathCause, float durationSec, int enemiesKilled,
            IReadOnlyList<ItemDefinitionSO> broughtIn, IReadOnlyList<ItemInstance> carriedOut,
            IReadOnlyList<ItemDefinitionSO> lost)
        {
            Outcome = outcome;
            ExtractionId = extractionId;
            ExtractionName = extractionName;
            HasDeathCause = hasDeathCause;
            DeathCause = deathCause;
            DurationSec = durationSec;
            EnemiesKilled = enemiesKilled;
            BroughtIn = Copy(broughtIn);
            CarriedOut = Copy(carriedOut);
            Lost = Copy(lost);
        }

        public RaidOutcome Outcome { get; }
        public bool Survived => Outcome == RaidOutcome.Extracted;
        public string ExtractionId { get; }                       // 탈출이 아니면 null
        public string ExtractionName { get; }
        public bool HasDeathCause { get; }                        // Killed일 때
        public BodyPart DeathCause { get; }
        public float DurationSec { get; }
        public int EnemiesKilled { get; }
        public IReadOnlyList<ItemDefinitionSO> BroughtIn { get; } // 반입(시작 시점, 표시용)
        public IReadOnlyList<ItemInstance> CarriedOut { get; }    // 반출(탈출 시 종료 시점 소지품 실물)
        public IReadOnlyList<ItemDefinitionSO> Lost { get; }      // 소실(사망·MIA·포기 시 종료 시점 소지품, 표시용)

        private static T[] Copy<T>(IReadOnlyList<T> source)
        {
            if (source == null || source.Count == 0)
                return Array.Empty<T>();
            var copy = new T[source.Count];
            for (int i = 0; i < copy.Length; i++)
                copy[i] = source[i];
            return copy;
        }
    }
}
