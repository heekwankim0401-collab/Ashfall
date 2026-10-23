# M7 코드 리뷰 지적 (code-reviewer, 읽기 전용)

하위 단계마다 리뷰한다. [확인] = 코드로 확인, [추정] = 실행 없이 추론. 경로는 `Assets/_Project/Scripts/` 기준.

## 7A 무기 — 높음 없음, 중간 2, 낮음 9

범위: 46857a7 대비 작업 트리(설계 §4.1~§4.3·§4.9~§4.11, 7A-1·1b·2a·2b·2c·3 메모의 파일).

### 중간
1. 튜브 장전 중 방아쇠가 실제 발사가 안 되는 경우에도 장전을 끊는다 → Q7("1발 이상이면 장전 중단 후 사격")과 다름 — Core/Combat/WeaponModel.cs:65-71.
   [확인] `TryFire`가 `fireControl.Tick` 전에 `CancelReload()`를 부른다. (a) 좌클릭을 누른 채 R → Semi라 `pressedNow=false`, 장전만 끊김(R이 무효). (b) 70RPM 간격 안 클릭 → 장전만 끊김.
   고칠 방향: 발사 판정을 먼저 하고 true일 때만 장전 취소 후 발사. 두 경우 테스트 추가.
2. 고장 상태가 은신처 귀환 뒤에도 같은 실행의 다음 레이드까지 남는다 → Q14 A("은신처 귀환 = 해제")와 다름 — Core/Items/WeaponItem.cs:24, Core/Save/ProfileService.cs:234-249.
   [확인] `IsJammed`를 푸는 곳은 `WeaponModel.ClearJam`뿐이고, 탈출 시 같은 인스턴스가 은신처 장비가 된다. 재시작(세이브 읽기)하면 풀려 동작이 달라진다.
   고칠 방향: `ProfileService.ApplyRaidResult`(장비가 되는 Loadout)에서 `IsJammed=false`. 흐름 테스트에 "고장 난 채 탈출 → 다음 레이드 해제".

### 낮음
3. Tests/EditMode/AudioConfigAssetTests.cs:105-121(M6 Rerun), M7WeaponDataAssetTests.cs:443-468 — 커밋 에셋에 실제 메뉴를 다시 돌리는 테스트 위험이 커졌다(M6 리뷰 낮음 5의 연장). M6 테스트가 이제 M7 WAV·AudioConfig 세트까지 만든다(7A-3에서 실제 발생).
   고칠 방향: M7 경로 Assume 또는 `Recipes()`만 쓰는 확인, 데이터 재실행은 `WriteM7WeaponData()`로 좁히고 머리 주석에 위험 기록.
4. EditorTools/M7WeaponDataSetup.cs:221 + M7WeaponAssets.cs:133-143 — 기존 무기 3종 `durabilityLossPerShot`을 "필드가 없을 때만" 써서, 뷰모델 메뉴를 먼저 돌리면 0으로 굳는다(지금 에셋 값은 맞음).
   고칠 방향: 0이면 미설정으로 보고 쓴다, 또는 뷰모델 메뉴가 정의가 모자라면 아무것도 쓰지 않음.
5. Weapons/WeaponController.cs:392·427·439 — 좌클릭을 누른 채 교체가 끝나면 반자동 무기가 새로 당기지 않아도 한 발 나간다(교체 중 `triggerWasHeld`가 false로 됨).
   고칠 방향: 교체 중에도 실제 방아쇠 상태를 FireControl에 넘기고 발사만 막는다.
6. 7B 선행: Core/Data/ItemEnums.cs:21 `WeaponClass.MachineGun`, WPN-LMG-01·MAG-762-100, M6AudioSetup.cs:61 `"Lmg"`. 이유는 7A-1b 메모에 있음. 7B-1이 다시 추가하지 않게 설계·CHANGELOG에 기록.
7. Weapons/WeaponController.cs:340-342 — 테스트 도우미(`M3TestSupport.PlayerMuzzle`의 첫 "Muzzle" 검색)에 맞춘 `SetAsFirstSibling()`. 도우미가 `WeaponController.MuzzlePosition`을 쓰게 하고 줄 제거. :361 `MuzzleChildName`과 `M7Names.MuzzleChild` 중복에 "같이 바꿀 것" 주석.
8. Weapons/WeaponController.cs:79-80 — 무기 모델 캐시를 지우지 않음(영향 작음). "의도적으로 유지" 주석.
9. Core/Save/SaveSerializer.cs:154 낡은 주석("CurrentVersion(1)"), :281-286 음수 내구도 → 100은 설계 "0~100 자름"과 다른 해석 — CHANGELOG에 기록.
10. 문서: CHANGELOG "M7 설계 결정" 없음. 7A 설계 차이(MachineGun 이동, 프리팹 `VM_{id}`, WAV `Weapon/WPN_DryFire.wav`, AudioConfigSO 코드 기본 gunshots, T 짧게/길게 = 고장 여부, 생성자 선택 인자, `M5DataSetup.CreateItemDatabase` 공개) 기록.
11. 테스트 계획: 7A-4 `M7WeaponRaidFlowTests`(진행 중이면 무시). v1 → v2 이전이 실제 ProfileService + 파일 경로로 아직 확인 안 됨.

