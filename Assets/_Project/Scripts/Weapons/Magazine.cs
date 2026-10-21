using Ashfall.Core;

namespace Ashfall.Weapons
{
    // §5.1 탄창은 개별 아이템이고 탄 수를 실제로 저장한다.
    public sealed class Magazine
    {
        public MagazineDefinitionSO Definition { get; }
        public int Rounds { get; private set; }
        public int Capacity => Definition.capacity;
        public bool IsEmpty => Rounds <= 0;

        public Magazine(MagazineDefinitionSO definition, int rounds)
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
    }
}
