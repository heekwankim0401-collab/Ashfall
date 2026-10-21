using System.Collections;
using Ashfall.Core;
using Ashfall.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ashfall.Tests.PlayMode
{
    // M1 AC: 탄도 투사체 + 표적 피격
    public class ProjectileHitTests
    {
        private const float TimeoutSec = 3f;
        private GameObject root;

        [TearDown]
        public void TearDown() => Object.Destroy(root);

        private static AmmoDefinitionSO Ammo556()
        {
            var a = ScriptableObject.CreateInstance<AmmoDefinitionSO>();
            a.damage = 45f; a.velocity = 900f; a.maxFlightTimeSec = 3f; a.penetration = 3; a.pelletCount = 1;
            return a;
        }

        private HitTarget CreateTarget(Vector3 center, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.SetParent(root.transform);
            go.transform.position = center;
            go.transform.localScale = size;
            return go.AddComponent<HitTarget>();
        }

        private static IEnumerator WaitUntilHitOrTimeout(HitTarget target, ProjectileSystem system)
        {
            float deadline = Time.realtimeSinceStartup + TimeoutSec;
            while (target.HitCount == 0 && system.LiveCount > 0 && Time.realtimeSinceStartup < deadline)
                yield return new WaitForFixedUpdate();
        }

        [UnityTest]
        public IEnumerator Projectile_HitsTargetAt100m_WithAmmoDamage()
        {
            root = new GameObject("TestRoot");
            var system = root.AddComponent<ProjectileSystem>();
            var target = CreateTarget(new Vector3(0f, 0f, 100f), new Vector3(2f, 2f, 0.2f));

            system.Fire(Vector3.zero, Vector3.forward, Ammo556(), null);
            yield return WaitUntilHitOrTimeout(target, system);

            Assert.AreEqual(1, target.HitCount);
            Assert.AreEqual(45f, target.LastHit.Amount);
            Assert.AreEqual(100f, target.LastHit.DistanceM, 0.5f);
            Assert.Less(target.LastHit.Point.y, 0f, "중력으로 조준점보다 아래에 맞는다");
        }

        [UnityTest]
        public IEnumerator Projectile_DropsBelowSmallTargetAtLongRange()
        {
            // 300m에서 낙차 ≈ ½·9.81·(1/3)² ≈ 0.55m. 높이 0.4m 표적의 중심을 수평으로 겨누면 아래로 빗나간다.
            root = new GameObject("TestRoot");
            var system = root.AddComponent<ProjectileSystem>();
            var target = CreateTarget(new Vector3(0f, 0f, 300f), new Vector3(2f, 0.4f, 0.2f));

            system.Fire(Vector3.zero, Vector3.forward, Ammo556(), null);
            yield return WaitUntilHitOrTimeout(target, system);

            Assert.AreEqual(0, target.HitCount, "히트스캔이었다면 맞았을 표적");
        }
    }
}
