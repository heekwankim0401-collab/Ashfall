using Ashfall.Core;
using UnityEngine;

namespace Ashfall.Raid
{
    // EXT-02 전원 스위치 (§8.2, TODO(GDD-?) Q5: 송전탑 언덕, F 1회로 레이드 끝까지 켜짐).
    // Core IWorldInteractable — Inventory의 F 레이캐스트(PlayerInventoryBehaviour)가 콜라이더(자신 또는 자식)에서 찾는다.
    // RaidController가 Start에서 세션을 묶는다(Bind). 묶이기 전·켠 뒤·레이드 종료 후에는 프롬프트가 없다.
    public sealed class PowerSwitch : MonoBehaviour, IWorldInteractable
    {
        [Tooltip("RaidConfigSO.extractions[].requiredSwitchId와 같아야 한다 (\"SW-PWR\")")]
        [SerializeField] private string switchId;
        [Tooltip("F 상호작용 프롬프트 이름")]
        [SerializeField] private string displayName;

        private RaidSession session;

        public string SwitchId => switchId;
        public string DisplayName => displayName;
        public bool IsOn => session != null && session.IsSwitchOn(switchId);

        // ── IWorldInteractable ──
        public InteractionKind Kind => InteractionKind.Activate;
        public string TargetName => displayName;
        public bool IsAvailable => session != null && !session.IsOver && !session.IsSwitchOn(switchId);

        public void Interact()
        {
            if (IsAvailable)
                session.ActivateSwitch(switchId);
        }

        // RaidController가 세션을 만든 뒤 부른다 (null = 풀기).
        public void Bind(RaidSession raidSession) => session = raidSession;
    }
}
