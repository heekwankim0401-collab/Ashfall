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
