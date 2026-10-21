using UnityEngine;

namespace Ashfall.Raid
{
    // 지도에 표시할 구역 (§8.1 POI, RULE-06 지도, TODO(GDD-?) Q11). 위치 = transform.
    // RaidController가 Awake에서 모아 IRaidInfo.PointsOfInterest로 준다.
    public sealed class PointOfInterest : MonoBehaviour
    {
        [Tooltip("\"POI-HWY\" 등 (CrimsonBasinLayout)")]
        [SerializeField] private string id;
        [SerializeField] private string displayName;

        public string Id => id;
        public string DisplayName => displayName;
    }
}
