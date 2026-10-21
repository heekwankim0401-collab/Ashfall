using UnityEngine;

namespace Ashfall.Core
{
    // AI가 보는 플레이어 (§7 감각). Player가 구현해 GameManager.Services에 등록한다.
    // 감각 주기마다 동기로 읽으므로 이벤트가 아니라 조회 인터페이스다 (M3 설계 D1).
    public interface IPlayerTarget
    {
        Transform Root { get; }            // 시야 레이가 맞힌 콜라이더의 transform.root와 비교
        Vector3 Position { get; }          // 발
        Vector3 EyePosition { get; }       // 카메라(눈) 위치
        Vector3 CenterPosition { get; }    // 캡슐 중심 (조준점·두 번째 시야 레이)
        Vector3 Velocity { get; }          // 명중 오차(이동 표적)
        TargetPosture Posture { get; }
        bool IsDead { get; }
    }
}
