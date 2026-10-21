using UnityEngine;

namespace Ashfall.Raid
{
    // 플레이어 스폰 지점 (TODO(GDD-?) Q2: 고정 3곳 중 무작위). 위치 = 발, 시선 = transform의 yaw.
    // RaidController가 Start에서 하나를 골라 IPlayerPlacement로 플레이어를 옮긴다.
    public sealed class PlayerSpawnPoint : MonoBehaviour
    {
        [Tooltip("\"SP-W\" — RaidConfigSO.spawnRules[].spawnId와 같아야 한다")]
        [SerializeField] private string id;

        public string Id => id;
        public Vector3 FeetPosition => transform.position;
        public float YawDeg => transform.eulerAngles.y;

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.3f, 0.6f, 1f, 0.9f);
            Vector3 p = transform.position;
            Gizmos.DrawWireCube(p + Vector3.up, new Vector3(0.6f, 2f, 0.6f));
            Gizmos.DrawLine(p + Vector3.up * 1.6f, p + Vector3.up * 1.6f + transform.forward * 2f);
        }
#endif
    }
}
