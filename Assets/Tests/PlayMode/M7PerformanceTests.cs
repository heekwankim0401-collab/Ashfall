#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Ashfall.AI;
using Ashfall.Audio;
using Ashfall.Core;
using Ashfall.Player;
using Ashfall.Raid;
using Ashfall.Weapons;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.Profiling;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace Ashfall.Tests.PlayMode
{
    // M7 7D-3 최악 상황 성능 (M7 설계 §1.8·§6 "7D-3 검증"·§7 AC3, Q50 A — 예산은 기존 그대로):
    // 실제 Raid_CrimsonBasin(34명, AudioDirector·AshStorm·RadiationField 포함)에서
    //  - 모든 적 무적(복제 아키타입 HP 1e9 — RaidPerformanceTests·M6PerformanceTests와 같은 방식), 측정 직전 탄 보충(RaidPerformanceTests.RefillAmmo)
    //  - 무적 표적(PerfTarget = IPlayerTarget + IDamageable, 근접·총탄 피격을 따로 셈) = AudioDirector 청취자
    //  - 재 폭풍 Peak 강제(시야 30m)
    //  - 표적 = 보스 거점(AI-BOS 첫 Tick 위치, 반경 25m) 중심 3~9m(Peak 시야 30m 안에서 보스가 거점 어디로 가도 보이게), 0.8m 띄움(아래 TargetRaiseM), 보스 눈에서 시선이 뚫린 자리 → 보스(LMG)·호위 3명은 거점에서 그대로 교전
    //    (보스 거점은 테스트가 바꿀 수 없으므로 표적을 거점 근처에 둔다)
    //  - 변이체 8명(두 무리)을 표적 2~6m 둘레로 옮김 → 둘레 자리에서 근접 공격 반복
    //  - 저격수 2명을 표적 18~27m(Peak 시야 30m 안), 시선이 뚫린 자리로 옮기고 표적을 보게 함 → 경고(섬광) → 사격
    //  - M4 고정 배치 20명(SCV·RDR)을 표적 12~24m 고리로 옮김(M6PerformanceTests와 같은 고리)
    //  - 플레이어(SP-W 스폰, 표적에서 약 330m)는 펌프 샷건(WPN-SHT-01, 펠릿 8)을 Primary2에 들고 하늘로 연사(가상 마우스 LMB, 튜브는 테스트가 채움)
    // 측정 A(10초): AI 틱 평균·99%, 투사체 스텝 평균, 프레임 중앙값(렌더 제외), AudioDirector.Update + AshStormController.Update + RadiationField 질의
    //   (엔진 호출과 별도로 같은 메서드를 한 번 더 직접 불러 Stopwatch — M6과 같은 보수적 측정), 측정 중 실제 사격·근접 타격·LMG/DMR ShotFired·섬광, 동시 탄 최대 ≤ 이론 상한.
    // 측정 B(GC, 300프레임 이상·최소 5초): AiDirector·WeaponController는 끄고 같은 Update를 직접 불러 그 호출만 GC.Alloc Recorder로 잰다(AiPerformanceTests와 같은 방식,
    //   AI 경로 = 감각·전술·근접 타격·적 사격·ShotFired/EnemyVocalized 구독자(Audio)까지, 무기 경로 = 플레이어 샷건 발사·펠릿 8·NoiseEmitted → AI 청각·ShotFired → Audio).
    //   ProjectileSystem.FixedUpdate·PlayerHealth.Update(생존·방사능)·AudioDirector·AshStormController는 끄면 서비스 등록이 풀리므로 추가 호출로 잰다.
    //   GC 구간 전에 씬 콜라이더의 관리 래퍼를 미리 만든다(WarmColliderWrappers — 탄·시야 레이가 처음 맞힌 콜라이더에서 생기는 엔진 1회 할당 제외).
    //   7D-3 PlayMode 전체 2회 모두 투사체 경로에서 2400여 프레임 중 1프레임 1블록이 잡혔다(래퍼 예열 전). 할당 프레임은 경로·프레임·동시 탄 수를 보고에 남긴다.
    //   예열을 두는 근거(7D-3 발견 2, 7D-4 유지 결정): 예열 전 6회 중 3회 1블록(1회는 AI 경로 포함), 예열 뒤 6회 연속 0 — 콜라이더마다 게임 전체에서 한 번뿐인
    //   엔진 래퍼 생성이지 프레임마다 반복되는 할당이 아니다. 예산 "GC 0"은 정상 상태 프레임 기준이라 이 1회분은 판정에서 뺀다.
    //   (이 Mono에서 GC.GetAllocatedBytesForCurrentThread는 실제 할당에도 0을 돌려줘 바이트 단위 판정은 쓸 수 없었다.)
    //   + 측정 A에서 기록한 실제 ShotFired·FootstepTaken·EnemyVocalized를 같은 버스로 다시 발행(오디오 이벤트 경로, M6과 같음).
    // 한계: 배치모드는 화면을 그리지 않는다(GPU·파티클·포스트 없음) — 1080p 60fps는 사람이 개발 빌드로(§7.1 P1).
    public class M7PerformanceTests
    {
        private const int ExpectedEnemies = 34;
        private const int ExpectedMutants = 8;
        private const int ExpectedSnipers = 2;
        private const string LmgId = "WPN-LMG-01";
        private const string DmrId = "WPN-DMR-01";
        private const string ShotgunPath = "Assets/_Project/Data/Weapons/WPN-SHT-01.asset";

        private const float StressNoiseRadiusM = 300f;
        private const float NoiseIntervalSec = 2f;
        private const float EngageTimeoutSec = 90f;
        private const float WarmupSec = 3f;
        private const float MeasureSec = 10f;
        private const int GcFrames = 300;
        private const float GcMinSec = 5f;   // GC 구간 최소 실시간 (샷건 70rpm → 5발 이상)
        private static readonly string[] PathNames = { "AI", "무기", "투사체", "생존", "Audio", "폭풍", "방사능" };
        // 이벤트 종류마다 기록 상한. 7D-3에서 512면 측정 구간 실제 적 ShotFired(510~535)가 잘려 1024로 올렸다(7D-3 발견 3, 7D-4).
        // 재발행 GC 판정에는 영향 없음(상한은 재발행 개수만 정한다).
        private const int MaxReplayEvents = 1024;
        private const float PlayerAimUpDeg = 35f;
        // 표적 높이. 7D-3은 0.8m(상자 위)로 띄웠다 — 지면 표적이면 변이체 8명이 1.35m 둘레를 감싸 사수 시선을 막고, SCV·RDR·호위가 LKP(= 표적 발)까지
        // 걸어 들어와 겹쳐 교전이 멈췄다(7D-3 발견 1). 7D-4에서 약탈자·습격자가 LKP·표적에서 AiConfigSO.approachStopDistanceM(2m) 앞에 멈추게 고쳤지만
        // 지면(0)으로 되돌리면 3회 중 1회 "90초 안에 보스 교전" 전제가 실패했다: 테스트의 300m 소음으로 사수 20여 명이 한꺼번에 LKP 둘레 2m 고리(둘레 약 12.5m)로 모여
        // 변이체 8명과 함께 고리가 꽉 차고(NavMeshAgent 회피로 일부는 0.6~1.1m까지 밀림), 겹친 몸이 보스(거점 2.7m)의 시선을 막는다. 정지 거리로 "표적 발까지 걸어 들어감"은
        // 없어졌지만 이 인위적 군집의 시야 가림은 남는다(적끼리 시야 가림 = 탄도와 같은 규칙, 7B 유지) → 표적은 0.8m 띄운 채로 둔다(근접 높이 차 1.2m 안). 아래 ShooterMinGapM 단언은 유지.
        private const float TargetRaiseM = 0.8f;
        // 사수(SCV·RDR·호위·보스)와 표적의 최소 수평 거리. 정지 점 = LKP에서 2m, 도착 판정 pointArriveDistanceM 1m → 1m 이상이어야 겹침 없음.
        private const float ShooterMinGapM = 1.0f;

        // 교전 진입 판정(측정 시작 조건) — 예산이 아니라 "최악 상황이 실제로 만들어졌다"는 전제
        private const int MinMutantsEngaged = 6;
        private const int MinM4Engaged = 10;

        private const double AiAverageBudgetMs = 1.0;
        private const double AiP99BudgetMs = 3.0;
        private const double ProjectileAverageBudgetMs = 1.0;
        private const double FrameMedianBudgetMs = 16.6;
        private const double AudioStormRadiationBudgetMs = 0.3;

        private M4Raid raid;
        private SimInput input;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            raid = new M4Raid();
            yield return M4Raid.Load(raid, "SP-W");
            M4Raid r = raid;
            yield return M3Scene.WaitUntil(() => r.Director.Projectiles != null && r.Enemies.All(e => e.Brain != null),
                10f, () => "적 Brain 준비");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            input?.Dispose();
            input = null;
            if (raid?.Director != null)
                raid.Director.enabled = true;
            if (raid?.Gm != null && raid.Gm.Flow.IsWorldPaused)
                raid.Gm.Flow.SetWorldPaused(false);
            Time.timeScale = 1f;
            yield return null;
        }

        [UnityTest]
        [Timeout(480000)]
        public IEnumerator RaidScene_WorstCase_StormPeak_MutantMelee_BossLmg_Sniper_PlayerShotgun_BudgetsHold_NoGc()
        {
            Assert.AreEqual(ExpectedEnemies, raid.Enemies.Count, $"적 {ExpectedEnemies}명");
            AiDirector director = raid.Director;
            AudioDirector audio = Object.FindFirstObjectByType<AudioDirector>();
            AshStormController storm = Object.FindFirstObjectByType<AshStormController>();
            RadiationField radiation = Object.FindFirstObjectByType<RadiationField>();
            WeaponController weapon = raid.Motor.GetComponentInChildren<WeaponController>(true);
            Assert.IsNotNull(audio, "AudioDirector 없음");
            Assert.IsNotNull(storm, "AshStormController 없음");
            Assert.IsNotNull(radiation, "RadiationField 없음 (Ashfall/Setup/Apply M7 Content)");
            Assert.IsNotNull(weapon, "플레이어 WeaponController");
            Assert.IsTrue(audio.isActiveAndEnabled && audio.IsInitialized, "오디오 켬");
            Terrain terrain = Object.FindFirstObjectByType<Terrain>();

            EnemyAgent boss = raid.Enemies.Single(e => e.Archetype.id == "AI-BOS");
            List<EnemyAgent> guards = raid.Enemies.Where(e => e.Archetype.id == "AI-BGD").ToList();
            List<EnemyAgent> snipers = raid.Enemies.Where(e => e.Archetype.id == "AI-SNP").ToList();
            List<EnemyAgent> mutants = raid.Enemies.Where(e => e.Archetype.id == "AI-MUT").ToList();
            List<EnemyAgent> m4 = raid.Enemies.Where(e => !RaidPerformanceTests.IsM7(e)).ToList();
            Assert.AreEqual(ExpectedSnipers, snipers.Count, "저격수 2");
            Assert.AreEqual(ExpectedMutants, mutants.Count, "변이체 8");
            Assert.AreEqual(3, guards.Count, "호위 3");
            Assert.AreEqual(RaidPerformanceTests.ExpectedM4Enemies, m4.Count, "M4 고정 배치 20");
            Assert.AreEqual(LmgId, boss.Weapon.Item.Definition.id, "보스 = LMG");
            List<SniperGlint> glints = snipers.Select(s => s.GetComponentInChildren<SniperGlint>(true)).ToList();
            Assert.IsTrue(glints.All(g => g != null), "저격수 섬광");
            yield return M3Scene.WaitUntil(() => boss.Brain.HasAnchor, 5f, () => "보스 거점(첫 Tick)");
            Vector3 anchor = boss.Brain.AnchorCenter;

            Action audioUpdate = UpdateDelegate(audio, "Update");
            Action stormUpdate = UpdateDelegate(storm, "Update");
            Action aiUpdate = UpdateDelegate(director, "Update");
            Action weaponUpdate = UpdateDelegate(weapon, "Update");
            Action projectileStep = UpdateDelegate(raid.Projectiles, "FixedUpdate");
            Action survivalUpdate = UpdateDelegate(raid.Health, "Update");
            MethodInfo resubscribe = typeof(AiDirector).GetMethod("TrySubscribe", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(resubscribe, "AiDirector.TrySubscribe");

            Vector3 center = PickTargetNearAnchor(boss, guards, anchor);
            PerfTarget target = PerfTarget.Create(center + Vector3.up * TargetRaiseM);
            var clones = new List<EnemyArchetypeSO>();
            var capture = new EventCapture(raid.Events, MaxReplayEvents);
            var counters = new Counters(raid.Events);
            try
            {
                foreach (EnemyAgent e in raid.Enemies)
                    MakeImmortal(e, clones);
                raid.Gm.Services.Unregister<IPlayerTarget>(raid.Motor);
                target.Register();
                audio.ResolveServices();   // 청취자 = 표적
                yield return M2Scene.WaitUntil(() => ReferenceEquals(director.Target, target), 3f);

                // ── 재 폭풍 Peak ──
                Assert.IsTrue(storm.ForceStartNow(), "폭풍 강제 시작");
                yield return M3Scene.WaitUntil(() => storm.Phase == AshStormPhase.Rising, 3f, () => "Rising");
                raid.Controller.AdvanceTime(storm.Config.riseSec * 1.25f);
                yield return M3Scene.WaitUntil(() => storm.Phase == AshStormPhase.Peak, 3f, () => "Peak");
                yield return M3Scene.WaitUntil(() => Mathf.Approximately(director.VisibilityCapM, 30f), 3f, () => "AI 시야 상한 30m");

                // ── 배치: M4 고리 12~24m, 저격수 18~27m(시선 뚫림), 변이체 2~6m ──
                var used = new List<Vector3> { center };
                List<Vector3> ring = RingSpots(terrain, center, m4.Count, 12f, 3f, 2.5f);
                for (int i = 0; i < m4.Count; i++)
                    WarpFacing(m4[i], ring[i % ring.Count], center);
                used.AddRange(ring);
                List<Vector3> sniperSpots = SniperSpots(target, center, snipers.Count, used);
                for (int i = 0; i < snipers.Count; i++)
                    WarpFacing(snipers[i], sniperSpots[i], center);
                used.AddRange(sniperSpots);
                List<Vector3> mutantSpots = MutantSpots(center, mutants.Count);
                for (int i = 0; i < mutants.Count; i++)
                    WarpFacing(mutants[i], mutantSpots[i], center);
                string placement =
                    $"표적 {Flat(center - anchor):0.0}m from 보스 거점, 보스 {Flat(boss.transform.position - center):0.0}m, " +
                    $"저격수 {string.Join("/", sniperSpots.Select(s => Flat(s - center).ToString("0.0")))}m, " +
                    $"변이체 {string.Join("/", mutantSpots.Select(s => Flat(s - center).ToString("0.0")))}m, M4 고리 {ring.Count}자리";

                // ── 플레이어: 펌프 샷건(펠릿 8) Primary2 → 2 → 하늘로 ──
                WeaponDefinitionSO shotgunDef = AssetDatabase.LoadAssetAtPath<WeaponDefinitionSO>(ShotgunPath);
                Assert.IsNotNull(shotgunDef, $"{ShotgunPath} 없음");
                var shotgun = new WeaponItem(shotgunDef, Magazine.Full(shotgunDef.internalMagazine));
                ItemInstance occupied = raid.Inventory.GetEquipped(EquipSlot.Primary2);
                if (occupied != null)
                    Assert.IsTrue(raid.Inventory.Remove(occupied), "Primary2 비우기");
                Assert.IsTrue(raid.Inventory.TryEquipNew(shotgun, EquipSlot.Primary2), "샷건 → Primary2");
                input = new SimInput();
                yield return null;
                yield return input.Keyboard.Tap(Key.Digit2);
                yield return M3Scene.WaitUntil(() => weapon.Model != null && ReferenceEquals(weapon.Model.Item, shotgun) && !weapon.IsSwitching, 10f,
                    () => $"2 → 샷건 (현재 {weapon.ActiveSlot}, 교체 중 {weapon.IsSwitching})");
                Assert.AreEqual(8, weapon.CurrentAmmo.pelletCount, "12ga 펠릿 8");
                raid.LookDown(-PlayerAimUpDeg);
                var gun = new PlayerGun(input.Mouse, weapon, shotgun);

                // ── 교전 진입 대기 (총성 2초마다, 샷건 연사) ──
                var everCombat = new HashSet<EnemyAgent>();
                bool glintSeen = false;
                float next = 0f;
                float start = Time.realtimeSinceStartup;
                while (true)
                {
                    foreach (EnemyAgent e in raid.Enemies)
                        if (e.State == EnemyState.Combat)
                            everCombat.Add(e);
                    glintSeen |= glints.Any(g => g.IsOn);
                    int mutantsEngaged = mutants.Count(everCombat.Contains);
                    int m4Engaged = m4.Count(everCombat.Contains);
                    bool ready = everCombat.Contains(boss) && glintSeen && snipers.Any(everCombat.Contains)
                                 && mutantsEngaged >= MinMutantsEngaged && m4Engaged >= MinM4Engaged;
                    if (ready)
                        break;
                    if (Time.realtimeSinceStartup - start > EngageTimeoutSec)
                        Assert.Fail($"{EngageTimeoutSec}초 안에 최악 상황 교전이 만들어지지 않았다: 보스 {everCombat.Contains(boss)}, 섬광 {glintSeen}, " +
                                    $"저격수 교전 {snipers.Count(everCombat.Contains)}, 변이체 {mutantsEngaged}/{MinMutantsEngaged}, M4 {m4Engaged}/{MinM4Engaged} | {placement} | {StateSummary(center)}");
                    next = MaybeNoise(center, next);
                    gun.Step();
                    yield return null;
                }
                float toReady = Time.realtimeSinceStartup - start;
                Assert.AreEqual(AshStormPhase.Peak, storm.Phase, "교전 대기 중 Peak 유지");

                // ── 측정 A (시간) ──
                Measurement m = null;
                List<EnemyAgent> shooters = m4.Concat(guards).Append(boss).ToList();   // 7D-4: 접근 정지 확인(변이체·저격수 제외)
                yield return Measure(storm, audioUpdate, stormUpdate, radiation, target, center, gun, glints, boss, mutants, shooters, capture, counters, r => m = r);
                capture.Stop();

                // ── 측정 B (GC) ──
                GcResult gc = null;
                yield return MeasureGc(director, resubscribe, weapon, aiUpdate, weaponUpdate, projectileStep, survivalUpdate, audioUpdate, stormUpdate,
                    radiation, target, center, gun, counters, r => gc = r);
                Replay replay = MeasureReplay(raid.Events, capture);

                int bound = ProjectileUpperBound(shotgunDef, weapon.CurrentAmmo, out string boundDetail);
                Assert.AreEqual(AshStormPhase.Peak, storm.Phase, "측정 내내 Peak");
                Assert.IsFalse(raid.Health.IsDead, "플레이어 생존 (레이드 진행 중)");

                string report =
                    $"[M7PerformanceTests] 적 {ExpectedEnemies}, 폭풍 Peak(시야 {storm.VisibilityM}m, AI 상한 {director.VisibilityCapM}m), 교전 준비 {toReady:0.0}초(교전 경험 {everCombat.Count}명) | {placement} | " +
                    $"A) {m} | B) {gc} | 이벤트 재발행 {replay} | 투사체 상한 {bound}({boundDetail}) | " +
                    $"오디오: 재생 {audio.PlayedCount}, 거리 밖 {audio.CulledCount}, voice 빼앗기 {audio.Voices.StealCount}, 버림 {audio.Voices.DroppedCount}, 가이거 클릭 {audio.Ambient.GeigerClicks} | " +
                    $"샷건 내구도 {shotgun.Durability:0.0}·고장 {shotgun.IsJammed}, 플레이어 선량 {raid.Health.Radiation.Dose:0.0} | 화면 {Screen.width}x{Screen.height}, 배치모드 렌더 없음";
                Debug.Log(report);

                // 전제: 최악 상황이 실제로 측정에 들어갔다
                Assert.AreEqual(ExpectedEnemies, m.MinAlive, "측정 중 34명 생존");
                Assert.Greater(m.EnemyShots, 0, "측정 중 적 사격(ShotFired)");
                Assert.Greater(m.LmgShots, 0, "측정 중 보스 LMG ShotFired");
                Assert.Greater(m.GlintFrames, 0, "측정 중 저격수 경고(섬광)");
                Assert.Greater(m.DmrShots, 0, "측정 중 저격수 DMR 사격");
                Assert.Greater(m.MeleeHits, 0, "측정 중 변이체 근접 타격");
                Assert.Greater(m.PlayerShots, 0, "측정 중 플레이어 샷건 사격");
                Assert.GreaterOrEqual(m.ShooterNearestM, ShooterMinGapM, "사수가 표적에 겹쳐 들어오지 않는다 (7D-3 발견 1 → AiConfigSO.approachStopDistanceM)");
                Assert.GreaterOrEqual(m.MaxLive, shotgunDef != null ? 8 : 0, "샷건 한 발 = 펠릿 8 이상 동시 탄");
                Assert.LessOrEqual(m.MaxLive, bound, $"동시 탄 ≤ 이론 상한 ({boundDetail})");

                // 예산 (Q50 A — 기존 값)
                Assert.LessOrEqual(m.AiAvg, AiAverageBudgetMs, "AI 틱 평균 ≤ 1.0ms");
                Assert.LessOrEqual(m.AiP99, AiP99BudgetMs, "AI 틱 99% ≤ 3ms");
                Assert.LessOrEqual(m.ProjAvg, ProjectileAverageBudgetMs, "투사체 스텝 평균 ≤ 1.0ms");
                Assert.LessOrEqual(m.FrameMedian, FrameMedianBudgetMs, "프레임 중앙값 ≤ 16.6ms (렌더 제외)");
                Assert.LessOrEqual(m.AudioStormRadiationAvgMs, AudioStormRadiationBudgetMs, "Audio + 폭풍 + RadiationField ≤ 0.3ms");

                // GC 0
                Assert.Less(gc.Noisy, gc.Frames / 2, $"GC 측정 잡음이 너무 많다 ({gc})");
                Assert.Greater(gc.ProbeBlocks, 0, $"GC 판정 도구: Recorder가 실제 할당을 잡는다 ({gc})");
                Assert.Greater(gc.PlayerShots, 0, "GC 구간 플레이어 사격 (무기 경로가 실제로 돌았다)");
                Assert.Greater(gc.EnemyShots, 0, "GC 구간 적 사격 (AI 무기 경로)");
                Assert.Greater(gc.MeleeHits, 0, "GC 구간 근접 타격 (근접 경로)");
                Assert.AreEqual(0, gc.Ai, $"AI 경로(감각·전술·근접·적 사격·이벤트 구독자) GC 0 ({gc})");
                Assert.AreEqual(0, gc.Weapon, $"플레이어 무기 경로(샷건 발사·소음·총성) GC 0 ({gc})");
                Assert.AreEqual(0, gc.Projectile, $"투사체 스텝 GC 0 ({gc})");
                Assert.AreEqual(0, gc.Survival, $"생존 경로(PlayerHealth.Update: 부위·수분·에너지·방사능) GC 0 ({gc})");
                Assert.AreEqual(0, gc.AudioStorm, $"AudioDirector + AshStormController Update GC 0 ({gc})");
                Assert.AreEqual(0, gc.Radiation, $"RadiationField 질의 GC 0 ({gc})");
                Assert.AreEqual(0, m.GcFrames, $"측정 A 오디오·폭풍·방사능 직접 호출 GC 0 (할당 프레임 {m.GcFrames}, 잡음 {m.GcNoisy})");
                Assert.Greater(replay.Events, 0, "재발행할 이벤트");
                Assert.IsTrue(replay.NoAlloc, $"오디오 이벤트 경로 GC 0 ({replay})");
            }
            finally
            {
                capture.Dispose();
                counters.Dispose();
                input?.Mouse.SetLeft(false);
                director.enabled = true;
                weapon.enabled = true;
                if (target != null)
                    Object.Destroy(target.gameObject);
                foreach (EnemyArchetypeSO a in clones)
                    Object.Destroy(a);
            }
        }

        // ── 측정 A ──

        private sealed class Measurement
        {
            public double AiAvg, AiP99, AiMax, ProjAvg, ProjMax, FrameMedian, FrameP99, CombatAvg;
            public double AudioAvgMs, StormAvgMs, RadiationAvgMs, AudioStormRadiationAvgMs, AudioStormRadiationMaxMs;
            public int Frames, ProjSteps, MinAlive, MaxLive, GcFrames, GcNoisy, Refilled;
            public int EnemyShots, LmgShots, DmrShots, PlayerShots, MeleeHits, BulletHits, GlintFrames, MutantAttackVocals, Footsteps;
            public float MutantNearestM = float.MaxValue, ShooterNearestM = float.MaxValue, BossMaxDistM;

            public override string ToString() =>
                $"{Frames}프레임, 교전 평균 {CombatAvg:0.0}명, AI 평균 {AiAvg:0.000}ms·99% {AiP99:0.000}ms·최대 {AiMax:0.000}ms, " +
                $"투사체 평균 {ProjAvg:0.000}ms·최대 {ProjMax:0.000}ms({ProjSteps}스텝, 동시 탄 최대 {MaxLive}), 프레임 중앙값 {FrameMedian:0.00}ms·99% {FrameP99:0.00}ms, 생존 최소 {MinAlive} | " +
                $"Audio {AudioAvgMs:0.0000} + 폭풍 {StormAvgMs:0.0000} + 방사능 {RadiationAvgMs:0.0000} = {AudioStormRadiationAvgMs:0.0000}ms(최대 {AudioStormRadiationMaxMs:0.000}ms), GC 프레임 {GcFrames}(잡음 {GcNoisy}) | " +
                $"적 ShotFired {EnemyShots}(LMG {LmgShots}, DMR {DmrShots}), 플레이어 샷건 {PlayerShots}, 근접 타격 {MeleeHits}, 총탄 피격 {BulletHits}, 섬광 프레임 {GlintFrames}, " +
                $"MutantAttack {MutantAttackVocals}, 발소리 {Footsteps}, 변이체 최근접 {MutantNearestM:0.00}m, 사수(SCV·RDR·호위·보스) 최근접 {ShooterNearestM:0.00}m, 보스-표적 최대 {BossMaxDistM:0.0}m, 측정 전 탄 보충 {Refilled}명";
        }

        private IEnumerator Measure(AshStormController storm, Action audioUpdate, Action stormUpdate, RadiationField radiation, PerfTarget target,
            Vector3 center, PlayerGun gun, List<SniperGlint> glints, EnemyAgent boss, List<EnemyAgent> mutants, List<EnemyAgent> shooters,
            EventCapture capture, Counters counters, Action<Measurement> result)
        {
            float next = 0f;
            float warmEnd = Time.realtimeSinceStartup + WarmupSec;
            while (Time.realtimeSinceStartup < warmEnd)
            {
                next = MaybeNoise(center, next);
                gun.Step();
                yield return null;
            }
            int refilled = RaidPerformanceTests.RefillAmmo(raid.Enemies);
            audioUpdate();   // JIT 제외
            stormUpdate();
            QueryRadiation(radiation, target);

            AiDirector director = raid.Director;
            director.Stats.Reset();
            raid.Projectiles.ResetStats();
            Recorder gc = Recorder.Get("GC.Alloc");
            Action empty = () => { };
            Action radiationQuery = () => QueryRadiation(radiation, target);
            SampleGc(gc, empty);

            var ai = new List<double>(20000);
            var frames = new List<double>(20000);
            var m = new Measurement { MinAlive = int.MaxValue, Refilled = refilled };
            int combatSum = 0;
            double audioSum = 0, stormSum = 0, radiationSum = 0;
            var sw = new Stopwatch();
            yield return null;
            Counters.Snapshot before = counters.Take();
            int meleeBefore = target.MeleeHits, bulletBefore = target.BulletHits;
            capture.Begin();
            float end = Time.realtimeSinceStartup + MeasureSec;
            while (Time.realtimeSinceStartup < end)
            {
                next = MaybeNoise(center, next);
                gun.Step();
                yield return null;
                ai.Add(director.Stats.LastMs);
                frames.Add(Time.unscaledDeltaTime * 1000.0);

                int n = SampleGcTimed(gc, audioUpdate, sw, out double a)
                        + SampleGcTimed(gc, stormUpdate, sw, out double s)
                        + SampleGcTimed(gc, radiationQuery, sw, out double r);
                audioSum += a;
                stormSum += s;
                radiationSum += r;
                m.AudioStormRadiationMaxMs = Math.Max(m.AudioStormRadiationMaxMs, a + s + r);
                if (n > 0)
                {
                    if (SampleGc(gc, empty) > 0)
                        m.GcNoisy++;
                    else
                        m.GcFrames++;
                }

                m.MinAlive = Math.Min(m.MinAlive, raid.Enemies.Count(e => e != null && !e.IsDead));
                combatSum += raid.Enemies.Count(e => e != null && !e.IsDead && e.State == EnemyState.Combat);
                m.MaxLive = Math.Max(m.MaxLive, raid.Projectiles.LiveCount);
                if (glints.Any(g => g.IsOn))
                    m.GlintFrames++;
                m.MutantNearestM = Math.Min(m.MutantNearestM, mutants.Min(e => Flat(e.transform.position - center)));
                m.ShooterNearestM = Math.Min(m.ShooterNearestM, shooters.Min(e => Flat(e.transform.position - center)));
                m.BossMaxDistM = Math.Max(m.BossMaxDistM, Flat(boss.transform.position - center));
            }
            m.Frames = frames.Count;
            m.AiAvg = director.Stats.AverageMs;
            m.AiMax = director.Stats.MaxMs;
            m.AiP99 = Percentile(ai, 0.99);
            m.ProjAvg = raid.Projectiles.AverageStepMs;
            m.ProjMax = raid.Projectiles.MaxStepMs;
            m.ProjSteps = raid.Projectiles.StepSamples;
            m.FrameMedian = Percentile(frames, 0.5);
            m.FrameP99 = Percentile(frames, 0.99);
            m.CombatAvg = (double)combatSum / Math.Max(1, m.Frames);
            m.AudioAvgMs = audioSum / Math.Max(1, m.Frames);
            m.StormAvgMs = stormSum / Math.Max(1, m.Frames);
            m.RadiationAvgMs = radiationSum / Math.Max(1, m.Frames);
            m.AudioStormRadiationAvgMs = m.AudioAvgMs + m.StormAvgMs + m.RadiationAvgMs;
            Counters.Snapshot after = counters.Take();
            m.EnemyShots = after.EnemyShots - before.EnemyShots;
            m.LmgShots = after.LmgShots - before.LmgShots;
            m.DmrShots = after.DmrShots - before.DmrShots;
            m.PlayerShots = after.PlayerShots - before.PlayerShots;
            m.MutantAttackVocals = after.MutantAttackVocals - before.MutantAttackVocals;
            m.Footsteps = after.Footsteps - before.Footsteps;
            m.MeleeHits = target.MeleeHits - meleeBefore;
            m.BulletHits = target.BulletHits - bulletBefore;
            result(m);
        }

        // 방사능 구역 질의: 실제 소비자(PlayerHealth = 플레이어 위치, AudioDirector 가이거 = 청취자 위치)와 같은 두 번
        private static void QueryRadiation(RadiationField radiation, PerfTarget target)
        {
            radiation.ZoneRatePerSec(target.Position);
            radiation.ZoneRatePerSec(target.EyePosition);
        }

        // ── 측정 B (GC) ──

        private sealed class GcResult
        {
            public int Frames, Noisy, WarmedColliders, Ai, Weapon, Projectile, Survival, AudioStorm, Radiation;
            public int PlayerShots, EnemyShots, LmgShots, MeleeHits, ProbeBlocks;
            public readonly List<string> Events = new List<string>();

            public override string ToString() =>
                $"GC {Frames}프레임(잡음 {Noisy}, 도구 확인 {ProbeBlocks}블록, 미리 만든 콜라이더 래퍼 {WarmedColliders}): 할당 프레임 AI {Ai}·무기 {Weapon}·투사체 {Projectile}·생존 {Survival}·Audio+폭풍 {AudioStorm}·방사능 {Radiation} | " +
                $"구간 중 플레이어 샷건 {PlayerShots}, 적 ShotFired {EnemyShots}(LMG {LmgShots}), 근접 타격 {MeleeHits}" +
                (Events.Count > 0 ? " | 할당: " + string.Join(", ", Events) : "");
        }

        private IEnumerator MeasureGc(AiDirector director, MethodInfo resubscribe, WeaponController weapon,
            Action aiUpdate, Action weaponUpdate, Action projectileStep, Action survivalUpdate, Action audioUpdate, Action stormUpdate,
            RadiationField radiation, PerfTarget target, Vector3 center, PlayerGun gun, Counters counters, Action<GcResult> result)
        {
            Recorder gc = Recorder.Get("GC.Alloc");
            Action empty = () => { };
            Action radiationQuery = () => QueryRadiation(radiation, target);
            Action[] paths = { aiUpdate, weaponUpdate, projectileStep, survivalUpdate, audioUpdate, stormUpdate, radiationQuery };
            var allocFrames = new int[paths.Length];
            var frameBlocks = new int[paths.Length];
            var frameLive = new int[paths.Length];
            var r = new GcResult();
            // 판정 도구 자체 확인: 실제 관리 할당을 Recorder가 잡는다
            object sink = null;
            r.ProbeBlocks = SampleGc(gc, () => sink = new object[16]);
            GC.KeepAlive(sink);
            r.WarmedColliders = WarmColliderWrappers();
            int meleeBefore = target.MeleeHits;
            Counters.Snapshot before = counters.Take();

            // 엔진 호출 대신 직접 부른다. AiDirector는 끄면 소음 구독이 풀리므로 다시 구독(엔진 경로와 같은 청각)
            director.enabled = false;
            resubscribe.Invoke(director, null);
            weapon.enabled = false;
            try
            {
                yield return null;
                aiUpdate();      // 첫 직접 호출(JIT) 제외
                weaponUpdate();
                SampleGc(gc, empty);
                float next = 0f;
                float gcEnd = Time.realtimeSinceStartup + GcMinSec;
                for (int f = 0; f < GcFrames || Time.realtimeSinceStartup < gcEnd; f++)
                {
                    next = MaybeNoise(center, next);
                    gun.Step();
                    yield return null;
                    bool any = false;
                    for (int p = 0; p < paths.Length; p++)
                    {
                        frameLive[p] = raid.Projectiles.LiveCount;
                        frameBlocks[p] = SampleGc(gc, paths[p]);
                        any |= frameBlocks[p] > 0;
                    }
                    // 잡음: 빈 대리자도 할당으로 잡히면 이 프레임은 세지 않는다 (M6·AiPerformanceTests와 같은 규칙)
                    if (any && SampleGc(gc, empty) > 0)
                    {
                        r.Noisy++;
                    }
                    else if (any)
                    {
                        for (int p = 0; p < paths.Length; p++)
                        {
                            if (frameBlocks[p] == 0)
                                continue;
                            allocFrames[p]++;
                            if (r.Events.Count < 16)
                                r.Events.Add($"{PathNames[p]}@{f}: {frameBlocks[p]}블록 (호출 전 동시 탄 {frameLive[p]}, 후 {raid.Projectiles.LiveCount})");
                        }
                    }
                    r.Frames++;
                }
            }
            finally
            {
                weapon.enabled = true;
                director.enabled = true;   // OnEnable → TrySubscribe(이미 구독 = 그대로)
            }
            Counters.Snapshot after = counters.Take();
            r.Ai = allocFrames[0];
            r.Weapon = allocFrames[1];
            r.Projectile = allocFrames[2];
            r.Survival = allocFrames[3];
            r.AudioStorm = allocFrames[4] + allocFrames[5];
            r.Radiation = allocFrames[6];
            r.PlayerShots = after.PlayerShots - before.PlayerShots;
            r.EnemyShots = after.EnemyShots - before.EnemyShots;
            r.LmgShots = after.LmgShots - before.LmgShots;
            r.MeleeHits = target.MeleeHits - meleeBefore;
            result(r);
        }

        // ── 투사체 상한 ──

        // 쏘는 사람마다 (rpm/60 × 탄 비행 시간 + 2) × 펠릿 수의 합 — 무기 rpm·탄약 maxFlightTimeSec·pelletCount(데이터)에서.
        private int ProjectileUpperBound(WeaponDefinitionSO playerWeapon, AmmoDefinitionSO playerAmmo, out string detail)
        {
            int total = PerShooter(playerWeapon, playerAmmo);
            int enemies = 0;
            foreach (EnemyAgent e in raid.Enemies)
            {
                EnemyWeapon w = e != null ? e.Weapon : null;
                if (w == null)
                    continue;
                enemies += PerShooter(w.Item.Definition, w.Loadout.ammo);
            }
            detail = $"플레이어 샷건 {total} + 적 {enemies}";
            return total + enemies;
        }

        private static int PerShooter(WeaponDefinitionSO weapon, AmmoDefinitionSO ammo)
        {
            if (weapon == null || ammo == null)
                return 0;
            int pellets = Mathf.Max(1, ammo.pelletCount);
            return (Mathf.FloorToInt(weapon.rpm / 60f * ammo.maxFlightTimeSec) + 2) * pellets;
        }

        // ── 이벤트 재발행 (M6과 같음 + EnemyVocalized) ──

        private sealed class Replay
        {
            public int Events, Attempts;
            public bool NoAlloc;
            public double UsPerEvent;
            public string Kinds;
            public override string ToString() => $"{Events}개({Kinds}) 평균 {UsPerEvent:0.0}µs/이벤트, GC 0 = {NoAlloc} (시도 {Attempts})";
        }

        private static Replay MeasureReplay(EventBus bus, EventCapture capture)
        {
            var r = new Replay { Events = capture.Count, Kinds = $"총성 {capture.Shots}·발소리 {capture.Footsteps}·울음 {capture.Vocals}" };
            if (capture.Count == 0)
                return r;
            Action publish = () => capture.Publish(bus);
            publish();
            var sw = Stopwatch.StartNew();
            publish();
            sw.Stop();
            r.UsPerEvent = sw.Elapsed.TotalMilliseconds * 1000.0 / capture.Count;
            Recorder gc = Recorder.Get("GC.Alloc");
            Action empty = () => { };
            for (int i = 0; i < 6 && !r.NoAlloc; i++)
            {
                r.Attempts++;
                SampleGc(gc, empty);
                r.NoAlloc = SampleGc(gc, publish) == 0;
                if (!r.NoAlloc)
                    System.Threading.Thread.Sleep(50 * (i + 1));
            }
            return r;
        }

        private static int SampleGcTimed(Recorder recorder, Action action, Stopwatch sw, out double ms)
        {
            recorder.enabled = false;
            recorder.FilterToCurrentThread();
            recorder.enabled = true;
            try
            {
                sw.Restart();
                action();
                sw.Stop();
            }
            finally
            {
                recorder.enabled = false;
                recorder.CollectFromAllThreads();
            }
            ms = sw.Elapsed.TotalMilliseconds;
            return recorder.sampleBlockCount;
        }

        // 씬의 모든 콜라이더·그 transform·root의 관리 래퍼를 미리 만든다. 엔진은 네이티브 객체의 C# 래퍼를 스크립트가 처음 만질 때 만든다(1회 할당) —
        // 탄·시야 레이가 처음 맞힌 콜라이더(hit.collider·transform.root)에서 생기는 이 1회 할당은 정상 상태 프레임 할당이 아니므로 GC 구간 전에 치운다.
        private static int WarmColliderWrappers()
        {
            Collider[] colliders = Object.FindObjectsByType<Collider>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (Collider c in colliders)
            {
                Transform t = c.transform;
                Transform root = t.root;
                c.GetComponentInParent<IDamageable>();
                GC.KeepAlive(root);
            }
            return colliders.Length;
        }

        private static int SampleGc(Recorder recorder, Action action)
        {
            recorder.enabled = false;
            recorder.FilterToCurrentThread();
            recorder.enabled = true;
            try
            {
                action();
            }
            finally
            {
                recorder.enabled = false;
                recorder.CollectFromAllThreads();
            }
            return recorder.sampleBlockCount;
        }

        // ── 이벤트 기록·집계 (구독자는 할당 없음 — GC 측정 구간 안에서 불린다) ──

        private sealed class EventCapture : IDisposable
        {
            private readonly EventBus bus;
            private readonly FootstepTaken[] steps;
            private readonly ShotFired[] shots;
            private readonly EnemyVocalized[] vocals;
            private readonly Action<FootstepTaken> onStep;
            private readonly Action<ShotFired> onShot;
            private readonly Action<EnemyVocalized> onVocal;
            private bool recording;
            private bool subscribed;

            public int Footsteps { get; private set; }
            public int Shots { get; private set; }
            public int Vocals { get; private set; }
            public int Count => Footsteps + Shots + Vocals;

            public EventCapture(EventBus bus, int capacity)
            {
                this.bus = bus;
                steps = new FootstepTaken[capacity];
                shots = new ShotFired[capacity];
                vocals = new EnemyVocalized[capacity];
                onStep = e =>
                {
                    if (recording && Footsteps < steps.Length)
                        steps[Footsteps++] = e;
                };
                onShot = e =>
                {
                    if (recording && Shots < shots.Length)
                        shots[Shots++] = e;
                };
                onVocal = e =>
                {
                    if (recording && Vocals < vocals.Length)
                        vocals[Vocals++] = e;
                };
                bus.Subscribe(onStep);
                bus.Subscribe(onShot);
                bus.Subscribe(onVocal);
                subscribed = true;
            }

            public void Begin()
            {
                Footsteps = 0;
                Shots = 0;
                Vocals = 0;
                recording = true;
            }

            public void Stop()
            {
                recording = false;
                Unsubscribe();
            }

            public void Publish(EventBus target)
            {
                for (int i = 0; i < Shots; i++)
                    target.Publish(shots[i]);
                for (int i = 0; i < Footsteps; i++)
                    target.Publish(steps[i]);
                for (int i = 0; i < Vocals; i++)
                    target.Publish(vocals[i]);
            }

            private void Unsubscribe()
            {
                if (!subscribed)
                    return;
                bus.Unsubscribe(onStep);
                bus.Unsubscribe(onShot);
                bus.Unsubscribe(onVocal);
                subscribed = false;
            }

            public void Dispose() => Unsubscribe();
        }

        private sealed class Counters : IDisposable
        {
            public struct Snapshot
            {
                public int EnemyShots, LmgShots, DmrShots, PlayerShots, MutantAttackVocals, Footsteps;
            }

            private readonly EventBus bus;
            private readonly Action<ShotFired> onShot;
            private readonly Action<EnemyVocalized> onVocal;
            private readonly Action<FootstepTaken> onStep;
            private Snapshot s;
            private bool subscribed;

            public Counters(EventBus bus)
            {
                this.bus = bus;
                onShot = e =>
                {
                    if (e.Source == SoundSource.Player)
                    {
                        s.PlayerShots++;
                        return;
                    }
                    s.EnemyShots++;
                    if (e.Weapon != null && e.Weapon.id == LmgId)
                        s.LmgShots++;
                    else if (e.Weapon != null && e.Weapon.id == DmrId)
                        s.DmrShots++;
                };
                onVocal = e =>
                {
                    if (e.Kind == EnemyVocalKind.MutantAttack)
                        s.MutantAttackVocals++;
                };
                onStep = e => s.Footsteps++;
                bus.Subscribe(onShot);
                bus.Subscribe(onVocal);
                bus.Subscribe(onStep);
                subscribed = true;
            }

            public Snapshot Take() => s;

            public void Dispose()
            {
                if (!subscribed)
                    return;
                bus.Unsubscribe(onShot);
                bus.Unsubscribe(onVocal);
                bus.Unsubscribe(onStep);
                subscribed = false;
            }
        }

        // 플레이어 샷건 연사: 프레임마다 LMB를 눌렀다 뗐다 하고(펌프 = 새로 당겨야 발사, 70rpm이 간격을 정한다), 튜브를 테스트가 채운다(제품 코드 무변경).
        private sealed class PlayerGun
        {
            private readonly SimMouse mouse;
            private readonly WeaponController weapon;
            private readonly WeaponItem item;
            private bool down;

            public PlayerGun(SimMouse mouse, WeaponController weapon, WeaponItem item)
            {
                this.mouse = mouse;
                this.weapon = weapon;
                this.item = item;
            }

            public void Step()
            {
                Magazine tube = item.LoadedMagazine;
                if (tube != null && weapon.Model != null && !weapon.Model.IsReloading)
                    while (!tube.IsFull && tube.TryAddRound()) { }
                down = !down;
                mouse.SetLeft(down);
            }
        }

        // ── 배치 ──

        // 보스 거점 중심 3~9m, NavMesh·빈 자리, 보스 눈 → 표적 눈 시선이 뚫린 곳
        private static Vector3 PickTargetNearAnchor(EnemyAgent boss, IEnumerable<EnemyAgent> guards, Vector3 anchor)
        {
            Transform eye = M3Scene.EnemyEye(boss);
            Transform[] ignore = new[] { boss.transform }.Concat(guards.Select(g => g.transform)).ToArray();
            int noNav = 0, offRing = 0, occupied = 0, blocked = 0;
            foreach (float distance in new[] { 5f, 3f, 7f, 9f })
            {
                for (int i = 0; i < 12; i++)
                {
                    float a = i * 30f * Mathf.Deg2Rad;
                    Vector3 p = anchor + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * distance;
                    if (!NavMesh.SamplePosition(p + Vector3.up * 2f, out NavMeshHit hit, 4f, NavMesh.AllAreas))
                    {
                        noNav++;
                        continue;
                    }
                    if (Mathf.Abs(Flat(hit.position - anchor) - distance) > 3f)
                    {
                        offRing++;
                        continue;
                    }
                    if (Physics.CheckCapsule(hit.position + Vector3.up * 0.4f, hit.position + Vector3.up * 1.5f, 0.3f,
                            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    {
                        occupied++;
                        continue;
                    }
                    if (M3Scene.IsBlocked(eye.position, hit.position + Vector3.up * (TargetRaiseM + 1.68f), ignore))
                    {
                        blocked++;
                        continue;
                    }
                    return hit.position;
                }
            }
            Assert.Fail($"보스 거점 {anchor} 근처에 표적 자리가 없다 (NavMesh 없음 {noNav}, 반경 밖 {offRing}, 자리 막힘 {occupied}, 보스 시선 가림 {blocked}, 보스 눈 {eye.position})");
            return anchor;
        }

        // 표적 주변 고리 (M6PerformanceTests와 같은 규칙: 12~24m, Peak 시야 30m 안)
        private static List<Vector3> RingSpots(Terrain terrain, Vector3 center, int count, float baseRadius, float step, float spacing)
        {
            var spots = new List<Vector3>();
            for (int i = 0; i < 72 && spots.Count < count; i++)
            {
                float angle = i * 5f * 7f * Mathf.Deg2Rad;
                float radius = baseRadius + (i % 5) * step;
                Vector3 p = center + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * radius;
                p.y = terrain.SampleHeight(p) + terrain.transform.position.y;
                if (!NavMesh.SamplePosition(p, out NavMeshHit hit, 3f, NavMesh.AllAreas))
                    continue;
                if (spots.Any(s => Vector3.Distance(s, hit.position) < spacing))
                    continue;
                spots.Add(hit.position);
            }
            Assert.GreaterOrEqual(spots.Count, count / 2, "고리 지점 부족");
            return spots;
        }

        // 저격수 자리: 표적에서 18~27m, 빈 자리, 눈 높이 → 표적 눈 시선이 뚫림, 서로 60° 이상 떨어짐
        private static List<Vector3> SniperSpots(PerfTarget target, Vector3 center, int count, List<Vector3> used)
        {
            var spots = new List<Vector3>();
            foreach (float distance in new[] { 22f, 18f, 26f })
            {
                for (int i = 0; i < 24 && spots.Count < count; i++)
                {
                    float a = (i * 15f + 7f) * Mathf.Deg2Rad;
                    Vector3 dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                    if (spots.Any(s => Vector3.Angle(Flatten(s - center), dir) < 60f))
                        continue;
                    if (!NavMesh.SamplePosition(center + dir * distance + Vector3.up * 2f, out NavMeshHit hit, 3f, NavMesh.AllAreas))
                        continue;
                    if (Mathf.Abs(Flat(hit.position - center) - distance) > 2.5f)
                        continue;
                    if (used.Any(u => Vector3.Distance(u, hit.position) < 2f))
                        continue;
                    if (Physics.CheckCapsule(hit.position + Vector3.up * 0.4f, hit.position + Vector3.up * 1.5f, 0.3f,
                            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                        continue;
                    if (M3Scene.IsBlocked(hit.position + Vector3.up * 1.6f, target.EyePosition, target.transform))
                        continue;
                    spots.Add(hit.position);
                }
                if (spots.Count >= count)
                    break;
            }
            Assert.AreEqual(count, spots.Count, "저격수 자리 부족");
            return spots;
        }

        // 변이체 자리: 표적 2~6m 둘레, 45°씩
        private static List<Vector3> MutantSpots(Vector3 center, int count)
        {
            var spots = new List<Vector3>();
            for (int i = 0; i < 48 && spots.Count < count; i++)
            {
                float a = (i * 45f + (i / 8) * 20f) * Mathf.Deg2Rad;
                float radius = 2f + (i % 3) * 2f;
                Vector3 p = center + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * radius;
                if (!NavMesh.SamplePosition(p + Vector3.up, out NavMeshHit hit, 1.5f, NavMesh.AllAreas))
                    continue;
                float d = Flat(hit.position - center);
                if (d < 1.8f || d > 6.5f)
                    continue;
                if (spots.Any(s => Vector3.Distance(s, hit.position) < 1.0f))
                    continue;
                spots.Add(hit.position);
            }
            Assert.AreEqual(count, spots.Count, "변이체 자리 부족");
            return spots;
        }

        private static void WarpFacing(EnemyAgent e, Vector3 spot, Vector3 center)
        {
            Vector3 face = center - spot;
            M3Scene.WarpEnemy(e, spot, Mathf.Atan2(face.x, face.z) * Mathf.Rad2Deg);
        }

        // ── 도우미 ──

        private static Action UpdateDelegate(MonoBehaviour behaviour, string name)
        {
            MethodInfo method = behaviour.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.IsNotNull(method, $"{behaviour.GetType().Name}.{name}");
            return (Action)Delegate.CreateDelegate(typeof(Action), behaviour, method);
        }

        private float MaybeNoise(Vector3 at, float next)
        {
            if (Time.time < next)
                return next;
            raid.Events.Publish(new NoiseEmitted(at + Vector3.up * 1.4f, StressNoiseRadiusM, NoiseKind.Gunshot));
            return Time.time + NoiseIntervalSec;
        }

        private void MakeImmortal(EnemyAgent enemy, List<EnemyArchetypeSO> clones)
        {
            EnemyArchetypeSO clone = Object.Instantiate(enemy.Archetype);
            clone.hp = 1e9f;
            clone.headHp = 1e9f;
            clone.legsHp = 1e9f;
            clones.Add(clone);
            PropertyInfo body = typeof(EnemyAgent).GetProperty(nameof(EnemyAgent.Body));
            Assert.IsNotNull(body);
            body.SetValue(enemy, new EnemyBodyModel(clone, raid.Director.Config));
        }

        private string StateSummary(Vector3 center) =>
            string.Join(", ", raid.Enemies.Where(e => e != null).Select(e => $"{e.name}:{e.State}@{Flat(e.transform.position - center):0.0}m"));

        private static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

        private static Vector3 Flatten(Vector3 v)
        {
            v.y = 0f;
            return v;
        }

        private static double Percentile(List<double> values, double p)
        {
            if (values.Count == 0)
                return 0.0;
            var sorted = values.OrderBy(v => v).ToList();
            int index = Math.Min(sorted.Count - 1, Math.Max(0, (int)Math.Ceiling(p * sorted.Count) - 1));
            return sorted[index];
        }
    }

    // 무적 표적 (M3 TestTarget과 같은 캡슐·눈 높이) + 근접(부위 힌트 있음)·총탄 피격을 따로 센다. ApplyDamage는 할당 없음.
    internal sealed class PerfTarget : MonoBehaviour, IPlayerTarget, IDamageable
    {
        private const float StandHeight = 1.8f;
        private const float EyeBelowTop = 0.12f;

        private CapsuleCollider capsule;
        private bool registered;

        public int MeleeHits { get; private set; }
        public int BulletHits { get; private set; }

        public Transform Root => transform.root;
        public Vector3 Position => transform.position;
        public Vector3 EyePosition => transform.position + Vector3.up * (capsule.height - EyeBelowTop);
        public Vector3 CenterPosition => transform.position + Vector3.up * (capsule.height * 0.5f);
        public Vector3 Velocity => Vector3.zero;
        public TargetPosture Posture => TargetPosture.Standing;
        public bool IsDead => false;

        public static PerfTarget Create(Vector3 position)
        {
            var go = new GameObject("PerfTarget");
            go.transform.position = position;
            var capsule = go.AddComponent<CapsuleCollider>();
            capsule.radius = 0.3f;
            capsule.height = StandHeight;
            capsule.center = new Vector3(0f, StandHeight * 0.5f, 0f);
            var body = go.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            var target = go.AddComponent<PerfTarget>();
            target.capsule = capsule;
            Physics.SyncTransforms();
            return target;
        }

        public void Register()
        {
            if (GameManager.Instance == null)
                return;
            GameManager.Instance.Services.Register<IPlayerTarget>(this);
            registered = true;
        }

        public void ApplyDamage(in DamageInfo info)
        {
            if (info.HasPartHint)
                MeleeHits++;
            else
                BulletHits++;
        }

        private void OnDisable()
        {
            if (registered && GameManager.Instance != null)
                GameManager.Instance.Services.Unregister<IPlayerTarget>(this);
            registered = false;
        }
    }
}
#endif
