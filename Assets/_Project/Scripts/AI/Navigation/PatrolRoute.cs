using System.Collections.Generic;
using UnityEngine;

namespace Ashfall.AI
{
    // 순찰 경로 (§7 순찰, M3 설계 §1.6). 웨이포인트를 순환한다. 비면 자식 순서.
    // 분대원은 모두 같은 경로를 연결하고 리더만 경로를 따른다(나머지는 대형).
    public sealed class PatrolRoute : MonoBehaviour
    {
        [SerializeField] private Transform[] waypoints;

        private List<Vector3> points;

        public IReadOnlyList<Vector3> Points
        {
            get
            {
                if (points == null)
                    Cache();
                return points;
            }
        }

        private void Awake() => Cache();

        private void Cache()
        {
            points = new List<Vector3>();
            if (waypoints != null && waypoints.Length > 0)
            {
                foreach (Transform t in waypoints)
                    if (t != null)
                        points.Add(t.position);
                return;
            }
            for (int i = 0; i < transform.childCount; i++)
                points.Add(transform.GetChild(i).position);
        }

        private void OnDrawGizmos()
        {
            int n = waypoints != null && waypoints.Length > 0 ? waypoints.Length : transform.childCount;
            if (n == 0)
                return;
            Gizmos.color = new Color(1f, 1f, 1f, 0.6f);
            for (int i = 0; i < n; i++)
            {
                Vector3 a = WaypointAt(i);
                Vector3 b = WaypointAt((i + 1) % n);
                Gizmos.DrawSphere(a + Vector3.up * 0.1f, 0.2f);
                Gizmos.DrawLine(a + Vector3.up * 0.1f, b + Vector3.up * 0.1f);
            }
        }

        private Vector3 WaypointAt(int i)
        {
            if (waypoints != null && waypoints.Length > 0)
                return waypoints[i] != null ? waypoints[i].position : transform.position;
            return transform.GetChild(i).position;
        }
    }
}
