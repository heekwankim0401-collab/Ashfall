using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ashfall.Core
{
    // 루트 테이블 항목 하나. §14의 튜플 목록은 직렬화되지 않아 구조체로 쓴다 (M3 결정).
    [Serializable]
    public struct LootEntry
    {
        public ItemDefinitionSO item;
        [Tooltip("상대 가중치 (0 이하는 뽑히지 않음)")]
        public float weight;
    }

    // §14 LootTableSO. 시체(M3)와 상자(M4)가 같이 쓴다. 굴림은 LootRoller.
    // id는 §14에 없는 필드 (데이터 검증용).
    [CreateAssetMenu(menuName = "Ashfall/Loot Table", fileName = "LootTable")]
    public class LootTableSO : ScriptableObject
    {
        public string id;
        public List<LootEntry> entries = new List<LootEntry>();
        [Tooltip("굴림 횟수 최소~최대 (포함)")]
        public Vector2Int rollCount = Vector2Int.one;
        // ★ M7 Q31 (설계 §3.9 "첫 굴림 고정"): 보스 유물처럼 정확히 1개 보장할 아이템. 굴림 1회를 대신하고(굴림 0회여도 1개),
        // 나머지 굴림은 entries에서 뽑는다. 보장 아이템은 entries에 넣지 않는다(넣으면 더 나올 수 있다). null = 없음(기존 동작).
        [Tooltip("★ M7 첫 굴림 고정 — 정확히 1개 보장 (null = 없음). entries에는 넣지 않는다")]
        public ItemDefinitionSO guaranteedFirstRoll;
    }
}
