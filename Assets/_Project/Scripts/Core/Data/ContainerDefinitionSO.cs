using UnityEngine;

namespace Ashfall.Core
{
    // 전술조끼·배낭 정의 (§6 저장 공간). §14에 없는 클래스 (설계 D6).
    // 내용물 그리드는 인스턴스가 아니라 PlayerInventory가 장착 슬롯별로 가진다 (설계 D5).
    [CreateAssetMenu(menuName = "Ashfall/Container", fileName = "Container")]
    public class ContainerDefinitionSO : ItemDefinitionSO
    {
        public ContainerKind kind;
        [Tooltip("내용물 그리드 크기 (칸, W×H)")]
        public Vector2Int gridSize = Vector2Int.one;
    }
}
