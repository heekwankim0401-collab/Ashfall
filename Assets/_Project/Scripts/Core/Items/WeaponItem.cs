using System;

namespace Ashfall.Core
{
    // 무기 아이템. 장착된 탄창을 들고 있고 무게에 포함한다 (§4.1 무게).
    // M7: 내구도(§5.1, 0~100 %)와 고장 상태. 내장 튜브 무기는 생성 때 빈 튜브를 장착 탄창으로 가진다 (TODO(GDD-?) Q5).
    public sealed class WeaponItem : ItemInstance
    {
        public const float MaxDurability = 100f;

        private float durability = MaxDurability;

        public new WeaponDefinitionSO Definition { get; }
        public Magazine LoadedMagazine { get; set; }

        // 0~100으로 자른다. NaN은 100 (TODO(GDD-?) Q12: 상인·제작·키트·상자 무기 = 100).
        public float Durability
        {
            get => durability;
            set => durability = float.IsNaN(value) ? MaxDurability : Math.Clamp(value, 0f, MaxDurability);
        }

        // 고장 중이면 발사 안 됨 — 레이드 중에는 T 길게로만 해제 (Q9). 저장하지 않고 은신처 귀환 때 푼다(ProfileService.ApplyRaidResult, Q14).
        public bool IsJammed { get; set; }

        public bool HasInternalMagazine => Definition.internalMagazine != null;

        public WeaponItem(WeaponDefinitionSO definition, Magazine loaded = null) : base(definition)
        {
            Definition = definition;
            LoadedMagazine = loaded ?? (definition.internalMagazine != null ? new Magazine(definition.internalMagazine, 0) : null);
        }

        public override float WeightKg => Definition.weightKg + (LoadedMagazine?.WeightKg ?? 0f);
    }
}
