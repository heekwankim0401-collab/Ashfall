# M4 "레이드 루프" 설계 (unity-architect) — 준비안

기준: docs/GDD.md §1, §2, §3(RULE-01·02·03·06), §6, §8, §9, §12, §13, §14(RaidConfigSO), §15 M4·M5, §17, §18.
M1~M3 결정(CHANGELOG_GDD), M3_design.md(구현 메모 포함), HANDOVER_M3.md, 현재 코드(d352c56)를 전제로 한다.
**상태: 준비안.** §5 질문에 사용자가 답하기 전에는 구현하지 않는다. 각 항목의 "추천"을 기준으로 썼고, 답이 다르면 표시된 곳(⟨Qn⟩)만 바꾼다.
여기 없는 것은 만들지 않는다. 모호하면 `// TODO(GDD-?)` + 가장 단순한 구현.

---

## 0. 범위

**만든다 (M4 AC)**
- Crimson Basin 블록아웃(§8.1): 1km×1km Unity Terrain, POI 5곳, 엄폐용 외벽·잔해·차량 무더기(실내 없음), NavMesh.
- 탈출 EXT-01~04(§8.2)와 조건, 탈출 대기 카운트다운(HUD 허용 항목), 레이드 타이머 25분 → MIA(RULE-02).
- 사망/MIA/탈출(+포기 ⟨Q16⟩) 결과 화면(UI Toolkit), RULE-01 적용(사망·MIA면 반출 0, 소지품 전부 소실로 기록).
- 씬 흐름: Boot → MainMenu(새 UI: 레이드 시작/종료) → Raid_CrimsonBasin → (종료 연출) → MainMenu 결과 패널 → 메인 메뉴.
- 레이드용 적 배치: M3 AI(AI-SCV·AI-RDR)를 그대로 재사용, 씬 생성 메뉴가 고정 배치.
- (추천 포함 시) 지도 화면 `M`(RULE-06, 내 위치 없음) ⟨Q11⟩, ESC 일시정지 ⟨Q16⟩, 맵 상자 LootTableSO 굴림 ⟨Q17⟩.

**만들지 않는다 (경계만 표시)**
- M5: 세이브/로드, 보관함·상인·부랑자 키트, 은신처 씬, `raid_in_progress` 강제 종료 처리(§13.3, RULE-03), 반입 장비 선택 UI.
  → M5가 받을 경계: `RaidResult`(Core), `GameFlow.StartRaid/CompleteRaid`(Core), 반입 주입 지점(§1.6).
- M6: 재 폭풍, 팔레트·포스트·VFX, 지면 재질 발소리, 지형 텍스처. M7: AI-SNP/MUT/BOS, 방사능 구역 효과, 무기 교체.
- 적 런타임 스포너·무작위 배치(§1.7에서 사유). 적 리스폰. 로딩 화면.

**§8.3 동적 환경 판단:** M4에 필요한 것은 **없다.**
- 재 폭풍은 §15 M6 AC, 방사능 +1/s·STAT-RAD는 M7. `RaidSession.ElapsedSec`만 있으면 M6에서 발생 시각을 정할 수 있으므로 미리 훅을 만들지 않는다.
- "낮/밤 없음, 고정된 붉은 황혼"은 **시간 진행 시스템을 만들지 않는 것**으로 충족(씬 기본 Directional Light 고정, 색은 M6).
- 방사능 정유소는 이름·블록아웃만. 구역 볼륨·Toxic 표시도 만들지 않는다 ⟨Q21⟩.

---

## 1. 아키텍처

### 1.1 경계 요약 (Raid ↔ 다른 기능)

Raid 어셈블리는 `Ashfall.Core`만 참조한다(asmdef 이미 있음, 변경 없음).

| Raid가 필요한 것 / 주는 것 | 상대 | 방법 | 이유 |
|---|---|---|---|
| 플레이어 위치(탈출 구역 판정)·사망 여부 | Player | 기존 `IPlayerTarget`(Position, IsDead) 조회 | 매 프레임 동기 조회. 새 인터페이스 불필요 |
| 플레이어 사망 원인 | Player | 기존 이벤트 `PlayerDied(Cause)` | 이미 발행됨 |
| 플레이어를 스폰 지점에 놓기 | Player | ★Core `IPlayerPlacement.PlaceAt(pos, yaw)` (PlayerMotor 구현·등록) | CharacterController 이동은 끄고·옮기고·켜고 + `Physics.SyncTransforms` + 시선 yaw 설정이 Player 내부 일. Raid가 Transform을 직접 건드리면 경계 위반 |
| EXT-04 배낭 착용 여부 | Inventory | 기존 `IInventoryService.GetEquipped(Backpack)` | 이미 있음 |
| EXT-03 신호탄 보유·소모, 지도 보유(UI) | Inventory | ★`IInventoryService.Contains(def)`, `TryConsume(def)` 추가 | 동기 판정·결과가 필요 → 이벤트 불가 |
| 반입·반출 목록(RULE-01) | Inventory | ★Core 정적 `InventorySnapshot.Collect(IInventoryService, list)` | 기존 읽기 API(GetEquipped·StorageGrids)만으로 계산. M5 Hideout도 같은 함수를 쓴다 |
| EXT-02 전원 스위치 F 상호작용 | Inventory(F 레이캐스트) | ★Core `IWorldInteractable` + `InteractionKind.Activate` | 지금 `IInteractable`은 Inventory 안(인자 `PlayerInventoryBehaviour`)이라 Raid가 구현할 수 없다. F 판정·프롬프트는 한 곳(PlayerInventoryBehaviour)에 두고, Core 인터페이스도 잡게 확장 |
| 처치 수(결과 화면) | AI | 기존 이벤트 `EnemyKilled` 구독 | 이미 발행됨 |
| 탈출 카운트다운·조건 문구 | → UI | ★이벤트 `ExtractionStatusChanged` | 초 단위 값이 바뀔 때만 발행(초당 1회) |
| 레이드 시작/종료 | → UI, (M5 Audio 등) | ★이벤트 `RaidStarted`, `RaidEnded` | 여러 기능이 받을 사실 |
| 지도·남은 시간 | → UI | ★Core `IRaidInfo`(RaidController 등록) ⟨Q10·Q11⟩ | 화면을 열 때 동기로 읽음 |
| 포기(일시정지 메뉴) | UI → | ★이벤트 `RaidAbandonRequested` ⟨Q16⟩ | UI가 Raid를 모른다 |
| 결과 전달·씬 전환·세계 정지 | → GameManager | ★Core `GameFlow`(GameManager가 소유) | 씬을 넘어 살아야 하는 상태는 유일한 싱글톤에만 둔다 |

- Raid가 Player·Inventory·AI·UI 타입을 직접 보는 곳은 없다. 레지스트리 등록은 계속 Core 인터페이스만.
- `RaidController`는 씬 컴포넌트(정적 Instance 없음). 싱글톤은 `GameManager` 하나.

### 1.2 Raid 순수 로직 (EditMode 테스트 대상)

```
RaidSession ── 시간(ElapsedSec/RemainingSec, RULE-02) ── 종료 판정(탈출/사망/MIA/포기, 한 번만)
   │            스위치 상태(EXT-02), 처치 수
   ├─ ExtractionModel × 4 ── 활성 여부, 대기 진행(구역 안 + 조건 충족이면 감소, 아니면 초기화)
   │     └─ ExtractionRules.Evaluate(config, ctx) → ExtractionBlock (조건 판정, 상태 없음)
   ├─ ActiveExtractionPicker (스폰별 활성 탈출구) ⟨Q3⟩
   └─ RaidResultBuilder (종료 시 RaidResult 생성, RULE-01 적용 지점)
```
- **틱 순서(한 프레임):** ① 이미 끝났으면 무시 ② `Elapsed += dt` 전에 탈출 진행을 먼저 처리(같은 틱에 대기 완료와 시간 초과가 겹치면 **탈출 우선**) ③ 탈출 완료 → `Extracted` ④ `Elapsed ≥ timeLimitSec` → `MissingInAction`. 사망은 이벤트로 즉시 `Killed`.
- 종료는 한 번만(`IsOver` 이후 모든 입력 무시). 탈출 후 연출 중 피격·사망해도 결과는 바뀌지 않는다.
- 시간은 `Time.deltaTime`(scaled) — 일시정지 중 멈춘다.

### 1.3 씬 흐름

```
Boot ──(BootLoader)──► MainMenu ──[레이드 시작]──► Raid_CrimsonBasin
  ▲                       ▲    (결과 패널: PendingResult 있으면 먼저 표시 → [확인])
  │                       │
  │                       └── GameFlow.CompleteRaid(result) ◄── 종료 연출 endDelaySec(실시간, 세계 정지)
  │                                                               ◄── RaidEnded(탈출/사망/MIA/포기)
  M5: MainMenu → Hideout → Raid → Hideout (CompleteRaid의 목적지만 바뀜, 결과 패널은 Hideout이 표시)
```
- 결과 화면은 **레이드 씬을 내린 뒤 MainMenu에서** 보여준다 ⟨Q15⟩. 사유: §13.2 씬 목록에 결과 씬이 없고, M5에서는 은신처(메뉴형 UI)가 같은 결과를 받아 보관함에 반영해야 한다. 레이드 씬 안 결과 화면은 AI·탄이 계속 도는 문제를 정지로 막아야 하고 M5에서 옮겨야 한다.
- 종료 순간: `RaidEnded` 발행 → UI가 `ScreenKind.RaidEnd` 오버레이("탈출 성공"/"사망"/"작전 중 실종")를 띄움(→ 기존 `ScreenStateChanged` 경로로 모든 조작 차단) → `GameFlow.SetWorldPaused(true)`(timeScale 0: AI·탄·출혈 정지) → `endDelaySec` 실시간 대기 → `CompleteRaid`.
- 에디터에서 Raid 씬을 바로 Play해도 동작(PendingRequest 없음 = 기본값). 끝나면 MainMenu로 간다.
- 씬 전환은 `LoadSceneAsync(Single)`. 로딩 화면 없음(TODO(M6)).

### 1.4 GameManager 역할 확장 (최소)

`GameManager`(Core, 유일 싱글톤)에 **`GameFlow Flow` 하나만** 더한다. 세이브·보관함은 M5.

```csharp
// Core/Flow/GameFlow.cs — 순수 C#. 씬 로드·timeScale은 생성자 대리자로 받아 EditMode 테스트한다.
public enum GamePhase { None, MainMenu, Raid }            // M5: Hideout 추가

public sealed class GameFlow
{
    public GameFlow(Action<string> loadScene, Action<float> setTimeScale);

    public GamePhase Phase { get; private set; }
    public bool IsWorldPaused { get; private set; }
    // 레이드 결과. 결과 화면이 확인(Acknowledge)할 때까지 남는다. M5: 은신처가 보관함에 반영한 뒤 확인.
    public RaidResult PendingResult { get; private set; }

    // MainMenu → Raid_CrimsonBasin.
    // M5 경계: StartRaid(RaidRequest request) — 반입 Loadout(보관함에서 꺼낸 ItemInstance 목록),
    //          raid_in_progress=true 세이브 기록(§13.3)이 여기 붙는다. M4는 인자 없음.
    public void StartRaid();

    // 레이드 종료. 결과를 보관하고 MainMenu를 불러온다(정지 해제).
    // M5 경계: 목적지 Hideout, raid_in_progress=false. 두 번째 호출·Raid 아닌 단계에서는 무시(경고).
    public void CompleteRaid(RaidResult result);

    public void AcknowledgeResult();                       // PendingResult = null
    public void ReturnToMainMenu();                        // (예비) 정지 해제 + MainMenu
    public void SetWorldPaused(bool paused);               // timeScale 0/1. 바뀔 때만 대리자 호출
    public void OnSceneLoaded(string sceneName);           // Phase 갱신(이름으로), 정지 해제
}
```
- `GameManager.Awake`: `Flow = new GameFlow(name => SceneManager.LoadSceneAsync(name, Single), s => Time.timeScale = s)`, `SceneManager.sceneLoaded += (s, _) => Flow.OnSceneLoaded(s.name)`(OnDestroy에서 해지). `ResetStatics`에서 timeScale 1.
- 일시정지 주체가 하나(GameFlow)여야 일시정지 메뉴·종료 연출·씬 전환이 timeScale을 엉키지 않게 한다.

### 1.5 RULE-01 적용 지점

1. **유일한 판정 지점:** `RaidResultBuilder.Build` — `Outcome != Extracted`면 `CarriedOut`는 빈 목록, `Lost`는 종료 시점 소지품 전체(반입 + 레이드 중 획득). `Extracted`면 `CarriedOut` = 종료 시점 소지품 전체, `Lost` 빈 목록.
2. 사망 후 인벤토리 변경 차단은 M2의 `IsDeadLocked` 그대로(사망 후 줍기·버리기로 목록이 바뀌지 않음).
3. M4에는 보관함이 없으므로 다음 레이드는 항상 고정 키트로 시작한다 ⟨Q13·Q14⟩. 즉 M4의 RULE-01은 "결과 데이터·화면"으로 확인하고, 실제 소실은 M5에서 보관함이 `CarriedOut`만 받는 것으로 완성된다.
4. M5 경계: 반입 장비는 레이드 시작 시 보관함에서 빠지고(타르코프식), 탈출 시 `CarriedOut`만 돌아온다. 강제 종료(RULE-03)는 `raid_in_progress` 플래그로 같은 결과.

### 1.6 반입(Loadout) 경계

- M4: 플레이어 반입 = 씬의 `PlayerInventoryBehaviour.startingItems`(M2 시작 장비 그대로 + 지도 ⟨Q11·Q13⟩). 새 코드 없음.
- `RaidController.Start`에서 `InventorySnapshot.Collect`로 **반입 목록을 정의(ItemDefinitionSO) 목록으로 기록**(결과 화면 "반입"용). 인스턴스 참조가 아니라 정의를 남기는 이유: 탄창 탄 수·의약품 횟수가 레이드 중 바뀌고, 소모되면 사라진다.
- M5 경계(표시만): `RaidRequest.Loadout`(ItemInstance + 장착 슬롯/배치)을 `GameFlow`가 들고 있다가, 레이드 씬의 Inventory가 `startingItems` 대신 그것을 `TryEquipNew`/`TryAutoStore`로 넣는다. M4는 이 경로를 만들지 않는다.

### 1.7 레이드 적 스폰 (M3 AI 재사용)

- **씬 생성 메뉴가 고정 배치**(M3와 같은 방식: 적 루트 최상위, 분대 id, 순찰 경로, 엄폐 지점). 런타임 스포너 없음 ⟨Q12⟩.
  - 사유: Raid는 AI를 참조할 수 없어 Raid의 스포너는 `EnemyAgent`를 모른다. AI 쪽 스포너를 만들면 `AiDirector`의 Awake 1회 엄폐 수집·`Detach` 후 재등록 미지원(M3 리뷰 중간 5) 등 M3 코드를 고쳐야 한다. 고정 배치는 M3 코드 무변경이고 테스트가 결정적이다.
  - 이후 무작위화가 필요하면: Raid가 `RaidStarted(seed)`를 발행하고 AI의 스폰 그룹이 받아 하위 집합만 켜는 방식(M7 이후 검토, TODO).
- **수·구성(추천): 20명** = M3에서 측정한 구성 그대로 — SCV 분대 5개(3·3·2·2·2 = 12, 권총/SMG) + RDR 분대 4개(2×4 = 8, 소총). POI별 배치는 §2.3 표.
- 규칙: 모든 적 시작 위치는 플레이어 스폰 지점에서 **150m 이상**(시야 60m·소총 소음 120m 밖), POI 사이 거리 ≥ 250m라 한 곳의 총성이 맵 전체로 번지지 않는다.
- M3 코드 영향 점검:
  - 플레이어는 레이드 중 파괴·교체되지 않는다(사망해도 오브젝트 유지, `IsDead`로 T12). `PlaceAt`은 같은 오브젝트를 옮기므로 `IPlayerTarget` 재등록 없음 → HANDOVER의 우려 해당 없음.
  - `CoverIndex`는 선형 탐색. 맵 엄폐 300개 내외 × 프레임당 질의 1회는 충분(측정해서 1000개를 넘으면 균일 격자, TODO(M4→M7)).
  - 1km 거리의 적은 감각 갱신마다 콘·거리 판정만(레이 0). 20명 비용은 M3 측정과 같다.
  - 세계 정지(timeScale 0) 중 AI는 `Time.time` 기준이라 틱하지 않는다. 4단계에서 정지·재개 시 예외·NaN 없음 확인.

---

## 2. 맵 블록아웃 계획 (§8.1)

### 2.1 선택지 ⟨Q1⟩
| 안 | 내용 | 시간·위험 |
|---|---|---|
| **A (추천)** | 1km Terrain 전체 + POI 5곳 **저밀도** 블록아웃(POI당 기본 도형 20~40개), 탈출구 4곳, 도로 띠, 맵 경계 벽, 그 사이는 지형 기복 + 흩어진 잔해 | 생성 코드 1개 + 굽기 1회. §8.1 그대로 |
| B | 1km Terrain + 탈출·스폰과 관련된 POI 3곳(고속도로·송전탑·저수지)만, 나머지 2곳은 M6 | A보다 생성 코드 약 30% 적음. 정유소·마을이 빈 땅 |
| C | 축소(500m) 맵 | GDD 수정 필요(CHANGELOG). 비추천 |

