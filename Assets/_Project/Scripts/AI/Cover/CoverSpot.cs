using UnityEngine;

namespace Ashfall.AI
{
    // CoverIndex에 들어가는 엄폐 지점 하나 (M3 설계 §1.7). CoverPoint 컴포넌트에서 만든다.
    public struct CoverSpot
    {
        public int Id;
        public Vector3 Position;       // 숨는 발 위치
        public Vector3 Forward;        // 막아 주는 방향(수평 정규화)
        public CoverHeight Height;
        public Vector3 PeekPosition;   // 사격 발 위치 (Low면 Position)
    }
}
