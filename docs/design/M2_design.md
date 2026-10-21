# M2 "체력 & 인벤토리" 설계 (unity-architect)

기준: docs/GDD.md §3 RULE-04/05, §4.1~4.3, §5.1, §5.3, §6, §12, §13.2~13.3, §14, §15 M2. M1 임시값은 그대로 둔다(사용자 결정).
구현 에이전트는 이 문서만 보고 작업한다. 여기 없는 것은 만들지 않는다. 모호하면 `// TODO(GDD-?)` + 가장 단순한 구현.

---

## 0. 범위

**만든다 (M2 AC)**
- SYS-HP-01 부위 체력 7부위(§4.2), 파괴 효과, 파괴 부위 추가 피해 70% 분산.
- STAT-BLEED / STAT-HBLEED / STAT-FRAC 발생·지속 피해·패널티·해제(붕대/지혈대/부목).
- SYS-INV 그리드 인벤토리(W×H, 회전 R), 장착 슬롯 7개, 저장 공간 = 조끼 + 배낭 + 주머니(1×1 ×4).
- 루트 컨테이너(상자) F 열기 → 아이템 1개당 0.5초 검색 연출. 바닥 아이템 F 줍기.
- RULE-04: T → 0.8초 후 "가득/절반/거의 없음". RULE-05: H → 부위 상태 화면(수치 없음).
- 재장전 ↔ 인벤토리 탄창 연동(일반 = 회수, 빠른 = 바닥에 줍기 가능한 아이템으로 떨어뜨림).
- 무게 → 이동(§4.1 무게 패널티), 체력 상태 → 이동 패널티·조준 흔들림.
- 의약품 사용(인벤토리에서 사용 + 퀵슬롯 4~7).

**만들지 않는다**
- STAT-RAD/DEHY/HUNG, 수분·에너지(M7). 복부 파괴 "탈수 가속"은 `// TODO(M7): 탈수 가속 (SYS-STAT DEHY 미구현)`만.
- 방어구 관통·둔탁 피해·내구도 감소(§5.3 공식) — `ArmorDefinitionSO`는 정의·장착·무게만.
- 고장(Jam)/내구도, T 길게 누르기, 적 AI, 세이브, 레이드 루프, 사망 결과 화면, 상인, LootTableSO 굴림.
- 무기 교체 1/2/3: 무기 데이터가 1종뿐이라 M2 AC 밖. 슬롯(Primary1/2, Secondary)은 만들되 `WeaponController`는 Primary1만 따른다(§9 결정 표 D11).
- 피격 방향 붉은 가장자리(§12 허용 HUD지만 M2 AC 아님), 희귀도 테두리 색, 폰트(M6).

---

## 1. 아키텍처 핵심 결정

### 1.1 GameManager = 유일한 싱글톤, 이벤트 버스·서비스 레지스트리를 가진다 (Core)
- `GameManager : MonoBehaviour`. `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]`로 자동 생성 + `DontDestroyOnLoad`.
  Boot를 거치지 않는 테스트 씬(M1/M2)·PlayMode 테스트에서도 존재한다.
  `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]`에서 static `Instance = null`(도메인 리로드 꺼짐 대비).
- 멤버: `EventBus Events`, `ServiceRegistry Services`. 둘 다 순수 C# 클래스(정적 상태 없음)라 EditMode 테스트에서 `new`로 만든다.
- M2 GameManager는 이것만 한다. 세이브·씬 흐름은 M4/M5.

### 1.2 이벤트 버스 형태
```csharp
namespace Ashfall.Core
{
    public interface IGameEvent { }   // 이벤트 표식. 모든 이벤트는 readonly struct.

    public sealed class EventBus
    {
        public void Subscribe<T>(Action<T> handler) where T : struct, IGameEvent;
        public void Unsubscribe<T>(Action<T> handler) where T : struct, IGameEvent;
        public void Publish<T>(in T evt) where T : struct, IGameEvent;
        public int SubscriberCount<T>() where T : struct, IGameEvent;   // 누수 테스트용
        public void Clear();
    }
}
```
- 동기 호출, 구독 순서대로. `Dictionary<Type, List<Delegate>>` 기반.
- Publish 중 구독/해지 안전(호출 전 스냅샷 배열 사용). 핸들러 예외는 `Debug.LogException` 후 다음 핸들러 계속.
- 같은 핸들러 중복 구독은 무시.
- **사용 규칙**
  - 도메인 모델(`BodyHealthModel`, `PlayerInventory`, `ItemGrid`, `WeaponModel` …)은 버스를 모른다. C# `event`/상태만 노출.
  - 버스 Publish/Subscribe는 얇은 MonoBehaviour(브리지)와 UI 프레젠터(생성자로 `EventBus` 주입)에서만.
  - MonoBehaviour: `OnEnable`에서 Subscribe, `OnDisable`에서 Unsubscribe(`GameManager.Instance != null` 확인).
  - 이벤트는 **기능 간** 통신에만. 같은 어셈블리 안(예: PlayerHealth → PlayerMotor)은 직접 참조.

### 1.3 서비스 레지스트리 (UI·Weapons가 다른 기능의 상태를 읽고 조작하는 방법)
```csharp
public sealed class ServiceRegistry
{
    public void Register<T>(T service) where T : class;        // 같은 T 중복 등록 시 덮어쓰고 경고
    public void Unregister<T>(T service) where T : class;      // 등록된 것과 같을 때만 해제
    public bool TryGet<T>(out T service) where T : class;
}
```
- 등록 대상은 **Core에 정의된 인터페이스만**: `IInventoryService`, `IMagazineSource`, `IBodyStatusView`.
- 규칙: 제공자는 `Awake`/`OnEnable`에서 Register, `OnDisable`에서 Unregister. 소비자는 `Start` 이후(또는 화면을 열 때) TryGet.
  같은 씬 로드에서 모든 Awake가 Start보다 먼저 끝나므로 순서 문제가 없다.

**결정(질문 2): UI = "Core 인터페이스로 조회·명령 + 이벤트로 변경 알림"**
- 조회: `IInventoryService`(그리드·슬롯·루트 상태 읽기 + `CanPlace` 판정), `IBodyStatusView`(부위별 등급·상태이상).
- 명령: `IInventoryService.TryMove/TryEquip/...` 동기 호출, bool 반환.
- 변경 알림: `InventoryChanged`, `PlayerBodyStatusChanged` 이벤트를 받으면 UI가 다시 읽어 그린다.
- 이유
  1. 드래그 중 "여기 놓을 수 있나" 미리보기는 매 프레임 동기 판정이 필요하다. 이벤트 요청/응답으로는 어색하고, UI에 배치 규칙을 복제하면 규칙이 두 군데가 된다.
  2. 명령의 성공/실패를 즉시 알아야(드래그 원위치 복귀) 하는데 이벤트는 반환값이 없다.
  3. 변경마다 전체 그리드 스냅샷을 이벤트로 복사하는 것보다 "바뀜" 신호 + 읽기 인터페이스가 싸고 단순하다.
  4. RULE-05를 구조로 강제: `IBodyStatusView`는 HP 수치를 내주지 않고 `HealthTier` 등급만 준다. UI가 수치를 표시할 방법이 없다.
  5. UI 프레젠터는 가짜 `IInventoryService`/`IBodyStatusView`로 EditMode 테스트할 수 있다.
- 인터페이스는 Core에 있으므로 의존 방향(기능 → Core)이 지켜진다. 레지스트리는 GameManager 멤버라 싱글톤이 늘지 않는다.

### 1.4 피격 → 부위 피해 (DamageInfo는 바꾸지 않는다)
- M2에서 플레이어를 맞히는 것은 `ProjectileSystem`뿐이고 충돌체는 `CharacterController` 캡슐 하나다.
  부위 히트박스 콜라이더를 붙이면 캡슐이 먼저 맞아 레이어/충돌 매트릭스(ProjectSettings) 작업이 필요해진다.
- 결정: **캡슐 피격 지점의 로컬 좌표로 부위를 판정**한다. `PlayerHealth`(Player, `IDamageable`, 플레이어 루트)가
  `transform.InverseTransformPoint(info.Point)` + 현재 캡슐 높이·반지름으로 `BodyPartResolver.Resolve(...)`를 호출.
- `ProjectileSystem`·`DamageInfo`·`IDamageable` 변경 없음. 기존 `GetComponentInParent<IDamageable>()`가 루트의 `PlayerHealth`를 찾는다.
- `BodyPartResolver`는 M2에서 Player만 쓰므로 Player에 둔다. M3에서 AI(3부위)가 필요하면 그때 Core로 옮기거나 히트박스로 바꾼다.
- 한계(TODO): 엎드림(캡슐 0.6m 세로)에서는 높이 구간이 부정확. 그레이박스 근사로 허용.

### 1.5 런타임 아이템 인스턴스 (질문 3) — Core
- 위치: `Ashfall.Core` (`Core/Items/`). Weapons(탄창·무기), Inventory(그리드), Player(의약품)가 같이 쓰기 때문.
- 회전은 **인스턴스가 아니라 배치(PlacedItem)** 에 둔다. 같은 아이템은 한 곳에만 있고, 회전은 그 그리드 배치의 속성이다.
  장착 슬롯·바닥에는 회전 개념이 없다. 드래그 중 회전은 UI 드래그 상태가 들고 있다가 `TryMove(..., rotated)`로 넘긴다.
- **`Magazine`을 `Ashfall.Weapons` → `Ashfall.Core`로 옮긴다** (`ItemInstance` 상속). 파일은 `.meta`와 함께 이동(`git mv` 2개, GUID 유지).
  클래스명·`Full()`·`TakeRound()`·`Rounds`·`Capacity` 유지 → 기존 테스트의 `Magazine.Full(mag30)`은 `using Ashfall.Core`로 그대로 컴파일된다.