### 2.2 Terrain
- `TerrainData`: size **(1000, 120, 1000)**, heightmapResolution **513**(≈1.95m/샘플; 1025는 굽기·용량 4배, 블록아웃엔 과함). 위치 (-500, 0, -500) → 원점이 맵 중앙(M1 방식).
- 높이 함수(EditorTools, 결정적 — 고정 시드 Perlin, 재실행 시 바이트 동일):
  - 분지 바닥 15m + 가장자리 상승(`max(|x|,|z|)` 380m → 500m에서 smoothstep +45m, 경사 ≈ 20°).
  - 기복: Perlin 진폭 ±4m, 파장 120m.
  - 송전탑 언덕: 가우시안 +35m(반경 90m). 말라붙은 저수지: -8m 웅덩이(반경 70m) + 남쪽 둑.
  - **평탄 패드**: POI·탈출 구역·스폰 지점·도로 띠는 목표 높이로 블렌딩(경사 0~5°). 블록아웃 도형은 패드 높이에 놓는다(`terrain.SampleHeight`).
- 지형 레이어(텍스처) 없음 — URP 기본 지형 재질(M1과 같음). 색·재질은 M6.
- 렌더 설정: `heightmapPixelError 5`, `basemapDistance 300`, `drawInstanced = true`, 나무·디테일 없음. 카메라 far clip 1000 그대로(안개 M6).
- 맵 경계: 가장자리 안쪽 ±495m에 보이지 않는 BoxCollider 벽 4개(높이 150m) ⟨Q20⟩. NavMesh 굽기에서는 제외(벽은 Environment 밖 `Bounds` 루트).
- 에셋: `Assets/_Project/Scenes/Raid_CrimsonBasin/Terrain-CrimsonBasin.asset` — 있으면 **SetHeights로 덮어써 GUID 유지**(지우고 다시 만들지 않는다). 예상 크기 0.5~1MB.

### 2.3 레이아웃 (월드 XZ, 원점 = 맵 중앙, +z 북) — 초안, 데이터는 `CrimsonBasinLayout.cs`
| ID | 이름 | 중심 | 내용(블록아웃) | 적(추천 20명) |
|---|---|---|---|---|
| POI-HWY | 붕괴된 고속도로 | (-60, -300) | 동서 300m 고가 상판 조각(높이 6m 박스)·무너진 경사 상판·차량 무더기 8곳, 남북 도로 띠(EXT-01까지) | SCV 3(순찰, 도로 따라) + RDR 2(차량 무더기) |
| POI-REF | 방사능 정유소 | (300, -60) | 원통 탱크 6(실린더, 진입 불가)·배관(얇은 박스)·외벽 구간 150×120m | RDR 2 + RDR 2 |
| POI-VIL | 매몰된 마을 외곽 | (-300, 80) | 반쯤 묻힌 집 외벽(지붕 없는 4면 벽 조각, 내부 진입 불가하게 안을 채운 박스)·담장 120×120m | SCV 3 + SCV 2 |
| POI-PYL | 송전탑 언덕 | (230, 300) | 언덕 위 송전탑 3기(가는 높은 박스)·변전 설비 박스·**전원 스위치(EXT-02)** ⟨Q5⟩ | RDR 2 |
| POI-RES | 말라붙은 저수지 | (-200, 330) | 웅덩이 바닥, 남쪽 둑(긴 벽), 좌초한 배·잔해 | SCV 2 + SCV 2 |
| EXT-01 | 남쪽 도로 | (0, -470) | 도로 끝 바리케이드, 구역 반경 8m | — |
| EXT-02 | 열차 신호소 | (470, -250) | 동쪽 가장자리 철로 둑 + 신호소 외벽, 반경 8m | — |
| EXT-03 | 헬기 착륙장 | (60, 460) | 20×20m 평탄 패드(낮은 박스 테두리), 반경 10m | — |
| EXT-04 | 하수 배출구 | (-470, 200) | 서쪽 가장자리 배수관 입구(큰 박스 + 구멍 모양 벽), 반경 5m | — |
| SP-W | 서쪽 스폰 | (-460, -150) | 평탄 패드 | 활성: EXT-01, EXT-02 (+EXT-03) ⟨Q3⟩ |
| SP-E | 동쪽 스폰 | (460, 120) | 평탄 패드 | 활성: EXT-01, EXT-04 (+EXT-03) |
| SP-N | 북쪽 스폰 | (-80, 470) | 평탄 패드 | 활성: EXT-01, EXT-02, EXT-04 |
- 흩어진 잔해·차량: POI 사이 길목에 60개 내외(엄폐, 이동 중 숨을 곳).
- 엄폐 지점(`CoverPoint`): M3 규칙 그대로(낮은 벽 앞뒤 Low, 높은 벽 앞뒤 High + peek, 차량 4방향 Low). 예상 250~350개.
- 블록아웃 도형은 정적 플래그(`StaticEditorFlags.BatchingStatic | NavigationStatic`)로 정적 배칭. 재질은 M3처럼 회색 기본 + 탈출 구역 표시용 반투명 재질 1개(그레이박스, M6에서 교체).
- 루트 상자: POI마다 3~5개(총 약 20개), `LootContainer` + 루트 테이블 ⟨Q17⟩. 신호탄 고정 상자 2개 ⟨Q7⟩.

### 2.4 NavMesh 굽기
- M3 `BakeNavMesh`와 같은 코드(NavMeshSurface, `CollectObjects.Children`, PhysicsColliders — TerrainCollider 포함, `CopySerialized`로 GUID 유지). 저장: `Scenes/Raid_CrimsonBasin/NavMesh-Environment.asset`.
- 범위: `Environment` 루트(Terrain + 블록아웃) 전체 = 1000×~130×1000. 경계 벽은 제외.
- 예상(기본 voxel = 반지름 0.5/3 ≈ 0.167m, 타일 256 voxel ≈ 43m → 약 24×24 = 576타일): **굽기 1~5분, 에셋 2~8MB(바이너리)**. 2d 단계가 사본에서 먼저 재서 보고한다.
  - 5분을 넘거나 10MB를 넘으면: `overrideVoxelSize = true, voxelSize = 0.25`(타일 64m, 약 256타일)로 낮춘다(블록아웃 틈 ≥ 1m라 충분). `minRegionArea` 2로 지붕 위 작은 섬 제거.
- 확인: 굽기 직후 모든 스폰·탈출 구역·적 시작 위치 `SamplePosition` 성공, 스폰마다 활성 탈출구까지 `NavMesh.CalculatePath` = Complete(플레이어는 CharacterController지만 도달 가능성 근사).

### 2.5 성능 예산 (M4 추가분)
- 렌더: Terrain 1장(인스턴싱) + 정적 배칭 블록아웃 ~400개 + 적 20명(캡슐). GTX 1660 1080p에서 문제될 규모가 아니지만 **배치모드는 GPU를 재지 않는다** → 사람 확인 필수(§7).
- CPU: M3 측정(AI 틱 평균 0.09ms, 투사체 0.02ms)에 Terrain 컬링·물리(TerrainCollider) 추가. 자동 확인: Raid 씬에서 20명 중 다수 교전 시 AI 틱·프레임 중앙값(M3 기준과 같은 임계).
- 메모리: TerrainData ~1MB, NavMesh ≤ 8MB, 씬 YAML 2~4MB(오브젝트 ~1500개).

---

## 3. 탈출·타이머·결과

### 3.1 탈출 조건 (GDD 그대로 + 해석)
| ID | GDD | 구현 | 모호한 부분 → 질문 |
|---|---|---|---|
| EXT-01 남쪽 도로 | 항상 열림, 10초 | 조건 없음. 활성일 때 구역 안 10초 | "항상 열림" vs "2~3개만 활성" → **EXT-01은 모든 스폰에서 활성**으로 해석 ⟨Q3⟩ |
| EXT-02 열차 신호소 | 전원 스위치 작동 필요, 15초 | `requiredSwitchId = "SW-PWR"`. 스위치(F, `IWorldInteractable`)를 켜면 레이드 끝까지 켜짐. 꺼져 있으면 Blocked(NoPower) | 스위치 위치·지속·작동 시간 ⟨Q5⟩ |
| EXT-03 헬기 착륙장 | 레이드 15분 이후만, 20초, 신호탄 소모 | `availableAfterSec = 900`(경과 ≥ 15:00), `consumedItem = 신호탄`. 둘 다 충족 시 20초, **완료 순간 1개 소모** 후 탈출 ⟨Q6⟩. 대기 중 신호탄을 버리면 초기화 | 소모 시점, 신호탄 획득처 ⟨Q6·Q7⟩ |
| EXT-04 하수 배출구 | 배낭 미착용 시만, 7초 | `forbidsBackpack = true`. 매 틱 `GetEquipped(Backpack) == null` 확인, 대기 중 배낭을 장착하면 초기화 | 조끼는 허용(문구 그대로) |

공통 규칙(추천 ⟨Q8⟩)
- 구역 = 중심에서 **수평 반경 `radiusM`** 안 + 높이 차 ≤ 3m(`ExtractionRules.IsInZone`). 물리 트리거 대신 위치 판정 — 순수 로직이라 EditMode 테스트 가능, 레이어 불필요.
- 대기: 구역 안 ∧ 조건 충족이면 `remaining -= dt`, 구역을 나가거나 조건이 깨지면 **처음부터 다시**(`waitSec`). 구역 안에서 움직이기·사격·피격은 취소하지 않는다.
- 비활성 탈출구: 들어가도 아무 표시 없음(지도로만 안다, RULE-06) ⟨Q4⟩.
- 대기 중 사망 → Killed. 대기 중 25:00 도달 → MIA(같은 틱 완료면 탈출 우선, §1.2).
- 조건 미충족 문구(구역 안에서만): "전원이 꺼져 있다" / "아직 헬기가 오지 않는다" / "신호탄이 필요하다" / "배낭을 메고는 들어갈 수 없다" ⟨Q9⟩.

### 3.2 활성 탈출구 (§8.2 "스폰 위치 기준 2~3개만 활성 표시")
- 해석(추천 ⟨Q3⟩): **스폰마다 미리 정한 활성 세트**(`RaidConfigSO.spawnRules`) — EXT-01 항상 포함 + 그 스폰에서 먼 1~2개. 스폰은 3곳 중 무작위 1곳 ⟨Q2⟩. "표시"는 지도 화면에서만(⟨Q11⟩).
- 비활성 탈출구는 **실제로 작동하지 않는다**("활성"을 기능까지 포함한 의미로 해석).
- `spawnRules`가 비어 있으면(대안 B) EXT-01 + 나머지 중 무작위 1~2개(`minActive 2`, `maxActive 3`).
- 시드: `RaidController.seed`(0 = 매번 무작위). 테스트는 고정.

### 3.3 레이드 타이머·MIA (RULE-02)
- `RaidConfigSO.timeLimitSec = 1500`(§14 기본값 그대로). `RaidController.Start`에서 시작(스폰 배치 직후).
- HUD에 남은 시간 표시 없음(§12). 지도 화면에 **분 단위 대략치**("남은 시간: 약 12분") ⟨Q10⟩ — EXT-03(15분 이후) 판단에 필요.
- 0 도달 → `MissingInAction` = 사망 처리(RULE-01 적용). 플레이어 몸은 죽이지 않는다(결과만 사망 취급).
- 사람 확인·디버그: `RaidController`에 `[ContextMenu("Debug/시간 +5분")]`(에디터 인스펙터 메뉴, 입력 추가 없음)과 `RaidConfigSO` 값 조정.

### 3.4 결과 화면 (UI Toolkit, MainMenu 씬)
- 표시 ⟨Q18⟩: 제목(탈출 성공 — {탈출구 이름} / 사망 — {부위} / 작전 중 실종(MIA) / 레이드 포기), 레이드 시간(분:초), 처치 수, **반입** 목록, **반출** 목록(탈출 시), **소실** 목록(사망·MIA·포기 시). 아이템은 표시 이름 + 같은 정의 묶음 수량(`군용 소총 탄창 ×3`). 탄 수·HP 수치는 표시하지 않는다(정보 희소성 원칙 유지; 레이드는 끝났지만 일관성).
- [확인] → `GameFlow.AcknowledgeResult()` → 메인 메뉴 버튼(레이드 시작 / 종료).
- 레이드 씬 종료 오버레이(`ScreenKind.RaidEnd`): 한 줄 문구만, `endDelaySec`(3초, TODO(GDD-?)) 뒤 자동 전환. Esc로 닫히지 않는다.

### 3.5 ESC 일시정지 ⟨Q16⟩ (추천: 실제 정지)
- 화면이 모두 닫혀 있을 때 Esc → `ScreenKind.Pause`: [계속] / [레이드 포기]. 열리면 `GameFlow.SetWorldPaused(true)`, 닫히면 false. Esc 다시 → 닫기.
- 일시정지 중 Tab·H·M은 무시. 사망 후·종료 오버레이 중에는 열리지 않는다.
- [레이드 포기] → 확인 한 번 더("레이드를 포기하면 가져온 장비를 모두 잃는다") → `RaidAbandonRequested` → `Abandoned`(RULE-01은 사망과 같게; RULE-03 "강제 종료 = 사망"과 같은 취지).

---

## 4. 파일·클래스·공개 API

### 4.1 Core 변경 (`namespace Ashfall.Core`)
| 파일 | 변경 |
|---|---|
| `SceneNames.cs` | `RaidCrimsonBasin = "Raid_CrimsonBasin"` 추가 |
| `GameManager.cs` | `public GameFlow Flow { get; private set; }`, sceneLoaded 연결·해지, 주석의 "M4/M5에서 더한다" 갱신 |
| `Flow/GameFlow.cs` (새) | §1.4 |
| `Raid/RaidResult.cs` (새) | `RaidOutcome`, `RaidResult` (아래) |
| `Items/InventorySnapshot.cs` (새) | 아래 |
| `Services/IInventoryService.cs` | `Contains`, `TryConsume` 추가 |
| `Services/IWorldInteractable.cs` (새) | 아래 |
| `Services/IPlayerPlacement.cs` (새) | 아래 |
| `Services/IRaidInfo.cs` (새) | `MapMarker`, `IRaidInfo` ⟨Q10·Q11: 둘 다 "없음"이면 만들지 않음⟩ |
| `Events/GameEvents.cs` | `RaidStarted`, `ExtractionBlock`, `ExtractionStatusChanged`, `RaidEnded`, `RaidAbandonRequested` |
| `UiEnums.cs` | `ScreenKind`에 `Map`, `Pause`, `RaidEnd` 추가(끝에 추가 — 직렬화 값 유지) |
| `Items/InventoryEnums.cs` | `InteractionKind.Activate` 추가 |
| `Services/ServiceRegistry.cs` | 주석의 등록 대상 목록 갱신(코드 변경 없음) |

