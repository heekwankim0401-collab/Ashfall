using System;
using Ashfall.Core;
using UnityEngine;

namespace Ashfall.Hideout
{
    // 레시피 재료 한 줄 (정의 × 개수). 보관함의 같은 정의 인스턴스를 개수만큼 쓴다 (Q44).
    [Serializable]
    public struct CraftingIngredient
    {
        [Tooltip("재료 아이템 정의 (고철 = JNK-*, Q48)")]
        public ItemDefinitionSO item;
        [Tooltip("필요 개수 (인스턴스 수). 1 이상")]
        [Min(1)]
        public int count;
    }

    // 레시피 하나 (M7 설계 §3.8·§4.7). 산출 = output 정의 × outputCount개 (탄창은 가득 찬 상태, Q46).
    [Serializable]
    public sealed class CraftingRecipe
    {
        [Tooltip("\"RCP-AMMO-9MM12\" — TryCraft(recipeId)가 찾는 id")]
        public string id;
        [Tooltip("제작 탭에 보이는 이름. TODO(GDD-?) Q47")]
        public string displayName;
        [Tooltip("재료 (보관함에서만 꺼낸다). TODO(GDD-?) Q47")]
        public CraftingIngredient[] inputs = new CraftingIngredient[0];
        [Tooltip("산출 아이템 정의 (ItemFactory.Create). TODO(GDD-?) Q47")]
        public ItemDefinitionSO output;
        [Tooltip("산출 개수. 1 이상")]
        [Min(1)]
        public int outputCount = 1;
    }

    // SYS-HUB 제작대 (§9 "고철을 탄약·의약품으로", M7 설계 §1.5, Q44~Q47). §14 스키마에 없는 추가. 에셋 Data/Hideout/CraftingConfig.asset(7C-1b).
    // 즉시 완료, 탄피 비용 없음, 제작 시간·대기열 없음. 규칙은 Hideout CraftingModel(7C-2b).
    [CreateAssetMenu(menuName = "Ashfall/Crafting Config", fileName = "CraftingConfig")]
    public class CraftingConfigSO : ScriptableObject
    {
        [Tooltip("레시피 목록 (이 순서로 표시). TODO(GDD-?) Q47")]
        public CraftingRecipe[] recipes = new CraftingRecipe[0];
    }
}
