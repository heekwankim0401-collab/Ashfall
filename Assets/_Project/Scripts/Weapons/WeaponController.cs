using System.Linq;
using Ashfall.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Ashfall.Weapons
{
    // 손에 든 무기 하나. 카메라 아래에 두고, 카메라 정면으로 쏜다.
    // RULE-04: 탄수는 화면에 표시하지 않는다. RULE-07: 크로스헤어 없음, 조준(ADS) 시에만 퍼짐 0.
    public sealed class WeaponController : MonoBehaviour
    {
        [SerializeField] private WeaponDefinitionSO weapon;
        [SerializeField] private AmmoDefinitionSO ammo;
        [SerializeField] private MagazineDefinitionSO magazine;
        // TODO(M2): 인벤토리·전술조끼에서 탄창을 받는다. 지금은 시작 시 꽉 찬 탄창을 이 수만큼 지급한다.
        [SerializeField] private int startingMagazines;

        [SerializeField] private Transform muzzle;
        [SerializeField] private Transform viewModel;
        [SerializeField] private Vector3 hipLocalPosition;
        [SerializeField] private Vector3 adsLocalPosition;
        [SerializeField] private ProjectileSystem projectiles;

        private InputAction fire, aim, reload;
        private IRecoilReceiver recoilReceiver;
        private Transform shooterRoot;
        private readonly System.Random random = new System.Random();

        public WeaponModel Model { get; private set; }
        // 0 = 지향 사격, 1 = 완전 조준
        public float AimProgress { get; private set; }

        private void Awake()
        {
            if (magazine.ammo != weapon.ammo || ammo.type != weapon.ammo)
                Debug.LogWarning($"[WeaponController] {weapon.id}: 탄창·탄약 종류가 무기와 다르다.", this);

            Model = new WeaponModel(weapon, Magazine.Full(magazine),
                Enumerable.Range(0, Mathf.Max(0, startingMagazines - 1)).Select(_ => Magazine.Full(magazine)));
            Model.MagazineDropped += m => Debug.Log($"[WeaponController] 빠른 재장전: 탄창 버림 (남은 탄 {m.Rounds})");

            recoilReceiver = GetComponentInParent<IRecoilReceiver>();
            shooterRoot = transform.root;
            if (projectiles == null)
                projectiles = FindFirstObjectByType<ProjectileSystem>();

            var map = InputSystem.actions.FindActionMap("Player", throwIfNotFound: true);
            fire = map.FindAction("Fire", true);
            aim = map.FindAction("Aim", true);
            reload = map.FindAction("Reload", true);
            map.Enable();
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            float aimTarget = aim.IsPressed() ? 1f : 0f;
            AimProgress = weapon.adsTime > 0f
                ? Mathf.MoveTowards(AimProgress, aimTarget, dt / weapon.adsTime)
                : aimTarget;
            if (viewModel != null)
                viewModel.localPosition = Vector3.Lerp(hipLocalPosition, adsLocalPosition, AimProgress);

            if (reload.WasPressedThisFrame())
                Model.PressReload();
            Model.Tick(dt);

            if (Model.TryFire(Time.time, fire.IsPressed()))
                Shoot();
        }

        private void Shoot()
        {
            float spread = Mathf.Lerp(weapon.hipSpreadDeg, 0f, AimProgress);
            Vector3 direction = Ballistics.ApplySpread(transform.forward, spread, random);
            Vector3 origin = muzzle != null ? muzzle.position : transform.position;

            for (int i = 0; i < Mathf.Max(1, ammo.pelletCount); i++)
                projectiles.Fire(origin, direction, ammo, shooterRoot);

            Vector2 kick = Ballistics.RecoilKick(weapon.recoilV, weapon.recoilH, random);
            recoilReceiver?.AddRecoil(kick.x, kick.y);
        }
    }
}