```csharp
// Raid/RaidResult.cs
public enum RaidOutcome { Extracted, Killed, MissingInAction, Abandoned }

// 레이드 한 판의 결과 (§2 코어 루프, RULE-01). Raid가 만들고 GameFlow가 씬을 넘어 들고 간다.
// M5 경계: 은신처가 CarriedOut을 보관함에 넣는다. 사망·MIA·포기면 CarriedOut은 비어 있다.
public sealed class RaidResult
{
    public RaidResult(RaidOutcome outcome, string extractionId, string extractionName,
        bool hasDeathCause, BodyPart deathCause, float durationSec, int enemiesKilled,
        IReadOnlyList<ItemDefinitionSO> broughtIn, IReadOnlyList<ItemInstance> carriedOut,
        IReadOnlyList<ItemDefinitionSO> lost);

    public RaidOutcome Outcome { get; }
    public bool Survived => Outcome == RaidOutcome.Extracted;
    public string ExtractionId { get; }                       // 탈출이 아니면 null
    public string ExtractionName { get; }
    public bool HasDeathCause { get; }                        // Killed일 때
    public BodyPart DeathCause { get; }
    public float DurationSec { get; }
    public int EnemiesKilled { get; }
    public IReadOnlyList<ItemDefinitionSO> BroughtIn { get; } // 반입(시작 시점, 표시용)
    public IReadOnlyList<ItemInstance> CarriedOut { get; }    // 반출(탈출 시 종료 시점 소지품 실물)
    public IReadOnlyList<ItemDefinitionSO> Lost { get; }      // 소실(사망·MIA·포기 시 종료 시점 소지품, 표시용)
}

// Items/InventorySnapshot.cs
public static class InventorySnapshot
{
    // 장착 7슬롯(EquipSlot 순) → StorageGrids(순서·배치 순)의 아이템을 into에 더한다. 더한 수를 돌려준다.
    // 열린 루트(ActiveLoot)는 제외. 같은 인스턴스는 한 번만. 무기의 장착 탄창은 무기에 포함(따로 넣지 않음).
    // 인자 null → 0. TODO(GDD-?): 재장전 중 손에 든 탄창(인벤토리 밖)은 빠진다.
    public static int Collect(IInventoryService inventory, List<ItemInstance> into);
}

// IInventoryService 추가
bool Contains(ItemDefinitionSO definition);   // 장착 슬롯·저장 공간에 같은 정의가 있나 (루트 제외)
bool TryConsume(ItemDefinitionSO definition); // 저장 공간에서 하나 제거 (잠김 제외, 사망 후 거부). 성공 시 InventoryChanged

// Services/IWorldInteractable.cs
// F 상호작용 대상 중 Inventory 밖 기능이 만드는 것 (EXT-02 전원 스위치). PlayerInventoryBehaviour가 레이캐스트로 찾는다.
public interface IWorldInteractable
{
    InteractionKind Kind { get; }
    string TargetName { get; }
    bool IsAvailable { get; }   // false면 프롬프트 없음 (이미 켠 스위치)
    void Interact();
}

// Services/IPlayerPlacement.cs — Player(PlayerMotor)가 구현·등록. 레이드 시작 스폰 배치용.
public interface IPlayerPlacement
{
    void PlaceAt(Vector3 feetPosition, float yawDeg);   // 이동 상태·발소리 누적 초기화, 시선 수평
}

// Services/IRaidInfo.cs — Raid(RaidController)가 등록. 지도·일시정지 화면이 열 때 읽는다.
public readonly struct MapMarker
{
    public readonly string Id;
    public readonly string DisplayName;
    public readonly Vector2 PositionXZ;
    public MapMarker(string id, string displayName, Vector2 positionXZ);
}
public interface IRaidInfo
{
    float ElapsedSec { get; }
    float RemainingSec { get; }
    Rect MapAreaXZ { get; }                              // 지도에 그릴 월드 범위
    IReadOnlyList<MapMarker> PointsOfInterest { get; }
    IReadOnlyList<MapMarker> ActiveExtractions { get; }  // RULE-06: 지도에서만. 내 위치는 주지 않는다(멤버 추가 금지)
}

// Events/GameEvents.cs 추가 (── M4: Raid → 다른 기능 ──)
public readonly struct RaidStarted : IGameEvent { public readonly string SpawnId; public readonly float TimeLimitSec; }
public enum ExtractionBlock { None, NoPower, TooEarly, MissingItem, BackpackEquipped }
// 탈출 구역 HUD (§12 탈출 카운트다운). InZone=false면 숨김. 초 단위 값·상태가 바뀔 때만 발행.
public readonly struct ExtractionStatusChanged : IGameEvent
{
    public readonly bool InZone;
    public readonly string DisplayName;
    public readonly ExtractionBlock Block;   // None이면 카운트다운 중
    public readonly int SecondsLeft;         // 올림 정수
}
public readonly struct RaidEnded : IGameEvent { public readonly RaidOutcome Outcome; public readonly string ExtractionName; }
// ── UI → Raid
public readonly struct RaidAbandonRequested : IGameEvent { }
```
(생성자는 기존 이벤트처럼 필드 순서대로.)

### 4.2 Raid (`Ashfall.Raid`, `Scripts/Raid/`)
| 파일 | 종류 | 내용 |
|---|---|---|
| `Data/RaidConfigSO.cs` | SO | §14 + 추가(아래). 에셋 `Data/Raid/RaidConfig.asset` |
| `Data/ExtractionPointConfig.cs` | `[Serializable] struct` | §14 `ExtractionPointConfig` |
| `Data/SpawnRule.cs` | `[Serializable] struct` | 스폰별 활성 탈출구 |
| `Logic/ExtractionRules.cs` | static | 조건 판정·구역 판정 |
| `Logic/ExtractionModel.cs` | class | 탈출구 하나의 상태 |
| `Logic/ActiveExtractionPicker.cs` | static | 스폰·활성 탈출구 선택 |
| `Logic/RaidSession.cs` | class | §1.2 |
| `Logic/RaidResultBuilder.cs` | static | RULE-01 |
| `Runtime/RaidController.cs` | MonoBehaviour | 씬 브리지(아래) |
| `Runtime/ExtractionZone.cs` | MonoBehaviour | id, 기즈모(반경은 config) |
| `Runtime/PlayerSpawnPoint.cs` | MonoBehaviour | id, 기즈모 |
| `Runtime/PowerSwitch.cs` | MonoBehaviour, `IWorldInteractable` | switchId, displayName → `controller.ActivateSwitch` |
| `Runtime/PointOfInterest.cs` | MonoBehaviour | id, displayName (지도 ⟨Q11⟩) |

```csharp
[Serializable]
public struct ExtractionPointConfig
{
    public string id;                  // "EXT-01" — 씬 ExtractionZone.id와 같아야 한다
    public string displayName;
    public float waitSec;              // §8.2 소요
    [Tooltip("구역 수평 반경 (m). TODO(GDD-?)")] public float radiusM;
    [Tooltip("켜져 있어야 하는 스위치 id. 빈 문자열 = 없음 (EXT-02)")] public string requiredSwitchId;
    [Tooltip("레이드 경과 이 시간 이후만 (초). 0 = 제한 없음 (EXT-03 = 900)")] public float availableAfterSec;
    [Tooltip("대기 완료 시 소모하는 아이템. null = 없음 (EXT-03 신호탄)")] public ItemDefinitionSO consumedItem;
    [Tooltip("배낭을 메고 있으면 불가 (EXT-04)")] public bool forbidsBackpack;
}

[Serializable]
public struct SpawnRule
{
    public string spawnId;                      // 씬 PlayerSpawnPoint.id
    public string[] activeExtractionIds;        // ⟨Q3⟩ 비면 무작위 규칙
}

[CreateAssetMenu(menuName = "Ashfall/Raid Config", fileName = "RaidConfig")]
public class RaidConfigSO : ScriptableObject
{
    [Tooltip("RULE-02 레이드 제한 시간 (초)")] public float timeLimitSec = 1500f;
    public ExtractionPointConfig[] extractions;
    public SpawnRule[] spawnRules;
    [Tooltip("spawnRules가 없을 때 활성 탈출구 수 (§8.2 2~3)")] public Vector2Int randomActiveCount = new Vector2Int(2, 3);
    [Tooltip("항상 활성 (§8.2 EXT-01 항상 열림 해석)")] public string[] alwaysActiveIds = { "EXT-01" };
    [Tooltip("종료 오버레이 시간 (실시간 초). TODO(GDD-?)")] public float endDelaySec = 3f;
    [Tooltip("구역 높이 허용 (m). TODO(GDD-?)")] public float zoneHeightToleranceM = 3f;
}

public static class ExtractionRules
{
    public static bool IsInZone(Vector3 player, Vector3 center, float radiusM, float heightToleranceM);
    // 조건만 본다(구역·활성은 호출자). 순서: 전원 → 시간 → 배낭 → 아이템 (문구 우선순위)
    public static ExtractionBlock Evaluate(in ExtractionPointConfig config, float elapsedSec,
        bool switchOn, bool hasBackpack, bool hasConsumedItem);
}

public enum ExtractionPhase { Inactive, Idle, Blocked, CountingDown, Completed }

public sealed class ExtractionModel
{
    public ExtractionModel(ExtractionPointConfig config, Vector3 position, bool active);
    public ExtractionPointConfig Config { get; }
    public Vector3 Position { get; }
    public bool IsActive { get; }
    public ExtractionPhase Phase { get; }
    public ExtractionBlock Block { get; }
    public float RemainingSec { get; }
    // 구역 밖 → Idle(초기화), 안 + block != None → Blocked(초기화), 안 + None → 감소. 0 도달 틱에 true(Completed).
    public bool Tick(float dt, bool inZone, ExtractionBlock block);
}

public static class ActiveExtractionPicker
{
    public static int PickSpawnIndex(int spawnCount, System.Random random);
    // spawnRules에 spawnId가 있으면 그 목록, 없으면 alwaysActive + 나머지에서 randomActiveCount 범위가 되게 무작위.
    public static int Pick(RaidConfigSO config, string spawnId, System.Random random, List<string> into);
}

public sealed class RaidSession
{
    public RaidSession(RaidConfigSO config, IReadOnlyList<ExtractionModel> extractions);
    public float ElapsedSec { get; }
    public float RemainingSec { get; }
    public bool IsOver { get; }
    public RaidOutcome Outcome { get; }                     // IsOver일 때만 의미
    public ExtractionModel CompletedExtraction { get; }
    public ExtractionModel CurrentZone { get; }             // 플레이어가 있는 활성 구역 (없으면 null)
    public bool HasDeathCause { get; }
    public BodyPart DeathCause { get; }
    public int EnemiesKilled { get; }
    public IReadOnlyList<ExtractionModel> Extractions { get; }

    public event Action Ended;

    public bool IsSwitchOn(string switchId);
    public bool ActivateSwitch(string switchId);            // 처음 켜면 true
    public void RecordKill();
    // 한 프레임 (§1.2 순서). inventory는 배낭·아이템 판정과 완료 시 소모(TryConsume)에 쓴다. null이면 배낭 없음·아이템 없음.
    public void Tick(float dt, Vector3 playerPosition, IInventoryService inventory);
    public void EndByDeath(BodyPart cause);
    public void Abandon();
    public void AdvanceTime(float seconds);                 // 디버그 메뉴·테스트용 (종료 판정은 다음 Tick)
}

public static class RaidResultBuilder
{
    // RULE-01 적용 지점 (§1.5). endItems = 종료 시점 InventorySnapshot.
    public static RaidResult Build(RaidSession session, IReadOnlyList<ItemDefinitionSO> broughtIn, IReadOnlyList<ItemInstance> endItems);
}
```

`RaidController` (씬당 1개, 정적 Instance 없음)
- 직렬화: `config`(RaidConfigSO), `seed`(int, 0 = 무작위).
- Awake: 씬의 `ExtractionZone`·`PlayerSpawnPoint`·`PowerSwitch`·`PointOfInterest` 수집(id 중복·config 불일치 시 LogError), OnEnable: `IRaidInfo` 등록, `PlayerDied`·`EnemyKilled`·`RaidAbandonRequested` 구독.
- Start: 서비스 조회(`IPlayerTarget`·`IPlayerPlacement`·`IInventoryService`, 없으면 LogError 후 비활성) → 스폰 선택 → `PlaceAt` → 활성 탈출구 선택 → `RaidSession` 생성 → 반입 스냅샷(정의 목록) → `RaidStarted` 발행.
- Update: `session.Tick(Time.deltaTime, target.Position, inventory)` → 구역 상태가 바뀌거나 초 값이 바뀌면 `ExtractionStatusChanged`. 할당 없음(문자열·LINQ 금지; 이벤트는 struct).
- 종료(`session.Ended`): 종료 스냅샷 → `RaidResultBuilder.Build` → `RaidEnded` 발행 → `Flow.SetWorldPaused(true)` → 코루틴 `WaitForSecondsRealtime(endDelaySec)` → `Flow.CompleteRaid(result)`. `GameManager.Instance` 없으면(EditMode 등) 결과만 보관.
- 공개(테스트·디버그): `Session`, `SpawnId`, `ActiveExtractionIds`, `LastResult`, `ActivateSwitch(id)`, `[ContextMenu]` 시간 +5분 / 즉시 MIA.

### 4.3 Player (`Ashfall.Player`)
| 파일 | 변경 |
|---|---|
| `PlayerMotor.cs` | `IPlayerPlacement` 구현·등록(OnEnable/OnDisable, 기존 IPlayerTarget과 같은 자리). `PlaceAt`: controller off → 위치·yaw → on → `Physics.SyncTransforms()`, 시선 pitch 0, 발소리 누적 `Reset()` |

### 4.4 Inventory (`Ashfall.Inventory`)
| 파일 | 변경 |
|---|---|
| `PlayerInventory.cs` | `Contains`, `TryConsume`(저장 공간 그리드에서 첫 번째 같은 정의, 잠김 제외, `IsDeadLocked` 거부, 퀵슬롯 참조 정리, `Changed`) |
| `PlayerInventoryBehaviour.cs` | 레이캐스트 대상에 Core `IWorldInteractable`(IsAvailable일 때) 추가. F → Inventory `IInteractable`이면 기존, 아니면 `IWorldInteractable.Interact()`. 상호작용 후 캐시 무효화(스위치 IsAvailable 변화 반영) |
| `LootContainer.cs` | ⟨Q17⟩ `[SerializeField] LootTableSO lootTable`, `int seed`(0 = 무작위): 처음 열 때 `contents` + `LootRoller.Roll` 결과. 주석의 `TODO(M4)` 해소 |

### 4.5 UI (`Ashfall.UI`)
| 파일 | 내용 |
|---|---|
| `GameUIPresenter.cs` | 생성자에 `GameFlow flow`(null 허용, 테스트) 추가. `ExtractionStatusChanged` → `ExtractionHudModel`, `RaidEnded` → `Screens.ShowRaidEnd()`, Esc(`Cancel`): 화면 없음 → Pause, Pause → 닫기. `ToggleMap()`(지도 아이템 보유 시). Pause 진입/해제 → `flow.SetWorldPaused`. `RequestAbandon()` → 확인 단계 → `RaidAbandonRequested` |
| `ScreenStateModel.cs` | `Map`·`Pause`·`RaidEnd` 처리: Pause 중 다른 화면 불가, `ShowRaidEnd()`는 사망 후에도 열리고 이후 닫히지 않음(IsDead·CloseAll 무시) |
| `ExtractionHudModel.cs` (새, 순수) | 이벤트 → 문구(`"탈출까지 7"` / 조건 문구 ⟨Q9⟩), InZone=false면 null |
| `MapScreenModel.cs` (새, 순수) | `IRaidInfo` → 정규화 좌표 마커 목록 + 남은 시간 분 단위 문구 ⟨Q10·Q11⟩ |
| `RaidResultViewModel.cs` (새, 순수) | `RaidResult` → 제목·시간·처치 수·아이템 묶음 줄(정의별 수량) |
| `MainMenuPresenter.cs` (새, 순수) | `GameFlow` → 결과 패널 표시 여부, 시작·확인·종료 명령 |
| `HudView.cs` | 탈출 카운트다운 라벨(`hud-extract`) |
| `PauseScreenView.cs`, `MapScreenView.cs`, `RaidEndOverlayView.cs` (새) | 뷰 |
| `GameUIRoot.cs` | 새 뷰·Map 입력 연결, 커서(Pause·Map·RaidEnd도 열린 화면 취급 — 기존 규칙 그대로) |
| `MainMenuRoot.cs`, `RaidResultView.cs` (새) | MainMenu 씬 UIDocument 진입점. 커서 해제·표시, UI 맵 Enable(M2: EventSystem 없이 Input System UI 맵) |
| `UiText.cs` | 새 문구, `InteractionVerb(Activate) = "작동"`(지금은 OpenContainer 외 전부 "줍기"라 반드시 고친다) |
| `HudConfigSO.cs` | `mapItem`(ItemDefinitionSO) ⟨Q11⟩ |
| `Art/UI/GameUI.uxml`·`Ashfall.uss` | 새 요소, `Art/UI/MainMenu.uxml` (새) |

### 4.6 EditorTools (`Ashfall.EditorTools`)
| 파일 | 내용 |
|---|---|
| `CrimsonBasinLayout.cs` (새, 1단계) | §2.3 좌표·크기·적 분대·경로 상수(레벨 데이터, 게임 수치 아님) |
| `M4DataSetup.cs` (새, 2c) | 메뉴 `Ashfall/Setup/Create M4 Data`, 배치 `CreateAll`. 먼저 `M3DataSetup.CreateData()`. `Data/Raid/RaidConfig.asset`(EXT-01~04, spawnRules), 아이템 `Data/Items/Raid/ITM-FLR-01`(신호탄)·`ITM-MAP-01`(지도) ⟨Q7·Q11⟩, 루트 테이블 `Data/Loot/LT-CRATE.asset` ⟨Q17⟩, `HudConfig.mapItem` 기록. 멱등 |
| `M4MapSetup.cs` (새, 2d) | 메뉴 `Ashfall/Setup/Create Raid Scene (Crimson Basin)`, 배치 `CreateAll`: 데이터 → Terrain → Environment 블록아웃·엄폐 → NavMesh → 관리자(ProjectileSystem·AiDirector·RaidController) → 탈출·스폰·스위치·POI·상자 → 플레이어(`M2SceneSetup.CreatePlayer` + 지도 아이템 추가) → GameUI → 적 → 저장. 매번 덮어씀(GUID 유지) |
| `M4EnemySetup.cs` (새, 2d) | M3SceneSetup의 적 생성(`CreateEnemy`·분대·경로)을 쓰기 위해 M3SceneSetup의 해당 함수를 `internal static`으로(동작 불변) |
| `M4SceneSetup.cs` (새, 3) | 메뉴 `Ashfall/Setup/Create M4 Scenes`, 배치 `CreateAll`: `M4MapSetup.CreateAll` + MainMenu 씬 재생성(카메라 + UIDocument(MainMenu.uxml) + MainMenuRoot) + 빌드 설정 Boot → MainMenu → Raid_CrimsonBasin |
- MainMenu 재생성 주의: M0 `SceneSetup.CreateM0Scenes`는 "있으면 건너뜀"이라 그대로 두고, M4 메뉴가 같은 경로에 덮어쓴다(.meta GUID 유지, 빌드 설정 GUID 불변).
- Raid 씬은 생성 메뉴 소유. M6에서 손 편집을 시작하면 메뉴 재실행을 중단한다(문서화).

