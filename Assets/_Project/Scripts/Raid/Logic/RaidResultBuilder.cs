using System;
using System.Collections.Generic;
using Ashfall.Core;
using UnityEngine;

namespace Ashfall.Raid
{
    // RULE-01 적용 지점 — 여기 하나뿐이다 (M4 설계 §1.5).
    // 탈출: 반출(CarriedOut) = 종료 시점 소지품 전부(반입 + 레이드 중 획득), 소실 없음.
    // 사망·MIA·포기: 반출 0, 소실(Lost) = 종료 시점 소지품 전부의 정의.
    // M5 경계: 보관함은 CarriedOut만 받는다. 반입 장비는 레이드 시작 시 보관함에서 빠진다.
    public static class RaidResultBuilder
    {
        // broughtIn = 시작 시점 소지품 정의(표시용), endItems = 종료 시점 InventorySnapshot. null은 빈 목록.
        public static RaidResult Build(RaidSession session, IReadOnlyList<ItemDefinitionSO> broughtIn,
            IReadOnlyList<ItemInstance> endItems)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));
            if (!session.IsOver)
                throw new InvalidOperationException("레이드가 끝나기 전에는 결과를 만들 수 없다.");

            bool extracted = session.Outcome == RaidOutcome.Extracted;
            var carriedOut = new List<ItemInstance>();
            var lost = new List<ItemDefinitionSO>();
            if (endItems != null)
            {
                for (int i = 0; i < endItems.Count; i++)
                {
                    ItemInstance item = endItems[i];
                    if (item == null)
                        continue;
                    if (extracted)
                        carriedOut.Add(item);
                    else
                        lost.Add(item.Definition);
                }
            }

            ExtractionModel completed = extracted ? session.CompletedExtraction : null;
            // MIA·AdvanceTime으로 경과가 제한을 넘을 수 있다 → 표시는 제한 시간까지.
            float duration = Mathf.Min(session.ElapsedSec, session.TimeLimitSec);
            return new RaidResult(session.Outcome,
                completed?.Config.id, completed?.Config.displayName,
                session.Outcome == RaidOutcome.Killed && session.HasDeathCause, session.DeathCause,
                duration, session.EnemiesKilled, broughtIn, carriedOut, lost);
        }

        // 소지품 실물 → 정의 목록 (반입 기록용, M4 설계 §1.6: 탄 수·사용 횟수가 바뀌어도 정의는 남는다)
        public static void ToDefinitions(IReadOnlyList<ItemInstance> items, List<ItemDefinitionSO> into)
        {
            if (items == null || into == null)
                return;
            for (int i = 0; i < items.Count; i++)
                if (items[i] != null)
                    into.Add(items[i].Definition);
        }
    }
}
