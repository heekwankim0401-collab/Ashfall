using Ashfall.Core;
using UnityEngine;

namespace Ashfall.Weapons
{
    // M2 그레이박스 피격 확인용 터렛. 대상이 구역(바닥 표시) 안에 서 있으면 일정 간격으로 한 발씩 쏜다.
    // 조준 높이는 대상 발밑 기준 무작위 (머리~다리가 고르게 맞도록). 구역 밖이면 쏘지 않는다.
    // 수치는 씬 생성 메뉴(M2SceneSetup)가 넣는다. 테스트 픽스처라 SO로 빼지 않는다.
    public sealed class TestTurret : MonoBehaviour
    {
        [SerializeField] private ProjectileSystem projectiles;
        [SerializeField] private AmmoDefinitionSO ammo;
        [Tooltip("쏠 대상 (플레이어 루트, 발 위치)")]
        [SerializeField] private Transform target;
        [Tooltip("구역 중심 (바닥 표시). 비우면 터렛 위치")]
        [SerializeField] private Transform zoneCenter;
        [Tooltip("구역 반경 (m, 수평 거리)")]
        [SerializeField] private float zoneRadiusM;
        [Tooltip("발사 간격 (초)")]
        [SerializeField] private float intervalSec;
        [Tooltip("조준 높이 범위 (m, 대상 발 기준)")]
        [SerializeField] private Vector2 aimHeightRangeM;
        [SerializeField] private Transform muzzle;

        private readonly System.Random random = new System.Random();
        private float cooldown;

        public int ShotsFired { get; private set; }

        private void Awake()
        {
            if (projectiles == null)
                projectiles = FindFirstObjectByType<ProjectileSystem>();
            cooldown = intervalSec;
        }

        public bool TargetInZone()
        {
            if (target == null)
                return false;
            Vector3 center = zoneCenter != null ? zoneCenter.position : transform.position;
            Vector3 d = target.position - center;
            d.y = 0f;
            return d.sqrMagnitude <= zoneRadiusM * zoneRadiusM;
        }

        private void Update()
        {
            if (projectiles == null || ammo == null || !TargetInZone())
            {
                cooldown = intervalSec;
                return;
            }

            cooldown -= Time.deltaTime;
            if (cooldown > 0f)
                return;
            cooldown = intervalSec;

            float height = Mathf.Lerp(aimHeightRangeM.x, aimHeightRangeM.y, (float)random.NextDouble());
            Vector3 origin = muzzle != null ? muzzle.position : transform.position;
            Vector3 aimPoint = target.position + Vector3.up * height;
            projectiles.Fire(origin, aimPoint - origin, ammo, transform.root);
            ShotsFired++;
        }
    }
}
