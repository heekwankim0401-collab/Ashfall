using UnityEngine;

namespace Ashfall.Raid
{
    // 탈출 구역 위치 (§8.2). 반경·조건은 RaidConfigSO.extractions에서 id로 찾는다 (M4 설계 §4.2).
    // 판정은 RaidSession(ExtractionRules.IsInZone)이 위치로 한다 — 트리거 콜라이더 없음 (TODO(GDD-?) Q8).
    public sealed class ExtractionZone : MonoBehaviour
    {
        [Tooltip("\"EXT-01\" — RaidConfigSO.extractions[].id와 같아야 한다")]
        [SerializeField] private string id;

        public string Id => id;

#if UNITY_EDITOR
        // 씬 뷰 확인용 (반경은 같은 씬의 RaidController.config에서 찾는다)
        private void OnDrawGizmos()
        {
            float radius = 1f;
            var controller = FindFirstObjectByType<RaidController>();
            if (controller != null && controller.Config != null && controller.Config.extractions != null)
                foreach (ExtractionPointConfig e in controller.Config.extractions)
                    if (e.id == id)
                        radius = e.radiusM;
            Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.8f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
#endif
    }
}
