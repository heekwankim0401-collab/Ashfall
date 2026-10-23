# MVP 마무리 설계 (unity-architect) — 준비안

기준: docs/GDD.md §0, §3(RULE-04·05), §4.2·§4.3, §5.1·§5.3, §6, §7, §9, §12, §13.3, §14. CHANGELOG_GDD M3("적에게 상태이상·방어구 없음"), M5(Q1 보관함 고정·Q10 가격·Q12 매도), M7(Q12~Q19, Q33, 7A 세이브 v2).
`M7_design.md` §1.6·§5·§10(목록 1·3·6·14)와 현재 코드(9cc2684)를 전제로 한다.
**사용자 결정(2026-10-24, 확정)** 4가지를 구현한다: ① §5.3 방어구 관통·둔탁 피해 ② 무기 내구도 대략 표시 + 상인 수리 + 가격 반영(M7 Q15·Q16·Q17 뒤집기) ③ 전신 사인 문구 ④ 보관함 업그레이드.
**상태: 준비안.** §5 질문은 ★추천안을 그대로 채택한다는 전제로 썼다. 답이 다르면 ⟨Fn-Qn⟩ 표시된 곳만 바꾼다. 여기 없는 것은 만들지 않는다. 모호하면 `// TODO(GDD-?) MF-Qn` + 가장 단순한 구현.

현재 코드에서 이 설계가 기대는 사실(확인함):
- **방어구 데이터:** `ArmorDefinitionSO { armorClass, maxDurability, covers, kind, blocksStormRadiation }`(Core). 에셋 값이 이미 있다 — ARM-HLM-01 등급 3·내구도 40·covers [Head], ARM-BDY-01 등급 3·60·[Thorax, Stomach], GEAR-GSM-01 등급 0·0·[](방독면).
  **에셋 변경 불필요.** 런타임 방어구는 상태 없는 `ItemInstance`(`ItemFactory.Create` default 분기) — 내구도를 담을 곳이 없다. `armorClass`는 어디서도 쓰지 않는다.
- **탄 관통력:** AMMO-9MM 1, AMMO-12GA 1(펠릿마다 `DamageInfo` 1개), AMMO-556 3, AMMO-762 4. `DamageInfo.Penetration`은 `ProjectileSystem`이 탄약 값으로 채운다. 변이체 근접은 관통 0 + 부위 힌트.
  → 등급 3 방어구에 9mm·12ga 관통 확률 = clamp01(0.5 − 0.5) = **0**, 5.56 = 0.5, 7.62 = 0.75(§3 밸런스 위험).
- **플레이어 피격:** `PlayerHealth.ApplyDamage`(Player) → 부위 판정(또는 힌트) → `BodyHealthModel.ApplyHit(part, amount)`. 헬멧 슬롯은 이미 매 프레임 `IInventoryService.GetEquipped(Helmet)`으로 조회한다(방독면).
- **적 피격:** `EnemyBodyModel`(AI, 3부위 `EnemyHitRegion`), 방어구 없음(M3 결정).
- **사인:** `BodyHealthModel.DeathCause`(BodyPart?)는 `CheckDeath`에서만 정해진다. 경로는 `ApplyHit`·`ApplyDamage`(피격), `Tick`(출혈 — 파괴 부위면 70% 흉부 분산), `ApplySystemicDamage`(탈수, 머리 제외 → 흉부 160초).
  `Died(BodyPart)` → `PlayerHealth.OnDied` → `PlayerDied(BodyPart)` → `RaidController` → `RaidSession.EndByDeath(BodyPart)` → `RaidResultBuilder` → `RaidResult(hasDeathCause, deathCause)` → `RaidResultViewModel.TitleFor` "사망 — 흉부".
  **방사능은 사인이 될 수 없다:** `MaxHpMultiplier`(0.8)는 현재 HP를 새 최대로 자를 뿐 0으로 만들지 않고, 방사능 피해 경로가 없다.
- **세이브:** `SaveSerializer.CurrentVersion = 2`, `ItemRecord.durability`(float, 기본 −1 = 해당 없음)는 `WeaponItem`만 쓴다. 음수·NaN → 100으로 읽는다. `ReadDurability(recorded, version)`.
  `SaveData`에 보관함 크기·단계 필드 없음 — 보관함 크기는 매 로드 `ProfileDefaults.StashSize`(= `HideoutConfigSO.stashSize` 10×30). `PlayerProfile.Stash`는 `{ get; }`, `ItemGrid.Size`도 `{ get; }`.
- **보관함 UI:** 은신처 인벤토리는 `LootContainerModel.ForExistingGrid(profile.Stash)`로 같은 그리드를 감싸 열고(`Size => Grid.Size`), `HideoutChanged` → `InventoryChanged`로 다시 그린다. 보관함은 `HideoutUI.uxml`의 세로 ScrollView 안.
- **상인:** `PriceRules`(Hideout) 매수 = 올림(base × 1.0), 매도 = 내림(base × 0.4) + 무기 장착 탄창. 매수는 정의(새 물건)만 판다. 매도는 보관함만. 읽기 전용 프로필에서 거래는 되고 제작만 `Unavailable`.
- **UI 이름:** `UiText.ItemLabel`(이름만)이 인벤토리 칸·장비 슬롯·드래그 고스트·상인 줄에 쓰인다. 툴팁·아이템 상세 화면은 없다. `HudConfigSO`는 `GameUIRoot`·`HideoutRoot` 둘 다 가진다.
- **IHideoutService 구현체:** `HideoutService`, 테스트 `FakeHideoutService`(EditMode `HideoutUiModelTests.cs`). 멤버를 늘리면 둘 다 고친다.
- 현재 원본 EditMode 1796 · PlayMode 181.

---

## 0. 범위

**만든다**

| 단계 | 내용 |
|---|---|
| **F1 사인** | 탈수·출혈로 죽으면 결과 화면 제목이 "사망 — 탈수"/"사망 — 출혈" ⟨Q1~Q4⟩. 피격 사망은 그대로 "사망 — 흉부/머리". |
| **F2 보관함 업그레이드** | 은신처 허브에서 탄피로 보관함 10×30 → 10×40 → 10×50 ⟨Q5~Q9⟩. 세이브 version 3 `stashLevel`(v1·v2 = 단계 0). `IHideoutService` API. |
| **F3 내구도 표시·수리·가격** | 무기(·방어구 ⟨Q15⟩) 이름 옆 대략 등급 "양호/낡음/위험"(숫자 없음, RULE-04 유지) ⟨Q10·Q11⟩. 상인 [수리](보관함 아이템, 탄피, 100%로) ⟨Q13·Q14⟩. 매도가 × 내구도 비율 ⟨Q12⟩. |
| **F4 방어구** | §5.3: 관통 확률 `clamp01(0.5 + (관통력 − 등급) × 0.25)`, 비관통 = 피해 30% 둔탁 + 방어구 내구도 감소, `covers` 부위만 ⟨Q16~Q24⟩. 순수 판정 Core `ArmorRules`(난수 주입). 런타임 `ArmorItem`(내구도), 세이브는 `ItemRecord.durability` 재사용. |

**만들지 않는다**
- 적 방어구(★Q16 A — 공식만 Core에 공유), 방어구 내구도 비율에 따른 등급 감소(타르코프식), 관통 시 방어구 마모, 방어구 피격 소리·HUD 표시.
- 수리 키트 아이템·제작 수리·레이드 중 수리, 은신처 장비(보관함 밖) 수리, 수리 시 최대 내구도 감소.
- 보관함 너비 확장·다른 은신처 업그레이드(제작대 단계 등), 업그레이드 확인 대화상자.
- 방사능 사인(경로 없음 ⟨Q3⟩), 사망 통계·기록 저장.
- 새 Input 액션(전부 기존 UI 버튼·클릭), 새 asmdef, 새 싱글톤·정적 인스턴스, 씬 오브젝트 추가.

---

## 1. 아키텍처

### 1.1 경계 요약

새 asmdef 없음, 기능 간 참조 추가 없음, 새 이벤트 없음(모두 기존 이벤트·서비스로 충분 — 쓰는 곳 없는 이벤트는 만들지 않는다, M6 Q27 원칙).

