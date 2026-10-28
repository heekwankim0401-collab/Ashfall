using System;
using System.Collections.Generic;
using Ashfall.Core;
using UnityEngine;

namespace Ashfall.Raid
{
    // 레이드 한 판 (§2 코어 루프, §8.2 탈출, RULE-02 타이머, M4 설계 §1.2). 순수 C#.
    // 한 틱 순서: ① 끝났으면 무시 ② 탈출 진행(경과 시간을 더하기 전) ③ 탈출 완료 → Extracted ④ 경과 ≥ 제한 → MIA.
    // 같은 틱에 대기 완료와 시간 초과가 겹치면 탈출 우선. 사망·포기는 호출 즉시. 종료는 한 번만, 이후 입력은 모두 무시.
    public sealed class RaidSession
    {
        private readonly RaidConfigSO config;
        private readonly ExtractionModel[] extractions;
        // 켜진 스위치 id (EXT-02). 몇 개 안 되므로 목록 선형 탐색 (할당 없음).
        private readonly List<string> switchesOn = new List<string>();

        public RaidSession(RaidConfigSO config, IReadOnlyList<ExtractionModel> extractions)
        {
            this.config = config ? config : throw new ArgumentNullException(nameof(config));
            int count = extractions?.Count ?? 0;
            this.extractions = new ExtractionModel[count];
            for (int i = 0; i < count; i++)
                this.extractions[i] = extractions[i];
        }

        public float ElapsedSec { get; private set; }
        public float TimeLimitSec => config.timeLimitSec;
        public float RemainingSec => Mathf.Max(0f, config.timeLimitSec - ElapsedSec);
        public bool IsOver { get; private set; }
        public RaidOutcome Outcome { get; private set; }               // IsOver일 때만 의미
        public ExtractionModel CompletedExtraction { get; private set; }
        public ExtractionModel CurrentZone { get; private set; }       // 플레이어가 있는 활성 구역 (없으면 null)
        public bool HasDeathCause { get; private set; }
        public BodyPart DeathCause { get; private set; }
        // MVP 마무리 F1: 사인 종류 (HasDeathCause일 때만 의미, 기본 Wound)
        public DeathCauseKind DeathKind { get; private set; } = DeathCauseKind.Wound;
        public int EnemiesKilled { get; private set; }
        // DEV-RETURN: 개발용 귀환으로 끝났는지 (Outcome은 Extracted, CompletedExtraction은 null)
        public bool IsDevReturn { get; private set; }
        public IReadOnlyList<ExtractionModel> Extractions => extractions;

        // 종료 순간 한 번
        public event Action Ended;

        public bool IsSwitchOn(string switchId) =>
            !string.IsNullOrEmpty(switchId) && switchesOn.Contains(switchId);

        // 처음 켜면 true. 이미 켜져 있거나 끝난 레이드면 false. TODO(GDD-?) Q5: 한 번 켜면 레이드 끝까지 유지.
        public bool ActivateSwitch(string switchId)
        {
            if (IsOver || string.IsNullOrEmpty(switchId) || switchesOn.Contains(switchId))
                return false;
            switchesOn.Add(switchId);
            return true;
        }

        public void RecordKill()
        {
            if (!IsOver)
                EnemiesKilled++;
        }

        // 한 프레임 (RaidController용). M4 리뷰 수정: 사망은 보통 PlayerDied 이벤트로 즉시 끝나지만,
        // 구독 전·Start 전에 죽어 이벤트를 놓쳤어도 죽은 몸이 탈출·MIA하지 않게 매 틱 IsDead를 먼저 본다(원인 불명 사망).
        public void Tick(float dt, IPlayerTarget player, IInventoryService inventory)
        {
            if (IsOver)
                return;
            if (player != null && player.IsDead)
            {
                EndByDeath();
                return;
            }
            Tick(dt, player != null ? player.Position : Vector3.zero, inventory);
        }