### 확인한 것 (문제 없음)
- 의존 방향: Weapons·Inventory·Audio → Core만. 새 asmdef 없음. `ILooseAmmoSource`·`WeaponClicked`·`JamRules`·`WeaponWear`는 Core.
- 싱글톤 GameManager 하나, 옛 `Input.Get*` 없음, `Weapon1/2/3` = 키 1·2·3(§12.1, 충돌 없음).
- 구독·등록 짝(WeaponController 5개 이벤트, ILooseAmmoSource 등록, AudioDirector WeaponClicked).
- 고장 굴림(발사 성립 + 탄 있음일 때만, 고장 = 탄·마모 없음), 마모 0~100, 적 무기 마모 없음(Q18), 튜브 한 발씩·종료·취소·정지, 낱탄 순서·상자 제외, 교체 규칙(Q1·Q2), T 동작, 헛격발, 펠릿 퍼짐(Q8), 세이브 v2 규칙.
- 할당: 발사·고장·튜브·교체·클릭 경로 할당 없음.
- 수치: §5.2 SHT 70RPM·18×8·튜브 6, DMR 40RPM·110·5, §5.1 임계 50·0.1%/점. GDD 밖 값은 SO + TODO(GDD-?).
- M6 회귀: `Recipes()`·`GunshotClasses` 그대로, SfxSynth 기존 분류 바이트 동일, M6 테스트 변경은 기대 목록 이어 붙이기 하나.
- 7B·7C 선행은 낮음 6 외 없음. §5.3 방어구(Q19 B)·약실 탄·수리·내구도 표시 없음.

### 7A 처리 결과
지적 11개 모두 수정(보류 없음) — 처리 내용은 M7_design.md "7A 리뷰 수정 메모". 수정 후 원본 EditMode 1472/1472, PlayMode 142/142 × 2, Mono 빌드 성공(경고 0).

## 7B AI — 높음 1, 중간 3, 낮음 10

범위: 7ad3d1a 대비 작업 트리(7B-1·1b·2a·2b·2c·3 메모의 파일).

### 높음
1. 변이체 타격 지점이 플레이어 부위 판정과 어긋나 선 자세에서도 약 10%가 머리, 웅크림·엎드림에서는 대부분 머리 — Q24("흉부·복부·팔")와 다름. AI/Runtime/EnemyAgent.cs:340-360, AI/Combat/EnemyMelee.cs:18-22, Player/PlayerHealth.cs:21·79-81.
   [확인] (a) PlayerHealth가 지점을 액터 배율 0.8로 환산 → 흉부 상단이 headZoneFrom 0.87을 넘음. (b) 표적 캡슐 높이·반지름을 Root가 바뀔 때만 캐시 → 자세 변화(1.8/1.2/0.6m) 무시. 테스트는 배율 없는 원시 비율·가짜 표적만 확인.
   고칠 방향: ① Core `DamageInfo`에 부위 힌트를 두고 PlayerHealth가 그 부위를 쓴다(오케스트레이터 선택) — 분포는 부위 가중치(SO). 테스트: 실제 Player(선·웅크림·엎드림)를 쳐서 머리·다리 0회.

### 중간
2. 거점 반경 밖 엄폐를 골라 경계에서 엄폐 아닌 곳에 숨기·A↔B 반복 가능, 엄폐 없음 대체 행동에서 40m 밖 표적을 쏘지 않음 — AI/Brain/TacticsContext.cs:86-137, RaiderTactics.cs:108-113·258-266·285-310, ScavengerTactics.cs:64-71.
   고칠 방향: `CoverQuery`에 거점 중심·반경 → 반경 밖 후보 제외(Q29 TODO 해소), 거점 있는 적은 대체 행동에서 다가가지 않고 사거리 안이면 제자리 사격. 테스트 2개.
