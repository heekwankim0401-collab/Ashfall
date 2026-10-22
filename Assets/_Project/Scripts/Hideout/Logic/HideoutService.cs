using System;
using System.Collections.Generic;
using Ashfall.Core;
using UnityEngine;

namespace Ashfall.Hideout
{
    // SYS-HUB 은신처 서비스 (M5 설계 §1.4·§3·§4.2). 순수 C# — HideoutController가 만들고 IHideoutService로 등록한다.
    // 진입 절차(W1, Enter) · 저장(W2 메뉴 복귀 / W3 앱 종료 = SaveNow) · 레이드 시작(W4, GameFlow.StartRaid) + 상인·키트 조합.
    // 거래·키트는 메모리에서만 바뀌고 W2/W3/W4에서 함께 저장된다 (Q7) — 프로필 전체를 한 번에 써서 크래시에도 복제·소실이 없다.
    public sealed class HideoutService : IHideoutService
    {
        private readonly ProfileService profiles;
        private readonly GameFlow flow;
        private readonly HideoutConfigSO config;
        private readonly TraderConfigSO trader;
        private readonly Func<IInventoryService> loadoutSource;
        private readonly Func<DateTime> utcNow;
        private readonly Action publishChanged;

        // 모델이 감싼 프로필 (UseStorage 등으로 프로필이 바뀌면 다시 만든다)
        private PlayerProfile modelProfile;
        private TraderModel traderModel;
        private VagrantKitModel kitModel;

        // ── W1 진입 절차 ────────────────────────────────────

        // EnsureLoaded(첫 로드·RULE-03 감지) → TakeRaidReturn
        //   Finished → ApplyRaidResult / Interrupted → ApplyInterruptedRaid /
        //   None인데 PendingResult 있음 → 표시만(흐름 밖 레이드, Q25) + 경고
        // → Save(raidInProgress=false). 저장 실패는 진입을 막지 않는다(LastSaveFailed → UI 알림).
        // 돌려주는 것 = EnsureLoaded의 보고서(같은 실행의 재진입이면 첫 로드 보고서). 알림 표시는 PendingNotice로 판단.
        // entryToken(은신처 씬 Scene.handle) ≠ 0이면 끝에 ProfileService.MarkHideoutEntered — 은신처 인벤토리가
        // "이 씬의 진입 절차(결과 반영)가 끝났다"를 확인한다 (M5 리뷰 5). 시작할 때 이전 표시를 지운다.
        public static SaveLoadReport Enter(ProfileService profiles, GameFlow flow, IItemCatalog catalog, ProfileDefaults defaults,
            int entryToken = 0)
        {
            if (profiles == null)
                throw new ArgumentNullException(nameof(profiles));
            if (flow == null)
                throw new ArgumentNullException(nameof(flow));

            profiles.ClearHideoutEntry();
            SaveLoadReport report = profiles.EnsureLoaded(catalog, defaults);

            switch (flow.TakeRaidReturn(out RaidResult result))
            {
                case RaidReturn.Finished:
                    profiles.ApplyRaidResult(result);
                    break;
                case RaidReturn.Interrupted:
                    profiles.ApplyInterruptedRaid();
                    break;
                default:
                    if (profiles.Profile.RaidInProgress)
                    {
                        // 흐름 표 없이 메모리에 레이드 중 표시가 남음(정상 흐름에선 없음) → 파일로 새지 않게 사망과 같게
                        Debug.LogWarning("[HideoutService] 흐름 레이드 표 없이 레이드 중 표시가 남아 결과 없는 귀환으로 처리한다.");
                        profiles.ApplyInterruptedRaid();
                    }
                    else if (flow.PendingResult != null)
                    {
                        Debug.LogWarning("[HideoutService] 흐름 밖 레이드 결과 — 표시만 하고 프로필에 반영하지 않는다 (TODO(GDD-?) Q25).");
                    }
                    break;
            }

            profiles.Save();
            profiles.MarkHideoutEntered(entryToken);
            return report;
        }

