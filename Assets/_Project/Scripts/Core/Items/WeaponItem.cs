namespace Ashfall.Core
{
    // 무기 아이템. 장착된 탄창을 들고 있고 무게에 포함한다 (§4.1 무게).
    public sealed class WeaponItem : ItemInstance
    {
        public new WeaponDefinitionSO Definition { get; }
        public Magazine LoadedMagazine { get; set; }

        public WeaponItem(WeaponDefinitionSO definition, Magazine loaded = null) : base(definition)
        {
            Definition = definition;
            LoadedMagazine = loaded;
        }

        public override float WeightKg => Definition.weightKg + (LoadedMagazine?.WeightKg ?? 0f);
    }
}
