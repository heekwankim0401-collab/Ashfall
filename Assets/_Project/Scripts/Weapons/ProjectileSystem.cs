using System.Collections.Generic;
using System.Diagnostics;
using Ashfall.Core;
using Unity.Profiling;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Ashfall.Weapons
{
    // 날아가는 탄 전체를 구조체 목록으로 들고 FixedUpdate마다 한 스텝씩 움직인다.
    // 스텝 사이 구간을 레이캐스트해 맞으면 IDamageable에 피해를 준다. 탄마다 GameObject를 만들지 않는다.
    // M3: IProjectileService로 등록(AI 사격), 스텝 시간을 ProfilerMarker와 Stopwatch로 잰다(§15 M3 성능 판정).
    public sealed class ProjectileSystem : MonoBehaviour, IProjectileService
    {
        private struct Projectile
        {
            public Vector3 Origin;
            public Vector3 Position;
            public Vector3 Velocity;
            public float Age;
            public float MaxAge;
            public float Damage;
            public float Drag;   // AmmoDefinitionSO.dragPerM (정조준·탄도 AB-Q3)
            public int Penetration;
            public Transform ShooterRoot;
        }

        private static readonly ProfilerMarker StepMarker = new ProfilerMarker("Ashfall.Projectiles.Step");

        [SerializeField] private LayerMask hitMask = ~0;
        [SerializeField] private bool drawDebugTrails = true;

        private readonly List<Projectile> live = new List<Projectile>();
        private double totalStepMs;

        public int LiveCount => live.Count;

        // ── 측정 (AI의 TickStats와 같은 의미: 마지막·누적 평균·최대). 배치모드 테스트가 Profiler 없이 읽는다. ──
        public double LastStepMs { get; private set; }
        public double AverageStepMs => StepSamples > 0 ? totalStepMs / StepSamples : 0d;
        public double MaxStepMs { get; private set; }
        public int StepSamples { get; private set; }

        public void ResetStats()
        {
            LastStepMs = 0d;
            MaxStepMs = 0d;
            totalStepMs = 0d;
            StepSamples = 0;
        }

        // M3: IProjectileService로 등록해 AI가 같은 탄도·피격 처리를 쓰게 한다 (설계 D1).
        private void OnEnable()
        {
            if (GameManager.Instance != null)
                GameManager.Instance.Services.Register<IProjectileService>(this);
        }

        private void OnDisable()
        {
            if (GameManager.Instance != null)
                GameManager.Instance.Services.Unregister<IProjectileService>(this);
        }

        // 탄약 피해로 쏜다 (무기 없이 쏘는 터렛·테스트, M1 호환).
        public void Fire(Vector3 origin, Vector3 direction, AmmoDefinitionSO ammo, Transform shooterRoot) =>
            Fire(origin, direction, ammo, ammo.damage, shooterRoot);

        // damage = DamageRules.ProjectileDamage(weapon, ammo). 관통력·탄속·비행 시간은 탄약에서 읽는다.
        public void Fire(Vector3 origin, Vector3 direction, AmmoDefinitionSO ammo, float damage, Transform shooterRoot)
        {
            live.Add(new Projectile
            {
                Origin = origin,
                Position = origin,
                Velocity = direction.normalized * ammo.velocity,
                MaxAge = ammo.maxFlightTimeSec,
                Damage = damage,
                Drag = ammo.dragPerM,
                Penetration = ammo.penetration,
                ShooterRoot = shooterRoot,
            });
        }

        private void FixedUpdate()
        {
            long start = Stopwatch.GetTimestamp();
            using (StepMarker.Auto())
                Step(Time.fixedDeltaTime);
            RecordStep((Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency);
        }

        private void RecordStep(double ms)
        {
            LastStepMs = ms;
            totalStepMs += ms;
            StepSamples++;
            if (ms > MaxStepMs)
                MaxStepMs = ms;
        }

        private void Step(float dt)
        {
            Vector3 gravity = Physics.gravity;

            for (int i = live.Count - 1; i >= 0; i--)
            {
                Projectile p = live[i];
                Vector3 from = p.Position;
                Ballistics.Step(ref p.Position, ref p.Velocity, gravity, p.Drag, dt);
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
