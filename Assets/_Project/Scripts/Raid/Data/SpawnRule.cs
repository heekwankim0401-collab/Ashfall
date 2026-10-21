using System;
using UnityEngine;

namespace Ashfall.Raid
{
    // 스폰 지점별 활성 탈출구 (§8.2 "스폰 위치 기준 2~3개만 활성", TODO(GDD-?) Q3: 스폰별 고정 세트, EXT-01 항상 포함).
    [Serializable]
    public struct SpawnRule
    {
        [Tooltip("씬 PlayerSpawnPoint.id")]
        public string spawnId;
        [Tooltip("이 스폰에서 활성인 탈출구 id. 비면 무작위 규칙 (RaidConfigSO.randomActiveCount)")]
        public string[] activeExtractionIds;
    }
}
