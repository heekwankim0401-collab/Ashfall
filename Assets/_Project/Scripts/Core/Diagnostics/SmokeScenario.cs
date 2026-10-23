using System;
using System.Collections.Generic;

namespace Ashfall.Core
{
    // 스모크가 쓰는 Core 부품 (GameManager가 가진 것 + 스모크 세이브 저장소). 기능 어셈블리는 참조하지 않는다.
    public sealed class SmokeEnvironment
    {
        public SmokeEnvironment(GameFlow flow, ServiceRegistry services, EventBus events, ProfileService profiles,
            ISaveStorage storage, float raidDurationSec = SmokeScenario.DefaultRaidDurationSec)
        {
            Flow = flow ?? throw new ArgumentNullException(nameof(flow));
            Services = services ?? throw new ArgumentNullException(nameof(services));
            Events = events ?? throw new ArgumentNullException(nameof(events));
            Profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
            Storage = storage ?? throw new ArgumentNullException(nameof(storage));
            RaidDurationSec = raidDurationSec;
        }

        public GameFlow Flow { get; }
        public ServiceRegistry Services { get; }
        public EventBus Events { get; }
        public ProfileService Profiles { get; }
        // 스모크 세이브 저장소 — 쓰인 파일을 다시 읽어 확인한다 (ProfileService와 같은 위치)
        public ISaveStorage Storage { get; }
        public float RaidDurationSec { get; }
    }

    // 스모크 흐름 단계 표 (M7 설계 §1.9, Q51):
    // Boot → MainMenu → Hideout(저장·읽기) → Raid(폭풍 강제 시작, 10초) → 포기 → Hideout(결과 반영·저장) → 종료.
    // 씬 이동은 GameFlow, 은신처 명령은 IHideoutService, 레이드 조회는 IRaidInfo·IAshStormInfo, 명령은 이벤트
    // (AshStormStartRequested·RaidAbandonRequested)만 쓴다. 단계 함수는 순수 C# — EditMode에서 가짜 씬 로드로 끝까지 돌릴 수 있다.
    public static class SmokeScenario
    {
        // TODO(GDD-?) Q51: 기술 상수(게임 수치 아님) — 레이드에 머무는 시간(레이드 경과 초)
        public const float DefaultRaidDurationSec = 10f;

        public const string StepBoot = "Boot";
        public const string StepEnterHideout = "EnterHideout";
        public const string StepHideoutSaveLoad = "HideoutSaveLoad";
        public const string StepStartRaid = "StartRaid";
        public const string StepForceStorm = "ForceStorm";
        public const string StepRaidPlay = "RaidPlay";
        public const string StepAbandon = "Abandon";
        public const string StepHideoutResult = "HideoutResult";

        // 단계 순서 (Build와 같다)
        public static readonly IReadOnlyList<string> StepNames = new[]
        {
            StepBoot, StepEnterHideout, StepHideoutSaveLoad, StepStartRaid,
            StepForceStorm, StepRaidPlay, StepAbandon, StepHideoutResult,
        };

        public static IReadOnlyList<SmokeStep> Build(SmokeEnvironment env)
        {
            if (env == null)
                throw new ArgumentNullException(nameof(env));
            return new[]
            {
                // Boot 씬의 BootLoader가 MainMenu를 부른다
                new SmokeStep(StepBoot, c => WaitPhase(env, c, GamePhase.MainMenu)),
                new SmokeStep(StepEnterHideout, c => EnterHideout(env, c)),
                new SmokeStep(StepHideoutSaveLoad, c => HideoutSaveLoad(env, c)),
                new SmokeStep(StepStartRaid, c => StartRaid(env, c)),
                new SmokeStep(StepForceStorm, c => ForceStorm(env, c)),
                new SmokeStep(StepRaidPlay, c => RaidPlay(env, c)),
                new SmokeStep(StepAbandon, c => Abandon(env, c)),
                new SmokeStep(StepHideoutResult, c => HideoutResult(env, c)),
            };
        }

