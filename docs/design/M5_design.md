# M5 "은신처 & 세이브" 설계 (unity-architect) — 준비안

기준: docs/GDD.md §0, §2, §3(RULE-01·RULE-03), §6, §9, §12, §13.2·§13.3, §14, §15 M5, §17, §18.
M1~M4 결정(CHANGELOG_GDD), `M4_design.md`(§1.3~§1.6 "M5 경계"와 끝의 구현·리뷰 수정 메모), `M4_review.md`, 현재 코드(ff6be6d)를 전제로 한다.
**상태: 준비안.** §5 질문은 오케스트레이터가 ★추천안으로 확정한다는 전제로 썼다. 답이 다르면 표시된 곳(⟨Qn⟩)만 바꾼다.
여기 없는 것은 만들지 않는다. 모호하면 `// TODO(GDD-?) Qn` + 가장 단순한 구현.

M4에서 실제 구현이 설계와 달라진 점 중 M5가 기대는 것(리뷰 수정 메모 기준):
- `GameFlow` 대리자는 `Func<string,bool>`(요청 성공 여부), `StartRaid()`·`CompleteRaid()`는 bool 반환, `ReturnToMainMenu`는 삭제됐다.
- 세계 정지는 `CompleteRaid`가 풀지 않고 목적지 씬 로드 완료(`OnSceneLoaded`)가 푼다.
- `RaidEnded`를 받으면 Inventory(`IsRaidEndLocked`)·Weapons·Player가 소지품 변경을 멈춘다. 결과 스냅샷은 그 **직전**에 만든다.
- `IInventoryService.CanConsume` 추가, `RaidController.OnSessionEnded`는 try/finally로 반드시 완료를 예약한다.
- `sceneLoaded`는 Awake·OnEnable **뒤**, Start **앞**에 온다(4단계 메모). 그래서 씬 오브젝트의 Awake 시점에는 `Flow.Phase`가 아직 이전 단계다.

---

## 0. 범위

**만든다 (M5 AC)**
1. 보관함·상인·부랑자 키트(§9)
   - 보관함: 그리드 10×30(W×H) 고정. 업그레이드 확장은 만들지 않는다 ⟨Q1⟩.
   - 상인 1명: 고정 판매 목록(재고 무제한) 매수·보관함 아이템 매도, 화폐 탄피(Casings) = 프로필 정수 ⟨Q8~Q12⟩.
   - 부랑자 키트: WPN-PST-01 + MAG-9MM-12 ×2 + MED-BND-01, 실제 시간 20분 쿨다운, 무료, 보관함에 지급 ⟨Q13~Q17⟩.
2. 세이브/로드와 강제 종료 처리(§13.3, RULE-03)
   - `Application.persistentDataPath/save.json`(JsonUtility, 버전 필드), 임시 파일 → 교체(원자적), 손상·누락·알 수 없는 id 처리.
   - 레이드 진입 시 `raidInProgress = true` 기록 → 다음 실행의 첫 은신처 진입에서 true면 반입 장비 삭제 + 알림.
3. 은신처 ↔ 레이드 전체 루프 1회 완주
   - 씬 흐름 Boot → MainMenu → **Hideout** → Raid_CrimsonBasin → Hideout. 결과 패널은 MainMenu에서 Hideout으로 이동.
   - 은신처는 메뉴형 UI(UI Toolkit, 3D 아님). 반입 장비 선택 = 기존 인벤토리 화면 재사용(장착 슬롯·주머니·조끼·배낭 ↔ 보관함) ⟨Q29⟩.
   - 레이드의 플레이어 인벤토리는 흐름으로 시작한 레이드에서만 `startingItems` 대신 반입 Loadout으로 시작한다.
   - RULE-01 완성: 반입 장비는 레이드 시작 때 은신처 장비에서 빠지고, 탈출 시 반출품(`CarriedOutLoadout`)만 은신처 장비로 돌아온다.

**만들지 않는다**
- M6: 아트·오디오, 은신처 BGM(§11 "은신처 제외"는 M6 오디오 패스에서), 로딩 화면, 아이콘.
- M7: 제작대(§9), 무기 교체 1/2/3(그래서 키트 권총은 M5에서 플레이어가 쏠 수 없다 ⟨Q16⟩), 탈수·허기·방사능.
- 보관함 업그레이드·은신처 업그레이드(§9 "진행도") ⟨Q1⟩, 상인 재고·평판·시세 변동, 체력 이월 ⟨Q18⟩, 여러 세이브 슬롯, 클라우드 저장, 설정 화면.
- 3D 은신처 공간, 은신처 안 플레이어·무기·입력 맵 변경.

---

## 1. 아키텍처

### 1.1 경계 요약 (Hideout ↔ Core ↔ Inventory / Raid / UI)

`Ashfall.Hideout`은 `Ashfall.Core`만 참조한다(asmdef 이미 있음, 변경 없음). 기능끼리 직접 참조는 계속 없다.

| 필요한 것 / 주는 것 | 상대 | 방법 | 이유 |
|---|---|---|---|
| 씬을 넘는 프로필(보관함·은신처 장비·탄피·키트 시각·raidInProgress)과 세이브 | 모두 | ★Core `ProfileService`(GameManager가 소유, `GameManager.Profiles`) | 씬을 넘어 사는 상태는 유일한 싱글톤에만 둔다(M4와 같은 원칙). 세이브는 §13.2상 Core |
| 보관함 그리드 | Hideout(상인·키트) · Inventory(드래그) | ★`ItemGrid`를 Inventory → **Core로 이동**(.cs·.meta 함께, GUID 유지) ⟨Q2⟩. `PlayerProfile.Stash`가 하나를 가진다 | 상인·키트 배치 로직이 그리드 판정을 써야 하는데 Hideout은 Inventory를 못 본다. M3 무기 로직 이동과 같은 전례 |
| 은신처 장비(다음 반입) 현재 상태 읽기 | Inventory → Hideout | ★Core `Loadout.Capture(IInventoryService)` | 기존 읽기 API(GetEquipped·StorageGrids·GetQuickSlot)만으로 위치까지 기록. Hideout이 Inventory 타입을 모른다 |
| 은신처 장비를 인벤토리에 놓기(은신처·레이드) | Core → Inventory | Inventory 내부 `PlayerInventory.TryApplyLoadout(Loadout, rejected)` | 배치 규칙은 Inventory에만 둔다 |
| 은신처에서 보관함 ↔ 장비 드래그 | Inventory(+UI) | ★Inventory `HideoutInventoryBehaviour`가 `PlayerInventory`를 만들고 `profile.Stash`를 **열린 루트 자리(ActiveLoot)**로 연다 ⟨Q28⟩, `IInventoryService` 등록 | 기존 인벤토리 화면·드래그·장착 규칙을 그대로 재사용. 새 UI 규칙 복제 없음 |
| 상인 매매·키트 수령·레이드 시작·메뉴 복귀 | UI → Hideout | ★Core `IHideoutService`(Hideout 등록) | 결과(성공·이유)가 동기로 필요 → 이벤트 불가 |
| 탄피·보관함·키트 상태 변경 | Hideout → UI | ★이벤트 `HideoutChanged` | UI가 다시 읽는다(InventoryChanged는 Inventory 소유 이벤트라 Hideout이 발행하지 않는다) |
| 레이드 시작(반입)·종료·은신처 진입/이탈 | Hideout·Raid·UI → Core | `GameFlow.StartRaid(Loadout)`, `CompleteRaid(result)`(목적지 Hideout), ★`EnterHideout`, `LeaveHideout`, `TakeRaidReturn` | 흐름·raidInProgress 기록을 한 곳(GameFlow → ProfileService)에서 강제 |
| 레이드 인벤토리 반입 주입 | Core → Inventory | ★`GameFlow.TryGetRaidLoadout(sceneName, out Loadout)` | 흐름으로 시작한 레이드에서만 `startingItems` 대신 반입. 확인 씬은 그대로 |
| 탈출 반출품(위치 포함) | Raid → Core | ★`RaidResult.CarriedOutLoadout`(Extracted일 때만) | RULE-01 판정은 계속 `RaidResultBuilder` 한 곳 |
| 세이브 id → 아이템 정의 | Hideout 씬 → Core | ★Core `IItemCatalog` + `ItemDatabaseSO`(에셋 `Data/Items/ItemDatabase.asset`), Hideout 씬의 `HideoutController`가 직렬화 참조로 넘긴다 | GameManager는 코드로 생성돼 에셋 참조가 없다. 읽기는 은신처 진입 때만 일어나므로 은신처 씬이 넘기면 된다(Resources 폴더 불필요) |
| 결과 패널(PendingResult) | UI ← Core | 기존 `GameFlow.PendingResult`·`AcknowledgeResult` | M4 그대로, 표시 위치만 Hideout |

