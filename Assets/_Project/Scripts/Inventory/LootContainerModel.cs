using System;
using System.Collections.Generic;
using Ashfall.Core;
using UnityEngine;

namespace Ashfall.Inventory
{
    // 루트 컨테이너 하나 (§6): 그리드 + 검색 연출(아이템 1개당 0.5초).
    // 검색은 열려 있는 동안만 진행한다 (Tick은 열린 동안만 호출). 닫았다 다시 열면 이어서.
    // 드러나는 순서 = 처음 배치 순서. 플레이어가 넣은 아이템은 처음부터 드러나 있다.
    public sealed class LootContainerModel : ILootView
    {
        // 누적 float 오차 허용 (0.5 × N 경계에서 한 틱 늦지 않게). 튜닝 수치 아님.
        private const float TimeEpsilon = 1e-4f;

        private readonly List<ItemInstance> hidden = new List<ItemInstance>();
        private readonly float searchTimePerItemSec;
        private float elapsed;

        public ItemGrid Grid { get; }
        public string DisplayName { get; }
        public GridRef Ref => Grid.Ref;
        public Vector2Int Size => Grid.Size;
        public IReadOnlyList<PlacedItem> Items => Grid.Items;
        public bool SearchComplete => hidden.Count == 0;
        public int RevealedCount { get; private set; }

        public LootContainerModel(string displayName, Vector2Int gridSize, IEnumerable<ItemInstance> contents,
            float searchTimePerItemSec)
        {
            DisplayName = displayName;
            Grid = new ItemGrid(new GridRef(GridKind.Loot), gridSize);
            this.searchTimePerItemSec = searchTimePerItemSec;

            if (contents == null)
                return;
            foreach (ItemInstance item in contents)
            {
                if (item == null)
                    continue;
                if (Grid.TryFindFreeCell(item, out Vector2Int cell, out bool rotated) && Grid.TryPlace(item, cell, rotated))
                    hidden.Add(item);
                else
                    // TODO(GDD-?): 그리드에 안 들어가는 내용물. 그레이박스에서는 뺀다.
                    Debug.LogWarning($"[LootContainer] {displayName}: {item} 이 그리드 {gridSize}에 들어가지 않아 뺀다.");
            }
        }

        // M5(설계 §3.1, Q28): 이미 있는 그리드를 그대로 감싼다 (복사·재배치 없음). 전부 드러난 상태 — 검색 연출 없음.
        // 은신처 보관함(PlayerProfile.Stash)을 열린 루트 자리로 열 때 쓴다. 그리드 변경은 그 그리드 주인에게 바로 보인다.
        // grid는 GridKind.Loot여야 한다 (UI·PlayerInventory가 Ref로 루트 자리를 찾는다).
        public static LootContainerModel ForExistingGrid(string displayName, ItemGrid grid)
        {
            if (grid == null)
                throw new ArgumentNullException(nameof(grid));
            if (grid.Ref.Kind != GridKind.Loot)
                throw new ArgumentException($"루트 자리 그리드가 아니다: {grid.Ref}", nameof(grid));
            return new LootContainerModel(displayName, grid);
        }

        private LootContainerModel(string displayName, ItemGrid grid)
        {
            DisplayName = displayName;
            Grid = grid;
            searchTimePerItemSec = 0f;
        }

        public bool IsRevealed(ItemInstance item) => item != null && !hidden.Contains(item);

        // 열려 있는 동안만 호출. 새로 드러난 게 있으면 true.
        public bool Tick(float dt)
        {
            if (hidden.Count == 0)
                return false;

            if (searchTimePerItemSec <= 0f)
            {
                RevealedCount += hidden.Count;
                hidden.Clear();
                return true;
            }

            elapsed += dt;
            bool revealed = false;
            while (hidden.Count > 0 && elapsed + TimeEpsilon >= searchTimePerItemSec)
            {
                elapsed -= searchTimePerItemSec;
                hidden.RemoveAt(0);
                RevealedCount++;
                revealed = true;
            }
            if (hidden.Count == 0)
                elapsed = 0f;
            return revealed;
        }
    }
}
