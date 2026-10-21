using System;
using Ashfall.Core;
using UnityEngine;

namespace Ashfall.Raid
{
    // 탈출 지점 하나 (§8.2 EXT-*, §14 ExtractionPointConfig). 조건은 플래그 조합으로 표현한다 (M4 설계 §3.1).
    [Serializable]
    public struct ExtractionPointConfig
    {
        [Tooltip("\"EXT-01\" — 씬 ExtractionZone.id와 같아야 한다")]
        public string id;
        public string displayName;
        [Tooltip("§8.2 소요 — 구역 안에서 기다리는 시간 (초)")]
        public float waitSec;
        [Tooltip("구역 수평 반경 (m). TODO(GDD-?) Q8")]
        public float radiusM;
        [Tooltip("켜져 있어야 하는 스위치 id. 빈 문자열 = 없음 (EXT-02). TODO(GDD-?) Q5")]
        public string requiredSwitchId;
        [Tooltip("레이드 경과 이 시간 이후만 (초). 0 = 제한 없음 (EXT-03 = 900)")]
        public float availableAfterSec;
        [Tooltip("대기 완료 시 소모하는 아이템. null = 없음 (EXT-03 신호탄). TODO(GDD-?) Q6")]
        public ItemDefinitionSO consumedItem;
        [Tooltip("배낭을 메고 있으면 불가 (EXT-04). 조끼는 허용")]
        public bool forbidsBackpack;
    }
}