| 필요한 것 | 상대 | 방법 | 단계 |
|---|---|---|---|
| 사인 종류 | Player → Raid → UI | ★Core enum `DeathCauseKind` + `PlayerDied.Kind` + `RaidResult.DeathKind`(선택 인자) | F1 |
| 보관함 단계·크기 | Hideout ↔ Core 세이브 | `PlayerProfile.StashLevel`·`TryUpgradeStash`, `ProfileDefaults` 단계별 크기, `ItemGrid.TryGrow`(제자리 확장) | F2 |
| 보관함 확장 명령 | UI → Hideout | `IHideoutService.NextStashUpgrade`·`TryUpgradeStash()` + 기존 `HideoutChanged` | F2 |
| 내구도 공통 접근 | UI·Hideout·Player | ★Core `IDurableItem { Durability; MaxDurability }` + `DurabilityRules.Ratio(item)` — `WeaponItem`(F3)·`ArmorItem`(F4) 구현 | F3·F4 |
| 수리 | UI → Hideout | `IHideoutService.GetRepairCost(item)`·`TryRepair(item)` + `HideoutChanged` | F3 |
| 대략 등급 | UI 내부 | UI `ItemConditionRules.TierFor(ratio, HudConfigSO)` → `UiText.ConditionWord` | F3 |
| 방어구 판정 | Player(지금), AI(나중) | ★Core 순수 `ArmorRules`(난수 `System.Random` 주입), 수치는 호출자 SO → `ArmorRuleParams` | F4 |
| 착용 방어구 | Inventory → Player | 기존 `IInventoryService.GetEquipped(Helmet/BodyArmor)` → `ArmorItem` 내구도를 직접 깎는다(`WeaponController`가 `WeaponItem.Durability`를 깎는 것과 같은 방식) | F4 |

### 1.2 F1 사인

```
BodyHealthModel: 진입점마다 현재 피해 종류 기록 → CheckDeath가 DeathKind로 복사
  ApplyHit/ApplyDamage = Wound · Tick(출혈·중출혈) = Bleeding · ApplySystemicDamage(탈수) = Dehydration
PlayerHealth.OnDied(part) → PlayerDied(part, Body.DeathKind)
RaidController.OnPlayerDied → RaidSession.EndByDeath(evt.Cause, evt.Kind)
RaidResultBuilder → new RaidResult(..., deathKind: session.DeathKind)
RaidResultViewModel.TitleFor: Wound → "사망 — " + 부위 / Bleeding → "사망 — 출혈" / Dehydration → "사망 — 탈수"
```
- `Died` C# 이벤트 서명(`Action<BodyPart>`)·`DeathCause`(BodyPart?)는 그대로 — 기존 EditMode 단언 유지. 출혈로 파괴 부위 → 흉부 분산 사망도 Bleeding.
- 기존 호출 호환: `PlayerDied(BodyPart)` 생성자 = Wound, `EndByDeath(BodyPart, DeathCauseKind kind = Wound)`, `RaidResult(..., Loadout carriedOutLoadout = null, DeathCauseKind deathKind = Wound)`.
- 사망 안전망 `EndByDeath()`(원인 모름)는 그대로 "사망".

### 1.3 F2 보관함 업그레이드 ⟨Q5~Q9⟩

```
HideoutConfigSO.stashSize(10×30) = 단계 0, stashUpgrades[k] = { rows, costCasings } = 단계 k+1 (너비 고정)
  ToProfileDefaults() → ProfileDefaults(stashSizesByLevel = [10×30, 10×40, 10×50], startingCasings)
SaveSerializer.ToProfile: level = version ≥ 3 ? clamp(data.stashLevel, 0, defaults.MaxStashLevel) : 0
  → new PlayerProfile(defaults.StashSizeFor(level), casings, level) → 기록된 자리에 배치(범위 밖은 기존 규칙: 자동 배치 → 버림)
HideoutService.TryUpgradeStash → StashUpgradeModel(순수): 떠나는 중·프로필 없음 = Unavailable, 최대 = MaxLevel, 탄피 부족 = NotEnoughCasings
  → Casings −= cost, profile.TryUpgradeStash(새 크기) (ItemGrid.TryGrow 제자리 확장, 아이템 자리 그대로) → HideoutChanged
  → HideoutInventoryBehaviour가 InventoryChanged → 인벤토리 화면이 새 Size로 다시 그림 (LootContainerModel은 같은 그리드를 감싼다)
저장 = 상인 거래와 같이 메모리에서만 바뀌고 W2/W3/W4에서 저장
```
- 그리드를 새로 만들지 않고 제자리에서 키운다 — `IHideoutService.Stash`·`LootContainerModel`·인벤토리가 들고 있는 참조가 그대로 유효하다.
- `TryGrow(newSize)`: 두 축 모두 현재 이상일 때만 true(줄이기 없음). 아이템 배치는 바뀌지 않는다.

### 1.4 F3 내구도 표시·수리·가격

```
Core IDurableItem { float Durability { get; set; } float MaxDurability { get; } }
  WeaponItem: 명시적 구현 float IDurableItem.MaxDurability => MaxDurability(const 100) — 기존 const 이름과 충돌하지 않게
  DurabilityRules.Ratio(ItemInstance) = IDurableItem이고 Max > 0 → clamp01(D / Max), 아니면 1 (내구도 없음)
  DurabilityRules.HasDurability(ItemInstance) = IDurableItem이고 Max > 0
UI: ItemConditionRules.TierFor(ratio, worn, critical) → Good/Worn/Critical → UiText.ConditionWord "양호"/"낡음"/"위험"
  UiText.ItemLabelWithCondition(item, thresholds) = HasDurability면 "이름 (낡음)", 아니면 ItemLabel 그대로
  쓰는 곳: 인벤토리 칸·장비 슬롯(BuildItem, 레이드·은신처 공통), 상인 매도 줄 이름. 드래그 고스트·퀵슬롯·결과 목록은 그대로 ItemLabel
Hideout PriceRules:
  SellPrice(item) = 내림(base × 매도배율 × Ratio(item)) + 무기 장착 탄창 매도가(그대로)
  RepairCost(item) = HasDurability && Ratio < 1 → max(1, 올림(base × repairCostRatio × (1 − Ratio))), 아니면 −1
TraderModel.TryRepair(item): 보관함 밖 NotInStash → 수리 불필요 NotNeeded → 탄피 부족 NotEnoughCasings → Casings −= cost, Durability = Max → Ok
```
- 매수가는 바뀌지 않는다: 상인은 정의(새 물건, 내구도 100%)만 팔아 비율 = 1이다(사용자 결정 "구매 가격에 반영" = 공식은 같고 결과가 같음, CHANGELOG에 명시).
- 경제 확인: 수리로 오른 매도가 = base × 0.4 × Δ < 수리비 base × 0.5 × Δ → 수리 후 되팔기로 탄피가 늘지 않는다(`repairCostRatio ≥ sellPriceMultiplier`를 테스트로 고정).
- 고장 상태(`IsJammed`)는 이미 은신처 귀환 때 풀리므로 수리와 무관.

### 1.5 F4 방어구 ⟨Q16~Q24⟩

```
PlayerHealth.ApplyDamage(info)
  part = 힌트 또는 지점 판정 (기존)
  armor = ArmorRules.FindCovering(GetEquipped(Helmet), GetEquipped(BodyArmor), part)   // ArmorItem && 등급 ≥ 1 && covers ∋ part && 내구도 > 0
  if armor != null:
     r = ArmorRules.Resolve(params, info.Penetration, armor.Definition.armorClass, info.Amount, armorRandom)
     amount = r.BodyDamage                // 관통 = 그대로, 비관통 = × 0.3
     armor.Durability −= r.DurabilityLoss // 비관통만: 원피해 × durabilityLossPerDamage
  Body.ApplyHit(part, amount)             // 상태이상 굴림·복부 배율 기존 규칙 그대로 (감소된 피해로) ⟨Q19⟩
```
- **판정 순수 로직 Core `ArmorRules`** — 적이 나중에 방어구를 갖게 되면 AI가 같은 함수를 부른다(§7 "동일한 피해 공식"). 난수는 `System.Random` 인자. **확률이 0 또는 1이면 난수를 뽑지 않는다**(결정적 경우 난수 순서 불변 — PlayMode 테스트가 결정적 조합만 쓰게).
- `PlayerHealth`는 방어구 전용 `System.Random`을 따로 가진다(BodyHealthModel 상태이상 난수 순서를 밀지 않게).
- 내구도 0 = 막지 못함(`FindCovering`이 고르지 않음 → 전부 관통). 등급은 내구도와 무관(타르코프식 없음) ⟨Q18⟩.
- 방독면(등급 0·covers 빈)·`new ItemInstance(armorDef)`(구 테스트 경로, `ArmorItem` 아님)는 판정에서 빠진다 → 기존 M7 방독면 테스트 무영향.
- `ArmorItem`: `ItemFactory.Create(ArmorDefinitionSO)`가 내구도 = `maxDurability`로 만든다. 상인·상자·시체·키트 모두 이 경로. 무게 불변.

