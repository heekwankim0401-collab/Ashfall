using System;
using Ashfall.Core;

namespace Ashfall.Weapons
{
    // 무기 교체 1/2/3 (§12.1, SYS-WPN, M7 설계 §1.3). 들고 있는 슬롯과 교체 시간만 다루는 순수 로직.
    // TODO(GDD-?) Q1: 1 = Primary1, 2 = Primary2, 3 = Secondary. 이미 든 슬롯·빈 슬롯 키는 무시. 교체 시간 동안 사격·조준·재장전·T 불가(컨트롤러).
    // TODO(GDD-?) Q2: 레이드 시작 = Primary1 → Primary2 → Secondary 중 첫 비지 않은 슬롯(모두 비면 Primary1, 비무장). 든 슬롯은 저장하지 않는다.
    public sealed class WeaponSwitchModel
    {
        // 키 1/2/3 순서
        private static readonly EquipSlot[] Order = { EquipSlot.Primary1, EquipSlot.Primary2, EquipSlot.Secondary };

        private float remaining;

        public float SwitchTimeSec { get; }
        public EquipSlot ActiveSlot { get; private set; } = EquipSlot.Primary1;
        public bool IsSwitching => remaining > 0f;
        // 교체 진행 0~1 (교체 중이 아니면 1)
        public float Progress => SwitchTimeSec > 0f && remaining > 0f ? 1f - remaining / SwitchTimeSec : 1f;

        public WeaponSwitchModel(float switchTimeSec)
        {
            SwitchTimeSec = Math.Max(0f, switchTimeSec);
        }

        public static int SlotCount => Order.Length;

        // 키 번호 0~2 → 슬롯
        public static EquipSlot SlotForKey(int index) => Order[index];

        public static bool IsWeaponSlot(EquipSlot slot) =>
            slot == EquipSlot.Primary1 || slot == EquipSlot.Primary2 || slot == EquipSlot.Secondary;

        // 레이드 시작: 첫 비지 않은 슬롯. 교체 시간 없음.
        public void PickInitial(Func<EquipSlot, bool> hasWeapon)
        {
            remaining = 0f;
            ActiveSlot = EquipSlot.Primary1;
            if (hasWeapon == null)
                return;
            for (int i = 0; i < Order.Length; i++)
            {
                if (hasWeapon(Order[i]))
                {
                    ActiveSlot = Order[i];
                    return;
                }
            }
        }

        // 키 입력. 무기 슬롯이 아니거나, 이미 든 슬롯이거나, 빈 슬롯이면 false(무시). 성공하면 교체 시간 시작.
        // 교체 중 다른 슬롯 키 = 그 슬롯으로 다시 교체(시간 처음부터).
        public bool Request(EquipSlot slot, Func<EquipSlot, bool> hasWeapon)
        {
            if (!IsWeaponSlot(slot) || slot == ActiveSlot)
                return false;
            if (hasWeapon == null || !hasWeapon(slot))
                return false;
            ActiveSlot = slot;
            BeginSwitch();
            return true;
        }

        // 든 슬롯에 새 무기가 들어왔을 때도 교체 시간을 적용한다 (설계 §1.3).
        public void BeginSwitch()
        {
            remaining = SwitchTimeSec;
        }

        // 교체 시간이 끝난 Tick에 한 번 true.
        public bool Tick(float dt)
        {
            if (remaining <= 0f)
                return false;
            remaining -= dt;
            if (remaining > 0f)
                return false;
            remaining = 0f;
            return true;
        }
    }
}
