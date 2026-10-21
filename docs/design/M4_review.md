# M4 코드 리뷰 지적 (code-reviewer, 읽기 전용) — 높음 없음, 중간 1, 낮음 7

## 중간
1. 종료 후 세계가 다시 돌아 결과 스냅샷 이후 인벤토리가 바뀜 — Core/Flow/GameFlow.cs:72(CompleteRaid의 SetWorldPaused(false)), Raid/Runtime/RaidController.cs:193-196, Weapons/WeaponController.cs:217-218.
   탈출 대기 중 R 재장전 → 종료 틱 스냅샷 후 같은 프레임/로드 중 프레임에 재장전 완료 → CarriedOut에서 탄창 A 이중, B 누락(RULE-01 위반). 의약품 사용 완료도 동일. 사망 경로는 OnPlayerDied CancelReload로 막혀 있음.
   고칠 방향: (a) CompleteRaid는 정지를 풀지 않고 OnSceneLoaded가 풀게(테스트 기대 수정), (b) RaidEnded에서 Inventory 잠금(IsDeadLocked 같은) + Weapons CancelReload. 둘 다.

## 낮음
2. RaidController.cs:224-234 흐름 밖(Phase None) 종료 시 정지만 풀고 RaidEnd 오버레이·조작 차단 유지. 의도면 주석, 아니면 ReturnToMainMenu.
3. RaidSession.cs:82,92-96 / PlayerInventory.cs:112-120,220-229 — Contains(장착 포함·잠김 포함)와 TryConsume(저장 공간만·잠김 제외) 범위 불일치 → EXT-03 20초 대기·Interrupt 무한 반복 가능. 대기 판정을 TryConsume과 같은 범위(CanConsume 추가 등)로.
4. RaidController.cs:154-162, RaidSession.cs:61 — 사망을 이벤트로만 반영. Start 전/구독 전 사망 시 죽은 몸이 탈출·MIA 가능(추정). Update에서 target.IsDead면 EndByDeath 안전망.
5. RaidController.cs:190-215 — OnSessionEnded 예외 시 RaidEnded·CompleteRaid가 안 와서 레이드 씬에 갇힘. try/finally로 보장.
6. GameEvents.cs:222-225 RaidStarted.SpawnId — 내 위치 노출 경로가 될 수 있음(RULE-06). 필드 제거 또는 "UI 표시 금지" 주석.
7. GameFlow.cs:79-83 ReturnToMainMenu(예비) — 호출처 없음, 레이드 중 호출 시 RULE-01 우회. 삭제하거나 Raid 단계에서 거부.
8. UI/MainMenuPresenter.cs:67-75 — StartRaid 무시/로드 실패 시 IsStarting이 true로 남아 버튼 영구 비활성, GameFlow.loadingScene도 남음. StartRaid가 bool 반환 또는 실패 처리.