### 1.6 세이브 v3 (F2에서 한 번만 올림)

- **F2:** `CurrentVersion = 3`. `SaveData.stashLevel`(int). v1·v2 → 단계 0(필드 기본값이 아니라 버전으로 판정, M7 §1.6 원칙). 단계 > 설정 최대 → 최대로 자름(TODO). v4 이상 = 지원 안 함(격리, 기존 규칙).
- **F4:** 버전을 올리지 않는다. `ItemRecord.durability`를 `ArmorItem`에도 쓴다(절대값 0~maxDurability, `maxDurability ≤ 0`이면 −1). 읽기 `ReadArmorDurability(recorded, max, version)`: version < 3·음수·NaN → max, max 초과 → max.
  v3 파일(F2 빌드가 쓴 것)의 방어구는 −1 → max로 읽혀 F4 전후가 호환된다. 그래서 v3 = "stashLevel + 방어구 내구도 기록"이고 올림은 F2 한 번.
- 다운그레이드(v3 파일을 M7 빌드로 열기) = 지원하지 않는 버전 → 격리 + 새 프로필(문서화만, M7과 같음).

### 1.7 씬·에셋 적용

- **씬 변경 없음.** 새 SO 필드는 C# 초기값이 곧 기본값이라 기존 에셋에 필드가 없어도 동작한다. 에셋 파일에 명시 기록만 EditorTools로:
  새 `EditorTools/MvpFinishDataSetup.cs` — 메뉴 `Ashfall/Setup/Apply MVP Finish Data`(배치 `Ashfall.EditorTools.MvpFinishDataSetup.ApplyAll`), **파일에 없는 필드만** 쓴다(M7 "명시 기록" 방식, 재실행 바이트 동일).
  `HideoutConfig.asset`(F2 stashUpgrades), `TraderConfig.asset`(F3 repairCostRatio), `HudConfig.asset`(F3 경계), `PlayerConfig.asset`(F4 방어구 4개). 각 단계는 자기 함수만 더한다.
  `M7SceneSetup.ApplyAll` 끝에서 한 줄 호출 — M4·M5 씬 메뉴·`Apply M7 Content`를 다시 돌려도 빠지지 않는다.
- 방어구 `.asset` 3개는 고치지 않는다(값 이미 있음). `M5DataSetup`의 "업그레이드 없음 Q1" 주석·툴팁만 갱신.
- UXML: `Art/UI/HideoutUI.uxml`에 보관함 확장 버튼·상태 라벨(F2), 상인 매도 줄 수리 버튼은 C#이 만든다(줄은 코드 생성, F3). USS는 기존 `ash-button` 재사용(새 색 없음).

---

## 2. 수치 (SO 기본값 — GDD에 없는 것은 `TODO(GDD-?) MF-Qn`)

| SO (에셋) | 필드 | 값 | 출처 |
|---|---|---|---|
| `PlayerConfigSO` | `armorBasePenetrationChance` | 0.5 | §5.3 |
| | `armorPenetrationPerClassStep` | 0.25 | §5.3 |
| | `armorBluntDamageRatio` | 0.3 | §5.3 |
| | `armorDurabilityLossPerDamage` | 0.2 (비관통 원피해당) | TODO Q17 |
| `HudConfigSO` | `conditionWornBelow` | 0.5 (비율 < 0.5 = 낡음, §5.1 고장 시작과 같은 경계) | TODO Q10 |
| | `conditionCriticalBelow` | 0.25 (비율 < 0.25 = 위험) | TODO Q10 |
| `TraderConfigSO` | `repairCostRatio` | 0.5 (basePrice × 0.5 × 잃은 비율) | TODO Q13 |
| `HideoutConfigSO` | `stashUpgrades` | [{rows 40, cost 5000}, {rows 50, cost 12000}] | TODO Q5 |

결과 예(등급 3): 9mm·12ga 관통 0% → 흉부 30 → 9, 펠릿 18 → 5.4. 5.56 관통 50%(비관통 45 → 13.5, 방탄복 −9). 7.62 관통 75%(비관통 110 → 33, −22).
ARM-BDY-01(60)은 9mm 비관통 10발(−6/발), ARM-HLM-01(40)은 7.62 비관통 2발에 0. 수리비 예: 방탄복(2500) 내구도 30/60 → 올림(2500 × 0.5 × 0.5) = 625, 매도가 1000 → 500.

---

## 3. 파일·클래스·공개 API

### 3.1 Core (`Ashfall.Core`)
| 파일 | 변경 | 단계 |
|---|---|---|
| `Combat/BodyEnums.cs` | `enum DeathCauseKind { Wound, Bleeding, Dehydration }` | F1 |
| `Events/GameEvents.cs` | `PlayerDied { BodyPart Cause; DeathCauseKind Kind; }` + 생성자 `(BodyPart, DeathCauseKind)`, 기존 `(BodyPart)` = Wound | F1 |
| `Raid/RaidResult.cs` | 생성자 끝 선택 인자 `DeathCauseKind deathKind = Wound`, 속성 `DeathKind` | F1 |
| `Items/ItemGrid.cs` | `Size { get; private set; }`, `bool TryGrow(Vector2Int newSize)` | F2 |
| `Save/PlayerProfile.cs` | `ProfileDefaults(IReadOnlyList<Vector2Int> stashSizesByLevel, int startingCasings)` 오버로드(기존 생성자 = 단계 1개), `StashSizeFor(int level)`, `MaxStashLevel`; `PlayerProfile(size, casings, stashLevel = 0)`, `StashLevel { get; }`, `bool TryUpgradeStash(Vector2Int newSize)` | F2 |
| `Save/SaveData.cs` | `int stashLevel` (v3) | F2 |
| `Save/SaveSerializer.cs` | `CurrentVersion = 3`, stashLevel 쓰기·읽기(§1.6) | F2 |
| `Save/SaveSerializer.cs` | `ArmorItem` 쓰기·읽기, `ReadArmorDurability(float recorded, float max, int version)` | F4 |
| `Services/IHideoutService.cs` | `readonly struct StashUpgradeOffer { bool HasNext; Vector2Int NewSize; int Cost; }`, `enum StashUpgradeResult { Ok, NotEnoughCasings, MaxLevel, Unavailable }`, 멤버 `int StashLevel`, `StashUpgradeOffer NextStashUpgrade`, `StashUpgradeResult TryUpgradeStash()` | F2 |
| 〃 | `enum RepairResult { Ok, NotEnoughCasings, NotNeeded, NotInStash, Unavailable }`, 멤버 `int GetRepairCost(ItemInstance item)`(−1 = 수리 불가·불필요·보관함 밖), `RepairResult TryRepair(ItemInstance item)` | F3 |
| `Items/IDurableItem.cs`(새) | `interface IDurableItem`, `static class DurabilityRules { Ratio, HasDurability, RestoreFull }` | F3 |
| `Items/WeaponItem.cs` | `: IDurableItem`(명시적 MaxDurability) | F3 |
| `Items/ArmorItem.cs`(새) | `sealed class ArmorItem : ItemInstance, IDurableItem` — `new ArmorDefinitionSO Definition`, `Durability`(0~max, NaN → max), `MaxDurability => Definition.maxDurability` | F4 |
| `Items/ItemInstance.cs` | `ItemFactory.Create`: `ArmorDefinitionSO` → `new ArmorItem(def)` | F4 |
| `Combat/ArmorRules.cs`(새) | `readonly struct ArmorRuleParams`(4값), `readonly struct ArmorHitResult { bool Penetrated; float BodyDamage; float DurabilityLoss; }`, `static class ArmorRules { float PenetrationChance(in p, int penetration, int armorClass); bool Protects(ArmorItem, BodyPart); ArmorItem FindCovering(ItemInstance helmet, ItemInstance body, BodyPart part); ArmorHitResult Resolve(in p, int penetration, int armorClass, float damage, System.Random random); }` | F4 |

### 3.2 Player (`Ashfall.Player`)
- `BodyHealthModel`: `DeathCauseKind DeathKind { get; }`, 내부 `currentKind`(진입점에서 설정, `CheckDeath`가 복사). Q33 TODO 주석 해소. (F1)
- `PlayerHealth`: `OnDied` → `PlayerDied(cause, Body.DeathKind)`(F1). `ApplyDamage`에 방어구 단계, `armorRandom`, `ArmorRuleParams`를 config에서 만든다(F4). 디버그 로그(`ASHFALL_DEBUG_DAMAGE`)에 관통 여부.
- `PlayerConfigSO`: §2 방어구 4필드 `[Header("§5.3 방어구")]`, `[Range(0,1)]`/`[Min(0)]` (F4).