### 4.7 asmdef 변경
| asmdef | 변경 |
|---|---|
| `Ashfall.Core`, `Ashfall.Raid`(Core만), `Ashfall.Player`, `Ashfall.Inventory`, `Ashfall.AI`, `Ashfall.Weapons` | 없음 |
| `Ashfall.UI` | 없음(Core + InputSystem 이미) |
| `Ashfall.EditorTools` | `+ Ashfall.Raid` |
| 테스트 2개 | 없음(이미 Raid 포함) |
새 asmdef 없음. 기능끼리 참조 추가 없음. Raid는 패키지 참조 불필요(NavMesh 미사용, Terrain은 엔진 모듈 — Raid는 Terrain도 쓰지 않음).

### 4.8 입력 변경
- `InputSystem_Actions.inputactions`(JSON 텍스트): Player 맵에 `Map`(Button, `<Keyboard>/m`) 추가 ⟨Q11; 지도 제외면 없음⟩. §12.1 키 표 그대로.
- Esc 일시정지는 기존 UI 맵 `Cancel` 재사용(새 액션 없음).
- 스위치는 기존 `Interact`(F).

---

## 5. 사용자 결정이 필요한 질문

| # | 질문 | 선택지 | 추천 |
|---|---|---|---|
| Q1 | M4 블록아웃 범위 | A 1km 전체 + POI 5곳 저밀도 / B 1km + POI 3곳(나머지 M6) / C 500m 축소(GDD 수정) | **A** |
| Q2 | 플레이어 스폰 | A 고정 3곳 중 무작위 / B 1곳 고정 / C 메뉴에서 선택 | **A** (스폰 기준 활성 규칙이 의미 있으려면 여러 곳) |
| Q3 | 탈출구 활성 규칙("2~3개만 활성" vs EXT-01 "항상 열림") | A 스폰별 고정 세트, EXT-01 항상 포함(§2.3 표) / B EXT-01 + 나머지 중 무작위 1~2 / C M4는 4개 모두 활성 | **A** |
| Q4 | 비활성 탈출구에 들어갔을 때 | A 표시 없음(지도로만) / B "닫혀 있다" 문구 | **A** |
| Q5 | EXT-02 전원 스위치 | 위치: A 송전탑 언덕(신호소에서 약 600m) / B 신호소 옆. 작동: F 1회, 레이드 끝까지 유지 / 일정 시간 후 꺼짐 | **A + 유지** |
| Q6 | EXT-03 신호탄 소모 시점 | A 20초 대기 완료 순간 자동 소모 / B 구역에서 F로 발사 시 즉시 소모, 이후 그 레이드 동안 호출 유지 / C 발사 시 소모, 구역을 나가면 다시 필요 | **A** (단순, 새 상호작용 없음). 긴장감을 원하면 B |
| Q7 | 신호탄 획득처 | A 고정 상자 2곳 + 상자 테이블 낮은 가중치 / B 시작 키트에 1개 / C 루트 테이블만(무작위) | **A** |
| Q8 | 탈출 대기 취소 | A 구역 이탈 시 초기화, 이동·피격 허용 / B 이탈 시 일시정지(진행 유지) / C 피격도 초기화(새 피격 이벤트 필요) | **A** |
| Q9 | 구역 안 조건 미충족 문구(§12 HUD 목록 밖) | A 짧은 이유 문구 표시 / B 표시 없음(카운트다운이 안 시작될 뿐) | **A** (아니면 EXT-02~04가 왜 안 되는지 알 수 없음) |
| Q10 | 남은 레이드 시간 표시 | A HUD 없음, 지도 화면에 분 단위 대략치 / B 어디에도 없음 / C 5분·1분 전 경고 문구 | **A** |
| Q11 | 지도(M, RULE-06)를 M4에 | A 포함: 정적 지도(POI·활성 탈출구·남은 시간, 내 위치 없음), 지도 아이템 소지 시만, 시작 키트에 포함 / B 아이템 없이 M 항상 / C 제외(그러면 Q3-C 권장) | **A** |
| Q12 | 적 수·배치 | A 20명 고정 배치(SCV 12 + RDR 8, §2.3) / B 12명 / C 30명(성능 미검증) | **A** |
| Q13 | 반입 장비(은신처 없음) | A 고정 키트 = M2/M3 시작 장비(소총·조끼·배낭·탄창·의약품) + 지도 / B 부랑자 키트(권총은 M7 전까지 쏠 수 없어 부적합) / C 메인 메뉴 프리셋 선택 | **A** |
| Q14 | 탈출 성공 소지품을 다음 레이드로 | A 이월 없음(결과 화면만, 보관은 M5) / B 세션 메모리에 유지해 다음 시작 장비로 | **A** (B는 M5 보관함을 앞당김) |
| Q15 | 결과 화면 위치·이후 | A 종료 오버레이 3초(세계 정지) → MainMenu에서 결과 패널 → 메인 메뉴 / B 레이드 씬 안 결과 화면 → 메인 메뉴 버튼 | **A** (M5에서 은신처가 그대로 받음) |
| Q16 | ESC 일시정지 | A 실제 정지 + [계속]/[레이드 포기 = 사망 처리] / B 메뉴만, 시간은 흐름 / C M4에 없음(Esc는 화면 닫기만) | **A** |
| Q17 | 맵 상자 내용 | A `LootContainer`에 LootTableSO 굴림 추가(코드의 TODO(M4)), 상자 테이블 1종 / B 고정 내용물 | **A** |
| Q18 | 결과 화면 내용 | A 결과·원인/탈출구·시간·처치 수·반입/반출/소실 목록(정의별 수량) / B 결과 + 아이템 목록만 | **A** |
| Q19 | 레이드 중 앱 종료(Alt+F4, RULE-03) | A M4는 처리 없음(M5 세이브 플래그) / B M4에서 임시로 경고만 | **A** |
| Q20 | 맵 경계 | A 가장자리 안쪽 보이지 않는 벽 / B 가파른 지형만 | **A** |
| Q21 | 방사능 정유소 표시 | A 블록아웃만(효과·색 없음, M7) / B Toxic 색 바닥 표시만 | **A** |
| Q22 | M4 범위 확인: 결과 이후 "메인 메뉴" 버튼 구성 | A [레이드 시작]/[종료] 2개 / B + 설정(감도 등) | **A** |

---

## 6. 작업 단계

규칙(M3와 같음): **원본 저장소에서 배치모드 Unity는 한 번에 하나**(1단계 끝, 3단계, 4단계, 리뷰 수정 — 순차). 병렬 단계는 1단계 완료 상태를 scratchpad로 복사(`Assets`·`Packages`·`ProjectSettings`·`Library`)한 사본에서만 배치모드를 돌리고, 원본에는 **자기 소유 파일만** 쓴다. 각 단계는 끝에 이 문서 맨 아래 "N단계 구현 메모"를 덧붙인다. 새 `.meta`는 만들지 않는다.

| 단계 | 담당 | 작업 | 마감 조건 | 예상 |
|---|---|---|---|---|
| **1. 계약** | gameplay-programmer | §4.1 Core 전부(GameFlow 구현 포함 — 작아서), `GameManager` 연결, `PlayerInventory.Contains/TryConsume` 구현(인터페이스 추가로 컴파일 필요) + `UiTestFakes.FakeInventoryService` 갱신, Raid **껍데기**(§4.2 SO·struct 필드와 초기값, MonoBehaviour 직렬화 필드만: ExtractionZone·PlayerSpawnPoint·PowerSwitch·PointOfInterest·RaidController), `CrimsonBasinLayout.cs`, EditorTools asmdef, 입력 `Map` 액션. 테스트 도우미 `Tests/EditMode/M4TestData.cs`(RaidConfig 빌더, `FakePlayerTarget`, 아이템 정의), 테스트 `GameFlowTests`, `InventorySnapshotTests`, `InventoryConsumeTests` | 원본 EditMode 전체 통과(기존 464 + 새) | 15~20분 |
| **2a** | gameplay-programmer | §4.2 Raid 로직·브리지 전부, §4.3 Player, §4.4 Inventory. 테스트 새 파일: `ExtractionRulesTests`, `ExtractionModelTests`, `ActiveExtractionPickerTests`, `RaidSessionTests`, `RaidResultBuilderTests`, `WorldInteractableTests`, `LootContainerRollTests`, `PlayerPlacementTests`(PlayMode, 간단 씬 구성) | 사본 EditMode·PlayMode(기존) 통과 | 40~45분 |
| **2b** | ui-programmer | §4.5 전부(GameUI 확장, MainMenu UI·결과 패널). 테스트 `RaidUiModelTests`(HUD 문구, 화면 상태 Pause/Map/RaidEnd 규칙, 지도 모델, 결과 뷰모델 묶음), `MainMenuPresenterTests` | 사본 EditMode 통과 | 45~55분 |
| **2c** | data-designer | `M4DataSetup.cs`, `M4DataAssetTests.cs`(EXT 값 = §8.2, 1500초, 아이템·테이블 존재, 멱등). 사본에서 메뉴 실행. **원본에 에셋을 만들지 않는다** | 사본 메뉴 + 데이터 테스트 통과 | 15~20분 |
| **2d** | ai-programmer | `M4MapSetup.cs`, `M4EnemySetup.cs`(+ M3SceneSetup 도우미 internal화). 사본에서 씬 생성·**NavMesh 굽기 시간·에셋 크기 측정**, 스폰→탈출구 경로 확인. 2c 데이터가 없으면 사본에서 2c 메뉴 파일을 임시로 같이 둔다(원본에는 안 씀) | 사본에서 씬 생성 성공 + 측정 보고 | 50~70분 (굽기 반복 위험) |
| **3. 통합** | gameplay-programmer | `M4SceneSetup.cs`, 원본에서 `-executeMethod Ashfall.EditorTools.M4SceneSetup.CreateAll` 실행(재실행 1회로 `.meta` 불변·Terrain/NavMesh 바이트 동일 확인), `BuildSettingsTests`에 Raid 씬 추가, `Tests/PlayMode/RaidSceneSmokeTests.cs` | 원본 EditMode·PlayMode 전체 통과 | 30~40분 |
| **4. 검증** | test-engineer | §7 PlayMode 통합 테스트 + `M4TestSupport.cs`, Raid 씬 성능 테스트, Windows 빌드, AC 체크 | 원본 전체 통과, 수치 보고 | 45~55분 |
| **5. 리뷰** | code-reviewer | 의존 방향(Raid → Core만), 싱글톤 1개, timeScale 주체 1곳, 하드코딩 수치, 이벤트·서비스 해지, Update 할당, RULE-01 단일 지점, RULE-06(위치 노출 없음) | | 10분 |
| 리뷰 수정 | gameplay/ui | 지적 수정 + 재검증(원본) | 원본 전체 통과·빌드 | 30~40분 |

- **예상 총 소요: 약 3.5~4.5시간**(1단계 20 + 병렬 2단계 최장 2d 60~70 + 3단계 35 + 4단계 50 + 리뷰 45). NavMesh 굽기가 5분을 넘거나 반복되면 +30분.
- 파일 소유: Core·`UiTestFakes`·`M4TestData`·`CrimsonBasinLayout` = 1단계만(이후 추가는 새 파일, 계약 변경은 오케스트레이터 경유). Raid·Player·Inventory = 2a. UI·Art/UI = 2b. `M4DataSetup`·데이터 테스트 = 2c. `M4MapSetup`·`M4EnemySetup`·`M3SceneSetup` = 2d. `M4SceneSetup`·`BuildSettingsTests` = 3. `TestData.cs`·`M3TestData.cs`는 아무도 고치지 않는다.
- 2a↔2d 접점: 2d는 1단계 껍데기의 직렬화 필드 이름(`ExtractionZone.id`, `PlayerSpawnPoint.id`, `PowerSwitch.switchId/displayName`, `PointOfInterest.id/displayName`, `RaidController.config/seed`, `LootContainer.lootTable/seed`)만 쓴다. 1단계가 `LootContainer` 필드도 미리 추가한다.
- 0할당 측정 잡음(M3 메모): 새 0할당 테스트는 `AiTest.AssertNoAlloc`(재시도) 사용.

---

## 7. AC별 테스트 계획

### AC1 — Crimson Basin 블록아웃(§8.1)
| 종류 | 테스트 |
|---|---|
| EditMode `M4MapAssetTests`(3) | TerrainData 크기 1000×1000, 높이 120, 해상도 513; POI·탈출·스폰 위치 높이 평탄(주변 4점 차 < 0.5m); NavMesh 에셋 존재 |
| PlayMode `RaidSceneSmokeTests`(3) | 씬 로드 → POI 5·ExtractionZone 4(id = config)·스폰 3·스위치 1, 적 20명 최상위·`isOnNavMesh`, CoverPoint 수 = `Covers.Count`, 모든 스폰·탈출 `SamplePosition` 성공, 스폰마다 활성 탈출구 `CalculatePath` Complete, 적 시작 위치가 모든 스폰에서 ≥ 150m, 경계 벽 4, 서비스 등록(IRaidInfo·IPlayerPlacement·IPlayerTarget·IProjectileService), 콘솔 오류 없음 |
| PlayMode `RaidPerformanceTests`(4) | M3 방식: 무적 TestTarget을 POI 사이에 두고 총성으로 POI 2곳 교전 유도 + 나머지 순찰, 10초 측정: AI 평균 ≤ 1.0ms·99% ≤ 3ms, 투사체 ≤ 1.0ms, 프레임 중앙값 ≤ 16.6ms(렌더 제외) |
| 사람 | 맵 걸어 보기(경계·막힘·지형 끼임), Profiler·Stats로 1080p 60fps(가능하면 개발 빌드) |

### AC2 — 탈출 EXT-01~04, 레이드 타이머
| 종류 | 테스트 |
|---|---|
| EditMode `ExtractionRulesTests` | 구역 반경·높이 경계, 조건 순서, 전원/시간(899·900초)/배낭/아이템 각 Block |
| EditMode `ExtractionModelTests` | 10/15/20/7초 정확히 완료, 이탈·조건 깨짐 시 초기화, 비활성은 항상 Inactive, Completed 이후 무시 |
| EditMode `ActiveExtractionPickerTests` | spawnRules 그대로, 규칙 없을 때 EXT-01 포함 2~3개, 시드 재현 |
| EditMode `RaidSessionTests` | 1500초 → MIA, 대기 중 시간 초과 → MIA, 같은 틱 완료 → 탈출, 사망 → Killed(원인), 포기, 종료 후 입력 무시, EXT-03 완료 시 `TryConsume` 1회·실패면 완료 안 함, 스위치 켜기 1회 true, 처치 수, `Ended` 1회 |
| PlayMode `ExtractionFlowTests`(4) | `IPlayerPlacement`로 각 구역에 순간이동: EXT-01 10초 → `RaidEnded(Extracted)`·`Flow.Phase`가 MainMenu로; 구역 이탈 시 초기화(이벤트 SecondsLeft 복귀); EXT-02 스위치 전 Blocked(NoPower) → F 상호작용(SimInput) 후 15초 탈출; EXT-03 `AdvanceTime(900)` 전 TooEarly, 신호탄 없으면 MissingItem, 있으면 20초 뒤 신호탄 1개 감소 + 탈출; EXT-04 배낭 착용 Blocked → 배낭 비우고 해제 후 7초 탈출; `AdvanceTime(1500)` → MIA. (비활성 탈출구는 시드 고정으로 고른다) |
| 사람 | 4곳 직접 탈출, 카운트다운·조건 문구 가독성, 지도에 활성 탈출구만 표시·내 위치 없음 |

### AC3 — 사망/MIA/탈출 결과 화면, RULE-01
| 종류 | 테스트 |
|---|---|
| EditMode `InventorySnapshotTests` | 장착·조끼·배낭·주머니 순서, 루트 제외, 무기 장착 탄창 미중복, null 안전 |
| EditMode `RaidResultBuilderTests` | 탈출: CarriedOut = 종료 소지품(획득 루트 포함), Lost 빈; 사망·MIA·포기: CarriedOut 빈, Lost = 반입 + 획득; BroughtIn은 시작 시점 정의(도중 소모돼도 남음) |
| EditMode `GameFlowTests` | StartRaid → 로드 대리자 "Raid_CrimsonBasin", CompleteRaid → PendingResult·"MainMenu"·정지 해제, 두 번 호출 무시, Acknowledge, SetWorldPaused 대리자 호출(바뀔 때만), OnSceneLoaded로 Phase·정지 해제 |
| EditMode `RaidUiModelTests`·`MainMenuPresenterTests` | 결과 제목 4종, 수량 묶음, 탄 수·HP 미표시, PendingResult 있으면 결과 패널 먼저, RaidEnd 화면은 사망 후에도 열리고 Esc로 안 닫힘, Pause 중 Tab·H·M 무시 |
| PlayMode `RaidLoopFlowTests`(4) | Boot → MainMenu → [레이드 시작] → Raid 로드 → 머리 피격(IDamageable)으로 사망 → 3초 뒤 MainMenu, 결과 패널 "사망 — 머리", Lost에 소총 포함, CarriedOut 0; 루트 상자에서 아이템 획득 후 EXT-01 탈출 → CarriedOut에 그 아이템; Esc 일시정지 → timeScale 0, 타이머 정지 → 포기 → Abandoned; 레이드 후 메뉴에서 Raid 관련 이벤트 구독 수 0(누수) |
| 사람 | 결과 화면 문구·목록, 메뉴 ↔ 레이드 2회 반복(두 번째도 정상 시작) |

