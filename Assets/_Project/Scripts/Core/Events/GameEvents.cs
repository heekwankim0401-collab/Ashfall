using UnityEngine;

namespace Ashfall.Core
{
    // M2~M6 기능 간 이벤트 (M2 설계 §3.4, M3 설계 §3, M4 설계 §4.1, M5 설계 §4.1, M6 설계 §1.2). 전부 readonly struct, GameManager.Events로 발행한다.
    // 이벤트는 기능 사이에서만 쓴다. 같은 어셈블리 안은 직접 참조한다.

    // ── Inventory → 다른 기능 ──────────────────────────────

    // 인벤토리 내용이 바뀜 → UI가 IInventoryService를 다시 읽어 그린다.
    public readonly struct InventoryChanged : IGameEvent { }

    // 장착 슬롯이 바뀜 (Item null = 비움) → Weapons가 Primary1 무기를 바인딩한다.
    public readonly struct EquipmentChanged : IGameEvent
    {
        public readonly EquipSlot Slot;
        public readonly ItemInstance Item;

        public EquipmentChanged(EquipSlot slot, ItemInstance item)
        {
            Slot = slot;
            Item = item;
        }
    }

    // 소지 무게 합계 (§4.1 무게 패널티) → Player
    public readonly struct CarriedWeightChanged : IGameEvent
    {
        public readonly float TotalKg;

        public CarriedWeightChanged(float totalKg)
        {
            TotalKg = totalKg;
        }
    }

    // 의약품 사용 요청 → Player가 사용 시간·대상 부위를 처리한다.
    public readonly struct MedicalUseRequested : IGameEvent
    {
        public readonly MedicalItem Item;

        public MedicalUseRequested(MedicalItem item)
        {
            Item = item;
        }
    }

    // F 상호작용 대상이 바뀜 → UI HUD 프롬프트
    public readonly struct InteractionPromptChanged : IGameEvent
    {
        public readonly bool Visible;
        public readonly InteractionKind Kind;
        public readonly string TargetName;

        public InteractionPromptChanged(bool visible, InteractionKind kind, string targetName)
        {
            Visible = visible;
            Kind = kind;
            TargetName = targetName;
        }
    }

    // 루트 컨테이너 열림/닫힘 → UI가 인벤토리 화면(루트 패널)을 연다.
    public readonly struct LootSessionChanged : IGameEvent
    {
        public readonly bool Open;

        public LootSessionChanged(bool open)
        {
            Open = open;
        }
    }

    // ── Weapons → 다른 기능 ────────────────────────────────

    // 바닥에 아이템을 떨어뜨려 달라 (빠른 재장전 탄창, 회수 공간 없음) → Inventory가 WorldItem 생성
    public readonly struct ItemDropRequested : IGameEvent
    {
        public readonly ItemInstance Item;
        public readonly Vector3 Position;
        public readonly Vector3 Velocity;

        public ItemDropRequested(ItemInstance item, Vector3 position, Vector3 velocity)
        {
            Item = item;
            Position = position;
            Velocity = velocity;
        }
    }

    // RULE-04 탄창 확인 시작 → UI가 이전 결과 문구를 지운다.
    public readonly struct MagazineCheckStarted : IGameEvent { }

    // RULE-04 탄창 확인 결과 → UI 문구
    public readonly struct MagazineCheckCompleted : IGameEvent
    {
        public readonly MagazineFillLevel Level;

        public MagazineCheckCompleted(MagazineFillLevel level)
        {
            Level = level;
        }
    }

    // ── Player → 다른 기능 ─────────────────────────────────

    // 의약품 사용 시작 → Inventory가 사용 중 아이템을 잠근다.
    public readonly struct MedicalUseStarted : IGameEvent
    {
        public readonly MedicalItem Item;
        public readonly BodyPart Target;
        public readonly float DurationSec;

        public MedicalUseStarted(MedicalItem item, BodyPart target, float durationSec)
        {
            Item = item;
            Target = target;
            DurationSec = durationSec;
        }
    }