- 싱글톤은 계속 `GameManager` 하나. `ProfileService`·`GameFlow`는 그 멤버(순수 C#, EditMode 테스트).
- 레지스트리 등록 추가: `IHideoutService`(Hideout). 은신처의 `IInventoryService`는 `HideoutInventoryBehaviour`가 등록한다(레이드 씬의 `PlayerInventoryBehaviour`와 동시에 존재하지 않음).

### 1.2 씬 흐름

```
Boot ─(BootLoader)─► MainMenu ─[은신처]─ EnterHideout ─► Hideout ─[레이드 시작]─ StartRaid(Loadout) ─► Raid_CrimsonBasin
                        ▲                                  │  ▲                    (raidInProgress=true 저장)        │
                        └──── LeaveHideout ◄─[메인 메뉴]───┘  │                                                     │ RaidEnded → 종료 오버레이 3초(세계 정지)
                                (저장)                         └──────────── CompleteRaid(result) ◄─────────────────┘
                                                               은신처 진입: 결과 반영(RULE-01) → 저장(raidInProgress=false)
                                                               → [알림] → [결과 패널] → 허브
```

- `GamePhase`에 `Hideout`을 **끝에** 추가(`None, MainMenu, Raid, Hideout`). `SceneNames.Hideout = "Hideout"`.
- `GameFlow` 전이(모두 bool 반환, 거부 시 경고 로그):
  - `EnterHideout()`: Phase가 MainMenu 또는 None이고 로드 중이 아닐 때 → `Hideout` 로드. Raid 중 거부.
  - `LeaveHideout()`: Phase Hideout → `MainMenu` 로드. 저장은 호출자(`HideoutService`)가 먼저 한다.
  - `StartRaid(Loadout loadout)`: **Phase Hideout에서만**(M4의 인자 없는 `StartRaid()`는 삭제 — MainMenu에서 바로 레이드로 가는 길이 RULE-03 기록을 건너뛰지 않게). 순서는 §1.4.
  - `CompleteRaid(result)`: M4 그대로(Raid 단계·한 번만·정지 유지) + 목적지 `Hideout`. 로드 요청 실패 시 결과 유지·정지 해제(M4 리뷰 수정 유지).
  - `OnSceneLoaded`: 이름 → Phase(Hideout 추가), 정지 해제(M4 그대로).
- 결과 패널은 Hideout이 보인다(MainMenu의 결과 패널 제거). `PendingResult`는 은신처 결과 패널의 [확인]까지 남는다.
- 에디터에서 Raid 씬을 바로 Play: M4처럼 Phase Raid → 끝나면 Hideout으로 간다. 흐름 레이드가 아니므로 결과는 **표시만**, 프로필에 반영하지 않는다 ⟨Q25⟩(디버그 고정 키트가 보관함에 불어나지 않게).
- 에디터에서 Hideout 씬을 바로 Play: 정상 동작(실제 세이브 경로 사용). 씬 전환은 계속 `LoadSceneAsync(Single)`, 로딩 화면 없음(M6).

### 1.3 RULE-01 완성 — 반입(Loadout)의 소유권

용어(§18): **반입(Loadout)** = 장착 7슬롯 + 저장 공간(주머니·조끼·배낭) 배치 + 퀵슬롯. 은신처에서는 "은신처 장비"(다음 레이드에 가져갈 장비)로 계속 유지된다 ⟨Q3⟩.

| 시점 | 프로필(메모리) | 레이드 | 보관함 |
|---|---|---|---|
| 은신처에서 장비 편집 | `HideoutInventoryBehaviour`의 `PlayerInventory`가 실물. 저장 때 `Loadout.Capture`로 `profile.Loadout` 갱신 | — | 드래그로 장비와 오감 |
| [레이드 시작] | `profile.Loadout = Capture()` → `BeginRaid`: 파일에 (Loadout + raidInProgress) 기록 → 메모리 `profile.Loadout = Empty` | `GameFlow.RaidLoadout = 그 Loadout` → 레이드 인벤토리가 받는다 | **변화 없음**(보관함 아이템은 장비로 옮긴 것만 레이드에 간다) |
| 레이드 중 | Loadout Empty, raidInProgress true | 소지품 = 반입 + 획득 | — |
| 탈출 | 은신처 진입에서 `profile.Loadout = result.CarriedOutLoadout`(종료 시점 소지품, 위치 그대로) | — | 변화 없음 |
| 사망·MIA·포기 | `profile.Loadout = Empty` | — | 변화 없음 |
| 강제 종료(RULE-03) | 다음 실행 첫 은신처 진입: 파일의 Loadout 삭제 | — | 변화 없음 |

1. **판정 지점은 계속 `RaidResultBuilder` 하나.** 새 오버로드가 종료 시점 `Loadout`을 받아 `Extracted`면 `CarriedOutLoadout`에 담고, 그 밖이면 null(반출 0). 평면 목록 `CarriedOut`/`Lost`는 같은 Loadout의 `CollectItems` 순서(= M4 `InventorySnapshot.Collect` 순서)로 만든다 — 결과 화면 표시는 M4와 같다.
2. **반영 지점은 `ProfileService.ApplyRaidResult` 하나.** `result.Survived && CarriedOutLoadout != null`일 때만 은신처 장비가 된다. 흐름 레이드 표(`GameFlow.TakeRaidReturn`)를 한 번만 꺼낼 수 있어 같은 결과가 두 번 반영되지 않는다.
3. 탈출 반출품을 보관함이 아니라 **은신처 장비로 위치 그대로** 돌려보내는 이유 ⟨Q3⟩: 보관함이 가득 차도 잃는 것이 없고(평면 자동 배치는 넘침 처리가 필요), 조끼·배낭 내용물 구조가 보존되며, 플레이어는 은신처에서 원하는 것만 보관함으로 옮기거나 판다.
4. M4 종료 잠금(`IsRaidEndLocked`, 재장전·의약품 취소)은 그대로. `Loadout.Capture`는 M4 스냅샷과 같은 시점(RaidEnded 발행 직전)에 부른다.

### 1.4 RULE-03 강제 종료 처리 순서 ⟨Q4~Q7⟩

**무엇을 언제 기록하나 (파일 = save.json)**

| # | 시점 | 기록 내용 | 실패 시 |
|---|---|---|---|
| W1 | 은신처 진입(`HideoutController.Awake` → `HideoutService.Enter`) | 첫 로드·RULE-03 감지 → 흐름 레이드 결과 반영 → **저장**(raidInProgress=false) | 진입은 계속, 알림 "저장하지 못했다" |
| W2 | 은신처를 나갈 때: [메인 메뉴] | 장비 Capture → 저장 → `LeaveHideout` | 알림만, 이동은 함 |
| W3 | 은신처에서 앱 종료(`OnApplicationQuit`, 떠나는 중이 아닐 때) | 장비 Capture → 저장 | 로그만 |
| W4 | [레이드 시작] = 레이드 진입(§13.3) | 장비 Capture → `profile.Loadout`에 두고 **raidInProgress=true** → 저장(파일엔 반입 장비가 그대로 있다) → 메모리 Loadout을 비우고 레이드 로드 | **레이드를 시작하지 않는다**(RULE-03을 보장할 수 없음). 플래그 되돌림, 알림 |
| — | 씬 로드 요청 실패(W4 직후) | `CancelRaid`: Loadout 되돌림, flag=false, 저장 | 로그 |
| — | 레이드 중 | **아무것도 쓰지 않는다**(RULE-03 "세이브는 은신처에서만") | — |

- 거래·키트 수령·보관함 정리는 메모리에서만 바뀌고 W2/W3/W4에서 함께 저장된다 ⟨Q7⟩. 은신처에서 크래시가 나면 마지막 저장(진입 시점)으로 돌아가는데, 프로필 전체를 한 번에 쓰므로 거래의 양쪽(탄피·아이템)이 함께 되돌아가 **복제·소실이 없다**.

**다음 실행 감지 지점**: 그 실행에서 프로필을 처음 읽을 때 = 첫 은신처 진입(`ProfileService.EnsureLoaded`) ⟨Q5⟩. 메인 메뉴는 프로필을 읽지 않으므로 은신처 전에는 아무 일도 없다(레이드는 은신처를 거쳐야만 시작할 수 있어 우회 경로 없음).
1. 파일의 `raidInProgress == true` → 파일 `loadout`의 정의 목록을 알림용으로 모으고(알 수 없는 id 포함 개수), `profile.Loadout = Empty`, `RaidInProgress = false`. 보관함·탄피·키트 시각은 그대로.
2. 같은 진입의 W1 저장으로 파일에 반영된다. W1 전에 다시 종료되면 다음 실행에서 같은 처리를 반복할 뿐이다(멱등).

**알리는 방식** ⟨Q6⟩: 은신처 첫 화면에 알림 패널 — "지난 레이드 도중 게임이 종료되어 사망으로 처리했다 (RULE-03). 반입 장비를 잃었다." + 잃은 아이템 묶음 목록(결과 패널과 같은 "이름 ×n") + [확인]. 알림 → (있으면) 결과 패널 → 허브 순서.

**같은 실행 안에서 결과 없이 은신처로 온 경우**(디버그·테스트: 레이드 씬에서 바로 Hideout을 로드): `TakeRaidReturn`이 `Interrupted` → `ProfileService.ApplyInterruptedRaid()`(사망과 같게: Loadout Empty, flag false, 알림 1회). 메모리에 raidInProgress=true가 남아 파일로 새지 않게 한다.

**남는 위험 창(인정)** ⟨Q31⟩: 탈출 순간 ~ 은신처 W1 저장(종료 오버레이 3초 + 씬 로드) 사이의 강제 종료는 다음 실행에서 반입 삭제로 처리된다. RULE-03("레이드 중 강제 종료 = 사망")의 경계로 받아들인다. 에디터에서 레이드 중 Play 중지도 강제 종료다(§7 사람 확인에 적음).

### 1.5 "세이브는 은신처 진입 시에만"의 해석 ⟨Q7⟩

- §13.3 "은신처 진입 시에만 기록"과 RULE-03 "세이브는 은신처에서만"의 목적은 **레이드 중 저장(세이브 스커밍) 금지**로 읽는다.
- 그래서 저장은 "은신처에 있는 동안의 경계 시점"으로 한정한다: 진입(W1), 나갈 때(W2·W4), 은신처에서 앱 종료(W3). 레이드 진입 기록(W4)은 §13.3이 명시한 예외다.
- 거래마다 저장(대안 C)은 하지 않는다: 크래시 시 되돌아가도 복제가 없어 필요 없고, 저장 지점을 늘리지 않는 쪽이 단순하다.
- 대안 A(진입·레이드 시작만)는 "은신처에서 정리 → 메인 메뉴 → 종료"로 정리·거래가 사라져 플레이어가 놀란다 → 비추천.

### 1.6 GameManager 역할 확장 (최소)

```csharp
// GameManager (Core, 유일한 싱글톤) — M5 추가분
public ProfileService Profiles { get; private set; }   // 세이브·프로필 (§13.3)
// Awake: Profiles = new ProfileService(new FileSaveStorage(Application.persistentDataPath), () => DateTime.UtcNow);
//        Flow = new GameFlow(load, setTimeScale, Profiles);
// 테스트·도구: 세이브 위치 교체. 로드한 프로필을 버린다(다음 EnsureLoaded가 새 위치에서 읽음 = "다음 실행" 흉내).
public void UseSaveStorage(ISaveStorage storage) => Profiles.UseStorage(storage);
```
- `Awake`에서는 파일을 읽지 않는다(경로 문자열만). 그래서 PlayMode 테스트가 첫 은신처 진입 전에 `UseSaveStorage`로 경로를 바꿀 수 있다(§2.6).

---

## 2. 세이브 형식

### 2.1 직렬화 방법 — JsonUtility
| 방법 | 판단 |
|---|---|
| **`UnityEngine.JsonUtility` (채택)** | 패키지 추가 없음, IL2CPP 동작, 빠름. 제약(다형성·Dictionary·최상위 배열·null 중첩 불가)은 **평평한 레코드 + 문자열 enum**으로 피한다 |
| Newtonsoft(`com.unity.nuget.newtonsoft-json`) | 패키지 추가 → 불가 |
| System.Text.Json | Unity .NET Standard 2.1에 기본 포함 아님 → 불가 |
| 손으로 쓴 파서 | 유지비·버그 위험 → 불가 |

- 모든 DTO는 `[Serializable]` + public 필드(camelCase = JSON 키). §13.3의 `raid_in_progress`는 JSON 키 `raidInProgress`로 쓴다(이름만 다름, 의미 같음).
- enum은 **이름 문자열**로 쓰고 `Enum.TryParse`로 읽는다(enum 순서가 바뀌어도 세이브가 깨지지 않게, 사람이 읽을 수 있게).
- `ToJson(data, prettyPrint: true)`(사람 확인·diff 편의, 크기 영향 작음: 아이템 300개 ≈ 80KB).

### 2.2 JSON 스키마 (version 1)

```json
{
    "version": 1,
    "savedAtUtc": "2026-10-22T09:30:00.0000000Z",
    "raidInProgress": false,
    "casings": 5480,
    "kitLastClaimUtcTicks": 638651718000000000,
    "stash": [
        { "id": "WPN-PST-01", "container": "Stash", "slot": "", "index": 0, "x": 0, "y": 0, "rotated": false,
          "rounds": -1, "usesLeft": -1, "loadedMagazineId": "", "loadedMagazineRounds": 0 }
    ],
    "loadout": [
        { "id": "WPN-RIF-01", "container": "Equipped", "slot": "Primary1", "index": 0, "x": 0, "y": 0, "rotated": false,
          "rounds": -1, "usesLeft": -1, "loadedMagazineId": "MAG-556-30", "loadedMagazineRounds": 17 },
        { "id": "RIG-01", "container": "Equipped", "slot": "TacticalVest", "index": 0, "x": 0, "y": 0, "rotated": false,
          "rounds": -1, "usesLeft": -1, "loadedMagazineId": "", "loadedMagazineRounds": 0 },
        { "id": "MAG-556-30", "container": "TacticalVest", "slot": "", "index": 0, "x": 0, "y": 0, "rotated": true,
          "rounds": 30, "usesLeft": -1, "loadedMagazineId": "", "loadedMagazineRounds": 0 },
        { "id": "MED-BND-01", "container": "Pocket", "slot": "", "index": 2, "x": 0, "y": 0, "rotated": false,
          "rounds": -1, "usesLeft": 1, "loadedMagazineId": "", "loadedMagazineRounds": 0 }
    ],
    "quickSlots": [3, -1, -1, -1]
}
```

| 필드 | 의미 | 읽을 때 |
|---|---|---|
| `version` | 스키마 버전(`SaveSerializer.CurrentVersion = 1`) | 없음·0 → 손상. 현재보다 큼 → 지원하지 않는 버전(손상과 같게 격리, 알림 문구만 다름). 작음 → 이전 버전 변환(지금은 없음) |
| `savedAtUtc` | 기록 시각(ISO 8601 "o"), 정보용 | 무시 |
| `raidInProgress` | §13.3 플래그 | true → §1.4 감지 |
| `casings` | 탄피 | 음수 → 0 |
| `kitLastClaimUtcTicks` | 키트 마지막 수령 UTC `DateTime.Ticks`, 0 = 받은 적 없음 | 0 → null |
| `stash` | 보관함 아이템 레코드(`container` = "Stash") | §2.4 |
| `loadout` | 은신처 장비 레코드. 순서 = `Loadout.Entries`(장착 → 주머니 → 조끼 → 배낭) | §2.4 |
| `quickSlots` | 길이 4, `loadout` 인덱스, -1 = 빈 칸 | 범위 밖·의약품 아님 → 빈 칸 |

**아이템 레코드 `ItemRecord`** (한 아이템 = 한 레코드, 무기의 장착 탄창은 무기 레코드 안의 필드)
| 필드 | 쓰는 경우 |
|---|---|
| `id` | `ItemDefinitionSO.id` |
| `container` | `"Stash"`, `"Equipped"`, `"Pocket"`, `"TacticalVest"`, `"Backpack"` (`GridKind` 이름 + Equipped/Stash) |
| `slot` | `container == "Equipped"`일 때 `EquipSlot` 이름 |
| `index` | 주머니 번호(0~3) |
| `x`, `y`, `rotated` | 그리드 왼쪽 위 칸·회전(§6 회전은 배치의 속성 — M2 D4) |
| `rounds` | `Magazine`일 때 탄 수, 그 밖 -1 |
| `usesLeft` | `MedicalItem`일 때 남은 횟수, 그 밖 -1 |
| `loadedMagazineId`, `loadedMagazineRounds` | `WeaponItem.LoadedMagazine`(없으면 빈 문자열) |

- 조끼·배낭 아이템 자체는 상태가 없다(내용물 그리드는 `PlayerInventory`가 장착 슬롯별로 가진다 — M2 D5). 내용물은 `container = "TacticalVest"/"Backpack"` 레코드로 따로 남는다. 보관함의 조끼·배낭은 항상 비어 있다(내용물 있는 조끼·배낭은 해제 불가 — M2 규칙).
- 상태 값 범위: `rounds` → [0, capacity], `usesLeft` → [0, uses] 로 자른다(0 횟수 의약품도 유지, 사용은 M2 `Depleted`로 거부).

### 2.3 아이템 정의 레지스트리 — `ItemDatabaseSO` (Core/Data)
- 필요하다: 세이브는 id 문자열만 갖고, 런타임에 id → SO를 찾을 방법이 지금 없다.
- `ItemDatabaseSO : ScriptableObject, IItemCatalog` — `List<ItemDefinitionSO> items`. `TryGet(id)`는 처음 호출 때 사전을 만든다(`[NonSerialized]` 캐시, null·빈 id 무시, 중복은 첫 번째 + 경고 1회).
- 에셋 `Data/Items/ItemDatabase.asset` — M5 데이터 메뉴가 `AssetDatabase.FindAssets("t:ItemDefinitionSO")`로 **프로젝트 전체 아이템 정의를 id 순으로** 채운다(멱등, 재실행 바이트 동일). 탄약 정의(AMMO-*)도 들어가지만 무해.
- 데이터 테스트가 "모든 ItemDefinitionSO 에셋이 DB에 있고 id가 유일"을 확인한다 → M6/M7에서 아이템을 추가하고 메뉴를 다시 안 돌리면 테스트가 잡는다(잡지 못하면 그 아이템은 다음 로드에서 "알 수 없는 id"로 사라진다).
- 테스트는 `IItemCatalog` 가짜(사전)를 쓴다.

### 2.4 읽기 규칙 (손상·누락·알 수 없는 id) ⟨Q22·Q23⟩

| 상황 | 처리 | 알림 |
|---|---|---|
| 파일 없음 | 새 프로필(보관함 10×30 빈 칸, 시작 탄피, 키트 즉시 가능) | 없음(첫 실행) |
| JSON 파싱 실패·빈 파일·`version` 없음 | 파일을 `save.corrupt.json`으로 **옮긴다**(덮어씀) → 새 프로필 | "세이브 파일이 손상돼 새로 시작했다 (save.corrupt.json으로 보관)" |
| `version` > 현재 | 위와 같게 격리 → 새 프로필 | "지원하지 않는 세이브 버전" |
| 알 수 없는 `id` | 그 레코드만 버린다(보고서 `UnknownItemIds`) | "불러오지 못한 아이템 N개" |
| 무기의 알 수 없는 `loadedMagazineId` | 탄창 없는 무기 | 같은 개수에 포함 |
| 보관함 자리가 범위 밖·겹침 | 같은 보관함에 자동 배치(`TryFindFreeCell`) → 안 되면 버림(`DroppedItemCount`) | 버린 개수 |
| 은신처 장비의 잘못된 `container`/`slot`/자리 | Inventory 적용 단계에서 거부 → 보관함 자동 배치 → 안 되면 버림 | 버린 개수(로그) |
| `raidInProgress` true | §1.4 감지 | RULE-03 알림 |

- `.bak` 자동 복구는 **하지 않는다** ⟨Q22⟩: 레이드 시작 직전의 백업(raidInProgress=false, 장비 있음)을 불러오면 RULE-03을 우회할 수 있다. 백업은 사람이 수동 복구할 때만 쓴다.
- 손상으로 새로 시작한 경우에도 W1에서 새 save.json이 쓰이고, 원본은 save.corrupt.json에 남는다.

### 2.5 쓰기 원자성 — `FileSaveStorage`
1. 디렉터리 생성(없으면).
2. `save.json.tmp`에 UTF-8(BOM 없음)로 쓰고 `Flush(true)`(디스크까지).
3. `save.json`이 있으면 `File.Replace(tmp, save.json, save.json.bak, ignoreMetadataErrors: true)` — 교체와 직전 세대 백업을 한 번에. 없으면 `File.Move(tmp, save.json)`.
4. `PlatformNotSupportedException`일 때만 대체 경로: `File.Copy(save, bak, true)` → `File.Delete(save)` → `File.Move(tmp, save)`(짧은 비원자 구간, tmp가 남아 수동 복구 가능). 그 밖 예외(`IOException`, `UnauthorizedAccessException`)는 false + 오류 문자열(호출자가 LogError·알림).
5. 읽을 때 남은 `.tmp`는 무시한다(다음 쓰기가 덮어씀).
- 파일: `<persistentDataPath>/save.json`, `save.json.bak`, `save.json.tmp`(일시), `save.corrupt.json`(손상 시). Windows 빌드 실제 위치: `%USERPROFILE%\AppData\LocalLow\DefaultCompany\Ashfall\`(ProjectSettings의 companyName·productName).

### 2.6 경로 주입 (테스트가 실제 persistentDataPath를 건드리지 않게)
- `ISaveStorage` 인터페이스로 저장소를 주입한다. 런타임 기본값만 `FileSaveStorage(Application.persistentDataPath)`.
- EditMode: `ProfileService`를 직접 만든다 — 로직은 `MemorySaveStorage`(테스트 도우미), 파일 동작은 `FileSaveStorage(Path.Combine(Path.GetTempPath(), "AshfallTests", Guid))` + TearDown 삭제.
- PlayMode: `Tests/PlayMode/SaveSandbox.cs`에 `[SetUpFixture]`(네임스페이스 `Ashfall.Tests.PlayMode` — 모든 PlayMode 테스트가 이 아래) → `OneTimeSetUp`에서 `GameManager.Instance.UseSaveStorage(new FileSaveStorage(<OS temp>/AshfallPlayModeSaves/<guid>))`. PlayMode 실행은 전체가 Play 모드 안이라 GameManager가 이미 있다. 은신처를 쓰는 테스트는 테스트마다 새 폴더로 다시 `UseSaveStorage`(격리).
  - 회귀 방지: PlayMode `SaveSandboxTests` — 저장 위치가 `Application.persistentDataPath` 아래가 아님을 확인.
- 에디터에서 사람이 Play하면 실제 경로를 쓴다(그게 게임 세이브다). 지우기·열기용 에디터 메뉴를 둔다(§4.6 `SaveTools`).

### 2.7 내장 모듈 매니페스트 ⟨Q24⟩
- 지금 `Packages/manifest.json`에는 M4가 넣은 `com.unity.modules.terrainphysics`만 명시돼 있다. M5가 쓰는 내장 모듈:
  - `com.unity.modules.jsonserialize`(JsonUtility) — lock에서 depth 1(addressables·render-pipelines.core·test-framework·uielements·visualscripting의 의존성).
  - `com.unity.modules.uielements`(UI Toolkit — 은신처 UI 전체) — lock에서 depth 1, **inputsystem 하나만** 의존한다(그 패키지가 빠지면 UI가 통째로 사라지는 구조, terrainphysics와 같은 종류의 위험).
- **추천: 두 모듈을 매니페스트에 명시**한다(동작 변화 없음, lock의 depth만 0으로 바뀜). 회귀 테스트 EditMode `SaveModuleTests`: 매니페스트에 두 줄이 있다 + `JsonUtility` 왕복 + `UnityEngine.UIElements.UIDocument` 형식 로드. System.IO는 모듈이 아니다. 그 밖 모듈은 건드리지 않는다.

---

## 3. 은신처 기능

### 3.1 보관함 (§9)
- `PlayerProfile.Stash` = Core `ItemGrid`(크기 `HideoutConfigSO.stashSize` = 10×30, W×H, 세로 스크롤). `GridRef(GridKind.Loot)`를 쓴다 — 은신처의 `PlayerInventory`가 이 그리드를 **열린 외부 그리드(ActiveLoot) 자리**로 열기 때문 ⟨Q28⟩. 새 `GridKind`를 만들지 않아 Inventory·UI 코드가 그대로 동작한다.
- 업그레이드 확장 없음 ⟨Q1⟩. 크기는 SO. 크기가 줄면 범위 밖 아이템은 §2.4 규칙(자동 배치 → 버림).
- 보관함에서는 검색 연출 없음(전부 드러난 상태로 감싼다: `LootContainerModel.ForExistingGrid`).
- 은신처에서 버리기(드래그를 패널 밖에 놓기)·의약품 사용은 막는다 ⟨Q27⟩(`PlayerInventory.AllowDrop/AllowUse = false`).

### 3.2 상인 (§9) ⟨Q8~Q12⟩
- 화폐 = 탄피, `PlayerProfile.Casings`(정수, 아이템 아님). 레이드에 가져가지 않으므로 잃지 않는다.
- 가격 규칙(`PriceRules`, Hideout):
  - 매수가(플레이어가 삼) = `basePrice ≤ 0`이면 판매 안 함, 아니면 `max(1, ceil(basePrice × buyPriceMultiplier))`. 기본 배율 1.0.
  - 매도가(플레이어가 팜) = `max(0, floor(basePrice × sellPriceMultiplier))`. 기본 배율 0.4. **무기는 장착 탄창의 매도가를 더한다**(탄창째 팔림).
  - 탄창의 남은 탄 수·의약품 남은 횟수는 가격에 반영하지 않는다(TODO(GDD-?) Q10). 사는 탄창은 가득(`ItemFactory.Create`), 무기는 탄창 없이.
  - 데이터 테스트: `sellPriceMultiplier ≤ buyPriceMultiplier`(되팔기 이득 없음).
- 판매 목록 = `TraderConfigSO.catalog`(고정, 재고 무제한, 시세 변동 없음) ⟨Q11⟩.
- 매도 범위 = **보관함에 있는 아이템만** ⟨Q12⟩. 은신처 장비에 있는 것은 먼저 보관함으로 옮긴다. 가격 0 아이템도 팔 수 있다(0 탄피 = 처분 — 은신처에는 버리기가 없다).
- 매수: 탄피 부족 → `NotEnoughCasings`, 보관함 자리 없음 → `NoSpace`(**돈을 받지 않는다**), 성공 시 보관함 자동 배치(`TryFindFreeCell`, 위→아래·왼→오른쪽).
- 시작 탄피 = `HideoutConfigSO.startingCasings`(추천 6000, TODO(GDD-?) Q9 — 새 플레이어가 M4 고정 키트에 해당하는 소총·탄창·지도·조끼를 한 번 살 수 있는 정도).

### 3.3 부랑자 키트 (§9) ⟨Q13~Q17⟩
- 내용 = `HideoutConfigSO.vagrantKitItems`: WPN-PST-01(탄창 없음), MAG-9MM-12 ×2(가득), MED-BND-01. GDD 문구 그대로.
- 지급 조건 ⟨Q13⟩: **쿨다운만**. 새 프로필은 바로 받을 수 있고, 받은 뒤 20분(`vagrantKitCooldownSec = 1200`) 동안 다시 못 받는다. §9 "신규 플레이어 및 전멸 시 구제"는 용도 설명으로 읽는다(조건 판정 없음 — 가장 단순, 쿨다운이 남용을 막는다).
- 시계 ⟨Q14⟩: 실제 시간 UTC(`DateTime.UtcNow`, 테스트는 주입 `Func<DateTime>`), 세이브에 `kitLastClaimUtcTicks`. 남은 시간 = `clamp(cooldown − (now − last), 0, cooldown)` → 시스템 시계를 되돌려도 최대 20분.
- 받을 공간 ⟨Q15⟩: 보관함에 **전부 들어갈 때만** 지급(하나씩 놓다가 실패하면 놓은 것을 되돌림). 실패 시 `NoSpace`, 쿨다운 소모 없음.
- 위치 ⟨Q17⟩: 보관함. 플레이어가 장비로 옮긴다.
- 키트 권총은 M5에서 플레이어가 쏠 수 없다 ⟨Q16⟩: `WeaponController`는 Primary1만, 그리고 자기 직렬화 무기(WPN-RIF-01)만 다룬다(M2 D11, M3 결정 "무기 교체 M7"). 키트는 아이템·매도용 구제이고, 사격 가능한 무기는 상인 소총(WPN-RIF-01)으로 얻는다. 은신처 키트 패널에 "권총은 아직 쓸 수 없다" 같은 문구는 넣지 않는다(M7에서 사라질 문구).

### 3.4 은신처 UI (UI Toolkit, `Art/UI/HideoutUI.uxml`) ⟨Q21·Q29·Q30⟩
- 화면 순서: **알림 패널**(`PendingNotice` 있을 때) → **결과 패널**(`Flow.PendingResult` 있을 때, M4 `RaidResultView` 재사용) → **허브**.
- 허브: 탄피 "탄피 N", 탭 [장비] / [상인], [부랑자 키트 받기](상태 "받을 수 있다" / "N분 뒤", 분 올림), [레이드 시작], [메인 메뉴], 한 줄 메시지(거래·키트 실패 이유).
  - [장비] 탭: 기존 `InventoryScreenView`를 그대로 쓴다 — 왼쪽 장착 7슬롯, 가운데 주머니·조끼·배낭(+퀵슬롯), 오른쪽 루트 패널 자리 = **보관함 10×30**(`ScrollView` 안, 세로 스크롤). 드래그·R 회전·Ctrl+클릭 빠른 이동은 M2 그대로. 패널 밖에 놓기는 거부(원위치).
  - [상인] 탭: 왼쪽 판매 목록(이름 · 매수가 · [구매], 탄피 부족이면 비활성), 오른쪽 보관함 아이템 목록(이름 · 매도가 · [판매]). 그리드가 아니라 목록(가장 단순).
- [레이드 시작]은 확인 창 없이 바로 시작 ⟨Q30⟩, 빈 장비도 허용 ⟨Q26⟩. 누르면 떠나는 중(`IsLeaving`) → 모든 버튼 비활성. 저장 실패로 거부되면 메시지.
- 탄 수·HP 수치·무게 숫자는 은신처에서도 그리지 않는다(M2/M4 정보 희소성 규칙 유지). 가격·탄피·남은 쿨다운 분은 표시한다(메뉴 화면 정보, HUD 아님).
- 커서: 항상 보이고 잠금 해제(MainMenuRoot와 같음). EventSystem 없음(M2 방식, Input System UI 맵). 퀵슬롯 배정(마우스 올리고 4~7)은 [장비] 탭에서 `Player/QuickSlot1~4` 액션만 켜서 지원(선택, 어렵으면 생략 — 퀵슬롯은 레이드 인벤토리에서도 배정 가능) ⟨Q19⟩.
- 메인 메뉴: [은신처] / [종료] ⟨Q20⟩. MainMenu.uxml의 결과 패널은 지운다.

---

## 4. 파일·클래스·공개 API

### 4.1 Core 변경 (`namespace Ashfall.Core`)
| 파일 | 변경 |
|---|---|
| `Items/ItemGrid.cs` | **이동**: `Scripts/Inventory/ItemGrid.cs`(+`.meta`) → `Scripts/Core/Items/ItemGrid.cs`(+`.meta`), `git mv`로 두 파일을 함께 옮겨 GUID 유지. 네임스페이스 `Ashfall.Inventory` → `Ashfall.Core`. 코드 변경 없음 |
| `Items/Loadout.cs` (새) | `LoadoutEntry`, `Loadout` (아래) |
| `Items/MedicalItem.cs` | 생성자 `MedicalItem(MedicalDefinitionSO definition, int usesLeft)` 추가(0~uses로 자름) |
| `Raid/RaidResult.cs` | 생성자 끝에 선택 인자 `Loadout carriedOutLoadout = null`, 프로퍼티 `CarriedOutLoadout`(Extracted가 아니면 항상 null) |
| `Data/ItemDatabaseSO.cs` (새) | `IItemCatalog`, `ItemDatabaseSO` |
| `Save/SaveData.cs` (새) | `SaveData`, `ItemRecord` DTO (§2.2) |
| `Save/SaveSerializer.cs` (새) | JSON ↔ DTO ↔ `PlayerProfile` |
| `Save/ISaveStorage.cs` (새) | `SaveReadStatus`, `ISaveStorage` |
| `Save/FileSaveStorage.cs` (새) | §2.5 |
| `Save/PlayerProfile.cs` (새) | `ProfileDefaults`, `PlayerProfile` |
| `Save/SaveLoadReport.cs` (새) | `SaveLoadStatus`, `SaveLoadReport` |
| `Save/ProfileService.cs` (새) | 로드·저장·RULE-03·레이드 시작/반영 |
| `Flow/GameFlow.cs` | `GamePhase.Hideout`, 생성자 3번째 인자 `ProfileService profiles = null`, `EnterHideout`, `LeaveHideout`, `StartRaid(Loadout)`(인자 없는 것 삭제), `CompleteRaid` 목적지 Hideout, `RaidReturn`/`TakeRaidReturn`, `HasRaidInFlow`, `TryGetRaidLoadout` |
| `SceneNames.cs` | `Hideout = "Hideout"` |
| `GameManager.cs` | `Profiles`, `UseSaveStorage`, Flow 생성에 Profiles, 주석의 "세이브·보관함은 M5" 갱신 |
| `Services/IHideoutService.cs` (새) | `TradeResult`, `KitClaimResult`, `TraderOffer`, `IHideoutService` |
| `Events/GameEvents.cs` | `HideoutChanged` (── M5: Hideout → 다른 기능 ──) |
| `Services/ServiceRegistry.cs` | 주석의 등록 대상에 `IHideoutService(Hideout)` 추가(코드 변경 없음) |
| `Items/InventorySnapshot.cs` | 변경 없음(반입 정의 목록용으로 계속 사용). 주석의 "M5 Hideout도 같은 함수" → "M5는 `Loadout.Capture`" |

```csharp
// Items/Loadout.cs
// 장비 하나의 자리: Equipped(slot) 또는 InGrid(Pocket[i] / TacticalVest / Backpack, cell, rotated). Loot 자리는 없다.
public readonly struct LoadoutEntry
{
    public readonly ItemInstance Item;
    public readonly ItemLocation Location;
    public LoadoutEntry(ItemInstance item, ItemLocation location);
}

// 반입(§18 Loadout) = 장착 슬롯 + 저장 공간 배치 + 퀵슬롯. 은신처 장비 ↔ 레이드 소지품을 오가는 묶음. 만든 뒤 바뀌지 않는다.
public sealed class Loadout
{
    public const int QuickSlotCount = 4;                 // §12.1 키 4~7 (PlayerInventory.QuickSlotCount가 이것을 쓴다 — 2a)
    public static readonly Loadout Empty;
    public Loadout(IReadOnlyList<LoadoutEntry> entries, IReadOnlyList<ItemInstance> quickSlots);   // 복사. null → 빈 것. 같은 인스턴스 중복은 첫 번째만
    public IReadOnlyList<LoadoutEntry> Entries { get; }
    public IReadOnlyList<ItemInstance> QuickSlots { get; }  // 길이 4, 빈 칸 null. Entries 밖 아이템은 null로 바꿔 둔다
    public bool IsEmpty { get; }
    public int CollectItems(List<ItemInstance> into);       // Entries 순서로 더한다 (= InventorySnapshot.Collect 순서)
    // 장착 7슬롯(EquipSlot 순) → StorageGrids(순서·배치 순, Loot 종류는 건너뜀) → 퀵슬롯 0~3. inventory null → Empty.
    public static Loadout Capture(IInventoryService inventory);
}

// Data/ItemDatabaseSO.cs
public interface IItemCatalog
{
    bool TryGet(string id, out ItemDefinitionSO definition);
}
[CreateAssetMenu(menuName = "Ashfall/Item Database", fileName = "ItemDatabase")]
public class ItemDatabaseSO : ScriptableObject, IItemCatalog
{
    [Tooltip("모든 아이템 정의 (M5 데이터 메뉴가 id 순으로 채운다)")] public List<ItemDefinitionSO> items = new List<ItemDefinitionSO>();
    public int Count { get; }
    public bool TryGet(string id, out ItemDefinitionSO definition);
}

// Save/SaveData.cs — JsonUtility DTO (§2.2). 필드 이름 = JSON 키
[Serializable] public sealed class SaveData
{
    public int version;
    public string savedAtUtc;
    public bool raidInProgress;
    public int casings;
    public long kitLastClaimUtcTicks;
    public List<ItemRecord> stash = new List<ItemRecord>();
    public List<ItemRecord> loadout = new List<ItemRecord>();
    public int[] quickSlots;
}
[Serializable] public sealed class ItemRecord
{
    public string id;
    public string container;   // "Stash" | "Equipped" | "Pocket" | "TacticalVest" | "Backpack"
    public string slot;        // EquipSlot 이름 (Equipped)
    public int index;          // 주머니 번호
    public int x, y;
    public bool rotated;
    public int rounds = -1;
    public int usesLeft = -1;
    public string loadedMagazineId;
    public int loadedMagazineRounds;
}

// Save/ISaveStorage.cs
public enum SaveReadStatus { Missing, Ok, Error }
public interface ISaveStorage
{
    string Location { get; }                                           // 로그·알림용 (파일 경로)
    SaveReadStatus TryRead(out string text, out string error);
    bool TryWrite(string text, out string error);                     // 원자적 (§2.5)
    bool TryQuarantine(out string movedTo, out string error);         // 손상 파일 → save.corrupt.json
}

// Save/FileSaveStorage.cs
public sealed class FileSaveStorage : ISaveStorage
{
    public const string DefaultFileName = "save.json";
    public FileSaveStorage(string directory, string fileName = DefaultFileName);
    public string FilePath { get; }      // <dir>/save.json
    public string TempPath { get; }      // <dir>/save.json.tmp
    public string BackupPath { get; }    // <dir>/save.json.bak
    public string CorruptPath { get; }   // <dir>/save.corrupt.json
}

// Save/PlayerProfile.cs
public readonly struct ProfileDefaults
{
    public readonly Vector2Int StashSize;     // HideoutConfigSO.stashSize
    public readonly int StartingCasings;      // HideoutConfigSO.startingCasings
    public ProfileDefaults(Vector2Int stashSize, int startingCasings);
}
// 씬을 넘는 플레이어 진행 상태 (§9 "진행도 = 보유 아이템"). GameManager.Profiles가 하나를 가진다.
public sealed class PlayerProfile
{
    public PlayerProfile(Vector2Int stashSize, int casings);
    public ItemGrid Stash { get; }                 // GridRef(GridKind.Loot) — 은신처에서 열린 외부 그리드 자리 (TODO(GDD-?) Q28)
    public Loadout Loadout { get; set; }           // 은신처 장비 (다음 반입). null 대입 → Empty
    public int Casings { get; set; }               // 음수 대입 → 0
    public DateTime? LastKitClaimUtc { get; set; }
    public bool RaidInProgress { get; internal set; }   // ProfileService·SaveSerializer만 바꾼다
}

// Save/SaveLoadReport.cs
public enum SaveLoadStatus { NotLoaded, NewProfile, Loaded, Corrupt, UnsupportedVersion }
public sealed class SaveLoadReport
{
    public SaveLoadStatus Status { get; set; }
    public bool CrashPenaltyApplied { get; set; }                      // RULE-03
    public List<ItemDefinitionSO> CrashLost { get; } = new List<ItemDefinitionSO>();   // 표시용 (알 수 없는 id 제외)
    public List<string> UnknownItemIds { get; } = new List<string>();
    public int DroppedItemCount { get; set; }                         // 자리 문제로 버린 수
    public string QuarantinedPath { get; set; }
    public string Error { get; set; }
    public bool HasNotice { get; }   // Corrupt·UnsupportedVersion·CrashPenalty·Unknown·Dropped 중 하나라도
}

// Save/SaveSerializer.cs
public static class SaveSerializer
{
    public const int CurrentVersion = 1;
    public static string ToJson(PlayerProfile profile, DateTime utcNow);
    // 실패면 false + failStatus(Corrupt | UnsupportedVersion). 예외를 밖으로 던지지 않는다.
    public static bool TryParse(string json, out SaveData data, out SaveLoadStatus failStatus, out string error);
    // §2.4 규칙. raidInProgress는 그대로 옮긴다(감지는 ProfileService).
    public static PlayerProfile ToProfile(SaveData data, IItemCatalog catalog, ProfileDefaults defaults, SaveLoadReport report);
    public static ItemRecord ToRecord(ItemInstance item);   // 상태 필드만. 자리는 호출자
    public static ItemInstance FromRecord(ItemRecord record, IItemCatalog catalog, SaveLoadReport report);   // 모르면 null
}

// Save/ProfileService.cs
public sealed class ProfileService
{
    public ProfileService(ISaveStorage storage, Func<DateTime> utcNow);
    public bool IsLoaded { get; }
    public PlayerProfile Profile { get; }               // IsLoaded가 아니면 null
    public SaveLoadReport PendingNotice { get; }        // 확인 전 알림. 없으면 null
    public bool LastSaveFailed { get; }
    public string LastSaveError { get; }
    public string StorageLocation { get; }

    public void UseStorage(ISaveStorage storage);       // 테스트·도구. IsLoaded = false, 알림 지움
    // 처음 한 번만 읽는다(이후 호출은 현재 상태 그대로). 파일 없음/손상/버전/알 수 없는 id(§2.4) + RULE-03 감지(§1.4).
    public SaveLoadReport EnsureLoaded(IItemCatalog catalog, ProfileDefaults defaults);
    public bool Save();                                  // 실패면 LastSaveFailed + LogError
    // GameFlow.StartRaid만 부른다. Loadout을 두고 RaidInProgress=true로 저장 → 성공하면 메모리 Loadout = Empty.
    // 저장 실패면 RaidInProgress·Loadout을 되돌리고 false.
    public bool BeginRaid(Loadout loadout);
    public void CancelRaid(Loadout loadout);            // 씬 로드 요청 실패: 되돌리고 저장
    // RULE-01 반영 지점: Survived && CarriedOutLoadout != null → Loadout = 그것, 아니면 Empty. RaidInProgress = false. 저장은 호출자.
    public void ApplyRaidResult(RaidResult result);
    public void ApplyInterruptedRaid();                 // 같은 실행에서 결과 없이 돌아옴 = 사망과 같게 + 알림 (§1.4)
    public void AcknowledgeNotice();
}

// Flow/GameFlow.cs (M5 변경분)
public enum GamePhase { None, MainMenu, Raid, Hideout }   // Hideout 끝에 추가
public enum RaidReturn { None, Finished, Interrupted }
public GameFlow(Func<string, bool> loadScene, Action<float> setTimeScale, ProfileService profiles = null);
public bool EnterHideout();                    // MainMenu·None → Hideout
public bool LeaveHideout();                    // Hideout → MainMenu
// Hideout에서만. profiles 없음·미로드 → false. profiles.BeginRaid 실패 → false. 로드 요청 실패 → CancelRaid + false.
public bool StartRaid(Loadout loadout);
public bool CompleteRaid(RaidResult result);   // M4 규칙 + 목적지 Hideout
public bool HasRaidInFlow { get; }             // StartRaid 성공 ~ TakeRaidReturn
// 흐름 레이드가 진행 중이고 sceneName == Raid_CrimsonBasin이며 아직 완료 전일 때만 true (확인 씬·직접 Play는 false)
public bool TryGetRaidLoadout(string sceneName, out Loadout loadout);
// 은신처 진입에서 한 번: 흐름 레이드 표를 꺼낸다. 결과 있음 → Finished(result), 결과 없음 → Interrupted, 표 없음 → None.
public RaidReturn TakeRaidReturn(out RaidResult result);

// Services/IHideoutService.cs — Hideout(HideoutController)이 등록. 은신처 UI가 쓴다.
public enum TradeResult { Ok, NotEnoughCasings, NoSpace, NotForSale, NotInStash, Unavailable }
public enum KitClaimResult { Ok, OnCooldown, NoSpace, Unavailable }
public readonly struct TraderOffer
{
    public readonly ItemDefinitionSO Item;
    public readonly int BuyPrice;
    public TraderOffer(ItemDefinitionSO item, int buyPrice);
}
public interface IHideoutService
{
    int Casings { get; }
    string TraderName { get; }
    IReadOnlyList<TraderOffer> Offers { get; }
    IGridView Stash { get; }
    int GetSellPrice(ItemInstance item);          // 보관함에 없으면 -1
    TradeResult TryBuy(ItemDefinitionSO item);
    TradeResult TrySell(ItemInstance item);
    bool IsKitAvailable { get; }
    float KitCooldownRemainingSec { get; }        // 0 = 받을 수 있음
    KitClaimResult TryClaimKit();
    SaveLoadReport PendingNotice { get; }         // ProfileService.PendingNotice
    bool LastSaveFailed { get; }
    void AcknowledgeNotice();
    bool IsLeaving { get; }                       // 레이드 시작·메뉴 복귀 요청 후 (버튼 비활성)
    bool StartRaid();                             // 현재 은신처 장비 = 반입
    bool ReturnToMainMenu();
}

// Events/GameEvents.cs 추가 (── M5: Hideout → 다른 기능 ──)
// 탄피·보관함 내용·키트 상태가 바뀜 → 은신처 UI가 IHideoutService·IInventoryService를 다시 읽는다.
public readonly struct HideoutChanged : IGameEvent { }
```

### 4.2 Hideout (`Ashfall.Hideout`, `Scripts/Hideout/`)
| 파일 | 종류 | 내용 |
|---|---|---|
| `Data/HideoutConfigSO.cs` | SO | 에셋 `Data/Hideout/HideoutConfig.asset` |
| `Data/TraderConfigSO.cs` | SO | 에셋 `Data/Hideout/TraderConfig.asset` |
| `Logic/PriceRules.cs` | static | §3.2 |
| `Logic/StashRules.cs` | static | `TryPlaceNew(ItemGrid, item)`(자동 배치), `TryPlaceAll(ItemGrid, IReadOnlyList<ItemInstance>)`(전부 아니면 되돌림) |
| `Logic/TraderModel.cs` | class | 프로필·설정으로 매매 |
| `Logic/VagrantKitModel.cs` | class | 쿨다운·지급 |
| `Logic/HideoutService.cs` | class, `IHideoutService` | 진입 절차·저장·레이드 시작·메뉴 복귀 + 위 모델 조합 (순수) |
| `Runtime/HideoutController.cs` | MonoBehaviour | 씬 브리지 |

```csharp
[CreateAssetMenu(menuName = "Ashfall/Hideout Config", fileName = "HideoutConfig")]
public class HideoutConfigSO : ScriptableObject
{
    [Header("§9 보관함")]
    [Tooltip("보관함 그리드 (칸, W×H). §9 10×30. 업그레이드 없음 (TODO(GDD-?) Q1)")] public Vector2Int stashSize = new Vector2Int(10, 30);
    [Header("§9 탄피")]
    [Tooltip("새 프로필 시작 탄피. TODO(GDD-?) Q9")] public int startingCasings = 6000;
    [Header("§9 부랑자 키트")]
    [Tooltip("쿨다운 (실제 시간 초). §9 20분")] public float vagrantKitCooldownSec = 1200f;
    [Tooltip("지급 아이템 (같은 것은 여러 번). §9 녹슨 권총 + 탄창 2개 + 붕대")] public ItemDefinitionSO[] vagrantKitItems;
}

[CreateAssetMenu(menuName = "Ashfall/Trader Config", fileName = "TraderConfig")]
public class TraderConfigSO : ScriptableObject
{
    public string id = "TRD-01";
    [Tooltip("상인 이름. TODO(GDD-?)")] public string displayName = "고물상";
    [Tooltip("매수가 = 올림(basePrice × 이 값), 최소 1. TODO(GDD-?) Q10")] public float buyPriceMultiplier = 1f;
    [Tooltip("매도가 = 내림(basePrice × 이 값). 매수 배율 이하. TODO(GDD-?) Q10")] public float sellPriceMultiplier = 0.4f;
    [Tooltip("판매 목록 (재고 무제한). TODO(GDD-?) Q11")] public ItemDefinitionSO[] catalog;
}

public static class PriceRules
{
    public static int BuyPrice(ItemDefinitionSO item, TraderConfigSO trader);    // 판매 안 함 = -1
    public static int SellPrice(ItemInstance item, TraderConfigSO trader);       // 무기 + 장착 탄창. null → -1
}

public sealed class TraderModel
{
    public TraderModel(PlayerProfile profile, TraderConfigSO trader);
    public IReadOnlyList<TraderOffer> Offers { get; }        // catalog 순, 가격 ≤ 0·null 제외
    public int GetSellPrice(ItemInstance item);              // 보관함 밖 → -1
    public TradeResult TryBuy(ItemDefinitionSO item);        // catalog 밖 → NotForSale. 자리 먼저 확인 후 차감
    public TradeResult TrySell(ItemInstance item);           // 보관함 밖 → NotInStash
}

public sealed class VagrantKitModel
{
    public VagrantKitModel(PlayerProfile profile, HideoutConfigSO config, Func<DateTime> utcNow);
    public float RemainingSec { get; }                        // §3.3 식
    public bool IsAvailable { get; }
    public KitClaimResult TryClaim();                         // 전부 들어가야 지급, 성공 시 LastKitClaimUtc = now
}

public sealed class HideoutService : IHideoutService
{
    // 진입 절차(W1): EnsureLoaded → TakeRaidReturn(Finished: ApplyRaidResult / Interrupted: ApplyInterruptedRaid /
    // None인데 PendingResult 있음: 표시만 + 경고) → Save. 진입 보고서를 돌려준다.
    public static SaveLoadReport Enter(ProfileService profiles, GameFlow flow, IItemCatalog catalog, ProfileDefaults defaults);

    public HideoutService(ProfileService profiles, GameFlow flow, HideoutConfigSO config, TraderConfigSO trader,
        Func<IInventoryService> loadoutSource, Func<DateTime> utcNow, Action publishChanged);
    public bool SaveNow();   // W2/W3: 장비 Capture → profile.Loadout → Save. 떠나는 중이면 아무것도 안 함
    // StartRaid: Capture → flow.StartRaid(captured). 실패면 IsLeaving 해제.
    // ReturnToMainMenu: SaveNow → flow.LeaveHideout().
}
```

```csharp
// Runtime/HideoutController.cs — 은신처 씬 진입점. 다른 은신처 오브젝트(HideoutInventoryBehaviour, HideoutRoot)보다 먼저 Awake.
[DefaultExecutionOrder(-100)]
public sealed class HideoutController : MonoBehaviour
{
    [SerializeField] private HideoutConfigSO config;
    [SerializeField] private TraderConfigSO trader;
    [SerializeField] private ItemDatabaseSO itemDatabase;
    // Awake: 셋 중 하나라도 없으면 LogError + 비활성. HideoutService.Enter(...) → new HideoutService(...).
    //        loadoutSource = () => Services.TryGet<IInventoryService>.
    // OnEnable: Register<IHideoutService>, OnDisable: Unregister.
    // OnApplicationQuit: Flow.Phase == Hideout && !IsLeaving → SaveNow (W3).
    // [ContextMenu] "Debug/탄피 +1000", "Debug/키트 쿨다운 초기화", "Debug/지금 저장".
    public HideoutService Service { get; }   // 테스트·디버그
}
```
- 순서 보장: `HideoutController.Awake`(-100)가 프로필을 읽고 진입 저장까지 끝낸 뒤 `HideoutInventoryBehaviour.Awake`(기본 0)가 장비를 놓는다. UI는 Start/Update에서 서비스를 찾는다(M4 MainMenuRoot의 지연 빌드 방식).

### 4.3 Inventory (`Ashfall.Inventory`)
| 파일 | 변경 |
|---|---|
| `ItemGrid.cs` | **Core로 이동**(1단계) |
| `PlayerInventory.cs` | `QuickSlotCount = Loadout.QuickSlotCount`. `AllowDrop`·`AllowUse`(기본 true; false면 `TryDrop`·`TryUse` 거부). `TryPlaceNew(item, GridRef, cell, rotated)`(밖에서 들어온 아이템을 정확한 자리에, Loot 자리 불가, 잠금·Frozen 거부). `TryApplyLoadout(Loadout, List<ItemInstance> rejected)`: 장착 항목 먼저(`TryEquipNew` — 조끼·배낭 그리드 생성) → 그리드 항목 `TryPlaceNew` → 실패면 `TryAutoStore` → 그래도 실패면 rejected. 퀵슬롯 `TryAssignQuickSlot`. 반환 = 놓은 수 |
| `LootContainerModel.cs` | `static LootContainerModel ForExistingGrid(string displayName, ItemGrid grid)` — 주어진 그리드를 그대로 감싸고 전부 드러난 상태(검색 없음) |
| `PlayerInventoryBehaviour.cs` | Awake: `GameManager.Instance?.Flow?.TryGetRaidLoadout(gameObject.scene.name, out loadout)`가 true면 `TryApplyLoadout`(rejected는 LogError — 같은 설정이라 정상 경로에선 없음), 아니면 기존 `ApplyStartingItems`. 확인 씬·직접 Play는 그대로 |
| `HideoutInventoryBehaviour.cs` (새) | 은신처의 얇은 브리지. 필드 `InventoryConfigSO config`, `string stashDisplayName = "보관함"`. Awake: `Profiles.IsLoaded` 아니면 LogError + 비활성. `PlayerInventory`(AllowDrop/AllowUse false) → `TryApplyLoadout(profile.Loadout)` → rejected는 `StashRules`와 같은 자동 배치로 `profile.Stash`(실패 시 LogWarning, 버림) → `OpenLoot(ForExistingGrid(stashDisplayName, profile.Stash))`. OnEnable/OnDisable: `IInventoryService` 등록·해지, `HideoutChanged` 구독(보관함이 상인에 의해 바뀜 → `InventoryChanged` 발행). `Changed` → `InventoryChanged`. 열린 루트가 닫히면 다시 연다. `IMagazineSource`는 등록하지 않는다 |
| `Inventory` 나머지 | 변경 없음 |

- 은신처의 `InventoryChanged`는 Inventory(이 브리지)만 발행한다. Hideout은 `HideoutChanged`만 발행하고, 이 브리지가 받아 `InventoryChanged`로 다시 알린다(보관함 그리드가 바뀌었으니).

### 4.4 Raid (`Ashfall.Raid`)
| 파일 | 변경 |
|---|---|
| `Logic/RaidResultBuilder.cs` | 오버로드 `Build(RaidSession, IReadOnlyList<ItemDefinitionSO> broughtIn, Loadout endLoadout)`: endItems = `endLoadout.CollectItems`, Extracted면 `carriedOutLoadout = endLoadout`, 아니면 null. 기존 목록 오버로드는 유지(Loadout 없음 → CarriedOutLoadout null) |
| `Runtime/RaidController.cs` | `OnSessionEnded`: `InventorySnapshot.Collect` 대신 `Loadout.Capture(inventory)` → 새 오버로드(같은 시점, RaidEnded 직전). 주석의 "M5 경계" 갱신. 흐름 밖 처리·try/finally 그대로 |

### 4.5 UI (`Ashfall.UI`)
| 파일 | 내용 |
|---|---|
| `MainMenuPresenter.cs` | 패널 Menu만(결과 패널 삭제). [은신처] → `flow.EnterHideout()`(한 번만, 실패 시 재시도 가능 — M4 리뷰 수정 방식), [종료] |
| `MainMenuRoot.cs` | 결과 뷰 제거, 버튼 이름 `menu-start`는 유지하고 문구만 "은신처"(테스트·씬 호환) |
| `HideoutPresenter.cs` (새, 순수) | `IHideoutService` + `GameFlow` → 패널(Notice → Result → Hub), 탭(Loadout/Trader), 탄피·키트 문구, 메시지(거래·키트·저장 실패 이유), 명령(ConfirmNotice, ConfirmResult = `AcknowledgeResult`, SelectTab, Buy, Sell, ClaimKit, StartRaid, ReturnToMenu), `IsLeaving` |
| `HideoutNoticeViewModel.cs` (새, 순수) | `SaveLoadReport` → 문구 줄 + 잃은 아이템 묶음(RaidResultViewModel의 묶음 규칙 재사용) |
| `TraderViewModel.cs` (새, 순수) | 판매 목록 줄(이름·가격·살 수 있나), 보관함 매도 줄(아이템별, 그리드 배치 순) |
| `HideoutRoot.cs` (새) | UIDocument 진입점: 서비스 지연 획득, 뷰 연결, `InventoryScreenView` 생성(드래그 임계 = HudConfig 값 또는 기본), UI 맵 + RotateItem, 커서 표시, `HideoutChanged`·`InventoryChanged` 구독 → 다시 그림, 키트 남은 시간 1초마다 갱신 |
| `HideoutHubView.cs`, `TraderView.cs`, `HideoutNoticeView.cs` (새) | 뷰 |
| `RaidResultView.cs` | 그대로 재사용(필요하면 루트 요소 이름만 상수화) |
| `UiText.cs` | 은신처·상인·키트·알림·메인 메뉴 문구 |
| `Art/UI/HideoutUI.uxml` (새), `Art/UI/MainMenu.uxml`(결과 패널 제거, 버튼 문구), `Art/UI/Ashfall.uss`(은신처 스타일, §10.1 토큰 변수만) | |

- `HideoutUI.uxml`은 인벤토리 화면 요소(`inventory-screen`, `inv-equip-slots`, `inv-storage-grids`, `inv-quick-slots`, `inv-loot-panel`, `inv-loot-title`, `inv-loot-status`, `inv-loot-grid-host`, `inv-drag-layer` — `InventoryScreenView` 상수)를 **복사해 넣는다**(GameUI.uxml은 건드리지 않음 — 레이드 UI 회귀 방지. 템플릿 공유는 M6 UI 정리 때). `inv-loot-grid-host`는 `ScrollView`(이름 `hideout-stash-scroll`) 안에 둔다. 드래그 좌표가 스크롤 오프셋에서 맞는지 2b가 EditMode/사람 확인.
- 은신처 요소 이름(2b 소유 계약, 3단계는 UXML 경로와 `HideoutRoot`만 쓴다): `hideout-root`, `hideout-notice-panel`/`-text`/`-list`/`-confirm`, 결과 패널은 MainMenu의 `result-*` 이름 그대로, `hideout-hub`, `hideout-casings`, `hideout-tab-loadout`, `hideout-tab-trader`, `hideout-kit-button`, `hideout-kit-status`, `hideout-start-raid`, `hideout-main-menu`, `hideout-message`, `trader-panel`, `trader-offers`, `trader-sell`.

### 4.6 EditorTools (`Ashfall.EditorTools`)
| 파일 | 내용 |
|---|---|
| `M5DataSetup.cs` (새, 2c) | 메뉴 `Ashfall/Setup/Create M5 Data`, 배치 `CreateAll`/`CreateData`. 먼저 `M4DataSetup.CreateData()`. ① 기존 아이템 에셋의 `basePrice`를 §4.6 표 값으로 **기록**(매번 같은 값 → 멱등) ② `Data/Hideout/HideoutConfig.asset`(없을 때만) ③ `Data/Hideout/TraderConfig.asset`(없을 때만) ④ `Data/Items/ItemDatabase.asset`(매번 FindAssets → id 순 정렬로 덮어씀, GUID 유지, 중복 id면 예외). 경로 상수 public |
| `M5SceneSetup.cs` (새, 3단계) | 메뉴 `Ashfall/Setup/Create M5 Hideout Scene`, 배치 `CreateAll`: `M5DataSetup.CreateData()` → Hideout 씬 NewScene(Default) → Light 삭제, 카메라 SolidColor 검정 → "HideoutSystems"(`HideoutController`: config·trader·itemDatabase / `HideoutInventoryBehaviour`: InventoryConfig) → "HideoutUI"(`UIDocument` = HideoutUI.uxml + GamePanelSettings, `HideoutRoot`), EventSystem 없음 → `Scenes/Hideout.unity` 저장(있으면 덮어씀, GUID 유지) → 빌드 설정 |
| `SetupUtil.cs` | `ApplyBuildScenes()`(3단계): Boot → MainMenu → Hideout(있으면) → Raid_CrimsonBasin(있으면). M4SceneSetup의 빌드 설정 단계도 이것을 부르게 바꾼다(M4 메뉴를 다시 돌려도 Hideout이 빠지지 않게) |
| `M4SceneSetup.cs` | 빌드 설정 부분만 `SetupUtil.ApplyBuildScenes()`로(3단계). 씬 생성 동작 불변 |
| `SaveTools.cs` (새, 3단계) | 메뉴 `Ashfall/Save/Open Save Folder`(`EditorUtility.RevealInFinder`), `Ashfall/Save/Delete Save Files`(확인 창, save.json·.bak·.tmp·corrupt). Play 중엔 비활성 |
- 씬·`.asset` YAML은 손으로 쓰지 않는다. 모두 위 메뉴를 `-executeMethod`로 실행. 새 파일·폴더의 `.meta`는 Unity가 만든다.
- MainMenu 씬은 다시 만들지 않는다(UIDocument가 같은 MainMenu.uxml GUID를 가리킴 — UXML 내용만 2b가 바꾼다). Raid 씬도 다시 만들지 않는다(코드 변경만, `startingItems`는 흐름 밖에서 계속 쓰임).

**아이템 가격 표 (basePrice, 전부 TODO(GDD-?) Q10)** — 2c가 기록, 상인 판매 목록 = ✔
| id | 이름 | basePrice | 판매 | | id | 이름 | basePrice | 판매 |
|---|---|---|---|---|---|---|---|---|
| WPN-RIF-01 | 군용 소총 | 4000 | ✔ | | MED-BND-01 | 붕대 | 60 | ✔ |
| WPN-SMG-01 | 개조 SMG | 1500 | | | MED-TRQ-01 | 지혈대 | 150 | ✔ |
| WPN-PST-01 | 녹슨 권총 | 400 | | | MED-SPL-01 | 부목 | 120 | ✔ |
| MAG-556-30 | 5.56 탄창 | 400 | ✔ | | RIG-01 | 전술조끼 | 600 | ✔ |
| MAG-9MM-30 | SMG 탄창 | 300 | | | BAG-01 | 배낭 | 800 | ✔ |
| MAG-9MM-12 | 권총 탄창 | 120 | | | ARM-HLM-01 | 헬멧 | 1200 | ✔ |
| ITM-MAP-01 | 종이 지도 | 100 | ✔ | | ARM-BDY-01 | 방탄복 | 2500 | ✔ |
| ITM-FLR-01 | 신호탄 | 500 | (안 팖, EXT-03 희소성) | | JNK-01/02/03 | 파이프/엔진 부품/전선 | 40/300/80 | |
| AMMO-556/9MM | 탄약 정의 | 0 | (인벤토리 아이템 아님) | | | | | |
- 판매 목록에 지도가 반드시 있어야 한다: 부랑자 키트에는 지도가 없고(RULE-06 지도는 아이템으로만), M4처럼 고정 키트에 지도가 붙지 않는다.
- 시작 탄피 6000 = 소총 4000 + 5.56 탄창 2(800) + 지도 100 + 조끼 600 = 5500, 여유 500.

### 4.7 asmdef 변경
| asmdef | 변경 |
|---|---|
| `Ashfall.Core` | 없음(참조 없음 유지) |
| `Ashfall.Hideout` | 없음(Core만, 이미 있음). 처음으로 스크립트가 생긴다 |
| `Ashfall.Inventory`, `Ashfall.Raid`, `Ashfall.UI`, `Ashfall.Player`, `Ashfall.Weapons`, `Ashfall.AI` | 없음 |
| `Ashfall.EditorTools` | `+ Ashfall.Hideout` (1단계 — 2c의 데이터 메뉴가 Hideout SO를 만든다) |
| 테스트 2개 | 없음(이미 Hideout 포함) |
새 asmdef 없음. 기능끼리 참조 추가 없음. 패키지 참조 추가 없음.

### 4.8 입력 변경
- **없음.** 은신처는 기존 UI 맵(Point·Click·Cancel·`RotateItem`)만 쓴다. 퀵슬롯 배정을 넣으면 기존 `Player/QuickSlot1~4` 액션만 개별 Enable(Player 맵 전체는 켜지 않음) ⟨Q19⟩.

### 4.9 빌드 설정 순서
`Boot`(0) → `MainMenu`(1) → `Hideout`(2) → `Raid_CrimsonBasin`(3). `BuildSettingsTests`는 정확히 이 4개·이 순서를 확인한다. `BuildTools.BuildWindows64`는 빌드 설정을 그대로 쓰므로 변경 없음.

---

## 5. GDD에 없거나 모호한 질문 (오케스트레이터가 ★추천안을 그대로 채택)

| # | 질문 | 선택지 | ★추천 |
|---|---|---|---|
| Q1 | 보관함 업그레이드 확장(§9 "업그레이드로 확장") | A M5 없음, 10×30 고정(SO) / B 탄피로 1단계 확장 | **A** — M5 AC는 "보관함"뿐, 은신처 업그레이드 체계는 별도 마일스톤 |
| Q2 | 보관함 그리드 로직 위치(Hideout은 Inventory 참조 불가) | A `ItemGrid`를 Core로 이동(.meta 함께, GUID 유지) / B Inventory가 `IStashService` 구현 / C Hideout 자체 그리드 | **A** — M3 무기 로직 이동 전례, 순수 클래스, 동기화할 사본 없음 |
| Q3 | 반입 장비 모델 | A 은신처 장비(캐릭터 장비)를 유지, 탈출 시 반출품이 위치 그대로 장비로 돌아옴 / B 탈출 시 반출품 전부 보관함 자동 배치, 장비는 매번 비움 | **A** — 넘침 처리·구조 손실 없음 |
| Q4 | RULE-03 기록 방식 | A 레이드 시작 저장에 반입 장비를 남기고 raidInProgress=true, 다음 실행에 삭제(§13.3 문구 그대로) / B 레이드 시작 저장에서 미리 빼고 플래그는 알림용 | **A** |
| Q5 | 다음 실행 감지 지점 | A 그 실행의 첫 프로필 로드 = 첫 은신처 진입 / B Boot에서 로드 | **A** — 레이드는 은신처를 거쳐야만 시작, Boot에 DB 참조 불필요 |
| Q6 | 강제 종료 알림 | A 은신처 첫 화면 알림 패널(잃은 장비 목록) / B 메인 메뉴 / C 없음 | **A** |
| Q7 | 저장 시점("은신처 진입 시에만" 해석) | A 진입·레이드 시작만 / B 진입 + 은신처를 나갈 때(메뉴·레이드) + 은신처에서 앱 종료 / C B + 거래·키트마다 | **B** (§1.5) |
| Q8 | 탄피 형태 | A 프로필 정수 / B 그리드 아이템 | **A** |
| Q9 | 시작 탄피 | A 6000(M4 고정 키트 수준 1회) / B 0(키트만) / C 1000 | **A**, TODO(GDD-?) |
| Q10 | 가격 규칙 | A 매수 = 올림(base×1.0), 매도 = 내림(base×0.4), 무기는 장착 탄창 포함, 탄 수·사용 횟수 미반영, basePrice 임시표(§4.6) / B 탄 수 비례 | **A** |
| Q11 | 판매 목록 | A SO 고정 목록·재고 무제한(§4.6 ✔: 소총·5.56 탄창·지도·의약품 3·조끼·배낭·헬멧·방탄복) / B basePrice>0 전부 | **A** — 신호탄은 팔지 않음(EXT-03 희소성) |
| Q12 | 매도 범위 | A 보관함 아이템만, 가격 0도 판매(=처분) / B 장비 포함 | **A** |
| Q13 | 키트 지급 조건("신규 및 전멸 시 구제") | A 쿨다운만(새 프로필 즉시) / B 무기가 하나도 없을 때만 + 쿨다운 / C 사망 직후만 | **A** |
| Q14 | 쿨다운 시계 | A 실제 시간 UTC(시계 역행 시 최대 20분) / B 플레이 시간 누적 | **A** |
| Q15 | 키트 받을 공간 부족 | A 전부 들어갈 때만 지급, 아니면 거부·쿨다운 소모 없음 / B 들어가는 것만 | **A** |
| Q16 | 키트 권총 사격 | A M5는 사격 불가 유지(무기 교체·다무기 컨트롤러 = M7), 사격 무기는 상인 소총 / B WeaponController가 Primary1 없으면 Secondary 권총을 쓰게(탄약 정의 조회 추가) | **A** — M3·M4 결정 유지, M7을 앞당기지 않음 |
| Q17 | 키트 지급 위치·상태 | A 보관함, 권총 빈 상태 + 탄창 2개 가득 / B 장비 슬롯에 바로 | **A** |
| Q18 | 체력·상태이상 이월 | A 레이드마다 가득(이월 없음) / B 세이브에 저장 | **A** |
| Q19 | 퀵슬롯 | A Loadout·세이브에 포함(탈출 시 유지), 은신처 배정은 선택 / B 매 레이드 비움 | **A** |
| Q20 | 메인 메뉴 버튼 | A [은신처]/[종료] / B + [새로 시작] | **A** — 초기화는 에디터 메뉴 `Ashfall/Save/Delete Save Files` |
| Q21 | 결과 패널 위치 | A 은신처 진입 시(알림 → 결과 → 허브) / B 메인 메뉴 유지 | **A** (M4 Q15에서 예고) |
| Q22 | 손상 세이브 | A save.corrupt.json으로 옮기고 새 프로필 + 알림 / B .bak 자동 복구(RULE-03 우회 위험) / C 실행 중단 | **A** |
| Q23 | 알 수 없는 아이템 id | A 그 아이템만 버리고 알림(직전 세대는 save.json.bak) / B 원본 레코드 보존 후 다시 씀 | **A** |
| Q24 | 내장 모듈 매니페스트 | A jsonserialize·uielements 명시 + 회귀 테스트 / B 그대로(전이 의존) | **A** |
| Q25 | 흐름 밖 레이드(에디터에서 Raid 씬 직접 Play) 결과 | A 은신처에서 표시만, 프로필 미반영 / B 반영 | **A** |
| Q26 | 빈 장비로 레이드 시작 | A 허용 / B 경고 확인 | **A** |
| Q27 | 은신처에서 버리기·의약품 사용 | A 둘 다 불가 / B 버리기 = 판매 0 | **A** |
| Q28 | 보관함 그리드 종류 | A 기존 `GridKind.Loot`(열린 외부 그리드 자리) 재사용 / B `GridKind.Stash` 추가 + Inventory·UI 분기 | **A** — Inventory·UI 코드 무변경 |
| Q29 | 반입 장비 선택 화면 | A 기존 `InventoryScreenView` 재사용 + 보관함은 루트 패널 자리(세로 스크롤) / B 전용 화면 | **A** |
| Q30 | [레이드 시작] 확인 창 | A 없음 / B "사망 시 반입 장비를 잃는다" 확인 | **A** |
| Q31 | 탈출 ~ 은신처 저장 사이 강제 종료 | A RULE-03 경계로 인정(반입 삭제) / B CompleteRaid에서 즉시 저장(레이드 씬 저장 = RULE-03 문구 위반) | **A** |

---

## 6. 작업 단계

규칙(M3·M4와 같음): **원본 저장소에서 배치모드 Unity는 한 번에 하나**(1단계 끝, 3단계, 4단계, 리뷰 수정 — 순차). 병렬 단계는 1단계 완료 상태를 scratchpad로 복사(`Assets`·`Packages`·`ProjectSettings`·`Library`)한 **프로젝트 사본에서만** 배치모드를 돌리고, 원본에는 **자기 소유 파일만** 쓴다. 각 단계는 끝에 이 문서 맨 아래 "N단계 구현 메모"를 덧붙인다. 새 `.meta`는 만들지 않는다(이동하는 `ItemGrid.cs.meta`는 `git mv`로 옮긴다). 세이브를 쓰는 테스트는 실제 persistentDataPath를 쓰지 않는다(§2.6).

### 1단계 — 계약 (gameplay-programmer, 원본) — 50~60분
- Core 전부(§4.1) **구현 포함**: `ItemGrid` 이동, `Loadout`, `MedicalItem` 생성자, `RaidResult.CarriedOutLoadout`, `ItemDatabaseSO`/`IItemCatalog`, `Save/*` 7개 파일(직렬화·파일 저장소·ProfileService 완성 — 2단계 모두가 기대므로), `GameFlow`·`SceneNames`·`GameManager`, `IHideoutService`, `HideoutChanged`.
- 컴파일 유지용 최소 수정: `UI/MainMenuPresenter.cs`의 `flow.StartRaid()` → `flow.EnterHideout()`(이후 2b 소유), `EditMode/MainMenuPresenterTests.cs` 기대값(이후 2b 소유), `EditMode/M4TestData.cs` `FlowRecorder.Create(ProfileService profiles = null)`.
- 껍데기(직렬화 필드·기본값·읽기 프로퍼티만, 동작 없음): `Hideout/Data/HideoutConfigSO.cs`, `Hideout/Data/TraderConfigSO.cs`, `Hideout/Runtime/HideoutController.cs`(`[DefaultExecutionOrder(-100)]` + 필드 3개), `Inventory/HideoutInventoryBehaviour.cs`(필드 `config`, `stashDisplayName`).
- `Ashfall.EditorTools.asmdef` `+ Ashfall.Hideout`. `Packages/manifest.json` 모듈 2줄 ⟨Q24⟩.
- 테스트 도우미 `Tests/EditMode/M5TestData.cs`: `MemorySaveStorage`(쓰기 실패 토글, 쓴 횟수·마지막 텍스트), `FakeCatalog`(사전, TestData·M3TestData·M4TestData 아이템 등록), `FakeClock`, `Defaults()`(10×30, 6000), `HideoutConfig()`·`TraderConfig()` 빌더, 루트·장비 만들기 도우미, `LoadedProfiles(json = null)`.
- 테스트(새): `LoadoutTests`, `SaveSerializerTests`, `FileSaveStorageTests`, `ProfileServiceTests`, `SaveModuleTests`. 수정: `GameFlowTests`(M5 전이), `ItemGridTests`(using만, 필요 시).
- 마감: 원본 EditMode 전체 통과. PlayMode는 **MainMenu 전환을 기다리는 M4 테스트를 뺀** 나머지 통과(실패 예상 목록을 메모에 적는다: `RaidLoopFlowTests` 3, `ExtractionFlowTests` 중 MainMenu 대기, `RaidEndFreezeTests` 2 — Hideout 씬이 아직 없어 `CompleteRaid`의 로드 요청이 실패한다).

### 2단계 — 병렬 (각자 프로젝트 사본)
| 단계 | 담당 | 소유 파일(원본에 쓰는 것) | 마감 | 예상 |
|---|---|---|---|---|
| **2a** Inventory·Raid | gameplay-programmer | `Inventory/PlayerInventory.cs`, `Inventory/LootContainerModel.cs`, `Inventory/PlayerInventoryBehaviour.cs`, `Inventory/HideoutInventoryBehaviour.cs`(구현), `Raid/Logic/RaidResultBuilder.cs`, `Raid/Runtime/RaidController.cs` / 테스트 새 `EditMode/LoadoutApplyTests.cs`, `EditMode/HideoutInventoryTests.cs`(ForExistingGrid·AllowDrop/Use·rejected → 보관함), 기존 `EditMode/RaidResultBuilderTests.cs`에 추가, 새 `PlayMode/LoadoutInjectionTests.cs`(코드로 만든 씬: 흐름 레이드 표가 없으면 startingItems, `TryGetRaidLoadout`이 true인 이름의 씬이면 Loadout — GameFlow 상태는 공개 API로 만든다) | 사본 EditMode·PlayMode(1단계 예외 목록 제외) 통과 | 40~50분 |
| **2d** Hideout | gameplay-programmer(두 번째 인스턴스) | `Hideout/Data/*.cs`(필드 툴팁·검증만, 필드 이름 불변), `Hideout/Logic/`{`PriceRules`,`StashRules`,`TraderModel`,`VagrantKitModel`,`HideoutService`}.cs, `Hideout/Runtime/HideoutController.cs`(구현) / 테스트 새 `EditMode/PriceRulesTests.cs`, `TraderModelTests.cs`, `VagrantKitTests.cs`, `HideoutServiceTests.cs`(가짜 `IInventoryService`는 기존 `UiTestFakes.FakeInventoryService` 사용) | 사본 EditMode 통과 | 45~55분 |
| **2b** UI | ui-programmer | `UI/MainMenuPresenter.cs`, `UI/MainMenuRoot.cs`, `UI/HideoutPresenter.cs`, `UI/HideoutNoticeViewModel.cs`, `UI/TraderViewModel.cs`, `UI/HideoutRoot.cs`, `UI/HideoutHubView.cs`, `UI/TraderView.cs`, `UI/HideoutNoticeView.cs`, `UI/RaidResultView.cs`(필요 시), `UI/UiText.cs`, `Art/UI/HideoutUI.uxml`, `Art/UI/MainMenu.uxml`, `Art/UI/Ashfall.uss` / 테스트 `EditMode/MainMenuPresenterTests.cs`, 새 `EditMode/HideoutUiModelTests.cs`(가짜 `IHideoutService`는 이 파일 안), `EditMode/RaidUiModelTests.cs`의 `RaidUiUxmlTests`(MainMenu 결과 패널 → HideoutUI.uxml로) | 사본 EditMode 통과 | 55~70분 |
| **2c** 데이터 | data-designer | `EditorTools/M5DataSetup.cs`, 새 `EditMode/M5DataAssetTests.cs`. **원본에 에셋을 만들지 않는다**(사본에서 메뉴 실행) | 사본 메뉴 + 데이터 테스트 통과 | 20~30분 |

- 접점: 2a·2d·2b는 1단계의 Core API와 껍데기 필드 이름(`HideoutController.config/trader/itemDatabase`, `HideoutInventoryBehaviour.config/stashDisplayName`, SO 필드)만 서로 기대한다. 2b는 `HideoutRoot`가 `IHideoutService`·`IInventoryService`를 레지스트리에서 지연 획득한다고 가정한다.
- `TestData.cs`·`M3TestData.cs`·`M4TestData.cs`(1단계 수정 후)·`UiTestFakes.cs`·Core 파일은 2단계에서 아무도 고치지 않는다. 계약 변경은 오케스트레이터 경유.
- 2a 확인 사항: `Loadout.Capture` → `TryApplyLoadout` 왕복이 장착·주머니·조끼·배낭·회전·퀵슬롯을 보존(EditMode). M4 `RaidEndFreezeTests`가 보던 "CarriedOut = 실제 소지품, 중복 없음"이 Loadout 경로에서도 성립.

### 3단계 — 통합 (gameplay-programmer, 원본) — 35~45분
- 새 `EditorTools/M5SceneSetup.cs`, `EditorTools/SaveTools.cs`, `SetupUtil.ApplyBuildScenes()`, `M4SceneSetup.cs`의 빌드 설정 부분.
- 원본에서 `-executeMethod Ashfall.EditorTools.M5SceneSetup.CreateAll` 실행 2회(두 번째: `.meta` GUID 불변, 데이터 에셋·HideoutUI 참조 바이트 동일 확인 — 씬 YAML의 로컬 fileID 차이는 M4처럼 허용).
- 테스트: `EditMode/BuildSettingsTests.cs`(4개·순서), 새 `PlayMode/SaveSandbox.cs`(SetUpFixture) + `PlayMode/SaveSandboxTests.cs`, 새 `PlayMode/HideoutSceneSmokeTests.cs`, M4 PlayMode 수정: `M4TestSupport.cs`(`WaitForHideout` 추가, 메뉴 버튼 문구), `ExtractionFlowTests.cs`·`RaidEndFreezeTests.cs`(종료 후 목적지 Hideout), `RaidSceneSmokeTests.cs`(MainMenu 버튼 문구 "은신처"). `RaidLoopFlowTests.cs` 3개는 `[Ignore("M5 4단계: HideoutLoopFlowTests로 교체")]`.
- 마감: 원본 EditMode 전체, PlayMode 전체(Ignore 3 제외) 통과.

### 4단계 — 검증 (test-engineer, 원본) — 55~70분
- 새 `PlayMode/M5TestSupport.cs`(은신처 UI·서비스 조작, 세이브 파일 읽기, 테스트마다 새 저장 폴더), `PlayMode/HideoutLoopFlowTests.cs`, `PlayMode/CrashRecoveryFlowTests.cs`, `PlayMode/CorruptSaveFlowTests.cs`. `RaidLoopFlowTests.cs` 삭제(범위는 HideoutLoopFlowTests가 대신 — 결과 문구·MIA·일시정지 포기 경로는 새 파일로 옮김).
- Windows 빌드(씬 4개), AC 체크, PlayMode 2회 연속.

### 5단계 — 리뷰 (code-reviewer, 읽기 전용) — 10분
- 의존 방향(Hideout → Core만), 싱글톤 1개, RULE-01 판정 1곳·반영 1곳, RULE-03 기록 지점(레이드 중 쓰기 없음, W4 실패 시 시작 거부), 세이브 경로 주입(테스트에서 실제 경로 미사용), 이벤트·서비스 해지, 수치 하드코딩, JsonUtility 예외 처리, 원자적 쓰기.

### 리뷰 수정 — gameplay/ui — 30~40분
- 지적 수정 + 원본 EditMode·PlayMode(2회)·빌드 재확인.

**예상 총 소요: 약 4.5~5.5시간**(1단계 55 + 병렬 최장 2b 60~70 + 3단계 40 + 4단계 65 + 리뷰 45).

---

## 7. AC별 테스트 계획

### AC1 — 보관함·상인·부랑자 키트
| 종류 | 테스트 |
|---|---|
| EditMode `ItemGridTests`(기존, Core로 이동 후 그대로) | 배치·회전·충돌·자동 칸 |
| EditMode `PriceRulesTests` | 올림·내림·최소 1, basePrice 0 → 판매 안 함(-1)·매도 0, 무기 + 장착 탄창, null |
| EditMode `TraderModelTests` | 매수: 차감·보관함 배치·탄창 가득·무기 빈 상태 / 탄피 부족 → 무변화 / 자리 없음 → 차감 없음 / 목록 밖 → NotForSale. 매도: 보관함 아이템만(장비·없는 아이템 → NotInStash), 가격 0 처분, 제거 후 탄피 증가 |
| EditMode `VagrantKitTests` | 새 프로필 즉시 가능, 지급 4개(권총 빈·탄창 12발 ×2·붕대), 1199초 OnCooldown / 1200초 가능, 시계 역행 → 남은 ≤ 1200, 자리 부족 → NoSpace·아무것도 안 놓임·쿨다운 그대로, 남은 시간 분 올림 문구(UI 모델) |
| EditMode `HideoutInventoryTests`·`LoadoutApplyTests` | 보관함을 열린 그리드로: 장비 ↔ 보관함 TryMove·TryEquip·Ctrl 빠른 이동, 버리기·사용 거부, 퀵슬롯 |
| EditMode `HideoutUiModelTests` | 패널 순서(알림 → 결과 → 허브), 탭, 키트 문구, 거래 실패 메시지, 떠나는 중 버튼 비활성, 판매 목록 줄·매도 줄, UXML 이름 계약(HideoutUI.uxml에 인벤토리·결과·은신처 요소) |
| EditMode `M5DataAssetTests`(2c) | HideoutConfig 10×30·1200초·키트 = PST-01 + MAG-9MM-12 ×2 + BND-01, TraderConfig 목록 전부 가격 > 0·지도 포함·신호탄 없음, sell ≤ buy, §4.6 가격 표, ItemDatabase = 프로젝트의 모든 ItemDefinitionSO·id 유일·id 순, 메뉴 재실행 바이트 동일 |
| PlayMode `HideoutSceneSmokeTests`(3단계) | Hideout 이름 로드 → Phase Hideout, IHideoutService·IInventoryService 등록, ActiveLoot = 10×30, 허브 표시, 저장 파일이 샌드박스에 생김, 콘솔 오류 없음 |
| 사람 | 실제 마우스로 장비 ↔ 보관함 드래그·회전·스크롤, 상인 구매·판매 버튼, 키트 수령, 문구 가독성 |

### AC2 — 세이브/로드, 강제 종료(§13.3, RULE-03)
| 종류 | 테스트 |
|---|---|
| EditMode `LoadoutTests` | Capture 순서(장착 → 주머니 → 조끼 → 배낭) = `InventorySnapshot.Collect`, 위치·회전, 루트 제외, 무기 장착 탄창 미중복, 퀵슬롯은 Entries 안만, null → Empty |
| EditMode `SaveSerializerTests` | 왕복: 모든 아이템 종류(탄창 탄 수, 무기 + 장착 탄창 탄 수, 의약품 남은 횟수, 조끼·배낭 내용물, 주머니 번호, 회전, 퀵슬롯, 탄피, 키트 시각, raidInProgress). 고정 JSON 문자열(§2.2 예) 파싱 = 스키마 안정성. 알 수 없는 id·탄창 id, 잘못된 container·slot, 보관함 범위 밖·겹침 → 자동 배치/버림 개수, 음수 탄피, version 0·없음 → Corrupt, version 2 → UnsupportedVersion, 쓰레기·빈 문자열·잘린 JSON → 예외 없이 Corrupt |
| EditMode `FileSaveStorageTests`(OS temp 폴더) | 첫 쓰기 = 파일 생성·tmp 없음, 두 번째 쓰기 = .bak에 이전 내용, 남은 .tmp 무시, 없음 → Missing, Quarantine → save.corrupt.json, 읽기 전용 폴더 등 실패 → false·예외 없음 |
| EditMode `ProfileServiceTests`(MemorySaveStorage) | 새 프로필 기본값, EnsureLoaded 한 번만 읽음, **raidInProgress=true 파일 → Loadout Empty·CrashLost 목록·보관함·탄피 유지·알림**, BeginRaid → 쓴 텍스트에 loadout 그대로 + raidInProgress true, 메모리 Loadout Empty / 쓰기 실패 → false·플래그 되돌림, CancelRaid, ApplyRaidResult 4종(탈출만 CarriedOutLoadout), ApplyInterruptedRaid, 손상 → Quarantine 호출·새 프로필, UseStorage 후 다시 읽음 |
| EditMode `GameFlowTests` | Hideout 단계, EnterHideout/LeaveHideout 허용·거부, StartRaid는 Hideout에서만 + BeginRaid 1회 + 실패·로드 실패 시 CancelRaid, CompleteRaid → "Hideout" 로드·정지 유지, TakeRaidReturn Finished 1회·Interrupted·None, TryGetRaidLoadout 씬 이름·완료 후 false, M4 기존 규칙(두 번 호출 무시 등) 유지 |
| EditMode `SaveModuleTests` | 매니페스트 jsonserialize·uielements, JsonUtility 왕복, UIDocument 형식 로드 |
| PlayMode `SaveSandboxTests`(3단계) | 저장 위치가 persistentDataPath 아래가 아님 |
| PlayMode `CrashRecoveryFlowTests`(4단계) | 은신처에서 장비 구성(키트·지도) → [레이드 시작] → **save.json에 raidInProgress=true + loadout 레코드** 확인 → 레이드 씬에서 `UseSaveStorage(같은 폴더의 새 FileSaveStorage)`(= 새 실행) → Hideout 이름 로드 → 알림 패널 "강제 종료" + 잃은 목록 = 반입, 장비 비어 있음, 보관함·탄피 그대로, save.json raidInProgress=false·loadout 빈 배열 |
| PlayMode `CorruptSaveFlowTests`(4단계) | 샌드박스 save.json에 쓰레기 → 은신처 진입 → 알림 "손상", save.corrupt.json 존재, 새 save.json 파싱 성공·시작 탄피. version 99 파일도 같은 경로(문구만 다름) |
| 사람 | Windows 빌드에서: 은신처 정리 → 메뉴 → 종료 → 재실행 → 유지 / 레이드 중 Alt+F4 → 재실행 → 은신처 알림·반입 소실 / `%USERPROFILE%\AppData\LocalLow\DefaultCompany\Ashfall\save.json` 내용 확인 |

### AC3 — 은신처 ↔ 레이드 전체 루프 1회 완주
| 종류 | 테스트 |
|---|---|
| EditMode `HideoutServiceTests` | Enter: 첫 로드 → Finished 반영 → 저장 1회 / Interrupted / None + PendingResult(표시만, 미반영) / StartRaid: Capture가 profile.Loadout으로 가고 flow.StartRaid 인자와 같음, 실패 시 IsLeaving 해제 / ReturnToMainMenu: 저장 → LeaveHideout / 떠나는 중 SaveNow 무시 / 이벤트 HideoutChanged |
| EditMode `RaidResultBuilderTests`(추가) | Loadout 오버로드: 탈출 → CarriedOutLoadout = 종료 Loadout(위치 그대로)·CarriedOut 순서 = M4, 사망·MIA·포기 → CarriedOutLoadout null·Lost = 전부 |
| PlayMode `LoadoutInjectionTests`(2a) | 흐름 레이드 표 없음 → startingItems / 있음 + 레이드 씬 이름 → Loadout 그대로(위치·회전·탄 수·퀵슬롯), startingItems 무시 |
| PlayMode `HideoutLoopFlowTests`(4단계) | ① Boot → MainMenu → [은신처] → (새 프로필) 탄피 6000 → [부랑자 키트 받기] → 보관함에 4개 → 상인에서 지도·소총·5.56 탄창 구매 → 장비로 옮김(서비스 명령, 드래그 경로는 사람 확인) → [레이드 시작] → save.json raidInProgress=true → 레이드 인벤토리 = 그 장비(startingItems 아님) → 상자 아이템 획득 → EXT-01 탈출 → Hideout: 결과 패널 "탈출 성공 — 남쪽 도로", [확인] → 장비 = 반출품(획득품 포함, 위치 그대로), save.json raidInProgress=false·loadout에 획득품, 보관함은 레이드 전과 같음. ② 다시 [레이드 시작] → 머리 사망 → Hideout: "사망 — 머리", 장비 비어 있음, 보관함 그대로, save.json loadout 빈 배열. ③ MIA·일시정지 [레이드 포기]도 같은 반영(M4 RaidLoopFlowTests 범위 이전). ④ [메인 메뉴] → 저장 → 다시 [은신처] → 같은 상태(같은 실행 재진입은 다시 읽지 않음), 이벤트 구독 수 누수 없음 |
| PlayMode 회귀(3단계 수정) | `ExtractionFlowTests`·`RaidEndFreezeTests`: 종료 후 Hideout, 결과는 표시만(흐름 밖), `RaidSceneSmokeTests` MainMenu 부분 |
| 빌드 | Windows64 성공, 씬 4개 순서 |
| 사람 | 에디터에서 Boot부터 루프 1회(키트 → 구매 → 장비 → 레이드 → 탈출 → 보관함 정리 → 판매 → 다시 레이드 → 사망), 실제 마우스 클릭(메뉴·허브·상인·결과·알림 [확인]), 커서, 에디터에서 레이드 중 Play 중지 = 다음 Play의 은신처에서 강제 종료 알림(의도된 동작) |

### 회귀
- 기존 EditMode 715 · PlayMode 66: 위에 적은 수정·이전(GameFlowTests, MainMenuPresenterTests, RaidUiUxmlTests 일부, BuildSettingsTests, ExtractionFlowTests·RaidEndFreezeTests·RaidSceneSmokeTests 목적지·문구, RaidLoopFlowTests → HideoutLoopFlowTests) 외에는 그대로 통과. M4 리뷰 수정(종료 잠금·정지 시점·try/finally·CanConsume)은 코드 무변경이고 RaidEndFreezeTests가 계속 확인한다.
- 확인 씬(M1~M3 Test 씬)은 startingItems 그대로(흐름 레이드 표가 없음) — `M2SceneSmokeTests`·`M3SceneSmokeTests` 무변경 통과.

---

## 8. CHANGELOG_GDD.md "M5 설계 결정" 초안

```markdown
## 2026-10-?? — M5 설계 결정 (§2, §6, §9, §12, §13.2, §13.3, §14, §15 M5, §18, RULE-01, RULE-03)
문서 본문은 바꾸지 않았다. 아래는 M5 구현을 위한 해석이며, 기획 확정 시 문서에 반영하거나 되돌린다.
사용자 지시로 GDD에 없거나 모호한 31개 항목(설계 문서 `docs/design/M5_design.md` §5의 Q1~Q31)은 가장 단순한 추천안으로 정했다.
코드의 결정 지점에는 `TODO(GDD-?) Qn` 주석이 있다.
- **§13.2·§13.3 세이브·프로필:** Core에 `ProfileService`(GameManager 소유)와 `PlayerProfile`(보관함·은신처 장비·탄피·키트 시각·raidInProgress)을 둔다.
  세이브는 JsonUtility로 `persistentDataPath/save.json`(version 1, 평평한 아이템 레코드, enum은 이름 문자열), 임시 파일 → `File.Replace`(직전 세대 .bak).
  §13.3의 `raid_in_progress`는 JSON 키 `raidInProgress`. 아이템 정의는 새 `ItemDatabaseSO`(Data/Items/ItemDatabase.asset, 모든 아이템 정의)로 id → SO를 찾는다.
  손상·지원하지 않는 버전은 save.corrupt.json으로 옮기고 새 프로필(Q22), 알 수 없는 id는 그 아이템만 버림(Q23). .bak 자동 복구는 RULE-03 우회 위험으로 하지 않는다.
  내장 모듈 jsonserialize·uielements를 매니페스트에 명시했다(Q24).
- **§13.3 ItemGrid 위치:** `ItemGrid`를 Inventory에서 Core로 옮겼다(파일·.meta 이동, GUID 유지). 사유: 상인·키트(Hideout)가 보관함 배치를 판정해야 하는데 기능끼리 참조가 금지돼 있다(Q2, M3 무기 로직 이동과 같은 사유).
- **§9·§18 반입(Loadout):** 반입 = 장착 슬롯 + 저장 공간 배치 + 퀵슬롯(Core `Loadout`). 은신처에는 "은신처 장비"가 유지되고 그것이 다음 반입이 된다(Q3, Q19).
  레이드는 흐름으로 시작했을 때만 `startingItems` 대신 반입으로 시작한다(확인 씬은 그대로).
- **RULE-01:** 반입은 레이드 시작 때 은신처 장비에서 빠지고, 탈출 시 종료 시점 소지품이 위치 그대로 은신처 장비로 돌아온다(`RaidResult.CarriedOutLoadout`, 판정은 계속 `RaidResultBuilder` 하나, 반영은 `ProfileService.ApplyRaidResult` 하나).
  사망·MIA·포기면 은신처 장비가 비고 보관함은 바뀌지 않는다. 흐름 밖(에디터에서 레이드 씬 직접 Play) 결과는 표시만 한다(Q25).
- **RULE-03·§13.3 저장 시점:** 레이드 중에는 아무것도 쓰지 않는다. 저장은 은신처 진입, 은신처를 나갈 때(메인 메뉴·레이드 시작), 은신처에서 앱 종료(Q7).
  레이드 시작 저장은 반입 장비를 그대로 두고 raidInProgress=true를 기록한다(Q4). 이 저장이 실패하면 레이드를 시작하지 않는다.
  다음 실행의 첫 은신처 진입에서 true면 은신처 장비를 지우고(보관함·탄피 유지) 알림 패널로 잃은 목록을 보여 준다(Q5, Q6).
  탈출 순간 ~ 은신처 진입 저장 사이의 강제 종료도 반입 삭제로 처리된다(Q31). 에디터에서 레이드 중 Play 중지도 강제 종료다.
- **§9 보관함:** 10×30(W×H) 고정, 업그레이드 없음(Q1). 은신처에서는 보관함을 인벤토리의 열린 외부 그리드 자리로 열어 기존 인벤토리 화면으로 장비 ↔ 보관함을 옮긴다(Q28, Q29). 은신처에서 버리기·의약품 사용 불가(Q27).
- **§9 상인:** 탄피는 프로필 정수(Q8), 시작 6000(Q9). 매수 = 올림(basePrice × 1.0), 매도 = 내림(basePrice × 0.4), 무기는 장착 탄창 포함, 탄 수·사용 횟수 미반영(Q10).
  판매 목록 고정·재고 무제한(소총·5.56 탄창·지도·붕대·지혈대·부목·조끼·배낭·헬멧·방탄복, 신호탄 제외)(Q11). 매도는 보관함 아이템만, 가격 0도 처분으로 판매(Q12).
- **§9 부랑자 키트:** 녹슨 권총(탄창 없음) + 권총 탄창 2개(가득) + 붕대를 보관함에 지급. 조건은 쿨다운만(새 프로필 즉시), 실제 시간 UTC 20분, 시계 역행 시 최대 20분(Q13, Q14, Q17).
  보관함에 전부 들어갈 때만 지급(Q15). 권총은 M7(무기 교체) 전까지 플레이어가 쏠 수 없다(Q16, M3 결정 유지).
- **§12 화면:** 은신처(메뉴형): 알림 → 레이드 결과(M4 결과 패널 이동, Q21) → 허브([장비]/[상인] 탭, 부랑자 키트, 레이드 시작, 메인 메뉴). 메인 메뉴는 [은신처]/[종료](Q20).
  레이드 시작 확인 없음·빈 장비 허용(Q26, Q30). 체력은 이월하지 않는다(Q18).
- **§13.2 씬:** `Hideout` 씬 추가, 빌드 순서 Boot → MainMenu → Hideout → Raid_CrimsonBasin. `Ashfall/Setup/Create M5 Hideout Scene`이 씬과 빌드 설정을 만든다.
  세이브 폴더 열기·삭제는 에디터 메뉴 `Ashfall/Save/...`.
- **§14 스키마에 없는 추가:** `HideoutConfigSO`(stashSize, startingCasings, vagrantKitCooldownSec, vagrantKitItems), `TraderConfigSO`(id, displayName, 매수·매도 배율, catalog), `ItemDatabaseSO`.
- **GDD에 수치가 없어 임시값을 넣은 항목 (TODO(GDD-?)):** 모든 아이템 basePrice(설계 §4.6 표), 매수·매도 배율 1.0/0.4, 시작 탄피 6000, 상인 이름 "고물상", 판매 목록.
```

---

## 9. 1단계 계약 파일 목록 (요약)

원본에 1단계가 만들거나 고치는 파일(이후 계약 변경은 오케스트레이터 경유):
- 이동: `Assets/_Project/Scripts/Inventory/ItemGrid.cs`(+`.meta`) → `Assets/_Project/Scripts/Core/Items/ItemGrid.cs`(+`.meta`)
- Core 새: `Core/Items/Loadout.cs`, `Core/Data/ItemDatabaseSO.cs`, `Core/Save/SaveData.cs`, `Core/Save/SaveSerializer.cs`, `Core/Save/ISaveStorage.cs`, `Core/Save/FileSaveStorage.cs`, `Core/Save/PlayerProfile.cs`, `Core/Save/SaveLoadReport.cs`, `Core/Save/ProfileService.cs`, `Core/Services/IHideoutService.cs`
- Core 수정: `Core/Items/MedicalItem.cs`, `Core/Raid/RaidResult.cs`, `Core/Flow/GameFlow.cs`, `Core/SceneNames.cs`, `Core/GameManager.cs`, `Core/Events/GameEvents.cs`, `Core/Services/ServiceRegistry.cs`(주석), `Core/Items/InventorySnapshot.cs`(주석)
- 껍데기: `Hideout/Data/HideoutConfigSO.cs`, `Hideout/Data/TraderConfigSO.cs`, `Hideout/Runtime/HideoutController.cs`, `Inventory/HideoutInventoryBehaviour.cs`
- 컴파일 유지: `UI/MainMenuPresenter.cs`(EnterHideout), `Tests/EditMode/MainMenuPresenterTests.cs`, `Tests/EditMode/M4TestData.cs`(FlowRecorder)
- 설정: `Scripts/EditorTools/Ashfall.EditorTools.asmdef`(+Hideout), `Packages/manifest.json`(jsonserialize·uielements)
- 테스트: 새 `Tests/EditMode/M5TestData.cs`, `LoadoutTests.cs`, `SaveSerializerTests.cs`, `FileSaveStorageTests.cs`, `ProfileServiceTests.cs`, `SaveModuleTests.cs` / 수정 `GameFlowTests.cs`(+ `ItemGridTests.cs` using 필요 시)

---

## 질문 결정 (오케스트레이터, 2026-10-22)
- Q1~Q31 모두 ★추천안 채택(사용자 지시: GDD에 없거나 모호한 것은 가장 단순한 쪽으로 정하고 `TODO(GDD-?)`와 CHANGELOG에 기록).
- 진행: §6 단계 그대로. 1단계 원본 → 2a/2b/2c/2d 병렬(프로젝트 사본) → 3단계 통합 → 4단계 검증 → 리뷰.

---

## 1단계 구현 메모 (gameplay-programmer, 2026-10-22)
결과: 원본 EditMode 825/825 통과(기존 715 + 새 110: LoadoutTests 15, SaveSerializerTests 31, FileSaveStorageTests 12, ProfileServiceTests 32, SaveModuleTests 4, GameFlowTests 20 → 36). PlayMode 59/66 통과(실패 7, 아래 목록). 커밋 안 함. CHANGELOG_GDD는 건드리지 않음.
실제 persistentDataPath에 save.json이 생기지 않았음을 확인(EditMode는 메모리·OS 임시 폴더만, PlayMode는 은신처 씬이 없어 쓰기 없음).

바뀐/새 파일
- 이동: `Inventory/ItemGrid.cs`(+`.meta`) → `Core/Items/ItemGrid.cs`(`git mv`, GUID 유지). 네임스페이스만 `Ashfall.Core`. `ItemGridTests`는 using 변경 불필요(이미 `Ashfall.Core` 사용).
- Core 새: `Items/Loadout.cs`, `Data/ItemDatabaseSO.cs`, `Save/{SaveData,ISaveStorage,FileSaveStorage,PlayerProfile,SaveLoadReport,SaveSerializer,ProfileService}.cs`, `Services/IHideoutService.cs`.
- Core 수정: `Items/MedicalItem.cs`, `Raid/RaidResult.cs`, `Flow/GameFlow.cs`, `SceneNames.cs`, `GameManager.cs`, `Events/GameEvents.cs`(HideoutChanged), `Services/ServiceRegistry.cs`·`Items/InventorySnapshot.cs`(주석).
- 껍데기: `Hideout/Data/HideoutConfigSO.cs`, `Hideout/Data/TraderConfigSO.cs`, `Hideout/Runtime/HideoutController.cs`, `Inventory/HideoutInventoryBehaviour.cs`.
- 컴파일 유지: `UI/MainMenuPresenter.cs`(`StartRaid()` 안에서 `flow.EnterHideout()`), `EditMode/MainMenuPresenterTests.cs`(기대 로드 = Hideout, 결과는 흐름 밖 레이드로 만든다), `EditMode/M4TestData.cs`(`FlowRecorder.Create(ProfileService profiles = null)`).
- 설정: `EditorTools.asmdef` +`Ashfall.Hideout`, `Packages/manifest.json` +jsonserialize·uielements(Unity가 `packages-lock.json`의 depth를 0으로 갱신 — 같이 커밋).
- 테스트 새: `EditMode/M5TestData.cs`, `LoadoutTests.cs`, `SaveSerializerTests.cs`, `FileSaveStorageTests.cs`, `ProfileServiceTests.cs`, `SaveModuleTests.cs`. 수정: `GameFlowTests.cs`(전면 M5 전이).

설계와 다르거나 설계에 없던 결정(API는 §4.1 그대로, 추가만)
- `GameFlow.StartRaid(Loadout)`: Hideout 단계 + 로드 중 아님 + **흐름 레이드 표가 비어 있을 때만**(이전 레이드를 `TakeRaidReturn`으로 꺼내기 전엔 거부 — 결과 반영을 건너뛰지 않게). 성공 시 `raidCompleted = false`로 리셋(두 번째 레이드 씬 Awake에서 `TryGetRaidLoadout`이 이전 완료 표시에 막히지 않게). null Loadout → Empty.
- `GameFlow.TakeRaidReturn`·`TryGetRaidLoadout`은 Phase를 보지 않는다(새 씬 Awake 시점엔 Phase가 이전 단계). `TryGetRaidLoadout`은 완료 전이면 여러 번 true(같은 인스턴스).
- `GameFlow.EnterHideout`: MainMenu·None에서, 어떤 씬이든 로드 중이면 거부. `LeaveHideout`도 로드 중 거부. 둘 다 `CompleteRaid`와 같은 `Load` 경로(실패 시 LogError, 재시도 가능).
- `ProfileService`
  - `EnsureLoaded` 두 번째 호출은 **첫 로드 보고서 객체를 그대로** 돌려준다. 알림을 보일지는 `PendingNotice`(확인 후 null)로 판단할 것.
  - 읽기 오류(`SaveReadStatus.Error`)는 Corrupt로 취급해 격리 시도 + 새 프로필(TODO(GDD-?) Q22). 격리 실패는 LogError, 진행은 계속(`report.Error`에 이어 붙임).
  - `BeginRaid`: 이미 RaidInProgress면 false(경고). 성공 시 맡은 반입을 내부에 기억 → `ApplyInterruptedRaid`가 `CrashLost`를 채우고, `CancelRaid(null)`은 그것으로 되돌린다.
  - `ApplyInterruptedRaid`: 이미 `PendingNotice`가 있으면 거기에 `CrashPenaltyApplied`·목록을 합친다(없으면 새 보고서, Status는 첫 로드 상태).
  - `ApplyRaidResult(null)` → 경고 + `ApplyInterruptedRaid`.
  - `Save()`는 미로드면 LogError + false. 실패 시 LogError `"[ProfileService] 세이브 실패 (...)"`(테스트는 `LogAssert.Expect(Error, "세이브 실패")`).
- `SaveSerializer`: 공개 `ToData(profile, utcNow)` 추가(ToJson이 씀). 보관함은 2단계 배치(기록된 자리 전부 → 실패분 자동 배치). 은신처 장비 레코드의 container·slot을 해석 못 하면(모르는 이름, "Stash", 숫자 문자열 "0", 대소문자 다름) 보관함 자동 배치 → 안 되면 Dropped. **단 파일이 raidInProgress면 보관함으로 구하지 않고 `report.CrashLost`에 넣는다**(RULE-03 우회 방지). 해석은 되지만 자리가 틀린 것(주머니 9번, 범위 밖 칸)은 Loadout에 남긴다 → 2a의 `TryApplyLoadout`이 거부·보관함으로. 무기의 탄창 id가 탄창 정의가 아니면 알 수 없는 id로 셈.
- `FileSaveStorage`: 추가 프로퍼티 `DirectoryPath`. 모든 예외를 잡아 false/Error + 문자열.
- `Loadout` 생성자: null 아이템·Loot 자리 항목도 버린다. 퀵슬롯 입력이 4칸 넘으면 자른다.
- `PlayerProfile`: stashSize 각 축 최소 1, 생성자 casings도 음수 → 0.
- `HideoutConfigSO.ToProfileDefaults()` 편의 메서드 추가(필드 이름은 설계 그대로).
- `HideoutController` 껍데기는 `Config`·`Trader`·`ItemDatabase` 읽기 프로퍼티만 있고 **`Service` 프로퍼티는 없다**(HideoutService 타입이 아직 없음 — 2d가 추가). `HideoutInventoryBehaviour`는 `Config`·`StashDisplayName`.
- `MainMenuPresenter`는 결과 패널이 아직 있다(메서드 이름 `StartRaid`도 그대로). 정리는 2b.

2단계가 알 것
- `M5TestData`: `Defaults()`(10×30, 6000), `Epoch`(2026-10-22 09:30 UTC), `HideoutConfig(catalog)`(키트 = WPN-PST-01 + MAG-9MM-12 ×2 + MED-BND-01, 1200초), `TraderConfig(catalog)`(§4.6 ✔ 10종, 1.0/0.4),
  `LoadedProfiles(json, storage, catalog, clock)`(모두 선택 인자 — 넘기면 같은 인스턴스를 씀), `Rifle(catalog, loadedRounds = -1)`, `Mag556`, `Bandage`, `Plain(catalog, id)`,
  `EquippedInventory(catalog, out items)`(실제 `PlayerInventory`: 소총 17발·헬멧·조끼·배낭 장착, 붕대 주머니 2 + 퀵슬롯 0, 조끼 5.56 30발 (0,0)·12발 (1,0) 회전, 배낭 전선 (2,3); 키 rifle/helmet/vest/backpack/bandage/magFull/magPart/wires),
  `SampleLoadout(catalog, out items)`(소총 Primary1·조끼·조끼 안 탄창 (0,1) 회전·주머니 1 붕대 + 퀵슬롯 0), `Loot(items)`, `Extracted(loadout)`.
  `FakeCatalog.Standard()`는 TestData·M3TestData·M4TestData 아이템을 등록하고 **§4.6 basePrice를 정의에 써 넣는다**(카탈로그마다 새 SO). `Get(id)`(없으면 예외), `Add(def, price)`.
  `MemorySaveStorage`(Text = 파일 내용, `WriteFails`/`ReadFails`/`QuarantineFails`, `WriteCount`(성공)·`WriteAttempts`·`LastWrittenText`·`LastWritten()` DTO·`QuarantineCount`), `FakeClock`(`UtcNow`, `Now`, `Advance(sec)`).
- 은신처 W1 순서(2d): `EnsureLoaded(itemDatabase, config.ToProfileDefaults())` → `TakeRaidReturn` → Finished면 `ApplyRaidResult`, Interrupted면 `ApplyInterruptedRaid`, None이면 PendingResult는 표시만 → `Save()`.
- 보관함 그리드는 `profile.Stash`(`GridRef(GridKind.Loot)`). `IHideoutService.Stash`는 그것을 `IGridView`로.
- 2a: `PlayerInventory.QuickSlotCount = Loadout.QuickSlotCount`로 바꾸는 것은 2a 몫(지금은 둘 다 4). `HideoutInventoryBehaviour`의 직렬화 필드 이름 `config`·`stashDisplayName`.
- PlayMode 세이브 샌드박스(3단계)는 `GameManager.Instance.UseSaveStorage(new FileSaveStorage(<temp>))`. GameManager Awake는 경로만 만들고 읽지 않는다.

PlayMode 실패 목록(7, 모두 Hideout 씬이 아직 없어 `CompleteRaid`의 Hideout 로드 요청이 실패 → "Scene 'Hideout' couldn't be loaded" 오류 로그)
- 예상 6: `RaidLoopFlowTests` 3개(`BootToMenu_DeathThenExtraction_TwoLoops_…`, `MenuToRaid_EscPauseFreezesWorld_…`, `MenuToRaid_MissingInAction_…`), `ExtractionFlowTests.Ext01_Waits10Seconds_LeavingZoneResets_ThenExtractsToMainMenu`, `RaidEndFreezeTests` 2개(`MedicalUseDuringExtraction_…`, `ReloadDuringExtraction_…`).
- 연쇄 1: `InventoryFlowTests.SceneUnload_LeavesNoSubscribersOrServices` — "ExtractionStatusChanged 구독 누수". 앞의 `ExtractionFlowTests`가 Hideout으로 못 넘어가 레이드 씬(GameUIPresenter 구독)이 다음 픽스처까지 남은 탓. 코드 누수 아님 — Hideout 씬이 생기면(3단계) 사라져야 한다. 3단계에서 다시 확인할 것.

---

## 2c 구현 메모 (data-designer, 2026-10-22)
결과: 사본(proj_2c)에서 `M5DataSetup.CreateAll` 2회 실행 — 2회째 `Data/` 전체(에셋·.meta) 바이트 동일, 1회째도 기존 .meta GUID 불변(아이템 18개는 `basePrice` 한 줄만 바뀜). 사본 EditMode 842 중 838 통과, **실패 4 = 기존 데이터 테스트의 "가격 0" 단언**(아래). 그 4줄을 사본에서만 고치면 842/842 통과. 원본에는 에셋을 만들지 않았다. 커밋·CHANGELOG 안 함.

파일(원본): 새 `EditorTools/M5DataSetup.cs`, 새 `Tests/EditMode/M5DataAssetTests.cs`(17개).

`M5DataSetup` (메뉴 `Ashfall/Setup/Create M5 Data`, 배치 `CreateAll` = `CreateData`)
- 순서: `M4DataSetup.CreateData()` → 가격 → HideoutConfig → TraderConfig → SaveAssets → ItemDatabase → SaveAssets.
- 가격: §4.6 표 값을 **`basePrice`가 0일 때만** 기록(오케스트레이터 지시 "0인 아이템에 임시값" — 조정한 값 보존, 설계 §4.6의 "매번 기록"과 결과는 같다). 탄약 정의(AMMO-*)는 0 그대로. 경로의 id가 다르면 LogError·건너뜀.
- `HideoutConfig`·`TraderConfig`: 없을 때만 생성(M2~M4 방식). 이미 있는데 `vagrantKitItems`/`catalog`가 비어 있으면 채운다.
- `ItemDatabase`: `Assets/` 아래 ScriptableObject 전부를 보고 `ItemDefinitionSO`(파생 포함)만 모아 id 서수 순으로 매번 다시 채움(같으면 SetDirty 안 함, GUID 유지). 빈 id·중복 id면 `InvalidOperationException`(DB 변경 전에 검사). 공개 `CollectItemDefinitions()`.
- 공개 상수: `HideoutFolder`, `HideoutConfigPath`, `TraderConfigPath`, `ItemDatabasePath`, `ItemSearchFolder`, 아이템 경로들.

만들어지는 에셋: `Assets/_Project/Data/Hideout/HideoutConfig.asset`, `Assets/_Project/Data/Hideout/TraderConfig.asset`, `Assets/_Project/Data/Items/ItemDatabase.asset`(20개 = 아이템 18 + AMMO 2), 폴더 `Data/Hideout`. 기존 아이템 18개 `basePrice` 갱신.

값 (전부 TODO(GDD-?), 설계 §4.6·Q9~Q17 그대로)
- 가격: RIF 4000, SMG 1500, PST 400, MAG-556-30 400, MAG-9MM-30 300, MAG-9MM-12 120, MAP 100, FLR 500, BND 60, TRQ 150, SPL 120, RIG 600, BAG 800, HLM 1200, BDY 2500, JNK-01/02/03 40/300/80, AMMO-556/9MM 0.
- HideoutConfig: 10×30, 시작 탄피 6000, 쿨다운 1200초, 키트 = WPN-PST-01, MAG-9MM-12, MAG-9MM-12, MED-BND-01.
- TraderConfig: TRD-01 "고물상", 매수 1.0 / 매도 0.4, 목록 = RIF, MAG-556-30, MAP, BND, TRQ, SPL, RIG, BAG, HLM, BDY(신호탄 없음).
- 값은 `M5TestData.HideoutConfig/TraderConfig`·`FakeCatalog.Standard()` 가격과 같다(테스트로 확인).

3단계가 할 것
- `M5SceneSetup.CreateAll`이 먼저 `M5DataSetup.CreateData()`를 부른 뒤 `M5DataSetup.HideoutConfigPath`·`TraderConfigPath`·`ItemDatabasePath`를 `HideoutController`의 `config`·`trader`·`itemDatabase`에 연결.
- **기존 테스트 4줄 갱신 필요**(2c 소유 아님): M5 가격 기록 후 `basePrice == 0` 단언이 실패한다.
  `M3DataAssetTests.Pistol_MatchesGddAndDesign`(100행 `0` → `400`), `Smg_MatchesGddAndDesign`(123행 `0` → `1500`),
  `M4DataAssetTests.Flare_MatchesDesign`(185행 `0` → `500`), `MapItem_MatchesDesign`(194행 `0` → `100`). 이 4개는 원본에 M5 메뉴를 실행한 뒤부터 실패한다.
- 메모: 사본 로그에 VisualScripting `YamlDotNet.dll` DirectoryNotFound 예외 1건(사본 경로 길이 문제로 보임, 원본과 무관, 종료 코드 0).

---

## 2d 구현 메모 (gameplay-programmer, 2026-10-22)
결과: 사본(`proj_2d`, 1단계 상태 + 2d 파일) EditMode 897/897 통과(1단계 825 + 새 72: PriceRulesTests 11, TraderModelTests 19, VagrantKitTests 11, HideoutServiceTests 31). 커밋·CHANGELOG 안 함. Core·테스트 도우미 무변경. 테스트는 MemorySaveStorage만 쓴다(실제 persistentDataPath 쓰기 없음).

바뀐/새 파일 (원본)
- 수정: `Hideout/Data/HideoutConfigSO.cs`·`TraderConfigSO.cs` — 툴팁 보강, `[Min]`, `OnValidate`(stashSize 각 축 ≥ 1, 시작 탄피·쿨다운 ≥ 0, 매수 배율 ≥ 0, 매도 배율은 [0, 매수 배율]로 자름). 필드 이름·기본값 그대로.
- 새: `Hideout/Logic/{PriceRules,StashRules,TraderModel,VagrantKitModel,HideoutService}.cs`. 구현: `Hideout/Runtime/HideoutController.cs`.
- 테스트 새: `EditMode/{PriceRulesTests,TraderModelTests,VagrantKitTests,HideoutServiceTests}.cs`.

설계(§4.2)와 다르거나 설계에 없던 결정 (API는 설계 그대로, 추가만)
- `PriceRules`: float 배율 오차(1.1f × 100 → 111) 막으려고 올림·내림 전에 1e-3을 정수 쪽으로 붙인다. 추가 오버로드 `SellPrice(ItemDefinitionSO, trader)`(장착 탄창 제외, basePrice ≤ 0 → 0).
- `TraderModel.Offers`는 생성 때 한 번 만든다(catalog 순, null·가격 ≤ 0·중복 제외). `TryBuy` 순서 = 목록 확인 → 탄피 → 보관함 배치 → 차감(둘 다 안 되면 NotEnoughCasings). `TrySell`은 보관함에서 빼고 매도가를 더한다(int 넘침은 최댓값으로 자름).
- `StashRules.TryPlaceAll`: 앞에서부터 탐욕 배치, 실패 시 이번에 놓은 것만 되돌림, null 항목은 건너뜀(TODO(GDD-?) Q15 — 다른 배치로는 들어가는 경우에도 실패할 수 있다).
- `VagrantKitModel`: 지급 목록이 비었거나 전부 null이면 `Unavailable`(설정 오류). 쿨다운 0이면 항상 가능. 추가 프로퍼티 `CooldownSec`.
- `HideoutService.Enter`: 반환값 = `EnsureLoaded` 보고서(같은 실행 재진입이면 첫 로드 보고서). 알림 표시는 `PendingNotice`로 판단할 것. `TakeRaidReturn`이 None인데 메모리에 `RaidInProgress`가 남아 있으면(정상 흐름엔 없음) `ApplyInterruptedRaid`로 처리. None + `PendingResult`는 경고 로그만(Q25). 저장 실패는 진입을 막지 않는다(`LastSaveFailed`).
- `HideoutService` 생성자는 프로필이 로드돼 있어야 한다(아니면 `InvalidOperationException`). `loadoutSource`·`publishChanged`는 null 허용.
- **은신처 인벤토리(IInventoryService)가 없으면 Capture하지 않고 `profile.Loadout`을 그대로 쓴다**(SaveNow·StartRaid). 인벤토리가 장비를 놓지도 않았는데 비워서 잃지 않게(TODO(GDD-?)).
- `StartRaid`: `profile.Loadout = Capture` → `IsLeaving = true` → `flow.StartRaid(captured)`. 실패면 `IsLeaving = false`(장비는 GameFlow·ProfileService가 되돌림). `ReturnToMainMenu`: `SaveNow`(실패해도 진행) → `IsLeaving = true` → `LeaveHideout`, 거부되면 해제.
- 떠나는 중에는 `TryBuy`/`TrySell` → `TradeResult.Unavailable`, `TryClaimKit` → `KitClaimResult.Unavailable`, `SaveNow`/`StartRaid`/`ReturnToMainMenu` → false.
- `HideoutChanged` 발행: 매수·매도·키트 **성공** 때, `IsLeaving`이 바뀔 때(켜짐·풀림), 디버그 명령. 실패·`AcknowledgeNotice`는 발행하지 않는다(UI가 호출 뒤 직접 다시 그린다).
- 디버그용 공개 메서드 `DebugAddCasings(int)`, `DebugResetKitCooldown()`(ContextMenu가 부른다).
- `HideoutController`: `Service`(설계대로) + 추가 `EntryReport`. Awake에서 참조 3개·GameManager 확인 → 실패면 LogError + `enabled = false`(OnEnable 등록 안 함). `IInventoryService`·`HideoutChanged`는 호출 시점에 `GameManager.Instance`에서 찾는다. 시계는 `DateTime.UtcNow`. 디버그 메뉴 "탄피 +1000"의 1000은 디버그 상수(게임 수치 아님).

3단계가 알 것
- 씬 구성: `HideoutController`(-100)에 `config`·`trader`·`itemDatabase` 연결. 하나라도 비면 은신처가 시작되지 않는다(LogError → PlayMode 스모크가 잡는다).
- 보관함 변경(매수·키트·매도)은 `profile.Stash`(ItemGrid)를 **직접** 바꾼다. 은신처 인벤토리(2a `LootContainerModel.ForExistingGrid`)가 같은 그리드를 감싼다. 원본 2a 코드는 `IsRevealed = !hidden.Contains(item)`라 나중에 들어온 아이템도 드러난 것으로 보인다(코드로 확인, PlayMode 미확인). Hideout은 `InventoryChanged`를 내지 않는다 — UI는 `HideoutChanged`로 다시 그린다.
- W3(`OnApplicationQuit`)은 `Flow.Phase == Hideout && !IsLeaving`일 때만 저장. 에디터에서 Hideout 씬 직접 Play 후 중지해도 실제 세이브 경로에 쓴다(의도).

---

## 2b 구현 메모 (ui-programmer, 2026-10-22)
결과: 사본(`proj_2b`, 1단계 상태 + 2b 파일) EditMode 862/862 통과(1단계 825 − MainMenuPresenterTests 2 + 새 38: HideoutPresenterTests 25, TraderViewModelTests 4, HideoutNoticeViewModelTests 5, HideoutUxmlTests 4 + RaidUiUxmlTests 1). 사본 컴파일 경고 0. PlayMode는 돌리지 않았다(PlayMode 어셈블리는 컴파일됨). 커밋·CHANGELOG 안 함. Core·공용 테스트 도우미 무변경.

바뀐/새 파일 (원본)
- 수정: `UI/MainMenuPresenter.cs`(결과 패널 제거, `EnterHideout()`·`IsEntering`·`Quit()`), `UI/MainMenuRoot.cs`(결과 뷰 제거, 버튼 `menu-start` 문구 "은신처"), `UI/RaidResultView.cs`(주석·`Find` 오류 문구만), `UI/UiText.cs`(은신처·상인·키트·알림 문구), `Art/UI/MainMenu.uxml`(결과 패널 삭제), `Art/UI/Ashfall.uss`(은신처 스타일 추가, 변수 `--hideout-stash-width`·`--hideout-trader-column-width`).
- 새: `UI/HideoutPresenter.cs`(+ `HideoutPanel`·`HideoutTab`), `UI/HideoutNoticeViewModel.cs`, `UI/TraderViewModel.cs`(+ `TraderOfferRow`·`TraderSellRow`), `UI/HideoutRoot.cs`, `UI/HideoutHubView.cs`, `UI/TraderView.cs`, `UI/HideoutNoticeView.cs`, `Art/UI/HideoutUI.uxml`.
- 테스트: `EditMode/MainMenuPresenterTests.cs`(M5로 다시 씀), 새 `EditMode/HideoutUiModelTests.cs`(`FakeHideoutService` + 테스트 클래스 4개), `EditMode/RaidUiModelTests.cs`의 `RaidUiUxmlTests`(MainMenu는 결과 패널 없음 확인, 결과 렌더 확인은 HideoutUI.uxml로).

설계와 다르거나 설계에 없던 결정
- **PlayMode 컴파일 호환(3·4단계 정리 대상)**: `MainMenuPanel.Result` enum 값은 남겼다(쓰지 않음 — `RaidLoopFlowTests`가 참조). `UiText.MenuStartRaid`는 `MenuHideout`("은신처")의 별칭으로 남겼다(`M4TestSupport.Attach`가 버튼 문구를 이것과 비교). 3단계가 M4TestSupport를 `MenuHideout`으로 바꾸고 4단계가 RaidLoopFlowTests를 지우면 둘 다 지운다. `MainMenuPresenter.Panel`은 항상 Menu.
- 패널 결정: `PendingNotice != null && HasNotice` → 알림, 다음 `Flow.PendingResult` → 결과, 그 밖 허브. 알림·결과 중엔 허브 명령 전부 무시.
- 메시지: 거래·키트 결과(성공 포함 "구매했다: X"/"판매했다: X"/"부랑자 키트를 보관함에 받았다."), W1 저장 실패면 첫 메시지 "저장하지 못했다.", [레이드 시작] 실패는 `LastSaveFailed`로 "저장하지 못해 레이드를 시작할 수 없다."/"레이드를 시작할 수 없다.", [메인 메뉴]는 저장이 실패해도 이동(메시지만).
- 키트 문구: `IsKitAvailable` → "받을 수 있다", 아니면 남은 초 > 0 → "N분 뒤"(올림, 최소 1), 그 밖 "받을 수 없다". 버튼 = 가능 && 떠나는 중 아님. `HideoutRoot`가 실제 시간 1초마다 `UpdateKitStatus()`(바뀔 때만 다시 그림).
- 상인 매도 줄: 보관함 배치 순(y, x 오름차순), 아이템마다 한 줄, `GetSellPrice < 0`인 것은 줄 없음, 가격 0도 [판매] 가능(Q12). 매수 [구매]는 `BuyPrice ≤ Casings`일 때만.
- 알림 문구(TODO(GDD-?) Q6·Q22·Q23): RULE-03 → 설계 §1.4 문구 + "잃은 장비" 묶음 목록(비면 "없음"), Corrupt/UnsupportedVersion 문구, 격리 경로("이전 파일은 따로 보관했다: <경로>"), 알 수 없는 id·버린 수. 같은 실행 Interrupted도 같은 RULE-03 문구(보고서에 구분 정보가 없다).
- **떠나는 중(IsLeaving)에는 `InventoryScreenView.Bind(inventory, null)`로 드래그 모델을 뗀다** → 집기·Ctrl 빠른 이동·더블클릭 사용이 막힌다(반입 Capture 뒤 보관함→장비 이동으로 아이템이 사라지지 않게). [레이드 시작]/[메인 메뉴] 클릭 때 진행 중 포인터도 취소.
- 은신처 인벤토리 레이아웃: `inventory-screen`은 전체 화면 `.screen`이 아니라 허브 안을 채우는 `.hideout-inventory`. 소지품 패널 내용(`inv-storage-grids`·퀵슬롯)도 `hideout-storage-scroll`(ScrollView)에 넣었다(헤더 때문에 세로 공간이 레이드보다 작다). 보관함은 설계대로 `hideout-stash-scroll` 안.
- 퀵슬롯 배정(Q19): 넣었다. `Player/QuickSlot1~4`만 Awake에서 개별 Enable(끄지 않음 — GameUIRoot와 같음), [장비] 탭 + 허브 + 떠나는 중 아님일 때 포인터 아래 아이템에 `TryAssignQuickSlot`.
- Esc(UI/Cancel)는 드래그 취소만, R(UI/RotateItem)은 [장비] 탭 드래그 회전. 다른 키 없음.
- 드래그 임계: `HideoutRoot.hudConfig`(선택, `HudConfigSO.dragThresholdPx`), 비면 `DefaultDragThresholdPx = 4`(HudConfig 기본값과 같음, TODO(GDD-?)).
- 추가 요소 이름(계약 밖, 2b 내부): `trader-title`, `trader-offers-scroll`, `trader-sell-scroll`, `hideout-content`, `hideout-notice-list-section`, `hideout-storage-scroll`.
- USS 팔레트 회귀 테스트 추가(`HideoutUxmlTests.Uss_UsesOnlyPaletteColors`): 색 리터럴은 변수 정의 줄에만, 값은 §10.1 7색과 Ash_Black 반투명뿐.

3단계가 씬에 연결할 것 (`M5SceneSetup`)
- "HideoutUI" 오브젝트: `UIDocument`(sourceAsset = `Assets/_Project/Art/UI/HideoutUI.uxml`, panelSettings = `Assets/_Project/Art/UI/GamePanelSettings.asset`) + `HideoutRoot`. 선택: `HideoutRoot.hudConfig` = `Data/UI/HudConfig.asset`(SerializedObject 필드 이름 `hudConfig`). EventSystem 없음.
- `HideoutRoot`는 `IHideoutService`·`IInventoryService`를 매 프레임 레지스트리에서 확인해 지연 획득한다(실행 순서 무관). PlayMode 확인용 공개 `Presenter`(서비스 전엔 null)·`ViewsBuilt`.
- MainMenu 씬은 다시 만들 필요 없다(UXML 내용만 바뀜, GUID 그대로). PlayMode `M4TestSupport.Attach`는 별칭 덕에 그대로 동작하지만 `Confirm`·`ResultPanel`은 이제 null(MainMenu에 결과 패널 없음).

확신 없는 API (PlayMode·사람 확인 필요)
- 보관함 `ScrollView` 안 드래그: `InventoryScreenView`는 `panel.Pick` + `WorldToLocal`로 칸을 계산하므로 스크롤 오프셋을 반영할 것으로 보지만 EditMode에서는 레이아웃이 없어 확인 못 했다. 마우스 누름에 ScrollView가 드래그 스크롤을 하지 않는다(터치 전용)고 가정했다.
- ScrollView 폭 `--hideout-stash-width: 500px`(10칸×48px + 스크롤바)이 실제 스크롤바 폭과 맞는지, 1080p에서 장착·소지품·보관함 세 패널이 가로로 들어가는지 사람 확인.
- 비활성 Button(`SetEnabled(false)`)이 EventSystem 없는 경로에서 클릭을 막는지는 M4와 같은 가정.

## 2a 구현 메모 (gameplay-programmer, 2026-10-22)
결과: 사본 EditMode 866/866 통과(1단계 825 + 새 41: LoadoutApplyTests 18, HideoutInventoryTests 14, RaidResultBuilderTests +9). PlayMode 61/68 통과 — 실패 7개는 1단계 메모의 예상 목록과 정확히 같다(Hideout 씬 없음). 새 `LoadoutInjectionTests` 2개 통과. 실제 persistentDataPath에 save.json이 생기지 않았음을 확인. 커밋·CHANGELOG 수정 없음.

바뀐/새 파일 (원본)
- `Inventory/PlayerInventory.cs`: `QuickSlotCount = Loadout.QuickSlotCount`, `AllowDrop`·`AllowUse`(기본 true, false면 `TryDrop`·`TryUse` 거부), `TryPlaceNew(item, GridRef, cell, rotated)`, `TryApplyLoadout(Loadout, List<ItemInstance> rejected)`.
- `Inventory/LootContainerModel.cs`: `static ForExistingGrid(displayName, ItemGrid)` — 그리드를 복사 없이 감싸고 전부 드러남(검색 없음). null → `ArgumentNullException`, `GridKind.Loot`가 아닌 그리드 → `ArgumentException`.
- `Inventory/PlayerInventoryBehaviour.cs`: Awake에서 `Flow.TryGetRaidLoadout(gameObject.scene.name)` true면 `TryApplyLoadout`(거부 있으면 LogError), 아니면 기존 `ApplyStartingItems`.
- `Inventory/HideoutInventoryBehaviour.cs`(구현): 직렬화 필드 이름 그대로(`config`, `stashDisplayName`). 순수 준비 `public static PlayerInventory CreateInventory(config, profile, stashDisplayName, out LootContainerModel stashLoot, List<ItemInstance> dropped = null)` + 얇은 브리지(Awake·OnEnable/OnDisable·Start). 테스트·디버그용 `Inventory`, `StashLoot`, `DroppedCount`.
- `Raid/Logic/RaidResultBuilder.cs`: 오버로드 `Build(session, broughtIn, Loadout endLoadout)`. 기존 목록 오버로드는 유지(CarriedOutLoadout null). 둘 다 내부 `BuildCore` 하나로 판정(RULE-01 한 곳).
- `Raid/Runtime/RaidController.cs`: `OnSessionEnded`가 `Loadout.Capture(inventory)` → Loadout 오버로드(같은 시점, RaidEnded 직전). 예외 대체 결과는 `Build(..., Loadout.Empty)`. 반입 정의 목록(Start)은 그대로 `InventorySnapshot.Collect`. 주석의 "M5 경계"·"MainMenu에서 결과" 갱신.
- 테스트 새: `EditMode/LoadoutApplyTests.cs`, `EditMode/HideoutInventoryTests.cs`, `PlayMode/LoadoutInjectionTests.cs`. 수정: `EditMode/RaidResultBuilderTests.cs`(Loadout 오버로드 9개 추가 + 아래 캐스트 1줄).

설계와 다르거나 설계에 없던 결정
- `TryApplyLoadout` 순서: ① 장착 항목 전부 `TryEquipNew` ② 그리드 항목 전부 `TryPlaceNew` ③ ①②의 실패를 Entries 순으로 `TryAutoStore`(정확한 자리를 다 채운 뒤라 뒤 항목의 자리를 뺏지 않는다) ④ 그래도 실패 → rejected(null 허용) ⑤ 퀵슬롯은 저장 공간에 놓인 의약품만. **장착 실패(슬롯 규칙 위반 등)도 ③으로 간다.** 이미 이 인벤토리에 있는 인스턴스는 건너뛴다(놓은 수에도 rejected에도 안 들어감 — 보관함으로 보내 복제되지 않게). 사망·종료 잠금이면 전부 rejected. 반환 = 놓은 수(자동 배치 포함).
- `TryPlaceNew`: Loot 자리·없는 그리드(장착 안 된 조끼/배낭, 범위 밖 주머니 번호)·이미 인벤토리 안·잠김·Frozen → false.
- `HideoutInventoryBehaviour.CreateInventory` 마지막에 **`profile.Loadout = Loadout.Capture(inventory)`** 로 맞춘다: 거부돼 보관함으로 간 아이템이 은신처 장비 기록에도 남아 다음 저장에서 두 번 기록(복제)되지 않게. 그 뒤로는 바꾸지 않는다(이후 동기화는 HideoutService의 Capture — 레이드 시작 뒤 씬이 내려갈 때 다시 쓰면 BeginRaid가 비운 메모리 장비가 되살아나므로 일부러 Changed마다 쓰지 않는다).
- 보관함 자동 배치는 Hideout `StashRules`를 참조할 수 없어 같은 규칙(`ItemGrid.TryFindFreeCell`, 위→아래·왼→오른쪽)을 직접 쓴다. 보관함도 가득이면 버리고 LogWarning(`DroppedCount`).
- 브리지 이벤트: 모델 `Changed` → `InventoryChanged`, `EquipmentChanged` → `EquipmentChanged`, `HideoutChanged` → `InventoryChanged`, Start에서 초기 EquipmentChanged×7 + InventoryChanged. `LootSessionChanged`·`CarriedWeightChanged`는 발행하지 않는다(은신처는 보관함이 항상 열려 있고 HUD가 없다). 모델의 `LootSessionChanged(false)`(UI의 CloseLoot)를 받으면 같은 보관함을 바로 다시 연다.
- Awake 실패 조건: `config` 없음, GameManager·Profiles 없음 또는 `!IsLoaded` → LogError + `enabled = false`(등록 안 함).
- Loadout 오버로드에 `null` → 빈 장비로 본다: 탈출이면 `CarriedOutLoadout = Loadout.Empty`(null 아님), 그 밖은 null.

계약 관련 (Core는 고치지 않음)
- `RaidResultBuilder.Build`에 오버로드가 둘이 되어 **`Build(session, x, null)` 리터럴 null은 모호(CS0121)** 하다. 기존 `RaidResultBuilderTests.NullLists_Empty`의 한 줄을 `(IReadOnlyList<ItemInstance>)null`로 캐스트했다. 다른 단계 코드에 같은 호출이 있으면 같은 캐스트가 필요하다(현재 원본엔 없음).
- Core 계약 변경 요청 없음.

3단계가 알 것
- `M5SceneSetup`: "HideoutSystems"의 `HideoutInventoryBehaviour`에 `config` = `Data/Inventory/InventoryConfig.asset`. `stashDisplayName`은 기본값 "보관함". `HideoutController`(-100)가 같은 씬에서 먼저 Awake해야 한다(아니면 LogError + 비활성).
- `PlayMode/LoadoutInjectionTests`는 TearDown에서 `Flow.TakeRaidReturn`으로 흐름 표를 비우고 `GameManager.UseSaveStorage(<OS temp 새 폴더>)`로 메모리 프로필을 버린다 → 이후 PlayMode 테스트의 기본 저장소가 실제 경로가 아니라 임시 폴더가 된다(무해, SaveSandbox가 다시 바꿔도 됨). 흐름 테스트는 `Flow.OnSceneLoaded(Hideout)` → `StartRaid(loadout)`로 실제 Raid_CrimsonBasin을 불러오고, 끝에 MainMenu를 직접 불러 단계를 MainMenu로 돌린다. 코드 씬 테스트는 먼저 MainMenu를 불러 남은 레이드 씬을 치운 뒤 이름 `Raid_CrimsonBasin`의 빈 씬을 만든다.
- 1단계 예상 실패 7개(Hideout 씬 없음)는 3단계에서 Hideout 씬이 생기면 다시 확인할 것. `InventoryFlowTests.SceneUnload_…` 구독 누수도 연쇄로 같이 사라져야 한다.
- 사람 확인: 은신처 [장비] 탭에서 패널 밖 드롭이 원위치로 돌아오는지(TryDrop false), 의약품 우클릭/퀵슬롯 키 사용이 아무 일도 안 하는지.

---

## 3단계 구현 메모 (gameplay-programmer, 2026-10-22)
결과(원본): EditMode **994/994**, PlayMode **69/72** 통과 + 3 무시(`RaidLoopFlowTests` 3개, `[Ignore("M5 4단계: HideoutLoopFlowTests로 교체")]`), 실패 0(종료 코드 둘 다 0). 컴파일 경고 CS 0. 커밋·CHANGELOG 안 함.
로그·결과: `scratchpad/m5s3_setup1.log`·`m5s3_setup2.log`(CreateAll 2회), `m5s3_meta_before/run1/run2.txt`·`m5s3_data_run1/run2.txt`(md5), `Hideout_run1.unity`(1회차 씬 사본), `m5s3_edit.*`, `m5s3_play.*`.
실제 `%USERPROFILE%\AppData\LocalLow\DefaultCompany\Ashfall\`에 `save.json`(·.bak·.tmp·corrupt) 없음 확인(있는 것은 Unity 기본 `TestResults.xml`·`Unity/`뿐). PlayMode 샌드박스 폴더 `<OS temp>/AshfallPlayModeSaves/`는 실행 뒤 비어 있다.

통합 점검 (원본, 1단계 + 2a/2b/2c/2d 첫 동시 컴파일)
- 컴파일 오류·경고 **없음**. 제품 코드(Core·Hideout·Inventory·Raid·UI)는 하나도 고치지 않았다.
- 1단계 연쇄 실패 `InventoryFlowTests.SceneUnload_LeavesNoSubscribersOrServices` **통과**(Hideout 씬이 생겨 앞 테스트의 레이드 씬이 정상으로 내려감). 1단계 예상 실패 6개는 아래 수정 후 통과(3개는 무시).
- 2a 메모의 `LoadoutInjectionTests` TearDown(`UseSaveStorage(<temp 새 폴더>)`) — 샌드박스와 충돌은 없었지만(둘 다 임시 폴더) 그 폴더를 TearDown이 지운 뒤에도 저장소로 남아 이후 쓰기가 정리되지 않는 폴더를 다시 만들 수 있어, **한 줄을 `SaveSandbox.UseFreshStorage("loadout-injection")`로 바꿨다**(실행 폴더 안 → 끝에 지워짐). 테스트 본문의 `NewTempDir()`은 그대로.
- 2b가 남긴 `MainMenuPanel.Result`·`UiText.MenuStartRaid`는 그대로 둔다(RaidLoopFlowTests가 컴파일에 쓴다 — 4단계가 지울 때 함께).
- Unity가 이 세션 실행 중 `Assets/Settings/Mobile_RPAsset.asset`을 URP 에셋 버전 12 → 13으로 자동 업그레이드했다(필드 추가·`m_UseLegacyLightmaps` 제거). M5 작업과 무관한 엔진 직렬화 갱신 — 커밋에 넣을지는 오케스트레이터 판단(되돌려도 다음 실행에서 다시 생긴다).

새·수정 파일
- 새 `Scripts/EditorTools/M5SceneSetup.cs` — 메뉴 `Ashfall/Setup/Create M5 Hideout Scene`, 배치 `CreateAll`. 공개 상수 `HideoutScenePath`·`HideoutUxmlPath`·`SystemsObjectName`("HideoutSystems")·`UiObjectName`("HideoutUI").
  순서: `M5DataSetup.CreateData()` → `M2SceneSetup.CreatePanelSettings()` → SaveAssets → NewScene(Default) → Light 삭제, MainCamera SolidColor 검정 →
  (NewScene 뒤 에셋 로드, 필수 하나라도 없으면 저장 안 하고 예외) "HideoutSystems": `HideoutController`(config/trader/itemDatabase = `M5DataSetup.*Path`) + `HideoutInventoryBehaviour`(config = `Data/Inventory/InventoryConfig.asset`, stashDisplayName 기본 "보관함") →
  "HideoutUI": `UIDocument`(HideoutUI.uxml + GamePanelSettings) + `HideoutRoot`(hudConfig = `Data/UI/HudConfig.asset`) → EventSystem 없음 → `Scenes/Hideout.unity` 저장(덮어씀) → `SetupUtil.ApplyBuildScenes()`.
- 새 `Scripts/EditorTools/SaveTools.cs` — 메뉴 `Ashfall/Save/Open Save Folder`(save.json이 있으면 선택, 없으면 폴더를 만들어 표시), `Ashfall/Save/Delete Save Files`(확인 창, save.json·.bak·.tmp·save.corrupt.json 중 있는 것만), 둘 다 Play 중 비활성. 경로는 `new FileSaveStorage(Application.persistentDataPath)`(게임과 같음). 공개 `SaveFilePaths()`.
- 수정 `Scripts/EditorTools/SetupUtil.cs` — `ApplyBuildScenes()`(Boot·MainMenu 필수(없으면 예외) → Hideout·Raid_CrimsonBasin은 있으면, 없으면 LogWarning 후 뺌), 로그용 `DescribeBuildScenes()`.
- 수정 `Scripts/EditorTools/M4SceneSetup.cs` — `SetBuildScenes()`가 `SetupUtil.ApplyBuildScenes()`만 부른다(M4 메뉴 재실행에도 Hideout 유지), 주석·완료 로그. 씬 생성 동작 불변.
- 테스트 수정: `EditMode/BuildSettingsTests.cs`(+Hideout 포함, 순서 정확히 Boot → MainMenu → Hideout → Raid, 빌드 씬 4개 모두 활성), `EditMode/M3DataAssetTests.cs`(권총 basePrice 400, SMG 1500), `EditMode/M4DataAssetTests.cs`(신호탄 500, 지도 100),
  `PlayMode/M4TestSupport.cs`(`M4Raid.WaitForHideout()` 추가 — Hideout 활성 + `HideoutRoot.Presenter`·`ViewsBuilt`; `M4Menu.Attach`가 `UiText.MenuHideout` 비교; `Confirm`·`ResultPanel`은 MainMenu에서 항상 null이라 주석으로 표시, RaidLoopFlowTests 컴파일용으로만 유지),
  `PlayMode/ExtractionFlowTests.cs`(EXT-01 테스트 이름 `…_ThenExtractsToHideout`, 종료 후 Hideout·Phase Hideout·`HasRaidInFlow` false·은신처 결과 패널 표시(Q25 표시만)), `PlayMode/RaidEndFreezeTests.cs`(종료 목적지 Hideout, 정지는 Hideout 로드 완료까지; 테스트 이름은 그대로),
  `PlayMode/RaidSceneSmokeTests.cs`(MainMenu 버튼 문구 "은신처", MainMenu에 `result-panel` 없음), `PlayMode/RaidLoopFlowTests.cs`(3개 Ignore), `PlayMode/LoadoutInjectionTests.cs`(TearDown 한 줄, 위).
- 테스트 새: `PlayMode/SaveSandbox.cs` — `[SetUpFixture]`(네임스페이스 `Ashfall.Tests.PlayMode` 전체). `OneTimeSetUp`: `<OS temp>/AshfallPlayModeSaves/run_<guid>/default_<guid>`로 `GameManager.UseSaveStorage`. `OneTimeTearDown`: 실행 폴더 삭제.
  공개 `RunDirectory`, `Current`, `UseFreshStorage(label)`(실행 폴더 아래 새 하위 폴더 + 메모리 프로필 버림), `IsUnderPersistentDataPath(path)`.
  `PlayMode/SaveSandboxTests.cs`(3): 현재 저장 위치가 persistentDataPath 아래가 아니고 샌드박스 폴더, `UseFreshStorage`가 위치를 바꾸고 프로필을 버림, 판정 함수 자체.
  `PlayMode/HideoutSceneSmokeTests.cs`(1): 새 샌드박스 폴더 → Hideout 이름 로드 → Phase Hideout, 카메라 1, 커서 해제, HideoutController·HideoutInventoryBehaviour·HideoutRoot 1개씩·활성·참조 4개 연결,
  `IHideoutService`(= controller.Service)·`IInventoryService`(= behaviour.Inventory) 등록, `IMagazineSource` 미등록, 새 프로필(탄피 6000, 보관함 10×30 빈, 장비 빈, 키트 가능), W1 save.json이 샌드박스에 생김(raidInProgress false·casings 6000·stash 0),
  ActiveLoot = 10×30 Loot·이름 "보관함", 허브 표시·알림/결과 숨김·탄피·키트 문구·버튼 5개 활성·[장비] 탭 = 인벤토리 화면·보관함 ScrollView·제목,
  [부랑자 키트 받기] 클릭(UiPointer — 배치모드에선 합성 포인터 이벤트 경로로 눌림, M4와 같음) → 보관함 = MAG-9MM-12 ×2·MED-BND-01·WPN-PST-01, 권총 탄창 없음, 탄창 가득, 탄피 그대로, 쿨다운, 버튼 비활성·문구 변경,
  [장비] 탭 보관함 패널에 아이템 요소 4개(숨김 없음), [상인] 탭 매도 줄 4·판매 목록 > 0, 파일은 진입 저장 그대로(Q7). TearDown: MainMenu 로드 + 새 샌드박스 폴더.

생성 에셋 (원본, 처음 — `.meta`는 Unity가 만듦)
- `Scenes/Hideout.unity`(10,145 B, GUID 463f652c…), `Data/Hideout/`(폴더 GUID 1ffa7471…) `HideoutConfig.asset`(176d3cbb…)·`TraderConfig.asset`(c1944410…), `Data/Items/ItemDatabase.asset`(130b1752…, 아이템 20 = 18 + AMMO 2).
- 수정: 기존 아이템 18개 `basePrice` 한 줄씩(2c 표 그대로), `ProjectSettings/EditorBuildSettings.asset`(+Hideout, 4개).
- 1단계·2단계 새 스크립트·UXML·테스트의 `.meta`도 이 실행에서 생겼다(커밋 대상). 기존 `.meta` 380개 GUID·내용 불변(1회차 전 ↔ 후 md5 비교).

재실행 결정성 (CreateAll 2회)
- `.meta` 전부 md5 동일(1회차 ↔ 2회차 diff 없음).
- **바이트 동일**: `Data/` 전체·`Art/` 전체(HideoutConfig·TraderConfig·ItemDatabase·아이템 18·HideoutUI.uxml·GamePanelSettings 포함), `EditorBuildSettings.asset`.
- `Hideout.unity`만 다름 — 로컬 fileID(`&N`, `fileID: N`)만 바뀌고 정규화하면 동일. 외부 참조 GUID(HideoutConfig·TraderConfig·ItemDatabase·InventoryConfig·HudConfig·GamePanelSettings·HideoutUI.uxml·스크립트 3개)는 같다.

4단계가 알 것
- Hideout은 빌드 설정에 있어 `SceneManager.LoadSceneAsync(SceneNames.Hideout)`로 불러 `Flow.Phase == Hideout`. 레이드 종료(흐름 밖 포함) → endDelaySec → Hideout. `M4Raid.WaitForHideout()`을 써도 된다.
- 세이브: 모든 PlayMode 테스트는 이미 샌드박스(SetUpFixture) 안이다. 은신처를 쓰는 테스트는 SetUp에서 `SaveSandbox.UseFreshStorage(label)`로 새 폴더 + 새 프로필(= 새 실행). "다음 실행" 흉내는 `gm.UseSaveStorage(new FileSaveStorage(SaveSandbox.Current.DirectoryPath))`(같은 폴더 새 저장소).
  파일 확인은 `SaveSerializer.TryParse(File.ReadAllText(storage.FilePath), …)`(HideoutSceneSmokeTests `AssertSaveFile` 참고).
- 흐름 밖 레이드로 은신처에 오면 `[HideoutService] 흐름 밖 레이드 결과 — 표시만…` 경고가 난다(기대 동작, ExtractionFlowTests·RaidEndFreezeTests 3건). 은신처 결과 패널은 `HideoutPanel.Result`, 다음 테스트가 남은 결과를 보지 않게 TearDown에서 `Flow.AcknowledgeResult()`.
- `UiPointer`로 은신처 버튼은 배치모드에서 실제 포인터 경로가 아니라 합성 이벤트로 눌린다(창 포커스 없음, M4와 같음) — 실제 마우스는 사람 확인.
- 정리 대상(4단계): `RaidLoopFlowTests.cs` 삭제와 함께 `M4Raid.WaitForMainMenu`, `M4Menu.Confirm`·`ResultPanel`, `MainMenuPanel.Result`, `UiText.MenuStartRaid`.
- 은신처 씬이 열린 채 Play 모드가 끝나면 W3(OnApplicationQuit)가 그때 저장소(샌드박스)에 쓸 수 있다 — 실제 경로가 아니라 무해하지만, 흐름 테스트 끝에 MainMenu로 내려 두면 깔끔하다.

## 4단계 구현 메모 (test-engineer, 2026-10-22)
결과(원본): EditMode **995/995**(994 + 새 1), PlayMode **77/77** 통과 **2회 연속**(69 + 새 8, RaidLoopFlowTests의 Ignore 3은 파일과 함께 삭제), 실패·무시 0, 종료 코드 모두 0, `error CS` 0.
Windows 빌드 성공(`Build Finished, Result: Success`, 오류 0, 경고 1 = 알려진 "스크립트 없는 asmdef" 경고, 약 39초) → `Builds/Windows64/Ashfall.exe`, 씬 4개 `Boot → MainMenu → Hideout → Raid_CrimsonBasin`(`Ashfall_Data/level0~3`).
실제 `%USERPROFILE%\AppData\LocalLow\DefaultCompany\Ashfall\`에 `save.json`·`.bak`·`.tmp`·`save.corrupt.json` 없음 확인(있는 것은 Unity 기본 `TestResults.xml`·`Unity/`뿐). PlayMode 샌드박스 `<OS temp>/AshfallPlayModeSaves/`는 실행 뒤 비어 있다. 커밋·CHANGELOG 안 함.
로그·결과(scratchpad): `m5s4_edit1/edit2.{xml,log}`, `m5s4_play1/play2.{xml,log}`, `m5s4_build.log`.

새 테스트
- `PlayMode/M5TestSupport.cs` — `M5Hideout`(은신처 참조·`Attach`, 허브 버튼·[상인] 줄 [구매]/[판매] 클릭(줄을 ScrollView 안에서 먼저 보이게 스크롤), 장비 옮기기 `Equip`/`QuickMove`(IInventoryService 명령), [레이드 시작] → `M4Raid.Attach`, 결과 패널 검사·[확인], 알림 [확인], `Describe(Loadout)` = "id@위치(회전)·탄 수·퀵슬롯" 비교 문자열),
  `M5Save`(SaveSandbox 저장소의 save.json을 `SaveSerializer.TryParse`로 읽기 + persistentDataPath 아님 단정, 폴더 스냅숏(이름·크기·수정 시각)), `M5Flow`(흐름 정리, TearDown = MainMenu로 내리고 새 샌드박스 폴더, 구독 수, 레이드 상자 하나 열어 검색 완료 후 저장 공간에 들어가는 루트 하나 가져오기).
- `PlayMode/HideoutLoopFlowTests.cs`(3)
  - `BootMenuHideout_KitTradeLoadout_RaidLootExtract_ThenDeath_ThenMenuReentry` — Boot → MainMenu [은신처](프로필 미로드 확인) → 새 프로필 6000 → [부랑자 키트 받기] → [상인] 소총·5.56 탄창·조끼·지도 구매(900 남음) → 장비(소총 Primary1, 조끼, 조끼 안 탄창 회전, 지도·붕대 주머니, 붕대 퀵슬롯 0) → [레이드 시작] → save.json raidInProgress=true·loadout = 반입·보관함 그대로, 레이드 소지품 = 반입(위치·회전·퀵슬롯 그대로, startingItems 아님), 메모리 장비 비어 있음 → 상자 루트 획득 → EXT-01 → 종료 오버레이 → 파일 그대로(레이드 중 쓰기 없음) → Hideout 결과 패널 "탈출 성공 — 남쪽 도로"·반출 목록에 획득품 → [확인] → 장비 = 종료 시점 소지품(위치 그대로), 보관함·탄피 그대로, save.json raidInProgress=false·loadout에 획득품
    → 다시 [레이드 시작] → 머리 사망 → "사망 — 머리", 장비 비어 있음, 보관함 그대로, save.json loadout 0 → 권총 장착 후 [메인 메뉴](W2에 그 장비 저장) → 파일을 "not json"으로 망가뜨리고 다시 [은신처] → 알림·격리 없이 같은 상태(같은 실행은 다시 읽지 않음), W1이 덮어씀 → [메인 메뉴] → 구독 수 누수 없음, 서비스 해지.
  - `HideoutToRaid_MissingInAction_LoadoutLost_StashKept` — 키트 일부를 장비로 → 레이드 → MIA → "작전 중 실종 (MIA)"·"레이드 시간 25:00", 장비 소실, 보관함 그대로, save.json loadout 0.
  - `HideoutToRaid_EscPauseFreezesWorld_ResumeButton_ThenAbandon_LoadoutLost` — M4의 일시정지 경로(세계·타이머·적 정지, Tab·M 무시, [계속], Esc 재개, [레이드 포기] → [포기]) + 일시정지 중 저장 없음 + 결과 "레이드 포기"·장비 소실.
- `PlayMode/CrashRecoveryFlowTests.cs`(1) `RaidInProgress_NextRun_FirstHideoutEntry_DeletesLoadout_NoticeShown_FlagCleared` — 키트·지도·조끼 → 장비 → [레이드 시작] → save.json raidInProgress=true·반입 기록·키트 시각, tmp 없음 → 레이드 중 일시정지·상자 루트·2초 경과 동안 세이브 폴더 변화 없음 →
  "다음 실행"(흐름 레이드 표 비움 + 같은 폴더 새 `FileSaveStorage`) → Boot → MainMenu(프로필 미로드, 폴더 변화 없음) → [은신처] → `EntryReport` Loaded·CrashPenalty·CrashLost = 반입, 알림 패널(RULE-03 문구 + 잃은 장비 묶음, 허브·결과 숨김), 장비 비어 있음·보관함·탄피·키트 쿨다운 그대로·레이드 루트 없음, save.json raidInProgress=false·loadout 0·키트 시각 그대로·.bak 있음 → [확인] → 허브 → 한 번 더 "다음 실행"은 알림 없음(멱등).
- `PlayMode/CorruptSaveFlowTests.cs`(4) — 잘린 JSON·빈 파일 → Corrupt / version 99 → UnsupportedVersion: save.corrupt.json(원본 내용 그대로), 알림 문구 + 격리 경로, 잃은 장비 목록 없음, 새 프로필(6000·빈 보관함·키트 가능), W1 새 save.json 파싱 성공. 알 수 없는 id(보관함 1·장비 1) → Loaded, 그 레코드만 버림·알림 "알 수 없는 아이템 2개", 격리 없음, 나머지(권총·7발 탄창·주머니 붕대·탄피 1234) 유지, W1 파일에 알 수 없는 id 없음.
- EditMode 보충(AC1, 기존에 빠진 것): `HideoutServiceTests.KitAndTrades_SavedOnMainMenu_NextRun_Restored_CooldownContinues` — 키트·매수·매도 → [메인 메뉴](W2) → 같은 저장소의 새 ProfileService·GameFlow(10분 뒤) → 탄피·보관함 유지, 쿨다운 600초 남음·OnCooldown, 20분 뒤 다시 수령. 그 밖 AC1(보관함 10×30, 가격, 탄피 부족, 자리 없음, 키트 쿨다운·NoSpace)은 기존 EditMode(Trader/VagrantKit/PriceRules/HideoutInventory/M5DataAsset)가 이미 덮어 추가하지 않았다.

정리(3단계 메모의 임시 잔재)
- 삭제: `PlayMode/RaidLoopFlowTests.cs`(+`.meta`), `M4Raid.WaitForMainMenu`, `M4Menu.Confirm`·`ResultPanel`, `MainMenuPanel.Result`(UI `MainMenuPresenter.cs`), `UiText.MenuStartRaid`(UI `UiText.cs`). M4TestSupport 주석의 "MainMenu로 전환"을 Hideout으로 고침.
- 새 `.meta` 4개(`M5TestSupport`·`HideoutLoopFlowTests`·`CrashRecoveryFlowTests`·`CorruptSaveFlowTests`)는 배치 실행에서 Unity가 만들었다(커밋 대상). `RaidLoopFlowTests.cs.meta`는 작업 트리에서 지웠다(스테이징 안 함).

고친 제품 코드: **없음**(위 정리의 테스트 호환 잔재 2개 삭제 외). 테스트 중 제품 버그를 찾지 못했다.

관찰(버그 아님, 참고)
- 배치모드에서 은신처·메뉴·결과·알림 버튼 클릭은 전부 합성 포인터 이벤트 경로(`SyntheticPointerEvents`, 44회)로 눌렸다(창 포커스 없음, Screen 640×480 — M4와 같음). 실제 마우스 경로는 사람 확인.
- `Assets/Settings/Mobile_RPAsset.asset` 변경은 3단계 메모대로 Unity 자동 업그레이드(이 단계 무관).

AC별 결과
| AC | 자동 검증 | 사람 확인 필요 |
|---|---|---|
| AC1 보관함·상인·부랑자 키트 | ✔ EditMode(가격·매수/매도·탄피 부족·자리 없음·키트 쿨다운·NoSpace·10×30·다음 실행 지속) + PlayMode(UI 버튼으로 키트·구매, 보관함 → 장비 이동, [상인] 줄) | 실제 마우스 드래그·R 회전·Ctrl+클릭·보관함 세로 스크롤, 상인 버튼, 1080p 레이아웃·문구 가독성 |
| AC2 세이브/로드·강제 종료(§13.3, RULE-03) | ✔ 레이드 진입 시 raidInProgress=true + 반입 기록, 레이드 중 쓰기 없음, 다음 실행 첫 은신처 진입에서 반입 삭제·알림·flag false 저장(멱등), 손상·빈 파일·미래 버전 → 격리·새 프로필·알림, 알 수 없는 id → 그 레코드만 버림, 같은 실행 재진입은 다시 읽지 않음, 실제 persistentDataPath 미사용 | Windows 빌드에서 정리 → 메뉴 → 종료 → 재실행 유지, 레이드 중 Alt+F4 → 재실행 → 알림·반입 소실, 실제 save.json 내용 |
| AC3 은신처 ↔ 레이드 전체 루프 1회 완주 | ✔ Boot → MainMenu → Hideout → 키트·구매 → 장비 → 레이드 → 상자 루트 → EXT-01 → Hideout 결과 → 장비 = 반출품·save.json 갱신 → 사망 루프(RULE-01) → MIA·포기 → 메뉴 재진입, 누수 없음. 빌드 씬 4개 | 에디터에서 Boot부터 루프를 실제 마우스로(메뉴·허브·상인·결과·알림 [확인]), 커서, 레이드 중 Play 중지 → 다음 Play 은신처 강제 종료 알림(의도된 동작) |

사람 확인 목록(에디터·빌드)
1. 에디터 Boot 씬 Play: [은신처] → [부랑자 키트 받기] → [상인] 구매·판매 → [장비] 탭에서 마우스로 보관함 ↔ 장비 드래그·R 회전·Ctrl+클릭·스크롤 → [레이드 시작] → 탈출 → 결과 [확인] → 장비에 반출품 → 다시 레이드 → 사망 → 장비 비어 있음.
2. 레이드 중 에디터 Play 중지 → 다시 Play → 은신처 진입 시 RULE-03 알림·반입 소실(의도). 테스트 전후 `Ashfall/Save/Delete Save Files`로 실제 세이브 정리 가능.
3. Windows 빌드(`Builds/Windows64/Ashfall.exe`): 은신처 정리 → [메인 메뉴] → 종료 → 재실행 → 유지 / 레이드 중 Alt+F4 → 재실행 → 알림 / `%USERPROFILE%\AppData\LocalLow\DefaultCompany\Ashfall\save.json` 내용.
4. 1080p에서 장착·소지품·보관함 세 패널 가로 배치, 보관함 ScrollView 폭(스크롤바), 비활성 버튼 클릭 차단.

## 리뷰 수정 메모 (gameplay-programmer, 2026-10-22)
대상: `M5_review.md` 중간 1·2 + 낮음 3~11. 커밋 안 함. CHANGELOG_GDD는 건드리지 않았다(오케스트레이터 몫). UI 파일도 이번엔 이 단계가 고쳤다.
결과(원본): EditMode **1021/1021**(995 + 26), PlayMode **77/77** 2회 연속, 종료 코드 모두 0, `error CS`·`warning CS` 0. Windows 빌드 Succeeded(오류 0, 경고 1 = 알려진 "스크립트 없는 asmdef", 107,265,875 B, 약 28초).
실제 `%USERPROFILE%\AppData\LocalLow\DefaultCompany\Ashfall\`에 `save.json`·`.bak`·`.tmp`·`save.corrupt.json` 없음(있는 것은 `TestResults.xml`·`Unity/`뿐). `<OS temp>/AshfallPlayModeSaves/`·`AshfallTests/`는 실행 뒤 비어 있다. 씬·`.asset`은 다시 만들지 않았다(M5SceneSetup 재실행 불필요).
로그·결과(scratchpad): `m5s5_edit1.{xml,log}`, `m5s5_play1.{xml,log}`, `m5s5_play2.{xml,log}`, `m5s5_build.log`.

설계 변경점 (본문 §1.4·§2.4·§2.5와 다른 것)
- **읽기 오류 ≠ 손상 (리뷰 1)**: 새 `SaveLoadStatus.ReadError`. `ProfileService`가 `TryRead`의 `Error`를 `ReadAttempts`(3)회까지 `ReadRetryDelayMs`(100ms) 간격으로 다시 읽는다(TODO(GDD-?) Q22, 기술 상수 — 생성자 선택 인자 `Action<int> wait`로 테스트가 대기를 바꾼다). 그래도 실패하면 **격리하지 않고** 새 프로필을 메모리에만 두고 `IsReadOnly = true` + LogError + 알림.
  읽기 전용이면 `Save()`가 경고만 남기고 false(`LastSaveError = ReadOnlySaveError`) → W1·W2·W3 쓰기 없음, W4는 `BeginRaid` 저장 실패로 레이드 거부. 같은 실행 동안 유지, `UseStorage`(= 다음 실행)가 푼다.
  `IHideoutService.IsReadOnly` 추가. UI: 알림 줄 `UiText.NoticeReadError`, 첫 메시지 `MessageReadOnly`, [레이드 시작] 거부 `MessageStartRaidReadOnly`. 읽기 전용에서 [메인 메뉴]는 바로 이동(진입 때 알렸고 어차피 저장하지 않는다).
- **진입 예외 (리뷰 2)**: `EnsureLoaded`의 `ToProfile` 예외 → 일부 채운 보고서를 버리고 Corrupt(`Error = "해석 실패 …"`) + 격리 + 새 프로필. `HideoutService.TryEnter(…, entryToken, createService, out report, out service)` 추가 — Enter와 서비스 생성을 감싸 예외면 LogException·LogError, 진입 표시 지움, **흐름 레이드 표 비움**(`TakeRaidReturn`, 다음 [레이드 시작]이 막히지 않게; 버린 결과는 반영 안 됨 — 메모리 RaidInProgress가 남으면 다음 진입이 결과 없는 귀환, 아직 못 읽었으면 파일의 raidInProgress가 다음 실행 RULE-03). `HideoutController`는 실패하면 `EntryFailed = true`, **`Start`에서 `Flow.LeaveHideout()`로 MainMenu**(sceneLoaded가 Start 앞이라 Phase Hideout). 서비스 없는 [메인 메뉴]는 만들지 않았다(단순한 쪽).
- **알림 Status (리뷰 3)**: `ApplyInterruptedRaid`의 새 알림은 `Loaded`(확인 안 한 알림이 있으면 거기에 합치는 것은 그대로).
- **진입 완료 표시 (리뷰 5)**: `ProfileService.HideoutEntryToken`/`MarkHideoutEntered(token)`/`IsHideoutEntered(token)`/`ClearHideoutEntry()`. 토큰 = 은신처 씬의 `Scene.handle`. `HideoutService.Enter(…, entryToken = 0)`가 시작에 지우고 W1 저장 뒤 남긴다. `BeginRaid` 성공·`ReturnToMainMenu` 성공·`UseStorage`가 지운다.
  `HideoutInventoryBehaviour`는 `IsLoaded` 대신 `IsEntryComplete(profiles, gameObject.scene.handle)`(공개 static)을 본다. Awake에서 아니면 **Start에서 한 번 더**(같은 씬 모든 Awake 뒤) → 그래도 아니면 LogError + 비활성(장비를 놓지 않아 HideoutService는 `profile.Loadout`을 그대로 쓴다). 실행 순서(-100)는 그대로 둔다.
- **W2 저장 실패 (리뷰 6)**: `HideoutService.ReturnToMainMenu`가 저장 실패면 **이동하지 않고 false**(`LastSaveFailed`). 다음 호출은 다시 저장해 보고 또 실패해도 이동. UI 메시지 `MessageMainMenuSaveFailed`("…다시 누르면 저장하지 못한 채로 나간다."). §1.4 표 W2 "알림만, 이동은 함"과 다르다 — 본문·CHANGELOG 반영은 오케스트레이터 몫.
- **레이드 시작 실패 이유 (리뷰 7)**: `ProfileService.ClearSaveFailure()` 추가, `HideoutService.StartRaid`가 `flow.StartRaid` 직전에 부른다 → 단계 거부는 "레이드를 시작할 수 없다.", 이번 시도의 저장 실패만 "저장하지 못해…".
- **탄약 종류 (리뷰 8)**: `SaveSerializer.ReadLoadedMagazine`이 탄창 `ammo` ≠ 무기 `ammo`면 탄창 없는 무기 + `DroppedItemCount++`(알 수 없는 id가 아님).
- **.tmp 읽기 (리뷰 9)**: `FileSaveStorage.TryRead` — save.json이 없고 `.tmp`가 있으면 `.tmp`를 읽는다(LogWarning, `LastReadFromTemp`). save.json이 있으면 `.tmp`는 계속 무시, `.bak`은 계속 읽지 않는다. `TryQuarantine`은 save.json이 없으면 `.tmp`를 옮긴다.
- **상인 목록 (리뷰 10)**: `HideoutPresenter.Trader`를 지연 생성(처음 읽을 때 `TraderViewModel.Build`, `Refresh`가 버림) → 숨은 [상인] 탭은 만들지 않는다. `TraderModel.GetSellPrice`의 `Stash.Contains` O(n)은 그대로(탭이 보일 때만 돈다).
- **정리 (리뷰 11)**: `UiText.MenuStartRaid`·`MainMenuPanel.Result`는 4단계에서 이미 지워져 있었다(확인만). `Loadout.Capture`·`InventorySnapshot.Collect`의 "재장전 중 손에 든 탄창" TODO를 "CompleteReload가 즉시 교체 — 인벤토리 밖 탄창 없음"으로 고침. `HideoutRoot` 퀵슬롯은 `Player/QuickSlotN`의 **복제본**(`InputAction.Clone()`)을 OnEnable에서 켜고 OnDisable에서 끄고 OnDestroy에서 Dispose — 프로젝트 Player 맵(레이드가 켬)을 건드리지 않아 씬 전환 순서와 무관.
- **반입 거부 (리뷰 4)**: 안전망 코드는 넣지 않고 데이터 테스트만(두 씬이 같은 `InventoryConfig.asset`만 참조). `PlayerInventoryBehaviour`의 LogError는 그대로.

바뀐 파일
- Core: `Save/ProfileService.cs`, `Save/FileSaveStorage.cs`, `Save/SaveLoadReport.cs`(ReadError), `Save/SaveSerializer.cs`, `Services/IHideoutService.cs`(IsReadOnly, 주석), `Items/Loadout.cs`·`Items/InventorySnapshot.cs`(주석)
- Hideout: `Logic/HideoutService.cs`(Enter 토큰, TryEnter, ReturnToMainMenu, StartRaid, IsReadOnly), `Runtime/HideoutController.cs`(TryEnter, EntryFailed, Start)
- Inventory: `HideoutInventoryBehaviour.cs`(진입 토큰 확인, Start 재시도)
- UI: `HideoutPresenter.cs`, `HideoutNoticeViewModel.cs`, `UiText.cs`, `HideoutRoot.cs`
- 테스트: EditMode `ProfileServiceTests`, `FileSaveStorageTests`, `SaveSerializerTests`, `HideoutServiceTests`, `HideoutInventoryTests`, `HideoutUiModelTests`(FakeHideoutService `IsReadOnly`·`OffersReads`), `M5TestData`(MemorySaveStorage `ReadFailCount`), 새 `SceneInventoryConfigTests.cs`(.meta는 배치 실행에서 Unity가 생성). PlayMode 변경 없음.

추가 테스트 (EditMode +26 = 995 → 1021)
- ProfileService(+5, 1개 대체): 읽기 오류 → 3회 시도·대기 2회·ReadError·격리 없음·파일 그대로·Save/BeginRaid/CancelRaid 쓰기 0·재진입도 읽기 전용·UseStorage 뒤 원래 진행도(기존 `ReadError_TreatedAsCorrupt` 대체) / 일시 오류 2회 후 성공 → Loaded / ToProfile 예외 → Corrupt·격리·새 프로필·저장 가능 / ClearSaveFailure / 진입 토큰 표시·지움(BeginRaid·UseStorage) / 손상 알림 확인 뒤 ApplyInterruptedRaid → Status Loaded. HasNotice에 ReadError.
- FileSaveStorage(+5): save.json 없음 + .tmp → .tmp 읽기(.bak 아님)·다음 쓰기 정상 / .bak만 → Missing / .tmp만 격리 / 잠긴 파일 → Error / 잠긴 실제 파일에 ProfileService → ReadError, 원본·.bak 그대로, corrupt·tmp 없음.
- SaveSerializer(+1): 소총에 9mm 탄창 → 탄창 없음·Dropped 1·Unknown 0, 맞는 탄창은 유지.
- HideoutService(+7, 1개 대체): W2 저장 실패 → 머묾 → 두 번째는 이동(기존 `ReturnToMainMenu_SaveFails_StillLeaves` 대체) / 실패 뒤 성공 저장으로 이동 / 읽기 전용: 레이드 거부·메뉴 즉시 이동·쓰기 0 / W1 실패 표시 뒤 단계 거부는 LastSaveFailed false / Enter 토큰·레이드 시작·메뉴 복귀로 지움 / TryEnter 예외 → false·토큰 없음·흐름 표 비움 / 서비스 생성 예외 → false / TryEnter 정상.
- HideoutInventory(+1): `IsEntryComplete` — 미로드·로드만·다른 씬 토큰·레이드 시작 뒤는 false.
- SceneInventoryConfig(+4): InventoryConfig 에셋 있음, Hideout·Raid_CrimsonBasin 각각 직접 참조하는 InventoryConfigSO = `Data/Inventory/InventoryConfig.asset` 하나, 두 씬 같음(`AssetDatabase.GetDependencies`, 씬을 열지 않음).
- HideoutPresenter(+3): W2 저장 실패 메시지·재시도, 읽기 전용 메시지·알림 줄·레이드 거부 문구, 상인 목록 지연 생성(Refresh 2회 = 0회, 읽기 1회, Refresh 뒤 다시).

남은 것 / 사람 확인
- 읽기 오류·진입 예외 경로는 EditMode로만 확인(PlayMode 없음). 원하면 에디터에서 `save.json`을 다른 프로그램으로 잠근 채(예: PowerShell `[IO.File]::Open(path,'Open','ReadWrite','None')`) [은신처] → 알림·[레이드 시작] 거부·[메인 메뉴] 이동, 잠금 해제 후 재시작 → 원래 진행도.
- 에디터 [장비] 탭에서 퀵슬롯 키 배정(복제 액션)이 그대로 동작하는지, 레이드에서 퀵슬롯 키가 그대로인지 한 번 확인(PlayMode 77개는 통과).
- W2 저장 실패 시 두 번 눌러야 나가는 동작은 GDD 문구가 없다(TODO(GDD-?)) — 본문 §1.4 W2 줄과 CHANGELOG 반영 필요.
- 이전 4단계 사람 확인 목록(실제 마우스 루프, 레이드 중 Play 중지, 빌드 재실행 유지, 1080p 레이아웃)은 그대로.