        // 한 프레임. inventory는 배낭·아이템 판정과 완료 시 소모(TryConsume)에 쓴다. null이면 배낭 없음·아이템 없음.
        public void Tick(float dt, Vector3 playerPosition, IInventoryService inventory)
        {
            if (IsOver)
                return;
            if (dt < 0f)
                dt = 0f;

            bool hasBackpack = inventory != null && inventory.GetEquipped(EquipSlot.Backpack) != null;
            CurrentZone = null;

            for (int i = 0; i < extractions.Length; i++)
            {
                ExtractionModel model = extractions[i];
                if (model == null || !model.IsActive)
                    continue;

                ExtractionPointConfig cfg = model.Config;
                bool inZone = ExtractionRules.IsInZone(playerPosition, model.Position, cfg.radiusM, config.zoneHeightToleranceM);
                ExtractionBlock block = ExtractionBlock.None;
                if (inZone)
                {
                    // M4 리뷰 수정: 완료 순간 TryConsume과 같은 범위(저장 공간·잠기지 않은 것)로 판정한다.
                    // Contains(장착·잠김 포함)를 쓰면 소모할 수 없는 신호탄으로 20초 대기 → 실패 → 처음부터가 반복된다.
                    bool hasItem = cfg.consumedItem == null || (inventory != null && inventory.CanConsume(cfg.consumedItem));
                    block = ExtractionRules.Evaluate(cfg, ElapsedSec, IsSwitchOn(cfg.requiredSwitchId), hasBackpack, hasItem);
                    if (CurrentZone == null)
                        CurrentZone = model;
                }

                if (!model.Tick(dt, inZone, block))
                    continue;

                // TODO(GDD-?) Q6: 신호탄은 대기 완료 순간 1개 소모. 소모하지 못하면(같은 틱 안의 경합 등 안전망) 탈출하지 않고 처음부터.
                if (cfg.consumedItem != null && (inventory == null || !inventory.TryConsume(cfg.consumedItem)))
                {
                    model.Interrupt(ExtractionBlock.MissingItem);
                    continue;
                }
                // 이 틱의 시간도 레이드 시간에 넣는다. 시간 초과는 보지 않는다 (같은 틱이면 탈출 우선).
                ElapsedSec += dt;
                Finish(RaidOutcome.Extracted, model);
                return;
            }

            ElapsedSec += dt;
            // RULE-02: 제한 시간 도달 = MIA (사망 처리, 몸은 그대로)
            if (ElapsedSec >= config.timeLimitSec)
                Finish(RaidOutcome.MissingInAction, null);
        }

        // MVP 마무리 F1(MF-Q1): kind 생략 = 피격(Wound) — 기존 호출 그대로.
        public void EndByDeath(BodyPart cause, DeathCauseKind kind = DeathCauseKind.Wound)
        {
            if (IsOver)
                return;
            HasDeathCause = true;
            DeathCause = cause;
            DeathKind = kind;
            Finish(RaidOutcome.Killed, null);
        }

        // 원인 부위를 모르는 사망 (Tick의 IsDead 안전망). 결과 화면은 "사망"만 표시한다.
        public void EndByDeath()
        {
            if (!IsOver)
                Finish(RaidOutcome.Killed, null);
        }

        // TODO(GDD-?) Q16: 일시정지 메뉴의 레이드 포기 = 사망과 같은 RULE-01
        public void Abandon()
        {
            if (!IsOver)
                Finish(RaidOutcome.Abandoned, null);
        }

        // DEV-RETURN: 개발용 귀환 = 탈출 성공과 같은 결과(Extracted, 반출 전부). 개발 빌드 판정은 호출하는 쪽(RaidController)이 한다.
        public void DevReturn()
        {
            if (IsOver)
                return;
            IsDevReturn = true;
            Finish(RaidOutcome.Extracted, null);
        }

        // 디버그 메뉴·테스트용. 종료 판정은 다음 Tick.
        public void AdvanceTime(float seconds)
        {
            if (IsOver || seconds <= 0f)
                return;
            ElapsedSec += seconds;
        }

        private void Finish(RaidOutcome outcome, ExtractionModel completed)
        {
            IsOver = true;
            Outcome = outcome;
            CompletedExtraction = completed;
            CurrentZone = null;
            Ended?.Invoke();
        }
    }
}
