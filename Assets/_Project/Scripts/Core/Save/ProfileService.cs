using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ashfall.Core
{
    // 프로필 로드·저장·RULE-03·레이드 시작/반영 (M5 설계 §1.3~§1.5, §2). 순수 C#, GameManager.Profiles가 하나를 가진다.
    // - 파일은 첫 EnsureLoaded(= 그 실행의 첫 은신처 진입, Q5)에서만 읽는다. 생성자·UseStorage는 읽지 않는다.
    // - 쓰기는 Save()·BeginRaid·CancelRaid만. 레이드 중에는 아무것도 쓰지 않는다(RULE-03 — 호출자가 지킨다).
    // - RULE-01 반영 지점은 ApplyRaidResult 하나.
    // - 읽기 오류(재시도 뒤에도)면 파일을 건드리지 않고 그 실행은 읽기 전용(IsReadOnly) — 모든 저장이 거부된다(M5 리뷰 1).
    public sealed class ProfileService
    {
        // TODO(GDD-?) Q22: 읽기 오류(일시 잠금·공유 위반) 재시도. 기술 상수(게임 수치 아님) — 첫 은신처 진입에서 최대 약 0.2초 멈춘다.
        public const int ReadAttempts = 3;
        public const int ReadRetryDelayMs = 100;
        public const string ReadOnlySaveError = "읽기 전용 — 세이브를 읽지 못한 실행에서는 저장하지 않는다";

        private readonly Func<DateTime> utcNow;
        private readonly Action<int> wait;
        private ISaveStorage storage;
        private SaveLoadReport loadReport;
        // BeginRaid가 맡긴 반입 (같은 실행에서 결과 없이 돌아왔을 때 알림 목록용)
        private Loadout raidLoadout;

        // wait = 읽기 재시도 사이 대기(ms). null → Thread.Sleep. 테스트는 기록만 하는 대리자를 넘긴다.
        public ProfileService(ISaveStorage storage, Func<DateTime> utcNow, Action<int> wait = null)
        {
            this.storage = storage ?? throw new ArgumentNullException(nameof(storage));
            this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
            this.wait = wait ?? (ms => System.Threading.Thread.Sleep(ms));
        }

        public bool IsLoaded { get; private set; }
        public PlayerProfile Profile { get; private set; }        // IsLoaded가 아니면 null
        public SaveLoadReport PendingNotice { get; private set; } // 확인 전 알림. 없으면 null
        public bool LastSaveFailed { get; private set; }
        public string LastSaveError { get; private set; }
        public string StorageLocation => storage.Location;
        // 세이브를 읽지 못한 실행 = 저장 막음 (Save·BeginRaid 실패). UseStorage가 풀어 준다.
        public bool IsReadOnly { get; private set; }

        // 은신처 진입 완료 표시 (M5 리뷰 5). HideoutService.Enter가 끝에 남기고, 은신처 인벤토리가 같은 토큰인지 확인한다.
        // 토큰 = 은신처 씬 인스턴스 식별값(Scene.handle). 0 = 진입 안 됨. 레이드 시작·메뉴 복귀·UseStorage에서 지운다.
        public int HideoutEntryToken { get; private set; }
        public bool IsHideoutEntered(int token) => token != 0 && HideoutEntryToken == token;
        public void MarkHideoutEntered(int token) => HideoutEntryToken = token;
        public void ClearHideoutEntry() => HideoutEntryToken = 0;

        // 테스트·도구: 저장 위치를 바꾸고 로드한 프로필을 버린다(다음 EnsureLoaded가 새 위치에서 읽음 = "다음 실행" 흉내).
        public void UseStorage(ISaveStorage newStorage)
        {
            storage = newStorage ?? throw new ArgumentNullException(nameof(newStorage));
            IsLoaded = false;
            Profile = null;
            PendingNotice = null;
            loadReport = null;
            raidLoadout = null;
            LastSaveFailed = false;
            LastSaveError = null;
            IsReadOnly = false;
            HideoutEntryToken = 0;
        }

        // 저장 실패 표시를 지운다 — 다음 시도의 실패 이유가 이전 실패(W1 등)와 섞이지 않게 (M5 리뷰 7). 읽기 전용은 그대로.
        public void ClearSaveFailure()
        {
            LastSaveFailed = false;
            LastSaveError = null;
        }

        // 처음 한 번만 읽는다. 이후 호출은 읽지 않고 첫 로드 보고서를 그대로 돌려준다(알림 표시는 PendingNotice로 판단).
        // 파일 없음 → 새 프로필 / 손상·버전·해석 예외 → 격리 + 새 프로필 / 알 수 없는 id·자리(§2.4) / RULE-03 감지(§1.4).
        // 읽기 오류(재시도 ReadAttempts회 뒤에도) → 격리하지 않고 새 프로필을 메모리에만 + 읽기 전용(저장 막음, M5 리뷰 1).
        public SaveLoadReport EnsureLoaded(IItemCatalog catalog, ProfileDefaults defaults)
        {
            if (IsLoaded)
                return loadReport;
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));

            var report = new SaveLoadReport();
            PlayerProfile profile = null;
            SaveReadStatus read = ReadWithRetry(out string text, out string readError);
            switch (read)
            {
                case SaveReadStatus.Missing:
                    report.Status = SaveLoadStatus.NewProfile;
                    break;

                case SaveReadStatus.Ok:
                    if (SaveSerializer.TryParse(text, out SaveData data, out SaveLoadStatus failStatus, out string parseError))
                    {
                        report.Status = SaveLoadStatus.Loaded;
                        try
                        {
                            profile = SaveSerializer.ToProfile(data, catalog, defaults, report);
                        }
                        catch (Exception e)
                        {
                            // 예상 밖 세이브 내용으로 해석 중 예외 → 손상과 같게 (M5 리뷰 2). 일부 채운 보고서는 버린다.
                            profile = null;
                            report = new SaveLoadReport
                            {
                                Status = SaveLoadStatus.Corrupt,
                                Error = $"해석 실패 {e.GetType().Name}: {e.Message}",
                            };
                            Quarantine(report);
                        }
                    }
                    else
                    {
                        report.Status = failStatus;
                        report.Error = parseError;
                        Quarantine(report);
                    }
                    break;

                default:
                    // 일시 잠금이 풀리지 않았다. 격리·덮어쓰기를 하면 진행도를 잃을 수 있어 파일은 그대로 두고 이 실행의 저장을 막는다.
                    // 다음 실행(게임 재시작)이 다시 읽는다. 레이드 시작은 W4 저장 실패로 거부된다.
                    report.Status = SaveLoadStatus.ReadError;
                    report.Error = readError;
                    IsReadOnly = true;
                    Debug.LogError($"[ProfileService] 세이브를 읽지 못했다 ({storage.Location}): {readError}. " +
                                   "이번 실행은 읽기 전용 — 저장하지 않는다.");
                    break;
            }

            if (profile == null)
                profile = new PlayerProfile(defaults.StashSize, defaults.StartingCasings);

            // RULE-03: 지난 실행이 레이드 도중 끝났다 → 파일의 은신처 장비(= 반입) 삭제. 보관함·탄피·키트 시각은 그대로.
            if (profile.RaidInProgress)
            {
                report.CrashPenaltyApplied = true;
                AddDefinitions(profile.Loadout, report.CrashLost);
                profile.Loadout = Loadout.Empty;
                profile.RaidInProgress = false;
                Debug.LogWarning($"[ProfileService] RULE-03: 지난 레이드 도중 종료 — 반입 장비 {report.CrashLost.Count}개 삭제.");
            }
            if (report.UnknownItemIds.Count > 0 || report.DroppedItemCount > 0)
                Debug.LogWarning($"[ProfileService] 불러오지 못한 아이템: 알 수 없는 id {report.UnknownItemIds.Count}개, 자리 없음 {report.DroppedItemCount}개.");

            Profile = profile;
            IsLoaded = true;
            loadReport = report;
            PendingNotice = report.HasNotice ? report : null;
            return report;
        }

        // 현재 프로필 전체를 쓴다(원자적). 실패면 LastSaveFailed + LogError.
        public bool Save()
        {
            if (!IsLoaded)
            {
                Debug.LogError("[ProfileService] 저장 거부 — 프로필을 아직 읽지 않았다.");
                LastSaveFailed = true;
                LastSaveError = "프로필 없음";
                return false;
            }
            if (IsReadOnly)
            {
                // 진입 때 이미 오류를 남기고 알렸다 — 시도마다는 경고만
                Debug.LogWarning($"[ProfileService] 저장하지 않는다 — {ReadOnlySaveError}.");
                LastSaveFailed = true;
                LastSaveError = ReadOnlySaveError;
                return false;
            }

            string error;
            bool ok;
            try
            {
                ok = storage.TryWrite(SaveSerializer.ToJson(Profile, utcNow()), out error);
            }
            catch (Exception e)
            {
                ok = false;
                error = $"{e.GetType().Name}: {e.Message}";
            }

            LastSaveFailed = !ok;
            LastSaveError = ok ? null : error;
            if (!ok)
                Debug.LogError($"[ProfileService] 세이브 실패 ({storage.Location}): {error}");
            return ok;
        }

        // GameFlow.StartRaid만 부른다(W4). 반입을 은신처 장비에 둔 채 RaidInProgress=true로 저장(Q4) →
        // 성공하면 메모리 장비를 비운다. 저장 실패면 플래그·장비를 되돌리고 false(레이드를 시작하지 않는다).
        public bool BeginRaid(Loadout loadout)
        {
            if (!IsLoaded)
            {
                Debug.LogError("[ProfileService] BeginRaid 거부 — 프로필을 아직 읽지 않았다.");
                return false;
            }
            if (Profile.RaidInProgress)
            {
                Debug.LogWarning("[ProfileService] BeginRaid 무시 — 이미 레이드 중.");
                return false;
            }

            Loadout previous = Profile.Loadout;
            Loadout brought = loadout ?? Loadout.Empty;
            Profile.Loadout = brought;
            Profile.RaidInProgress = true;
            if (!Save())
            {
                Profile.Loadout = previous;
                Profile.RaidInProgress = false;
                return false;
            }
            raidLoadout = brought;
            Profile.Loadout = Loadout.Empty;
            HideoutEntryToken = 0;   // 은신처를 떠난다 — 다음 은신처 씬은 새 진입 절차를 거쳐야 한다
            return true;
        }

        // 레이드 씬 로드 요청 실패(W4 직후): 장비를 되돌리고(null → BeginRaid가 맡은 것) 플래그를 내린 뒤 저장.
        public void CancelRaid(Loadout loadout)
        {
            if (!IsLoaded)
                return;
            Profile.Loadout = loadout ?? raidLoadout ?? Loadout.Empty;
            Profile.RaidInProgress = false;
            raidLoadout = null;
            Save();
        }

        // RULE-01 반영 지점: Survived && CarriedOutLoadout != null → 은신처 장비 = 그것, 아니면 Empty.
        // RaidInProgress = false. 보관함은 바뀌지 않는다. 저장은 호출자(W1).
        public void ApplyRaidResult(RaidResult result)
        {
            if (!IsLoaded)
            {
                Debug.LogError("[ProfileService] ApplyRaidResult 무시 — 프로필을 아직 읽지 않았다.");
                return;
            }
            if (result == null)
            {
                Debug.LogWarning("[ProfileService] ApplyRaidResult — 결과가 null, 결과 없는 귀환으로 처리.");
                ApplyInterruptedRaid();
                return;
            }
            Profile.Loadout = result.Survived && result.CarriedOutLoadout != null ? result.CarriedOutLoadout : Loadout.Empty;
            ClearJams(Profile.Loadout);
            Profile.RaidInProgress = false;
            raidLoadout = null;
        }

        // M7 Q14 A: 고장 상태는 저장하지 않고 은신처 귀환 = 해제. 같은 실행에서 다음 레이드로 고장이 넘어가지 않게
        // 은신처 장비가 되는 반출 장비의 무기를 전부 푼다(세이브를 다시 읽은 경우와 같게 — M7 리뷰 7A 중간 2). 내구도는 그대로.
        private static void ClearJams(Loadout loadout)
        {
            IReadOnlyList<LoadoutEntry> entries = loadout?.Entries;
            if (entries == null)
                return;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Item is WeaponItem weapon)
                    weapon.IsJammed = false;
            }
        }

        // 같은 실행에서 결과 없이 은신처로 돌아옴(디버그·테스트) = 사망과 같게 + RULE-03 알림 1회 (§1.4).
        // 메모리에 RaidInProgress=true가 남아 파일로 새지 않게 한다.
        public void ApplyInterruptedRaid()
        {
            if (!IsLoaded)
            {
                Debug.LogError("[ProfileService] ApplyInterruptedRaid 무시 — 프로필을 아직 읽지 않았다.");
                return;
            }
            // 새 알림은 Loaded — 첫 로드의 Corrupt·버전 문구가 이미 확인한 뒤 다시 붙지 않게 (M5 리뷰 3).
            // 아직 확인하지 않은 알림이 있으면 거기에 합친다(그 문구는 원래 보일 것).
            SaveLoadReport notice = PendingNotice ?? new SaveLoadReport { Status = SaveLoadStatus.Loaded };
            notice.CrashPenaltyApplied = true;
            AddDefinitions(raidLoadout, notice.CrashLost);
            PendingNotice = notice;

            Profile.Loadout = Loadout.Empty;
            Profile.RaidInProgress = false;
            raidLoadout = null;
        }

        public void AcknowledgeNotice() => PendingNotice = null;

        // Error면 ReadRetryDelayMs 쉬고 다시(최대 ReadAttempts회). Missing·Ok는 바로 돌려준다.
        private SaveReadStatus ReadWithRetry(out string text, out string error)
        {
            SaveReadStatus status = SaveReadStatus.Error;
            text = null;
            error = null;
            for (int attempt = 1; attempt <= ReadAttempts; attempt++)
            {
                status = storage.TryRead(out text, out error);
                if (status != SaveReadStatus.Error)
                    return status;
                if (attempt < ReadAttempts)
                {
                    Debug.LogWarning($"[ProfileService] 세이브 읽기 오류 ({attempt}/{ReadAttempts}), 다시 시도: {error}");
                    wait(ReadRetryDelayMs);
                }
            }
            return status;
        }

        private void Quarantine(SaveLoadReport report)
        {
            if (storage.TryQuarantine(out string movedTo, out string error))
            {
                report.QuarantinedPath = movedTo;
                Debug.LogWarning($"[ProfileService] 세이브를 읽을 수 없어 새로 시작한다 ({report.Status}: {report.Error}). 원본 → {movedTo}");
            }
            else
            {
                Debug.LogError($"[ProfileService] 세이브를 읽을 수 없고 격리도 실패했다 ({report.Status}: {report.Error}; {error}).");
                report.Error = string.IsNullOrEmpty(report.Error) ? error : report.Error + "; " + error;
            }
        }

        private static void AddDefinitions(Loadout loadout, List<ItemDefinitionSO> into)
        {
            if (loadout == null)
                return;
            IReadOnlyList<LoadoutEntry> entries = loadout.Entries;
            for (int i = 0; i < entries.Count; i++)
                into.Add(entries[i].Item.Definition);
        }
    }
}