### 3.3 Raid (`Ashfall.Raid`) — F1
- `RaidSession`: `DeathCauseKind DeathKind { get; }`, `EndByDeath(BodyPart cause, DeathCauseKind kind = Wound)`. `RaidResultBuilder`가 넘긴다. `RaidController.OnPlayerDied` → `EndByDeath(evt.Cause, evt.Kind)`.

### 3.4 Hideout (`Ashfall.Hideout`)
- `HideoutConfigSO`: `[Serializable] struct StashUpgradeStep { int rows; int costCasings; }`, `StashUpgradeStep[] stashUpgrades`, `ToProfileDefaults()`가 단계별 크기를 넘김, `OnValidate`(rows 단조 증가·cost ≥ 0). (F2)
- `Logic/StashUpgradeModel.cs`(새, 순수): `StashUpgradeModel(PlayerProfile, HideoutConfigSO)`, `StashUpgradeOffer Next`, `StashUpgradeResult TryUpgrade()`. (F2)
- `HideoutService`: F2 멤버(떠나는 중·프로필 없음 = Unavailable, 읽기 전용은 상인처럼 허용 ⟨Q8⟩), F3 멤버(`TraderModel` 위임). 성공 시 `HideoutChanged`.
- `PriceRules`: `SellPrice(ItemInstance)` × `DurabilityRules.Ratio`, `RepairCost(ItemInstance, TraderConfigSO)`. `TraderModel`: `GetRepairCost`, `TryRepair`. `TraderConfigSO.repairCostRatio`. (F3)

### 3.5 UI (`Ashfall.UI`)
- F1: `RaidResultViewModel.TitleFor` 분기, `UiText.DeathBleeding = "출혈"`·`DeathDehydration = "탈수"`.
- F2: `HideoutHubView` — `StashUpgradeButtonName = "hideout-stash-upgrade-button"`, `StashStatusName = "hideout-stash-status"`, `HideoutPresenter`가 `NextStashUpgrade`로 문구·활성(탄피 ≥ 비용 && !IsLeaving)을 정하고 클릭 → `TryUpgradeStash` → 결과 문구(허브 메시지 줄). `UiText.StashStatus(size)` "보관함 10×30", `StashUpgradeButton(size, cost)` "보관함 확장 10×40 (5000 탄피)", `StashMaxed` "보관함 최대", 결과 문구. `HideoutUI.uxml` 키트 줄 옆에 요소 2개.
- F3: `ItemConditionTier { Good, Worn, Critical }`·`ItemConditionRules`(순수), `HudConfigSO` 경계 2개, `UiText.ConditionWord`·`ItemLabelWithCondition`, `InventoryScreenView`(경계를 생성자 선택 인자/속성으로 받음 — 기존 생성 호출 유지) `BuildItem`, `TraderSellRow`에 `RepairText`·`CanRepair`·`NeedsRepair`, `TraderView` 줄에 [수리] 버튼(`RowRepairClass`, 수리 불필요면 숨김), `HideoutPresenter` 연결, 수리 결과 문구.
- F4: UI 코드 변경 없음(방어구가 `IDurableItem`이 되면 F3 표시·수리·가격이 그대로 적용).

### 3.6 EditorTools
- `MvpFinishDataSetup.cs`(새): `ApplyAll()` = `ApplyHideoutConfig`(F2) → `ApplyTraderAndHud`(F3) → `ApplyPlayerArmor`(F4). 메뉴 1개. `M7SceneSetup.ApplyAll` 끝에서 호출(F2가 추가).
- `M5DataSetup`: stashSize 주석만.

---

## 4. (없음 — 자리표시 에셋 없음. 새 소리·모델·머티리얼 없음)

---

## 5. 질문 (오케스트레이터가 ★추천안을 그대로 채택)

| # | 질문 | 선택지 | ★추천 |
|---|---|---|---|
| **F1 사인** | | | |
| Q1 | 사인 표현 | A Core `DeathCauseKind` 추가, 기존 `BodyPart` 사인 유지(선택 인자로 전달) / B BodyPart 대신 문자열 사인 | **A** — 기존 단언·호출 그대로 |
| Q2 | 출혈 사망을 따로 보일지 | A "사망 — 출혈"(출혈·중출혈 틱이 낸 사망, 파괴 부위 분산 포함) / B 흉부 그대로 | **A** — 경로가 `Tick` 하나라 비용이 거의 없고 원인을 알려 준다 |
| Q3 | 방사능 사인 | A 없음 — 피폭은 최대 HP × 0.8로 자를 뿐 HP를 0으로 만들지 않아 사인이 될 수 없다 / B 피폭 중 사망이면 "방사능 피폭" | **A** — B는 실제 원인(피격)을 가린다 |
| Q4 | 문구 | A 탈수 "사망 — 탈수", 출혈 "사망 — 출혈"(부위 없음), 피격 기존 "사망 — 흉부/머리" / B "사망 — 출혈 (흉부)" | **A** |
| **F2 보관함** | | | |
| Q5 | 단계·비용 | A 너비 10 고정, 30 → 40(5000) → 50(12000)줄, `HideoutConfigSO.stashUpgrades` / B 너비도 확장 | **A** — UI 가로 크기 불변 |
| Q6 | 저장 | A `stashLevel` 정수(v3), v1·v2 = 0, 설정보다 크면 최대로 자름 / B 크기 자체 저장 | **A** — 사용자 결정 "단계 저장" |
| Q7 | UI 위치 | A 허브 공통 줄(부랑자 키트 옆): 상태 "보관함 10×30" + 버튼 "보관함 확장 10×40 (5000 탄피)", 최대면 비활성 "보관함 최대" / B [장비] 탭 안 | **A** — 탭과 무관하게 보임, 키트와 같은 방식 |
| Q8 | 읽기 전용 프로필 | A 상인 거래처럼 허용(저장은 안 됨) / B 제작처럼 Unavailable | **A** — 아이템을 소비하지 않는다(M7 7C 제작 예외 사유에 해당 없음) |
| Q9 | 확인 대화상자 | A 없음(즉시) / B 확인 | **A** — 상인 구매와 같음 |
| **F3 내구도 표시·수리·가격** | | | |
| Q10 | 등급 경계 | A 비율 ≥ 0.5 양호, 0.25~0.5 낡음, < 0.25 위험(`HudConfigSO`) / B 4단계 | **A** — 0.5 = §5.1 고장 시작 |
| Q11 | 표시 위치·형식 | A 인벤토리 칸·장비 슬롯(레이드·은신처)·상인 매도 줄 이름 뒤 "(양호)" — 내구도 있는 아이템만, 세 등급 모두 / B 새 툴팁 / C 양호는 생략 | **A** — 툴팁 체계가 없다. 숫자 없음(RULE-04) |
| Q12 | 가격 | A 매도 = 내림(base × 매도배율 × 비율) + 장착 탄창(그대로), 매수는 새 물건이라 변화 없음 / B 하한 비율 | **A** — 0%면 0 탄피(가격 0 처분 판매 M5 Q12) |
| Q13 | 수리 비용·결과 | A max(1, 올림(base × 0.5 × (1 − 비율))), 100%로, 최대 내구도 감소 없음, 100%면 수리 불가(NotNeeded) / B 고정 비용 | **A** — 수리 후 되팔기 이득 없음(0.5 > 0.4) |
| Q14 | 수리 대상 | A 보관함 아이템만(매도와 같음), [상인] 탭 매도 줄의 [수리] / B 은신처 장비도 | **A** — M5 Q12와 같은 범위 |
| Q15 | 방어구에도 같은 표시·수리·가격 | A 예(`IDurableItem` 공통, F4가 방어구를 넣으면 자동) / B 무기만 | **A** — 단순·일관 |
| **F4 방어구** | | | |
| Q16 | 적 방어구 | **A 없음(MVP) — `ArmorRules`는 Core에 두어 공식만 공유** / B `EnemyArchetypeSO.helmet·bodyArmor`(ArmorDefinitionSO, null = 없음), 머리 → Head·몸통 → Thorax로 `covers` 판정, 다리 없음, 적마다 내구도, 시체 루트 미포함 / C B + 시체 드롭 | **A** — M3 결정(적 방어구 없음) 유지, 적 사망 테스트·밸런스 무영향 |
| Q17 | 내구도 감소량 | A 비관통 때만 원피해 × 0.2 / B 비관통 1회당 고정 1 / C 관통도 감소 | **A** — 펠릿 8개가 고정값 8번이 되지 않음, §5.3 "비관통 시" 문구 |
| Q18 | 내구도 0 | A 막지 못함(전부 관통), 등급은 내구도와 무관 / B 비율로 등급 감소 | **A** — 사용자 지시 |
| Q19 | 둔탁 피해 처리 | A 감소된 피해로 기존 `ApplyHit`(복부 파괴 배율·상태이상 굴림 그대로) / B 둔탁은 상태이상 굴림 없음 | **A** — 경로 하나(9mm 둔탁 9 < 출혈 최소 피해라 실제로 굴림은 5.56·7.62만) |
| Q20 | 근접(관통 0) | A 같은 공식(등급 ≥ 2면 항상 막힘 → 30%, 방탄복 마모) / B 근접은 방어구 무시 | **A** — "동일한 피해 공식" |
| Q21 | 세이브 | A `ItemRecord.durability` 재사용(절대값), 버전은 F2의 v3에 포함, 없음·음수·NaN·v < 3 → 최대 / B 새 필드 | **A** |
| Q22 | 수치 위치 | A `PlayerConfigSO`(지금 쓰는 곳 = 플레이어뿐) → `ArmorRuleParams`로 Core에 전달 / B Core 새 SO + 씬 연결 | **A** — 씬 변경 없음. 적 방어구가 생기면 그때 AiConfigSO가 같은 구조체를 만든다 |
| Q23 | 판정 제외 | A 등급 < 1·covers 빈(방독면)·`ArmorItem`이 아닌 인스턴스·내구도 0 / B 방독면도 판정 | **A** |
| Q24 | 피드백 | A 소리·이벤트·HUD 없음(인벤토리 등급 문구로만 확인) / B 방어구 피격음 이벤트 | **A** — 구독자 없는 이벤트를 만들지 않는다, 자리표시 소리 없음 |