    // 의약품 사용 완료 → Inventory가 사용 횟수를 차감·제거한다.
    public readonly struct MedicalUseCompleted : IGameEvent
    {
        public readonly MedicalItem Item;
        public readonly BodyPart Target;

        public MedicalUseCompleted(MedicalItem item, BodyPart target)
        {
            Item = item;
            Target = target;
        }
    }

    // 의약품 사용 거부 (로그만, UI 표시 없음)
    public readonly struct MedicalUseRejected : IGameEvent
    {
        public readonly MedicalItem Item;
        public readonly MedicalUseFailReason Reason;

        public MedicalUseRejected(MedicalItem item, MedicalUseFailReason reason)
        {
            Item = item;
            Reason = reason;
        }
    }

    // 부위 등급·상태이상·사망이 바뀜 → UI가 IBodyStatusView를 다시 읽는다.
    // 매 프레임 출혈 피해로는 발행하지 않는다 (등급이 바뀔 때만).
    public readonly struct PlayerBodyStatusChanged : IGameEvent { }

    // 이동·조준 패널티가 바뀜 → Weapons(조준 흔들림). 값이 바뀔 때만 발행.
    public readonly struct PlayerConditionChanged : IGameEvent
    {
        public readonly ConditionModifiers Modifiers;

        public PlayerConditionChanged(ConditionModifiers modifiers)
        {
            Modifiers = modifiers;
        }
    }

    // 플레이어 사망 (머리·흉부 0) → 조작 정지, 화면 닫기
    public readonly struct PlayerDied : IGameEvent
    {
        public readonly BodyPart Cause;

        public PlayerDied(BodyPart cause)
        {
            Cause = cause;
        }
    }

    // ── M3: 소음 (Player·Weapons → AI) ───────────────────

    public enum NoiseKind
    {
        Footstep,
        Jump,
        Gunshot,
    }

    // 플레이어가 낸 소리 (§4.1 소음 반경, 사격) → AI 청각. 반경 안의 적이 듣는다.
    public readonly struct NoiseEmitted : IGameEvent
    {
        public readonly Vector3 Position;
        public readonly float RadiusM;
        public readonly NoiseKind Kind;

        public NoiseEmitted(Vector3 position, float radiusM, NoiseKind kind)
        {
            Position = position;
            RadiusM = radiusM;
            Kind = kind;
        }
    }

    // ── M3: AI → 다른 기능 ────────────────────────────────

    // 적 사망 → Inventory가 시체에 루트 컨테이너를 붙인다 (§6 시체). 이후 M4 레이드 통계·M6 오디오도 받을 수 있다.
    public readonly struct EnemyKilled : IGameEvent
    {
        public readonly GameObject Corpse;       // 시체 루트 (콜라이더 유지)
        public readonly string DisplayName;      // 프롬프트·루트 패널 이름 (EnemyArchetypeSO.corpseDisplayName)
        public readonly ItemInstance[] Items;    // 배치 순서 = 검색으로 드러나는 순서
        public readonly Vector3 Position;
        public readonly string ArchetypeId;

        public EnemyKilled(GameObject corpse, string displayName, ItemInstance[] items, Vector3 position, string archetypeId)
        {
            Corpse = corpse;
            DisplayName = displayName;
            Items = items;
            Position = position;
            ArchetypeId = archetypeId;
        }
    }

    // ── M4: Raid → 다른 기능 ──────────────────────────────

    // 레이드 시작 (스폰 배치 직후, RULE-02 타이머 시작) → UI, (M6) Audio 등
    public readonly struct RaidStarted : IGameEvent
    {
        // RULE-06: UI에 표시하지 말 것(스폰 이름·위치는 내 위치 단서). 테스트·로그·(M6) 연출 선택용으로만 둔다.
        public readonly string SpawnId;
        public readonly float TimeLimitSec;

        public RaidStarted(string spawnId, float timeLimitSec)
        {
            SpawnId = spawnId;
            TimeLimitSec = timeLimitSec;
        }
    }

