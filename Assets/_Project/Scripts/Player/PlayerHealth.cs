using Ashfall.Core;
using UnityEngine;

namespace Ashfall.Player
{
    // SYS-HP-01 플레이어 체력의 얇은 브리지. 규칙은 BodyHealthModel·MedicalUseModel에 있다.
    // - IDamageable: 캡슐 피격 지점으로 부위를 판정해 ApplyHit (설계 D3). DamageInfo에 부위 힌트가 있으면 그 부위(M7 변이체 근접)
    // - IBodyStatusView 등록 (RULE-05 H 화면), 모델 변화 → 버스 이벤트
    // - MedicalUseRequested → 사용 시간 → MedicalUseCompleted
    // - M4: RaidEnded → 진행 중 의약품 사용 취소, 이후 사용 요청 거부
    // - M6: 재 폭풍 방사능 누적(RadiationDoseModel, §8.3 +1/s × 강도). 매 프레임 IAshStormInfo를 조회(레이드 밖에는 없음 = 0).
    //       사망·레이드 종료 뒤 멈춘다.
    // - M7(7C-2a, 설계 §1.5): 수분·에너지(SurvivalModel) 감소, 수분 0 → 머리를 뺀 파괴되지 않은 부위마다 −0.5 HP/s(Q33, 흉부 160초).
    //       방사능률 = 폭풍 률 × (방독면 ? 0 : 1) + 구역 률(IRadiationField, 발 위치)(Q38). 방독면 = 헬멧 슬롯 정의의 blocksStormRadiation,
    //       매 프레임 IInventoryService 조회(할당 없음, 레이드 밖·인벤토리 없음 = 미착용). 선량 ≥ 100 → 최대 HP × 0.8(BodyHealthModel.SyncRadiation, Q35).
    //       등급 변화는 BodyHealthModel.StatusChanged → PlayerBodyStatusChanged. 수치는 상시 HUD용 IPlayerHudSource(PlayerHudReadout)로만 낸다(RULE-05 2026-10-28 B안 개정, H 화면 IBodyStatusView는 등급만).
    // - MVP 마무리 F4 (§5.3): 피격 부위를 덮는 헬멧·방탄복(IInventoryService.GetEquipped, ArmorItem)이 있으면 Core ArmorRules로 관통 판정.
    //       방어구 전용 난수(확률 0·1이면 뽑지 않음). 피격음·이벤트·HUD 없음(MF-Q24).
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerHealth : MonoBehaviour, IDamageable
    {
        [SerializeField] private PlayerConfigSO config;

        // 엔진 상수(게임 수치 아님): PhysX PxControllerDesc::scaleCoeff 기본값. Unity CharacterController의 쿼리용 캡슐은
        // height·radius의 0.8배다(4단계 PlayMode 측정: 높이 1.8·반지름 0.3 → 1.44·0.24). 이걸 무시하면 머리 구간(≥0.87)이
        // 실제 캡슐 꼭대기 몇 cm만 남아 사실상 맞지 않는다.
        private const float PhysicsActorScale = 0.8f;

        private CharacterController controller;
        private MedicalUseModel medical;
        // M4 리뷰 수정: 레이드 종료 후 의약품 사용 없음 (결과 스냅샷 이후 소지품·상태가 바뀌지 않게)
        private bool raidOver;
        private readonly RadiationDoseModel radiation = new RadiationDoseModel();
        // MVP 마무리 F4: 방어구 전용 난수(BodyHealthModel 상태이상 굴림 순서를 밀지 않게)·§5.3 수치(config에서 Awake 1회)
        // 리뷰 수정 2: Guid seed — Awake의 상태이상 new System.Random()과 같은 프레임에 만들어져도 seed가 겹치지 않게.
        // 리뷰 수정 6: 수치는 Awake에서 한 번 묶어 두고 피격 경로에서는 SO를 다시 읽지 않는다(적 AiConfig와 같은 방식).
        //  그래서 플레이 중 인스펙터에서 바꾼 값은 이 컴포넌트가 다시 만들어질 때(씬 재진입)부터 반영된다.
        private readonly System.Random armorRandom = ArmorRules.CreateArmorRandom(0);
        private ArmorRuleParams armorParams;

        public BodyHealthModel Body { get; private set; }
        public MedicalUseModel Medical => medical;
        // M6: 이번 레이드의 방사능 누적량 (STAT-RAD). M7: 피폭 효과·해독제
        public RadiationDoseModel Radiation => radiation;
        public float RadiationDose => radiation.Dose;
        // M7: 이번 레이드의 수분·에너지 (STAT-DEHY·HUNG, 이월 없음 Q32)
        public SurvivalModel Survival { get; private set; }
        // M7: 지난 프레임에 적용한 방사능률·방독면 착용 여부 (테스트·디버그 확인용, UI에 내지 않는다)
        public float LastRadiationRatePerSec { get; private set; }
        public bool GasMaskWorn { get; private set; }
        public bool IsDead => Body != null && Body.IsDead;
        // 상시 HUD(B안) 수치 창구 (IPlayerHudSource). RULE-05 개정으로 HUD에만 수치를 낸다(H 화면은 등급만 그대로).
        public PlayerHudReadout HudReadout { get; private set; }

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            if (config == null)
            {
                Debug.LogError("[PlayerHealth] PlayerConfigSO가 연결되지 않았다.", this);
                enabled = false;
                return;
            }
            armorParams = config.ArmorRuleParams;
            Body = new BodyHealthModel(config, new System.Random());
            Survival = new SurvivalModel(config);
            Body.AttachSurvival(Survival, radiation);
            medical = new MedicalUseModel(Body);
            HudReadout = new PlayerHudReadout(Body, Survival, radiation, config, GetComponent<PlayerMotor>());
            Body.StatusChanged += OnStatusChanged;
            Body.ModifiersChanged += OnModifiersChanged;
            Body.Died += OnDied;
        }

