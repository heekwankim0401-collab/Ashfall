#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Ashfall.AI;
using Ashfall.Core;
using Ashfall.Inventory;
using Ashfall.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace Ashfall.Tests.PlayMode
{
    // M7 7B-4 검증 — 실제 Raid_CrimsonBasin의 M7 적 (M7 설계 §6 "7B-4", §7 AC1, SYS-AI §7, Q12·Q18·Q22~Q31):
    // ① 보스(AI-BOS) 처치 → 시체 = LMG·탄띠 없음(enemyOnly, Q18) + 유물 ITM-REL-01 정확히 1개(LT-BOS 보장 첫 굴림, Q31) → 루팅해 가져옴.
    // ② 보스 무리(보스 + 호위 3) 근처에 무적 표적·총성 → 교전/경계 → 표적을 거점 밖 55m로 옮겨 유인해도 거점(보스 첫 Tick 위치 공유, 7B 리뷰 6) 25m 안.
    // ③ 저격수(AI-SNP) 2명 처치 → 시체 DMR 내구도 50~90(Q12, 살아 있을 땐 100) → 한 명의 DMR을 루팅·Primary2 장착 → 2 → LMB로 실제 사격.
    //    내구도 ≥ 50이면 고장 확률 0(JamRules) — 첫 발은 고장 없이 나간다(발당 마모 0.1로 50 미만이 되는 둘째 발부터 확률 > 0이라 1발만 단언).
    // ④ 변이체 무리(MUT_REF 4) 근처 무적 표적 + 총성 → EnemyVocalized(MutantAlert, 무리 대원 눈 위치) → 접근(≤ 3m) → MutantAttack 울음,
    //    5초 더 지켜봐도 최근접 ≥ 1.0m(몸 겹침 없음, 7B 리뷰 15).
    // 적 처치는 IDamageable.ApplyDamage(EnemyAgent 실제 피격·사망 경로). 시체 루트는 LootContainer.Interact(상호작용 레이캐스트는 CorpseLootFlowTests).
    // 레이드 씬을 이름으로 직접 불러온다(흐름 레이드 아님 — 세이브 기록 없음, SaveSandbox SetUpFixture가 경로를 격리).
    public class M7AiRaidFlowTests
    {
        private const string BossName = "Enemy_M7_BOS_VIL1";
        private const string BossGroupPrefix = "Enemy_M7_BOS_VIL";
        private static readonly string[] SniperNames = { "Enemy_M7_SNP_PYL1", "Enemy_M7_SNP_HWY1" };
        private const string MutantPackPrefix = "Enemy_M7_MUT_REF";
        private const string RelicId = "ITM-REL-01";
        private const string LmgId = "WPN-LMG-01";
        private const string LmgBeltId = "MAG-762-100";
        private const string DmrId = "WPN-DMR-01";
        private const float AnchorToleranceM = 1.0f;   // NavMeshAgent 정지 거리·감속 여유
        private const float LureNearM = 12f;
        private const float LureFarM = 55f;
        private const float LureSeconds = 20f;
        private const float NoiseIntervalSec = 2f;

        private M4Raid raid;
        private SimInput input;
        private TestTarget target;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            M5Flow.ResetFlow();
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
            if (target != null)
                Object.Destroy(target.gameObject);
            target = null;
            M5Flow.ResetFlow();
            yield return null;
        }

        // ── ① 보스 처치 → LMG·탄띠 없음, 유물 ──

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Boss_Killed_CorpseHasRelic_NoLmgNoBelt_RelicLooted()
        {
            EnemyAgent boss = Enemy(BossName);
            Assert.AreEqual("AI-BOS", boss.Archetype.id);
            Assert.IsNotNull(boss.Weapon, "살아 있는 보스 무장");
            Assert.AreEqual(LmgId, boss.Weapon.Item.Definition.id, "보스 = LMG");
            Assert.IsTrue(boss.Weapon.Item.Definition.enemyOnly, "LMG enemyOnly");
            Assert.AreEqual(LmgBeltId, boss.Weapon.Loadout.magazine.id, "보스 탄띠");

            using var killed = new EventLog<EnemyKilled>(raid.Events);
            Kill(boss);
            yield return M3Scene.WaitUntil(() => killed.Count > 0, 3f, () => "보스 사망");
            EnemyKilled evt = killed.Received.Single(k => k.Corpse == boss.gameObject);
            Assert.AreEqual("AI-BOS", evt.ArchetypeId);
            string ids = string.Join(", ", evt.Items.Select(i => i.Definition.id));
            Assert.IsFalse(evt.Items.Any(i => i.Definition is WeaponDefinitionSO w && w.enemyOnly), $"시체에 enemyOnly 무기 없음 ({ids})");
            Assert.IsFalse(evt.Items.Any(i => i.Definition.id == LmgId), $"시체에 LMG 없음 ({ids})");
            Assert.IsFalse(evt.Items.Any(i => i.Definition.id == LmgBeltId), $"시체에 탄띠 없음 ({ids})");
            Assert.AreEqual(1, evt.Items.Count(i => i.Definition.id == RelicId), $"유물 정확히 1개 ({ids})");

            // 시체 루트 → 검색 → 유물 가져오기
            var container = boss.GetComponent<LootContainer>();
            Assert.IsNotNull(container, "보스 시체에 LootContainer");
            ItemInstance relic = evt.Items.First(i => i.Definition.id == RelicId);
            yield return OpenAndSearch(container);
            CollectionAssert.AreEquivalent(evt.Items, raid.Inventory.ActiveLoot.Items.Select(p => p.Item).ToArray(), "시체 루트 = 이벤트 아이템");
            Assert.IsTrue(raid.Inventory.TryQuickMove(relic), "유물 가져오기");
            Assert.IsTrue(raid.Inventory.IsInStorage(relic));
            Assert.AreEqual(1, raid.CountCarried(relic.Definition), "플레이어가 유물을 지님");
            Debug.Log($"[M7AiRaidFlowTests] 보스 시체: {ids}");
        }

        // ── ② 보스 무리: 거점 밖으로 유인해도 25m 안 ──

        [UnityTest]
        [Timeout(180000)]
        public IEnumerator BossGroup_TargetLuredOutsideAnchor_StaysWithinAnchorRadius()
        {
            List<EnemyAgent> group = raid.Enemies.Where(e => e.name.StartsWith(BossGroupPrefix)).ToList();
            Assert.AreEqual(4, group.Count, "보스 1 + 호위 3");
            Assert.AreEqual(1, group.Count(e => e.Archetype.id == "AI-BOS"));
            Assert.AreEqual(3, group.Count(e => e.Archetype.id == "AI-BGD"));
            yield return M3Scene.WaitUntil(() => group.All(e => e.Brain.HasAnchor), 5f, () => "거점 설정(첫 Tick)");
            float radius = group[0].Archetype.anchorRadiusM;
            Assert.AreEqual(25f, radius, 1e-4f, "거점 반경 25m");
            EnemyAgent bossAgent = Enemy(BossName);
            Assert.Less(Flat(bossAgent.transform.position - bossAgent.Brain.AnchorCenter), 1.5f, "보스 거점 = 보스 시작 위치");
            foreach (EnemyAgent e in group)
            {
                Assert.AreEqual(radius, e.Archetype.anchorRadiusM, 1e-4f, $"{e.name} 반경");
                Assert.Less(Flat(e.Brain.AnchorCenter - bossAgent.Brain.AnchorCenter), 1e-3f, $"{e.name} 거점 = 보스 거점 (분대 리더 공유, 7B 리뷰 6)");
            }

            Vector3 anchor = bossAgent.Brain.AnchorCenter;
            Vector3 near = FindOpenSpot(anchor, LureNearM, null);
            Vector3 away = (near - anchor);
            away.y = 0f;
            Vector3 far = FindOpenSpot(anchor, LureFarM, away.normalized);
            Assert.Greater(Flat(far - anchor), radius + 15f, "유인 지점 = 거점 밖");
            target = PlaceTarget(near);
            yield return M2Scene.WaitUntil(() => ReferenceEquals(raid.Director.Target, target), 3f);

            var maxDist = new Dictionary<EnemyAgent, float>();
            foreach (EnemyAgent e in group)
                maxDist[e] = 0f;

            // 1) 가까이(12m)에서 총성 → 무리가 경계/교전
            float next = 0f;
            float deadline = Time.time + 20f;
            while (!group.Any(e => e.State == EnemyState.Alert || e.State == EnemyState.Combat))
            {
                Assert.Less(Time.time, deadline, $"보스 무리 반응 없음: {States(group)}");
                next = Noise(near, next);
                Track(group, maxDist);
                yield return null;
            }

            // 2) 표적을 거점 밖 55m로 → 총성 반복 (LKP·교전 목적지가 거점 밖)
            target.MoveTo(far);
            next = 0f;
            int engagedFrames = 0, frames = 0;
            float end = Time.time + LureSeconds;
            while (Time.time < end)
            {
                next = Noise(far, next);
                yield return null;
                Track(group, maxDist);
                frames++;
                if (group.Any(e => e.State == EnemyState.Alert || e.State == EnemyState.Combat || e.State == EnemyState.Search))
                    engagedFrames++;
            }

            string report = string.Join(", ", group.Select(e => $"{e.name}({e.Archetype.id}) 최대 {maxDist[e]:0.0}m {e.State}"));
            Debug.Log($"[M7AiRaidFlowTests] 보스 무리 유인 {LureSeconds}초 (표적 {Flat(far - anchor):0.0}m): {report}, 반응 프레임 {engagedFrames}/{frames}");
            foreach (EnemyAgent e in group)
                Assert.LessOrEqual(maxDist[e], radius + AnchorToleranceM, $"{e.name} 거점 {radius}m 안 유지 ({report})");
            Assert.Greater(engagedFrames, frames / 2, $"유인 중 무리가 반응 상태 (유인이 실제로 걸렸다): {report}");
            Assert.Greater(group.Max(e => maxDist[e]), 10f, $"무리가 유인 쪽으로 움직였다 (거점이 막았다는 근거): {report}");
            Assert.IsTrue(group.All(e => !e.IsDead), "무적 표적은 쏘지 않는다 — 모두 생존");
        }

        // ── ③ 저격수 처치 → DMR 내구도 50~90 → 루팅·장착·사격 ──

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Sniper_Killed_DmrDurability50to90_LootedEquippedAndFires()
        {
            WeaponController weapon = raid.Motor.GetComponentInChildren<WeaponController>(true);
            Assert.IsNotNull(weapon, "플레이어 WeaponController");
            Assert.IsNull(raid.Inventory.GetEquipped(EquipSlot.Primary2), "Primary2 비어 있음 (레이드 씬 시작 장비)");

            using var killed = new EventLog<EnemyKilled>(raid.Events);
            var corpses = new List<(EnemyAgent sniper, EnemyKilled evt, WeaponItem dmr)>();
            foreach (string name in SniperNames)
            {
                EnemyAgent sniper = Enemy(name);
                Assert.AreEqual("AI-SNP", sniper.Archetype.id);
                Assert.AreEqual(new Vector2(50f, 90f), sniper.Archetype.weaponDurabilityRange, $"{name} 스폰 내구도 범위 (Q12)");
                Assert.AreEqual(DmrId, sniper.Weapon.Item.Definition.id, $"{name} = DMR");
                Assert.AreEqual(WeaponItem.MaxDurability, sniper.Weapon.Item.Durability, "살아 있는 적 무기 = 100 (시체가 될 때 굴림)");
                int before = killed.Count;
                Kill(sniper);
                yield return M3Scene.WaitUntil(() => killed.Count > before, 3f, () => $"{name} 사망");
                EnemyKilled evt = killed.Last;
                Assert.AreSame(sniper.gameObject, evt.Corpse);
                var dmr = evt.Items.OfType<WeaponItem>().SingleOrDefault(w => w.Definition.id == DmrId);
                Assert.IsNotNull(dmr, $"{name} 시체에 DMR ({string.Join(", ", evt.Items.Select(i => i.Definition.id))})");
                Assert.GreaterOrEqual(dmr.Durability, 50f, $"{name} DMR 내구도 ≥ 50");
                Assert.LessOrEqual(dmr.Durability, 90f, $"{name} DMR 내구도 ≤ 90");
                Assert.IsFalse(dmr.IsJammed);
                corpses.Add((sniper, evt, dmr));
            }
            Debug.Log("[M7AiRaidFlowTests] 저격수 시체 DMR 내구도: " + string.Join(", ", corpses.Select(c => $"{c.sniper.name} {c.dmr.Durability:0.00}")));

            // 첫 저격수 시체 루트 → 검색 끝 → DMR을 Primary2에 바로 장착 (탄창이 비었으면 예비 탄창도 가져와 R)
            (EnemyAgent first, EnemyKilled firstEvt, WeaponItem looted) = corpses[0];
            var container = first.GetComponent<LootContainer>();
            Assert.IsNotNull(container, "저격수 시체에 LootContainer");
            yield return OpenAndSearch(container);
            Assert.IsTrue(raid.Inventory.TryEquip(looted, EquipSlot.Primary2), "루트의 DMR → Primary2 장착");
            foreach (Magazine spare in firstEvt.Items.OfType<Magazine>().Where(m => m.Definition.ammo == looted.Definition.ammo && m.Rounds > 0))
                raid.Inventory.TryQuickMove(spare);
            // 루트를 열면 인벤토리 화면이 열린다(게임플레이 입력 차단) → Tab으로 닫는다
            Assert.AreEqual(ScreenKind.Inventory, raid.Presenter.Screens.Current, "루트 = 인벤토리 화면");
            input = new SimInput();
            yield return input.Keyboard.Tap(Key.Tab);
            yield return M3Scene.WaitUntil(() => raid.Presenter.Screens.Current == ScreenKind.None, 2f,
                () => $"Tab → 화면 닫힘 (현재 {raid.Presenter.Screens.Current})");
            raid.LookDown(-45f);   // 하늘로
            yield return input.Keyboard.Tap(Key.Digit2);
            yield return M3Scene.WaitUntil(() => weapon.ActiveSlot == EquipSlot.Primary2 && !weapon.IsSwitching && weapon.Model != null, 3f,
                () => $"2 → Primary2 (현재 {weapon.ActiveSlot}, 교체 중 {weapon.IsSwitching}, 화면 {raid.Presenter.Screens.Current})");
            Assert.AreSame(looted, weapon.Model.Item, "든 무기 = 시체에서 주운 DMR");
            if (looted.LoadedMagazine == null || looted.LoadedMagazine.IsEmpty)
            {
                Assert.IsTrue(weapon.Model.CanReload, "주운 예비 탄창으로 재장전 가능");
                yield return input.Keyboard.Tap(Key.R);
                yield return M2Scene.WaitUntil(() => !weapon.Model.IsReloading, 10f);
            }
            int rounds = looted.LoadedMagazine.Rounds;
            float durability = looted.Durability;
            Assert.Greater(rounds, 0, "장전된 탄");

            using var shots = new EventLog<ShotFired>(raid.Events);
            using var clicks = new EventLog<WeaponClicked>(raid.Events);
            float deadline = Time.realtimeSinceStartup + 6f;
            while (!shots.Received.Any(s => s.Source == SoundSource.Player))
            {
                Assert.Less(Time.realtimeSinceStartup, deadline, $"DMR 사격이 나가지 않았다 (클릭 {clicks.Count}, 고장 {looted.IsJammed})");
                yield return input.Mouse.ClickLeft();
            }
            ShotFired shot = shots.Received.First(s => s.Source == SoundSource.Player);
            Assert.AreSame(looted.Definition, shot.Weapon, "주운 DMR로 쐈다");
            Assert.AreEqual(rounds - 1, looted.LoadedMagazine.Rounds, "탄 1 소모");
            Assert.AreEqual(durability - looted.Definition.durabilityLossPerShot, looted.Durability, 1e-4f, "발당 마모");
            Assert.IsFalse(looted.IsJammed, "내구도 ≥ 50 = 고장 확률 0");
        }

        // ── ④ 변이체 무리 접근 → 울음 ──

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator MutantPack_ApproachesTarget_EnemyVocalizedAlertThenAttack()
        {
            List<EnemyAgent> pack = raid.Enemies.Where(e => e.name.StartsWith(MutantPackPrefix)).ToList();
            Assert.AreEqual(4, pack.Count, "MUT_REF 무리 4");
            Assert.IsTrue(pack.All(e => e.Archetype.id == "AI-MUT"));
            Vector3 center = Vector3.zero;
            foreach (EnemyAgent e in pack)
                center += e.transform.position;
            center /= pack.Count;

            var vocals = new List<(float time, EnemyVocalized evt)>();
            System.Action<EnemyVocalized> onVocal = e => vocals.Add((Time.time, e));
            raid.Events.Subscribe(onVocal);
            try
            {
                Vector3 spot = FindOpenSpot(center, 20f, null);
                target = PlaceTarget(spot);
                yield return M2Scene.WaitUntil(() => ReferenceEquals(raid.Director.Target, target), 3f);
                float start = Time.time;
                float next = 0f;
                float deadline = Time.time + 25f;
                float closest = float.MaxValue;
                while (Time.time < deadline)
                {
                    next = Noise(spot, next);
                    yield return null;
                    closest = Mathf.Min(closest, pack.Min(e => Flat(e.transform.position - spot)));
                    if (closest <= 3f && vocals.Any(v => v.evt.Kind == EnemyVocalKind.MutantAttack))
                        break;
                }
                // 7B 리뷰 15: 접근 뒤에도 몇 초 더 보며 최근접을 잰다 — 표적(발) 0m까지 겹치지 않고 사거리 × 0.75 근처에서 멈춘다
                float overlapWatchUntil = Time.time + 5f;
                float nearest = closest;
                while (Time.time < overlapWatchUntil)
                {
                    next = Noise(spot, next);
                    yield return null;
                    nearest = Mathf.Min(nearest, pack.Min(e => Flat(e.transform.position - spot)));
                }
                string summary = $"울음 {vocals.Count}개 [{string.Join(", ", vocals.Select(v => $"{v.evt.Kind}@{v.time - start:0.0}s"))}], 최근접 {closest:0.0}m(관찰 끝 {nearest:0.00}m), {States(pack)}";
                Debug.Log($"[M7AiRaidFlowTests] 변이체 무리: {summary}");

                var alerts = vocals.Where(v => v.evt.Kind == EnemyVocalKind.MutantAlert).ToList();
                Assert.Greater(alerts.Count, 0, $"MutantAlert 울음 ({summary})");
                foreach (var (_, evt) in vocals)
                    Assert.IsTrue(pack.Any(e => Flat(e.transform.position - evt.Position) < 30f),
                        $"울음 위치 {evt.Position}가 무리 대원 근처");
                Assert.Less(Flat(alerts[0].evt.Position - center), 15f, "첫 경계 울음 = 무리 위치에서 (접근 전)");
                Assert.LessOrEqual(closest, 3f, $"무리가 표적에 접근 ({summary})");
                Assert.GreaterOrEqual(nearest, 1.0f, $"무리가 표적과 겹치지 않음 ({summary})");
                Assert.IsTrue(vocals.Any(v => v.evt.Kind == EnemyVocalKind.MutantAttack), $"근접 준비 MutantAttack 울음 ({summary})");
                Assert.IsTrue(pack.Any(e => e.State == EnemyState.Combat), $"무리 교전 ({summary})");
                Assert.Greater(target.HitCount, 0, "근접 타격이 표적(IDamageable)에 들어감");
            }
            finally
            {
                raid.Events.Unsubscribe(onVocal);
            }
        }

        // ── 도우미 ──

        private EnemyAgent Enemy(string name)
        {
            EnemyAgent e = raid.Enemies.FirstOrDefault(a => a != null && a.name == name);
            Assert.IsNotNull(e, $"{name} 없음 (Ashfall/Setup/Apply M7 Content)");
            return e;
        }

        private static void Kill(EnemyAgent enemy)
        {
            Vector3 point = enemy.transform.position + Vector3.up * 1.2f;   // 몸통
            Vector3 dir = enemy.transform.forward;
            enemy.ApplyDamage(new DamageInfo(1e5f, 0, point, dir, 10f));
            Assert.IsTrue(enemy.IsDead, $"{enemy.name} 사망");
        }

        private IEnumerator OpenAndSearch(LootContainer container)
        {
            container.Interact(raid.InventoryBehaviour);
            ILootView loot = raid.Inventory.ActiveLoot;
            Assert.IsNotNull(loot, "시체 루트 열림");
            yield return M3Scene.WaitUntil(() => loot.SearchComplete, 15f, () => "검색 완료");
        }

        private TestTarget PlaceTarget(Vector3 at)
        {
            TestTarget t = TestTarget.Create(at);
            raid.Gm.Services.Unregister<IPlayerTarget>(raid.Motor);
            t.Register();
            return t;
        }

        // center에서 수평 distance 떨어진 NavMesh 위 빈 자리 (preferred 방향 우선, 없으면 30°씩)
        private static Vector3 FindOpenSpot(Vector3 center, float distance, Vector3? preferred)
        {
            var dirs = new List<Vector3>();
            if (preferred.HasValue && preferred.Value.sqrMagnitude > 1e-4f)
                dirs.Add(preferred.Value.normalized);
            for (int i = 0; i < 12; i++)
            {
                float a = i * 30f * Mathf.Deg2Rad;
                dirs.Add(new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)));
            }
            foreach (Vector3 d in dirs)
            {
                Vector3 p = center + d * distance;
                if (!NavMesh.SamplePosition(p + Vector3.up * 2f, out NavMeshHit hit, 6f, NavMesh.AllAreas))
                    continue;
                if (Mathf.Abs(Flat(hit.position - center) - distance) > 5f)
                    continue;
                if (Physics.CheckCapsule(hit.position + Vector3.up * 0.4f, hit.position + Vector3.up * 1.5f, 0.3f,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    continue;
                return hit.position;
            }
            Assert.Fail($"{center} 주변 {distance}m 빈 자리 없음");
            return center;
        }

        private float Noise(Vector3 at, float next)
        {
            if (Time.time < next)
                return next;
            raid.Events.Publish(new NoiseEmitted(at + Vector3.up * 1.4f, 120f, NoiseKind.Gunshot));
            return Time.time + NoiseIntervalSec;
        }

        private static void Track(List<EnemyAgent> group, Dictionary<EnemyAgent, float> maxDist)
        {
            foreach (EnemyAgent e in group)
                maxDist[e] = Mathf.Max(maxDist[e], Flat(e.transform.position - e.Brain.AnchorCenter));
        }

        private static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

        private static string States(IEnumerable<EnemyAgent> enemies) =>
            string.Join(", ", enemies.Select(e => $"{e.name}:{e.State}@{new Vector2(e.transform.position.x, e.transform.position.z)}"));
    }
}
#endif
