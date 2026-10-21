using UnityEngine;

namespace Ashfall.Inventory
{
    // SYS-INV 인벤토리·루트 수치. §14에 없는 SO (설계 D6).
    // 에셋: Data/Inventory/InventoryConfig.asset
    [CreateAssetMenu(menuName = "Ashfall/Inventory Config", fileName = "InventoryConfig")]
    public class InventoryConfigSO : ScriptableObject
    {
        [Header("§6 주머니")]
        public int pocketCount = 4;
        public Vector2Int pocketSize = Vector2Int.one;

        [Header("§6 루트 검색")]
        [Tooltip("아이템 1개를 드러내는 데 걸리는 시간 (초)")]
        public float searchTimePerItemSec = 0.5f;

        // TODO(GDD-?): 아래는 문서에 없는 임시값.
        [Header("상호작용 (임시값)")]
        [Tooltip("F 상호작용 레이캐스트 거리 (m)")]
        public float interactRangeM = 2.0f;

        [Header("재장전 탄창 (임시값)")]
        [Tooltip("재장전 탄창을 배낭에서도 찾는다 (false = 조끼 + 주머니만)")]
        public bool reloadFromBackpack = false;

        [Header("바닥에 버리기 (임시값)")]
        [Tooltip("버린 아이템이 생기는 앞쪽 거리 (m)")]
        public float dropForwardM = 0.6f;
        [Tooltip("버린 아이템 초기 속도 (m/s)")]
        public float dropSpeed = 1.5f;
        [Tooltip("바닥 아이템 상자 크기 = 칸 수 × 이 값 (m). 그레이박스 표현")]
        public float worldItemCellM = 0.1f;
    }
}