        private static SmokeStatus WaitPhase(SmokeEnvironment env, SmokeStepContext c, GamePhase phase)
        {
            c.Detail = $"단계 {env.Flow.Phase} → {phase} 대기";
            if (env.Flow.Phase != phase)
                return SmokeStatus.Running;
            c.Detail = $"단계 {phase}";
            return SmokeStatus.Passed;
        }

        // MainMenu → Hideout: EnterHideout 요청 → 단계 Hideout + IHideoutService 등록(W1 진입 절차 끝)
        private static SmokeStatus EnterHideout(SmokeEnvironment env, SmokeStepContext c)
        {
            if (c.IsFirstTick && !env.Flow.EnterHideout())
            {
                c.Detail = "GameFlow.EnterHideout 거부";
                return SmokeStatus.Failed;
            }
            c.Detail = "은신처 로드 대기";
            if (env.Flow.Phase != GamePhase.Hideout || !env.Services.TryGet(out IHideoutService _))
                return SmokeStatus.Running;
            c.Detail = $"프로필 로드 {env.Profiles.IsLoaded}, 세이브 {env.Profiles.StorageLocation}";
            return SmokeStatus.Passed;
        }

        // 은신처: 프로필 저장 → 파일을 다시 읽어 버전·raidInProgress 확인
        private static SmokeStatus HideoutSaveLoad(SmokeEnvironment env, SmokeStepContext c)
        {
            ProfileService profiles = env.Profiles;
            if (!profiles.IsLoaded || profiles.IsReadOnly)
            {
                c.Detail = $"프로필 로드 {profiles.IsLoaded}, 읽기 전용 {profiles.IsReadOnly}";
                return SmokeStatus.Failed;
            }
            if (!profiles.Save())
            {
                c.Detail = $"저장 실패: {profiles.LastSaveError}";
                return SmokeStatus.Failed;
            }
            if (!TryReadSave(env, out SaveData data, out string error))
            {
                c.Detail = error;
                return SmokeStatus.Failed;
            }
            if (data.raidInProgress)
            {
                c.Detail = "은신처 저장에 raidInProgress = true";
                return SmokeStatus.Failed;
            }
            c.Detail = $"save version {data.version}, 탄피 {data.casings}";
            return SmokeStatus.Passed;
        }

        // 은신처 → 레이드: IHideoutService.StartRaid(W4 = raidInProgress 기록) → 단계 Raid + IRaidInfo·IAshStormInfo
        private static SmokeStatus StartRaid(SmokeEnvironment env, SmokeStepContext c)
        {
            if (c.IsFirstTick)
            {
                if (!env.Services.TryGet(out IHideoutService hideout) || !hideout.StartRaid())
                {
                    c.Detail = "IHideoutService.StartRaid 거부";
                    return SmokeStatus.Failed;
                }
                // RULE-03 기록이 파일에 있어야 한다 (BeginRaid 저장)
                if (!TryReadSave(env, out SaveData data, out string error))
                {
                    c.Detail = error;
                    return SmokeStatus.Failed;
                }
                if (!data.raidInProgress)
                {
                    c.Detail = "레이드 시작 저장에 raidInProgress = false";
                    return SmokeStatus.Failed;
                }
            }
            c.Detail = "레이드 로드 대기";
            if (env.Flow.Phase != GamePhase.Raid || !env.Services.TryGet(out IRaidInfo _) || !env.Services.TryGet(out IAshStormInfo _))
                return SmokeStatus.Running;
            c.Detail = "raidInProgress 기록됨, 레이드 씬 로드";
            return SmokeStatus.Passed;
        }

        // 재 폭풍 강제 시작 → 단계가 Pending을 벗어날 때까지
        private static SmokeStatus ForceStorm(SmokeEnvironment env, SmokeStepContext c)
        {
            if (!env.Services.TryGet(out IAshStormInfo storm))
            {
                c.Detail = "IAshStormInfo 없음";
                return SmokeStatus.Failed;
            }
            if (c.IsFirstTick)
                env.Events.Publish(new AshStormStartRequested());
            c.Detail = $"폭풍 {storm.Phase} 대기";
            if (storm.Phase == AshStormPhase.Pending)
                return SmokeStatus.Running;
            c.Detail = $"폭풍 {storm.Phase}, 강도 {storm.Intensity:0.00}";
            return SmokeStatus.Passed;
        }

