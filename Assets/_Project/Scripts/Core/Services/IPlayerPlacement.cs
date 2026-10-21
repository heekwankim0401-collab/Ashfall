using UnityEngine;

namespace Ashfall.Core
{
    // 플레이어를 지정 위치에 놓는다 (레이드 시작 스폰 배치, M4 설계 §1.1). Player(PlayerMotor)가 구현·등록한다.
    // CharacterController 이동·시선은 Player 내부 일이라 Raid가 Transform을 직접 건드리지 않는다.
    public interface IPlayerPlacement
    {
        // 발 위치·수평 시선(yaw, 도). 이동 상태·발소리 누적을 초기화하고 시선 pitch는 0.
        void PlaceAt(Vector3 feetPosition, float yawDeg);
    }
}