3. 수치 하드코딩: EnemyMelee.cs:18-22 타격 분포 7개(PlayerConfig 구간 사본 — 높음 1 원인), MutantTactics.cs:103-104 비율 0.5·0.75, EnemyAgent.cs:353 반지름 0.2. SO로(①이면 분포 상수는 부위 가중치로 바뀜).
4. PlayMode 전체 미통과·34명 성능 미측정(커밋 차단) — RaidPerformanceTests·M6PerformanceTests. 7B-4에서 (b) 사격 유형만 교전 전제 + 실제 사격 확인으로 처리 중.

### 낮음
5. EnemyAgent.cs:315-320 근접 사거리 수평만 — 높이 차 상한(SO) 추가 또는 의도 기록.
6. EnemyBrain.cs:153-154 거점 중심이 대원마다 자기 첫 위치 — 분대 리더(보스) 위치 공유 또는 기록.
7. AnchorRules.cs:20 자른 점 y = 원래 목적지 높이 — NavMesh 샘플로 바닥에 붙임.
8. CorpseLootBuilder.cs:8·33 주석이 동작보다 넓음 — 주석 수정.
9. M7EnemySceneApply.cs:210-254 실패 시 씬이 반쯤 바뀜(에디터) — 검사·스냅 계산 먼저, 그다음 씬 변경.
10. EnemyAgent.cs:382-391 무리 4명 MutantAlert 동시 재생 — 분대 단위 쿨다운 또는 합치기.
11. M7AiDataAssetTests.cs:497-524 커밋 에셋 재실행 테스트(7A 낮음 3과 같은 위험, 범위 좁힘·주석 있음) — 커밋 전 git status 확인 절차.
12. `RaidPerformanceTests.RaidScene_TwentyEnemies_…` 이름이 낡음(34명).
13. AiDirector.cs:106·25-26 초기 용량 32 < 34 — 64로.
14. CHANGELOG에 7B 결정 없음 — Q22~Q31, `LootTableSO.guaranteedFirstRoll`, 스폰 내구도 = 시체가 될 때(살아 있는 적 무기 100), `PickStrikeOffset`·`TryTickRelaxed`·`LookAround`, 울음 WAV 경로, 근접 시야 = 감각 주기, 훑기 폭·주기, Alert에서 섬광, (100,100) = 미설정.

### 시야 가림 진단 의견
적끼리 시야 가림은 탄도와 일관된 규칙(아군 오사 방지)이라 실제 플레이 결함으로 보지 않는다. 34명이 한 점에 모이는 것은 테스트가 전원을 끌어들일 때만 생긴다. (c) AI 규칙 변경은 하지 않는다. 변이체가 붙은 플레이어 옆에서 SCV가 멍하니 서는 장면은 사람 확인 항목.

### 확인한 것 (문제 없음)
- §13.3: AI·Audio → Core만, 새 asmdef 없음, 정적 인스턴스 없음, 옛 Input 없음.
- 저격수: Hold, Alert부터 섬광·Combat ∧ 1.5초 ∧ ≤ 200m에서만 사격, 안 보이면 경고 재시작, 폭풍 cap, 섬광 할당 없음·사망 시 끔.
- 변이체: 준비 → 타격/헛침 1회, 간격, 둘레 각도·좁힘, 표적·자기 사망 처리.
- 루트·내구도: guaranteedFirstRoll null이면 기존 난수 순서, enemyOnly 시체 제외, 유형별 범위.
- 오디오 구독 짝·할당 없음, 씬 적용 멱등·실패 시 예외, M4 메뉴 재생성 시 유지.
- M3·M6 회귀: SCV·RDR Auto·필드 0 → 기존 경로, 생성자 난수 없음.
- 7C 선행 없음.

### 7B-4 검증에서 추가 (test-engineer)
15. (중간) 변이체가 표적과 수평 0m까지 겹친다(기대: 사거리 × 0.75 ≈ 1.35m에서 멈춤) — AI/Brain/MutantTactics.cs Engage. [확인] 무리 테스트 최근접 0.0m 재현. [추정] 같은 무리에 시야가 막힌 Tick에 LKP(표적 발)로 질주, 멈춤 처리가 목적지만 지워 감속 미끄러짐. 영향: 몸 겹침, 다른 적 시야 차단으로 사격 압박 소실.
    고칠 방향: 안 보일 때도 LKP 대신 둘레 자리로, 또는 LKP를 사거리 × 0.75 앞에서 끊고 멈출 때 속도 정지.