---

## 6. 작업 단계·파일 소유

규칙(M7과 같음): 원본에서 배치모드 Unity는 한 번에 하나. 각 단계 = 구현 + EditMode 테스트 → 원본 EditMode·PlayMode(2회 연속) 통과 → `docs/CHANGELOG_GDD.md` 절 추가 → 한국어 커밋. 단계 끝마다 이 문서에 "Fn 구현 메모"를 덧붙인다.
새 `.meta`는 만들지 않는다. 테스트는 `MemorySaveStorage`·`SaveSandbox`만. **기본은 순차 F1 → F2 → F3 → F4**(각 1~3시간이라 사본 병렬의 이득보다 관리 비용이 크다).

| 관계 | 판단 |
|---|---|
| F1 ↔ 나머지 | 거의 독립. 겹침 = `UiText.cs`(문구 추가만), `PlayerHealth.cs`(F4). 원하면 F1 ∥ F2를 사본에서 병렬로 해도 되지만 권장하지 않음. |
| F2 → F3 | `IHideoutService.cs`·`HideoutService.cs`·`FakeHideoutService`·`HideoutPresenter.cs`·`UiText.cs`·`MvpFinishDataSetup.cs`를 둘 다 고친다 → 순차. |
| F3 → F4 | F4의 `ArmorItem`이 F3 `IDurableItem`을 구현해 표시·수리·가격을 공짜로 얻는다. `SaveSerializer.cs`는 F2·F4 → 순차. |
| 세이브 버전 | F2에서 한 번 v3로 올린다. F4는 올리지 않는다(§1.6). F2 없이 F4를 먼저 하면 안 된다. |

| 단계 | 담당 | 소유 파일 (이 단계만 고친다) | 예상 |
|---|---|---|---|
| **F1 사인** | gameplay-programmer | Core `BodyEnums.cs`·`GameEvents.cs`(PlayerDied)·`RaidResult.cs`, Player `BodyHealthModel.cs`·`PlayerHealth.cs`(OnDied), Raid `RaidSession.cs`·`RaidResultBuilder.cs`·`RaidController.cs`, UI `RaidResultViewModel.cs`·`UiText.cs`, 테스트 `DeathCauseTests.cs`(새)·기존 BodyHealth/RaidSession/RaidResult 테스트에 케이스 추가 | 1~1.5h |
| **F2 보관함** | gameplay-programmer(Core·Hideout) → ui-programmer(허브 UI) 순 | Core `ItemGrid.cs`·`PlayerProfile.cs`·`SaveData.cs`·`SaveSerializer.cs`·`IHideoutService.cs`, Hideout `HideoutConfigSO.cs`·`StashUpgradeModel.cs`(새)·`HideoutService.cs`, UI `HideoutHubView.cs`·`HideoutPresenter.cs`·`UiText.cs`, `Art/UI/HideoutUI.uxml`, EditorTools `MvpFinishDataSetup.cs`(새)·`M7SceneSetup.cs`(호출 1줄)·`M5DataSetup.cs`(주석), 테스트 `SaveSerializerV3Tests.cs`(새)·`StashUpgradeModelTests.cs`(새)·`ItemGridTests`(TryGrow)·`HideoutUiModelTests.cs`(Fake + 허브)·`SaveSerializerV2Tests.cs`(버전 단언)·PlayMode `M7WeaponRaidFlowTests.cs`(`"version": 2` 문자열)·`StashUpgradeFlowTests.cs`(새) | 2~2.5h |
| **F3 표시·수리** | gameplay-programmer(Core·Hideout) → ui-programmer(UI) 순 | Core `IDurableItem.cs`(새)·`WeaponItem.cs`·`IHideoutService.cs`, Hideout `PriceRules.cs`·`TraderModel.cs`·`TraderConfigSO.cs`·`HideoutService.cs`, UI `ItemConditionRules.cs`(새)·`HudConfigSO.cs`·`UiText.cs`·`InventoryScreenView.cs`·`TraderViewModel.cs`·`TraderView.cs`·`HideoutPresenter.cs`·`GameUIRoot.cs`/`HideoutRoot.cs`(경계 전달), EditorTools `MvpFinishDataSetup.cs`, 테스트 `DurabilityRulesTests`·`PriceRulesTests`/`TraderModelTests`(수리·매도)·`HideoutUiModelTests`(Fake·수리 줄)·`UiModelTests`(등급 문구) | 2~2.5h |
| **F4 방어구** | gameplay-programmer | Core `ArmorRules.cs`(새)·`ArmorItem.cs`(새)·`ItemInstance.cs`(ItemFactory)·`SaveSerializer.cs`, Player `PlayerConfigSO.cs`·`PlayerHealth.cs`(ApplyDamage), EditorTools `MvpFinishDataSetup.cs`, 테스트 `ArmorRulesTests.cs`(새)·`ArmorItemTests`·`SaveSerializerV3Tests`(방어구)·PlayMode `ArmorFlowTests.cs`(새) | 2~3h |
| **검증·리뷰** | test-engineer + code-reviewer(읽기 전용) | F4 뒤 한 번: 전체 EditMode·PlayMode 2회, `Apply M7 Content` 재실행 바이트 동일(git status·md5), IL2CPP 개발 빌드 + 스모크(세이브 version 3), 릴리스 빌드. 리뷰 수정 → CHANGELOG "리뷰 수정" → 커밋 | 1~1.5h + 빌드 |

---

## 7. AC별 테스트 계획

GDD §15에 이 작업의 AC가 없어 아래를 AC로 쓴다(§9 GDD 수정 제안).

**AC-F1 전신 사인** (EditMode 위주)
- `BodyHealthModel`: 탈수만으로 160초 → `DeathKind = Dehydration`, `DeathCause = Thorax`(기존 단언 유지). 출혈(파괴 다리 분산 포함)로 흉부 0 → `Bleeding`. 피격 → `Wound`. 사망 뒤 다른 경로가 바꾸지 않음.
- `PlayerDied(BodyPart)` 기존 생성자 = Wound. `RaidSession.EndByDeath(part, kind)` → `RaidResultBuilder` → `RaidResult.DeathKind`. 선택 인자 생략 = Wound.
- `RaidResultViewModel.TitleFor`: "사망 — 탈수", "사망 — 출혈", "사망 — 흉부", 원인 없음 "사망". 방사능 피폭 상태에서 HP가 0이 되지 않음(최대 × 0.8 자르기 회귀 1개).
- PlayMode 1개: 레이드 씬에서 수분 0 → 사망 → 결과 제목 "사망 — 탈수"(테스트에서 수분을 0으로 두고 시간 단축은 PlayerConfig 복제본 감소율 상향).

