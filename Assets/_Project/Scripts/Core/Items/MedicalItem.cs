namespace Ashfall.Core
{
    // 의약품 아이템 (§4.3 해제 수단). 남은 사용 횟수를 저장한다.
    public sealed class MedicalItem : ItemInstance
    {
        public new MedicalDefinitionSO Definition { get; }
        public int UsesLeft { get; private set; }

        public MedicalItem(MedicalDefinitionSO definition) : base(definition)
        {
            Definition = definition;
            UsesLeft = definition.uses;
        }

        // M5 세이브 복원 (M5 설계 §2.2). 남은 횟수는 0~uses로 자른다 (0 횟수도 유지, 사용은 M2 Depleted로 거부).
        public MedicalItem(MedicalDefinitionSO definition, int usesLeft) : base(definition)
        {
            Definition = definition;
            UsesLeft = System.Math.Clamp(usesLeft, 0, System.Math.Max(0, definition.uses));
        }

        // 1회 사용. 남은 횟수가 0이면 false.
        public bool ConsumeUse()
        {
            if (UsesLeft <= 0)
                return false;
            UsesLeft--;
            return true;
        }
    }
}