### 회귀
- 기존 EditMode 464·PlayMode 39 그대로. `BuildSettingsTests`: 첫 씬 Boot, MainMenu·Raid_CrimsonBasin 포함. `BootFlowTests`는 MainMenu 재생성 후에도 통과. M3 씬 테스트는 GameUIPresenter 생성자 변경(flow null 허용)에 영향 없음 확인.
- Windows 빌드 성공(씬 3개, 1km Terrain 포함).

---

## 8. CHANGELOG_GDD.md "M4 설계 결정" 초안

```markdown
## 2026-10-?? — M4 설계 결정 (§2, §8, §12, §12.1, §13.2, §13.3, §14, §15 M4, RULE-01, RULE-02, RULE-06)
문서 본문은 바꾸지 않았다. 아래는 M4 구현을 위한 해석이며, 기획 확정 시 문서에 반영하거나 되돌린다.
- **§13.3 씬 흐름·GameManager:** 유일한 싱글톤 `GameManager`에 `GameFlow`(Core, 순수)를 더한다 — 씬 전환(Boot → MainMenu → Raid_CrimsonBasin → MainMenu),
  레이드 결과 보관(`PendingResult`), 세계 정지(timeScale)의 유일한 주체. 레이드 결과는 `RaidResult`(Core)로 씬을 넘는다.
  M5 경계: `StartRaid(RaidRequest)`(반입 Loadout, raid_in_progress 기록), `CompleteRaid`의 목적지 = Hideout.
- **§13.3 경계:** Raid는 Core만 참조한다. 플레이어 위치·사망은 기존 `IPlayerTarget`/`PlayerDied`, 스폰 배치는 새 `IPlayerPlacement`(Player 구현),
  배낭·신호탄은 `IInventoryService`(새 `Contains`·`TryConsume`), 반입·반출 목록은 Core `InventorySnapshot`, 전원 스위치 F 상호작용은 새 Core
  `IWorldInteractable`(Inventory의 F 레이캐스트가 함께 찾음), UI로는 이벤트 `RaidStarted`·`ExtractionStatusChanged`·`RaidEnded`와 `IRaidInfo`(지도),
  UI에서 Raid로는 `RaidAbandonRequested`.
- **§14 RaidConfigSO:** `ExtractionPointConfig`를 조건 플래그로 정의(waitSec, radiusM, requiredSwitchId, availableAfterSec, consumedItem, forbidsBackpack).
  추가: spawnRules(스폰별 활성 탈출구), randomActiveCount, alwaysActiveIds, endDelaySec, zoneHeightToleranceM.
- **§8.1 블록아웃:** ⟨Q1 결과⟩. Terrain 1000×120×1000(해상도 513), 분지형 높이(결정적 생성), POI 좌표는 `CrimsonBasinLayout`. 경계는 ⟨Q20⟩.
  실내 없음: 외벽·잔해·차량 무더기는 내부를 채운 기본 도형. `Ashfall/Setup/Create M4 Scenes`가 씬·Terrain·NavMesh를 매번 새로 만든다(GUID 유지).
- **§8.2 탈출:** 구역 = 수평 반경 + 높이 차 3m. 대기 중 구역 이탈·조건 깨짐 시 초기화 ⟨Q8⟩. 같은 틱에 대기 완료와 시간 초과가 겹치면 탈출 우선.
  활성 규칙 ⟨Q3⟩: EXT-01은 "항상 열림"이므로 모든 스폰에서 활성. 비활성 탈출구는 작동하지 않는다. 스폰 ⟨Q2⟩.
  EXT-02 스위치 ⟨Q5⟩. EXT-03 "15분 이후" = 경과 ≥ 900초, 신호탄 소모 ⟨Q6⟩, 획득처 ⟨Q7⟩. EXT-04는 배낭만 금지(조끼 허용), 대기 중 매 틱 확인.
- **§12 HUD:** 탈출 카운트다운(초, 구역 안에서만)과 ⟨Q9⟩ 조건 문구. 남은 레이드 시간 ⟨Q10⟩. 지도 ⟨Q11⟩(RULE-06, 내 위치 없음).
  화면 추가: 일시정지 ⟨Q16⟩, 레이드 종료 오버레이(3초, 세계 정지), 메인 메뉴, 레이드 결과(MainMenu에서 표시 ⟨Q15⟩, 내용 ⟨Q18⟩).
- **RULE-01:** 판정 지점은 `RaidResultBuilder` 하나 — 탈출이 아니면 반출 0, 종료 시점 소지품 전부를 소실로 기록. M4는 보관함이 없어 다음 레이드는
  고정 키트로 시작한다 ⟨Q13·Q14⟩. 실제 보관함 반영은 M5.
- **RULE-02:** 1500초(RaidConfigSO) 도달 = MIA(사망 처리, 몸은 그대로). 일시정지 중 정지. 레이드 포기 = 사망과 같은 RULE-01 ⟨Q16⟩.
- **§7 레이드 적:** M3 AI를 코드 변경 없이 고정 배치 ⟨Q12⟩(SCV 12 + RDR 8). 런타임 스포너·무작위 배치는 M4 범위 밖(Raid는 AI를 참조할 수 없음).
- **§8.3:** M4에는 동적 환경을 만들지 않는다(재 폭풍 M6, 방사능 M7, 낮/밤 없음 = 시간 진행 없음).
- **§12.1 키:** 지도 = M(입력 액션 `Map`) ⟨Q11⟩. 일시정지 = Esc(UI Cancel 재사용, 화면이 모두 닫혀 있을 때).
- **GDD에 수치가 없어 임시값을 넣은 항목 (TODO(GDD-?)):** 탈출 구역 반경(8/8/10/5m)·높이 허용 3m, 종료 오버레이 3초, POI·탈출·스폰 좌표,
  지형 높이(바닥 15m·가장자리 +45m·언덕 +35m·저수지 -8m), 적 배치, 신호탄·지도 아이템 크기·무게·희귀도, 상자 루트 테이블 ⟨Q17⟩.
- **M4 확인 씬:** `Scenes/Raid_CrimsonBasin.unity`(빌드 포함), `Scenes/Raid_CrimsonBasin/`의 Terrain·NavMesh 에셋. MainMenu는 M4 메뉴가 다시 만든다.
```

---

## 9. 의견 — M3 에디터 확인을 M4 전에 해야 하나

- **행동 확인(M3_AI, 10~15분): M4 3단계(맵에 적 배치) 전에는 하는 것이 좋다.** M4는 AI 코드를 바꾸지 않고 그대로 1km 지형·20명 배치에 올린다. RDR 엄폐·우회·Alert 같은 행동 결함이 M4 확인 중에 처음 드러나면 "지형·배치 문제"와 "M3 로직 문제"가 섞여 원인 분리가 어려워진다. 1~2단계(Core 계약·Raid 로직·UI·데이터)는 AI와 무관하므로 사람 확인과 병렬로 시작해도 된다.
- **성능 확인(M3_AIPerf 60fps): M4 Raid 씬 확인으로 합쳐도 된다, 단 짧은 기준점 측정은 권장.** M4 씬은 M3_AIPerf의 상위 집합(같은 20명 구성 + Terrain 렌더 + 블록아웃 ~400개 + 넓은 NavMesh)이라 최종 판정은 Raid 씬에서 해야 의미가 있다. 다만 Raid 씬에서 60fps가 안 나오면 원인이 AI(CPU)인지 Terrain(GPU)인지 가르기 위해, M3_AIPerf에서 Profiler 2~3분 기준값(Stats FPS, `Ashfall.AI.Tick`)을 먼저 남겨 두는 것이 싸다.

---

---

## 질문 결정 (오케스트레이터, 2026-10-21)
사용자 지시: 사용자 답을 기다리지 않고 GDD §0대로 가장 단순한 쪽으로 정해 `TODO(GDD-?)`와 CHANGELOG에 기록한다.
→ §5 질문 Q1~Q22는 **모두 ★추천안으로 확정**한다. 문서 본문의 ⟨Qn⟩ 표시는 추천안 그대로 읽는다.
(근거: 추천안이 이미 GDD 문구를 지키면서 가장 적은 신규 시스템을 쓰는 쪽이다. 예외 없음.)
구현 시 각 결정 지점에 `// TODO(GDD-?) Qn: …` 주석을 남긴다. CHANGELOG "M4 설계 결정"의 ⟨Qn⟩ 자리는 추천안으로 채운다(오케스트레이터가 작성).

---

## 1단계 구현 메모 (gameplay-programmer, 2026-10-21)
결과: 원본 EditMode 505/505 통과(기존 464 + 새 41: GameFlowTests 18, InventoryConsumeTests 16, InventorySnapshotTests 7), PlayMode 39/39 통과. 커밋 안 함.

설계와 다르거나 설계에 없던 결정
- `GameFlow.StartRaid`: Raid 단계이거나 레이드 씬 로드 요청 후 `OnSceneLoaded` 전이면 무시(연속 클릭 이중 로드 방지). 호출 시 정지 해제.
- `GameFlow.CompleteRaid`: 설계대로 정지 해제 후 MainMenu 로드(씬이 내려가기 전 몇 프레임 세계가 다시 돈다 — 세션은 이미 끝나 결과 불변). `null` 결과도 무시.
- `GameManager`: `sceneLoaded`는 `LoadSceneMode.Single`일 때만 `Flow.OnSceneLoaded`에 전달(테스트의 Additive 씬은 단계를 바꾸지 않음). `ResetStatics`에서 `Time.timeScale = 1`.
- `RaidResult`: 생성 시 목록 3개를 배열로 복사, null → 빈 목록(스냅샷 리스트 재사용 안전).
- `InventorySnapshot.Collect`: StorageGrids에 `GridKind.Loot` 그리드가 섞여 있어도 건너뜀(방어). 중복 판정은 이번 호출에서 더한 범위만.
- `PlayerInventory.Contains`: 잠긴 아이템도 true(보유 여부). `TryConsume`: StorageGrids 순서(주머니 → 조끼 → 배낭)·배치 순 첫 번째, 잠긴 것은 건너뜀, 장착 슬롯·열린 루트 제외. 둘 다 할당 없음.
- Raid 껍데기: 직렬화 필드(`[SerializeField] private`) + 읽기 전용 프로퍼티(`Id`, `SwitchId`, `DisplayName`, `Config`, `Seed`)만. `PowerSwitch`는 아직 `IWorldInteractable`을 구현하지 않는다(2a). 기즈모 없음.
- `LootContainer`: `lootTable`·`seed` 필드 + `LootTable`·`Seed` 프로퍼티만. 굴림·클래스 주석의 `TODO(M4)` 해소는 2a.
- 입력: Player 맵 `Map` 액션(id f7e1e63d-…) + `<Keyboard>/m` 바인딩(id c5d38ab7-…). 아직 아무 코드도 읽지 않는다(2b).
- `EditorTools` asmdef에 `Ashfall.Raid` 추가.

2단계가 알아야 할 것
- `M4TestData`(EditMode): `RaidConfig(flare)`(EXT-01~04 = 10/15/20/7초, 반경 8/8/10/5, EXT-02 `SW-PWR`, EXT-03 900초 + 신호탄, EXT-04 배낭 금지),
  spawnRules `SP-W {01,02,03}`, `SP-E {01,04,03}`, `SP-N {01,02,04}`(§2.3 표의 "(+EXT-03)"을 포함으로 읽음 — 2c 데이터도 같게),
  `Flare()`(ITM-FLR-01, 1×2, 0.3kg), `MapItem()`(ITM-MAP-01, 1×1, 0.1kg), `Extraction(...)`, `SpawnRule(...)`, `Find`, `Result(outcome, …)`,
  `FlowRecorder`(GameFlow 대리자 기록), `FakePlayerTarget`, `FakePlayerPlacement`. 상수 `Ext01~04`, `PowerSwitchId`, `SpawnWest/East/North`.
- `FakeInventoryService`(UiTestFakes)는 `Contains`(장착+모든 그리드)·`TryConsume`(FakeGridView에서 첫 번째 제거, `Calls`에 기록) 구현.
- `CrimsonBasinLayout`(EditorTools, public static): 지형 상수(§2.2), 경계 벽, `Pois`(5, 중심·footprint), `Extractions`(4, 중심·패드 크기), `Spawns`(3, `YawDeg` = 중앙 방향),
  `PowerSwitchId/DisplayName/XZ/YawDeg`, `Roads`, 잔해 개수·시드, `LootCrates`(19)·`FlareCrates`(2), `Squads`(9개 분대 = SCV 12 + RDR 8, squadId 1~9,
  SCV는 `RouteXZ`(M3 CreateSquad 방식), RDR은 `SpotsXZ` + `YawDeg`), `MinEnemyDistanceFromSpawnM = 150`. 손 계산: 적 시작 위치는 모든 스폰에서 ≥ 약 190m(REF_R2 ↔ SP-E), 순찰 경로 점은 ≥ 약 178m(RES ↔ SP-N).
  지형 기복·패드 높이에 따라 경로 점이 막히면 2d가 새 파일에서 보정하고 오케스트레이터에 알린다(이 파일은 1단계 소유).
- `ScreenKind`에 `Map`, `Pause`, `RaidEnd`(끝에 추가), `InteractionKind.Activate` 추가 — `UiText.InteractionVerb`는 아직 Activate를 "줍기"로 보여 준다(2b가 고침).

---

## 2c 구현 메모 (data-designer, 2026-10-21)
결과: 사본에서 `-executeMethod Ashfall.EditorTools.M4DataSetup.CreateAll` 종료 코드 0 → EditMode 525/525 통과(1단계 505 + M4DataAssetTests 20). 원본에는 에셋을 만들지 않았다. 커밋 안 함.
(사본에서 2b 작업 중 파일 `UI/RaidResultViewModel.cs`가 CS0102(`DurationText` 중복)로 컴파일되지 않아 사본에서만 뺐다. 다른 2b 파일·`HudConfigSO.mapItem`은 그대로 포함해 확인.)

파일: `Scripts/EditorTools/M4DataSetup.cs`(메뉴 `Ashfall/Setup/Create M4 Data`, `CreateData`/`CreateAll`, 먼저 `M3DataSetup.CreateData()`), `Tests/EditMode/M4DataAssetTests.cs`. SetupUtil 변경 없음.

생성 에셋(없을 때만 만든다 — 멱등, 재실행 시 파일 바이트·SO 수 불변을 테스트로 확인)
- `Data/Raid/RaidConfig.asset`: 값은 `M4TestData.RaidConfig`와 같다(테스트 `RaidConfig_EqualsM4TestData`가 고정). timeLimitSec 1500, EXT-01 남쪽 도로 10초 r8 / EXT-02 열차 신호소 15초 r8 `SW-PWR`(= `CrimsonBasinLayout.PowerSwitchId` 참조) / EXT-03 헬기 착륙장 20초 r10 900초 이후 + ITM-FLR-01 소모 / EXT-04 하수 배출구 7초 r5 배낭 금지,
  spawnRules SP-W {01,02,03}, SP-E {01,04,03}, SP-N {01,02,04}, randomActiveCount (2,3), alwaysActiveIds {EXT-01}, endDelaySec 3, zoneHeightToleranceM 3.
- `Data/Items/Raid/ITM-FLR-01.asset`(신호탄, 1×2, 0.3kg, Common, 가격 0), `Data/Items/Raid/ITM-MAP-01.asset`(종이 지도, 1×1, 0.1kg, Common, 가격 0). 둘 다 기본 `ItemDefinitionSO`.
- `Data/Loot/LT-CRATE.asset`(Q17, 전부 TODO(GDD-?)): rollCount (1,3), 가중치 BND 3 · JNK-03 3 · TRQ 2 · MAG-556-30 2 · JNK-01 2 · SPL 1 · MAG-9MM-12 1 · ARM-HLM-01 0.5 · ITM-FLR-01 0.5(Q7-A 낮은 가중치, 맵 전체 기대 약 1.3개). 지도는 넣지 않음.
- `Data/UI/HudConfig.asset`: `mapItem` = ITM-MAP-01 — 필드가 있고 비어 있을 때만(SerializedObject로 이름 접근, UI 코드 컴파일 의존 없음). 필드가 없으면 경고 후 건너뜀.

다른 단계가 쓸 것: 경로 상수 `M4DataSetup.RaidConfigPath`·`FlarePath`·`MapItemPath`·`LootCratePath`(public const). 2d는 상자 `lootTable` = LootCratePath, 신호탄 고정 상자 `contents` = FlarePath, 플레이어 시작 장비에 MapItemPath 추가.
시작 키트 테스트: M2 키트(소총·조끼·배낭·탄창 3·붕대·지혈대·부목) + 지도가 모두 들어가고 남은 공간(배낭)에 신호탄 1개를 주워 `TryConsume`할 수 있음을 확인. 상자 `gridSize`가 헬멧(2×2)·탄창(1×2)을 담을 수 있어야 한다(2d, M2의 LootGridSize 그대로면 충분).