    // 탈출 구역 안에서 카운트다운을 막는 조건 (§8.2). 문구 우선순위 = 선언 순서 (전원 → 시간 → 배낭 → 아이템).
    public enum ExtractionBlock
    {
        None,
        NoPower,            // EXT-02 전원 스위치 꺼짐
        TooEarly,           // EXT-03 레이드 15분 전
        MissingItem,        // EXT-03 신호탄 없음
        BackpackEquipped,   // EXT-04 배낭 착용
    }

    // 탈출 구역 HUD (§12 탈출 카운트다운). InZone=false면 숨김. 초 단위 값·상태가 바뀔 때만 발행(초당 최대 1회).
    // TODO(GDD-?) Q9: 조건 미충족이면 Block으로 이유 문구를 보인다.
    public readonly struct ExtractionStatusChanged : IGameEvent
    {
        public readonly bool InZone;
        public readonly string DisplayName;
        public readonly ExtractionBlock Block;   // None이면 카운트다운 중
        public readonly int SecondsLeft;         // 올림 정수

        public ExtractionStatusChanged(bool inZone, string displayName, ExtractionBlock block, int secondsLeft)
        {
            InZone = inZone;
            DisplayName = displayName;
            Block = block;
            SecondsLeft = secondsLeft;
        }
    }

    // 레이드 종료 (탈출/사망/MIA/포기) → UI 종료 오버레이. 결과 전체는 GameFlow.PendingResult로 씬을 넘는다.
    public readonly struct RaidEnded : IGameEvent
    {
        public readonly RaidOutcome Outcome;
        public readonly string ExtractionName;   // Extracted일 때만

        public RaidEnded(RaidOutcome outcome, string extractionName)
        {
            Outcome = outcome;
            ExtractionName = extractionName;
        }
    }

    // ── M5: Hideout → 다른 기능 ───────────────────────────

    // 탄피·보관함 내용·키트 상태가 바뀜 → 은신처 UI가 IHideoutService·IInventoryService를 다시 읽는다.
    // 은신처 인벤토리 브리지(Inventory)는 이것을 받아 InventoryChanged로 다시 알린다(보관함 그리드가 바뀌었으니).
    public readonly struct HideoutChanged : IGameEvent { }

    // ── M6: 소리 발생 (Player·Weapons·AI → Audio) ─────────

    // 소리를 낸 쪽 (M6 설계 §1.2). Audio가 내 발소리 음량·voice 우선순위를 가른다.
    public enum SoundSource
    {
        Player,
        Enemy,
    }

    // 걸음 종류 (§4.1 이동 상태). 음량·들리는 거리를 가른다. 자세(엎드림 > 웅크림) > 질주 > 걷기 순으로 정한다.
    public enum FootstepGait
    {
        Walk,
        Sprint,
        Crouch,
        Prone,
    }

    // 한 걸음 (§11 지면별 발소리, M6 Q12·Q13). 보폭마다(StrideCadence) 플레이어·적이 발행 → Audio가 지면을 판정해 재생한다.
    // AI 청각용 NoiseEmitted(Footstep)와 별개(그쪽은 0.5초 펄스, 그대로).
    public readonly struct FootstepTaken : IGameEvent
    {
        public readonly Vector3 Position;    // 발 위치 (지면 판정은 여기서 위로 조금 띄워 아래로)
        public readonly FootstepGait Gait;
        public readonly SoundSource Source;

        public FootstepTaken(Vector3 position, FootstepGait gait, SoundSource source)
        {
            Position = position;
            Gait = gait;
            Source = source;
        }
    }

