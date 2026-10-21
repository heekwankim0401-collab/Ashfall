using System;
using UnityEngine;

namespace Ashfall.AI
{
    // 씬의 엄폐 지점 목록 + 예약 (M3 설계 §1.7). 한 지점에 한 명, 한 명은 한 지점만 예약한다.
    // id = 추가 순번. 공간 분할 없음(선형 탐색). TODO(M4): 수백 개면 균일 격자.
    public sealed class CoverIndex
    {
        private CoverSpot[] spots;
        private int[] occupants;

        public CoverIndex(int capacity)
        {
            capacity = Math.Max(1, capacity);
            spots = new CoverSpot[capacity];
            occupants = new int[capacity];
        }

        public int Count { get; private set; }

        // 등록 때만 부른다(용량이 모자라면 늘린다). id 반환.
        public int Add(Vector3 position, Vector3 forward, CoverHeight height, Vector3 peekPosition)
        {
            if (Count == spots.Length)
            {
                Array.Resize(ref spots, spots.Length * 2);
                Array.Resize(ref occupants, occupants.Length * 2);
            }

            Vector3 flat = new Vector3(forward.x, 0f, forward.z);
            flat = flat.sqrMagnitude > 1e-6f ? flat.normalized : Vector3.forward;

            int id = Count++;
            spots[id] = new CoverSpot
            {
                Id = id,
                Position = position,
                Forward = flat,
                Height = height,
                PeekPosition = height == CoverHeight.Low ? position : peekPosition,
            };
            occupants[id] = -1;
            return id;
        }

        public CoverSpot Get(int id) => spots[id];
        public int OccupantOf(int id) => occupants[id];

        // 비어 있거나 자기 예약이면 예약한다(이전 자기 예약은 풀린다).
        public bool TryReserve(int id, int agentId)
        {
            if (id < 0 || id >= Count)
                return false;
            int occupant = occupants[id];
            if (occupant != -1 && occupant != agentId)
                return false;
            ReleaseAll(agentId);
            occupants[id] = agentId;
            return true;
        }

        public void ReleaseAll(int agentId)
        {
            for (int i = 0; i < Count; i++)
                if (occupants[i] == agentId)
                    occupants[i] = -1;
        }

        public int ReservedBy(int agentId)
        {
            for (int i = 0; i < Count; i++)
                if (occupants[i] == agentId)
                    return i;
            return -1;
        }
    }
}