        private void OnEnable()
        {
            if (Body == null || GameManager.Instance == null)
                return;
            GameManager.Instance.Services.Register<IBodyStatusView>(Body);
            GameManager.Instance.Services.Register<IPlayerHudSource>(HudReadout);
            GameManager.Instance.Events.Subscribe<MedicalUseRequested>(OnMedicalUseRequested);
            GameManager.Instance.Events.Subscribe<RaidEnded>(OnRaidEnded);
        }

        private void OnDisable()
        {
            if (Body == null || GameManager.Instance == null)
                return;
            GameManager.Instance.Services.Unregister<IBodyStatusView>(Body);
            GameManager.Instance.Services.Unregister<IPlayerHudSource>(HudReadout);
            GameManager.Instance.Events.Unsubscribe<MedicalUseRequested>(OnMedicalUseRequested);
            GameManager.Instance.Events.Unsubscribe<RaidEnded>(OnRaidEnded);
        }

        public void ApplyDamage(in DamageInfo info)
        {
            // config 누락으로 꺼진 경우(Body 없음)에도 IDamageable로는 계속 불린다.
            if (Body == null || IsDead)
                return;

            // 발 = y 0 기준 로컬 좌표로 바꿔 캡슐 높이 비율로 부위를 고른다.
            // 레이캐스트가 맞히는 것은 PhysX 캐릭터 컨트롤러의 kinematic actor이고, 이 캡슐은 컨트롤러 크기의
            // PhysicsActorScale배다. 중심 기준으로 되돌려 컨트롤러 크기(height·radius) 기준 좌표로 판정한다.
            // M7 (7B 리뷰 1): 부위 힌트가 있으면(변이체 근접) 지점 판정 대신 그 부위. 피해·굴림 규칙은 같다(ApplyHit).
            BodyPart part;
            if (info.HasPartHint)
            {
                part = info.PartHint;
            }
            else
            {
                Vector3 local = (transform.InverseTransformPoint(info.Point) - controller.center) / PhysicsActorScale;
                local.y += controller.height * 0.5f;
                part = BodyPartResolver.Resolve(local, controller.height, controller.radius, config);
            }
            // MVP 마무리 F4 §5.3: 그 부위를 덮는 헬멧·방탄복이 있으면 관통 판정 → 관통 = 그대로, 비관통 = 둔탁 30% + 방어구 내구도 감소.
            // 감소된 피해로 기존 ApplyHit(상태이상 굴림·복부 배율 그대로, MF-Q19). 근접(관통 0)도 같은 공식(MF-Q20).
            // TODO(GDD-?) MF-Q25: 방어구 판정은 처음 맞은 부위로 한 번만. 파괴된 팔다리를 맞혀 흉부·복부로 넘어가는 70% 분산 몫
            //  (BodyHealthModel 분산)은 방탄복을 거치지 않는다(가장 단순, 리뷰 1 (b) — ArmorRulesTests가 고정).
            ArmorItem armor = FindEquippedArmor(part);
            ArmorHitResult armorHit = ArmorRules.Absorb(armor, armorParams, info.Penetration, info.Amount, armorRandom);
            Body.ApplyHit(part, armorHit.BodyDamage);
            // 상시 HUD(B안) 피격 방향 붉은 가장자리 (§12)
            bool hasDirection = PlayerHudReadout.TryRelativeYawDeg(transform.forward, info.Direction, out float yawDeg);
            Publish(new PlayerHitReceived(yawDeg, hasDirection));
            LogHit(part, armorHit.BodyDamage, armor != null, armorHit.Penetrated);
        }

