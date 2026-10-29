using System.Collections;
using System.Collections.Generic;
using Ashfall.AI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ashfall.Tests.PlayMode
{
    // 맵 밀도 리뷰 M2·L(TryPath): PhysicsLineOfSight의 "가장 가까운 충돌 먼저" 동등성, NavPathProbe의 끊긴 섬 회귀.
    // 빈 공간(원점에서 멀리)에 프리미티브를 만들어 쓰고 지운다.
    public class LineOfSightProbeTests
    {
        private static readonly Vector3 Origin = new Vector3(5000f, 500f, 5000f);
        private readonly List<GameObject> made = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in made)
                if (go != null)
                    Object.Destroy(go);
            made.Clear();
        }

        private GameObject Box(string name, Vector3 local, Vector3 scale, Transform parent = null, bool trigger = false)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = Origin + local;
            go.transform.localScale = scale;
            go.GetComponent<Collider>().isTrigger = trigger;
            if (parent == null)
                made.Add(go);
            return go;
        }

        private static Vector3 At(float z) => Origin + new Vector3(0f, 0f, z);

        [UnityTest]
        public IEnumerator IsBlocked_And_CanSee_NearestFirst_Equivalence()
        {
            var los = new PhysicsLineOfSight();
            var self = new GameObject("Self").transform;
            self.position = Origin;
            made.Add(self.gameObject);
            var target = new GameObject("Target").transform;
            target.position = At(20f);
            made.Add(target.gameObject);
            Box("SelfShield", new Vector3(0f, 0f, 2f), new Vector3(2f, 2f, 0.3f), self);       // 가장 가까운 충돌 = 무시 대상(자기 몸)
            Box("TargetBody", new Vector3(0f, 0f, 20f), new Vector3(1f, 2f, 1f), target);
            Physics.SyncTransforms();
            yield return null;

            // 가장 가까운 것이 무시 대상 + 그 뒤 아무것도 없음 → 안 막힘 / 보임
            Assert.IsFalse(los.IsBlocked(Origin, At(19f), self, target), "무시 대상만 → 안 막힘");
            Assert.IsTrue(los.CanSee(Origin, At(20f), target, self), "무시 대상 뒤 표적 → 보임");

            // 트리거만 → 안 막힘
            Box("Trigger", new Vector3(0f, 0f, 8f), new Vector3(3f, 3f, 0.3f), null, trigger: true);
            Physics.SyncTransforms();
            Assert.IsFalse(los.IsBlocked(Origin, At(19f), self, target), "트리거만 → 안 막힘");
            Assert.IsTrue(los.CanSee(Origin, At(20f), target, self), "트리거 뒤 표적 → 보임");

            // 가장 가까운 것이 무시 대상 + 뒤에 벽 → 막힘 / 안 보임
            Box("Wall", new Vector3(0f, 0f, 10f), new Vector3(3f, 3f, 0.3f));
            Physics.SyncTransforms();
            Assert.IsTrue(los.IsBlocked(Origin, At(19f), self, target), "무시 대상 뒤 벽 → 막힘");
            Assert.IsFalse(los.CanSee(Origin, At(20f), target, self), "벽 뒤 표적 → 안 보임");
            // 무시 대상 없이: 가장 가까운 것이 벽 → 막힘(빠른 경로)
            Assert.IsTrue(los.IsBlocked(At(5f), At(19f), null, target), "가장 가까운 벽 → 막힘");
        }

        [UnityTest]
        public IEnumerator ManyHits_NearestBlockerRecognized()
        {
            var los = new PhysicsLineOfSight();
            var target = new GameObject("Target").transform;
            target.position = At(30f);
            made.Add(target.gameObject);
            Box("TargetBody", new Vector3(0f, 0f, 30f), new Vector3(1f, 2f, 1f), target);
            // 눈과 표적 사이 나무 10그루 + 표적 = 충돌 11개(전체 수집 버퍼 8 초과) — 가장 가까운 나무를 놓치면 안 된다
            for (int i = 0; i < 10; i++)
                Box($"Tree_{i}", new Vector3(0f, 0f, 3f + i * 2f), new Vector3(0.3f, 4f, 0.3f));
            Physics.SyncTransforms();
            yield return null;
            Assert.IsFalse(los.CanSee(Origin, At(30f), target, null), "가장 가까운 나무가 가린다 → 안 보임");
            Assert.IsTrue(los.IsBlocked(Origin, At(29f), null, target), "충돌 9개 이상 → 막힘");
            foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                if (t.name.StartsWith("Tree_"))
                    Object.Destroy(t.gameObject);
            yield return null;
            Physics.SyncTransforms();
            Assert.IsTrue(los.CanSee(Origin, At(30f), target, null), "나무를 치우면 보임");
            Assert.IsFalse(los.IsBlocked(Origin, At(29f), null, target), "나무를 치우면 안 막힘");
        }

        // 작은 NavMesh 두 덩이(사이 20m 틈) — 섬 사이는 이어 찾기로도 실패, 한 덩이 안은 한 번에 완성(이어 찾기 0)
        [UnityTest]
        public IEnumerator NavPathProbe_DisconnectedIslands_Fail_SameIsland_Complete()
        {
            var sources = new List<NavMeshBuildSource>();
            foreach (float x in new[] { -25f, 25f })
                sources.Add(new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Box,
                    size = new Vector3(30f, 1f, 30f),
                    transform = Matrix4x4.TRS(Origin + new Vector3(x, 0f, 0f), Quaternion.identity, Vector3.one),
                    area = 0,
                });
            NavMeshBuildSettings settings = NavMesh.GetSettingsByIndex(0);
            NavMeshData data = NavMeshBuilder.BuildNavMeshData(settings, sources, new Bounds(Origin, new Vector3(120f, 20f, 60f)), Vector3.zero, Quaternion.identity);
            NavMeshDataInstance instance = NavMesh.AddNavMeshData(data);
            try
            {
                yield return null;
                Vector3 a = Origin + new Vector3(-30f, 0.5f, 0f), a2 = Origin + new Vector3(-18f, 0.5f, 5f), b = Origin + new Vector3(30f, 0.5f, 0f);
                Assert.IsTrue(NavMesh.SamplePosition(a, out NavMeshHit ha, 2f, NavMesh.AllAreas), "섬 A");
                Assert.IsTrue(NavMesh.SamplePosition(a2, out NavMeshHit ha2, 2f, NavMesh.AllAreas), "섬 A 안 다른 점");
                Assert.IsTrue(NavMesh.SamplePosition(b, out NavMeshHit hb, 2f, NavMesh.AllAreas), "섬 B");
                var path = new NavMeshPath();
                Assert.IsTrue(NavPathProbe.TryConnected(ha.position, ha2.position, path, out _, out int continued), "같은 섬 → 연결");
                Assert.AreEqual(0, continued, "한 번에 완성");
                Assert.IsTrue(NavPathProbe.SingleQueryComplete(ha.position, ha2.position, path, out float length));
                Assert.Greater(length, 1f);
                Assert.IsFalse(NavPathProbe.TryConnected(ha.position, hb.position, path, out _, out _), "끊긴 섬 → 이어 찾아도 실패");
                Assert.IsFalse(NavPathProbe.SingleQueryComplete(ha.position, hb.position, path, out _));
            }
            finally
            {
                NavMesh.RemoveNavMeshData(instance);
                Object.Destroy(data);
            }
        }
    }
}