---

## 2d 구현 메모 (ai-programmer, 2026-10-21)
결과: 사본(copy4_2d, 1단계 상태 + 2d 파일)에서 빈 씬에 맵 + 적 생성 3회 성공, 경로·적 확인 통과. 사본 EditMode 505/505 통과(컴파일 경고·오류 없음). 원본에는 에셋·씬을 만들지 않았다. 커밋 안 함.
파일: `EditorTools/M4MapSetup.cs`(새), `EditorTools/M4EnemySetup.cs`(새), `EditorTools/M3SceneSetup.cs`(private → internal 13곳 + 주석 1줄, 동작 불변). `CrimsonBasinLayout.cs`는 고치지 않았다(보정 필요 없음).

측정 (사본, 노트북 i7 + HD 620, 배치모드)
- NavMesh 굽기 **16.4s / 21.9s / 21.8s**(3회) — 기본 설정(voxel = 반지름/3 ≈ 0.167m, 타일 256 voxel, minRegionArea 2). 5분 기준보다 훨씬 짧아 voxel·타일 덮어쓰기 안 함(`DefaultBake` = 기본값).
- `NavMesh-Environment.asset` **약 0.97MB**(997,484 B), `Terrain-CrimsonBasin.asset` **536KB**(549,176 B, 바이너리), 확인용 씬 YAML 1.88MB(플레이어·UI 없음).
- 도형 197개(블록아웃, 정적 배칭) + 상자 21 + 스위치 1, **CoverPoint 494개**(설계 예상 250~350보다 많음 — 긴 벽·담장·잔해 60개. CoverIndex 선형 탐색 1000 기준 아래, 4단계 성능 측정에서 확인).
- 결정성: 같은 코드로 2회 실행 → Terrain·NavMesh 에셋 **바이트 동일(md5)**, 두 `.meta` GUID 불변(SetHeights / CopySerialized 덮어쓰기).
- 경로(스폰 3 × 탈출 4 = 12쌍 전부, spawnRules의 상위 집합. RaidConfig는 2c 전이라 없음): **12/12 `PathComplete`**. 예) SP-W→EXT-01 601m, SP-E→EXT-02 371m, SP-N→EXT-03 141m, 최장 SP-N→EXT-02 1025m.
- 적: 20명(SCV 12 + RDR 8), 시작 위치·순찰 웨이포인트 전부 NavMesh 위(SamplePosition 차 ≤ 0.5m), 최상위. 스폰과 가장 가까운 적 **192m**(REF_R2 ↔ SP-E, 기준 150m).
- 2c 데이터 대체(사본 전용 측정 코드에서만): 상자 테이블 = `LT-SCV`, 신호탄 = `MED-BND-01`, RaidConfig 없음(구역 표시 반경 기본 8m).

설계와 다르거나 설계에 없던 결정 (모두 `TODO(GDD-?)` 주석)
- **송전탑 언덕:** "가우시안 +35m(반경 90m)" → 정상 평탄 반경 75m + 경사 폭 90m smoothstep 고원(최대 경사 ≈ 30°). 가우시안 꼭대기에 POI 100×100m를 평탄화하면 가장자리가 45°를 넘는다. 언덕·저수지 POI는 지형 함수 자체가 모양이라 POI 패드를 두지 않는다(저수지 웅덩이를 메우지 않게).
- **평탄 패드 블렌딩 폭:** POI 40m, 탈출·스폰 60m, 도로 30m. 첫 측정(탈출·스폰 40m, 도로 15m)에서 EXT-01 도로 둑 옆면 최대 59°·EXT-02 서쪽 49°가 나와 넓혔다. 지금 45° 초과 지형은 표본의 0.12%(최대 47°, EXT-01 도로 옆 한 점·EXT-02 서쪽 비탈·동쪽 철로 둑 서쪽 비탈의 좁은 띠) — 걸을 수 없는 띠일 뿐 경로는 모두 있다. 패드 목표 높이 = 패드 중심의 원래 높이, 적용 순서 도로 → POI → 탈출 → 스폰.
- 도형은 지형 위에 놓을 때 발자국 네 모서리 + 중심의 지면 높이를 재서 바닥을 최저점 0.5m 아래, 윗면을 최고점 + 도형 높이로 늘린다(경사면 틈 방지). 고가 상판은 최고점 + 6m, 기둥은 상판까지.
- 탱크는 원통 프리미티브 + **MeshCollider**(기본 캡슐 콜라이더는 위아래가 반구라 바닥이 비어 NavMesh가 탱크 밑으로 들어간다).
- 정적 플래그는 `BatchingStatic`만(`NavigationStatic`은 Unity 6에서 사용 중단, NavMeshSurface는 플래그 불필요).
- 엄폐 규칙(M3 확장): 낮은 벽은 앞뒤 6m 간격(긴 담장·배관), 높은 벽·집·높은 잔해는 짧으면 M3와 같게(앞뒤 가운데, peek 반대쪽 끝 밖 0.4m), 길면 앞뒤 양 끝 4개(peek 각 끝 밖). 차량 4방향 Low. 엄폐 지점·peek 위치는 지면 높이에 붙인다(peekLocalOffset에 높이 차 포함).
- 흩어진 잔해 60개: 시드 4041, ±460m 안, POI 발자국 + 40m·탈출/스폰/스위치 40m·도로 반폭 + 6m 밖, 경사 ≤ 25°, 서로 15m 이상. 차량 40% / 낮은 벽 30% / 높은 벽 30%.
- 루트 상자: Environment/LootCrates 아래(NavMesh 장애물로 구워짐, 정적 배칭 안 함), 이름 "보급 상자", 그리드 6×4(M2와 같음), `seed 0`(열 때마다 무작위). **신호탄 상자 2개는 신호탄 1개 고정, 테이블 굴림 없음**, 이름·모양은 일반 상자와 같다.
- 탈출 구역 표시: 4곳 모두 반투명 원판(콜라이더 없음, `M4_ExtractionZone.mat` URP Unlit 투명). 반경은 생성 시점 RaidConfig 값(없으면 8m) — config 반경을 바꾸면 메뉴 재실행. 활성 여부는 표시하지 않는다(RULE-06: 지도에서만).
- 전원 스위치: 큐브 패널 0.6×1.2×0.3m(BoxCollider = F 레이캐스트 대상), 바닥 0.6m 위, `PowerSwitchYawDeg` 방향. Environment 밖(굽기 제외). `PowerSwitch.switchId/displayName` = Layout 값.
- 레이드 오브젝트(최상위 루트): `RaidController`(config, seed 0), `ExtractionZones/Extraction_EXT-0n`(id, 지면 높이), `PlayerSpawns/Spawn_SP-*`(id, yaw = Layout `YawDeg`), `PointsOfInterest/POI_*`(id, displayName), `PowerSwitch_SW-PWR`. 경계 벽 = 최상위 `Bounds` 아래 BoxCollider 4개(렌더러 없음, ±495m 안쪽 면, 높이 150m).
- 적: M3 `CreateRoute`가 웨이포인트를 y 0에 두므로 만든 뒤 NavMesh에 붙인다(지면 → `SamplePosition` 3m). 시작 위치도 같다. 분대 대형·seed(배치 순번 + 1)·NavMeshAgent 값은 M3 그대로.

3단계(M4SceneSetup)가 부를 API (`Ashfall.EditorTools`, 모두 internal static — 같은 어셈블리)
```csharp
// NewScene 전(에셋 생성): 
M4MapSetup.EnsureZoneMaterial();          // M4_ExtractionZone.mat 없을 때만
M4EnemySetup.EnsureEnemyMaterials();      // M3_EnemySCV/RDR.mat 없을 때만
AssetDatabase.SaveAssets();
// NewScene(DefaultGameObjects) 후 (M2 함정: 에셋 참조는 NewScene 뒤에 불러온다):
var assets = M4MapSetup.MapAssets.LoadDefaults();   // 또는 new MapAssets { RaidConfig, CrateLootTable, Flare, ZoneMaterial } (M4DataSetup 상수)
M4MapSetup.MapBuildResult map = M4MapSetup.BuildMap(assets, M4MapSetup.DefaultBake);  // 씬당 한 번. map.Success false면 중단
//   map.Terrain, map.Environment, map.Bounds, map.Controller(RaidController), map.Surface, map.CoverPointCount, map.BakeSeconds, map.NavMeshBytes, map.TerrainBytes
AiDirector director = M4EnemySetup.CreateDirector();                 // 최상위 AiDirector(config = M3 AiConfig)
M4EnemySetup.EnemyPlacementResult enemies = M4EnemySetup.PlaceEnemies(map.Terrain, director);  // 굽기 뒤에
bool navOk = M4MapSetup.ValidateNavigation(assets.RaidConfig, out string navReport);   // 스폰·탈출 샘플 + spawnRules 경로
bool enemyOk = M4EnemySetup.ValidateEnemyStarts(enemies, out string enemyReport);      // 20 = 12 + 8, NavMesh 위, ≥ 150m, 최상위
// 도우미: M4MapSetup.GroundY(terrain, x, z), GroundPoint(terrain, xz), SnapToNavMesh(terrain, xz)
// 상수: M4MapSetup.SceneFolder("Assets/_Project/Scenes/Raid_CrimsonBasin"), TerrainDataPath, NavMeshPath, ZoneMaterialPath,
//       DefaultRaidConfigPath(Data/Raid/RaidConfig.asset), DefaultCrateLootTablePath(Data/Loot/LT-CRATE.asset), DefaultFlarePath(Data/Items/Raid/ITM-FLR-01.asset)
```
- 기본 경로는 2c `M4DataSetup.RaidConfigPath`·`LootCratePath`·`FlarePath`와 같다(확인함) — `LoadDefaults()` 그대로 써도 된다.
- 3단계 몫: 씬 생성·저장(`Scenes/Raid_CrimsonBasin.unity` 권장 — 에셋 폴더와 같은 이름), MainCamera 삭제, `ProjectileSystem`, 플레이어(`M2SceneSetup.CreatePlayer` + 지도 아이템; 위치는 아무 스폰 — RaidController가 Start에서 `PlaceAt`), GameUI, 빌드 설정. 2c 경로가 위 기본 경로와 다르면 `MapAssets`를 직접 채운다.
- 메뉴(사람용, 열린 씬에 추가): `Ashfall/Setup/M4/Build Crimson Basin Map (Open Scene)`, `Ashfall/Setup/M4/Place Crimson Basin Enemies (Open Scene)`. 같은 씬에서 두 번 누르면 두 벌 생긴다.
- 3단계 원본 실행 시 확인할 것: 처음 실행 때 `Scenes/Raid_CrimsonBasin/`(Terrain·NavMesh)와 `Art/Materials/M4_ExtractionZone.mat`의 `.meta`가 새로 생긴다(커밋 대상). 재실행 시 두 에셋 바이트 동일·GUID 불변(사본에서 확인).

사람 확인(에디터)
- 맵 걸어 보기: 경계 벽, EXT-01 도로 둑·EXT-02 서쪽 비탈(걸을 수 없는 좁은 띠), 도형 사이 끼임, 고가 상판(올라갈 수 없음 — 경사 상판은 상판과 이어지지 않는다).
- Scene 뷰 기즈모로 CoverPoint 방향·peek, 순찰 경로가 지형 위에 있는지.

---

## 2b 구현 메모 (ui-programmer, 2026-10-21)
결과: 사본(1단계 + UI) EditMode 575/575 통과(1단계 505 + 새 70), PlayMode 39/39 통과(기존 회귀). 원본에서 배치모드 실행 안 함. 커밋 안 함. Core·UiTestFakes·M4TestData 무변경.

파일 (전부 `Assets/_Project/Scripts/UI/`, `Art/UI/`, 테스트 새 파일)
- 수정: `GameUIPresenter`, `ScreenStateModel`, `GameUIRoot`, `HudView`, `UiText`, `HudConfigSO`(+`mapItem`), `Art/UI/GameUI.uxml`, `Art/UI/Ashfall.uss`.
- 새: `ExtractionHudModel`, `MapScreenModel`, `RaidResultViewModel`, `MainMenuPresenter`(순수), `MapScreenView`, `PauseScreenView`, `RaidEndOverlayView`, `RaidResultView`, `MainMenuRoot`, `Art/UI/MainMenu.uxml`.
- 테스트: `Tests/EditMode/RaidUiModelTests.cs`(61: 동사·탈출 HUD·화면 상태·지도·결과 뷰모델·프레젠터 M4·UXML 이름 계약), `MainMenuPresenterTests.cs`(9). 테스트용 `UiFakeRaidInfo`는 RaidUiModelTests.cs 안에 있다.

설계와 다르거나 설계에 없던 결정
- `GameUIPresenter(bus, services, config, GameFlow flow = null)` — 선택 인자라 기존 호출(M2·M3 테스트) 그대로. `HudView.Set(..., extractValue = null)`도 선택 인자.
- Esc: 드래그 취소 → (포기 확인 중이면) 확인 취소 → 열린 화면 닫기 → 화면이 없으면 Pause. **Pause는 모든 씬에서 열린다**(확인 씬에서도 세계 정지). [레이드 포기]는 `IRaidInfo`가 등록돼 있을 때만 보인다(`CanAbandon`) — 확인 씬에서는 [계속]만.
- 세계 정지: Pause 진입 → `flow.SetWorldPaused(true)`, Pause → None이면 false, **Pause → RaidEnd면 풀지 않는다**(Raid가 이어서 멈춰 둠). Disable(씬 언로드)은 `Screens.Reset()`으로 종료 오버레이까지 닫아 조작 차단·정지를 푼다.
- 포기: [레이드 포기] → 확인 단계 → [포기] → `RaidAbandonRequested` 1회(이후 `CanAbandon` false, 재발행 없음). Pause는 Raid의 `RaidEnded`가 올 때까지 열린 채(그 사이 Esc = 닫기 가능).
- `ScreenStateModel`: Pause 중 Tab·H·M·루트 열림 거부(루트 세션은 프레젠터가 `CloseLoot`). `ShowRaidEnd()`는 사망 후에도 열리고 `CloseAll`·`IsDead`·토글로 닫히지 않는다. `HudVisible`은 종료 후 false.
- RaidEnded 순서 무관: `PlayerDied` → `RaidEnded`, `RaidEnded` → `PlayerDied` 둘 다 RaidEnd 유지. 종료·사망 후 `ExtractionStatusChanged`는 무시.
- 지도(M): `IRaidInfo` 없음 → 경고 1회·안 열림, `HudConfigSO.mapItem` 비어 있음 → 경고 1회·안 열림, 지도 미소지(`IInventoryService.Contains`, 장착·소지품) → 조용히 안 열림. 지도는 세계를 멈추지 않는다. 열 때 `MapScreenModel.Build`, 열린 동안 매 프레임 `RefreshTime`(분 값이 바뀔 때만 문자열).
- 남은 시간 문구(Q10): 분 반올림(.5 올림) "남은 시간: 약 N분", 60초 미만 "1분 미만", 0 이하 "없음". 좌표는 `MapAreaXZ` 기준 0~1, 북쪽 = 위, 영역 밖은 가장자리. 지도 영역은 USS `--ash-map-size` 560px 정사각형(그레이박스).
- HUD 탈출 문구(Q9): "탈출까지 N" / "전원이 꺼져 있다" / "아직 헬기가 오지 않는다" / "신호탄이 필요하다" / "배낭을 메고는 들어갈 수 없다". 탈출구 이름은 HUD에 쓰지 않는다. 위치: 화면 위쪽 28%(`.hud-extract`).
- 종료 오버레이 문구: "탈출 성공" / "사망" / "작전 중 실종" / "레이드 포기". 결과 패널 제목: "탈출 성공 — {이름}" / "사망 — {부위}"(원인 없으면 "사망") / "작전 중 실종 (MIA)" / "레이드 포기". 시간 "레이드 시간 m:ss", "처치 N". 목록은 정의별 첫 등장 순 묶음 "이름 ×n", 빈 목록 "없음". 반출 섹션은 탈출만, 소실 섹션은 그 밖만.
- `MainMenuPresenter`: PendingResult 있으면 Result 패널(시작·종료 거부) → [확인] = `AcknowledgeResult` → Menu. [레이드 시작]은 한 번만(`IsStarting`, 버튼 비활성). [종료]는 대리자(런타임 `Application.Quit`, 에디터 `EditorApplication.ExitPlaymode`).
- `UiText.InteractionVerb(Activate) = "작동"` (EXT-02 스위치 프롬프트 "F  작동: {이름}").
- 문구 원본은 `UiText`, UXML의 text는 미리보기용(뷰가 덮어씀; 결과 섹션 제목 "반입/반출/소실"·지도 제목은 UXML 그대로).
- 색: §10.1 토큰 변수만. 버튼 `.ash-button`(Ash_Black/Ash_Grey 테두리, hover Dried_Blood, active Rust_Red, 위험 Ember 테두리), 사망 제목 Ember.

