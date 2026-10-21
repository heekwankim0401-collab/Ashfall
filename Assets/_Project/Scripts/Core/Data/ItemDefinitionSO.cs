using UnityEngine;

namespace Ashfall.Core
{
    // §14 모든 아이템의 공통 정의.
    [CreateAssetMenu(menuName = "Ashfall/Item", fileName = "Item")]
    public class ItemDefinitionSO : ScriptableObject
    {
        public string id;
        public string displayName;
        public Vector2Int size = Vector2Int.one;
        public float weightKg;
        public Rarity rarity;
        public int basePrice;
        public Sprite icon;
    }
}
