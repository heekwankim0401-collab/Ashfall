using UnityEngine;

namespace Ashfall.AI
{
    // 머리 위 상태 색 표시 (그레이박스 확인용, M6에서 AiConfigSO.showStateIndicator로 끔).
    // 상태가 바뀔 때만 MaterialPropertyBlock으로 색을 바꾼다(재질 복제·할당 없음).
    [RequireComponent(typeof(EnemyAgent))]
    [DisallowMultipleComponent]
    public sealed class EnemyDebugView : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        [SerializeField] private Renderer indicator;
        [Tooltip("EnemyState 순서: Idle, Patrol, Suspicious, Alert, Combat, Search, Dead")]
        [SerializeField] private Color[] stateColors =
        {
            new Color(0.5f, 0.5f, 0.5f),   // Idle 회색
            Color.white,                   // Patrol 흰색
            new Color(1f, 0.9f, 0.1f),     // Suspicious 노랑
            new Color(1f, 0.5f, 0f),       // Alert 주황
            new Color(0.9f, 0.1f, 0.1f),   // Combat 빨강
            new Color(0.2f, 0.4f, 1f),     // Search 파랑
            Color.black,                   // Dead
        };

        private EnemyAgent agent;
        private MaterialPropertyBlock block;
        private int shown = -1;

        private void Awake()
        {
            agent = GetComponent<EnemyAgent>();
            block = new MaterialPropertyBlock();
        }

        private void Start()
        {
            if (indicator != null && agent.Config != null && !agent.Config.showStateIndicator)
                indicator.enabled = false;
        }

        private void LateUpdate()
        {
            if (indicator == null || !indicator.enabled)
                return;

            EnemyState state = agent.IsDead ? EnemyState.Dead : agent.State;
            int index = (int)state;
            if (index == shown)
                return;
            shown = index;

            if (state == EnemyState.Dead)
            {
                indicator.enabled = false;
                return;
            }
            Color c = stateColors != null && index < stateColors.Length ? stateColors[index] : Color.magenta;
            block.SetColor(BaseColorId, c);
            block.SetColor(ColorId, c);
            indicator.SetPropertyBlock(block);
        }
    }
}