**AC-F2 보관함 업그레이드**
- `ItemGrid.TryGrow`: 키우면 아이템 자리 그대로·새 줄에 배치 가능, 줄이기·같은 크기 false.
- `StashUpgradeModel`: 0 → 1 → 2, 탄피 차감, 부족 = NotEnoughCasings(변화 없음), 최대 = MaxLevel, 떠나는 중 Unavailable. 읽기 전용 허용(Q8).
- `SaveSerializer`: v3 왕복(단계 2 + 40~49줄 아이템 자리 유지), v1·v2 → 단계 0, 단계 > 설정 → 최대, `"version": 3`, v4 지원 안 함, v1·v2·v3 파싱. `HideoutConfigSO.ToProfileDefaults` 크기 목록.
- 허브 뷰(UXML 로드): 상태·버튼 문구·비활성(탄피 부족·최대·떠나는 중), 클릭 → `TryUpgradeStash`.
- PlayMode `StashUpgradeFlowTests`: 은신처에서 확장 → 보관함 화면 줄 수 증가 → 메인 메뉴(W2 저장) → 다시 진입 시 단계·아이템 유지(SaveSandbox).

**AC-F3 내구도 표시·수리·가격**
- `DurabilityRules.Ratio`: 무기 0/49/50/100, 내구도 없는 아이템 = 1. `ItemConditionRules` 경계(0.5·0.25 정확히 경계 포함 규칙).
- `UiText.ItemLabelWithCondition`: 무기 "(양호)/(낡음)/(위험)", 탄창·잡동사니는 이름 그대로(`ItemLabel` 기존 테스트 유지). 숫자 문자 없음 단언(RULE-04).
- `PriceRules`: 매도 × 비율(무기 50% → 절반 + 탄창 전액), 100% = 기존 값(기존 테스트 유지), 수리비 공식·최소 1·100% = −1, `repairCostRatio ≥ sellPriceMultiplier`(에셋 값).
- `TraderModel.TryRepair`: Ok(탄피 차감·100), NotEnoughCasings, NotNeeded, NotInStash. `HideoutService` 위임 + `HideoutChanged`.
- 뷰: 인벤토리 칸 라벨에 등급(레이드·은신처), 상인 매도 줄 [수리] 표시·숨김.

**AC-F4 방어구**
- `ArmorRules.PenetrationChance`: (1,3) 0, (3,3) 0.5, (4,3) 0.75, (5,3) 1, (0,3) 0, 상하한 clamp. `Resolve`: 고정 시드 `System.Random` 관통/비관통, 확률 0·1이면 난수 미소비(같은 Random의 다음 값이 그대로), 비관통 피해 × 0.3·내구도 손실 = 피해 × 0.2, 관통 손실 0.
- `FindCovering`: 헬멧 = 머리만, 방탄복 = 흉부·복부, 팔다리 없음, 방독면·등급 0·내구도 0·`ItemInstance` 방어구 = null.
- `ArmorItem`: 생성 = max, 자르기, NaN. `ItemFactory` 방어구 → ArmorItem, 조끼(ContainerDefinitionSO) → ItemInstance 그대로(기존 테스트).
- 세이브: 방어구 내구도 왕복, −1·NaN·v2 → max, max 초과 → max, 방독면 −1.
- PlayMode `ArmorFlowTests`(결정적 조합만): M2 씬에서 방탄복(등급 3) 착용 + 9mm 투사체(관통 0%) 흉부 → 흉부 HP 감소 = 30%·방탄복 내구도 −6. 헬멧 내구도 0 → 머리 피해 그대로. 7.62 대 등급 2 테스트 정의(관통 100%) → 전부 관통·마모 없음.
- 기존 방독면 PlayMode(`SurvivalFlowTests`·`M7SurvivalRaidFlowTests`) 그대로 통과.

**회귀**: 기존 EditMode 1796·PlayMode 181 전부 통과(의도적으로 바꾸는 단언은 §8.1 목록만). `Apply M7 Content` 두 번째 실행 바이트 동일. IL2CPP 개발 빌드 스모크 종료 0(세이브 version 3).

### 7.1 에디터 확인 항목 (사람이)

1. 배치모드 또는 메뉴 `Ashfall/Setup/Apply MVP Finish Data`를 한 번 실행(또는 `Apply M7 Content`) — HideoutConfig·TraderConfig·HudConfig·PlayerConfig에 새 값이 보이는지 인스펙터로 확인.
2. **F2:** Hideout 씬 Play → 허브 "보관함 10×30", [보관함 확장 10×40 (5000 탄피)] → 탄피 감소·보관함 스크롤이 40줄 → 메인 메뉴 → 재진입 후 유지. 50줄에서 버튼 "보관함 최대" 비활성.
3. **F3:** 레이드에서 적 시체 무기를 주워 Tab — 이름 뒤 (양호)/(낡음)/(위험). 탈출 → 보관함으로 옮김 → [상인] 매도 줄 가격이 새 무기보다 낮음 → [수리] → 탄피 차감·"(양호)"·매도가 원래대로.
4. **F4:** 방탄복을 입고 레이드 → 약탈자(9mm)에게 맞으면 흉부가 쉽게 망가지지 않음(H 화면 등급), 인벤토리 방탄복 등급이 점점 낡음/위험으로. 은신처에서 수리.
5. **F1:** 레이드에서 붕대 없이 출혈로 죽으면 결과 "사망 — 출혈"(탈수 사인은 PlayMode 테스트로 확인 — 실제로는 16분 이상 걸림).
6. IL2CPP 개발 빌드 스모크(CLAUDE.md 명령) 종료 0, `smoke.log`의 "save version 3".

---

## 8. 위험

| 위험 | 대응 |
|---|---|
| **방어구 난수로 PlayMode 피격 테스트가 흔들림** | 현재 PlayMode에서 헬멧·방탄복을 끼고 맞는 테스트는 없다(확인: `InventoryFlowTests`는 방탄복을 장착만 하고 쏘지 않음, `SurvivalFlowTests.OrdinaryHelmet_DoesNotBlockStorm`은 `TEST-HLM` 등급 3이지만 `maxDurability` 0 → 내구도 0 → 판정 제외 — **F4에서 이 테스트가 피격을 추가하지 않는지 확인**, `M7SurvivalRaidFlowTests`는 방독면(등급 0)). M2·M4 시작 장비에 방어구 없음. 새 PlayMode 방어구 테스트는 확률 0/1 조합만 쓴다(난수 미소비). |
| 은신처 장비에 방어구를 넣고 레이드를 도는 PlayMode 흐름 테스트를 나중에 추가하면 적 사격 결과가 난수로 흔들림 | 흐름 테스트는 방어구를 넣지 않거나 9mm(확률 0)만 맞게. 리뷰 체크리스트에 넣는다. |
| 적 방어구(Q16 B/C를 고르면) | 적 사망 테스트(`EnemyDamageFlowTests`·`AiCombatFlowTests`·`CorpseLootFlowTests`·`M7AiRaidFlowTests`·`MutantFlowTests`)와 성능 테스트 교전 시간이 바뀐다 — 기존 에셋은 방어구 null로 두고 새 필드만. ★A면 해당 없음. |
| **밸런스**: 9mm·12ga는 등급 3에 관통 0% → 방탄복 착용 시 약탈자(권총·SMG)는 몸통에 30%만 | §5.3 공식 그대로. 팔다리·머리(헬멧 없으면)는 그대로 맞는다. 기획 확인 목록(§10)에 올린다. |
| 의도적으로 바뀌는 기존 단언 | §8.1 |
| UXML 요소 누락 → `RaidResultView.Find`가 예외 | F2에서 UXML과 뷰를 같은 커밋으로. `HideoutUiModelTests`가 실제 UXML을 로드해 확인. |
| 보관함 50줄 = 칸 요소 500개, 변경마다 재구성 | M5 300개 대비 1.7배, 은신처 전용(레이드 성능 무관). 문제면 이후 최적화. |
| v3 파일을 이전 빌드로 열면 격리 + 새 프로필 | 기존 규칙(M7 v2와 같음). 문서화만. |
| 인벤토리 라벨이 길어져 1×N 작은 칸에서 잘림 | 권총(2×1 등)에서 "(낡음)"이 잘릴 수 있다 — 에디터 확인 3번, 필요하면 USS 줄바꿈(F3 ui-programmer 판단). |