3단계 씬 생성에 필요한 것 (M4SceneSetup)
- **MainMenu 씬**: 카메라 1개(배경용, UI Toolkit은 카메라 불필요) + GameObject "MainMenuUI": `UIDocument`(sourceAsset = `Assets/_Project/Art/UI/MainMenu.uxml`, panelSettings = `M2SceneSetup.PanelSettingsPath` = `Art/UI/GamePanelSettings.asset`) + `Ashfall.UI.MainMenuRoot`(직렬화 필드 없음). EventSystem 없음(M2와 같은 방식). `MainMenuRoot`가 UI 맵을 켜고 커서를 푼다.
- **Raid 씬**: 기존 `M2SceneSetup.CreateGameUI(panelSettings)` 그대로(GameUI.uxml에 새 요소가 들어 있음, GameUIRoot.config = HudConfig). `HudConfig.mapItem = ITM-MAP-01`은 2c `M4DataSetup`이 기록, 플레이어 시작 장비에 지도 포함(2d).
- 입력: `Player/Map`(1단계 추가)을 GameUIRoot가 직접 Enable(없으면 경고만). Esc = 기존 `UI/Cancel`.

2a(Raid)에 기대하는 계약 (UI가 이렇게 가정함)
- `RaidController`가 레이드 진행 중 `IRaidInfo`를 등록(지도·[레이드 포기] 표시 조건). 
- 종료 시 `RaidEnded` 발행 **후** `SetWorldPaused(true)`(설계 §4.2 순서). UI는 RaidEnd 오버레이에서 정지를 건드리지 않는다.
- 구역을 나가거나 비활성 탈출구면 `ExtractionStatusChanged(InZone=false)`.
- `RaidAbandonRequested`를 받아 Abandoned로 끝낸다(Pause가 열린 채 받는다 — 세계 정지 중 이벤트 처리).
- `PowerSwitch.Kind = InteractionKind.Activate`.

확신 없는 API
- UI Toolkit 런타임 `Button.clicked`가 timeScale 0에서 EventSystem 없이 정상 동작하는지(이벤트 처리는 timeScale과 무관하다고 보고 있음 — 4단계 PlayMode/사람 확인).
- `UnityEditor.EditorApplication.ExitPlaymode()`(런타임 어셈블리에서 `#if UNITY_EDITOR`로만 사용).
- USS `letter-spacing`(메뉴 제목) — 배치모드 임포트 경고 없음 확인.

사람 확인(에디터)
- Esc 일시정지: 적·출혈·타이머 정지, [계속]·Esc로 재개, 커서 표시/잠금 복귀. [레이드 포기] → 확인 → 종료 오버레이 "레이드 포기" → MainMenu 결과 패널.
- M 지도: 활성 탈출구만 Ember 사각, POI 회색 점, 내 위치 없음, 남은 시간 분 단위. 지도 아이템을 버리면 안 열림.
- 탈출 구역 HUD 가독성(위치·크기), 조건 문구, 결과 패널 목록(탄 수 없음), 메뉴 ↔ 레이드 2회 반복.

---

## 2a 구현 메모 (gameplay-programmer, 2026-10-21)
결과(사본 `scratchpad/copy4_2a`, 다른 에이전트의 진행 중 UI·EditorTools 파일은 사본에서만 HEAD/1단계 상태로 되돌리거나 제외):
EditMode **602/602** 통과(1단계 505 + 새 97: ExtractionRulesTests 9, ExtractionModelTests 12, ActiveExtractionPickerTests 11,
RaidSessionTests 24, RaidResultBuilderTests 13, WorldInteractableTests 16, LootContainerRollTests 12), PlayMode **49/49** 통과(기존 39 + PlayerPlacementTests 10).
로그·결과: `scratchpad/m4s2a_editmode.*`, `m4s2a_playmode.*`, `m4s2a_pm_placement.*`. 커밋 안 함. 원본에 .meta 만들지 않음(새 폴더 `Raid/Logic`·새 파일 .meta는 3단계 원본 실행에서 생성).

바뀐 파일(원본)
- 새: `Scripts/Raid/Logic/`{`ExtractionRules`,`ExtractionModel`(+`ExtractionPhase`),`ActiveExtractionPicker`,`RaidSession`,`RaidResultBuilder`}.cs, `Scripts/Inventory/InteractionTargets.cs`
- 수정: `Raid/Data/RaidConfigSO.cs`(+`mapAreaXZ`), `Raid/Runtime/`{`RaidController`,`ExtractionZone`,`PlayerSpawnPoint`,`PowerSwitch`,`PointOfInterest`}.cs,
  `Player/PlayerMotor.cs`, `Inventory/PlayerInventoryBehaviour.cs`, `Inventory/LootContainer.cs`
- 테스트 새 파일 8개(위 목록, PlayMode `PlayerPlacementTests`)

설계와 다르거나 설계에 없던 결정
- `RaidConfigSO.mapAreaXZ`(Rect, 기본 (-500,-500,1000,1000)) 추가 — `IRaidInfo.MapAreaXZ`의 출처. 하드코딩 대신 SO. 2c 에셋은 필드 기본값으로 채워진다(데이터 메뉴 수정 불필요, 바꾸려면 2c가 기록).
- `RaidSession`: 탈출 완료 틱에도 그 틱의 dt를 경과 시간에 더한다(결과 시간이 10.0초가 되게). 시간 초과 판정은 하지 않으므로 "같은 틱이면 탈출 우선"은 그대로. `TimeLimitSec` 프로퍼티 추가.
  EXT-03 완료 순간 `TryConsume` 실패(잠김·사망)면 `ExtractionModel.Interrupt(MissingItem)`로 처음부터(탈출 안 함).
- `ExtractionModel`: `Id`, `Interrupt(block)` 추가. 시간 허용 오차 1e-3초(0.1초 틱 200번 = 20초 경계).
- `ActiveExtractionPicker.Pick`: 스폰 규칙 목록 + `alwaysActiveIds` 중 빠진 것을 뒤에 붙인다(EXT-01 항상 열림 보장). config.extractions에 없는 id·중복은 넣지 않는다. 규칙이 비면 무작위(EXT-01 + 나머지, 총 2~3).
- `RaidResultBuilder.Build`: `DurationSec`는 제한 시간으로 상한(MIA·AdvanceTime). `HasDeathCause`는 Killed일 때만. 정의 목록 도우미 `ToDefinitions` 추가(반입 기록).
  세션이 끝나기 전 호출 → InvalidOperationException.
- `RaidController`
  - 시드 0이면 `UnityEngine.Random.Range(1, int.MaxValue)`로 시드를 뽑는다. 씬 수집은 id 순 정렬(시드 재현).
  - Start에서 `IPlayerTarget`·`IPlayerPlacement`·`IInventoryService` 중 하나라도 없으면 LogError + 비활성(설계대로). 스폰 지점이 없으면 경고 후 현재 위치에서 시작.
  - 종료: 결과 → (구역 HUD가 보이고 있었으면 `ExtractionStatusChanged(InZone=false)`) → `RaidEnded` → `Flow.SetWorldPaused(true)` → `WaitForSecondsRealtime(endDelaySec)` →
    `Flow.Phase == Raid`면 `CompleteRaid`, **아니면(확인·테스트 씬) 경고 로그 "흐름 밖 씬" + 정지 해제만**(세계가 영구 정지하지 않게).
  - `PowerSwitch`는 RaidController가 Start에서 `Bind(session)`한다(스위치 → 컨트롤러 탐색 없음). `OnDestroy`에서 풀고 `Ended` 해지.
  - 디버그 `[ContextMenu]` "Debug/시간 +5분", "Debug/즉시 MIA". 공개: `Session`, `SpawnId`, `ActiveExtractionIds`, `BroughtIn`, `LastResult`, `AdvanceTime`, `ActivateSwitch`.
- `PowerSwitch`: `IsAvailable` = 세션 있음 ∧ 안 끝남 ∧ 아직 안 켬. `IsOn`, `Bind(RaidSession)` 추가.
- `ExtractionZone`·`PlayerSpawnPoint`: 에디터 기즈모만. 구역 반경 기즈모는 같은 씬 RaidController의 config에서 찾는다. `PlayerSpawnPoint.FeetPosition`/`YawDeg`.
- Inventory F 판정: 새 public static `InteractionTargets`(`Find(collider)` — Inventory `IInteractable` 우선, 없으면 Core `IWorldInteractable`; `IsUsable`; `TryDescribe`; `Interact`).
  `PlayerInventoryBehaviour`는 콜라이더별 캐시는 유지하되 사용 가능 여부(`IsAvailable`)를 매 프레임 보고, F 실행 후 캐시를 비운다.
- `LootContainer`: `EnsureModel(searchTimePerItemSec)`(public, 처음 한 번 생성)·`CreateContents(fixed, table, random)`(public static, 고정 내용물 → `LootRoller.Roll` 순) 추가.
  `Interact`는 `EnsureModel` 사용. 시드 0이면 `UnityEngine.Random`에서 시드를 뽑는다. Linq 제거.
- `PlayerMotor.PlaceAt`: controller 끄기 → `SetPositionAndRotation` → 원래 enabled 복원 → `Physics.SyncTransforms()`, pitch·lean·낙하 속도 0, 발소리 누적 `Reset()`, 눈 회전 즉시 수평. 자세(앉기·엎드리기)는 유지.
- PlayMode `PlayerPlacementTests`: 코드로 만든 씬(바닥 + PlayerMotor·PlayerInventoryBehaviour, 데이터 에셋 `PlayerConfig`·`InventoryConfig`). 다른 씬은 **내리지 않고** 루트를 끈다
  (테스트 러너 초기 씬을 `UnloadSceneAsync`하면 러너가 멈춤 — 단독 실행에서 확인). RaidController 스폰 배치·IRaidInfo·EXT-01 탈출(HUD 이벤트 10→1, 종료 숨김, 세계 정지 → 해제)·포기·해지 확인 포함.

2a 요청(계약 변경): 없음. Core·UiTestFakes·M4TestData는 고치지 않았다.

3단계 통합에 필요한 것
- 원본에서 배치모드 1회로 새 파일 .meta 생성(Raid/Logic 폴더 포함) 후 커밋 대상에 넣기.
- 2d 씬: `PowerSwitch`에 **콜라이더(자신 또는 자식)** 필요(F 레이캐스트). `ExtractionZone`은 콜라이더 불필요(위치 판정). 플레이어 오브젝트는 `PlayerMotor`(IPlayerPlacement 등록)·`PlayerInventoryBehaviour`·`PlayerHealth`가 있는 기존 M2 플레이어면 된다.
  RaidController는 Start에서 서비스를 찾으므로 플레이어와 같은 씬에 활성으로 두기만 하면 된다(순서 무관).
- 2b: `ExtractionStatusChanged`는 구역 진입·Block·초가 바뀔 때만, 종료 시 InZone=false 한 번. `RaidEnded`는 세계 정지 **직전**에 발행. `UiText.InteractionVerb(Activate)`(현재 "줍기") 수정 필요.
- 흐름: Raid_CrimsonBasin을 이름으로 로드해야 `Flow.Phase == Raid` → `CompleteRaid` → MainMenu. 다른 이름의 씬에서는 결과만 `LastResult`에 남는다(경고 1회).
- 4단계 ExtractionFlowTests: `RaidController.AdvanceTime`·`ActivateSwitch`·`Session.CurrentZone`·`LastResult`를 쓰면 된다. 세계 정지 중(timeScale 0) `Time.captureDeltaTime`은 dt를 만들지 않는다.

---

## 3단계 구현 메모 (gameplay-programmer, 2026-10-21)
결과(원본): EditMode **695/695**, PlayMode **51/51** 통과(종료 코드 0, 컴파일 경고 CS 0). 커밋 안 함.
로그·결과: `scratchpad/m4s3_editmode1.*`(통합 첫 실행), `m4s3_setup1.log`·`m4s3_setup2.log`(CreateAll 2회), `m4s3_snap1.txt`·`m4s3_snap2.txt`(GUID·md5), `m4s3_editmode2.*`, `m4s3_playmode.*`.

통합 점검 (원본, 1단계 + 2a/2b/2c/2d 첫 동시 컴파일)
- 컴파일 오류·경고 **없음** — 다른 단계 파일은 하나도 고치지 않았다.
- 2c가 본 `RaidResultViewModel.cs` CS0102(`DurationText` 중복)는 2b가 이미 고쳤다(프로퍼티 1개, 컴파일 통과).
- `UiText.InteractionVerb(Activate) = "작동"` 확인(2b).
- 첫 EditMode 692개 중 실패 19 + 불확정 1 = 모두 `M4DataAssetTests`(원본에 M4 데이터 에셋이 아직 없어서). CreateAll 실행 후 전부 통과.
- 새 파일·폴더 `.meta`는 이 실행들에서 Unity가 만들었다(커밋 대상).

새·수정 파일
- 새 `Scripts/EditorTools/M4SceneSetup.cs` — 메뉴 `Ashfall/Setup/Create M4 Raid Scenes`(설계 §4.6의 "Create M4 Scenes" 대신 오케스트레이터 지시 이름), 배치 `CreateAll`.
  순서: `M4DataSetup.CreateData()` → (NewScene 전) PanelSettings·`EnsureZoneMaterial`·`EnsureEnemyMaterials`·SaveAssets →
  Raid 씬: NewScene(Default) → MainCamera 삭제(Directional Light 유지) → `MapAssets.LoadDefaults()` → `BuildMap(DefaultBake)`(실패면 저장 안 하고 예외) →
  ProjectileSystem(기본값, 디버그 궤적은 Scene 뷰 전용이라 그대로) → `CreateDirector` → `PlaceEnemies` → 플레이어(`M2SceneSetup.CreatePlayer`, 위치 = SP-W 지면 + 0.05m, yaw = 중앙 방향; `startingItems` 끝에 ITM-MAP-01 추가, 장착 안 함·퀵슬롯 -1) →
  `CreateGameUI` → `ValidateNavigation`·`ValidateEnemyStarts` → `Scenes/Raid_CrimsonBasin.unity` 저장 →
  MainMenu 씬: NewScene(Default) → Light 삭제, 카메라 SolidColor 검정 → "MainMenuUI"(UIDocument = MainMenu.uxml + GamePanelSettings, `MainMenuRoot`), EventSystem 없음 → 같은 경로에 덮어씀 →
  빌드 설정 Boot → MainMenu → Raid_CrimsonBasin. 확인(경로·적) 실패 시 씬은 저장하고 마지막에 예외(배치 종료 코드 ≠ 0).
- 수정 `Tests/EditMode/BuildSettingsTests.cs` (+3: Raid 포함, 순서 정확히 3개, 빌드 씬 파일 존재).
- 새 `Tests/PlayMode/RaidSceneSmokeTests.cs` (2): Raid 씬 이름 로드 → Phase Raid, RaidController 1·세션 시작, IRaidInfo(= 컨트롤러)·IPlayerTarget·IPlayerPlacement·IProjectileService·IInventoryService 등록,
  스폰 3곳 중 `SpawnId` 스폰과 수평 1m 이내 + NavMesh 위, 활성 탈출구 2~3·EXT-01 포함·IRaidInfo와 수 일치, POI 5, 적 20 최상위·`isOnNavMesh`·AiDirector 등록 20·Target 일치,
  3초 실행 후 세션 진행·미종료·timeScale 1·여전히 NavMesh 위(예외·LogError 자동 실패). MainMenu 이름 로드 → Phase MainMenu, MainMenuRoot 1·활성·Presenter, PanelSettings·UXML, `menu-panel`·시작·종료 버튼, 카메라 1, 커서 잠금 해제.
- 생성 에셋(원본, 처음): `Scenes/Raid_CrimsonBasin.unity`(1.89MB), `Scenes/Raid_CrimsonBasin/`{Terrain 549,176 B, NavMesh 997,484 B}, `Art/Materials/M4_ExtractionZone.mat`,
  `Data/Raid/RaidConfig.asset`, `Data/Items/Raid/ITM-FLR-01·ITM-MAP-01`, `Data/Loot/LT-CRATE.asset`. 수정: `Scenes/MainMenu.unity`(GUID 3b426db… 유지), `Data/UI/HudConfig.asset`(+mapItem), `ProjectSettings/EditorBuildSettings.asset`(+Raid).

측정 (원본, 1회차)
- NavMesh 굽기 16.5s, 도형 197·엄폐 494·상자 21, 적 20(SCV 12 + RDR 8), 스폰과 가장 가까운 적 192m. 경로 확인 9쌍(spawnRules) 모두 Complete.

재실행 결정성 (CreateAll 2회, `m4s3_snap1/2.txt` diff)
- `.meta` GUID 불변: Raid 씬 1d8dcf31…, MainMenu 3b426db…, Boot, 폴더 `Raid_CrimsonBasin`, Terrain 00e3b7b5…, NavMesh 69493030…, 구역 재질, RaidConfig·신호탄·지도·LT-CRATE·HudConfig.
- **바이트 동일(md5)**: Terrain(7b3105f7…), NavMesh(fef47159…), 구역 재질, M4 데이터 4개, HudConfig, EditorBuildSettings.
- 씬 YAML 2개만 다름 — 로컬 fileID가 매번 새로 매겨진다(기대 동작, GUID·참조는 같음).