        // Enter + 서비스 생성을 예외 없이 (M5 리뷰 2). 실패하면 LogException, 진입 표시를 지우고, 흐름 레이드 표를 비운다
        // (다음 [레이드 시작]이 표에 막히지 않게). 표에서 버린 결과는 반영되지 않는다 — 메모리에 RaidInProgress가 남아 있으면
        // 다음 진입이 결과 없는 귀환(사망과 같게)으로, 아직 읽지 못했으면 파일의 raidInProgress가 다음 실행에서 RULE-03으로 처리한다.
        // 호출자(HideoutController)는 false면 메인 메뉴로 돌아간다.
        public static bool TryEnter(ProfileService profiles, GameFlow flow, IItemCatalog catalog, ProfileDefaults defaults,
            int entryToken, Func<HideoutService> createService, out SaveLoadReport report, out HideoutService service)
        {
            report = null;
            service = null;
            try
            {
                report = Enter(profiles, flow, catalog, defaults, entryToken);
                service = createService != null ? createService() : null;
                if (service == null)
                    throw new InvalidOperationException("HideoutService를 만들지 못했다.");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Debug.LogError("[HideoutService] 은신처 진입 절차 실패 — 메인 메뉴로 돌아간다.");
                report = null;
                service = null;
                profiles?.ClearHideoutEntry();
                if (flow != null && flow.TakeRaidReturn(out _) != RaidReturn.None)
                    Debug.LogWarning("[HideoutService] 진입 실패로 흐름 레이드 표를 비웠다 — 그 레이드 결과는 반영되지 않았다.");
                return false;
            }
        }

        // profiles는 Enter 뒤(로드됨)여야 한다. loadoutSource는 은신처 인벤토리(IInventoryService)를 그때그때 찾는다(없으면 null).
        // publishChanged = HideoutChanged 발행 (거래·키트·떠남 상태가 바뀔 때).
        public HideoutService(ProfileService profiles, GameFlow flow, HideoutConfigSO config, TraderConfigSO trader,
            Func<IInventoryService> loadoutSource, Func<DateTime> utcNow, Action publishChanged)
        {
            this.profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
            this.flow = flow ?? throw new ArgumentNullException(nameof(flow));
            this.config = config != null ? config : throw new ArgumentNullException(nameof(config));
            this.trader = trader != null ? trader : throw new ArgumentNullException(nameof(trader));
            this.loadoutSource = loadoutSource ?? (() => null);
            this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
            this.publishChanged = publishChanged ?? (() => { });
            if (!profiles.IsLoaded)
                throw new InvalidOperationException("HideoutService: 프로필을 먼저 읽어야 한다 (HideoutService.Enter).");
        }

        private PlayerProfile Profile => profiles.Profile;

        // ── 읽기 ────────────────────────────────────────────

        public int Casings => Profile?.Casings ?? 0;
        public string TraderName => trader.displayName;
        public IReadOnlyList<TraderOffer> Offers => Trader?.Offers ?? (IReadOnlyList<TraderOffer>)Array.Empty<TraderOffer>();
        public IGridView Stash => Profile?.Stash;
        public bool IsKitAvailable => Kit != null && Kit.IsAvailable;
        public float KitCooldownRemainingSec => Kit?.RemainingSec ?? 0f;
        public SaveLoadReport PendingNotice => profiles.PendingNotice;
        public bool LastSaveFailed => profiles.LastSaveFailed;
        public bool IsReadOnly => profiles.IsReadOnly;
        public bool IsLeaving { get; private set; }

        // W2 저장이 실패해 한 번 멈췄다 → 다음 [메인 메뉴]는 저장이 또 실패해도 이동한다 (M5 리뷰 6)
        private bool leaveDespiteSaveFailure;

        public int GetSellPrice(ItemInstance item) => Trader?.GetSellPrice(item) ?? -1;

        // ── 명령 ────────────────────────────────────────────

        public TradeResult TryBuy(ItemDefinitionSO item)
        {
            if (IsLeaving || Trader == null)
                return TradeResult.Unavailable;
            TradeResult result = Trader.TryBuy(item);
            if (result == TradeResult.Ok)
                publishChanged();
            return result;
        }

        public TradeResult TrySell(ItemInstance item)
        {
            if (IsLeaving || Trader == null)
                return TradeResult.Unavailable;
            TradeResult result = Trader.TrySell(item);
            if (result == TradeResult.Ok)
                publishChanged();
            return result;
        }

        public KitClaimResult TryClaimKit()
        {
            if (IsLeaving || Kit == null)
                return KitClaimResult.Unavailable;
            KitClaimResult result = Kit.TryClaim();
            if (result == KitClaimResult.Ok)
                publishChanged();
            return result;
        }

