# M3 코드 리뷰 지적 (code-reviewer, 읽기 전용, 추론 기반)

## 높음
1. RDR이 Alert로 엄폐 뒤에 무기한 갇힘 — RaiderTactics.cs:65-88, EnemyBrain.cs:222-245(228-231, 242).
   Alert에서 엄폐 도착 시 Hold(+Low면 웅크림). CoverSelector가 숨는 눈이 막힌 지점만 고르므로 표적을 못 봄 → T7(보임 필요) 불가, Alert엔 Peek 없음.
   분대 ReportContact(0.5초마다)·총성·발소리가 SetLastKnown으로 LastKnownUpdatedAt 갱신 → T8(alertTimeoutSec)도 안 옴.
   고칠 방향: Alert 엄폐 도착 시 Hide/Peek 주기(Peek 때 일어서기/PeekPosition), 또는 RDR 엄폐 도착 시 Combat 전이(CHANGELOG 기록). T8 타이머는 "마지막으로 본 시각" 또는 Alert 진입 시각 기준으로.
2. High 엄폐 첫 Peek 후 Hide가 숨는 위치로 안 돌아감 — RaiderTactics.cs:139-150, 184-188, 205-206.
   TickPeek가 High면 PeekPosition으로 이동, EnterHide의 Hide 분기는 Hold+자세만 → 벽 끝 밖에 노출된 채 재장전.
   고칠 방향: Hide에서 Reached(cover.Position) 아니면 MoveTo(cover.Position, Walk). EditMode 테스트 추가.

## 중간
3. 엄폐 질의 Deferred(maxCoverQueriesPerFrame=1 초과) 때 TryAcquireCover false → fallback(ScavengerTactics) 실행, SetDestination이 repathMinIntervalSec(0.5s) 동안 고정 — RaiderTactics.cs:68-75, 99-107, 115-121, 228-238; EnemyAgent.cs:270-279.
   고칠 방향: CoverResult로 다루고 Deferred면 Hold+위협 쪽 보기, fallback은 None에서만.
4. (추정) 엄폐 무효화 직후 같은 지점 재선택 반복(무효화 판정은 실제 표적 눈, 재선택 LOS는 ThreatEye=LKP+1.6) — RaiderTactics.cs:111-122, CoverSelector.cs:42-94.
   고칠 방향: 무효화 지점 id를 몇 초간 후보 제외 또는 ThreatEye를 현재 표적 눈으로.
5. 살아 있는 적이 Destroy/disable되면 분대·우회권 정리 안 됨 — EnemyAgent.cs:150-155, Squad.cs:42-51. OnDestroy가 director.Unregister만.
   고칠 방향: OnDestroy/OnDisable에서 살아 있으면 Detach(예약·우회권 반납, squad.Leave).
6. 경로 없는 경계 분대(RDR C) 분대원이 Idle에서 대형 자리로 이동해 경계 위치 이탈 — EnemyBrain.cs:387-395.
   고칠 방향: 리더에게 경로가 없으면 Idle 분대원은 제자리 유지.
7. loadout.ammo null 검사 누락 → ProjectileSystem.Fire NRE, AiDirector.Update FrameTick 루프에 격리 없어 뒤 순번 적 전부 정지 — EnemyAgent.cs:135-140, EnemyWeapon.cs:104, ProjectileSystem.cs:75-76, AiDirector.cs:192-207.
   고칠 방향: Start 검사에 ammo != null(+ ammo.type == weapon.ammo), director 루프에 에이전트별 try/catch + LogException.

## 낮음
8. 파괴된 표적 접근 MissingReferenceException — EnemyAgent.cs:285-286, 311-313. FrameTick 시작 시 currentTarget을 director.Target으로 또는 UnityEngine.Object null 검사.
9. IProjectileService 등록 해제 후에도 Projectiles 참조 유지 — AiDirector.cs:227-233, 245-246, EnemyWeapon.cs:18, 33. TryGet 실패 시 null, EnemyWeapon은 director 통해 조회.
10. NavMesh 밖 적이 경고 없이 제자리 — EnemyAgent.cs:243-244, 436-437. 첫 FrameTick에 1회 LogWarning.
11. 아군 피격 시 아군 총구를 LKP로 삼음 — EnemyAgent.cs:374-375. TODO 주석 정도.
12. 리더가 Start 순서로 결정(비결정적), 리더 승계 시 낡은 patrolIndex — EnemyBrain.cs:65, 332-349, 397-430, EnemyAgent.cs:131-133. 승계 시 BeginPatrol 재계산, 등록 순서 결정적으로.
13. (추정) 경로 없는 RDR Idle↔Suspicious 반복(웅크리면 눈높이 1.0m로 표적 상실) — EnemyBrain.cs:311-312. 사람 확인 항목.
14. 하드코딩 상수: EnemyAgent.cs:289 0.04f(→ movingSpeedThresholdMps), EnemyBrain.cs:454·512, TacticsContext.cs:60 바라볼 거리 10f.
15. 죽은 적이 시체로 넘긴 WeaponItem·Magazine 참조 유지 — EnemyAgent.cs:399-407. Build 뒤 Weapon = null.
16. (추정) 투사체 비용: 동시 탄 200~300개면 스텝당 200~300 레이 — 4단계 측정으로 판단.
17. (추정) FootstepNoiseModel: W 연타 시 펄스 증가 — 의도일 수 있음.