        // 레이드 경과 시간(일시정지 중 멈춤)이 RaidDurationSec에 닿을 때까지
        private static SmokeStatus RaidPlay(SmokeEnvironment env, SmokeStepContext c)
        {
            if (env.Flow.Phase != GamePhase.Raid || !env.Services.TryGet(out IRaidInfo raid))
            {
                c.Detail = $"레이드가 먼저 끝났다 (단계 {env.Flow.Phase})";
                return SmokeStatus.Failed;
            }
            c.Detail = $"레이드 {raid.ElapsedSec:0.0}/{env.RaidDurationSec:0}초";
            if (raid.ElapsedSec < env.RaidDurationSec)
                return SmokeStatus.Running;
            string storm = env.Services.TryGet(out IAshStormInfo s) ? $"{s.Phase} 강도 {s.Intensity:0.00}" : "없음";
            c.Detail = $"레이드 {raid.ElapsedSec:0.0}초, 폭풍 {storm}";
            return SmokeStatus.Passed;
        }

        // 포기 요청 → 종료 오버레이(실시간 지연) → 은신처 로드 + IHideoutService (W1 결과 반영·저장)
        private static SmokeStatus Abandon(SmokeEnvironment env, SmokeStepContext c)
        {
            if (c.IsFirstTick)
                env.Events.Publish(new RaidAbandonRequested());
            c.Detail = $"은신처 복귀 대기 (단계 {env.Flow.Phase})";
            if (env.Flow.Phase != GamePhase.Hideout || !env.Services.TryGet(out IHideoutService _))
                return SmokeStatus.Running;
            c.Detail = "은신처 복귀";
            return SmokeStatus.Passed;
        }

        // 결과 반영 확인: 포기 결과, 메모리·파일 모두 raidInProgress = false, 파일 버전 = 현재
        private static SmokeStatus HideoutResult(SmokeEnvironment env, SmokeStepContext c)
        {
            RaidResult result = env.Flow.PendingResult;
            if (result == null || result.Outcome != RaidOutcome.Abandoned)
            {
                c.Detail = $"레이드 결과 {(result != null ? result.Outcome.ToString() : "없음")} (기대 Abandoned)";
                return SmokeStatus.Failed;
            }
            if (env.Flow.HasRaidInFlow || env.Profiles.Profile == null || env.Profiles.Profile.RaidInProgress)
            {
                c.Detail = "흐름 레이드 표·raidInProgress가 남았다 (W1 반영 안 됨)";
                return SmokeStatus.Failed;
            }
            if (env.Profiles.LastSaveFailed)
            {
                c.Detail = $"W1 저장 실패: {env.Profiles.LastSaveError}";
                return SmokeStatus.Failed;
            }
            if (!TryReadSave(env, out SaveData data, out string error))
            {
                c.Detail = error;
                return SmokeStatus.Failed;
            }
            if (data.raidInProgress)
            {
                c.Detail = "귀환 저장에 raidInProgress = true";
                return SmokeStatus.Failed;
            }
            c.Detail = $"결과 {result.Outcome} ({result.DurationSec:0.0}초), save version {data.version}, raidInProgress {data.raidInProgress}";
            return SmokeStatus.Passed;
        }

        // 저장소에서 다시 읽어 해석 + 버전 확인
        private static bool TryReadSave(SmokeEnvironment env, out SaveData data, out string error)
        {
            data = null;
            SaveReadStatus read = env.Storage.TryRead(out string text, out string readError);
            if (read != SaveReadStatus.Ok)
            {
                error = $"세이브 읽기 {read}: {readError}";
                return false;
            }
            if (!SaveSerializer.TryParse(text, out data, out SaveLoadStatus status, out string parseError))
            {
                error = $"세이브 해석 {status}: {parseError}";
                return false;
            }
            if (data.version != SaveSerializer.CurrentVersion)
            {
                error = $"세이브 버전 {data.version} (기대 {SaveSerializer.CurrentVersion})";
                return false;
            }
            error = null;
            return true;
        }
    }
}