### 7B 처리 결과
지적 15개 모두 수정(11은 커밋 전 git status 확인으로 처리) — 처리 내용은 M7_design.md "7B 리뷰 수정 메모". 수정 후 원본 EditMode 1596/1596, PlayMode 163/163 × 2, Mono 빌드 성공(경고 0), ApplyAll 2회 멱등.

## 7C 생존·제작 — 높음 0, 중간 1, 낮음 8

범위: 7eccabb 대비 작업 트리(7C-1·1b·2a·2b·2c·3 메모의 파일).

### 중간
1. 수분 0 사망 시점이 Q33 A 문구("흉부 80 → 160초")와 다르다 — Player/BodyHealthModel.cs:126-140, UI/RaidResultViewModel.cs:64. [확인] 부위마다 0.5/s라 머리 35가 70초(피폭 중 56초)에 먼저 0 → 사인 "머리". CHANGELOG에 차이 없음.
   오케스트레이터 결정: (b) 탈수 피해에서 머리를 뺀다(흉부 80 → 160초, 사인 흉부). 전신 사인 문구("탈수")는 MVP 남은 목록.

### 낮음
2. CHANGELOG에 7C-2a·2b·2c·3 결정 없음(1번, 수분·에너지 public 읽기, 전신 대상 Thorax, 탈수 피해 분산·배율·굴림 없음, 테스트 씬에서도 감소, 읽기 전용 제작 Unavailable, 무효·중복 레시피 제외, 재료 배치 순, 가이거 = 폭풍 + 구역).
3. 읽기 전용에서 상인은 되고 제작만 Unavailable — HideoutService.cs:195 대 :144-162. 결정: 유지하고 사유를 CHANGELOG에 기록.
4. 재료 칸 충족 표시(줄 단위)와 버튼 판정(정의 합계)이 어긋날 수 있음 — UI/CraftingViewModel.cs:77-78, Core/Services/IHideoutService.cs:62. 합계 기준으로.
5. 탈수 루프가 사망 뒤에도 남은 부위를 깎음 — BodyHealthModel.cs:131-138. 사망하면 break.
6. 생존 설정 범위 가드 없음 — PlayerConfigSO.cs:136-164. [Min]/[Range] 또는 자르기.
7. 낡은 주석 — Audio/Logic/GeigerClickModel.cs:5("폭풍에만 반응").
8. 커밋 에셋 재실행 테스트 위험 지속(M7SurvivalDataAssetTests.cs:403-422, M6 AtmosphereAssetTests가 M_Toxic까지 만듦) — 커밋 전 git status·md5 확인 유지.
9. CLAUDE.md "구조"에 7C 타입 없음(마일스톤 끝에 갱신). 희귀도 WPN-PST-01·WPN-SMG-01·ITM-FLR-01 = Common 유지(오케스트레이터 결정, M3·M4 결정·테스트 보존).

### 확인한 것 (문제 없음)
- §13.3: 기능 → Core만, 공용 타입은 Core, 정적 인스턴스 없음, 옛 Input 없음.
- RULE-05: IBodyStatusView는 등급만, 수치는 Player 안, "전신" 줄 문구·숫자 없음(정규식 테스트), 제작 탭 숫자는 재료 개수뿐.
- 감소율·복부 ×2·0 멈춤·dt 가드·사망/종료 후 정지, 에너지 0 → 스태미나 재생 ×0.5, 피폭 ×0.8 자르기, 해독제(Q36), 의약품 흐름(Q42), 방독면 = 폭풍만(Q37·Q38), 구역 수학·등록 짝(Q39), 가이거 입력(Q41), 이월 없음(Q32), 세이브 v2 스키마 불변.
- 제작: 보관함만·배치 순·가득 찬 산출·NoSpace 시 통째 원위치·Unavailable/UnknownRecipe·성공 때만 HideoutChanged·W2~W4 저장.
- 할당 0(PlayerHealth.Update·구역·가이거), 수치는 SO + TODO, M5·M6 회귀 없음, 7D 선행 없음.

### 7C 처리 결과
지적 9개 모두 처리(1은 탈수에서 머리 제외, 3·9는 기록, 8은 커밋 전 확인) — M7_design.md "7C 리뷰 수정 메모". 수정 후 원본 EditMode 1737/1737, PlayMode 179/179 × 2, Mono 빌드 성공(경고 0).
