using System.Linq;
using Ashfall.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Ashfall.Weapons
{
    // 손에 든 무기 하나. 카메라 아래에 두고, 카메라 정면으로 쏜다.
    // RULE-04: 탄수는 화면에 표시하지 않는다(T로 대략치 확인). RULE-07: 크로스헤어 없음, 조준(ADS) 시에만 퍼짐 0.
    // M2:
    // - 인벤토리가 있으면 Primary1 장착 무기(IInventoryService)와 IMagazineSource(조끼·주머니)를 쓴다.
    //   없으면(M1 씬) 직렬화된 탄창 수만큼 지급한다 (LocalMagazineSource).
    // - Primary1만 따른다 (설계 D11). TODO(M7): 무기 교체 1/2/3.
    // - 버린 탄창 → ItemDropRequested, 화면 열림·사망 → 사격·조준·R·T 무시, 체력 → 조준 흔들림.
    // M3: 피해는 DamageRules(무기 > 탄약), 쏠 때마다 NoiseEmitted(Gunshot) 발행.
    // M4: RaidEnded → 진행 중 재장전 취소, 이후 재장전 진행·입력 정지 (사망과 같은 처리).
    public sealed class WeaponController : MonoBehaviour
    {
        [SerializeField] private WeaponDefinitionSO weapon;
        [SerializeField] private AmmoDefinitionSO ammo;
        // 인벤토리가 없는 씬(M1)용: 시작 시 꽉 찬 탄창을 이 수만큼 지급한다 (1개 장착 + 나머지 예비).
        [SerializeField] private MagazineDefinitionSO magazine;
        [SerializeField] private int startingMagazines;
        // RULE-04·조준 흔들림 수치. 없으면(M1 씬) T와 흔들림이 꺼진다.
        [SerializeField] private WeaponHandlingConfigSO handling;

        [SerializeField] private Transform muzzle;
        [SerializeField] private Transform viewModel;
        [SerializeField] private Vector3 hipLocalPosition;
        [SerializeField] private Vector3 adsLocalPosition;
        [SerializeField] private ProjectileSystem projectiles;

        private InputAction fire, aim, reload, checkMagazine;
        private IRecoilReceiver recoilReceiver;
        private Transform shooterRoot;
        private readonly System.Random random = new System.Random();
        private MagazineCheckModel magazineCheck;
        private IMagazineSource source;
        private bool usesInventory;
        private bool screenOpen;
        private bool dead;
        // M4 리뷰 수정: 레이드가 끝남(RaidEnded) — 사망과 같이 재장전 진행·입력을 멈춘다 (결과 스냅샷 이후 탄창 교환 금지, RULE-01)
        private bool raidOver;
        private float swayMultiplier = 1f;
        private int swaySeed;
        private Quaternion baseLocalRotation;

        // 현재 들고 있는 무기. null = 비무장 (Primary1이 비었거나 다른 무기)
        public WeaponModel Model { get; private set; }
        public bool IsArmed => Model != null;
        // 인벤토리 없는 씬에서 쓰는 예비 탄창 목록 (인벤토리가 있으면 null)
        public LocalMagazineSource MagazineSource { get; private set; }
        public MagazineCheckModel MagazineCheck => magazineCheck;
        // 0 = 지향 사격, 1 = 완전 조준
        public float AimProgress { get; private set; }

        private bool InputBlocked => screenOpen || dead || raidOver;

        private void Awake()
        {
            if (weapon == null)
            {
                Debug.LogError("[WeaponController] weapon이 비어 있다.", this);
                enabled = false;
                return;
            }
            if ((magazine != null && magazine.ammo != weapon.ammo) || (ammo != null && ammo.type != weapon.ammo))
                Debug.LogWarning($"[WeaponController] {weapon.id}: 탄창·탄약 종류가 무기와 다르다.", this);

            if (handling != null)
                magazineCheck = new MagazineCheckModel(handling);
            else
                Debug.LogWarning("[WeaponController] handling(WeaponHandlingConfig)이 없어 탄창 확인(T)·조준 흔들림을 끈다.", this);

            recoilReceiver = GetComponentInParent<IRecoilReceiver>();
            shooterRoot = transform.root;
            baseLocalRotation = transform.localRotation;
            swaySeed = random.Next(0, 1000);
            if (projectiles == null)
                projectiles = FindFirstObjectByType<ProjectileSystem>();

            var map = InputSystem.actions.FindActionMap("Player", throwIfNotFound: true);
            fire = map.FindAction("Fire", true);
            aim = map.FindAction("Aim", true);
            reload = map.FindAction("Reload", true);
            checkMagazine = map.FindAction("CheckMagazine", true);
            map.Enable();
        }

        private void OnEnable()
        {
            if (GameManager.Instance == null)
                return;
            var events = GameManager.Instance.Events;
            events.Subscribe<EquipmentChanged>(OnEquipmentChanged);
            events.Subscribe<PlayerConditionChanged>(OnConditionChanged);
            events.Subscribe<ScreenStateChanged>(OnScreenStateChanged);
            events.Subscribe<PlayerDied>(OnPlayerDied);
            events.Subscribe<RaidEnded>(OnRaidEnded);
        }

        private void OnDisable()
        {
            if (GameManager.Instance == null)
                return;
            var events = GameManager.Instance.Events;
            events.Unsubscribe<EquipmentChanged>(OnEquipmentChanged);
            events.Unsubscribe<PlayerConditionChanged>(OnConditionChanged);
            events.Unsubscribe<ScreenStateChanged>(OnScreenStateChanged);
            events.Unsubscribe<PlayerDied>(OnPlayerDied);
            events.Unsubscribe<RaidEnded>(OnRaidEnded);
        }

        // 서비스 소비는 Start 이후 (제공자는 Awake/OnEnable에서 등록한다).
        private void Start()
        {
            if (TryResolveInventory(out IInventoryService inventory))
            {
                if (Model == null)
                    Bind(inventory.GetEquipped(EquipSlot.Primary1));
                return;
            }

            // 인벤토리 없는 씬 (M1): 직렬화된 탄창 수만큼 지급
            if (magazine == null)
            {
                Debug.LogWarning("[WeaponController] 인벤토리도 magazine도 없어 비무장으로 시작한다.", this);
                SetArmedVisual(false);
                return;
            }
            MagazineSource = new LocalMagazineSource(
                Enumerable.Range(0, Mathf.Max(0, startingMagazines - 1)).Select(_ => Magazine.Full(magazine)));
            source = MagazineSource;
            Bind(new WeaponItem(weapon, Magazine.Full(magazine)));
        }

        // 인벤토리 서비스가 있으면 탄창 공급원도 거기서 받는다. 한 번 찾으면 계속 쓴다.
        private bool TryResolveInventory(out IInventoryService inventory)
        {
            inventory = null;
            if (GameManager.Instance == null || !GameManager.Instance.Services.TryGet(out inventory))
                return false;

            if (!usesInventory)
            {
                usesInventory = true;
                MagazineSource = null;
                if (GameManager.Instance.Services.TryGet(out IMagazineSource inventorySource))
                {
                    source = inventorySource;
                }
                else
                {
                    Debug.LogWarning("[WeaponController] IMagazineSource가 등록되지 않아 예비 탄창 없이 쓴다.", this);
                    source = new LocalMagazineSource(null);
                }
            }
            return true;
        }

        // 같은 정의의 WeaponItem이면 들고, 아니면 비무장.
        private void Bind(ItemInstance item)
        {
            if (Model != null && ReferenceEquals(Model.Item, item))
                return;

            Unbind();
            if (item is WeaponItem weaponItem && weaponItem.Definition == weapon && source != null)
            {
                Model = new WeaponModel(weaponItem, source);
                Model.MagazineDropped += OnMagazineDropped;
                SetArmedVisual(true);
            }
            else
            {
                if (item is WeaponItem other && other.Definition != weapon)
                    Debug.Log($"[WeaponController] {other.Definition.id}는 이 컨트롤러({weapon.id})가 다루지 않는다. 비무장. TODO(M7): 무기 교체");
                SetArmedVisual(false);
            }
        }

        // 슬롯에서 빠질 때 진행 중 재장전·탄창 확인은 버린다.
        private void Unbind()
        {
            if (Model == null)
                return;
            Model.CancelReload();
            Model.MagazineDropped -= OnMagazineDropped;
            Model = null;
            magazineCheck?.Cancel();
            AimProgress = 0f;
        }

        private void SetArmedVisual(bool armed)
        {
            if (viewModel != null)
                viewModel.gameObject.SetActive(armed);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            bool canAct = Model != null && !InputBlocked;

            float aimTarget = canAct && aim.IsPressed() ? 1f : 0f;
            AimProgress = weapon.adsTime > 0f
                ? Mathf.MoveTowards(AimProgress, aimTarget, dt / weapon.adsTime)
                : aimTarget;
            if (viewModel != null)
                viewModel.localPosition = Vector3.Lerp(hipLocalPosition, adsLocalPosition, AimProgress);

            if (canAct && reload.WasPressedThisFrame())
            {
                magazineCheck?.Cancel();
                Model.PressReload();
            }
            if (canAct && magazineCheck != null && checkMagazine.WasPressedThisFrame()
                && magazineCheck.TryStart(Model.IsReloading))
                Publish(new MagazineCheckStarted());

            // 사망·레이드 종료 후에는 재장전 진행을 멈춘다 (인벤토리에서 탄창을 꺼내지 않게).
            if (!dead && !raidOver)
                Model?.Tick(dt);

            bool trigger = canAct && fire.IsPressed();
            if (trigger)
                magazineCheck?.Cancel();

            if (Model != null && magazineCheck != null && magazineCheck.Tick(dt))
                Publish(new MagazineCheckCompleted(MagazineCheckModel.Classify(Model.Current, handling)));

            if (Model != null && Model.TryFire(Time.time, trigger))
                Shoot();

            ApplySway();
        }

        // 무기 루트를 돌려 뷰모델과 사격 방향(transform.forward)이 같이 흔들린다 (설계 D12).
        private void ApplySway()
        {
            if (handling == null)
                return;
            Vector2 offset = AimSway.Offset(Time.time, handling.aimSwayBaseDeg, swayMultiplier,
                handling.aimSwayFrequencyHz, swaySeed);
            transform.localRotation = baseLocalRotation * Quaternion.Euler(-offset.y, offset.x, 0f);
        }

        private void Shoot()
        {
            float spread = Mathf.Lerp(weapon.hipSpreadDeg, 0f, AimProgress);
            Vector3 direction = Ballistics.ApplySpread(transform.forward, spread, random);
            Vector3 origin = muzzle != null ? muzzle.position : transform.position;

            // 피해 = 무기 값(> 0) 또는 탄약 값 (M3 DamageRules, §5.2 무기별 피해 — 적과 같은 규칙)
            if (projectiles != null && ammo != null)
            {
                float damage = DamageRules.ProjectileDamage(weapon, ammo);
                for (int i = 0; i < Mathf.Max(1, ammo.pelletCount); i++)
                    projectiles.Fire(origin, direction, ammo, damage, shooterRoot);
            }

            // 사격 소음 → AI 청각 (M3, 발당 1회)
            if (WeaponNoise.TryCreateShot(weapon, origin, out NoiseEmitted noise))
                Publish(noise);

            Vector2 kick = Ballistics.RecoilKick(weapon.recoilV, weapon.recoilH, random);
            recoilReceiver?.AddRecoil(kick.x, kick.y);
        }

        // 빠른 재장전 또는 회수 공간 없음 → 바닥으로 (Inventory가 WorldItem을 만든다)
        private void OnMagazineDropped(Magazine dropped)
        {
            Vector3 position = muzzle != null ? muzzle.position : transform.position;
            float speed = handling != null ? handling.magazineDropSpeed : 0f;
            Vector3 velocity = -transform.up * speed;
            Debug.Log($"[WeaponController] 탄창 버림 {dropped}");
            Publish(new ItemDropRequested(dropped, position, velocity));
        }

        private void OnEquipmentChanged(EquipmentChanged evt)
        {
            if (evt.Slot != EquipSlot.Primary1)
                return;
            TryResolveInventory(out _);
            Bind(evt.Item);
        }

        private void OnConditionChanged(PlayerConditionChanged evt) => swayMultiplier = evt.Modifiers.AimSwayMultiplier;

        private void OnScreenStateChanged(ScreenStateChanged evt)
        {
            screenOpen = evt.Screen != ScreenKind.None;
            if (screenOpen)
                magazineCheck?.Cancel();
        }

        private void OnPlayerDied(PlayerDied evt)
        {
            dead = true;
            Model?.CancelReload();   // 진행 중 재장전이 사망 후 완료되어 인벤토리를 건드리지 않게
            magazineCheck?.Cancel();
        }

        // M4 리뷰 수정: 탈출 대기 중 시작한 재장전이 종료 스냅샷 뒤에 끝나 탄창이 이중·누락되지 않게 취소하고 이후 Tick을 멈춘다.
        private void OnRaidEnded(RaidEnded evt)
        {
            raidOver = true;
            Model?.CancelReload();
            magazineCheck?.Cancel();
        }

        private static void Publish<T>(in T evt) where T : struct, IGameEvent
        {
            if (GameManager.Instance != null)
                GameManager.Instance.Events.Publish(evt);
        }
    }
}