    // 한 발 사격 (§11 총성 거리 레이어, M6 Q14). 발당 1회(펠릿 수 무관), 플레이어·적 모두 → Audio.
    // AI 청각은 계속 NoiseEmitted(Gunshot)만 본다(적 총성은 적이 듣지 않는다, M3 규칙 유지).
    public readonly struct ShotFired : IGameEvent
    {
        public readonly Vector3 Position;            // 총구
        public readonly WeaponDefinitionSO Weapon;   // null 허용 → Audio가 AudioConfigSO.fallbackGunshotClass 세트
        public readonly SoundSource Source;

        public ShotFired(Vector3 position, WeaponDefinitionSO weapon, SoundSource source)
        {
            Position = position;
            Weapon = weapon;
            Source = source;
        }
    }

    // ── M7: Weapons → Audio ───────────────────────────────

    // 방아쇠·고장 소리 종류 (§5.1, M7 Q9·Q10). HUD 문구 없음 — 소리만.
    public enum WeaponClickKind
    {
        DryFire,      // 빈 탄창·고장 상태에서 방아쇠를 새로 당김 (헛격발)
        Jammed,       // 고장 난 순간
        JamCleared,   // T 길게 눌러 해제 완료
    }

    // 플레이어 무기 클릭 소리 → Audio (플레이어 자신 3D). 발행: Weapons WeaponController.
    public readonly struct WeaponClicked : IGameEvent
    {
        public readonly Vector3 Position;   // 총구 (없으면 무기 위치)
        public readonly WeaponClickKind Kind;

        public WeaponClicked(Vector3 position, WeaponClickKind kind)
        {
            Position = position;
            Kind = kind;
        }
    }

    // ── M7: AI → Audio ────────────────────────────────────

    // 적 울음 종류 (§7 AI-MUT, M7 Q26). "소리가 곧 정보"(기둥 3) — 무리 접근을 듣고 알게 한다.
    public enum EnemyVocalKind
    {
        MutantAlert,    // 변이체가 평시 → Alert/Combat에 들어간 순간 (경계 울음)
        MutantAttack,   // 변이체 근접 공격 시작마다
    }

    // 적 울음 → Audio (3D, 적 위치). 발행: AI EnemyAgent. 같은 적의 울음 간격은 AiConfigSO.vocalCooldownSec.
    public readonly struct EnemyVocalized : IGameEvent
    {
        public readonly Vector3 Position;   // 적 눈 위치
        public readonly EnemyVocalKind Kind;

        public EnemyVocalized(Vector3 position, EnemyVocalKind kind)
        {
            Position = position;
            Kind = kind;
        }
    }

    // ── M6: Raid(재 폭풍) → 다른 기능 ─────────────────────

    // 재 폭풍 단계 (§8.3, M6 Q23·Q24). 레이드당 Pending → Rising → Peak → Falling → Over 한 번씩.
    public enum AshStormPhase
    {
        Pending,    // 아직 시작 전
        Rising,     // 강도 0 → 1 전환
        Peak,       // 최대 강도 (§8.3 "3분", 시야 30m)
        Falling,    // 강도 1 → 0 전환
        Over,       // 끝남 (이번 레이드에 다시 없음)
    }

    // 재 폭풍 단계가 바뀜 (바뀔 때만, 레이드당 4회) → Audio 폭풍 루프 시작·정지. 강도·시야는 IAshStormInfo로 읽는다.
    public readonly struct AshStormChanged : IGameEvent
    {
        public readonly AshStormPhase Phase;

        public AshStormChanged(AshStormPhase phase)
        {
            Phase = phase;
        }
    }

    // ── UI → 다른 기능 ─────────────────────────────────────

    // 일시정지 메뉴에서 레이드 포기 확인 → Raid가 Abandoned로 끝낸다 (TODO(GDD-?) Q16).
    public readonly struct RaidAbandonRequested : IGameEvent { }

    // 열린 화면이 바뀜 (None = 모두 닫힘) → Player·Weapons·Inventory가 게임플레이 입력을 막는다.
    public readonly struct ScreenStateChanged : IGameEvent
    {
        public readonly ScreenKind Screen;

        public ScreenStateChanged(ScreenKind screen)
        {
            Screen = screen;
        }
    }
}
