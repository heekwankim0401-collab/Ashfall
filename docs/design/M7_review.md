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
