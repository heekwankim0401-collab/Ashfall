# MVP 마무리 코드 리뷰 지적 (code-reviewer, 읽기 전용, 2026-10-24)

범위: 9cc2684..a34a1cd (F1 전신 사인, F2 보관함 업그레이드·세이브 v3, F3 내구도 표시·수리·가격, F4 방어구 플레이어·적).
[확인] = 코드로 확인, [추정] = 실행 없이 추론. 경로는 `Assets/_Project/Scripts/` 기준. **높음 0, 중간 2, 낮음 8.**

## 중간
1. **파괴 부위 분산 피해가 방어구를 건너뛴다** [확인] — `AI/Runtime/EnemyAgent.cs`(ApplyDamage), `AI/Combat/EnemyBodyModel.cs`(ApplyHit 분산), `Player/PlayerHealth.cs`(ApplyDamage), `Player/BodyHealthModel.cs`(분산).
   방어구 판정은 처음 맞은 부위로 한 번. 파괴된 다리(적)·팔다리(플레이어)를 맞히면 70%가 몸통으로 가는데 방탄복을 거치지 않는다 → 방탄복 대상은 파괴 부위를 노리는 편이 몸통 직격(9mm 30%)보다 세다.
   고칠 방법: (a) 분산 몫에 도착 부위 기준으로 `ArmorRules.Absorb` 한 번 더, 또는 (b) `TODO(GDD-?) MF-Q25`로 현재 동작 기록. 어느 쪽이든 테스트 1개.
2. **방어구 전용 난수가 기존 난수와 독립이 아니다** — `AI/Runtime/EnemyAgent.cs`, `Player/PlayerHealth.cs`.
   [확인] 적은 `seed != 0`이면 `armorRandom = new System.Random(seed)`로 `random`과 seed가 같다. [추정] seed 없음·플레이어는 둘 다 `new System.Random()`이 같은 프레임에 만들어져 같은 seed일 가능성이 크다 → 관통 굴림과 상태이상(중출혈) 굴림이 함께 움직인다.
   고칠 방법: 고정 seed는 소금(salt)을 섞은 별도 seed, seed 없음은 `Guid.NewGuid().GetHashCode()`. 기존 random에서 Next()로 뽑지 않는다(순서 밀림). 같은 seed에서 두 난수 첫 값이 다르다는 테스트.

## 낮음
3. 은신처 상인의 정확한 수리비·매도가로 내구도를 역산할 수 있다(`UI/TraderViewModel.cs`, `Hideout/Logic/PriceRules.cs`). RULE-04(탄 수) 위반은 아니고 "가격 반영" 결정의 결과 — CHANGELOG에 의도로 기록하거나 등급 구간 가격(기획 판단).
4. 레이드 인벤토리를 열어 둔 채 맞으면 방어구 등급 문구가 다음 다시 그리기 전까지 갱신되지 않는다(`ArmorRules.cs`, `InventoryScreenView.cs`) [추정]. 문서화 또는 등급 경계를 넘을 때만 알림(소유권 검토 필요).
5. 보관함 10×50은 다시 그릴 때마다 칸 500개·줄 50개를 새로 만든다(`InventoryScreenView.cs`, `HideoutRoot.cs`). 은신처 전용, 설계 §8에서 수용 — 남은 목록에.
6. §5.3 수치 4개가 `PlayerConfigSO`·`AiConfigSO`에 중복(값 같음은 테스트 고정), `ArmorRuleParams`를 Awake에서 캐시해 플레이 중 인스펙터 조정이 안 먹는다. 툴팁·주석으로 이유 명시.
7. `MvpFinishDataSetup.AssignEnemyArmor`: BOS·BGD를 새로 만들 때 M5 방어구 에셋이 아직 없으면 `helmet: {fileID: 0}`로 저장되고 `ApplyEnemyArmor`가 "사람이 조정한 값"으로 보고 건너뛴다 [추정]. 지금 커밋된 에셋은 맞다. 경로가 있는데 로드가 null이면 `LogError`.
8. (a) `UiText.StashUpgraded`가 조사 "으로" 고정(40·50은 맞음, 설정이 바뀌면 틀릴 수 있음) — 주석. (b) 설정에서 단계를 줄이면 범위 밖 아이템이 자동 배치·버려질 수 있다 — CHANGELOG에 "아이템 소실 가능"도.
9. 테스트 빈틈: (a) 1번 분산 경로, (b) `ArmorItem` 매도가·수리비(설계 예 방탄복 30/60 → 매도 500·수리 625), (c) 방탄복 레이드 → 탈출 → 저장 → 읽기 흐름(EditMode 왕복만 있음), (d) 2번 난수 독립성.
10. PlayMode `DeathCauseFlowTests`는 `PlayerHealth.config` 리플렉션 교체가 `TickSurvival`이 매 프레임 config를 읽는 데 기댄다 — 테스트 머리 주석에 명시.

## 확인한 것 (문제 없음)
- 의존 방향(기능 → Core만, `using Ashfall.<기능>`은 EditorTools뿐), 새 asmdef·싱글톤·정적 인스턴스·옛 Input 없음, 새 수치 SO·`TODO(GDD-?) MF-Qn`.
- 방어구 공식·NaN → 0, 확률 0/1·피해 ≤ 0이면 난수 미소비(0.5 ± k×0.25는 float 정확), 비관통 × 0.3·마모 원피해 × 0.2·관통 마모 0, 내구도 0 = 막지 못함, 방독면·비 `ArmorItem` 제외, 방어구 없는 적 null 안전.
- 피격 경로 할당 0(적 34명 상수 비용).
- F1 사인: 진입점별 `currentKind`, `CheckDeath`만 복사, 사망 뒤 불변, `Died` 한 번, `EndByDeath` 중복 무시.
- 세이브 v3: stashLevel·방어구 절대 내구도 쓰기, v1·v2 → 0, 음수 → 0, 최대 초과 자름, 방어구 v<3·−1·NaN → 최대, v4 지원 안 함.
- 보관함: `TryGrow` 제자리 확장으로 참조 유효, 확장 성공 뒤에만 탄피 차감, 더티 플래그로 프레임당 1회 재구성.
- 수리·매도: 공식·검사 순서 맞음, 되팔기 이득 없음(floor/ceil 경계까지 계산), `OnValidate`가 수리 배율 ≥ 매도 배율.
- RULE-04: 등급 문구에 숫자 없음(테스트), 고스트·퀵슬롯 이름만, 레이드 HUD 새 수치 없음.
- IL2CPP: 리플렉션·동적 코드 없음, `[Serializable]` 구조체·JsonUtility int 1개.
- PlayMode 결정성: 보스 사격 테스트는 확률 0, `ArmorFlowTests`는 0/1 조합만.
