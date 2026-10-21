using UnityEngine;

namespace Ashfall.AI
{
    // §7 레벨에 미리 배치하는 엄폐 지점 (M3 설계 §1.7). AiDirector가 시작할 때 모아 CoverIndex에 넣는다.
    // 위치 = 숨는 발 위치. transform.forward = 막아 주는 방향(엄폐물 쪽 = 위협이 있어야 할 쪽).
    [DisallowMultipleComponent]
    public sealed class CoverPoint : MonoBehaviour
    {
        [SerializeField] private CoverHeight height;
        [Tooltip("High 엄폐에서 사격하러 나오는 위치 (로컬). Low면 무시")]
        [SerializeField] private Vector3 peekLocalOffset;

        public CoverHeight Height => height;

        // 사격 발 위치 (월드). Low면 숨는 위치 그대로.
        public Vector3 PeekPosition => height == CoverHeight.Low ? transform.position : transform.TransformPoint(peekLocalOffset);

        // 막아 주는 방향 (수평 정규화)
        public Vector3 Forward
        {
            get
            {
                Vector3 f = transform.forward;
                f.y = 0f;
                return f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.forward;
            }
        }

        private void OnDrawGizmos()
        {
            Vector3 p = transform.position;
            Gizmos.color = height == CoverHeight.Low ? new Color(0.2f, 0.8f, 0.2f) : new Color(0.2f, 0.5f, 1f);
            Gizmos.DrawWireSphere(p + Vector3.up * 0.25f, 0.25f);
            Gizmos.DrawLine(p + Vector3.up * 0.25f, p + Vector3.up * 0.25f + Forward);
            if (height == CoverHeight.High)
            {
                Gizmos.color = new Color(1f, 0.6f, 0.1f);
                Gizmos.DrawLine(p + Vector3.up * 0.1f, PeekPosition + Vector3.up * 0.1f);
                Gizmos.DrawWireCube(PeekPosition + Vector3.up * 0.25f, Vector3.one * 0.3f);
            }
        }
    }
}
