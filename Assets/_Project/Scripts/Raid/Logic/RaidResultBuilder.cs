using System;
using System.Collections.Generic;
using Ashfall.Core;
using UnityEngine;

namespace Ashfall.Raid
{
    // RULE-01 적용 지점 — 여기 하나뿐이다 (M4 설계 §1.5, M5 설계 §1.3).
    // 탈출: 반출(CarriedOut) = 종료 시점 소지품 전부(반입 + 레이드 중 획득), 소실 없음.
    // 사망·MIA·포기: 반출 0, 소실(Lost) = 종료 시점 소지품 전부의 정의.
    // M5: 종료 시점 Loadout(위치 포함)을 받는 오버로드가 탈출이면 CarriedOutLoadout에 그대로 담는다 → 은신처 장비가 된다
    //     (반영은 ProfileService.ApplyRaidResult 한 곳). 반입 장비는 레이드 시작 때 은신처 장비에서 빠져 있다. 보관함은 바뀌지 않는다.
    public static class RaidResultBuilder
    {
        // M4 목록 오버로드. broughtIn = 시작 시점 소지품 정의(표시용), endItems = 종료 시점 InventorySnapshot. null은 빈 목록.
        // 위치가 없으므로 CarriedOutLoadout은 항상 null(흐름 레이드는 아래 Loadout 오버로드를 쓴다).
        public static RaidResult Build(RaidSession session, IReadOnlyList<ItemDefinitionSO> broughtIn,
            IReadOnlyList<ItemInstance> endItems) =>
            BuildCore(session, broughtIn, endItems, null);

        // M5 Loadout 오버로드. endLoadout = 종료 시점 Loadout.Capture(RaidEnded 직전). null → 빈 장비.
        // 평면 목록(CarriedOut/Lost)은 endLoadout.CollectItems 순서(= InventorySnapshot.Collect 순서) — 결과 화면은 M4와 같다.
        // 탈출이면 CarriedOutLoadout = endLoadout(위치 그대로), 그 밖이면 null(반출 0).
        public static RaidResult Build(RaidSession session, IReadOnlyList<ItemDefinitionSO> broughtIn, Loadout endLoadout)
        {
            Loadout end = endLoadout ?? Loadout.Empty;
            var endItems = new List<ItemInstance>(end.Entries.Count);
            end.CollectItems(endItems);
            return BuildCore(session, broughtIn, endItems, end);
        }

        private static RaidResult BuildCore(RaidSession session, IReadOnlyList<ItemDefinitionSO> broughtIn,
            IReadOnlyList<ItemInstance> endItems, Loadout endLoadout)
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
            string extractionId = completed?.Config.id;
            string extractionName = completed?.Config.displayName;
            // DEV-RETURN: 개발용 귀환은 탈출구 없이 Extracted → 결과 화면 "탈출 성공 — 귀환(개발)"
            if (extracted && completed == null && session.IsDevReturn)
            {
                extractionId = DevFeatures.DevReturnExtractionId;
                extractionName = DevFeatures.DevReturnLabel;
            }
            // MIA·AdvanceTime으로 경과가 제한을 넘을 수 있다 → 표시는 제한 시간까지.
            float duration = Mathf.Min(session.ElapsedSec, session.TimeLimitSec);
            return new RaidResult(session.Outcome,
                extractionId, extractionName,
                session.Outcome == RaidOutcome.Killed && session.HasDeathCause, session.DeathCause,
                duration, session.EnemiesKilled, broughtIn, carriedOut, lost,
                extracted ? endLoadout : null,
                session.DeathKind);   // MVP 마무리 F1: 사인 종류 (원인 모르는 사망·사망 외 = Wound, 표시에 안 쓰임)
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
