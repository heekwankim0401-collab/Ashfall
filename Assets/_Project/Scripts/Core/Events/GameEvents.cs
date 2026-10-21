using UnityEngine;

namespace Ashfall.Core
{
    // M2·M3 기능 간 이벤트 (M2 설계 §3.4, M3 설계 §3). 전부 readonly struct, GameManager.Events로 발행한다.
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

    // ── UI → 다른 기능 ─────────────────────────────────────

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
