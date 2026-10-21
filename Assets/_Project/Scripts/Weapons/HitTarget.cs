using Ashfall.Core;
using UnityEngine;

namespace Ashfall.Weapons
{
    // M1 그레이박스용 표적. 맞으면 잠깐 색이 바뀌고 콘솔에 피해·거리를 남긴다.
    public sealed class HitTarget : MonoBehaviour, IDamageable
    {
        [SerializeField] private Color hitColor = Color.red;
        [SerializeField] private float flashSec;

        private Renderer cachedRenderer;
        private Color baseColor;
        private float flashUntil;

        public int HitCount { get; private set; }
        public float TotalDamage { get; private set; }
        public DamageInfo LastHit { get; private set; }

        private void Awake()
        {
            cachedRenderer = GetComponentInChildren<Renderer>();
            if (cachedRenderer != null)
                baseColor = cachedRenderer.material.color;
        }

        public void ApplyDamage(in DamageInfo info)
        {
            HitCount++;
            TotalDamage += info.Amount;
            LastHit = info;
            flashUntil = Time.time + flashSec;
            Debug.Log($"[HitTarget] {name} 피격 #{HitCount}: 피해 {info.Amount}, 거리 {info.DistanceM:F1}m, 지점 {info.Point}");
        }

        private void Update()
        {
            if (cachedRenderer != null)
                cachedRenderer.material.color = Time.time < flashUntil ? hitColor : baseColor;
        }
    }
}
