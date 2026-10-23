using System;
using Ashfall.Core;
using UnityEngine;

namespace Ashfall.Hideout
{
    // 은신처 씬 진입점 (SYS-HUB, M5 설계 §4.2). 얇은 브리지 — 로직은 HideoutService.
    // 다른 은신처 오브젝트(HideoutInventoryBehaviour, HideoutRoot)보다 먼저 Awake해 프로필 로드·진입 저장(W1)까지 끝낸다.
    // UI는 Start/Update에서 IHideoutService를 레지스트리에서 찾는다.
    [DefaultExecutionOrder(-100)]
    public sealed class HideoutController : MonoBehaviour
    {
        // 디버그 메뉴 "탄피 +1000"의 양 (디버그 전용, 게임 수치 아님)
        private const int DebugCasingsAmount = 1000;

        [SerializeField] private HideoutConfigSO config;
        [SerializeField] private TraderConfigSO trader;
        [Tooltip("세이브 id → 아이템 정의 (Data/Items/ItemDatabase.asset)")]
        [SerializeField] private ItemDatabaseSO itemDatabase;
        [Tooltip("M7 제작대 레시피 (Data/Hideout/CraftingConfig.asset). 비어 있으면 [제작] 탭이 \"제작법 없음\" — 은신처는 그대로 시작한다")]
        [SerializeField] private CraftingConfigSO crafting;

        private bool registered;

        public HideoutConfigSO Config => config;
        public TraderConfigSO Trader => trader;
        public ItemDatabaseSO ItemDatabase => itemDatabase;
        public CraftingConfigSO Crafting => crafting;

        // 테스트·디버그. Awake가 실패(참조 누락·GameManager 없음)하면 null.
        public HideoutService Service { get; private set; }
        // 진입 시 EnsureLoaded 보고서 (W1)
        public SaveLoadReport EntryReport { get; private set; }

        private void Awake()
        {
            if (config == null || trader == null || itemDatabase == null)
            {
                Debug.LogError($"[HideoutController] 참조 누락 — config {(config != null)}, trader {(trader != null)}, " +
                               $"itemDatabase {(itemDatabase != null)}. 은신처를 시작하지 않는다.", this);
                enabled = false;
                return;
            }
            GameManager gm = GameManager.Instance;
            if (gm == null || gm.Profiles == null || gm.Flow == null)
            {
                Debug.LogError("[HideoutController] GameManager가 없다. 은신처를 시작하지 않는다.", this);
                enabled = false;
                return;
            }

            // 제작대는 선택 참조 — 없어도 은신처는 시작한다 (레시피 없음)
            if (crafting == null)
                Debug.LogWarning("[HideoutController] CraftingConfigSO가 연결되지 않았다 — [제작] 탭에 제작법이 없다.", this);

            // W1: 첫 로드·RULE-03 감지 → 흐름 레이드 결과 반영 → 저장 → 진입 완료 표시(이 씬의 handle).
            // 예외가 나면 서비스 없이 멈추지 않게 Start에서 메인 메뉴로 돌아간다 (M5 리뷰 2).
            if (HideoutService.TryEnter(gm.Profiles, gm.Flow, itemDatabase, config.ToProfileDefaults(), gameObject.scene.handle,
                    () => new HideoutService(gm.Profiles, gm.Flow, config, trader, FindInventory, () => DateTime.UtcNow, PublishChanged,
                        crafting),
                    out SaveLoadReport report, out HideoutService service))
            {
                EntryReport = report;
                Service = service;
            }
            else
            {
                EntryFailed = true;
            }
        }

        // 진입 절차가 예외로 실패했다 (테스트·디버그). Start에서 메인 메뉴로 돌아간다.
        public bool EntryFailed { get; private set; }

        // sceneLoaded(→ Phase Hideout)는 Awake·OnEnable 뒤, Start 앞이다. 그래서 여기서 LeaveHideout이 받아들여진다.
        private void Start()
        {
            if (!EntryFailed)
                return;
            GameManager gm = GameManager.Instance;
            if (gm == null || gm.Flow == null || !gm.Flow.LeaveHideout())
                Debug.LogError("[HideoutController] 진입 실패 뒤 메인 메뉴로 돌아가지 못했다.", this);
        }

        private void OnEnable()
        {
            if (Service == null || registered)
                return;
            GameManager gm = GameManager.Instance;
            if (gm == null)
                return;
            gm.Services.Register<IHideoutService>(Service);
            registered = true;
        }

        private void OnDisable()
        {
            if (!registered)
                return;
            registered = false;
            GameManager gm = GameManager.Instance;
            if (gm != null)
                gm.Services.Unregister<IHideoutService>(Service);
        }

        // W3: 은신처에 있는 동안 앱 종료 → 장비 Capture → 저장 (떠나는 중이면 하지 않는다 — 레이드 시작 기록을 덮지 않게)
        private void OnApplicationQuit()
        {
            GameManager gm = GameManager.Instance;
            if (Service == null || gm == null || gm.Flow == null)
                return;
            if (gm.Flow.Phase == GamePhase.Hideout && !Service.IsLeaving)
                Service.SaveNow();
        }

        private static IInventoryService FindInventory()
        {
            GameManager gm = GameManager.Instance;
            return gm != null && gm.Services.TryGet(out IInventoryService inventory) ? inventory : null;
        }

        private static void PublishChanged()
        {
            GameManager gm = GameManager.Instance;
            if (gm != null)
                gm.Events.Publish(new HideoutChanged());
        }

        [ContextMenu("Debug/탄피 +1000")]
        private void DebugAddCasings() => Service?.DebugAddCasings(DebugCasingsAmount);

        [ContextMenu("Debug/키트 쿨다운 초기화")]
        private void DebugResetKitCooldown() => Service?.DebugResetKitCooldown();

        [ContextMenu("Debug/지금 저장")]
        private void DebugSaveNow()
        {
            if (Service == null)
                return;
            bool ok = Service.SaveNow();
            Debug.Log($"[HideoutController] 지금 저장: {(ok ? "성공" : "실패 또는 떠나는 중")}", this);
        }
    }
}
