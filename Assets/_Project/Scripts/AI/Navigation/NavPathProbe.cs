using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Ashfall.AI
{
    // NavMesh 연결성 확인용 경로 질의 (맵 밀도 §14.2 — 에디터 배치 확인·테스트 공용, 게임 로직은 쓰지 않는다).
    // 긴 경로는 A* 노드 한도에서 PathPartial이 될 수 있다(나무 줄기 구멍으로 NavMesh가 촘촘해진 레이드 씬). 이 함수는 부분 경로 끝에서 이어 찾아
    // "걸어서 닿는가"(연결성)만 증명한다 — 런타임 NavMeshAgent는 부분 경로 끝에서 멈추므로 적이 쓰는 경로는 SingleQueryComplete로 확인한다.
    // 끝이 목표에 0.5m 이상 가까워지지 않으면(진짜로 끊긴 섬) 실패.
    public static class NavPathProbe
    {
        public const int MaxContinuations = 12;

        // continued = 이어 찾은 횟수(0 = 한 번에 완성), length·corners = 이어 붙인 경로, status = 마지막 질의 상태
        public static bool TryConnected(Vector3 from, Vector3 to, NavMeshPath path, out float length, out int continued,
            List<Vector3> corners = null)
        {
            length = 0f;
            continued = 0;
            Vector3 start = from;
            float best = float.MaxValue;
            for (int i = 0; i <= MaxContinuations; i++)
            {
                if (!NavMesh.CalculatePath(start, to, NavMesh.AllAreas, path))
                    return false;
                Vector3[] c = path.corners;
                for (int k = 1; k < c.Length; k++)
                    length += Vector3.Distance(c[k - 1], c[k]);
                if (corners != null)
                    corners.AddRange(corners.Count > 0 && c.Length > 0 ? new ArraySegment<Vector3>(c, 1, c.Length - 1) : (IEnumerable<Vector3>)c);
                if (path.status == NavMeshPathStatus.PathComplete)
                    return true;
                if (c.Length == 0)
                    return false;
                Vector3 end = c[c.Length - 1];
                float remaining = Vector3.Distance(end, to);
                if (remaining >= best - 0.5f || Vector3.Distance(end, start) < 1f)
                    return false;
                best = remaining;
                start = end;
                continued++;
            }
            return false;
        }

        public static bool TryConnected(Vector3 from, Vector3 to, NavMeshPath path) => TryConnected(from, to, path, out _, out _);

        // 한 번의 질의로 완성되는가 (NavMeshAgent가 실제로 받는 경로 — 적 순찰·근거리 이동 확인용)
        public static bool SingleQueryComplete(Vector3 from, Vector3 to, NavMeshPath path, out float length)
        {
            length = 0f;
            if (!NavMesh.CalculatePath(from, to, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
                return false;
            Vector3[] c = path.corners;
            for (int k = 1; k < c.Length; k++)
                length += Vector3.Distance(c[k - 1], c[k]);
            return true;
        }
    }
}