        public void AcknowledgeNotice() => profiles.AcknowledgeNotice();

        // W2/W3: 장비 Capture → profile.Loadout → Save. 떠나는 중이면 아무것도 하지 않는다(false).
        public bool SaveNow()
        {
            if (IsLeaving || Profile == null)
                return false;
            CaptureLoadout();
            return profiles.Save();
        }

        // W4: 현재 은신처 장비 = 반입. Capture → profile.Loadout → flow.StartRaid(captured)(BeginRaid가 기록).
        // 실패(단계·저장·로드 요청)면 IsLeaving을 풀고 false. 빈 장비도 허용 (Q26), 확인 창 없음 (Q30).
        public bool StartRaid()
        {
            if (IsLeaving || Profile == null)
                return false;

            Loadout captured = CaptureLoadout();
            // 이번 시도의 실패 이유만 남게 (W1 저장 실패 표시가 단계 거부를 "저장 실패"로 보이게 하지 않게 — M5 리뷰 7)
            profiles.ClearSaveFailure();
            SetLeaving(true);
            if (flow.StartRaid(captured))
                return true;
            SetLeaving(false);
            return false;
        }

        // W2: SaveNow → flow.LeaveHideout(). 저장이 실패하면 이동하지 않고 false(LastSaveFailed — UI가 메시지) (M5 리뷰 6).
        // 그다음 호출은 다시 저장해 보고, 또 실패해도 이동한다(TODO(GDD-?): "두 번 누르면 그래도 이동"은 문서에 없는 가장 단순한 방식).
        // 읽기 전용(진입 때 알림)이면 저장 실패와 상관없이 바로 이동.
        // 로드 요청이 거부되면 IsLeaving을 풀고 false.
        public bool ReturnToMainMenu()
        {
            if (IsLeaving || Profile == null)
                return false;

            bool saved = SaveNow();
            if (!saved && !profiles.IsReadOnly && !leaveDespiteSaveFailure)
            {
                leaveDespiteSaveFailure = true;
                return false;
            }
            leaveDespiteSaveFailure = false;
            SetLeaving(true);
            if (flow.LeaveHideout())
            {
                profiles.ClearHideoutEntry();
                return true;
            }
            SetLeaving(false);
            return false;
        }

        // ── 디버그 (HideoutController ContextMenu) ──────────

        public void DebugAddCasings(int amount)
        {
            if (Profile == null)
                return;
            long total = (long)Profile.Casings + amount;
            Profile.Casings = total >= int.MaxValue ? int.MaxValue : (int)Math.Max(0, total);
            publishChanged();
        }

        public void DebugResetKitCooldown()
        {
            if (Profile == null)
                return;
            Profile.LastKitClaimUtc = null;
            publishChanged();
        }

        // ── 내부 ────────────────────────────────────────────

        // 은신처 인벤토리가 있으면 그 장비를 기록해 profile.Loadout에 둔다.
        // 인벤토리가 없으면(씬 구성 오류·테스트) profile.Loadout을 그대로 쓴다 — 장비가 놓이지도 않았으니 비우면 잃는다.
        // TODO(GDD-?): 은신처 인벤토리 없이 은신처 장비를 지우지 않는 쪽을 택함(가장 안전·단순).
        private Loadout CaptureLoadout()
        {
            IInventoryService inventory = loadoutSource();
            if (inventory != null)
                Profile.Loadout = Loadout.Capture(inventory);
            return Profile.Loadout;
        }

        private void SetLeaving(bool leaving)
        {
            if (IsLeaving == leaving)
                return;
            IsLeaving = leaving;
            publishChanged();
        }

        private TraderModel Trader
        {
            get
            {
                EnsureModels();
                return traderModel;
            }
        }

        private VagrantKitModel Kit
        {
            get
            {
                EnsureModels();
                return kitModel;
            }
        }

        private void EnsureModels()
        {
            PlayerProfile profile = Profile;
            if (ReferenceEquals(profile, modelProfile))
                return;
            modelProfile = profile;
            traderModel = profile != null ? new TraderModel(profile, trader) : null;
            kitModel = profile != null ? new VagrantKitModel(profile, config, utcNow) : null;
        }
    }
}
