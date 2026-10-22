# M5 코드 리뷰 지적 (code-reviewer, 읽기 전용) — 높음 없음, 중간 2, 낮음 9

범위: 설계 §4의 Core·Hideout·Inventory·Raid·UI·EditorTools 제품 코드(4단계에서 새로 들어온 PlayMode 테스트 제외). [확인] = 코드로 확인, [추정] = 실행 없이 추론.
RULE-01·RULE-03 핵심 경로에서 복제·소실·우회 버그는 찾지 못했다.

## 중간
1. 읽기 오류(I/O)를 손상과 같게 처리 → 진행도 통째 손실 가능 — Core/Save/ProfileService.cs:77-87, :220-232, Core/Save/FileSaveStorage.cs:41-52.
   [확인] `TryRead`가 `Error`(권한·공유 위반)면 Corrupt로 격리 시도 후 새 프로필로 계속, W1이 바로 저장. [추정] 일시 잠금 → 격리도 실패 → 잠금 해제 후 W1이 빈 프로필로 Replace(원본은 .bak) → 다음 저장이 .bak까지 덮으면 영구 손실.
   고칠 방향: Error를 Corrupt와 나누고, 짧게 재시도 후에도 안 되면 그 실행의 저장을 막는다(은신처 읽기 전용, 레이드 시작은 W4 실패로 거부).
2. 진입 절차 예외 시 은신처가 멈춤 — Hideout/Runtime/HideoutController.cs:50-52, Core/Save/ProfileService.cs:67.
   [확인] `SaveSerializer.ToProfile`이 try 밖. [추정] 예상 밖 세이브 내용으로 예외 → Awake 중단, IHideoutService 미등록 → 모든 패널 숨김, 나갈 버튼 없음, 흐름 레이드 표도 남음.
   고칠 방향: EnsureLoaded에서 ToProfile을 try/catch(실패 = Corrupt 격리 + 새 프로필), Awake 예외 시에도 메인 메뉴로 나갈 수 있게.

## 낮음
3. ProfileService.cs:208, UI/HideoutNoticeViewModel.cs:36-39 — ApplyInterruptedRaid가 첫 로드 Status를 복사해 Corrupt/UnsupportedVersion 문구가 다시 붙음. 새 알림 Status는 Loaded로.
4. Inventory/PlayerInventoryBehaviour.cs:123-129 — 레이드에서 반입 적용 거부 아이템은 LogError 후 버려짐(broughtIn·결과에도 없음, RULE-01 위반이 됨). 정상 경로는 같은 InventoryConfig라 거부 없음. 두 씬 InventoryConfig 같은지 데이터 테스트 또는 안전망.
5. Inventory/HideoutInventoryBehaviour.cs:75-81 — 진입 완료를 `IsLoaded`로 짐작, 순서는 `[DefaultExecutionOrder(-100)]`에만 의존. [추정] 두 번째 진입에서 순서가 뒤집히면 반영 전 빈 장비를 놓고 다음 저장이 반출품을 지움. Core에 "진입 완료" 표시 또는 IHideoutService 등록 뒤 배치.
6. UI/HideoutPresenter.cs:187-198, Hideout/Logic/HideoutService.cs:160-171 — W2 저장 실패 메시지가 내려가는 은신처 UI에만 쓰여 보이지 않음. 실패 시 이동하지 않고 메시지(재시도/그래도 이동).
7. UI/HideoutPresenter.cs:181 — StartRaid가 저장 시도 없이 거부돼도 W1의 LastSaveFailed가 남아 "저장 실패" 이유가 나올 수 있음. 시도 직전 초기화 또는 이유 enum.
8. Core/Save/SaveSerializer.cs:320-328 — 장착 탄창의 탄약 종류를 무기와 비교하지 않음(직접 수정한 세이브만). 다르면 탄창 없는 무기 + 버림 집계.
9. Core/Save/FileSaveStorage.cs:43-44, :75-81 — 비원자 대체 경로에서 Delete/Move 사이 종료 시 save.json 없음 → Missing → 새 프로필(.tmp·.bak 남음). save.json 없고 .tmp 있으면 .tmp 읽기(.bak 자동 복구는 계속 안 함).
10. UI/HideoutRoot.cs:300-313, UI/TraderViewModel.cs:77-107, Hideout/Logic/TraderModel.cs:43 — 이벤트마다 숨은 상인 탭 모델까지 다시 만들고 매도가마다 Stash.Contains O(n). 이벤트 단위라 지금은 문제 아님. 탭이 보일 때만 만들기.
11. 정리: UiText.cs:29 `MenuStartRaid`·MainMenuPresenter.cs:9-11 `MainMenuPanel.Result` 잔재, Loadout.cs:83 TODO "재장전 중 손에 든 탄창" 주석이 틀림(WeaponModel.CompleteReload는 즉시 교체), HideoutRoot.cs:73-85 QuickSlot 액션을 끄지 않음(무해).

## 확인한 것 (문제 없음)
- 의존 방향: Core 참조 없음, Hideout·Raid·AI·Audio → Core만, 기능 간 using은 EditorTools뿐. 싱글톤은 GameManager 하나.
- RULE-01: 판정 RaidResultBuilder.BuildCore 하나, 반영 ProfileService.ApplyRaidResult 하나, TakeRaidReturn 1회, 흐름 밖 레이드는 표시만, Capture는 RaidEnded 직전.
- RULE-03: 레이드 중 쓰기 없음, W4 실패 시 되돌리고 로드 안 함, W3는 !IsLeaving일 때만, MainMenu → 레이드 직행 없음, 결과 없는 귀환 = Interrupted, raidInProgress 파일의 미해석 레코드는 보관함으로 구하지 않음, .bak 자동 읽기 없음. 탈출~W1 사이 창은 Q31로 받아들인 위험.
- 복제·소실: 매수는 자리 확인 후 차감, 매도는 Remove 성공 후 가산, 키트 전부/전무, 거부 아이템 이중 기록 없음, 떠나는 중 거래·키트·저장·드래그 거부.
- 세이브 경로: GameManager.Awake는 경로만, EditMode는 메모리·임시 폴더, PlayMode는 SetUpFixture 샌드박스(같은 네임스페이스).
- 이벤트·서비스 해지 짝 맞음. JsonUtility 예외·버전 처리, tmp → Flush → File.Replace(.bak). 수치는 SO, 옛 Input 없음.