### 8.1 의도적으로 고치는 기존 테스트
- `SaveSerializerV2Tests.CurrentVersion_Is2` → 3, `V3_Unsupported` → `V4_Unsupported`(F2). `V1_And_V2_Parse`에 v3 추가.
- PlayMode `M7WeaponRaidFlowTests`의 `"version": 2` 문자열 2곳 → `SaveSerializer.CurrentVersion`으로(F2). (`M5TestSupport`·`HideoutSceneSmokeTests`·`SmokeScenario`는 이미 `CurrentVersion`)
- `FakeHideoutService`에 F2·F3 멤버 추가(컴파일).
- `BodyHealthSystemicTests`의 "사인 흉부 — 전신 사인 문구는 MVP 남은 목록" 주석 갱신(단언은 그대로).

---

## 9. GDD 본문 수정 제안 (CHANGELOG 기록 뒤 오케스트레이터가 반영)

- **§5.1** 끝에 한 줄: "내구도는 숫자 대신 대략 등급(양호/낡음/위험)으로만 보인다. 은신처 상인에게 탄피로 수리(100%), 매도가는 내구도 비율만큼."
- **§5.3** 끝에: "방어구가 덮는 부위(`covers`)에만 적용. 내구도 0이면 막지 못한다. 비관통 시 내구도 감소 = 원피해 × 0.2(임시). MVP에서 적은 방어구가 없다(공식은 같다)."
- **§9** 보관함 줄: "그리드 10×30에서 시작, 탄피로 10×40(5000) → 10×50(12000) 업그레이드(임시값)."
- **§12** 레이드 결과: "사망 사인 = 피격 부위, 또는 출혈·탈수."
- **§15** M7 아래에 "MVP 마무리 (M7 후속)" — [ ] 방어구 관통·둔탁(§5.3) [ ] 내구도 대략 표시·수리 [ ] 전신 사인 [ ] 보관함 업그레이드.
- CLAUDE.md "현재 진행"·"구조"(Core `ArmorRules`·`ArmorItem`·`IDurableItem`·`DeathCauseKind`, Hideout `StashUpgradeModel`, 세이브 version 3)는 검증 단계 끝에 갱신.

---

## 10. CHANGELOG_GDD.md 초안

```
## 2026-10-?? — MVP 마무리 설계 결정 (§5.1, §5.3, §7, §9, §12, §13.3, §14 — MF-Q1~Q24)
사용자 결정(2026-10-24): §5.3 방어구를 MVP에 넣고(M7 Q19 B 뒤집기), 무기 내구도 대략 표시·상인 수리·가격 반영(M7 Q15·Q16·Q17 뒤집기),
전신 사인 문구(M7 Q33 TODO 해소), 보관함 업그레이드(M5 Q1 뒤집기). 나머지 모호한 항목(`docs/design/MVP_finish_design.md` §5 MF-Q1~Q24)은 ★추천안.
코드 결정 지점에는 `TODO(GDD-?) MF-Qn`. 하위 단계 F1 사인 → F2 보관함 → F3 내구도 표시·수리 → F4 방어구, 단계마다 커밋.
- **§12 사인 (Q1~Q4):** Core `DeathCauseKind { Wound, Bleeding, Dehydration }`, `PlayerDied.Kind`·`RaidResult.DeathKind`(선택 인자, 기존 = Wound).
  결과 제목 "사망 — 탈수"/"사망 — 출혈"(출혈·중출혈 틱 사망, 파괴 부위 분산 포함)/피격은 기존 "사망 — 부위". 방사능은 최대 HP만 줄여 사인이 될 수 없다 — 문구 없음.
- **§9 보관함 업그레이드 (Q5~Q9):** 너비 10 고정, 30 → 40줄(5000 탄피) → 50줄(12000)(`HideoutConfigSO.stashUpgrades`, TODO). 허브 공통 줄 버튼, 확인 없음,
  읽기 전용 프로필에서도 허용(상인과 같음). 그리드는 제자리 확장(`ItemGrid.TryGrow`).
- **§13.3 세이브 version 3:** `SaveData.stashLevel`(v1·v2 → 0, 설정보다 크면 최대). 방어구 내구도는 `ItemRecord.durability` 재사용(절대값, 없음·음수·NaN·v < 3 → 최대) — 버전은 한 번만 올렸다.
- **§5.1 내구도 표시·수리·가격 (Q10~Q15):** 무기·방어구(`IDurableItem`) 이름 뒤 "(양호/낡음/위험)" — 비율 ≥ 0.5 / 0.25~0.5 / < 0.25(`HudConfigSO`, TODO), 숫자 없음(RULE-04).
  인벤토리 칸·장비 슬롯·상인 매도 줄. 매도 = 내림(base × 0.4 × 비율) + 장착 탄창, 매수는 새 물건이라 변화 없음.
  수리 = 보관함 아이템만, max(1, 올림(base × 0.5 × 잃은 비율)), 100%로(`TraderConfigSO.repairCostRatio`, TODO). 수리 후 되팔기 이득 없음(0.5 ≥ 0.4).
- **§5.3 방어구 (Q16~Q24):** 관통 확률 clamp01(0.5 + (관통력 − 등급) × 0.25), 비관통 = 피해 × 0.3(감소된 피해로 기존 부위 규칙·상태이상 굴림), 방어구 내구도 −= 원피해 × 0.2(비관통만, TODO).
  `covers` 부위만, 등급 < 1·covers 빈(방독면)·내구도 0이면 막지 않음, 내구도는 등급에 영향 없음. 근접(관통 0)도 같은 공식. 피격음·HUD 없음.
  순수 판정 Core `ArmorRules`(난수 `System.Random` 주입, 확률 0·1이면 난수 미소비), 수치 `PlayerConfigSO`(0.5·0.25·0.3 = §5.3, 감소율 TODO) → `ArmorRuleParams`.
  **적 방어구 없음(MVP, Q16 A)** — M3 "적에게 방어구 없음" 유지, 공식만 Core에서 공유. 런타임 `ArmorItem`(Core, `ItemFactory`가 내구도 최대로 생성).
- **§13.3 경계:** Core 추가 — `DeathCauseKind`, `IDurableItem`·`DurabilityRules`, `ArmorItem`, `ArmorRules`·`ArmorRuleParams`·`ArmorHitResult`, `IHideoutService`(StashLevel·NextStashUpgrade·TryUpgradeStash·GetRepairCost·TryRepair,
  `StashUpgradeOffer`·`StashUpgradeResult`·`RepairResult`), `ItemGrid.TryGrow`, `PlayerProfile.StashLevel`·`TryUpgradeStash`, `ProfileDefaults` 단계별 크기. 새 asmdef·이벤트·싱글톤 없음.
- **§14 스키마에 없는 추가:** `PlayerConfigSO`(armor 4필드), `HudConfigSO`(conditionWornBelow·conditionCriticalBelow), `TraderConfigSO.repairCostRatio`, `HideoutConfigSO.stashUpgrades`.
  에셋은 `Ashfall/Setup/Apply MVP Finish Data`(`MvpFinishDataSetup.ApplyAll`, `Apply M7 Content` 끝에서도 호출, 파일에 없는 필드만)가 기록. 방어구 에셋 3개는 그대로.
- **GDD에 수치가 없어 임시값을 넣은 항목 (TODO(GDD-?)):** 보관함 40/50줄·5000/12000 탄피, 등급 경계 0.5/0.25, 수리비 비율 0.5, 방어구 내구도 감소 × 0.2,
  매도 내구도 비율(하한 없음). 밸런스 확인: 9mm·12ga는 등급 3에 관통 0%(§5.3 공식 결과).
```

---

## 11. MVP 남은 목록 갱신 (M7 §10)

- 1(§5.3) → **해소**(F4). 적 방어구는 남음(Q16 A).
- 3(고장·내구도) → 표시·수리·가격 **해소**(F3). 남은 임시값: 등급 경계·수리비·감소율.
- 6(생존 해석) → 사인 문구 **해소**(F1). 머리 제외 탈수·방사능 효과 해석은 남음.
- 14(보관함 업그레이드) → **해소**(F2). 단계 수·비용은 임시값.
- 새 항목: 방어구 밸런스(9mm·12ga 관통 0%, 감소율 0.2), 보관함 50줄 UI 재구성 비용.

---

