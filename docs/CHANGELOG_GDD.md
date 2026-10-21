# GDD 변경 기록

> GDD §0: 문서 수정이 필요하면 이 파일에 사유를 먼저 기록한 뒤 `docs/GDD.md`를 고친다.
> 형식: `## YYYY-MM-DD — 섹션 ID` 아래에 변경 내용과 사유.

## 2026-10-21 — 최초 반입
- `docs/GDD.md`를 원본 기획서 그대로 저장소에 추가. 내용 변경 없음.

## 2026-10-21 — §13.2 폴더 구조
- `Scripts/EditorTools/`(어셈블리 `Ashfall.EditorTools`, 에디터 전용) 추가.
- 사유: 씬·에셋은 YAML을 손으로 쓰지 않고 에디터 메뉴로 생성하는데(M0 Boot·MainMenu 씬), 에디터 전용 코드를 둘 폴더가 §13.2에 없었다.
  이름을 `Editor`로 하지 않은 이유: `Ashfall.Editor` 네임스페이스가 다른 Ashfall 코드에서 `UnityEditor.Editor`를 가린다.

## 2026-10-21 — M1 구현 중 결정 (§13.3, §14, §12.1, §5)
문서 본문은 바꾸지 않았다. 아래는 구현에서 정한 해석이며, 기획 확정 시 문서에 반영하거나 되돌린다.
- **§13.3·§14 아이템 SO 위치:** `ItemDefinitionSO`·`WeaponDefinitionSO`·`AmmoDefinitionSO`·`MagazineDefinitionSO`와 아이템 enum을 `Ashfall.Core`에 둔다.
  사유: Weapons·Inventory(M2)·AI(M3)가 함께 쓰는데 기능 어셈블리끼리 참조가 금지돼 있다. `PlayerConfigSO`는 Player만 쓰므로 Player에 둔다.
- **§14 스키마에 없는 필드 추가:**
  - `WeaponDefinitionSO.reloadTimeSec`, `fastReloadTimeSec`, `hipSpreadDeg` — 재장전(§5.1)·지향 사격(RULE-07)에 필요한 값.
  - `AmmoDefinitionSO.maxFlightTimeSec` — 탄도 투사체(§5.1)가 무한히 날지 않게.
  - `PlayerConfigSO`의 자세별 캡슐·눈높이, 시점 감도·상하 제한, 기울이기 이동량·속도, 접지 속도.
- **GDD에 수치가 없어 임시값을 넣은 항목 (TODO(GDD-?)):** 반동 V/H(0.6°/0.25°), 조준 시간(0.25s), 재장전 2.5s / 빠른 재장전 1.5s, 지향 사격 퍼짐 2°,
  5.56 관통력 3 / 초기 속도 900m/s, 아이템 크기·무게·가격, 캡슐 높이(1.8/1.2/0.6m), 감도 등. 값은 `Assets/_Project/Data/` 에셋에서 조정한다.
- **§12.1 키 표에 없는 입력:** 점프 = Space. 빠른 재장전 = 일반 재장전 중 R 한 번 더(남은 시간은 빠른 재장전 기준). 기울이기 = Q/E 누르고 있는 동안.
- **§5.2 피해 위치 충돌:** 표의 "피해"는 무기별인데 §14에서 피해는 `AmmoDefinitionSO.damage`에 있다. 9mm를 쓰는 WPN-PST-01(35)과 WPN-SMG-01(30)이 같은 탄으로는 표현되지 않는다.
  M1은 5.56 = 45로 두고, M7(무기 5종) 전에 무기 배율 필드 등으로 정해야 한다.
- **§5.1 미구현(이후 마일스톤):** 고장(Jam)·내구도, 약실 탄(+1), 버린 탄창을 줍기 가능한 아이템으로 떨어뜨리기(M2 인벤토리).

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
- **RULE-04:** 구간은 탄 수/용량 ≥ 0.67 "가득"(30발 탄창은 21발부터, 20발은 "절반"), ≥ 0.34 "절반", 그 아래 "거의 없음"(0발 포함). 결과 문구 2초 표시. 사격·재장전 시 확인 취소.
- **RULE-05:** 부위 상태 화면은 등급(정상/부상/중상/파괴)과 상태이상 이름만 표시. 등급 경계 HP 비율 0.75 / 0.35.
- **조준 흔들림:** M1에는 없던 기본 흔들림(0.15°, 0.5Hz)을 추가해 팔 파괴·골절 배율이 적용되게 했다.
- **GDD에 수치가 없어 임시값을 넣은 항목 (TODO(GDD-?)):** 출혈 확률 0.35 / 중출혈 0.15 (피해 10 이상), 골절 확률 0.2 (피해 20 이상),
  골절 이동 ×0.8 / 조준 흔들림 +0.5, 부위 판정 높이 비율(0.87/0.62/0.45, 팔 0.6R), 의약품 사용 시간(붕대 3s, 지혈대 4s, 부목 5s)·크기·무게,
  조끼 그리드 4×2, 배낭 4×5, 헬멧·방탄복 등급 3, 상호작용 거리 2m, 아이템 ID(MED-*, RIG-01, BAG-01, ARM-*, JNK-*).
- **구현 중 추가 결정:**
  - `WeaponHandlingConfigSO.magazineDropSpeed`(버린 탄창 속도), `HudConfigSO.dragThresholdPx`(드래그 시작 거리) 추가. 둘 다 TODO(GDD-?).
  - UI 상태 로직은 순수 클래스 `GameUIPresenter`로 분리(EditMode 테스트용). UI Toolkit 입력은 EventSystem 없이 Input System의 UI 맵을 쓴다(Input System 1.20, Unity 2023.2+).
  - 그레이박스 피격원으로 `TestTurret`(Weapons, 테스트 전용)을 둔다. M2 확인 씬 `Scenes/Test/M2_HealthInventory.unity`는 `Ashfall/Setup/Create M2 Test Scene`이 매번 새로 만든다(빌드 설정에는 없음).
  - 희귀도·가격이 정해지지 않은 M2 아이템(의약품·장비·방어구)은 희귀도 Scrap, 가격 0으로 둔다(TODO(GDD-?)).
  - (리뷰 수정, SYS-INV) 인벤토리 이동·장착·버리기 명령은 지금 인벤토리(장착·저장 공간·열린 루트)에 있는 아이템만 받는다. 드래그 중
    재장전·의약품 소진으로 빠진 아이템이 놓을 때 복제·부활하던 문제 때문. 사용 중(잠긴) 의약품은 드래그 시작부터 거부하며, 이를 위해
    `IInventoryService.IsLocked(ItemInstance)`를 추가했다. 남은 횟수 0인 의약품은 사용 시작 거부(`MedicalUseFailReason.Depleted`). 사망 후 재장전은 완료되지 않는다.
  - (리뷰 수정, SYS-PLR) 씬 생성 메뉴가 플레이어 `CharacterController.minMoveDistance`를 0으로 둔다(기본 1mm는 고fps + 느린 이동에서 멈춤).
    M1 씬을 다시 만들려면 `Ashfall/Setup/Recreate M1 Greybox Scene`.