- 상속 구조(전부 순수 C#):
  - `ItemInstance` — `Definition`, `virtual float WeightKg`, `Vector2Int SizeFor(bool rotated)`.
  - `Magazine : ItemInstance` — 탄 수(§5.1).
  - `WeaponItem : ItemInstance` — `Magazine LoadedMagazine`(장착 탄창, 무게 포함).
  - `MedicalItem : ItemInstance` — `UsesLeft`.
  - 그 밖(방어구·조끼·배낭·잡동사니)은 상태가 없어 `ItemInstance` 그대로.
- 조끼/배낭의 **내용물 그리드는 인스턴스가 아니라 `PlayerInventory`가 슬롯별로 소유**한다.
  M2 규칙: 내용물이 있는 조끼/배낭은 장착 해제·이동·버리기 불가(TODO(GDD-?)). 빈 조끼/배낭은 일반 아이템처럼 그리드에 넣을 수 있다.
  → 컨테이너 안의 컨테이너, 내용물째 버리기 같은 문제를 M2에서 피한다.

### 1.6 기능 간 흐름 요약 (질문 1)

| 흐름 | 발행 → 구독 | 방식 |
|---|---|---|
| 투사체 → 플레이어 피해 | Weapons `ProjectileSystem` → Player `PlayerHealth.ApplyDamage` | 기존 `IDamageable`(Core). 부위는 PlayerHealth가 판정 |
| 체력 → 이동 | `BodyHealthModel.Modifiers` → `PlayerMotor` | 같은 어셈블리, 직접 참조 (`GetComponent<PlayerHealth>()`) |
| 체력 → 조준 흔들림 | Player → Weapons | 이벤트 `PlayerConditionChanged` |
| 체력 → H 화면 | Player → UI | `IBodyStatusView`(레지스트리) + 이벤트 `PlayerBodyStatusChanged` |
| 사망 | Player → Weapons/Inventory/UI/Player | 이벤트 `PlayerDied` (조작 정지, 화면 닫기) |
| 무게 → 이동 | Inventory → Player | 이벤트 `CarriedWeightChanged` |
| 장착 무기 → 무기 | Inventory → Weapons | `IInventoryService.GetEquipped`(Start) + 이벤트 `EquipmentChanged` |
| 재장전 탄창 꺼내기/회수 | Weapons → Inventory | `IMagazineSource`(레지스트리) 동기 호출 |
| 빠른 재장전 탄창 버림 / 회수 공간 없음 | Weapons → Inventory | 이벤트 `ItemDropRequested` → Inventory가 `WorldItem` 생성 |
| 의약품 사용 | Inventory → Player → Inventory | `MedicalUseRequested` → (사용 시간) → `MedicalUseCompleted` → Inventory가 사용 횟수 차감 |
| T 탄창 확인 결과 | Weapons → UI | 이벤트 `MagazineCheckStarted` / `MagazineCheckCompleted` |
| F 상호작용 프롬프트 | Inventory → UI | 이벤트 `InteractionPromptChanged` |
| 루트 열기 | Inventory → UI | 이벤트 `LootSessionChanged` → UI가 인벤토리 화면(루트 패널 포함)을 연다 |
| 화면 열림 → 조작 차단 | UI → Player/Weapons/Inventory | 이벤트 `ScreenStateChanged` |

재장전 상세 (§5.1)
1. R → `WeaponModel.PressReload()`: `source.HasLoadable(ammo)`가 false면 거부.
2. 완료 시: `next = source.TakeFullest(ammo)` (저장 공간에서 제거) → 그다음 이전 탄창 처리
   - 일반: `source.TryStore(old)`; false(공간 없음)면 `MagazineDropped(old)`.
   - 빠른: `MagazineDropped(old)`.
3. `WeaponController`가 `MagazineDropped`를 받아 `ItemDropRequested { Item = old, Position = 총구 앞 아래, Velocity }` 발행.
4. Inventory `PlayerInventoryBehaviour`가 구독해 `WorldItem.Spawn(...)` → F로 다시 주울 수 있다.
- 탄창을 찾는 범위: 전술조끼 + 주머니(배낭 제외). `InventoryConfigSO.reloadFromBackpack = false`. TODO(GDD-?) — 문서에 없음.
- 호환 판정: 탄창 `ammo == 무기 ammo` (M1과 동일). 같은 탄 다른 탄창 구분은 M7 무기 추가 때 정한다.

---

## 2. 어셈블리 / asmdef 변경

| asmdef | references 변경 |
|---|---|
| `Ashfall.Core` | 없음 |
| `Ashfall.Player` | 없음 (`Ashfall.Core`, `Unity.InputSystem`) |
| `Ashfall.Weapons` | 없음 |
| `Ashfall.Inventory` | `+ Unity.InputSystem` (F, 4~7) |
| `Ashfall.UI` | `+ Unity.InputSystem` (Tab, H, Esc, R 회전). UI Toolkit은 엔진 모듈이라 추가 불필요 |
| `Ashfall.EditorTools` | `+ Ashfall.Inventory`, `+ Ashfall.UI` |
| 테스트 asmdef 2개 | 변경 없음(이미 전체 참조) |

기능 어셈블리끼리 참조 추가 금지. 새 asmdef 없음.

---

## 3. Core 타입 (전부 `namespace Ashfall.Core`)

### 3.1 파일 목록
| 파일 | 내용 |
|---|---|
| `Core/GameManager.cs` | 싱글톤, `Events`, `Services` |
| `Core/Events/EventBus.cs` | `IGameEvent`, `EventBus` |
| `Core/Events/GameEvents.cs` | 아래 3.4 이벤트 struct 전부 |
| `Core/Services/ServiceRegistry.cs` | `ServiceRegistry` |
| `Core/Services/IInventoryService.cs` | `IInventoryService`, `IGridView`, `ILootView`, `PlacedItem`, `GridRef`, `ItemLocation` |
| `Core/Services/IMagazineSource.cs` | `IMagazineSource` |
| `Core/Services/IBodyStatusView.cs` | `IBodyStatusView` |
| `Core/Combat/BodyEnums.cs` | `BodyPart`, `StatusEffect`, `HealthTier` |
| `Core/Combat/ConditionModifiers.cs` | `ConditionModifiers` |
| `Core/Items/ItemInstance.cs` | `ItemInstance`, `ItemFactory` |
| `Core/Items/Magazine.cs` | **Weapons/Magazine.cs(+.meta)에서 이동**, `ItemInstance` 상속 |
| `Core/Items/WeaponItem.cs`, `Core/Items/MedicalItem.cs` | 런타임 상태 |
| `Core/Items/InventoryEnums.cs` | `EquipSlot`, `GridKind`, `ContainerKind`, `ArmorKind`, `InteractionKind` |
| `Core/Data/MedicalDefinitionSO.cs`, `ArmorDefinitionSO.cs`, `ContainerDefinitionSO.cs` | 새 SO (§3.5) |
| `Core/UiEnums.cs` | `ScreenKind`, `MagazineFillLevel`, `MedicalUseFailReason` |

### 3.2 enum
```csharp
public enum BodyPart { Head, Thorax, Stomach, LeftArm, RightArm, LeftLeg, RightLeg }   // 배열 인덱스로 씀, 순서 고정
public enum StatusEffect { Bleed, HeavyBleed, Fracture }   // STAT-RAD/DEHY/HUNG은 M7에 추가
public enum HealthTier { Healthy, Injured, Critical, Destroyed }   // RULE-05: 수치 대신 등급
public enum EquipSlot { Primary1, Primary2, Secondary, Helmet, BodyArmor, TacticalVest, Backpack }
public enum GridKind { Pocket, TacticalVest, Backpack, Loot }
public enum ContainerKind { TacticalVest, Backpack }
public enum ArmorKind { Helmet, BodyArmor }
public enum InteractionKind { OpenContainer, PickUp }
public enum ScreenKind { None, Inventory, BodyStatus }
public enum MagazineFillLevel { Full, Half, AlmostEmpty, NoMagazine }
public enum MedicalUseFailReason { NotNeeded, Busy, Dead }
```

### 3.3 값 타입·인터페이스
```csharp
public readonly struct ConditionModifiers
{
    public readonly float MoveSpeedMultiplier;   // 1 = 정상
    public readonly bool SprintAllowed;
    public readonly float AimSwayMultiplier;     // 1 = 정상, 팔 파괴 1개당 +1 (§4.2 +100%)
    public ConditionModifiers(float move, bool sprint, float sway);
    public static ConditionModifiers Healthy => new ConditionModifiers(1f, true, 1f);
    // Equals 구현 (변경 감지용)
}

public readonly struct GridRef : IEquatable<GridRef>
{
    public readonly GridKind Kind;
    public readonly int Index;    // 주머니 0~3, 나머지 0
    public GridRef(GridKind kind, int index = 0);
}

public readonly struct PlacedItem
{
    public readonly ItemInstance Item;
    public readonly Vector2Int Cell;      // 왼쪽 위 칸 (x 오른쪽, y 아래쪽)
    public readonly bool Rotated;         // true면 W↔H
    public Vector2Int Size => Item.SizeFor(Rotated);
}

public readonly struct ItemLocation
{
    public readonly bool IsEquipped;
    public readonly EquipSlot Slot;       // IsEquipped일 때
    public readonly GridRef Grid;         // 아닐 때
    public readonly Vector2Int Cell;
    public readonly bool Rotated;
}

public interface IGridView
{
    GridRef Ref { get; }
    Vector2Int Size { get; }
    IReadOnlyList<PlacedItem> Items { get; }
}

public interface ILootView : IGridView
{
    string DisplayName { get; }
    bool IsRevealed(ItemInstance item);   // 검색 전 아이템은 UI가 빈칸/"?"로 그린다
    bool SearchComplete { get; }
}

public interface IInventoryService
{
    // 읽기
    IReadOnlyList<IGridView> StorageGrids { get; }   // 주머니 4 + (장착 시) 조끼 + 배낭, 이 순서
    ItemInstance GetEquipped(EquipSlot slot);        // 비었으면 null
    ILootView ActiveLoot { get; }                    // 열린 루트 컨테이너, 없으면 null
    ItemInstance GetQuickSlot(int index);            // 0~3 = 키 4~7
    float TotalWeightKg { get; }
    bool TryGetLocation(ItemInstance item, out ItemLocation location);

    // 판정 (드래그 미리보기)
    bool CanPlace(ItemInstance item, GridRef grid, Vector2Int cell, bool rotated);
    bool CanEquip(ItemInstance item, EquipSlot slot);

    // 명령. 성공하면 true + InventoryChanged 발행(브리지가)
    bool TryMove(ItemInstance item, GridRef grid, Vector2Int cell, bool rotated);
    bool TryEquip(ItemInstance item, EquipSlot slot);    // 슬롯이 차 있으면 실패 (교환 없음, TODO)
    bool TryQuickMove(ItemInstance item);                // 루트 ↔ 저장 공간 자동 배치
    bool TryDrop(ItemInstance item);                     // 바닥에 버리기 (WorldItem)
    bool TryAssignQuickSlot(int index, ItemInstance item);  // 의약품만, 저장 공간에 있는 것만
    bool TryUse(ItemInstance item);                      // 의약품 → MedicalUseRequested
    void CloseLoot();
}

public interface IMagazineSource
{
    bool HasLoadable(AmmoType ammo);              // 탄이 1발 이상 든 맞는 탄창이 있나
    Magazine TakeFullest(AmmoType ammo);          // 가장 많이 든 것을 저장 공간에서 꺼냄. 없으면 null
    bool TryStore(Magazine magazine);             // 일반 재장전 회수. 공간 없으면 false
}

public interface IBodyStatusView
{
    bool IsDead { get; }
    HealthTier GetTier(BodyPart part);
    bool HasStatus(BodyPart part, StatusEffect effect);
}
```

### 3.4 이벤트 목록 (`Core/Events/GameEvents.cs`, 전부 `readonly struct : IGameEvent`)

| 이벤트 | 필드 | 발행 | 구독 |
|---|---|---|---|
| `InventoryChanged` | (없음) | Inventory | UI (다시 그리기) |
| `EquipmentChanged` | `EquipSlot Slot; ItemInstance Item` (null = 비움) | Inventory | Weapons |
| `CarriedWeightChanged` | `float TotalKg` | Inventory | Player |
| `ItemDropRequested` | `ItemInstance Item; Vector3 Position; Vector3 Velocity` | Weapons | Inventory |
| `MedicalUseRequested` | `MedicalItem Item` | Inventory | Player |
| `MedicalUseStarted` | `MedicalItem Item; BodyPart Target; float DurationSec` | Player | Inventory (사용 중 아이템 잠금) |
| `MedicalUseCompleted` | `MedicalItem Item; BodyPart Target` | Player | Inventory (횟수 차감·제거) |
| `MedicalUseRejected` | `MedicalItem Item; MedicalUseFailReason Reason` | Player | (로그만, UI 표시 없음) |
| `PlayerBodyStatusChanged` | (없음) | Player | UI. **등급·상태이상·사망이 바뀔 때만** 발행(매 프레임 출혈로는 발행 안 함) |
| `PlayerConditionChanged` | `ConditionModifiers Modifiers` | Player | Weapons. 값이 바뀔 때만 |
| `PlayerDied` | `BodyPart Cause` | Player | Weapons, Inventory, UI |
| `ScreenStateChanged` | `ScreenKind Screen` (None = 모두 닫힘) | UI | Player, Weapons, Inventory (조작 차단) |
| `MagazineCheckStarted` | (없음) | Weapons | UI |
| `MagazineCheckCompleted` | `MagazineFillLevel Level` | Weapons | UI |
| `InteractionPromptChanged` | `bool Visible; InteractionKind Kind; string TargetName` | Inventory | UI (HUD 프롬프트) |
| `LootSessionChanged` | `bool Open` | Inventory | UI |

### 3.5 아이템 클래스·SO (§14 대비 추가 필드 표시 ★)
```csharp
public class ItemInstance
{
    public ItemDefinitionSO Definition { get; }
    public ItemInstance(ItemDefinitionSO definition);
    public virtual float WeightKg => Definition.weightKg;
    public Vector2Int SizeFor(bool rotated);   // rotated면 (size.y, size.x)
}
public static class ItemFactory
{
    // Magazine 정의 → Magazine.Full, Weapon → WeaponItem(탄창 없음), Medical → MedicalItem(uses), 그 밖 → ItemInstance
    public static ItemInstance Create(ItemDefinitionSO definition);
}
public sealed class Magazine : ItemInstance        // 이동 + 상속만, 기존 API 유지
{
    public new MagazineDefinitionSO Definition { get; }
    public int Rounds { get; }  public int Capacity { get; }  public bool IsEmpty { get; }
    public Magazine(MagazineDefinitionSO definition, int rounds);
    public static Magazine Full(MagazineDefinitionSO definition);
    public bool TakeRound();
    // WeightKg = 정의 무게. TODO(GDD-?): 탄 무게 미반영
}
public sealed class WeaponItem : ItemInstance
{
    public new WeaponDefinitionSO Definition { get; }
    public Magazine LoadedMagazine { get; set; }
    public WeaponItem(WeaponDefinitionSO definition, Magazine loaded = null);
    public override float WeightKg => Definition.weightKg + (LoadedMagazine?.WeightKg ?? 0f);
}
public sealed class MedicalItem : ItemInstance
{
    public new MedicalDefinitionSO Definition { get; }
    public int UsesLeft { get; }
    public MedicalItem(MedicalDefinitionSO definition);   // UsesLeft = definition.uses
    public bool ConsumeUse();                             // 남은 횟수 0이면 false
}
```
SO (Core/Data, `CreateAssetMenu "Ashfall/..."`)
```csharp
[CreateAssetMenu(menuName = "Ashfall/Medical")]   class MedicalDefinitionSO : ItemDefinitionSO
{ StatusEffect[] cures; float healAmount; float useTime; int uses; }                 // §14 그대로
[CreateAssetMenu(menuName = "Ashfall/Armor")]     class ArmorDefinitionSO : ItemDefinitionSO
{ int armorClass; float maxDurability; BodyPart[] covers; ArmorKind kind /*★*/; }    // 관통 계산은 M3 이후
[CreateAssetMenu(menuName = "Ashfall/Container")] class ContainerDefinitionSO : ItemDefinitionSO   // ★ 클래스 자체가 §14에 없음
{ ContainerKind kind; Vector2Int gridSize; }
```

**§14 스키마에 없는 것 (질문 3 목록)**
| 항목 | 위치 | 이유 |
|---|---|---|
| `ContainerDefinitionSO { kind, gridSize }` | Core/Data | 조끼·배낭 그리드 크기, 슬롯 판정 |
| `ArmorDefinitionSO.kind` | Core/Data | 헬멧/방탄복 슬롯 판정 (`covers`로 추론하면 모호) |
| `PlayerConfigSO` §4.2/§4.3 필드 | Player | §14 "PlayerConfigSO = §4 수치 전부"에 따름 (새 SO 안 만듦) |
| `WeaponHandlingConfigSO` (새 SO) | Weapons | RULE-04 확인 시간·구간, 조준 흔들림 기본값 |
| `InventoryConfigSO` (새 SO) | Inventory | 주머니 수·크기, 검색 시간, 상호작용 거리, 버리기 |
| `HudConfigSO` (새 SO) | UI | 탄창 확인 결과 표시 시간 |
| 런타임 `ItemInstance`/`WeaponItem`/`MedicalItem` | Core/Items | 정의 + 상태 분리 |

**장착 슬롯 판정 (`Inventory/EquipRules`)**
| 슬롯 | 허용 |
|---|---|
| Primary1, Primary2 | `WeaponDefinitionSO` 이고 `cls != Pistol` |
| Secondary | `WeaponDefinitionSO` 이고 `cls == Pistol` — TODO(GDD-?) |
| Helmet / BodyArmor | `ArmorDefinitionSO` 이고 `kind == Helmet / BodyArmor` |
| TacticalVest / Backpack | `ContainerDefinitionSO` 이고 `kind == TacticalVest / Backpack` |

---

## 4. 수치 (질문 4) — SO 필드와 임시값

GDD 값은 그대로, 나머지는 `// TODO(GDD-?)` 임시값. EditorTools가 에셋에 넣는다.

### 4.1 `PlayerConfigSO` 추가 필드 (Player)
| 필드 | 값 | 출처 |
|---|---|---|
| `headMaxHp / thoraxMaxHp / stomachMaxHp / armMaxHp / legMaxHp` | 35 / 80 / 70 / 60 / 65 | §4.2 |
| `destroyedPartRedistribution` | 0.7 | §4.2 |
| `stomachDestroyedDamageMultiplier` | 1.5 | §4.2 |
| `armDestroyedAimSwayAdd` | 1.0 (= +100%) | §4.2 |
| `legDestroyedSpeedMultiplier` | 0.6 (= -40%), 질주 불가 | §4.2 |
| `bleedHpPerSec / heavyBleedHpPerSec` | 1 / 3 | §4.3 |
| `bleedMinDamage` | 10 | TODO |
| `heavyBleedChance` | 0.15 (먼저 굴림) | TODO |
| `bleedChance` | 0.35 (중출혈이 안 나왔을 때) | TODO |
| `fractureMinDamage` | 20 | TODO |
| `fractureChance` | 0.2 (팔·다리만) | TODO |
| `fractureLegSpeedMultiplier` | 0.8 | TODO (§4.3 "이동 패널티" 크기 없음) |
| `fractureArmAimSwayAdd` | 0.5 | TODO (§4.3 "조준 패널티" 크기 없음) |
| `tierInjuredBelow / tierCriticalBelow` | 0.75 / 0.35 (HP 비율) | TODO (RULE-05 등급 경계) |
| `headZoneFrom / thoraxZoneFrom / stomachZoneFrom` | 0.87 / 0.62 / 0.45 (캡슐 높이 비율, 그 아래 다리) | TODO |
| `armZoneSideRatio` | 0.6 (캡슐 반지름 대비, 흉부·복부 구간) | TODO |

규칙 해석 (구현은 `BodyHealthModel`, 전부 TODO(GDD-?) 주석):
- 피격으로 0 아래가 된 초과분은 버린다. **이미 파괴된** 부위가 받는 피해만 70% 분산.
- 분산: 70%를 흉부·복부 중 파괴되지 않은 곳에 균등(둘 다 살아 있으면 35%씩). 복부가 파괴됐으면 전부 흉부. 파괴된 복부가 맞으면 70%를 흉부로.
- 머리·흉부 0 → 즉사. 출혈로 0이 돼도 같다.
- 복부 파괴 후 1.5배는 **피격 피해(ApplyHit)에만**. 출혈 지속 피해에는 적용 안 함.
- 출혈 지속 피해도 "피해"이므로 파괴 부위면 분산 규칙 적용.
- 다리: 두 다리 중 가장 나쁜 값 하나만 적용(파괴 0.6 > 골절 0.8, 곱하지 않음). 파괴된 다리가 있으면 질주 불가.
- 팔: 흔들림 = 1 + Σ(팔마다 파괴 +1.0, 아니면 골절 +0.5).
- 상태이상 굴림은 피격(ApplyHit)마다, `damage >= MinDamage`일 때. 이미 같은 상태면 그대로.
- 골절은 팔·다리만. 출혈·중출혈은 모든 부위. 출혈과 중출혈은 동시에 가질 수 있다.
- 파괴 부위도 상태이상 유지. 파괴 부위 회복(수술)은 없음.

### 4.2 `WeaponHandlingConfigSO` (Weapons, 새 SO, 에셋 `Data/Weapons/WeaponHandlingConfig.asset`)
| 필드 | 값 | 출처 |
|---|---|---|
| `magCheckTimeSec` | 0.8 | RULE-04 |
| `magCheckFullAtOrAbove` | 0.67 (탄 수/용량) | TODO |
| `magCheckHalfAtOrAbove` | 0.34 (그 아래는 "거의 없음", 0발 포함) | TODO |
| `aimSwayBaseDeg` | 0.15 | TODO (M1에 흔들림 없음, 기본값이 있어야 +100%가 의미 있음) |
| `aimSwayFrequencyHz` | 0.5 | TODO |

### 4.3 `InventoryConfigSO` (Inventory, 새 SO, `Data/Inventory/InventoryConfig.asset`)
| 필드 | 값 | 출처 |
|---|---|---|
| `pocketCount` / `pocketSize` | 4 / (1,1) | §6 |
| `searchTimePerItemSec` | 0.5 | §6 |
| `interactRangeM` | 2.0 | TODO |
| `reloadFromBackpack` | false | TODO |
| `dropForwardM` / `dropSpeed` | 0.6 / 1.5 | TODO |
| `worldItemCellM` | 0.1 (바닥 상자 크기 = 칸 수 × 이 값) | TODO, 그레이박스 표현 |

### 4.4 `HudConfigSO` (UI, 새 SO, `Data/UI/HudConfig.asset`)
| 필드 | 값 | 출처 |
|---|---|---|
| `magCheckResultSec` | 2.0 | TODO |

### 4.5 아이템 에셋 (data-designer, 전부 TODO(GDD-?) — ID·수치 GDD에 없음)
경로 `Assets/_Project/Data/Items/<분류>/<ID>.asset`
| ID | 종류 | 이름 | size | kg | 기타 |
|---|---|---|---|---|---|
| MED-BND-01 | Medical | 붕대 | 1×1 | 0.05 | cures [Bleed], heal 0, useTime 3, uses 1 |
| MED-TRQ-01 | Medical | 지혈대 | 1×1 | 0.1 | cures [HeavyBleed], heal 0, useTime 4, uses 1 |
| MED-SPL-01 | Medical | 부목 | 1×2 | 0.2 | cures [Fracture], heal 0, useTime 5, uses 1 |
| RIG-01 | Container TacticalVest | 전술조끼 | 2×2 | 1.2 | grid 4×2 (1×2 탄창 4개) |
| BAG-01 | Container Backpack | 배낭 | 3×3 | 1.5 | grid 4×5 |
| ARM-HLM-01 | Armor Helmet | 헬멧 | 2×2 | 1.4 | class 3, dur 40, covers [Head] |
| ARM-BDY-01 | Armor BodyArmor | 방탄복 | 3×3 | 8.0 | class 3, dur 60, covers [Thorax, Stomach] |
| JNK-01 | Item | 녹슨 파이프 | 1×3 | 1.0 | Scrap |
| JNK-02 | Item | 엔진 부품 | 3×2 | 20.0 | Scrap, 무게 패널티 확인용 |
| JNK-03 | Item | 전선 뭉치 | 1×1 | 0.3 | Scrap |

M1 에셋(WPN-RIF-01, MAG-556-30, AMMO-556)은 손대지 않는다(무게 0 그대로, 사용자 결정). `AMMO-556`은 M2에서 인벤토리 아이템으로 쓰지 않는다(낱탄·탄창 채우기는 범위 밖).

---

## 5. 기능별 클래스와 공개 API (질문 5)

### 5.1 Player (`Ashfall.Player`)
| 파일 | 종류 | 역할 |
|---|---|---|
| `PlayerConfigSO.cs` | SO (수정) | §4.1 필드 + `MaxHp(BodyPart)` 도우미 |
| `BodyHealthModel.cs` | 순수 | SYS-HP-01, STAT-* |
| `BodyPartResolver.cs` | 순수 static | 캡슐 로컬 지점 → 부위 |
| `MedicalUseModel.cs` | 순수 | 사용 시간·대상 부위 |
| `LocomotionModel.cs` | 순수 (수정) | 상태 패널티 반영 |
| `PlayerHealth.cs` | MonoBehaviour | `IDamageable`, 모델 Tick, 버스 브리지, `IBodyStatusView` 등록 |
| `PlayerMotor.cs` | MonoBehaviour (수정) | 무게 이벤트·상태 패널티·조작 차단·사망 |

```csharp
public sealed class BodyHealthModel : IBodyStatusView
{
    public BodyHealthModel(PlayerConfigSO config, System.Random random);
    public bool IsDead { get; }
    public BodyPart? DeathCause { get; }
    public float GetHp(BodyPart part);
    public float GetMaxHp(BodyPart part);
    public bool IsDestroyed(BodyPart part);
    public HealthTier GetTier(BodyPart part);
    public bool HasStatus(BodyPart part, StatusEffect effect);
    public ConditionModifiers Modifiers { get; }

    public void ApplyHit(BodyPart part, float damage);     // 복부 배율 + 분산 + 상태이상 굴림
    public void ApplyDamage(BodyPart part, float damage);  // 분산만 (출혈 등 내부·테스트용)
    public void Tick(float dt);                            // 출혈·중출혈 지속 피해
    public void AddStatus(BodyPart part, StatusEffect effect);
    public bool RemoveStatus(BodyPart part, StatusEffect effect);
    public void Heal(BodyPart part, float amount);         // 파괴 부위는 회복 안 됨

    public event Action StatusChanged;      // 등급·상태이상·사망 변화 (→ PlayerBodyStatusChanged)
    public event Action ModifiersChanged;   // (→ PlayerConditionChanged)
    public event Action<BodyPart> Died;     // (→ PlayerDied)
}

public static class BodyPartResolver
{
    // localPoint: 플레이어 루트 기준 (발 = y 0). x < 0 = 왼쪽.
    public static BodyPart Resolve(Vector3 localPoint, float capsuleHeight, float capsuleRadius, PlayerConfigSO config);
}

public enum MedicalStartResult { Started, NotNeeded, Busy, Dead }
public sealed class MedicalUseModel
{
    public MedicalUseModel(BodyHealthModel body);
    public bool IsUsing { get; }
    public MedicalItem Current { get; }
    public BodyPart Target { get; }
    public float RemainingSec { get; }
    public MedicalStartResult TryStart(MedicalItem item);
    public bool Tick(float dt);   // 이번 Tick에 끝났으면 true (효과 적용: cures 제거 + healAmount 회복)
    // 대상: cures 중 하나를 가진 부위를 BodyPart 순서로 첫 번째. 없고 healAmount>0이면 가장 비율이 낮은 비파괴 부위. 없으면 null.
    public static BodyPart? FindTarget(BodyHealthModel body, MedicalDefinitionSO definition);
}

// LocomotionModel 추가 오버로드 (기존 시그니처는 ConditionModifiers.Healthy로 위임해 유지)
public bool CanSprint(bool moving, float carriedKg, StaminaModel stamina, in ConditionModifiers condition);
public float Speed(bool sprinting, float carriedKg, in ConditionModifiers condition);  // × condition.MoveSpeedMultiplier
```
- `PlayerHealth`: `[SerializeField] PlayerConfigSO config`. Awake: 모델 생성, `Services.Register<IBodyStatusView>(body)`.
  `ApplyDamage(in DamageInfo)`: 사망이면 무시 → 부위 판정 → `ApplyHit`. Update: `body.Tick`, `medical.Tick` (완료 시 `MedicalUseCompleted`).
  `MedicalUseRequested` 구독 → `TryStart` → Started/Rejected 발행. 의약품 사용 중 이동·사격 제한 없음, 취소 없음(TODO(GDD-?)).
  `public BodyHealthModel Body`, `public bool IsDead` 노출(PlayerMotor용).
- `PlayerMotor`: `CarriedWeightChanged` 구독 → `carriedWeightKg` 갱신(직렬화 필드는 인벤토리 없는 M1 씬용 초기값으로 유지, TODO(M2) 주석 제거).
  `GetComponent<PlayerHealth>()`가 있으면 `Body.Modifiers` 사용, 없으면 `Healthy`. `ScreenStateChanged != None` 또는 사망 시 입력(시점·이동·자세·점프) 무시, 중력만.
  커서 잠금은 UI가 관리한다(아래 5.4). PlayerMotor의 OnEnable 잠금은 그대로 둬도 된다.

### 5.2 Weapons (`Ashfall.Weapons`)
| 파일 | 종류 | 역할 |
|---|---|---|
| `Magazine.cs` | **삭제(→ Core/Items로 이동)** | |
| `WeaponModel.cs` | 순수 (수정) | `IMagazineSource` 사용 |
| `LocalMagazineSource.cs` | 순수 | `IMagazineSource` 목록 구현. 인벤토리 없는 M1 씬·테스트용 |
| `MagazineCheckModel.cs` | 순수 | RULE-04 |
| `AimSway.cs` | 순수 static | 조준 흔들림 |
| `WeaponHandlingConfigSO.cs` | SO | §4.2 |
| `WeaponController.cs` | MonoBehaviour (수정) | 장착 무기 바인딩, T, 흔들림, 버림 이벤트, 조작 차단 |
| `TestTurret.cs` | MonoBehaviour | 그레이박스: 구역 안 플레이어에게 주기적으로 사격(피격 확인용) |

```csharp
public sealed class WeaponModel
{
    public WeaponModel(WeaponItem item, IMagazineSource source);
    public WeaponItem Item { get; }
    public Magazine Current => Item.LoadedMagazine;
    public bool IsReloading { get; }  public bool IsFastReload { get; }
    public bool CanReload => source.HasLoadable(Item.Definition.ammo);
    public bool TryFire(float now, bool triggerHeld);
    public bool PressReload();        // 기존 규칙 유지 (재장전 중 R 한 번 더 = 빠른)
    public void Tick(float dt);
    public event Action<Magazine> MagazineDropped;   // 빠른 재장전 또는 회수 공간 없음
    // Spares 프로퍼티 제거 (예비 탄창은 source가 가진다)
}

public sealed class LocalMagazineSource : IMagazineSource
{
    public LocalMagazineSource(IEnumerable<Magazine> magazines);
    public IReadOnlyList<Magazine> Magazines { get; }
    // TryStore는 항상 성공
}

public sealed class MagazineCheckModel
{
    public MagazineCheckModel(WeaponHandlingConfigSO config);
    public bool IsChecking { get; }
    public bool TryStart(bool isReloading);   // 재장전 중이면 false
    public void Cancel();                     // 사격·재장전 입력 시 컨트롤러가 호출, 결과 없음
    public bool Tick(float dt);               // 0.8초 지나면 true (한 번)
    public static MagazineFillLevel Classify(Magazine magazine, WeaponHandlingConfigSO config);  // null → NoMagazine
}

public static class AimSway
{
    // 펄린 노이즈 기반 (yawDeg, pitchDeg). |값| ≤ baseDeg × multiplier
    public static Vector2 Offset(float time, float baseDeg, float multiplier, float frequencyHz, int seed);
}
```
- `WeaponController`
  - 직렬화 필드 유지(weapon/ammo/magazine/startingMagazines) + `WeaponHandlingConfigSO handling` 추가.
  - Start: `Services.TryGet<IInventoryService>` 있으면 `GetEquipped(Primary1)`이 `WeaponItem`이고 정의가 `weapon`과 같으면 바인딩,
    `IMagazineSource`도 레지스트리에서. 없으면(M1 씬) `new WeaponItem(weapon, Magazine.Full(magazine))` + `LocalMagazineSource(startingMagazines-1개)`.
  - `EquipmentChanged(Primary1)` 구독: 같은 정의의 `WeaponItem`이면 새 `WeaponModel` 바인딩, null이거나 다른 무기면 비무장(뷰모델 숨김, 사격·T·R 불가). TODO(M7): 무기 교체.
  - 무기를 슬롯에서 뺄 때 재장전 중이면 취소(진행 중 재장전은 버림).
  - T(`CheckMagazine`): 결과는 완료 시점의 탄 수로 판정 → `MagazineCheckCompleted`. 사격/R 누르면 Cancel.
  - 흔들림: `PlayerConditionChanged`로 받은 `AimSwayMultiplier` 캐시. `transform.localRotation = Euler(-pitch, yaw, 0)`으로 무기 루트를 돌려 뷰모델과 사격 방향이 같이 흔들린다.
  - `MagazineDropped` → `ItemDropRequested`(총구 위치 + 카메라 아래쪽 속도).
  - `ScreenStateChanged != None`, `PlayerDied` → 사격·조준·R·T 무시(조준은 해제).
- `GreyboxSceneTests`의 `weapon.Model.Spares.Count` 단언은 `LocalMagazineSource.Magazines.Count`로 바꾼다(필요하면 `WeaponController`에 `MagazineSource` 프로퍼티 노출).

### 5.3 Inventory (`Ashfall.Inventory`)
| 파일 | 종류 | 역할 |
|---|---|---|
| `ItemGrid.cs` | 순수 | W×H 칸, 배치·회전·충돌 |
| `EquipRules.cs` | 순수 static | 슬롯 판정 |
| `PlayerInventory.cs` | 순수 | 슬롯·저장 공간·퀵슬롯·무게·루트 세션, `IInventoryService` + `IMagazineSource` 구현 |
| `LootContainerModel.cs` | 순수 | 루트 그리드 + 검색 연출, `ILootView` 구현 |
| `InventoryConfigSO.cs` | SO | §4.3 |
| `PlayerInventoryBehaviour.cs` | MonoBehaviour | 플레이어에 붙음. 서비스 등록, 버스 브리지, F·4~7 입력, 상호작용 레이캐스트, WorldItem 생성 |
| `IInteractable.cs` | 인터페이스 (Inventory 내부) | `InteractionKind Kind; string TargetName; void Interact(PlayerInventoryBehaviour by)` |
| `LootContainer.cs` | MonoBehaviour | 씬의 상자. 고정 내용물(`ItemDefinitionSO[] contents`, `Vector2Int gridSize`, `string displayName`). LootTableSO는 M3/M4 |
| `WorldItem.cs` | MonoBehaviour | 바닥 아이템(그레이박스 상자 + Rigidbody). `static WorldItem Spawn(ItemInstance, Vector3 pos, Vector3 vel, InventoryConfigSO)`, 씬 배치용 `[SerializeField] ItemDefinitionSO definition` |

```csharp
public sealed class ItemGrid : IGridView
{
    public ItemGrid(GridRef id, Vector2Int size);
    public GridRef Ref { get; }  public Vector2Int Size { get; }
    public IReadOnlyList<PlacedItem> Items { get; }
    public bool IsEmpty { get; }
    public bool CanPlace(ItemInstance item, Vector2Int cell, bool rotated, ItemInstance ignore = null); // ignore: 같은 그리드 안에서 옮길 때 자기 자신
    public bool TryPlace(ItemInstance item, Vector2Int cell, bool rotated);
    public bool Remove(ItemInstance item);
    public bool Contains(ItemInstance item);
    public bool TryGetPlacement(ItemInstance item, out PlacedItem placed);
    // 위→아래, 왼→오른쪽. 같은 칸에서 회전 안 한 것 먼저.
    public bool TryFindFreeCell(ItemInstance item, out Vector2Int cell, out bool rotated);
}

public static class EquipRules
{
    public static bool Accepts(EquipSlot slot, ItemDefinitionSO definition);
}

public sealed class PlayerInventory : IInventoryService, IMagazineSource
{
    public PlayerInventory(InventoryConfigSO config);
    // IInventoryService 전부 (3.3)
    // IMagazineSource 전부: 조끼 → 주머니 (config.reloadFromBackpack이면 배낭까지)
    public bool TryAutoStore(ItemInstance item);   // 줍기·회수: 조끼 → 주머니 → 배낭 순 (TODO(GDD-?))
    public bool Remove(ItemInstance item);         // 어디에 있든 꺼냄
    public void OpenLoot(LootContainerModel loot);
    public void ConsumeMedicalUse(MedicalItem item);   // UsesLeft-- , 0이면 제거·퀵슬롯 해제
    public void SetItemLocked(ItemInstance item, bool locked);  // 사용 중 의약품 이동·버리기 거부
    public bool IsDeadLocked { get; set; }         // 사망 후 모든 명령 거부

    public event Action Changed;                               // → InventoryChanged (+ 무게가 바뀌었으면 CarriedWeightChanged)
    public event Action<EquipSlot, ItemInstance> EquipmentChanged;
    public event Action<ItemInstance> Dropped;                 // → WorldItem 생성
    public event Action<MedicalItem> UseRequested;             // → MedicalUseRequested
    public event Action<bool> LootSessionChanged;
}

public sealed class LootContainerModel : ILootView
{
    public LootContainerModel(string displayName, Vector2Int gridSize, IEnumerable<ItemInstance> contents, float searchTimePerItemSec);
    public ItemGrid Grid { get; }
    public string DisplayName { get; }
    public bool IsRevealed(ItemInstance item);
    public bool SearchComplete { get; }
    public int RevealedCount { get; }
    public bool Tick(float dt);   // 열려 있는 동안만 호출. 새로 드러난 게 있으면 true
}
```
규칙
- 저장 공간 = 주머니 그리드 4개(각 1×1) + 장착된 조끼 그리드 + 장착된 배낭 그리드. 1×2 이상은 주머니에 못 들어간다.
- `TryEquip`: 저장/루트에 있던 아이템을 슬롯으로. 조끼/배낭을 장착하면 `gridSize` 빈 그리드 생성, 해제하면(빈 경우만) 그리드 제거.
- 무게 = 장착 아이템 + 저장 공간 아이템(`WeightKg`, 무기는 장착 탄창 포함). 루트 컨테이너는 제외.
- 루트: 검색 전 아이템은 `TryMove/TryQuickMove/TryDrop` 거부. 빈 칸에 내 아이템을 넣는 건 허용. 검색은 열려 있는 동안만 진행, 닫으면 멈췄다가 다시 열면 이어서.
  드러나는 순서 = 처음 배치 순서. N개 → N × 0.5초.
- 사용 중(`SetItemLocked`) 의약품은 이동·버리기 불가.
- `PlayerInventoryBehaviour`
  - 직렬화: `InventoryConfigSO config`, `Transform viewOrigin`(카메라), 시작 장비 `StartingItem[] startingItems`
    (`[Serializable] struct StartingItem { ItemDefinitionSO item; bool equip; EquipSlot slot; MagazineDefinitionSO loadedMagazine; int quickSlot /* -1 = 없음 */; }`).
    시작 장비는 Awake에서 순서대로: equip이면 TryEquip(무기는 `loadedMagazine`으로 꽉 찬 탄창 장착), 아니면 TryAutoStore.
  - Awake에서 `Register<IInventoryService>`, `Register<IMagazineSource>`. Start에서 `CarriedWeightChanged`·장착 슬롯별 `EquipmentChanged` 1회 발행.
  - Update: 화면 없음·생존 시 `viewOrigin` 전방 `interactRangeM` 레이캐스트 → `IInteractable` 바뀔 때만 `InteractionPromptChanged`. F → `Interact`.
    4~7 → `TryUse(GetQuickSlot(i))`. 열린 루트가 있으면 `Tick` → 드러나면 `InventoryChanged`.
  - `ItemDropRequested` 구독 → `WorldItem.Spawn`. `MedicalUseStarted/Completed` → 잠금/차감. `PlayerDied` → `IsDeadLocked`, 루트 닫기.
  - UI가 화면을 닫으면(`ScreenStateChanged(None)`) 열린 루트를 닫는다.
- `WorldItem.Interact` → `TryAutoStore` 성공 시 Destroy. 실패면 그대로(TODO(GDD-?): "공간 없음" 표시 없음).
- `LootContainer.Interact` → `OpenLoot(model)`. 모델은 처음 열 때 한 번 `ItemFactory.Create`로 만든다.

### 5.4 UI (`Ashfall.UI`, UI Toolkit)
| 파일 | 종류 | 역할 |
|---|---|---|
| `HudConfigSO.cs` | SO | §4.4 |
| `ScreenStateModel.cs` | 순수 | 열린 화면 상태 머신 |
| `MagazineCheckToastModel.cs` | 순수 | RULE-04 문구·표시 시간 |
| `BodyStatusPresenter.cs` | 순수 | RULE-05 행 데이터(라벨·등급 문구·상태이상 문구·USS 클래스) |
| `InventoryDragModel.cs` | 순수 | 드래그 상태·회전·대상 칸 계산·명령 호출 |
| `GameUIRoot.cs` | MonoBehaviour | UIDocument 하나에 아래 뷰를 붙이고 입력·커서·버스 처리 |
| `HudView.cs`, `InventoryScreenView.cs`, `BodyStatusScreenView.cs` | 일반 클래스(VisualElement 조립) | 그리기만 |
| `Assets/_Project/Art/UI/GameUI.uxml`, `Ashfall.uss`, `AshfallTheme.tss`, `GamePanelSettings.asset` | 에셋 | `.uxml/.uss/.tss`는 텍스트라 직접 작성. `.asset`은 EditorTools가 생성 |

```csharp
public sealed class ScreenStateModel
{
    public ScreenKind Current { get; }
    public bool IsDead { get; set; }       // 사망 후 열기 거부, 열려 있으면 닫힘
    public bool ToggleInventory();         // 바뀌면 true. BodyStatus가 열려 있으면 그걸 닫고 Inventory를 연다
    public bool ToggleBodyStatus();
    public bool OpenInventory();           // 루트 열림 때
    public bool CloseAll();                // Esc
}
public sealed class MagazineCheckToastModel
{
    public MagazineCheckToastModel(HudConfigSO config);
    public string Text { get; }            // null = 표시 안 함
    public void OnStarted();               // 결과 문구 지움 (확인 중 별도 문구 없음)
    public void OnCompleted(MagazineFillLevel level);
    public void Tick(float dt);
    public static string TextFor(MagazineFillLevel level);   // Full "가득", Half "절반", AlmostEmpty "거의 없음", NoMagazine "탄창 없음"
}
public readonly struct BodyPartRow { public readonly BodyPart Part; public readonly string Label; public readonly string TierText; public readonly string TierClass; public readonly IReadOnlyList<string> StatusTexts; }
public static class BodyStatusPresenter
{
    // Label: 머리/흉부/복부/왼팔/오른팔/왼다리/오른다리. Tier: 정상/부상/중상/파괴. 상태: 출혈/중출혈/골절. 숫자 없음.
    public static IReadOnlyList<BodyPartRow> Build(IBodyStatusView view);
}
public sealed class InventoryDragModel
{
    public InventoryDragModel(IInventoryService inventory);
    public ItemInstance Dragging { get; }
    public bool Rotated { get; }
    public void Begin(ItemInstance item);          // 현재 배치의 회전으로 시작
    public void ToggleRotate();                    // R
    public bool CanDropAt(GridRef grid, Vector2Int cell);
    public bool DropAt(GridRef grid, Vector2Int cell);  // TryMove
    public bool DropOnSlot(EquipSlot slot);        // TryEquip
    public bool DropOutside();                     // TryDrop
    public void Cancel();
}
```
- `GameUIRoot`
  - 입력: Player 맵 `Inventory`(Tab), `BodyStatus`(H), UI 맵 `Cancel`(Esc), `RotateItem`(R, 드래그 중만), Player 맵 `QuickSlot1~4`(인벤토리 열림 + 아이템 위에 마우스 → `TryAssignQuickSlot`). UI 맵은 Awake에서 Enable.
  - 화면 상태가 바뀌면 `ScreenStateChanged` 발행 + 커서: None이면 Locked/숨김, 아니면 None/보임. **커서는 UI만 바꾼다.**
  - 구독: `InventoryChanged`·`LootSessionChanged(true → OpenInventory)`·`PlayerBodyStatusChanged`·`MagazineCheck*`·`InteractionPromptChanged`·`PlayerDied`.
  - 서비스는 화면을 열 때 `TryGet`(없으면 화면 안 열림, 경고 한 번).
- 인벤토리 화면: 왼쪽 장착 슬롯 7개, 가운데 저장 공간(주머니 4칸 + 조끼 + 배낭), 오른쪽 루트 패널(열렸을 때, 검색 전 칸은 "?" + "검색 중…" 문구), 아래 퀵슬롯 4칸(4~7).
  칸 크기는 USS 변수. 아이콘 없으면 `displayName` 텍스트 상자. 무게 숫자는 표시하지 않는다(정보 희소성, TODO(GDD-?)).
  드래그: PointerDown → Begin, PointerMove로 대상 칸 강조(가능 `Bone`/불가 `Ember`), PointerUp → DropAt/DropOnSlot/DropOutside. 더블클릭 = 의약품 `TryUse`, 루트↔내 공간 `TryQuickMove`는 Ctrl+클릭.
- H 화면: 몸 실루엣 대신 7행 목록(그레이박스). 등급별 USS 클래스(색 토큰). 수치·퍼센트 금지.
- HUD: 탄창 확인 문구(화면 아래 중앙, `magCheckResultSec` 동안), 상호작용 프롬프트("F  열기: 상자" / "F  줍기: 붕대"). 그 밖 HUD 없음.
- 색은 §10.1 토큰을 `Ashfall.uss`의 `:root` 변수로(`--ash-black` 등), 텍스트 `Bone`.
- 확인 필요: Unity 6 UI Toolkit 런타임 포인터 입력이 EventSystem 없이 새 Input System에서 동작하는지. 안 되면 EditorTools가 `EventSystem` + `InputSystemUIInputModule`을 씬에 추가(`com.unity.ugui` 설치돼 있음).
- `PanelSettings`를 스크립트로 만들면 테마가 비어 경고가 난다 → `AshfallTheme.tss`(내용 `@import url("unity-theme://default");`)를 텍스트로 만들고 EditorTools가 `themeStyleSheet`에 연결.

### 5.5 입력 (`Assets/InputSystem_Actions.inputactions`, JSON 직접 편집)
- 각 action·binding에 새 GUID(`[guid]::NewGuid()` 등). binding `groups`는 `";Keyboard&Mouse"`(기존과 동일). action `type: "Button"`, `interactions: ""`.
- **Player 맵에 추가**

| action | binding | 사용 |
|---|---|---|
| `CheckMagazine` | `<Keyboard>/t` | Weapons (RULE-04) |
| `Interact` | `<Keyboard>/f` | Inventory |
| `Inventory` | `<Keyboard>/tab` | UI |
| `BodyStatus` | `<Keyboard>/h` | UI |
| `QuickSlot1`~`QuickSlot4` | `<Keyboard>/4`~`<Keyboard>/7` | Inventory(사용), UI(배정) |

- **UI 맵에 추가**: `RotateItem` = `<Keyboard>/r`. Esc는 기존 `Cancel`(`*/{Cancel}`) 사용.
- 맵을 끄고 켜지 않는다. 화면이 열려 있으면 각 기능이 `ScreenStateChanged`로 게임플레이 입력을 무시한다(R 재장전 ↔ R 회전 충돌도 이걸로 해결).
- T 길게 누르기(고장 해제)는 M7. 지금은 단순 Button.

### 5.6 EditorTools (`Ashfall.EditorTools`)
| 파일 | 메뉴 / -executeMethod | 담당 |
|---|---|---|
| `SetupUtil.cs` | (M1Setup의 `CreateAssetIfMissing`·`EnsureFolder`·`SetFields`를 공용으로 이동, 동작 동일) | gameplay |
| `M2DataSetup.cs` | `Ashfall/Setup/Create M2 Data` / `Ashfall.EditorTools.M2DataSetup.CreateData` | data-designer |
| `M2SceneSetup.cs` | `Ashfall/Setup/Create M2 Test Scene` / `Ashfall.EditorTools.M2SceneSetup.CreateAll` | gameplay (+ UI 연결 부분 ui) |

- `M2DataSetup.CreateData`: §4.5 아이템, 3개 config SO, `PlayerConfig.asset`의 §4.2/§4.3 필드 채우기(**`headMaxHp <= 0`일 때만** — 이미 조정한 값 보존). 기존 에셋 덮어쓰지 않음.
- `M2SceneSetup.CreateAll`: CreateData(M1·M2) → 씬 `Assets/_Project/Scenes/Test/M2_HealthInventory.unity`(있으면 건너뜀). 빌드 설정에 넣지 않는다.
  - 바닥: 60m×60m 평면(큐브). 지형 불필요.
  - `ProjectileSystem`.
  - Player: M1과 같은 구성 + `PlayerHealth` + `PlayerInventoryBehaviour`(viewOrigin = Camera).
    시작 장비: WPN-RIF-01(Primary1, MAG-556-30 장착), RIG-01(장착) + MAG-556-30 ×3, BAG-01(장착), MED-BND-01(퀵 0), MED-TRQ-01(퀵 1), MED-SPL-01(퀵 2). 장착하지 않는 것은 `TryAutoStore` 순서(조끼 → 주머니 → 배낭)대로 들어간다. 이 순서면 탄창 3개 + 붕대·지혈대가 조끼(4×2)에, 부목(1×2)은 배낭에 들어간다. 테스트는 칸 위치가 아니라 "저장 공간 안에 있음"만 단언한다.
    무게 확인: 상자 C의 엔진 부품 2개(40kg, 3×2는 배낭 4×5에 위아래로 2개 들어감) + 조끼·배낭·의약품 ≈ 43kg → 40kg 초과로 질주 불가.
  - `WeaponController.handling` = WeaponHandlingConfig.
  - 루트 상자 3개(1m 큐브 + `LootContainer`): A(2개: ARM-HLM-01, JNK-03 → 1초), B(6개: 의약품 3종 + JNK-01 + MAG-556-30 + JNK-03 → 3초), C(JNK-02 엔진 부품 20kg ×2 → 무게 패널티 확인).
  - 바닥 아이템: ARM-BDY-01, MAG-556-30 각 1개(`WorldItem`).
  - `TestTurret` 1개: 플레이어 시작점에서 15m, 바닥에 붉은 사각형 구역(반경 4m) 안에 서 있으면 2초마다 5.56 1발(높이 0.3~1.75m 무작위 조준). 구역 밖이면 쏘지 않는다.
  - HitTarget 1개(사격 확인용).
  - UI: `GameUI` GameObject(`UIDocument` + `GameUIRoot`), PanelSettings·UXML 연결, `HudConfig` 연결.
- 재실행 안전(이미 있으면 건너뜀) — M1Setup과 같은 원칙.

---

## 6. 작업 분할 (질문 5)

순서가 있다. 1단계가 끝나 컴파일되어야 2단계를 병렬로 시작한다.

| 단계 | 담당 | 작업 |
|---|---|---|
| 1. 계약 | gameplay-programmer | §3 Core 전부(GameManager, EventBus, ServiceRegistry, 인터페이스, enum, 이벤트, ItemInstance 계열, 새 SO 3개), `Magazine` 이동(.meta 같이), §5.5 입력 액션 추가, §2 asmdef 변경, `PlayerConfigSO`·`WeaponHandlingConfigSO`·`InventoryConfigSO`·`HudConfigSO` 클래스(필드만), `WeaponModel` 시그니처 변경 + 기존 테스트 컴파일 수정, `TestData`에 M2 도우미(`Player()`에 §4.2/4.3 값, `Bandage()` 등). EventBus·ServiceRegistry EditMode 테스트. **배치모드 EditMode 통과로 마감** |
| 2a | gameplay-programmer | Player(§5.1), Weapons(§5.2), Inventory(§5.3) + 각 EditMode 테스트 |
| 2b | ui-programmer | UI(§5.4) 순수 모델 + 테스트, UXML/USS/TSS, 뷰. 가짜 `IInventoryService`/`IBodyStatusView`로 테스트 |
| 2c | data-designer | `M2DataSetup`(§4, §5.6), `SetupUtil` 사용. 데이터 검증 EditMode 테스트(에셋 값 = GDD 표) |
| 3 | gameplay-programmer (+ui-programmer가 UI 오브젝트 부분) | `M2SceneSetup`, 배치모드 `-executeMethod`로 씬 생성 |
| 4 | test-engineer | PlayMode 통합 테스트(§7), EditMode·PlayMode 전체 실행, AC 체크 |
| 5 | code-reviewer | 의존 방향(기능→Core만), 싱글톤 1개, 하드코딩 수치, 옛 Input API, 이벤트 해지 누락 |

파일 소유가 겹치지 않게: `TestData.cs`는 1단계에서만 고치고 이후 추가는 각자 새 파일(`HealthTestData.cs` 등)로.

---

## 7. AC별 테스트 계획

### AC1 — §4.2 부위 체력, §4.3 출혈·골절
EditMode (`Tests/EditMode/BodyHealthTests.cs`, `MedicalTests.cs`, `BodyPartResolverTests.cs`, `LocomotionConditionTests.cs`)
- 부위별 최대 HP = 35/80/70/60/65.
- 머리 35 피해 → 사망(Cause Head). 흉부 80 → 사망. 팔 60 → 사망 아님, 파괴.
- 복부 파괴 후 팔 10 피해 → 15 감소.
- 파괴된 왼팔에 20 → 흉부·복부 각 7 감소. 복부도 파괴면 흉부 14.
- 팔 1개 파괴 → `AimSwayMultiplier` 2.0, 2개 → 3.0. 다리 파괴 → 0.6·질주 불가. 골절 다리 → 0.8, 파괴+골절 → 0.6(곱 아님).
- 출혈 1초 → -1, 중출혈 → -3, 둘 다 → -4. 흉부 출혈로 0 → 사망.
- 고정 난수(`NextDouble` 재정의한 `System.Random` 하위 클래스)로 상태이상 굴림: 0.1 → 중출혈, 0.3 → 출혈, 0.9 → 없음. 피해 < 최소값 → 굴리지 않음. 머리·몸통은 골절 없음.
- 붕대: 출혈만 해제(중출혈 남음). 지혈대: 중출혈. 부목: 골절. 필요 없으면 `NotNeeded`. 사용 시간 동안 효과 없음, 끝나면 적용. 사용 중 다른 것 → `Busy`.
- `PlayerInventory.ConsumeMedicalUse` → uses 1 → 제거, 퀵슬롯 비움.
- 부위 판정: 높이 비율별 7부위, 좌/우.
- `LocomotionModel.Speed(..., condition)` = 기본 × 무게 × 상태. 기존 오버로드 결과 불변.
- `PlayerBodyStatusChanged`는 등급이 바뀔 때만(출혈 1틱마다 아님) — 모델 `StatusChanged` 호출 횟수로 확인.
PlayMode (`Tests/PlayMode/PlayerDamageTests.cs`)
- 빈 씬에 `PlayerHealth`+`CharacterController` 플레이어, `ProjectileSystem`으로 머리 높이 수평 발사 → `IBodyStatusView.GetTier(Head)` 변화/사망, 다리 높이 → 다리.
- 자기 탄은 안 맞음(shooterRoot).
사람 확인: 터렛 구역에 서서 맞기 → H로 부위 상태·출혈 확인 → 4/5/6으로 치료 → H에서 해제 확인. 다리 파괴 후 질주 안 됨·느려짐 체감. 팔 파괴 후 조준 흔들림 체감.

### AC2 — 그리드 인벤토리, 회전, 장착 슬롯
EditMode (`ItemGridTests.cs`, `PlayerInventoryTests.cs`, `EquipRulesTests.cs`)
- 경계 밖·겹침 거부, 2×1 회전 → 1×2로 들어가는 자리, 같은 그리드 안 이동(자기 자신 무시), `TryFindFreeCell` 순서.
- 주머니 4개 각 1×1: 1×1 4개 들어감, 1×2 거부.
- 조끼 미장착 → 저장 공간은 주머니만. 장착 → 그리드 추가. 내용물 있는 조끼 해제 거부.
- 슬롯 판정 표(§3.5) 전부, 차 있는 슬롯 TryEquip 실패.
- 무게 = 장착 + 저장(무기 장착 탄창 포함), 루트 제외. 변경 시 `Changed`.
- `IMagazineSource`: 조끼·주머니에서 가장 많이 든 탄창, 배낭 제외(설정 false), `TryStore` 공간 없으면 false.
- `WeaponModel` + `LocalMagazineSource`: 일반 재장전 회수, 빠른 재장전 `MagazineDropped`, 회수 공간 없음 → `MagazineDropped`.
- UI: `InventoryDragModel` 회전 토글 후 `DropAt`이 `rotated=true`로 호출(가짜 서비스), 실패 시 원위치.
- 입력 에셋: `InputSystem_Actions`에 §5.5 액션·바인딩 존재(EditMode, `AssetDatabase` 로드).
PlayMode (`InventoryFlowTests.cs`, M2 씬 로드)
- 씬 로드 30프레임 오류 없음, 서비스 3종 등록, 시작 장비 배치(조끼 탄창 3, 장착 탄창 30발).
- 무거운 아이템 저장 → `PlayerMotor`가 받은 무게로 `Locomotion.Speed` 감소.
- 빠른 재장전 진행 → `WorldItem` 1개 생성 → `Interact` 호출 → 다시 저장 공간에.
- Primary1 무기 해제 → `WeaponController` 비무장(사격 안 됨).
- 씬 언로드 후 `EventBus.SubscriberCount` 원래대로(누수 없음).
사람 확인: Tab 열기·닫기, 커서 표시/잠금, 드래그·R 회전·슬롯 장착·바깥에 버리기, 화면 열린 동안 시점·사격 안 됨.

### AC3 — 루트 컨테이너 검색 연출
EditMode (`LootSearchTests.cs`): N개 → `Tick` 누적 0.5×N초에 완료, 0.49초에 0개, 0.5초에 1개. 검색 전 아이템 이동 거부, 드러난 것은 `TryQuickMove` 가능. 닫은 동안 진행 없음.
PlayMode: M2 씬에서 상자 B 열기(`Interact` 직접 호출) → 3초 뒤 `SearchComplete`, `LootSessionChanged(true)` 발행.
사람 확인: 상자 앞 F 프롬프트, F로 열면 인벤토리 + 루트 패널, 아이템이 0.5초마다 하나씩 드러남, 닫았다 다시 열면 이어서.

### AC4 — RULE-04, RULE-05
EditMode: `MagazineCheckModel` 0.79초 미완료/0.8초 완료, 재장전 중 시작 거부, Cancel 시 결과 없음. `Classify`: 30/30·20/30 → Full, 15/30 → Half, 5/30·0/30 → AlmostEmpty, null → NoMagazine. `MagazineCheckToastModel.TextFor` 문구, 표시 시간 후 null.
`BodyStatusPresenter.Build` 결과 모든 문자열에 숫자(0-9, %)가 없음. `IBodyStatusView`에 HP 수치 멤버가 없음(리플렉션으로 float 반환 멤버 없음 확인).
`ScreenStateModel`: Tab/H 토글·전환·Esc·사망 시 닫힘.
사람 확인: T 누르고 0.8초 뒤 문구가 잠깐 표시, 평소 HUD에 탄수 없음. H 화면에 숫자 없음.

---

## 8. 결정 표 (요약, CHANGELOG에도 반영)

| # | 결정 |
|---|---|
| D1 | 이벤트 버스 = Core `EventBus`(동기, struct 이벤트), `GameManager.Events`. GameManager는 자동 생성 싱글톤 |
| D2 | UI·Weapons는 Core 인터페이스(`IInventoryService`, `IMagazineSource`, `IBodyStatusView`)를 `GameManager.Services`에서 얻어 조회·명령, 변경은 이벤트로 알림 |
| D3 | 부위 판정 = 캡슐 피격 지점 높이·좌우(`BodyPartResolver`). `DamageInfo` 변경 없음, 히트박스·레이어 없음 |
| D4 | `ItemInstance` 계열을 Core에, `Magazine`을 Weapons → Core로 이동. 회전은 배치에 저장 |
| D5 | 조끼·배낭 내용물은 `PlayerInventory`가 슬롯별 소유. 내용물 있으면 해제·버리기 불가 |
| D6 | §4.2/4.3 수치는 `PlayerConfigSO`에 추가(§14 따름). 새 SO: `WeaponHandlingConfigSO`, `InventoryConfigSO`, `HudConfigSO`, `ContainerDefinitionSO`. `ArmorDefinitionSO.kind` 추가 |
| D7 | 재장전 탄창은 조끼+주머니에서만. 일반 재장전 공간 없으면 바닥에 |
| D8 | 의약품 대상 부위는 자동 선택. 사용 중 이동·사격 제한·취소 없음 |
| D9 | 화면 열림 중 게임플레이 입력 차단(`ScreenStateChanged`), 이동 포함. 액션 맵은 끄지 않음. 커서는 UI만 관리 |
| D10 | 입력: Player 맵 + `CheckMagazine/Interact/Inventory/BodyStatus/QuickSlot1~4`, UI 맵 + `RotateItem`(R) |
| D11 | 무기 교체 1/2/3 미구현(무기 1종). `WeaponController`는 Primary1만 따른다 |
| D12 | 조준 흔들림 신설(기본 0.15°), 체력 배율 곱. 무기 루트 회전으로 뷰모델·탄도 같이 흔들림 |
| D13 | UI 레이아웃·스타일·PanelSettings는 `Assets/_Project/Art/UI/` |

**문서와 어긋나는 점 (문서 수정 없이 CHANGELOG에 해석으로 기록)**
- §13.2는 "방어구"를 Weapons 폴더에 두지만 `ArmorDefinitionSO`는 Inventory도 써서 Core에 둔다(M1 아이템 SO 결정과 같은 이유).
- §12.1 R = 재장전, §6 R = 회전 → 화면 열림 여부로 구분.
- §12.1 T = 탄 확인, §5.1 T 길게 = 고장 해제 → M2는 누름만, 길게는 M7.
- §12.1 무기 교체 1/2/3은 M2에서 동작하지 않음.

---

## 9. CHANGELOG_GDD.md 문안 초안 (질문 6)

```markdown
## 2026-10-21 — M2 설계 결정 (§4.2, §4.3, §6, §12.1, §13.2, §13.3, §14, RULE-04, RULE-05)
문서 본문은 바꾸지 않았다. 아래는 M2 구현을 위한 해석이며, 기획 확정 시 문서에 반영하거나 되돌린다.
- **§13.3 이벤트 버스·GameManager:** `Ashfall.Core`에 `EventBus`(동기, `readonly struct` 이벤트)와 `ServiceRegistry`를 두고,
  유일한 싱글톤 `GameManager`가 둘을 가진다. `GameManager`는 `RuntimeInitializeOnLoadMethod`로 자동 생성된다(Boot를 거치지 않는 테스트 씬 대비).
  다른 기능의 상태를 읽고 조작할 때는 Core 인터페이스(`IInventoryService`, `IMagazineSource`, `IBodyStatusView`)를 레지스트리에서 얻고,
  변경 알림은 이벤트로 받는다. 사유: 드래그 미리보기처럼 동기 판정이 필요하고, 배치 규칙을 UI에 복제하지 않기 위해.
- **§4.2 부위 판정:** 플레이어는 히트박스 콜라이더 없이 캡슐 피격 지점의 높이·좌우로 7부위를 판정한다(그레이박스 근사, 엎드림 부정확).
- **§4.2 해석:** 피격으로 0 아래가 된 초과분은 버리고, 이미 파괴된 부위가 받는 피해만 70%를 흉부·복부에 균등 분산(복부 파괴 시 흉부로).
  복부 파괴 1.5배는 피격 피해에만 적용, 출혈 지속 피해에는 미적용. 팔 파괴 흔들림 +100%는 팔마다 더한다. 다리 패널티는 두 다리 중 나쁜 쪽 하나만.
  복부 파괴 "탈수 가속"은 탈수(M7)가 없어 미구현.
- **§4.3 해석:** 출혈·중출혈은 모든 부위, 골절은 팔·다리만. 피격마다 확률 굴림. 의약품 대상 부위는 자동 선택(해당 상태가 있는 첫 부위).
- **§14 스키마에 없는 추가:** `ContainerDefinitionSO { kind, gridSize }`(조끼·배낭), `ArmorDefinitionSO.kind`(헬멧/방탄복),
  `WeaponHandlingConfigSO`(탄창 확인 시간·구간, 조준 흔들림), `InventoryConfigSO`(주머니, 검색 시간, 상호작용 거리, 버리기),
  `HudConfigSO`(탄창 확인 문구 표시 시간). §4.2·§4.3 수치는 §14에 따라 `PlayerConfigSO`에 추가.
  런타임 아이템(`ItemInstance`, `Magazine`, `WeaponItem`, `MedicalItem`)은 `Ashfall.Core`에 둔다(`Magazine`은 Weapons에서 이동).
- **§13.2 방어구 위치:** `ArmorDefinitionSO`는 Inventory도 쓰므로 Weapons가 아니라 Core에 둔다(M1 아이템 SO 결정과 같은 사유). 관통 계산(§5.3)은 M2 범위 밖.
- **§6 해석:** 회전은 아이템이 아니라 그리드 배치에 저장. 내용물이 있는 조끼·배낭은 해제·버리기 불가. 차 있는 슬롯에 장착하면 실패(교환 없음).
  재장전은 전술조끼·주머니의 탄창만 쓴다(배낭 제외). 일반 재장전 시 회수 공간이 없으면 바닥에 떨어뜨린다.
  루트 검색은 열려 있는 동안만 진행, 검색 전 아이템은 옮길 수 없다. 줍기 자동 배치 순서: 조끼 → 주머니 → 배낭.
- **§12.1 키:** R은 화면이 닫혀 있으면 재장전, 인벤토리 드래그 중이면 회전. T 길게(고장 해제, §5.1)는 M7. 무기 교체 1/2/3은 무기가 1종이라 M2에서 미구현.
  퀵슬롯 4~7은 인벤토리에서 아이템 위에 마우스를 두고 숫자로 배정. 화면이 열려 있으면 이동을 포함한 게임플레이 입력을 막는다.
- **RULE-04:** 구간은 탄 수/용량 ≥ 0.67 "가득", ≥ 0.34 "절반", 그 아래 "거의 없음"(0발 포함). 결과 문구 2초 표시. 사격·재장전 시 확인 취소.
- **RULE-05:** 부위 상태 화면은 등급(정상/부상/중상/파괴)과 상태이상 이름만 표시. 등급 경계 HP 비율 0.75 / 0.35.
- **조준 흔들림:** M1에는 없던 기본 흔들림(0.15°, 0.5Hz)을 추가해 팔 파괴·골절 배율이 적용되게 했다.
- **GDD에 수치가 없어 임시값을 넣은 항목 (TODO(GDD-?)):** 출혈 확률 0.35 / 중출혈 0.15 (피해 10 이상), 골절 확률 0.2 (피해 20 이상),
  골절 이동 ×0.8 / 조준 흔들림 +0.5, 부위 판정 높이 비율(0.87/0.62/0.45, 팔 0.6R), 의약품 사용 시간(붕대 3s, 지혈대 4s, 부목 5s)·크기·무게,
  조끼 그리드 4×2, 배낭 4×5, 헬멧·방탄복 등급 3, 상호작용 거리 2m, 아이템 ID(MED-*, RIG-01, BAG-01, ARM-*, JNK-*).
```

---

## 10. 사람이 에디터에서 할 일 (구현 완료 후 안내용)
1. Unity Hub 켜고 프로젝트 열기 → 메뉴 `Ashfall/Setup/Create M2 Test Scene` (배치모드로 이미 만들어졌으면 생략).
2. `Assets/_Project/Scenes/Test/M2_HealthInventory.unity` 열고 Play.
3. 확인: Tab 인벤토리(드래그·R 회전·장착·버리기), 상자 F → 0.5초당 1개 검색, 바닥 탄창 F 줍기, R 두 번(빠른 재장전) → 탄창이 바닥에 떨어지고 다시 주울 수 있음,
   T → 0.8초 뒤 "가득/절반/거의 없음", 터렛 구역에서 맞은 뒤 H로 부위 상태(숫자 없음), 4/5/6으로 치료, 엔진 부품 2개 들고 느려짐·40kg 넘으면 질주 불가.
4. 조작감 이상(커서, 입력 차단, 흔들림 크기)은 `Data/` 에셋 값으로 조정.

---

## 1단계 구현 메모 (gameplay-programmer, 계약 단계)

2단계 이후 에이전트가 알아야 할 것. 설계와 다른 점과 이미 만들어 둔 것.

**설계보다 앞당겨 만든 것 (2a는 다시 만들지 말 것)**
- `Weapons/LocalMagazineSource.cs` — 설계 §5.2(2a 몫)였지만 `WeaponModel` 새 시그니처로 기존 테스트·`WeaponController`를 컴파일하려면 필요해서 1단계에서 만들었다. API는 설계 그대로(`Magazines`, `TryStore`는 같은 탄창 중복이 아니면 항상 true). 꺼낼 때 `!IsEmpty && Definition.ammo == ammo`인 것 중 탄 수 최대.
- `WeaponModel` 동작까지 완성: 완료 시 `source.TakeFullest` → 이전 탄창은 빠른 재장전이면 `MagazineDropped`, 일반이면 `source.TryStore` 실패 시 `MagazineDropped`. 꺼낸 **다음** 회수하므로 꺼낸 자리를 회수에 쓸 수 있다. `TakeFullest`가 null이면(그사이 탄창이 사라짐) 아무것도 바뀌지 않는다.
- `WeaponModel.CancelReload()` 추가(설계에 없음) — "무기를 슬롯에서 뺄 때 재장전 중이면 취소"용. 진행 중 재장전만 버리고 탄창은 그대로.
- `WeaponController`는 최소 수정만: `LocalMagazineSource` + `new WeaponItem(weapon, Magazine.Full(magazine))`로 M1과 같은 지급. `public LocalMagazineSource MagazineSource` 노출(인벤토리 연결 시 2a가 null로 두거나 정리). 인벤토리 바인딩·T·흔들림·버림 이벤트·조작 차단은 전부 2a 몫으로 남아 있다(`TODO(M2 2a)` 주석).
- `GreyboxSceneTests`의 `Spares` 단언은 `weapon.MagazineSource.Magazines.Count`로 바꿨다.

**Core 세부 (설계에 없던 작은 추가)**
- `ServiceRegistry.Clear()` 추가. `GameManager.OnDestroy`(자신이 Instance일 때)에서 `Events.Clear()`·`Services.Clear()` 후 `Instance = null`. 중복 GameManager는 Awake에서 Destroy.
- `ServiceRegistry.Register(null)`은 `ArgumentNullException`. `EventBus.Subscribe(null)`도 예외, `Unsubscribe(null)`은 무시.
- `PlacedItem(item, cell, rotated)` 생성자, `ItemLocation` 생성자 + `ItemLocation.Equipped(slot)`·`ItemLocation.InGrid(grid, cell, rotated)` 팩토리.
- `GridRef`·`ConditionModifiers`는 `==`/`!=`/`Equals`/`GetHashCode` 구현. `ConditionModifiers`는 float 정확 비교(같은 계산 결과끼리 비교 전제).
- `ItemInstance(null)`은 `ArgumentNullException`. `ToString()` = `"Magazine(MAG-556-30)"` 형식(로그용).
- 하위 클래스의 `new Definition`은 같은 객체를 더 구체적인 타입으로 돌려준다. `ItemInstance` 타입 변수로 읽으면 `ItemDefinitionSO`.
- 이벤트 struct는 전부 생성자를 가진다(필드 순서 = 설계 §3.4 표 순서). 필드 없는 이벤트는 `new InventoryChanged()`.
- `MedicalDefinitionSO.uses` 기본 1, `cures`·`ArmorDefinitionSO.covers` 기본 빈 배열, `ContainerDefinitionSO.gridSize` 기본 (1,1).

**설정 SO 기본값 (2c data-designer 주의)**
- `PlayerConfigSO`의 M2 필드, `WeaponHandlingConfigSO`·`InventoryConfigSO`·`HudConfigSO`의 모든 필드는 **필드 초기값 = 설계 §4 값**이다(`CreateInstance`만 해도 설계값).
- 유니티는 YAML에 없는 필드를 초기값으로 둔다. 따라서 기존 `PlayerConfig.asset`을 로드하면 `headMaxHp`가 이미 35로 보인다 → 설계 §5.6의 "`headMaxHp <= 0`일 때만 채우기" 조건은 기존 에셋에서 참이 되지 않는다. 값은 같으므로 결과는 동일하지만, 에셋 파일에 필드를 실제로 기록하려면 `EditorUtility.SetDirty` + `AssetDatabase.SaveAssets`로 한 번 저장해야 한다(필드를 채우지 않아도 저장만 하면 기록된다).
- `PlayerConfigSO.MaxHp(BodyPart)` 도우미 추가(설계 §5.1 표에 있던 것).

**테스트 도우미 (`Tests/EditMode/TestData.cs`, 이후 수정 금지)**
- `Player()`(§4.1 + §4.2/§4.3 + 임시값 전부), `Rifle()`(cls AssaultRifle, size 4×2, weight 0), `Pistol()`(Secondary 판정용, 9mm), `Magazine30()`(size 1×2), `PistolMagazine()`(9mm 15발, 1×1).
- 의약품: `Bandage()`·`Tourniquet()`·`Splint()`, 일반형 `Medical(id, name, size, kg, useTime, cure, heal = 0, uses = 1)`.
- 장비: `Vest()`(그리드 4×2)·`Backpack()`(4×5)·`Container(...)`, `Helmet()`·`BodyArmor()`·`Armor(...)`.
- 잡동사니: `Pipe()`(1×3, 1kg)·`EnginePart()`(3×2, 20kg)·`Wires()`(1×1, 0.3kg)·`Item(id, name, size, kg, rarity = Scrap)`.
- 설정: `WeaponHandling()`·`Inventory()`·`Hud()`.
- `FixedRandom(params double[])` — `System.Random` 하위 클래스, `NextDouble()`이 값을 차례로 돌려주고 다 쓰면 마지막 값 반복(상태이상 굴림 테스트용). `Next(...)`도 같은 값 기반.
- 1단계 테스트 파일: `EventBusTests.cs`(EventBus·ServiceRegistry·ConditionModifiers 동등성), `ItemInstanceTests.cs`(ItemFactory·무게·의약품 횟수·LocalMagazineSource·회수 공간 없음 → 버림·탄 불일치 거부·CancelReload).

**입력 (§5.5)**
- `InputSystem_Actions.inputactions`에 Player 맵 `CheckMagazine`(T)·`Interact`(F)·`Inventory`(Tab)·`BodyStatus`(H)·`QuickSlot1~4`(4~7), UI 맵 `RotateItem`(R) 추가. 새 GUID 18개, 중복 없음 확인.
- 입력 에셋 존재 테스트(AC2 계획)는 만들지 않았다. 테스트 asmdef가 `Unity.InputSystem`을 참조하지 않으므로(설계 §2 "변경 없음") 4단계에서 필요하면 asmdef에 `Unity.InputSystem`을 추가하거나 파일 텍스트를 JSON으로 읽어 확인한다.

**asmdef**: 설계 §2 그대로(Inventory·UI + `Unity.InputSystem`, EditorTools + `Ashfall.Inventory`·`Ashfall.UI`). Inventory·UI 폴더에는 지금 각각 `InventoryConfigSO.cs`·`HudConfigSO.cs`가 있어 컴파일된다.

**검증**: 배치모드 EditMode 종료 코드 0, 48/48 통과(EventBus 12, ServiceRegistry 7, ItemInstance 9, Weapon 10, PlayerMovement 11, BuildSettings 3, Smoke 2, Addressables 스텁 2). PlayMode는 실행하지 않았다(`GreyboxSceneTests` 단언만 고쳐 컴파일 확인).
`Magazine.cs`/`.meta`는 `git mv`로 이동해 스테이징된 상태(GUID 유지), 나머지는 미커밋.

---

## 2c 요청 (data-designer)

**SO 필드 요청: 없음.** 설계 §4의 모든 값이 기존 SO 필드에 들어간다.

참고 (3단계·4단계 작업자)
- `M2DataSetup.CreateData()`(메뉴 `Ashfall/Setup/Create M2 Data`)는 맨 앞에서 `M1Setup.CreateData()`를 부른다. `M2SceneSetup`은 `M2DataSetup.CreateData()`만 불러도 M1·M2 데이터가 다 생긴다. `CreateAll()`은 같은 동작의 배치모드용 별칭. 3단계에서 `M1Setup.CreateData`의 이름·접근자를 바꾸면 `M2DataSetup`도 고칠 것.
- `SetupUtil.cs`가 아직 없어 `M2DataSetup` 안에 `CreateAssetIfMissing`·`EnsureFolder`를 private으로 복제했다(M1Setup과 동작 동일, `// TODO` 표시). 3단계에서 SetupUtil을 만들면 교체해도 된다.
- 경로 상수는 `M2DataSetup`의 `public const`(`PlayerConfigPath`, `WeaponHandlingConfigPath`, `InventoryConfigPath`, `HudConfigPath`, `MedicalFolder`=`Items/Medical`, `GearFolder`=`Items/Gear`(RIG-01·BAG-01), `ArmorFolder`=`Items/Armor`, `JunkFolder`=`Items/Junk`). 씬 설정에서 그대로 쓸 것.
- PlayerConfig 처리(1단계 메모 5): 파일 텍스트에 `headMaxHp:`가 없을 때만 §4.2/§4.3 값을 넣고 `SetDirty` → `SaveAssets`. 이미 기록돼 있으면 건드리지 않는다(조정값 보존, 재실행 시 파일 불변).
- 희귀도·기본 가격은 GDD·설계 모두에 없어 기본값(Scrap, 0). TODO(GDD-?).
- 데이터 검증 테스트 `Tests/EditMode/M2DataAssetTests.cs`는 **메뉴를 먼저 실행해야** 통과한다(에셋 없으면 실패). 멱등 테스트는 `EditorApplication.ExecuteMenuItem`으로 메뉴를 다시 실행해 에셋 파일 텍스트가 그대로인지 본다(에셋이 없으면 Inconclusive).

---

## 2b 구현 메모 (ui-programmer, UI 단계)

Core 변경 없음. "## 2b 요청" 없음. Unity는 실행하지 않았다(컴파일·테스트 미확인).

**파일**
- `Scripts/UI/`: `ScreenStateModel`, `MagazineCheckToastModel`, `InteractionPromptModel`, `BodyStatusPresenter`(+`BodyPartRow`), `InventoryDragModel`(+`DropTarget`, `DropTargetKind`), `InventoryLayout`(px↔칸), `UiText`(고정 문구), `GameUIPresenter`(순수: 버스 구독·화면 상태·ScreenStateChanged 발행), `GameUIRoot`(MonoBehaviour), `HudView`, `InventoryScreenView`, `BodyStatusScreenView`. `HudConfigSO`에 `dragThresholdPx = 4`(TODO(GDD-?)) 추가.
- `Art/UI/`: `GameUI.uxml`(`<Style src="Ashfall.uss" />` 상대 경로), `Ashfall.uss`(§10.1 팔레트 변수 `--ash-black` 등 + `--ash-cell-size: 48px`), `AshfallTheme.tss`(`@import url("unity-theme://default");`).
- 테스트: `Tests/EditMode/UiModelTests.cs`, `Tests/EditMode/UiTestFakes.cs`(가짜 `IInventoryService`/`ILootView`/`IBodyStatusView`).

**설계와 다른 점 / 추가**
- 설계의 GameUIRoot 로직을 순수 `GameUIPresenter(EventBus, ServiceRegistry, HudConfigSO)`로 뺐다(EditMode 테스트용). `GameUIRoot.Presenter`로 노출(PlayMode 테스트에서 `Presenter.Screens.Current` 등 확인 가능).
- `InventoryDragModel.Begin`은 `bool` 반환(검색 전 루트 아이템이면 false). `CanDropOnSlot`, `CanDrop(DropTarget)`, `Drop(DropTarget)`, `Size` 추가.
- 인벤토리 화면을 떠나면(닫기·H로 전환·사망) UI가 `ActiveLoot != null`일 때 `CloseLoot()`를 직접 호출한 뒤 `ScreenStateChanged`를 발행한다. Inventory의 `ScreenStateChanged(None)` 처리와 겹쳐도 되게 `CloseLoot`는 멱등이어야 한다.
- 루트가 열렸는데(`LootSessionChanged(true)`) 화면을 못 열면(사망·서비스 없음) UI가 `CloseLoot()`를 부른다.
- 키 표시 문자열(F, 4~7)은 `InputAction.GetBindingDisplayString()`에서 얻는다(하드코딩 없음).
- 아이템 표시: `displayName`(없으면 id)만. 탄 수·사용 횟수·무게 숫자 없음. 검색 전 루트 아이템은 같은 크기의 "?" 상자(집을 수 없음).
- 드래그: 누르고 `dragThresholdPx` 이상 움직이면 시작. 놓은 곳 → 그리드 칸(포인터가 아이템 가운데) `TryMove` / 장착 슬롯 `TryEquip` / 패널(`.inv-panel`) 안 빈 곳 = 취소 / 패널 밖 어두운 배경 = `TryDrop`. 더블클릭 = 의약품 `TryUse`, Ctrl+클릭 = `TryQuickMove`. Esc = 드래그 중이면 드래그 취소, 아니면 화면 닫기. 퀵슬롯 배정 = 인벤토리에서 아이템 위에 마우스 + 4~7.

**Inventory(2a)에 기대하는 동작 (UI가 의존)**
- `CanPlace(item, grid, cell, rotated)`는 같은 아이템의 현재 자리를 무시해야 한다(같은 그리드 안 이동 미리보기).
- `TryMove`는 장착 슬롯·루트에 있는 아이템도 받는다(슬롯 → 그리드 = 장착 해제).
- `TryGetLocation`은 그리드 배치의 `Rotated`를 돌려준다(드래그 시작 회전).
- 검색 전 아이템 판정은 UI가 `ActiveLoot.Items` + `IsRevealed`로 한다. 서비스도 거부해야 한다(설계 §5.3 그대로).
- 루트 검색이 진행돼 아이템이 드러날 때마다 `InventoryChanged` 발행(설계 §5.3 그대로) → UI가 다시 그린다.

**3단계 씬 생성 (`M2SceneSetup`)에 필요한 것**
1. PanelSettings 에셋 (없으면 생성): `Assets/_Project/Art/UI/GamePanelSettings.asset`
   ```csharp
   var ps = ScriptableObject.CreateInstance<UnityEngine.UIElements.PanelSettings>();
   ps.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>("Assets/_Project/Art/UI/AshfallTheme.tss");
   ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
   ps.referenceResolution = new Vector2Int(1920, 1080);
   ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
   ps.match = 0.5f;   // TODO(GDD-?) 그레이박스 값
   AssetDatabase.CreateAsset(ps, path);
   ```
   (`UnityEngine.UIElements`는 엔진 모듈이라 EditorTools asmdef 참조 추가 불필요.)
2. 씬에 `GameUI` GameObject: `UIDocument`(`panelSettings` = 위 에셋, `visualTreeAsset` = `AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/Art/UI/GameUI.uxml")`) + `GameUIRoot`.
   `GameUIRoot`의 직렬화 필드는 `config` 하나(`HudConfigSO`, `Data/UI/HudConfig.asset`). private이므로 `SerializedObject.FindProperty("config")`로 연결(SetupUtil.SetFields 사용 가능).
   **`config`가 비어 있으면 Play 시 `LogError`** (PlayMode 테스트가 실패하므로 반드시 연결).
3. 입력: 포인터(드래그)는 UI Toolkit 런타임 이벤트에 의존한다. EventSystem 없이 새 Input System만으로 UI Toolkit 포인터 이벤트가 오는지 확인되지 않았다(아래 "확신 없는 API"). 안전하게 씬에 `EventSystem` + `InputSystemUIInputModule`(기본 설정, actionsAsset 변경 없음)을 추가하는 것을 권한다.
   이를 위해 EditorTools asmdef에 `Unity.InputSystem`(InputSystemUIInputModule)과 `UnityEngine.UI`(EventSystem) 참조 추가가 필요하다(현재 둘 다 없음). 쓰기 싫으면 사람이 에디터에서 `GameObject > UI > Event System` 추가 후 "Replace with InputSystemUIInputModule".
4. 커서: `GameUIRoot.OnEnable`이 커서를 잠근다(PlayerMotor도 잠금). 화면이 열리면 UI가 해제. 별도 설정 없음.

**사람 확인 (AC2·AC3·AC4 UI 부분)**
- Tab 열기/닫기, 커서 표시/잠금, 드래그 중 R 회전(강조 색 Bone=가능/Ember=불가), 슬롯 장착, 패널 밖에 놓아 버리기, 루트 "?" → 0.5초마다 드러남, "검색 중…" 문구, H 화면 숫자 없음, T 뒤 문구 2초, 상자·바닥 아이템 앞 "F  열기: …"/"F  줍기: …".

---

## 2a 구현 메모 (gameplay-programmer, Player·Weapons·Inventory)

**Core 변경 없음.** 시그니처·이벤트·인터페이스 그대로. `TestData.cs`도 고치지 않았다.

**설계와 다른 점 / 추가**
- `WeaponHandlingConfigSO.magazineDropSpeed`(기본 1.0, TODO(GDD-?)) 추가 — 버린 탄창의 아래쪽 초기 속도(설계 §5.2 "카메라 아래쪽 속도"에 수치가 없었다). Weapons가 `InventoryConfigSO`를 볼 수 없어 여기 둔다. 2c: 에셋에 값을 넣을 필요는 없다(필드 초기값).
- **RULE-04 구간과 §7 테스트 계획 불일치:** 경계 0.67이면 20/30 = 0.667 → "절반"이다(§7은 "가득"). SO 값을 따르고 테스트는 21/30 → 가득, 20/30 → 절반으로 썼다. 20/30을 가득으로 하려면 에셋 값을 0.66으로 바꾸면 된다(코드 수정 불필요).
- `WeaponController`: `Model`은 Awake가 아니라 **Start**에서 만든다(서비스 소비는 Start 이후 규칙). `Model == null` = 비무장(`IsArmed`), 뷰모델 `SetActive(false)`. 인벤토리가 있으면 `MagazineSource`(LocalMagazineSource)는 null. `handling`이 비면(M1 씬) T·흔들림을 끄고 경고 1회. `MagazineCheck` 프로퍼티 노출. 화면 열림 시 진행 중 탄창 확인 취소. 재장전은 화면이 열려 있어도 진행된다.
- `PlayerInventory` 공개 추가: `IsInStorage`, `IsLocked`, `ActiveLootModel`, `const QuickSlotCount = 4`(§12.1 키 4~7). `TryAssignQuickSlot(i, null)` = 슬롯 비우기(true). 같은 의약품은 한 슬롯에만(다른 슬롯에서 빠진다). 저장 공간을 떠난 의약품은 퀵슬롯에서 자동 해제.
  - `TryQuickMove`: 장착 아이템은 false(TODO). `TryAutoStore`: 이미 저장 공간에 있으면 false. `TryStore` = `TryAutoStore`(배낭 포함, 꺼낼 때만 배낭 제외). `TakeFullest` 동률이면 조끼 → 주머니 순 먼저 찾은 것.
  - 장착된 조끼/배낭을 자기 내용물 그리드로 옮기기 거부. 빈 조끼는 배낭 그리드로 옮기면 장착 해제(+ `EquipmentChanged(slot, null)`).
  - `TryUse`는 저장 공간의 의약품만(루트 안 것은 거부, TODO).
- `PlayerInventoryBehaviour`: 무게는 `Changed` 때 + 매 `LateUpdate`에 비교해 바뀌면 `CarriedWeightChanged`(재장전처럼 인벤토리 밖에서 무기 무게가 바뀌는 경우). Start에서 무게, 7개 슬롯 `EquipmentChanged`, `InventoryChanged` 1회 발행. `StartingItem.quickSlot`은 **의약품에만** 적용(직렬화 기본값 0이 다른 아이템에 붙지 않게). 3단계 씬 생성 시 의약품 외에는 -1로 둬도 되고 0이어도 무시된다. `Config` 프로퍼티 노출(LootContainer가 검색 시간을 읽음).
- `LootContainerModel`: 누적 float 오차 허용 1e-4초(0.49초 0개 / 0.5초 1개는 유지). 안 들어가는 내용물은 경고 후 뺀다(TODO).
- `BodyHealthModel`: 분산 피해로 흉부가 0이 되면 `DeathCause = Thorax`. 상태이상 굴림은 복부 배율 적용 **전** 피해로 판정. `AddStatus`도 몸통 골절은 무시. 사망 후 모든 피해·회복 무시.
- `PlayerHealth`: 피격 y는 `controller.center` 기준으로 발 = 0이 되게 보정. `Medical`(MedicalUseModel) 노출.
- `PlayerMotor`: `CarriedWeightKg`, `Condition`, `InputBlocked` 노출. 차단 중에는 시점·자세·이동·질주·점프·기울이기 무시, 중력과 스태미나 회복(정지)은 계속. 커서 OnEnable/OnDisable 처리는 M1 그대로.
- `TestTurret`(Weapons): 수치 필드에 코드 기본값 없음(테스트 픽스처, HitTarget과 같은 방식). **3단계 씬 생성에서 넣을 것:** `projectiles`, `ammo`(AMMO-556), `target`(Player 루트), `zoneCenter`(붉은 바닥), `zoneRadiusM` 4, `intervalSec` 2, `aimHeightRangeM` (0.3, 1.75), 선택 `muzzle`.
- `WorldItem.Spawn`: `CreatePrimitive(Cube)`(BoxCollider + 기본 재질) + `Rigidbody.linearVelocity`. 씬 배치용은 `definition`만 넣으면 Awake에서 인스턴스 생성(3단계는 시각용 큐브/콜라이더를 같은 오브젝트에 두면 된다).

**테스트 (새 파일)**: `BodyHealthTests`(29), `MedicalTests`(11), `BodyPartResolverTests`(3), `LocomotionConditionTests`(3), `MagazineCheckTests`(15, AimSway 포함), `ItemGridTests`(9), `EquipRulesTests`(1), `PlayerInventoryTests`(29, WeaponModel+PlayerInventory 재장전 포함), `LootSearchTests`(7) = 107개.

**검증**: 작업 트리에서는 다른 에이전트 파일 컴파일 오류로 테스트 어셈블리가 빌드되지 않는다(아래). 내 어셈블리(Player·Weapons·Inventory)는 오류 0으로 빌드됨. 프로젝트를 scratchpad에 복사해 그 복사본에서만 아래 오류를 막고 실행: EditMode 212/212, PlayMode 4/4(M1 Greybox 포함) 통과.
- `UI/GameUIRoot.cs(214,215)`: `Cursor`가 `UnityEngine.UIElements.Cursor`와 모호 → `UnityEngine.Cursor`로 쓰면 해결.
- `EditorTools/M2DataSetup.cs(163)`, `Tests/EditMode/M2DataAssetTests.cs(61~63,207,218)`: `BodyPart`가 `UnityEditor.BodyPart`와 모호 → `using BodyPart = Ashfall.Core.BodyPart;` 별칭으로 해결.

---

## 3단계 구현 메모 (gameplay-programmer, M2SceneSetup)

**파일**
- 새: `EditorTools/SetupUtil.cs`(`CreateAssetIfMissing(path, fill, logTag)`·`EnsureFolder`·`SetFields`(없는 필드면 LogError)·`Load<T>`(없으면 LogError)), `EditorTools/M2SceneSetup.cs`, `Tests/PlayMode/M2SceneSmokeTests.cs`.
- 수정: `M1Setup.cs`·`M2DataSetup.cs`의 private 도우미를 `SetupUtil` 위임으로 교체(동작·로그 태그 동일). 다른 단계 코드 버그 수정 없음.
- 생성 에셋(메뉴가 코드로): `Art/UI/GamePanelSettings.asset`(theme = AshfallTheme.tss, ScaleWithScreenSize 1920×1080, MatchWidthOrHeight 0.5), `Art/Materials/M2_TurretZone.mat`(URP Lit 붉은색, 터렛 구역 표시), 씬 `Scenes/Test/M2_HealthInventory.unity`.

**설계와 다른 점**
- 멱등 방식: 설계 §5.6은 "씬이 있으면 건너뜀"이었으나 요청에 따라 **씬은 실행할 때마다 새로 만들어 덮어쓴다**(GUID 유지). 데이터·PanelSettings·재질은 없을 때만 만든다. 에디터에서 실행 시 수정된 씬 저장 여부를 먼저 묻는다(배치모드는 생략).
- HitTarget 3개(정면 10/25/45m). 설계는 1개, 요청은 "몇 개".
- 시작 장비는 설계 §5.6 그대로(헬멧은 상자 A, 방탄복은 바닥). 장착 순서 무기 → 조끼 → 배낭 → 탄창 3 → 붕대(퀵 0)·지혈대(퀵 1)·부목(퀵 2). 의약품 외 quickSlot = -1.
- 루트 상자 그리드 6×4(TODO(GDD-?), 상자 C의 엔진 부품 2개(3×2)까지 들어가는 크기). 상자 이름 "상자 A/B/C".
- 배치(바닥 윗면 y 0, 바닥 60×60 큐브): 플레이어 (0,0.05,-20) +z 방향. 상자 A/B/C = 플레이어 왼쪽 4m, 앞 3/6/9m. 바닥 아이템(방탄복, 탄창) = 오른쪽 3m, 앞 3/5m, 크기 = 칸 × worldItemCellM + Rigidbody. 터렛 구역 중심 (15,0,-20)(시작점에서 오른쪽 15m, 반경 4m 붉은 사각형, 충돌체 없음), 터렛 (15,0,-8) 높이 1.5m에서 구역을 향함. TestTurret 값 = 2a 메모(반경 4, 간격 2초, 조준 높이 0.3~1.75, ammo AMMO-556, target Player, muzzle).
- `WeaponController.startingMagazines` = 4(인벤토리가 있으면 쓰이지 않음, 인벤토리를 빼고 시험할 때 M1과 같은 지급).

**발견한 함정 (고침)**
- `EditorSceneManager.NewScene(..., Single)`이 사용되지 않는 에셋을 언로드한다. NewScene **전에** 불러 둔 PanelSettings·재질 참조가 끊겨 씬에 `m_PanelSettings: {fileID: 0}`·URP 기본 Lit 재질로 저장됐다(로그에 오류 없음). → 씬에 넣는 에셋은 NewScene **뒤에** 경로로 다시 불러온다. 스모크 테스트에 `panelSettings`·`visualTreeAsset` 단언을 넣어 회귀를 잡는다.

**EventSystem 결정: 넣지 않는다.** EditorTools asmdef 변경 없음.
- 근거: Input System 1.20 문서 `Documentation~/understand-ui-compatibility.md` 호환 표 "UI Toolkit (2023.2+) — UI Input Module: Not required", "From Unity 2023.2 and onwards, the UI actions defined in the default project-wide actions directly map to UI Toolkit input".
- 소스: `InputSystem/Runtime/Plugins/InputForUI/InputSystemProvider.cs`가 `ENABLE_INPUT_SYSTEM`일 때 `EventProvider.SetInputSystemProvider`로 등록되고, `InputSystem.actions`에 `UI` 맵이 있으면 그 `UI/Point`·`UI/Click`·`UI/ScrollWheel`·`UI/Cancel` 등을 쓴다. 프로젝트 `activeInputHandler: 1`(새 Input System), `InputSystem_Actions`의 UI 맵에 Point/Click/RightClick/ScrollWheel 있음.
- (같은 문서 KnownLimitations.md의 "UI Toolkit support currently requires use of an EventSystem"은 2023.2 이전 기준의 오래된 문구로 판단.) EventSystem을 넣으면 UI Toolkit이 EventSystem 경로로 바뀌므로, 사람 확인에서 드래그·클릭이 안 되면 그때 `EventSystem` + `InputSystemUIInputModule`을 추가(EditorTools asmdef에 `Unity.InputSystem`·`UnityEngine.UI` 참조 필요).
- 주의: 포인터 입력은 배치모드 테스트로 검증하지 못했다(스모크 테스트는 패널 생성까지). 에디터에서 드래그 확인 필요.

**검증**: 배치모드 `-executeMethod Ashfall.EditorTools.M2SceneSetup.CreateAll` 종료 코드 0, 오류 로그 없음. 재실행 시 PanelSettings 파일·씬 .meta 불변, Data 변화 없음. EditMode 235/235, PlayMode 5/5(기존 4 + `M2SceneSmokeTests` 1) 통과. 결과·로그: scratchpad `s3_*`(최종 = s3_setup3.log, s3_editmode.xml, s3_playmode.xml).

---

## 4단계 구현 메모 (test-engineer, AC 검증)

**새 PlayMode 테스트 (`Assets/Tests/PlayMode/`, 전부 M2 씬 로드 통합 테스트)**
- `M2TestSupport.cs` — `M2Scene`(씬 로드·참조 수집·대기 도우미), `EventLog<T>`(버스 기록), `SimKeyboard`(가상 키보드: `InputSystem.AddDevice<Keyboard>` + `QueueStateEvent`로 실제 바인딩 T/H/4/R/W/Shift를 거친다. 배치모드 포커스 문제로 테스트 동안 현재 `InputSettings`의 `backgroundBehavior=IgnoreFocus`, `editorInputBehaviorInPlayMode=AllDeviceInputAlwaysGoesToGameView`로 바꾸고 되돌린다. 설정 객체를 교체하면 Input System이 임시 기본 설정을 DestroyImmediate하므로 값만 바꾼다. 프로젝트에 InputSettings 에셋이 없어 파일 변화 없음).
- `PlayerDamageTests.cs`(6) — 투사체 → 캡슐 높이·좌우별 7부위, 자기 탄 무시, 머리 45 → 즉사·PlayerDied·조작/인벤토리 잠금, 출혈 -1/s·중출혈 -3/s → 키 4(붕대)/TryUse(지혈대)/부목으로 해제·소모·퀵슬롯 비움, 다리 파괴 → 실제 W+Shift 이동 속도(질주 불가, 걷기×0.6), 팔 파괴 → PlayerConditionChanged → WeaponController 흔들림 ×2가 무기 회전에 반영.
- `InventoryFlowTests.cs`(5) — 시작 장비, 회전 이동·장착·잘못된 슬롯 거부·내용물 있는 조끼 버리기 거부, 무게 → CarriedWeightChanged → PlayerMotor, 40kg 초과 질주 불가·실제 이동 속도, Primary1 해제 → 비무장/재장착, 일반 재장전 회수·빠른 재장전 WorldItem → 다시 줍기, 씬 언로드 후 구독·서비스 누수 없음.
- `LootSearchFlowTests.cs`(1) — 상자 B 6개: 0.5초 간격·배치 순서로 드러남, 검색 전 TryQuickMove/TryMove/TryDrop 거부, UI "?"·"검색 중…", Esc로 닫으면 진행 멈춤·다시 열면 이어서, 완료 후 가져오기.
- `HudRuleTests.cs`(3) — RULE-04: T → 0.8초(±0.1) 뒤 MagazineCheckCompleted, HUD 문구 가득/절반/거의 없음, HUD Label에 숫자 없음, 일정 시간 뒤 사라짐, R로 취소 시 결과 없음. RULE-05: H → 부위 상태 화면, Label 전부 숫자·% 없음, 등급·상태 문구, 상태 변경 시 다시 그림, H로 닫힘.
- 테스트 asmdef: `Ashfall.Tests.PlayMode`에 `Unity.InputSystem` 참조 추가.

**제품 코드 수정**
1. `Player/PlayerHealth.cs` — **버그: 머리가 사실상 맞지 않았다.** Unity CharacterController의 물리 쿼리 캡슐(PhysX CCT kinematic actor)은 컨트롤러 height·radius의 0.8배(PxControllerDesc::scaleCoeff 기본값)다. 측정: 높이 1.8·반지름 0.3·발 y 0.05 → 실제 캡슐 y 0.23~1.67, 반지름 0.24. `BodyPartResolver`는 컨트롤러 크기(발 0 ~ 1.8)로 비율을 내므로 머리 구간(≥0.87 = 1.566m)이 실제 캡슐 꼭대기 5cm만 남아 수평 사격으로는 맞지 않았다(테스트에서 1.72m 사격이 빗나감). 팔 구간도 실제 반지름과 어긋났다.
   수정: 피격 지점을 컨트롤러 중심 기준으로 `/ PhysicsActorScale(0.8, 엔진 상수 주석)` 한 뒤 발 = 0으로 옮겨 판정. `BodyPartResolver`·설정값·EditMode 테스트는 그대로.
2. `EditorTools/BuildTools.cs`(새 파일) — `Ashfall/Build/Windows64` 메뉴 / `-executeMethod Ashfall.EditorTools.BuildTools.BuildWindows64`. 빌드 설정의 활성 씬, StandaloneWindows64, 출력 `Builds/Windows64/Ashfall.exe`(git 제외). 실패 시 예외(종료 코드 ≠ 0).

**발견했지만 고치지 않은 것 (보고)**
- `CharacterController.minMoveDistance` = 0.001m(M1 씬 생성 기본값). 한 프레임 이동량이 1mm 미만이면 무시된다. 배치모드(프레임 ≈1ms)에서 느린 이동이 거의 멈췄다(51kg 걷기 1.44m/s가 0.32m/s로 측정). 실제 게임에서도 고프레임(수백 fps) + 느린 이동(엎드리기 0.8m/s × 무게 패널티)이면 같은 일이 생길 수 있다. 테스트는 `Time.captureDeltaTime = 1/60`으로 고정해 잰다. 수정 후보: 씬 생성에서 `minMoveDistance = 0` (M1 범위라 손대지 않음).
- `LootSearchFlowTests` 첫 프레임 가정: 상자를 연 다음 프레임 dt가 0.5초 미만이어야 한다(배치모드 측정 최대 0.12초).

**검증**: EditMode 235/235(종료 코드 0), PlayMode 20/20(종료 코드 0, 기존 5 + 새 15). 결과·로그 scratchpad `s4_*`(최종 s4_editmode.xml, s4_playmode2.xml, s4_build.log).

## 리뷰 수정 메모 (커밋 전 코드 리뷰, gameplay-programmer)

**Core 변경 (인터페이스·enum 추가만, 기존 멤버 변경 없음)**
- `IInventoryService.IsLocked(ItemInstance)` 추가 — 사용 중(잠긴) 의약품을 UI가 드래그 시작부터 거부하기 위해. 구현: `PlayerInventory`(기존 공개 메서드를 인터페이스로 승격), 테스트 가짜 `FakeInventoryService`(`Locked` 집합).
- `MedicalUseFailReason.Depleted` 추가 (Player `MedicalStartResult.Depleted`와 짝).

**1. [높음] 드래그 중 다른 경로로 빠진 아이템 복제·부활**
- `Inventory/PlayerInventory.cs`: `CanTakeFrom`이 아이템이 실제로 장착 슬롯·저장 공간·열린 루트에 있어야 true. 그래서 `CanPlace`·`CanEquip`·`TryMove`·`TryEquip`·`TryDrop`·`TryQuickMove`가 밖의 아이템(재장전으로 꺼낸 탄창, 소진된 의약품)을 거부. `TryMove`·`TryEquip`·`Relocate`·`TakeFullest`는 `Detach` 실패 시 실패. 잠긴 아이템은 `TryAutoStore`도 거부.
- 밖에서 새로 들어오는 아이템 경로를 분리: 줍기·회수 = `TryAutoStore`(기존), 시작 장비 직접 장착 = 새 `TryEquipNew`(인터페이스 밖). `PlayerInventoryBehaviour.ApplyStartingItems`와 EditMode 테스트의 "새 아이템 장착"을 `TryEquipNew`로 바꿨다.
- UI: `InventoryDragModel.CancelIfGone()`(드래그 중 아이템이 `TryGetLocation`으로 안 잡히면 취소), `GameUIPresenter.OnInventoryChanged`가 호출. 뷰는 기존 `SyncDrag`가 다음 프레임에 고스트를 정리. `CanPick`이 `IsLocked`면 거부.

**2. UsesLeft 0 의약품**: `MedicalUseModel.TryStart` → `Depleted`, `PlayerHealth`가 `MedicalUseRejected(Depleted)`. `PlayerInventory.TryUse`도 `UsesLeft <= 0` 거부.

**3. 사망 후 재장전**: `WeaponController.OnPlayerDied`에서 `Model?.CancelReload()`, 사망 후 `Model.Tick` 중단. `PlayerInventory.HasLoadable/TakeFullest/TryStore`는 `IsDeadLocked`면 false/null/false.

**4. 무게 계산 할당**: `PlayerInventory`가 저장 공간 그리드 배열(`storageGrids`)을 장착 변경 때만 다시 만들고 인덱스로 순회. `ItemGrid.TotalWeightKg`(List 인덱스 합) 추가. `IsInStorage`·`FindContainingGrid`·`HasLoadable`·`TakeFullest`도 yield 이터레이터 없이. 브리지는 그대로 매 `LateUpdate` 확인하므로 인벤토리 밖(무기 탄창 교체) 무게 변화도 반영된다. EditMode 테스트가 `Is.Not.AllocatingGCMemory`로 0 할당 확인.

**5. 상호작용 레이캐스트**: 마지막 Collider와 `IInteractable` 결과를 캐시, Collider가 바뀔 때만 `GetComponentInParent`.

**6. 피격 로그**: `PlayerHealth`의 피격 로그를 `[Conditional("ASHFALL_DEBUG_DAMAGE")]` 메서드로. 기본 빌드·에디터에서는 찍히지 않는다.

**7. config 누락**: `PlayerHealth`·`PlayerMotor`·`PlayerInventoryBehaviour`가 Awake에서 config null이면 LogError 후 `enabled = false`(OnEnable/OnDisable/ApplyDamage/AddRecoil 가드). `LootContainer.Interact`는 `by.Config`가 null이면 무시.

**8. minMoveDistance**: `M2SceneSetup`·`M1Setup`의 플레이어 `CharacterController.minMoveDistance = 0`. M1은 기존 `CreateAll`이 씬이 있으면 건너뛰므로 덮어쓰는 `M1Setup.RecreateAll`(메뉴 `Ashfall/Setup/Recreate M1 Greybox Scene`)을 추가해 실행. 두 씬 재생성(GUID 유지, M1 YAML은 fileID 재생성으로 diff가 큼). `InventoryFlowTests` 무게 이동 측정에서 `Time.captureDeltaTime` 우회를 제거해도 통과(PlayerDamageTests의 우회는 유지). `M2SceneSmokeTests`에 minMoveDistance 0 확인 추가.

**추가 테스트 (EditMode +15)**: PlayerInventoryTests 9(TakeFullest로 빠진 탄창 이동·버리기 거부, 소진·제거된 의약품 이동 거부, 밖 아이템 TryEquip 거부/TryEquipNew 허용, 잠긴 의약품, UsesLeft 0 TryUse 거부, 사망 후 IMagazineSource, 사망 후 재장전 미완료, 무게 0 할당, 인벤토리 밖 탄창 교체 무게 반영), MedicalTests 1(Depleted), UiModelTests 5(잠긴 아이템 드래그 거부, InventoryChanged 드래그 취소, 실제 PlayerInventory로 시나리오 A·B·잠금).

**검증**: EditMode 250/250, PlayMode 20/20, Windows 빌드 성공. 로그 scratchpad `s5_*`.