        // 헬멧·방탄복 슬롯에서 부위를 막는 방어구 (인벤토리 없음 = 없음). 매 피격 조회, 할당 없음.
        private static ArmorItem FindEquippedArmor(BodyPart part)
        {
            if (GameManager.Instance == null || !GameManager.Instance.Services.TryGet(out IInventoryService inventory))
                return null;
            return ArmorRules.FindCovering(inventory.GetEquipped(EquipSlot.Helmet), inventory.GetEquipped(EquipSlot.BodyArmor), part);
        }

        // RULE-05: 피해 수치를 콘솔에 상시 찍지 않는다. 디버그할 때만 스크립팅 심볼 ASHFALL_DEBUG_DAMAGE를 켠다.
        [System.Diagnostics.Conditional("ASHFALL_DEBUG_DAMAGE")]
        private void LogHit(BodyPart part, float amount, bool armored, bool penetrated) =>
            Debug.Log($"[PlayerHealth] 피격 {part}: 피해 {amount}" + (armored ? (penetrated ? " (방어구 관통)" : " (방어구 비관통)") : string.Empty), this);

        private void Update()
        {
            float dt = Time.deltaTime;
            Body.Tick(dt);

            MedicalItem item = medical.Current;
            BodyPart target = medical.Target;
            if (medical.Tick(dt))
                Publish(new MedicalUseCompleted(item, target));

            TickSurvival(dt);
            TickRadiation(dt);
        }

        // M7 수분·에너지 (§4.3). 수분 0이면 머리를 뺀 파괴되지 않은 부위마다 dehydratedHpPerSec (Q33). 사망·레이드 종료 뒤 멈춘다.
        private void TickSurvival(float dt)
        {
            if (IsDead || raidOver || !(dt > 0f))
                return;
            Survival.Tick(dt, Body.IsDestroyed(BodyPart.Stomach));
            if (Survival.IsDehydrated)
                Body.ApplySystemicDamage(config.dehydratedHpPerSec * dt);
        }