4단계가 알 것
- Raid 씬은 빌드 설정에 있으므로 `SceneManager.LoadSceneAsync(SceneNames.RaidCrimsonBasin)`로 불러 `Flow.Phase == Raid`가 된다(종료 시 MainMenu로 실제 전환).
- 테스트 러너에서 Raid → MainMenu 로드 순서로 문제없음(GameManager 유지, 구독 누수 확인은 4단계 RaidLoopFlowTests).
- PlayMode 로그의 "[RaidController] 흐름 밖 씬" 경고 2건은 PlayerPlacementTests(코드로 만든 씬)에서 나오는 기대 동작.

---

## 4단계 구현 메모 (test-engineer, 2026-10-21)
결과(원본): EditMode **697/697**(695 + RaidSceneModuleTests 2), PlayMode **64/64** 2회 연속(51 + 새 13), 종료 코드 0, 컴파일 경고 0. 커밋 안 함.
로그·결과: `scratchpad/m4s4_editmode.*`, `m4s4_playmode1.*`, `m4s4_playmode2.*`, `m4s4_pm_m4_1~3.*`(M4만 반복 실행), `m4s4_build.log`.

### 4단계 구현 메모 — 고친 제품 설정 (1건)
- **Terrain Physics 내장 패키지가 꺼져 있었다** → `Packages/manifest.json`에 `"com.unity.modules.terrainphysics": "1.0.0"` 추가(`packages-lock.json`은 Unity가 갱신).
  - 증상: 레이드 씬을 열 때마다 로그 "TerrainCollider component deleted: Component belongs to a disabled built-in package." → 지형 충돌 없음 → **플레이어가 스폰 직후 지형을 뚫고 떨어진다**(탈출 구역 높이 허용 3m를 벗어나 대기 초기화). 탄·AI 시야 레이도 지형을 통과했다. 빌드도 같다.
  - 3단계 스모크가 놓친 이유: 플레이어 위치를 수평 거리·로드 직후 NavMesh 샘플로만 봤다(낙하 전). NavMesh 굽기는 TerrainData를 직접 읽어 정상이었다.
  - 매니페스트에 모듈 목록이 전혀 없는 프로젝트(M0)라 이 모듈만 명시했다. 회귀 방지: EditMode `RaidSceneModuleTests`(매니페스트·형식 로드), PlayMode `RaidMapTests`(지형 레이 = TerrainCollider).

### 새 테스트 (Assets/Tests/PlayMode, 도우미 `M4TestSupport.cs`)
- `M4Raid`: 레이드 씬 이름 로드 + **스폰 고정**(sceneLoaded가 Awake·OnEnable 뒤·Start 전에 오는 점을 이용해 `RaidController.seed`를 리플렉션으로 바꿈 — 같은 규칙으로 원하는 스폰이 뽑히는 시드 계산), 구역 순간이동(`IPlayerPlacement`), 적 제거, 머리 피격(IDamageable), 소지품 스냅샷. `M4Menu`: MainMenu UI 참조. `UiPointer`: UI Toolkit 버튼 클릭(아래).
- `ExtractionFlowTests`(6): EXT-01 10초·이탈 초기화·MainMenu 전환 / EXT-02 NoPower → 스위치 앞에서 실제 F(SimKeyboard, 프롬프트 "작동") → 15초 / EXT-03 TooEarly → AdvanceTime(900) → MissingItem → 신호탄 2 → 20초, 1개 소모·1개 반출 / EXT-04 배낭 Blocked → 해제 7초, 대기 중 재착용 초기화, 조끼 허용 / 비활성 EXT-03(SP-N): 조건 다 갖춰도 HUD·탈출 없음 / 1499초 → 실제 1초 뒤 MIA, 결과 시간 1500, 몸은 살아 있음.
- `RaidLoopFlowTests`(3): Boot → MainMenu → [레이드 시작] → 머리 사망("사망 — 머리", 반출 0, 소실 = 소지품) → [확인] → 2회차(같은 고정 키트) 상자 테이블 아이템 획득 → EXT-01 탈출("탈출 성공 — 남쪽 도로", 반출 = 소지품, 획득품 포함) → [확인], 레이드 관련 이벤트 구독 수가 메뉴 기준과 같음(누수 없음) / MIA("작전 중 실종 (MIA)", "레이드 시간 25:00") / Esc 일시정지(timeScale 0, 타이머·적 위치·상태 1.5초 불변, Tab·M 무시) → [계속] → 재개 → Esc 토글 → [레이드 포기] → [포기] → "레이드 포기". 종료 오버레이는 세계 정지·Esc로 안 닫힘. 결과 패널 줄은 "정의 표시 이름 (×n)"과 정확히 같다(현재 탄 수·횟수 없음).
- `RaidMapTests`(2): Terrain 1000×120×1000·원점 중앙·지형 충돌, POI 5곳 = §8.1 이름, 스폰 3 × 활성 탈출구 9쌍 경로 Complete(601~1025m), 네 방향 경계에서 CharacterController로 30m 밀어도 494.62m에서 멈춤 + 바깥 레이가 495m의 Bounds 벽에 맞음, 서쪽으로 실제 질주 8초도 -494.62에서 멈춤 / 지도(M): POI 5 + 활성 탈출구 3만, 비활성 없음, 표식 외 요소 없음, 플레이어 이동 후 표식 동일, `IRaidInfo` 멤버 5개(위치 없음), "남은 시간: 약 N분", 지도 아이템 없으면 안 열림.
- `RaidPerformanceTests`(1): 아래 수치. `UiToolkitInputPathTests`(1): 아래 2b 확인.
- EditMode `RaidSceneModuleTests`(2): 위 회귀.

### 2b "확신 없는 API" 결과
- **Button.clicked는 EventSystem 없이 timeScale 0에서 동작한다**(합성 PointerDown/PointerUp을 버튼에 보내는 Clickable 경로): 일시정지 [계속]·[레이드 포기]·[포기], 메뉴 [레이드 시작]·[확인] 모두 clicked → 기대 동작. 이벤트 처리는 timeScale과 무관.
- **가상 마우스(Input System 장치 → InputForUI → UI Toolkit) 실제 경로는 배치모드에서 확인 불가**: 패널↔화면 좌표 변환(640×480, 패널 스케일)과 Input System 설정(IgnoreFocus)을 맞춰도 clicked가 오지 않았다. 진단: `DefaultEventSystem.isAppFocused = false`(계산 프로퍼티, `Application.isFocused` = false, 배치모드엔 창 포커스 없음), `useInputForUI = true`, `isInputReady = true`. 포커스 없는 앱의 포인터 입력을 무시하는 것으로 보인다(강제할 수 없어 인과는 추정). → 실제 마우스 클릭은 **에디터 사람 확인**(Pause [계속]/[포기], 메뉴 버튼).
- `EditorApplication.ExitPlaymode()`([종료])는 누르면 테스트 러너가 끝나므로 자동 테스트하지 않았다(사람 확인). USS `letter-spacing` 임포트 경고 없음(로그 확인).

### 성능 (배치모드, 노트북 i7 + HD 620, 렌더 없음 — 2회 실행 범위)
- 엄폐 지점 494, 적 20, Terrain 1km.
- A) 자연 교전(무적 표적을 고속도로 남쪽 도로에, 총성 → 고속도로 5명 교전·15명 순찰, 10초): AI 틱 평균 0.072~0.080ms, 99% 0.18~0.23ms, 최대 0.67~1.14ms / 투사체 평균 0.012~0.014ms / 프레임 중앙값 0.97~1.01ms(99% 2.1~2.6ms).
- B) 전원 교전(나머지 15명을 표적 35~55m 고리로 옮김, 20명 Combat, 10초): AI 평균 0.097~0.099ms, 99% 0.27~0.28ms, 최대 0.75~1.29ms / 투사체 평균 0.024~0.025ms / 프레임 중앙값 1.00ms(99% 2.2~2.3ms).
- 엄폐 질의(CoverSelector.TrySelect 494개 선형 탐색) 1회: 교전 위치 30µs(후보 없음), 후보 많은 경우(상위 후보 레이까지) 36µs. 적 20명이 엄폐를 고르는 빈도에서 무시할 수준 → M3 TODO의 균일 격자는 아직 불필요(1000개 기준 유지).
- M3 대비(M3_AIPerf 같은 회차): AI 평균 0.086ms·99% 0.23ms — Raid 씬 B가 약 +0.01ms.
- 한계: GPU·Terrain 렌더·URP 비용 미포함. 1080p 60fps(GTX 1660)는 에디터 Stats/Profiler 또는 개발 빌드로 사람이 확인.
- 참고: 일시정지 중에도 `AiDirector.Update`는 돌아 Stats 샘플은 늘지만(1.5초에 약 2300), 적 위치·상태는 변하지 않는다(due 없음).

### 사람 확인 (에디터, 자동으로 못 본 것)
- 실제 마우스로 버튼 클릭(메뉴 시작/종료, 결과 [확인], 일시정지 [계속]/[레이드 포기]/[포기]), 커서 잠금/해제(배치모드에서는 lockState가 항상 None이라 확인 생략).
- 1080p 60fps(Raid 씬, 적 교전 포함), 맵 걸어 보기(지형 충돌·경계·끼임), HUD 탈출 문구 가독성, 결과 패널 문구·목록 배치.
- 결과 패널의 탄창 이름 "5.56 탄창 (30발)"은 용량(고정 규격)이라 허용으로 판단 — 현재 탄 수는 표시되지 않음. 기획 판단이 다르면 표시 이름 정리(M6).

---

## 리뷰 수정 메모 (gameplay-programmer, 2026-10-21)
대상: `M4_review.md` 중간 1 + 낮음 2~8. 커밋 안 함. CHANGELOG_GDD는 건드리지 않았다(오케스트레이터 몫).
결과(원본): EditMode **715/715**, PlayMode **66/66** 2회 연속(각 약 7분 28초), 종료 코드 0, 컴파일 경고 CS 0. Windows 빌드 종료 코드 0(Succeeded, 오류 0, 경고 2, 107,155,086 B).
로그·결과: `scratchpad/m4s5_editmode1.*`, `m4s5_playmode1.*`, `m4s5_playmode2.*`, `m4s5_build.log`.

설계 변경점 (본문 §1.3·§1.4와 다른 것)
- **세계 정지 해제 시점**: `GameFlow.CompleteRaid`는 정지를 풀지 않는다. MainMenu 로드 완료(`OnSceneLoaded`)가 푼다. 종료 → MainMenu 로드 완료까지 레이드 세계는 계속 정지.
  MainMenu 로드 요청이 실패하면 결과는 남기고 정지만 풀고 LogError(영구 정지 방지, 오버레이·잠금은 유지).
- **종료 잠금 (RULE-01)**: `RaidEnded`(결과 스냅샷 직후 발행)를 받으면
  - Inventory: `PlayerInventory.IsRaidEndLocked = true` → `IsFrozen`(= 사망 || 종료) 하나로 모든 변경 거부 — 이동·장착·버리기·줍기·퀵슬롯·`TryUse`·`TryConsume`·`TakeFullest`·`TryStore`·루트 열기, 그리고 **`ConsumeMedicalUse`(사용 완료 차감)도 잠금 중 무시**(사망 경로에도 적용). 열린 루트는 닫지 않는다(오버레이와 LootSessionChanged가 엉키지 않게). F 대상·퀵슬롯 입력도 멈춤.
  - Weapons: `WeaponController`가 `CancelReload` + 탄창 확인 취소, 이후 `Model.Tick`·입력 정지(`raidOver`, 사망과 같은 처리).
  - Player: `MedicalUseModel.Cancel()`(새, 효과 없이 비움) + 이후 `MedicalUseRequested` 무시. 아이템 잠금은 풀지 않는다(인벤토리도 잠기고 씬이 내려간다).
- `IInventoryService.CanConsume(def)` 추가(Core): TryConsume과 같은 범위(저장 공간·잠기지 않은 것·잠금 아님). EXT-03 대기 판정이 `Contains` 대신 사용. `Contains`는 지도(보유 여부)용으로 그대로.
- `RaidSession.Tick(dt, IPlayerTarget, inventory)` 오버로드 + `EndByDeath()`(원인 불명): 대상이 `IsDead`면 탈출·MIA 판정 전에 사망 종료. RaidController.Update가 이 오버로드를 쓴다(PlayerDied를 놓친 경우 안전망). 결과 문구는 "사망"(부위 없음).
- `RaidController.OnSessionEnded`: 결과·HUD 숨김·RaidEnded를 try, 정지·완료 예약을 finally(`FinishEnding`). 결과를 못 만들었으면 소지품 없는 결과로 대신(LogError). RaidEnded 이중 발행 없음.
- 흐름 밖(Phase None) 종료: 의도된 동작으로 주석 명시 — 씬 유지, 결과는 LastResult, 정지 해제, 오버레이·조작 차단 유지(레이드가 끝나 할 일 없음, 인벤토리·무기도 잠김). 정상 경로(Raid 씬을 이름으로 로드 또는 에디터에서 바로 Play)는 Phase Raid라 오지 않는다.
- `GameFlow.ReturnToMainMenu` **삭제**(호출처 없음, 레이드 중 RULE-01 우회 위험). 낮음 2에서도 쓰지 않는다.
- `GameFlow` 생성자 대리자 `Action<string>` → `Func<string, bool>`(요청 성공 여부). GameManager는 `LoadSceneAsync(...) != null`. `StartRaid()`·`CompleteRaid()`가 bool 반환. 요청 실패·대리자 예외 → false + LogError, 로드 중 표시 해제(재시도 가능).
  `MainMenuPresenter.StartRaid`: GameFlow가 false면 IsStarting을 켜지 않고 false(버튼 영구 비활성 방지). 비동기 로드가 요청 뒤 실패하는 경우는 Unity가 알려 주지 않아 다루지 않음.
- `RaidStarted.SpawnId`: "UI 표시 금지(RULE-06)" 주석. 필드 유지.

바뀐 파일
- Core: `Flow/GameFlow.cs`, `GameManager.cs`, `Services/IInventoryService.cs`(+CanConsume), `Events/GameEvents.cs`(주석)
- Raid: `Logic/RaidSession.cs`, `Runtime/RaidController.cs`
- Inventory: `PlayerInventory.cs`, `PlayerInventoryBehaviour.cs` / Weapons: `WeaponController.cs` / Player: `MedicalUseModel.cs`, `PlayerHealth.cs` / UI: `MainMenuPresenter.cs`
- 테스트: EditMode `GameFlowTests`, `MainMenuPresenterTests`, `RaidSessionTests`, `PlayerInventoryTests`, `InventoryConsumeTests`, `MedicalTests`, `M4TestData`(FlowRecorder.LoadSucceeds), `UiTestFakes`(CanConsume, TryConsume 잠김 제외) /
  PlayMode 새 `RaidEndFreezeTests.cs`(.meta는 배치 실행에서 Unity가 생성)

추가 테스트 (EditMode +18 = 697 → 715, PlayMode +2 = 64 → 66)
- GameFlow: CompleteRaid 후 정지 유지 → OnSceneLoaded에서 해제(기존 "Unpauses" 테스트 대체), CompleteRaid 로드 실패 → 결과 유지·정지 해제, StartRaid 로드 실패 → false·재시도, 대리자 예외 → false. (ReturnToMainMenu 테스트 삭제)
- MainMenuPresenter: 로드 실패 시 IsStarting false·재시도, GameFlow 거부 시 IsStarting false.
- RaidSession: 죽은 몸 탈출 안 함(Killed, 원인 없음), 시작부터 사망 + 시간 초과 → Killed, 살아 있으면 위치 오버로드와 같음, 종료 후 EndByDeath() 무시 / EXT-03 잠긴 신호탄만 → MissingItem·대기 없음·TryConsume 호출 없음 → 잠금 해제 후 탈출, 장착 슬롯 신호탄만 → MissingItem.
- PlayerInventory: 종료 잠금이 모든 변경 거부(사용 완료 차감 포함, Changed 0회), 종료 잠금 중 재장전 Tick이 탄창을 바꾸지 않음.
- InventoryConsume: CanConsume 저장 공간/잠김/장착/사망·종료/할당 없음, 가짜 서비스 CanConsume.
- Medical: Cancel → 효과·완료 없음.
- PlayMode `RaidEndFreezeTests`: (1) EXT-01 대기 중 재장전 시작 → 탈출 순간 취소 → 정지를 일부러 풀고 재장전 시간 + 0.3초 진행해도 재장전 미완료·장착 탄창 동일·CarriedOut = 실제 소지품(인스턴스 중복 없음, 장착 탄창이 저장 공간에 없음, 탄창 수 보존) → MainMenu.
  (2) EXT-01 대기 중 붕대 사용 시작 → 탈출 순간 취소·인벤토리 잠금 → MainMenu 로드 완료 전 매 프레임 timeScale 0·사용 횟수 불변 → 메뉴에서 timeScale 1, 사용 완료 이벤트 0·출혈 그대로·CarriedOut = 실제 소지품.

남은 것 / 사람 확인
- 에디터: 탈출 대기 중 R → 탈출 오버레이 → 메뉴 결과의 탄창 수가 소지품과 같은지 한 번 확인(자동 테스트로 확인됨, 선택).
