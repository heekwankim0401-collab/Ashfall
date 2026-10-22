using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ashfall.Core
{
    // 세이브 id → 아이템 정의 (M5 설계 §2.3). 테스트는 사전 가짜를 쓴다.
    public interface IItemCatalog
    {
        bool TryGet(string id, out ItemDefinitionSO definition);
    }

    // 모든 아이템 정의 목록 (에셋 Data/Items/ItemDatabase.asset, M5 데이터 메뉴가 id 순으로 채운다).
    // §14 스키마에 없는 추가. 은신처 씬의 HideoutController가 직렬화 참조로 ProfileService에 넘긴다.
    [CreateAssetMenu(menuName = "Ashfall/Item Database", fileName = "ItemDatabase")]
    public class ItemDatabaseSO : ScriptableObject, IItemCatalog
    {
        [Tooltip("모든 아이템 정의 (M5 데이터 메뉴가 id 순으로 채운다)")]
        public List<ItemDefinitionSO> items = new List<ItemDefinitionSO>();

        // 처음 TryGet에서 만든다. 에디터에서 목록을 바꾸면(OnValidate) 다시 만든다.
        [NonSerialized] private Dictionary<string, ItemDefinitionSO> cache;

        public int Count => items?.Count ?? 0;

        public bool TryGet(string id, out ItemDefinitionSO definition)
        {
            definition = null;
            if (string.IsNullOrEmpty(id))
                return false;
            if (cache == null)
                BuildCache();
            return cache.TryGetValue(id, out definition);
        }

        private void OnValidate() => cache = null;

        private void OnEnable() => cache = null;

        // null·빈 id 무시. 중복 id는 첫 번째를 쓰고 경고 1회(빌드할 때마다 1회).
        private void BuildCache()
        {
            cache = new Dictionary<string, ItemDefinitionSO>(StringComparer.Ordinal);
            if (items == null)
                return;
            bool warned = false;
            for (int i = 0; i < items.Count; i++)
            {
                ItemDefinitionSO item = items[i];
                if (item == null || string.IsNullOrEmpty(item.id))
                    continue;
                if (cache.ContainsKey(item.id))
                {
                    if (!warned)
                    {
                        Debug.LogWarning($"[ItemDatabase] 중복 id {item.id} — 첫 번째 정의를 쓴다.", this);
                        warned = true;
                    }
                    continue;
                }
                cache.Add(item.id, item);
            }
        }
    }
}
