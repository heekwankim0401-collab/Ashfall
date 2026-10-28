using System;
using System.Collections.Generic;
using System.Linq;
using Ashfall.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Ashfall.Weapons
{
    // 손에 든 무기 하나. 카메라 아래에 두고, 카메라 정면으로 쏜다.
    // RULE-04(2026-10-28 B안 개정): 탄 수는 상시 HUD가 IWeaponHudSource(이 컴포넌트)로 읽어 보인다. T 대략치 확인은 그대로. RULE-07: 크로스헤어 없음, 조준(ADS) 시에만 퍼짐 0.
    // M2:
    // - 인벤토리가 있으면 장착 무기(IInventoryService)와 IMagazineSource(조끼·주머니)를 쓴다.
    //   없으면(M1 씬) 직렬화된 무기·탄창 수만큼 지급한다 (LocalMagazineSource).
    // - 버린 탄창 → ItemDropRequested, 화면 열림·사망 → 사격·조준·R·T 무시, 체력 → 조준 흔들림.
    // M3: 피해는 DamageRules(무기 > 탄약), 쏠 때마다 NoiseEmitted(Gunshot) 발행.
    // M4: RaidEnded → 진행 중 재장전 취소, 이후 재장전 진행·입력 정지 (사망과 같은 처리).
    // M6: 쏠 때마다 ShotFired(총구, 무기, Player) 발행 (Audio 총성 레이어).
    // M7 (SYS-WPN §5.1·§12.1, 설계 §1.3):
    // - 1/2/3 = Primary1/Primary2/Secondary 교체(WeaponSwitchModel, 교체 시간 동안 사격·조준·재장전·T 불가, 진행 중 재장전·확인·해제 취소) (Q1·Q2).
    //   M5 Q16 해소 — 장착된 어떤 무기든 든다(직렬화 weapon·ammo·magazine은 인벤토리 없는 M1 씬 전용).
    // - 무기 아이템마다 WeaponModel을 캐시(마모·고장 옵션 = handling.CreateWear) — 다시 들어도 연사 간격 상태가 이어진다.
    // - 탄약 정의 = handling.TryGetAmmo(무기 ammo) (Q3), 뷰모델 = WeaponDefinitionSO.viewModel 프리팹(처음 들 때 생성·캐시, 자식 "Muzzle" = 총구) (Q4).
    // - 산탄은 펠릿마다 퍼짐 (Q8). 튜브 무기 R = 한 발씩(WeaponModel).
    // - 고장: 고장 순간 WeaponClicked(Jammed), 빈 탄창·고장 중 새 방아쇠 WeaponClicked(DryFire), T 길게(jamClearHoldSec) = 해제 + WeaponClicked(JamCleared).
    //   고장이 아닐 때 T(누름) = 기존 RULE-04 탄창 확인 (Q9·Q10).
    // 정조준·탄도 (2026-10-28, AB-Q1·Q2, docs/design/AdsBallistics_design.md): 뷰모델 자식 "SightAnchor"가 있고 무기 정의 adsEyeReliefM > 0이면
    //   정조준 자세를 무기마다 계산해 가늠자가 화면 중앙에 오게 한다(없으면 adsLocalPosition 폴백). zeroRangeM > 0이면 영점 방향으로 쏜다(장착 시 1회 계산·캐시).
    public sealed class WeaponController : MonoBehaviour, IWeaponHudSource
    {
        [SerializeField] private WeaponDefinitionSO weapon;
        [SerializeField] private AmmoDefinitionSO ammo;
        // 인벤토리가 없는 씬(M1)용: 시작 시 꽉 찬 탄창을 이 수만큼 지급한다 (1개 장착 + 나머지 예비).
        // 튜브 무기면 튜브를 채우고 이 탄창(탄 상자)을 이 수만큼 예비로 둔다 (M7).
        [SerializeField] private MagazineDefinitionSO magazine;
        [SerializeField] private int startingMagazines;
        // RULE-04·조준 흔들림·M7 교체·고장·탄약 표 수치. 없으면(M1 씬) T·흔들림·마모가 꺼지고 교체 시간 0.
        [SerializeField] private WeaponHandlingConfigSO handling;

        [SerializeField] private Transform muzzle;
        [SerializeField] private Transform viewModel;
        [SerializeField] private Vector3 hipLocalPosition;
        // 정조준 위치 폴백: 무기 정의 adsEyeReliefM이 0이거나 뷰모델에 SightAnchor가 없을 때만 쓴다 (정조준·탄도 AB-Q1).
        [SerializeField] private Vector3 adsLocalPosition;
        [SerializeField] private ProjectileSystem projectiles;

        // 뷰모델 프리팹에서 만든 것 (무기 정의마다 하나)
        private readonly struct ViewInstance
        {
            public readonly Transform Root;
            public readonly Transform Muzzle;
            public readonly Transform Sight;   // 자식 "SightAnchor" (없으면 null — 폴백 정조준 위치)

            public ViewInstance(Transform root, Transform muzzle, Transform sight)
            {
                Root = root;
                Muzzle = muzzle;
                Sight = sight;
            }
        }

        // 정조준·탄도 (AB-Q1·Q2): 무기 정의마다 정조준 뷰모델 자세와 영점 발사 방향. 장착 시 1회 계산해 캐시(탄약이 바뀌면 다시).
        private struct AdsSolution
        {
            public AmmoDefinitionSO Ammo;
            public float FixedDt;             // 영점을 구한 Time.fixedDeltaTime (프로젝트 0.02 고정이지만 바뀌면 다시 구한다)
            public Vector3 Position;          // 정조준 뷰모델 루트 로컬 위치 (컨트롤러 기준)
            public Quaternion Rotation;       // 정조준 뷰모델 루트 로컬 회전 (가늠자 시선이 카메라 정면과 같게)
            public bool HasZero;
            public Vector3 ZeroDirection;     // 흔들림 없는 컨트롤러 로컬 발사 방향 (영점)
        }

        private InputAction fire, aim, reload, checkMagazine;
        private readonly InputAction[] slotKeys = new InputAction[3];
        private IRecoilReceiver recoilReceiver;
        private Transform shooterRoot;
        private System.Random random = new System.Random();
        private MagazineCheckModel magazineCheck;
        private WeaponSwitchModel switcher;
        private JamClearModel jamClear;
        private IMagazineSource source;
        private ILooseAmmoSource looseSource;
        private IInventoryService inventory;
        private Func<EquipSlot, bool> hasWeaponInSlot;
        private bool usesInventory;
        private bool initialized;
        private bool screenOpen;
        private bool dead;
        // M4 리뷰 수정: 레이드가 끝남(RaidEnded) — 사망과 같이 재장전 진행·입력을 멈춘다 (결과 스냅샷 이후 탄창 교환 금지, RULE-01)
        private bool raidOver;
        private float swayMultiplier = 1f;
        private int swaySeed;
        private Quaternion baseLocalRotation;

        // 무기 아이템 → 모델 (레이드 동안, 보통 3개 이하). 버린 무기 항목도 의도적으로 지우지 않는다(M7 리뷰 7A 낮음 8):
        // 다시 주워 들면 같은 모델(연사 간격·이벤트 구독)을 쓰고, 항목 수는 레이드에서 든 무기 수뿐이며 씬과 함께 사라진다. TODO(GDD-?)
        private readonly Dictionary<WeaponItem, WeaponModel> models = new Dictionary<WeaponItem, WeaponModel>();
        private readonly Dictionary<WeaponDefinitionSO, ViewInstance> views = new Dictionary<WeaponDefinitionSO, ViewInstance>();
        private readonly HashSet<AmmoType> missingAmmoWarned = new HashSet<AmmoType>();
        private readonly Dictionary<WeaponDefinitionSO, AdsSolution> adsSolutions = new Dictionary<WeaponDefinitionSO, AdsSolution>();
        private Transform activeView;
        private Transform activeMuzzle;
        private Transform activeSight;
        private AdsSolution activeAds;
        private Vector3[] pelletBuffer = new Vector3[8];

        // 현재 들고 있는 무기. null = 비무장 (든 슬롯이 비었음)
        public WeaponModel Model { get; private set; }
        public bool IsArmed => Model != null;
        // 상시 HUD(B안, RULE-04 개정) — IWeaponHudSource
        public WeaponItem CurrentWeapon => Model != null ? Model.Item : null;
        public FireMode CurrentFireMode => Model != null ? Model.FireMode : FireMode.Semi;
        // 인벤토리 없는 씬에서 쓰는 예비 탄창 목록 (인벤토리가 있으면 null)
        public LocalMagazineSource MagazineSource { get; private set; }
        public MagazineCheckModel MagazineCheck => magazineCheck;
        // 0 = 지향 사격, 1 = 완전 조준
        public float AimProgress { get; private set; }
        // M7: 든 슬롯(인벤토리 없는 씬은 항상 Primary1), 교체 중, 고장 해제 진행 0~1 (테스트용, 화면 표시 없음)
        public EquipSlot ActiveSlot => switcher != null ? switcher.ActiveSlot : EquipSlot.Primary1;
        public bool IsSwitching => switcher != null && switcher.IsSwitching;
        public float JamClearProgress => jamClear != null ? jamClear.Progress : 0f;
        // 든 무기의 탄약 정의 (없으면 null — 투사체 없이 소리만)
        public AmmoDefinitionSO CurrentAmmo { get; private set; }
        // 든 무기의 뷰모델·총구 (비무장이면 뷰모델 null)
        public Transform CurrentViewModel => activeView;
        public Vector3 MuzzlePosition => activeMuzzle != null ? activeMuzzle.position
            : muzzle != null ? muzzle.position : transform.position;
        // 정조준·탄도 (AB-Q1·Q2, 테스트·확인용): 든 뷰모델의 가늠자 기준점(없으면 null), 영점 적용 여부와 발사 방향(흔들림 없는 컨트롤러 로컬)
        public Transform CurrentSightAnchor => activeSight;
        public bool IsZeroed => activeAds.HasZero;
        public Vector3 ZeroDirectionLocal => activeAds.HasZero ? activeAds.ZeroDirection : Vector3.forward;

        private bool InputBlocked => screenOpen || dead || raidOver;

        // 코드로 만든 장면·테스트용 (AudioDirector.Configure와 같은 방식). 비활성 오브젝트에 AddComponent한 뒤 활성화 전에 부른다.
        // randomSeed: 퍼짐·반동·고장 굴림 난수 (결정적 테스트).
        public void Configure(WeaponDefinitionSO weaponDefinition, AmmoDefinitionSO ammoDefinition, MagazineDefinitionSO magazineDefinition,
            int magazines, WeaponHandlingConfigSO handlingConfig, ProjectileSystem projectileSystem, int randomSeed)
        {
            weapon = weaponDefinition;
            ammo = ammoDefinition;
            magazine = magazineDefinition;
            startingMagazines = magazines;
            handling = handlingConfig;
            projectiles = projectileSystem;
            random = new System.Random(randomSeed);
        }

        private void Awake()
        {
            if (weapon != null && ((magazine != null && magazine.ammo != weapon.ammo) || (ammo != null && ammo.type != weapon.ammo)))
                Debug.LogWarning($"[WeaponController] {weapon.id}: 탄창·탄약 종류가 무기와 다르다.", this);

            if (handling != null)
                magazineCheck = new MagazineCheckModel(handling);
            else
                Debug.LogWarning("[WeaponController] handling(WeaponHandlingConfig)이 없어 탄창 확인(T)·조준 흔들림·마모를 끈다.", this);
            switcher = new WeaponSwitchModel(handling != null ? handling.weaponSwitchTimeSec : 0f);
            jamClear = new JamClearModel(handling != null ? handling.jamClearHoldSec : 0f);
            hasWeaponInSlot = HasWeaponInSlot;

            recoilReceiver = GetComponentInParent<IRecoilReceiver>();
            shooterRoot = transform.root;
            baseLocalRotation = transform.localRotation;
            activeAds = DefaultAds();
            swaySeed = random.Next(0, 1000);
            if (projectiles == null)
                projectiles = FindFirstObjectByType<ProjectileSystem>();

            var map = InputSystem.actions.FindActionMap("Player", throwIfNotFound: true);
            fire = map.FindAction("Fire", true);
            aim = map.FindAction("Aim", true);
            reload = map.FindAction("Reload", true);
            checkMagazine = map.FindAction("CheckMagazine", true);
            for (int i = 0; i < slotKeys.Length; i++)
            {
                slotKeys[i] = map.FindAction("Weapon" + (i + 1), false);
                if (slotKeys[i] == null)
                    Debug.LogWarning($"[WeaponController] 입력 액션 Weapon{i + 1}이 없어 그 키 교체를 끈다.", this);
            }
            map.Enable();
        }

        private void OnEnable()
        {
            if (GameManager.Instance == null)
                return;
            GameManager.Instance.Services.Register<IWeaponHudSource>(this);
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
            GameManager.Instance.Services.Unregister<IWeaponHudSource>(this);
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
            if (TryResolveInventory())
            {
                if (!initialized)
                    InitializeFromInventory();
                return;
            }

            initialized = true;
            // 인벤토리 없는 씬 (M1): 직렬화된 탄창 수만큼 지급
            if (weapon == null || magazine == null)
            {
                Debug.LogWarning("[WeaponController] 인벤토리도 weapon·magazine도 없어 비무장으로 시작한다.", this);
                ShowView(null);
                return;
            }
            WeaponItem item;
            if (weapon.internalMagazine != null)
            {
                // TODO(GDD-?) Q5: 튜브 무기 = 튜브 가득 + 탄 상자(magazine) startingMagazines개
                MagazineSource = new LocalMagazineSource(
                    Enumerable.Range(0, Mathf.Max(0, startingMagazines)).Select(_ => Magazine.Full(magazine)));
                item = new WeaponItem(weapon, Magazine.Full(weapon.internalMagazine));
            }
            else
            {
                MagazineSource = new LocalMagazineSource(
                    Enumerable.Range(0, Mathf.Max(0, startingMagazines - 1)).Select(_ => Magazine.Full(magazine)));
                item = new WeaponItem(weapon, Magazine.Full(magazine));
            }
            source = MagazineSource;
            looseSource = MagazineSource;
            Equip(item, withSwitchTime: false);
        }

        // 인벤토리 서비스가 있으면 탄창·낱탄 공급원도 거기서 받는다. 한 번 찾으면 계속 쓴다.
        private bool TryResolveInventory()
        {
            if (usesInventory)
                return true;
            if (GameManager.Instance == null || !GameManager.Instance.Services.TryGet(out IInventoryService found))
                return false;

            usesInventory = true;
            inventory = found;
            MagazineSource = null;
            var services = GameManager.Instance.Services;
            if (services.TryGet(out IMagazineSource inventorySource))
            {
                source = inventorySource;
            }
            else
            {
                Debug.LogWarning("[WeaponController] IMagazineSource가 등록되지 않아 예비 탄창 없이 쓴다.", this);
                source = new LocalMagazineSource(null);
            }
            // 등록된 낱탄 공급원이 없으면 탄창 공급원이 구현할 때 그것 (WeaponModel 규칙과 같음)
            looseSource = services.TryGet(out ILooseAmmoSource loose) ? loose : source as ILooseAmmoSource;
            return true;
        }

        // Q2: 첫 비지 않은 슬롯을 교체 시간 없이 든다.
        private void InitializeFromInventory()
        {
            initialized = true;
            switcher.PickInitial(hasWeaponInSlot);
            Equip(inventory.GetEquipped(switcher.ActiveSlot) as WeaponItem, withSwitchTime: false);
        }

        private bool HasWeaponInSlot(EquipSlot slot) => inventory != null && inventory.GetEquipped(slot) is WeaponItem;

        // 무기를 든다(null = 비무장). 이미 든 아이템이면 그대로.
        private void Equip(WeaponItem item, bool withSwitchTime)
        {
            if (Model != null && ReferenceEquals(Model.Item, item))
                return;

            Unbind();
            if (item == null || source == null)
            {
                ShowView(null);
                return;
            }
            Model = GetOrCreateModel(item);
            CurrentAmmo = ResolveAmmo(item.Definition);
            ShowView(item.Definition);
            if (withSwitchTime)
                switcher.BeginSwitch();
        }

        private WeaponModel GetOrCreateModel(WeaponItem item)
        {
            if (models.TryGetValue(item, out WeaponModel model))
                return model;
            // 플레이어 무기만 마모·고장 (Q18). handling이 없으면(M1 씬) 끔.
            WeaponWear wear = handling != null ? handling.CreateWear(random) : WeaponWear.None;
            model = new WeaponModel(item, source, looseSource, wear);
            model.MagazineDropped += OnMagazineDropped;
            model.Jammed += OnJammed;
            models.Add(item, model);
            return model;
        }

        // Q3: 탄약 표 → (없으면) 직렬화 ammo가 같은 종류일 때. 둘 다 없으면 null(투사체 없음) + 경고 1회.
        private AmmoDefinitionSO ResolveAmmo(WeaponDefinitionSO definition)
        {
            if (handling != null && handling.TryGetAmmo(definition.ammo, out AmmoDefinitionSO fromTable))
                return fromTable;
            if (ammo != null && ammo.type == definition.ammo)
                return ammo;
            if (missingAmmoWarned.Add(definition.ammo))
                Debug.LogWarning($"[WeaponController] {definition.id}: 탄약 정의({definition.ammo})가 handling.ammoDefinitions에 없다. 투사체 없이 쏜다.", this);
            return null;
        }

        // 슬롯에서 빠지거나 교체할 때 진행 중 재장전·탄창 확인·고장 해제는 버린다 (탄창 그대로).
        private void Unbind()
        {
            if (Model == null)
                return;
            Model.CancelReload();
            Model = null;
            CurrentAmmo = null;
            magazineCheck?.Cancel();
            jamClear.Reset();
            AimProgress = 0f;
        }

        // Q4: 무기 정의의 뷰모델 프리팹(처음 들 때 생성·캐시), 없으면 직렬화 뷰모델. 나머지는 숨긴다.
        private void ShowView(WeaponDefinitionSO definition)
        {
            Transform view = null;
            Transform viewMuzzle = null;
            Transform viewSight = null;
            if (definition != null)
            {
                if (definition.viewModel != null)
                {
                    ViewInstance instance = GetOrCreateView(definition);
                    view = instance.Root;
                    viewMuzzle = instance.Muzzle;
                    viewSight = instance.Sight;
                }
                else
                {
                    view = viewModel;
                    viewMuzzle = muzzle;
                }
            }

            if (viewModel != null)
                viewModel.gameObject.SetActive(view != null && view == viewModel);
            foreach (KeyValuePair<WeaponDefinitionSO, ViewInstance> pair in views)
                pair.Value.Root.gameObject.SetActive(pair.Value.Root == view);

            activeView = view;
            activeMuzzle = viewMuzzle != null ? viewMuzzle : muzzle;
            activeSight = viewSight;
            // 총구는 MuzzlePosition으로만 읽는다(계층 순서로 "Muzzle"을 찾지 않는다 — M7 리뷰 7A 낮음 7).
            activeAds = definition != null ? GetOrComputeAds(definition, view, viewSight, activeMuzzle) : DefaultAds();
            ApplyViewPose();
        }

        private ViewInstance GetOrCreateView(WeaponDefinitionSO definition)
        {
            if (views.TryGetValue(definition, out ViewInstance existing) && existing.Root != null)
                return existing;
            GameObject go = Instantiate(definition.viewModel, transform, false);
            go.name = definition.viewModel.name;
            Transform root = go.transform;
            root.localPosition = hipLocalPosition;
            root.localRotation = Quaternion.identity;
            var instance = new ViewInstance(root, FindChild(root, MuzzleChildName), FindChild(root, SightAnchorChildName));
            views[definition] = instance;
            adsSolutions.Remove(definition);   // 새 뷰모델 — 정조준 자세를 다시 구한다
            return instance;
        }

        // 지향 ↔ 정조준 사이 뷰모델 자세 (AimProgress로 보간)
        private void ApplyViewPose()
        {
            if (activeView == null)
                return;
            activeView.localPosition = Vector3.Lerp(hipLocalPosition, activeAds.Position, AimProgress);
            activeView.localRotation = Quaternion.Slerp(Quaternion.identity, activeAds.Rotation, AimProgress);
        }

        private AdsSolution DefaultAds() => new AdsSolution { Position = adsLocalPosition, Rotation = Quaternion.identity };

        // 정조준·탄도 (AB-Q1·Q2). 무기 정의마다 1회(탄약이 바뀌면 다시) — 사격마다 계산하지 않는다.
        // 정조준 자세: SightAnchor + adsEyeReliefM > 0이면 앵커가 카메라 정면 축 위 eyeRelief 앞, 앵커 +z = 카메라 정면. 아니면 adsLocalPosition(폴백).
        // 영점: zeroRangeM > 0이고 탄약이 있으면, 그 정조준 자세의 총구에서 출발한 탄(같은 적분기·드래그·fixedDeltaTime)이
        //   시선 위 영점 거리 점을 지나는 방향(수평 조준 기준, BallisticZero). 못 구하면 영점 없음(카메라 정면, 이전 동작).
        // 계산은 순수 함수 AdsAlignment.Solve — 여기서는 Transform에서 값만 모은다.
        // 캐시 키 = 무기 정의 + 탄약 + fixedDeltaTime(영점 적분 dt가 실행 중 바뀌면 다시 구한다).
        private AdsSolution GetOrComputeAds(WeaponDefinitionSO definition, Transform root, Transform sight, Transform viewMuzzle)
        {
            float fixedDt = Time.fixedDeltaTime;
            if (adsSolutions.TryGetValue(definition, out AdsSolution cached) && cached.Ammo == CurrentAmmo && cached.FixedDt == fixedDt)
                return cached;

            var input = new AdsAlignment.Input
            {
                FallbackPosition = adsLocalPosition,
                EyeReliefM = definition.adsEyeReliefM,
                ZeroRangeM = CurrentAmmo != null ? definition.zeroRangeM : 0f,
                Gravity = Physics.gravity.magnitude,
                FixedDt = fixedDt,
            };
            EyeFromController(out input.EyeFromControllerPosition, out input.EyeFromControllerRotation);
            if (root != null && sight != null)
            {
                input.HasAnchor = true;
                input.AnchorInRoot = Vector3.Scale(root.localScale, root.InverseTransformPoint(sight.position));
                input.AnchorRotationInRoot = Quaternion.Inverse(root.rotation) * sight.rotation;
            }
            if (root != null && viewMuzzle != null && CurrentAmmo != null)
            {
                input.HasMuzzle = true;
                input.MuzzleInRoot = Vector3.Scale(root.localScale, root.InverseTransformPoint(viewMuzzle.position));
                input.Speed = CurrentAmmo.velocity;
                input.DragPerM = CurrentAmmo.dragPerM;
                input.MaxFlightSec = CurrentAmmo.maxFlightTimeSec;
            }

            AdsAlignment.Result result = AdsAlignment.Solve(input);
            if (result.ZeroFailed)
                Debug.LogWarning($"[WeaponController] {definition.id}: 영점 {definition.zeroRangeM}m를 구하지 못해(탄속·비행 시간 부족) 카메라 정면으로 쏜다.", this);
            var solution = new AdsSolution
            {
                Ammo = CurrentAmmo,
                FixedDt = fixedDt,
                Position = result.Root.Position,
                Rotation = result.Root.Rotation,
                HasZero = result.HasZero,
                ZeroDirection = result.ZeroDirection,
            };
            adsSolutions[definition] = solution;
            return solution;
        }

        // 눈(카메라) ← 흔들림 없는 컨트롤러 로컬 변환. 카메라가 없거나 컨트롤러 자신이면 항등. 식은 AdsAlignment.EyeFromController.
        private void EyeFromController(out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            Camera eyeCamera = GetComponentInParent<Camera>();
            Transform eye = eyeCamera != null ? eyeCamera.transform : transform.parent;
            if (eye == null || eye == transform)
                return;
            Quaternion parentRotation = transform.parent != null ? transform.parent.rotation : Quaternion.identity;
            AdsAlignment.EyeFromController(eye.position, eye.rotation, eye.lossyScale, transform.position,
                parentRotation * baseLocalRotation, out position, out rotation);
        }

        // 프리팹의 총구 자식 이름. EditorTools M7Names.MuzzleChild와 같은 값 — 바꿀 때 같이 바꿀 것
        // (런타임 어셈블리는 EditorTools를 참조할 수 없어 상수가 둘이다. M7WeaponSceneSmokeTests가 프리팹에 이 이름의 자식이 있는지 본다).
        private const string MuzzleChildName = "Muzzle";
        // 가늠자 기준점 자식 이름 (정조준·탄도 AB-Q1). EditorTools WeaponSightLayout.AnchorChild와 같은 값 — 바꿀 때 같이 바꿀 것.
        private const string SightAnchorChildName = "SightAnchor";

        private static Transform FindChild(Transform parent, string childName)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child.name == childName)
                    return child;
                Transform deeper = FindChild(child, childName);
                if (deeper != null)
                    return deeper;
            }
            return null;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            switcher.Tick(dt);

            // 1/2/3 교체 (Q1). 화면 열림·사망·레이드 종료 중에는 무시.
            if (!InputBlocked && usesInventory)
            {
                for (int i = 0; i < slotKeys.Length; i++)
                {
                    if (slotKeys[i] != null && slotKeys[i].WasPressedThisFrame())
                        RequestSwitch(WeaponSwitchModel.SlotForKey(i));
                }
            }

            bool canAct = Model != null && !InputBlocked && !switcher.IsSwitching;

            float aimTarget = canAct && aim.IsPressed() ? 1f : 0f;
            float adsTime = Model != null ? Model.Item.Definition.adsTime : 0f;
            AimProgress = adsTime > 0f
                ? Mathf.MoveTowards(AimProgress, aimTarget, dt / adsTime)
                : aimTarget;
            ApplyViewPose();

            bool jammed = Model != null && Model.IsJammed;
            if (canAct && reload.WasPressedThisFrame())
            {
                magazineCheck?.Cancel();
                Model.PressReload();   // 고장 중이면 거부 (Q9)
            }

            // T: 고장 중 = 길게 눌러 해제, 아니면 = 누름으로 탄창 확인 (RULE-04)
            if (canAct && jammed)
            {
                if (jamClear.Tick(dt, checkMagazine.IsPressed(), true) && Model.ClearJam())
                    Publish(new WeaponClicked(MuzzlePosition, WeaponClickKind.JamCleared));
            }
            else
            {
                jamClear.Reset();
                if (canAct && magazineCheck != null && checkMagazine.WasPressedThisFrame()
                    && magazineCheck.TryStart(Model.IsReloading))
                    Publish(new MagazineCheckStarted());
            }

            // 사망·레이드 종료 후에는 재장전 진행을 멈춘다 (인벤토리에서 탄창을 꺼내지 않게).
            if (!dead && !raidOver)
                Model?.Tick(dt);

            bool trigger = canAct && fire.IsPressed();
            if (trigger)
                magazineCheck?.Cancel();

            if (Model != null && magazineCheck != null && magazineCheck.Tick(dt))
                Publish(new MagazineCheckCompleted(MagazineCheckModel.Classify(Model.Current, handling)));

            // 헛격발: 고장 중이거나 빈 탄창에서 방아쇠를 새로 당김 (재장전 중 제외). 이 프레임의 고장(Jammed)과 겹치지 않게 사격 전에 본다.
            if (canAct && fire.WasPressedThisFrame() && !Model.IsReloading
                && (Model.IsJammed || Model.Current == null || Model.Current.IsEmpty))
                Publish(new WeaponClicked(MuzzlePosition, WeaponClickKind.DryFire));

            // 교체 중에도 실제 방아쇠 상태는 넘기고 발사만 막는다 — 좌클릭을 누른 채 교체가 끝나도 반자동이 새로 당긴 것으로 되지 않게 (M7 리뷰 7A 낮음 5).
            // 화면 열림·사망·레이드 종료는 기존대로 방아쇠 없음.
            bool triggerHeld = Model != null && !InputBlocked && fire.IsPressed();
            if (Model != null && Model.TryFire(Time.time, triggerHeld, allowShot: canAct))
                Shoot();

            ApplySway();
        }

        private void RequestSwitch(EquipSlot slot)
        {
            if (!switcher.Request(slot, hasWeaponInSlot))
                return;
            Equip(inventory.GetEquipped(slot) as WeaponItem, withSwitchTime: false);   // 교체 시간은 Request가 시작했다
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
            WeaponDefinitionSO definition = Model.Item.Definition;
            Vector3 origin = MuzzlePosition;
            int pellets = CurrentAmmo != null ? Mathf.Max(1, CurrentAmmo.pelletCount) : 1;
            if (pelletBuffer.Length < pellets)
                pelletBuffer = new Vector3[pellets];
            // 정조준·탄도 AB-Q2: 영점이 있으면 총열 방향 = 컨트롤러 회전(흔들림 포함, 설계 D12) × 영점 방향, 없으면 카메라 정면(이전 동작)
            Vector3 aimForward = activeAds.HasZero ? transform.rotation * activeAds.ZeroDirection : transform.forward;
            // Q8: 지향 퍼짐(조준 시 0)은 묶음 방향 1회, 펠릿마다 pelletSpreadDeg
            Ballistics.ShotDirections(aimForward, definition.hipSpreadDeg, AimProgress, definition.pelletSpreadDeg,
                pellets, random, pelletBuffer);

            // 피해 = 무기 값(> 0) 또는 탄약 값 (M3 DamageRules, §5.2 무기별 피해 — 적과 같은 규칙)
            if (projectiles != null && CurrentAmmo != null)
            {
                float damage = DamageRules.ProjectileDamage(definition, CurrentAmmo);
                for (int i = 0; i < pellets; i++)
                    projectiles.Fire(origin, pelletBuffer[i], CurrentAmmo, damage, shooterRoot);
            }

            // 사격 소음 → AI 청각 (M3, 발당 1회)
            if (WeaponNoise.TryCreateShot(definition, origin, out NoiseEmitted noise))
                Publish(noise);
            // M6 총성 소리 (§11 거리 레이어, Q14) → Audio. 발당 1회(펠릿 수 무관). AI 청각은 위 NoiseEmitted만 본다.
            Publish(new ShotFired(origin, definition, SoundSource.Player));

            Vector2 kick = Ballistics.RecoilKick(definition.recoilV, definition.recoilH, random);
            recoilReceiver?.AddRecoil(kick.x, kick.y);
        }

        // 고장 순간 (탄 소모 없음) → Audio
        private void OnJammed() => Publish(new WeaponClicked(MuzzlePosition, WeaponClickKind.Jammed));

        // 빠른 재장전 또는 회수 공간 없음 → 바닥으로 (Inventory가 WorldItem을 만든다)
        private void OnMagazineDropped(Magazine dropped)
        {
            Vector3 position = MuzzlePosition;
            float speed = handling != null ? handling.magazineDropSpeed : 0f;
            Vector3 velocity = -transform.up * speed;
            Debug.Log($"[WeaponController] 탄창 버림 {dropped}");
            Publish(new ItemDropRequested(dropped, position, velocity));
        }

        // 든 슬롯의 무기가 빠지면 비무장, 들어오면 교체 시간을 두고 든다. 다른 슬롯은 키로 바꿀 때 읽는다.
        private void OnEquipmentChanged(EquipmentChanged evt)
        {
            if (!WeaponSwitchModel.IsWeaponSlot(evt.Slot) || switcher == null)
                return;
            if (!TryResolveInventory())
                return;
            // 인벤토리 Start가 이 컨트롤러 Start보다 먼저 장비를 알리면 그때 시작 무기를 고른다 (교체 시간 없음).
            if (!initialized)
            {
                InitializeFromInventory();
                return;
            }
            if (evt.Slot != switcher.ActiveSlot)
                return;
            Equip(evt.Item as WeaponItem, withSwitchTime: true);
        }

        private void OnConditionChanged(PlayerConditionChanged evt) => swayMultiplier = evt.Modifiers.AimSwayMultiplier;

        private void OnScreenStateChanged(ScreenStateChanged evt)
        {
            screenOpen = evt.Screen != ScreenKind.None;
            if (screenOpen)
            {
                magazineCheck?.Cancel();
                jamClear?.Reset();
            }
        }

        private void OnPlayerDied(PlayerDied evt)
        {
            dead = true;
            Model?.CancelReload();   // 진행 중 재장전이 사망 후 완료되어 인벤토리를 건드리지 않게
            magazineCheck?.Cancel();
            jamClear?.Reset();
        }

        // M4 리뷰 수정: 탈출 대기 중 시작한 재장전이 종료 스냅샷 뒤에 끝나 탄창이 이중·누락되지 않게 취소하고 이후 Tick을 멈춘다.
        private void OnRaidEnded(RaidEnded evt)
        {
            raidOver = true;
            Model?.CancelReload();
            magazineCheck?.Cancel();
            jamClear?.Reset();
        }

        private static void Publish<T>(in T evt) where T : struct, IGameEvent
        {
            if (GameManager.Instance != null)
                GameManager.Instance.Events.Publish(evt);
        }
    }
}
