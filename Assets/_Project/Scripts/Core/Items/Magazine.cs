namespace Ashfall.Core
{
    // §5.1 탄창은 개별 아이템이고 탄 수를 실제로 저장한다.
    // M2: Weapons → Core로 이동. 무기·인벤토리가 같이 쓴다 (설계 D4).
    // M7: 샷건 내장 튜브·탄 상자(isAmmoBox)도 같은 타입이다 (TODO(GDD-?) Q5).
    public sealed class Magazine : ItemInstance
    {
        public new MagazineDefinitionSO Definition { get; }
        public int Rounds { get; private set; }
        public int Capacity => Definition.capacity;
        public bool IsEmpty => Rounds <= 0;
        public bool IsFull => Rounds >= Capacity;

        // 무게는 정의 무게(ItemInstance 기본). TODO(GDD-?): 탄 무게 미반영.

        public Magazine(MagazineDefinitionSO definition, int rounds) : base(definition)
        {
            Definition = definition;
            Rounds = System.Math.Clamp(rounds, 0, definition.capacity);
        }

        public static Magazine Full(MagazineDefinitionSO definition) => new Magazine(definition, definition.capacity);

        public bool TakeRound()
        {
            if (IsEmpty)
                return false;
            Rounds--;
            return true;
        }

        // 튜브 장전: 한 발 넣는다. 가득이면 false (M7).
        public bool TryAddRound()
        {
            if (IsFull)
                return false;
            Rounds++;
            return true;
        }
    }
}
