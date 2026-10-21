using System.Collections.Generic;
using Ashfall.Core;
using UnityEngine;

namespace Ashfall.Weapons
{
    // 날아가는 탄 전체를 구조체 목록으로 들고 FixedUpdate마다 한 스텝씩 움직인다.
    // 스텝 사이 구간을 레이캐스트해 맞으면 IDamageable에 피해를 준다. 탄마다 GameObject를 만들지 않는다.
    public sealed class ProjectileSystem : MonoBehaviour
    {
        private struct Projectile
        {
            public Vector3 Origin;
            public Vector3 Position;
            public Vector3 Velocity;
            public float Age;
            public float MaxAge;
            public float Damage;
            public int Penetration;
            public Transform ShooterRoot;
        }

        [SerializeField] private LayerMask hitMask = ~0;
        [SerializeField] private bool drawDebugTrails = true;

        private readonly List<Projectile> live = new List<Projectile>();

        public int LiveCount => live.Count;

        public void Fire(Vector3 origin, Vector3 direction, AmmoDefinitionSO ammo, Transform shooterRoot)
        {
            live.Add(new Projectile
            {
                Origin = origin,
                Position = origin,
                Velocity = direction.normalized * ammo.velocity,
                MaxAge = ammo.maxFlightTimeSec,
                Damage = ammo.damage,
                Penetration = ammo.penetration,
                ShooterRoot = shooterRoot,
            });
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            Vector3 gravity = Physics.gravity;

            for (int i = live.Count - 1; i >= 0; i--)
            {
                Projectile p = live[i];
                Vector3 from = p.Position;
                Ballistics.Step(ref p.Position, ref p.Velocity, gravity, dt);
                p.Age += dt;

                Vector3 segment = p.Position - from;
                if (drawDebugTrails)
                    Debug.DrawLine(from, p.Position, Color.yellow, 1f);

                if (TryHit(from, segment, p, out RaycastHit hit))
                {
                    var target = hit.collider.GetComponentInParent<IDamageable>();
                    target?.ApplyDamage(new DamageInfo(p.Damage, p.Penetration, hit.point, segment.normalized,
                        Vector3.Distance(p.Origin, hit.point)));
                    live.RemoveAt(i);
                    continue;
                }

                if (p.Age >= p.MaxAge)
                    live.RemoveAt(i);
                else
                    live[i] = p;
            }
        }

        private bool TryHit(Vector3 from, Vector3 segment, in Projectile p, out RaycastHit hit)
        {
            float length = segment.magnitude;
            if (length > 0f && Physics.Raycast(from, segment / length, out hit, length, hitMask, QueryTriggerInteraction.Ignore))
            {
                // 쏜 사람 자신은 맞지 않는다.
                if (p.ShooterRoot == null || hit.collider.transform.root != p.ShooterRoot)
                    return true;
            }
            hit = default;
            return false;
        }
    }
}