        // M6 재 폭풍 방사능 (§8.3, Q27) + M7 방독면·구역 (Q38). 서비스는 매 프레임 조회한다(사전 조회, 할당 없음) — 늦게 등록·해제돼도 맞다.
        private void TickRadiation(float dt)
        {
            if (IsDead || raidOver || GameManager.Instance == null)
                return;
            ServiceRegistry services = GameManager.Instance.Services;
            GasMaskWorn = IsGasMaskEquipped(services);
            float rate = RadiationRate(
                services.TryGet(out IAshStormInfo storm) ? storm.RadiationPerSec : 0f,
                GasMaskWorn,
                services.TryGet(out IRadiationField field) ? field.ZoneRatePerSec(transform.position) : 0f);
            LastRadiationRatePerSec = rate;
            radiation.Tick(dt, rate);
            Body.SyncRadiation();   // 피폭 → 최대 HP 배율, 방사능 등급
        }

        // 순수 규칙 (Q38): 방독면은 폭풍 몫만 막는다. 음수·NaN 률은 0으로 본다.
        public static float RadiationRate(float stormRatePerSec, bool gasMask, float zoneRatePerSec)
        {
            float storm = !gasMask && stormRatePerSec > 0f ? stormRatePerSec : 0f;
            float zone = zoneRatePerSec > 0f ? zoneRatePerSec : 0f;
            return storm + zone;
        }

        // 방독면 = 헬멧 슬롯 정의가 ArmorDefinitionSO이고 blocksStormRadiation (Q37). 인벤토리 없음 = 미착용.
        private static bool IsGasMaskEquipped(ServiceRegistry services)
        {
            if (!services.TryGet(out IInventoryService inventory))
                return false;
            ItemInstance helmet = inventory.GetEquipped(EquipSlot.Helmet);
            return helmet != null && helmet.Definition is ArmorDefinitionSO armor && armor.blocksStormRadiation;
        }

        private void OnMedicalUseRequested(MedicalUseRequested evt)
        {
            if (evt.Item == null || raidOver)
                return;

            switch (medical.TryStart(evt.Item))
            {
                case MedicalStartResult.Started:
                    Publish(new MedicalUseStarted(evt.Item, medical.Target, medical.RemainingSec));
                    break;
                case MedicalStartResult.NotNeeded:
                    Reject(evt.Item, MedicalUseFailReason.NotNeeded);
                    break;
                case MedicalStartResult.Busy:
                    Reject(evt.Item, MedicalUseFailReason.Busy);
                    break;
                case MedicalStartResult.Dead:
                    Reject(evt.Item, MedicalUseFailReason.Dead);
                    break;
                case MedicalStartResult.Depleted:
                    Reject(evt.Item, MedicalUseFailReason.Depleted);
                    break;
            }
        }

        // M4 리뷰 수정: 진행 중 사용을 효과 없이 취소 (완료 이벤트가 종료 스냅샷 뒤에 와서 소지품을 바꾸지 않게).
        // 아이템 잠금(Inventory)은 풀지 않는다 — 인벤토리도 같은 이벤트로 잠기고 씬이 곧 내려간다.
        private void OnRaidEnded(RaidEnded evt)
        {
            raidOver = true;
            medical?.Cancel();
        }

        private void Reject(MedicalItem item, MedicalUseFailReason reason)
        {
            Debug.Log($"[PlayerHealth] 의약품 사용 거부 {item}: {reason}");
            Publish(new MedicalUseRejected(item, reason));
        }

        private void OnStatusChanged() => Publish(new PlayerBodyStatusChanged());
        private void OnModifiersChanged() => Publish(new PlayerConditionChanged(Body.Modifiers));

        private void OnDied(BodyPart cause)
        {
            // MVP 마무리 F1: 사인 종류(피격·출혈·탈수)를 같이 보낸다 (Died 서명은 그대로 — 기존 단언 유지)
            Debug.Log($"[PlayerHealth] 사망: {cause} ({Body.DeathKind})");
            Publish(new PlayerDied(cause, Body.DeathKind));
        }

        private static void Publish<T>(in T evt) where T : struct, IGameEvent
        {
            if (GameManager.Instance != null)
                GameManager.Instance.Events.Publish(evt);
        }
    }
}