## 질문 결정 (오케스트레이터, 2026-10-24)
- §5 MF-Q1~Q24는 ★추천안을 채택한다. **예외: Q16은 B** — 사용자 결정이 "플레이어와 적 모두(§7 동일한 피해 공식)"이므로 적에게도 방어구를 붙인다.
  - `EnemyArchetypeSO.helmet`·`bodyArmor`(ArmorDefinitionSO, null = 없음, Core 스키마 추가). 판정은 같은 Core `ArmorRules`: 적 `Head` → covers ∋ Head, `Torso` → covers ∋ Thorax 또는 Stomach, `Legs` → 방어구 없음.
  - 적마다 런타임 내구도(시작 = maxDurability, 비관통 시 감소, 0이면 막지 못함). 시체 루트에는 넣지 않는다(B, 가장 단순 — `TODO(GDD-?) MF-Q16`).
  - 수치(0.5·0.25·0.3·0.2)는 AI 쪽 기존 전역 설정 SO(적이 이미 들고 있는 것)에 같은 4필드를 두고 `ArmorRuleParams`를 만든다. Player 에셋과 값이 같은지 EditMode 테스트로 고정.
  - 데이터: **AI-BOS = 헬멧 + 방탄복, AI-BGD = 방탄복**(보스 거점), 나머지 유형은 없음. 기존 에셋 값 기록은 `MvpFinishDataSetup`이 빈 필드만.
  - 난수: 적마다 방어구 전용 `System.Random`(기존 AI 난수 순서를 밀지 않게). 기존 적 사망 PlayMode 테스트가 BOS/BGD를 쏘는지 확인하고, 흔들리면 테스트가 결정적 조합(관통 0/1)이 되게 고친다.
- 진행: 순차 F1 → F2 → F3 → F4. 구현은 사본 `C:\Users\hena9\UnityProjects\Ashfall_mf`(git clone + Library 복사)에서 하고 단계마다 사본에서 커밋 → 원본으로 fast-forward. 마지막에 원본에서 전체 검증·IL2CPP 빌드(`Builds/MvpFinish/`).

## F1 구현 메모 (gameplay-programmer, 2026-10-24)
- 설계 §1.2·§3.1~3.5대로 구현: Core `DeathCauseKind`(`Combat/BodyEnums.cs`), `PlayerDied.Kind`(기존 생성자 = Wound), `RaidResult(..., deathKind = Wound)`·`DeathKind`,
  `BodyHealthModel.DeathKind`(진입점 `ApplyHit`·`ApplyDamage` = Wound, `Tick` = Bleeding, `ApplySystemicDamage` = Dehydration → `CheckDeath`가 복사, 사망 뒤 불변),
  `PlayerHealth.OnDied` → `PlayerDied(cause, Body.DeathKind)`, `RaidSession.DeathKind`·`EndByDeath(cause, kind = Wound)`, `RaidResultBuilder`·`RaidController` 전달,
  `RaidResultViewModel.TitleFor`(출혈·탈수는 부위 없이, 피격은 기존 부위 문구, 원인 없음 "사망"), `UiText.DeathBleeding`·`DeathDehydration`. `Died(BodyPart)` 서명·`DeathCause` 그대로.
- 설계와 다른 점: 없음. `UiText`에 "사망 — " 접두어는 넣지 않았다(기존 `TitleFor` 문자열 그대로). 방사능 사인 없음(MF-Q3, 회귀 테스트 1개). CHANGELOG_GDD는 손대지 않았다(§10 초안 = 오케스트레이터).
- 테스트: EditMode `DeathCauseTests.cs` 26개(몸 10·이벤트/결과 3·세션 6·제목 7), PlayMode `DeathCauseFlowTests` 1개(EditMode 1823·PlayMode 182 전부 통과, 레이드 씬 수분 0 → 흉부 사망 → `PlayerDied(Thorax, Dehydration)` → 은신처 결과 제목 "사망 — 탈수";
  시간 단축은 `PlayerHealth.config`를 복제본(dehydratedHpPerSec 40)으로 리플렉션 교체, 에셋 불변, SaveSandbox 새 폴더). 기존 단언 변경 없음(`BodyHealthSystemicTests` 주석 1곳만).
- 사본 환경 메모: git clone(autocrlf)으로 CRLF가 된 `.mat`·`.asset`·`.terrainlayer` 17개(+ PlayMode 뒤 `ProjectSettings.asset`)를 첫 EditMode 실행의 에셋 재생성이 LF로 다시 써서 `AtmosphereAssetTests.CreateAssets_Rerun_KeepsGuidsAndBytes`가 1회 실패, 두 번째 실행부터 통과(내용 차이 없음, `git diff` 빈 결과). F1과 무관 — 커밋에 넣지 않는다.

## F2 구현 메모 (gameplay-programmer, 2026-10-24)
- 설계 §1.3·§1.6·§3대로 구현: Core `ItemGrid.TryGrow`(두 축 ≥, 같은 크기 false), `ProfileDefaults(IReadOnlyList<Vector2Int>, int)`·`StashSizeFor`·`MaxStashLevel`(기존 생성자 = 단계 1개),
  `PlayerProfile(size, casings, stashLevel = 0)`·`StashLevel`·`TryUpgradeStash`, `SaveData.stashLevel`, `SaveSerializer.CurrentVersion = 3`·`ReadStashLevel`(v < 3·음수 → 0, 최대로 자름),
  `IHideoutService.StashLevel`·`NextStashUpgrade`·`TryUpgradeStash()`·`StashUpgradeOffer`(생성자 = HasNext, `None`)·`StashUpgradeResult`. Hideout `StashUpgradeStep`·`HideoutConfigSO.stashUpgrades`(C# 기본 40/5000·50/12000, TODO MF-Q5),
  순수 `StashUpgradeModel`(최대 → MaxLevel, 부족 → NotEnoughCasings, 확장 실패 → Unavailable, 성공 때만 차감), `HideoutService` 위임(떠나는 중·프로필 없음 Unavailable, 읽기 전용 허용 MF-Q8) + `HideoutChanged`.
  UI `HideoutHubView`(요소 2개, 생성자 끝 선택 인자 `onStashUpgrade` — 기존 호출 유지)·`HideoutPresenter`(`StashStatusText`·`StashUpgradeText`·`CanUpgradeStash`·`UpgradeStash()`)·`HideoutRoot` 연결·`UiText` 문구, `HideoutUI.uxml` 키트 줄 다음 줄(기존 `hideout-kit`·`hideout-kit-status`·`ash-button`만).
  EditorTools `MvpFinishDataSetup`(메뉴·`ApplyAll` = `ApplyHideoutConfig`), `M7SceneSetup.ApplyAll` 끝 호출, `M5DataSetup` 주석.
- 설계와 다른 점: ① `HideoutConfigSO`에 도우미 `BaseStashSize`·`ValidStashUpgradeCount`·`StashSizesByLevel()`·`TryGetStashUpgrade(from, out size, out cost)` — 줄 수가 이전보다 크지 않은 단계부터는 무시(설정 오류에도 `TryGrow` 실패 없음), `OnValidate`는 rows ≥ 이전 + 1로 고친다.
  ② 성공 문구 `UiText.StashUpgraded(size)` "보관함을 10×40으로 늘렸다."·`MessageStashMaxLevel` 추가(설계에 "결과 문구"만 있었다). ③ §8.1 목록에 없던 기존 단언 3곳도 같은 이유(버전 3)로 고쳤다:
  `SaveSerializerTests.ToJson_WritesSchemaKeys`("version": 2 → `CurrentVersion`), `SaveSerializerTests.TryParse_NewerVersion_Unsupported`(3 → 4), `SaveSerializerV2Tests.V1File_ResavedAsV2`("version": 2 → `CurrentVersion`).
  ④ EditMode에는 UI Toolkit 클릭 흉내가 없어 버튼 클릭 → `TryUpgradeStash`는 PlayMode `StashUpgradeFlowTests`(UiPointer)로 확인한다.
- 에셋: 사본에서 `-executeMethod Ashfall.EditorTools.MvpFinishDataSetup.ApplyAll` 1회 → `HideoutConfig.asset`에 `stashUpgrades`(40/5000, 50/12000) 기록, 2회째 기록 로그 없음·md5 동일.
  EditMode `MvpFinishDataAssetTests`가 파일 기록·재실행 바이트 동일을 고정한다.
- 테스트: EditMode 새 46개(`SaveSerializerV3Tests`·`StashUpgradeModelTests`(모델·서비스 위임·에셋)·`ItemGridTests` TryGrow 2·`HideoutStashUpgradeUiTests`), PlayMode `StashUpgradeFlowTests` 1개.
  사본에서 EditMode 1869·PlayMode 183 전부 통과. 새 `.meta`는 Unity가 만들었다(손으로 만든 것 없음). CHANGELOG_GDD는 손대지 않았다(§10 초안 = 오케스트레이터).
