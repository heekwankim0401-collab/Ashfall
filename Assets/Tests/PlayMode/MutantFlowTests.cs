#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Ashfall.AI;
using Ashfall.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using BodyPart = Ashfall.Core.BodyPart;

namespace Ashfall.Tests.PlayMode
{
    // M7 7B-2b — AI-MUT 변이체 무리 (§7 "무리 행동, 근접", M7 설계 §1.4, Q24~Q26).
    // M3_AI 씬(NavMesh·AiDirector)을 쓰되 기존 적은 모두 지우고, 기존 적 하나를 틀로 복제해 AI-MUT 에셋 복제본을 넣은 변이체를 코드로 만든다.
    // 표적 = 이 파일의 MortalTarget(IPlayerTarget + IDamageable, 피해 기록, 사망 전환 가능). 플레이어는 표적 등록을 풀고 구석으로 치운다.
    public class MutantFlowTests
    {
        private const string MutantArchetypePath = "Assets/_Project/Data/AI/AI-MUT.asset";
        private const int PackSquadId = 90;
        private static readonly Vector3 TargetSpot = new Vector3(M3Scene.OpenLaneX, 0f, 10f);

        private M3Scene scene;
        private EnemyArchetypeSO archetype;
        private MortalTarget target;
        private readonly List<EnemyAgent> pack = new List<EnemyAgent>();
        private readonly List<(float time, EnemyVocalized evt)> vocals = new List<(float, EnemyVocalized)>(256);
        private Action<EnemyVocalized> vocalHandler;
        private SimInput input;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            scene = new M3Scene();
            yield return M3Scene.Load(scene);

            var asset = AssetDatabase.LoadAssetAtPath<EnemyArchetypeSO>(MutantArchetypePath);
            Assert.IsNotNull(asset, "AI-MUT 에셋 없음 (Ashfall/Setup/Create M7 AI Data)");
            archetype = Object.Instantiate(asset);   // 원본 에셋은 건드리지 않는다
            Assert.AreEqual(EnemyTacticsKind.Mutant, archetype.tactics);
            Assert.Greater(archetype.meleeDamage, 0f);

            vocals.Clear();
            vocalHandler = e => vocals.Add((Time.time, e));
            scene.Events.Subscribe(vocalHandler);

            target = MortalTarget.Create(TargetSpot);
            scene.Gm.Services.Unregister<IPlayerTarget>(scene.Motor);
            scene.TeleportPlayer(M3Scene.FarCorner);
            target.Register();
            AiDirector director = scene.Director;
            MortalTarget t = target;
            yield return M2Scene.WaitUntil(() => ReferenceEquals(director.Target, t), 3f);
        }

        [TearDown]
        public void TearDown()
        {
            input?.Dispose();
            input = null;
            if (vocalHandler != null && GameManager.Instance != null)
                GameManager.Instance.Events.Unsubscribe(vocalHandler);
            vocalHandler = null;
            if (target != null)
                Object.Destroy(target.gameObject);
            target = null;
            if (archetype != null)
                Object.Destroy(archetype);
            archetype = null;
            pack.Clear();
        }

        // 기존 적 하나를 틀로 변이체 count명을 만들고(같은 분대, 경로 없음) 기존 적은 모두 지운다.
        private IEnumerator SpawnPack(Vector3[] positions, float[] yaws)
        {
            pack.Clear();
            GameObject source = scene.Enemies[0].gameObject;
            source.SetActive(false);   // 복제본도 꺼진 채로 생긴다 → 필드를 바꾼 뒤 켠다(Awake가 새 아키타입을 읽음)
            for (int k = 0; k < positions.Length; k++)
            {
                GameObject go = Object.Instantiate(source, positions[k], Quaternion.Euler(0f, yaws[k], 0f));
                go.name = "Enemy_MUT_" + (k + 1);
                var agent = go.GetComponent<EnemyAgent>();
                var so = new SerializedObject(agent);
                so.FindProperty("archetype").objectReferenceValue = archetype;
                so.FindProperty("patrolRoute").objectReferenceValue = null;
                so.FindProperty("squadId").intValue = PackSquadId;
                so.FindProperty("loadoutIndex").intValue = -1;
                so.FindProperty("seed").intValue = 700 + k;
                so.ApplyModifiedPropertiesWithoutUndo();
                pack.Add(agent);
            }
            foreach (EnemyAgent e in scene.Enemies)
                if (e != null)
                    Object.Destroy(e.gameObject);
            scene.Enemies.Clear();
            yield return null;

            foreach (EnemyAgent agent in pack)
                agent.gameObject.SetActive(true);
            scene.Enemies.AddRange(pack);
            AiDirector director = scene.Director;
            yield return M3Scene.WaitUntil(() => pack.All(a => a.Brain != null) && director.Agents.Count == pack.Count, 3f,
                () => $"등록 {director.Agents.Count}/{pack.Count}");
            Assert.IsTrue(director.TryGetSquad(PackSquadId, out Squad squad));
            Assert.AreEqual(pack.Count, squad.Count, "한 분대");
            foreach (EnemyAgent a in pack)
            {
                Assert.AreEqual(EnemyTacticsKind.Mutant, a.Brain.TacticsKind);
                Assert.IsNull(a.Weapon, "무장 없음");
            }
        }

        // 1명: 표적 쪽(-z)을 보며 12m 앞
        private IEnumerator SpawnSingle() =>
            SpawnPack(new[] { TargetSpot + new Vector3(0f, 0f, 12f) }, new[] { 180f });

        // 4명: 첫째만 표적 쪽(-z)을 보고, 나머지 셋은 등을 돌린다(+z) — 표적은 그들의 시야 콘(140°) 밖
        private IEnumerator SpawnPack4() =>
            SpawnPack(new[]
            {
                new Vector3(M3Scene.OpenLaneX - 4f, 0f, 30f),
                new Vector3(M3Scene.OpenLaneX - 1f, 0f, 32f),
                new Vector3(M3Scene.OpenLaneX + 2f, 0f, 32f),
                new Vector3(M3Scene.OpenLaneX + 5f, 0f, 32f),
            }, new[] { 180f, 0f, 0f, 0f });

        private string Describe() =>
            string.Join(" | ", pack.Select(a => $"{a.name}:{a.State} {a.transform.position} 의도 {a.Brain.Intent.Move}" +
                                               $"{(a.Brain.Intent.HasDestination ? "→" + a.Brain.Intent.Destination : "")}")) +
            $" | 표적 피해 {target.Hits.Count}";

        // ── 무리: 한 명 발견 = 전원 Combat, 전원이 표적을 친다 ──

        [UnityTest]
        public IEnumerator Pack4_OneSees_AllCombat_StrikeTarget_Vocalize()
        {
            yield return SpawnPack4();
            var logs = pack.Select(M3Scene.RecordStates).ToList();

            yield return M3Scene.WaitUntil(() => pack.All(a => a.State == EnemyState.Combat), 10f, Describe);

            // 처음 깨어난 것은 표적을 보는 첫째, 나머지는 분대 연락(T4)으로 Alert
            float firstWake = logs[0].First(l => l.from == EnemyState.Idle).time;
            for (int k = 1; k < 4; k++)
            {
                var wake = logs[k].First(l => l.from == EnemyState.Idle);
                Assert.AreEqual(EnemyState.Alert, wake.to, $"{pack[k].name}: 분대 연락 → Alert ({M3Scene.Describe(logs[k])})");
                Assert.GreaterOrEqual(wake.time, firstWake, $"{pack[k].name}: 첫째보다 먼저 깨지 않음");
            }

            // 경계 울음 = 분대에서 한 번 (7B 리뷰 10: 무리 4명 동시 재생 대신 먼저 경계에 든 한 명만, Squad.TryClaimAlertVocal)
            int alerts = vocals.Count(v => v.evt.Kind == EnemyVocalKind.MutantAlert);
            Assert.AreEqual(1, alerts, "MutantAlert = 분대당 1회");
            Assert.IsTrue(pack.Any(a => Vector3.Distance(M3Scene.EnemyEye(a).position, vocals.First(v => v.evt.Kind == EnemyVocalKind.MutantAlert).evt.Position) < 3f),
                "울음 위치 = 무리 대원 눈");

            yield return M3Scene.WaitUntil(() => target.Hits.Count >= 4, 12f, Describe);
            foreach (MortalTarget.Hit h in target.Hits)
            {
                Assert.AreEqual(archetype.meleeDamage, h.Amount, 1e-4f, "근접 피해 = meleeDamage");
                Assert.AreEqual(0, h.Penetration, "관통 0");
                Assert.Less(h.DistanceM, archetype.meleeRangeM + 1.5f, "눈 → 지점 거리 = 근접");
                Assert.IsTrue(h.HasPartHint, "부위 힌트");
                Assert.That(h.Part, Is.Not.EqualTo(BodyPart.Head).And.Not.EqualTo(BodyPart.LeftLeg).And.Not.EqualTo(BodyPart.RightLeg), "흉부·복부·팔");
            }
            Assert.Greater(vocals.Count(v => v.evt.Kind == EnemyVocalKind.MutantAttack), 0, "공격 울음");

            // 둘레로 흩어져 붙는다: 표적 사거리 안에 둘 이상이면 서로 겹치지 않음
            var close = pack.Where(a => Flat(a.transform.position, TargetSpot) <= archetype.meleeRangeM + 0.3f).ToList();
            for (int p = 0; p < close.Count; p++)
                for (int q = p + 1; q < close.Count; q++)
                    Assert.Greater(Flat(close[p].transform.position, close[q].transform.position), 0.3f, "겹치지 않음");

            // 7B 리뷰 15: 계속 붙어 쳐도 표적 몸 안으로 들어가지 않는다 (사거리 × 0.75 ≈ 1.35m에서 멈춤, 최근접 ≥ 1.0m)
            float nearest = pack.Min(a => Flat(a.transform.position, TargetSpot));
            float until = Time.time + 3f;
            while (Time.time < until)
            {
                yield return null;
                nearest = Mathf.Min(nearest, pack.Min(a => Flat(a.transform.position, TargetSpot)));
            }
            Assert.GreaterOrEqual(nearest, 1.0f, $"표적과 겹치지 않음 (최근접 {nearest:0.00}m) {Describe()}");
        }

        // ── 7B 리뷰 1: 실제 플레이어(선·웅크림·엎드림)를 쳐도 머리·다리 피해 0 (부위 힌트 → PlayerHealth) ──

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator RealPlayer_StandCrouchProne_StrikesOnlyThoraxStomachArms_NoHeadNoLegs()
        {
            // 표적을 실제 플레이어로 바꾼다. 상태이상 굴림 최소 피해(10)보다 작게 해서 HP 변화 = 타격 부위만(출혈 없음)
            const float lowDamage = 5f;
            archetype.meleeDamage = lowDamage;
            Object.Destroy(target.gameObject);
            target = null;
            yield return null;
            Vector3 spot = new Vector3(M3Scene.OpenLaneX, 0.05f, 10f);
            scene.TeleportPlayer(spot, spot + Vector3.forward);
            scene.Gm.Services.Register<IPlayerTarget>(scene.Motor);
            AiDirector director = scene.Director;
            yield return M2Scene.WaitUntil(() => ReferenceEquals(director.Target, scene.Motor), 3f);

            yield return SpawnPack(new[] { spot + new Vector3(0f, -0.05f, 4f) }, new[] { 180f });
            EnemyAgent mut = pack[0];
            input = new SimInput();
            var body = scene.PlayerBody;
            BodyPart[] forbidden = { BodyPart.Head, BodyPart.LeftLeg, BodyPart.RightLeg };
            BodyPart[] allowed = { BodyPart.Thorax, BodyPart.Stomach, BodyPart.LeftArm, BodyPart.RightArm };
            var forbiddenHp = forbidden.Select(p => body.GetHp(p)).ToArray();

            float Allowed() => allowed.Sum(p => body.GetHp(p));
            string State() => $"{mut.State} 자세 {scene.Motor.Posture} 거리 {Flat(mut.transform.position, scene.Motor.transform.position):0.00}m " +
                              string.Join(", ", System.Enum.GetValues(typeof(BodyPart)).Cast<BodyPart>().Select(p => $"{p} {body.GetHp(p):0.#}"));

            var postures = new[] { TargetPosture.Standing, TargetPosture.Crouching, TargetPosture.Prone };
            const int strikesPerPosture = 3;
            float nearest = float.MaxValue;
            foreach (TargetPosture posture in postures)
            {
                if (posture == TargetPosture.Crouching)
                    yield return input.Keyboard.Tap(UnityEngine.InputSystem.Key.C);
                else if (posture == TargetPosture.Prone)
                    yield return input.Keyboard.Tap(UnityEngine.InputSystem.Key.Z);
                yield return M3Scene.WaitUntil(() => scene.Motor.Posture == posture, 3f, State);

                float before = Allowed();
                float deadline = Time.time + 15f;
                while (before - Allowed() < strikesPerPosture * lowDamage - 1e-3f)
                {
                    Assert.Less(Time.time, deadline, $"{posture}: 타격 {strikesPerPosture}회 안 됨 ({State()})");
                    Assert.IsFalse(body.IsDead, State());
                    yield return null;
                    nearest = Mathf.Min(nearest, Flat(mut.transform.position, scene.Motor.transform.position));
                    for (int k = 0; k < forbidden.Length; k++)
                        Assert.AreEqual(forbiddenHp[k], body.GetHp(forbidden[k]), 1e-4f, $"{posture}: {forbidden[k]} 피해 ({State()})");
                }
                float dealt = before - Allowed();
                Assert.AreEqual(Mathf.Round(dealt / lowDamage), dealt / lowDamage, 0.01f, $"{posture}: 피해 = 타격 수 × {lowDamage} ({dealt})");
                Debug.Log($"[MutantFlowTests] 실제 플레이어 {posture}: 흉부·복부·팔 피해 {dealt} — {State()}");
            }
            Assert.AreEqual(TargetPosture.Prone, scene.Motor.Posture);
            Assert.GreaterOrEqual(nearest, 1.0f, $"플레이어와 겹치지 않음 (최근접 {nearest:0.00}m)");
        }

        // ── 1명: 간격·울음 쿨다운 ──

        [UnityTest]
        public IEnumerator Single_StrikesAtInterval_AttackVocalCooldown()
        {
            yield return SpawnSingle();
            EnemyAgent mut = pack[0];
            yield return M3Scene.WaitUntil(() => target.Hits.Count >= 5, 15f, Describe);

            Assert.AreEqual(EnemyState.Combat, mut.State);
            for (int k = 1; k < target.Hits.Count; k++)
            {
                float gap = target.Hits[k].Time - target.Hits[k - 1].Time;
                Assert.That(gap, Is.InRange(archetype.meleeIntervalSec - 0.05f, archetype.meleeIntervalSec + 0.35f),
                    $"타격 {k} 간격 {gap:0.000}");
            }
            Assert.LessOrEqual(Flat(mut.transform.position, TargetSpot), archetype.meleeRangeM + 0.05f, "사거리 안에서 친다");

            Assert.AreEqual(EnemyVocalKind.MutantAlert, vocals[0].evt.Kind, "첫 울음 = 경계");
            Assert.IsTrue(vocals.Any(v => v.evt.Kind == EnemyVocalKind.MutantAttack), "공격 울음");
            for (int k = 1; k < vocals.Count; k++)
                Assert.GreaterOrEqual(vocals[k].time - vocals[k - 1].time, scene.Director.Config.vocalCooldownSec - 0.05f,
                    "같은 변이체 울음 쿨다운");
            Transform eye = M3Scene.EnemyEye(mut);
            Assert.Less(Vector3.Distance(vocals[vocals.Count - 1].evt.Position, eye.position), 3f, "울음 위치 = 눈");
        }

        // ── 표적 사망 후 공격 없음 ──

        [UnityTest]
        public IEnumerator TargetDies_NoMoreStrikes()
        {
            yield return SpawnSingle();
            EnemyAgent mut = pack[0];
            yield return M3Scene.WaitUntil(() => target.Hits.Count >= 1, 10f, Describe);
            // 다음 준비가 걸리도록 조금 더 기다린 뒤 사망 (준비 중 사망 = 헛침)
            yield return M2Scene.WaitSeconds(0.9f);
            target.Dead = true;
            int hits = target.Hits.Count;

            yield return M2Scene.WaitSeconds(3f);
            Assert.AreEqual(hits, target.Hits.Count, "표적 사망 후 타격 없음");
            Assert.That(mut.State, Is.EqualTo(EnemyState.Idle).Or.EqualTo(EnemyState.Patrol), "T12: 표적 없음 → 평시");
            Assert.IsFalse(mut.IsDead);
        }

        // ── GC 0: 무리 교전(돌진·타격·울음) 중 AiDirector.Update 경로 ──

        [UnityTest]
        public IEnumerator Pack4_Engaged_DirectorUpdate_NoGcAlloc()
        {
            yield return SpawnPack4();
            yield return M3Scene.WaitUntil(() => pack.All(a => a.State == EnemyState.Combat) && target.Hits.Count >= 1, 15f, Describe);

            int frames = 0, samples = 0, noisy = 0;
            int hitsBefore = target.Hits.Count;
            int measured = 0;
            // 배치모드는 프레임이 매우 짧다 → 프레임 수(120)와 게임 시간(타격 간격 1.1초의 2배 이상)을 둘 다 채운다
            yield return MeasureDirectorGc(scene.Director, 120, archetype.meleeIntervalSec * 2.5f, (total, f, s, n) =>
            {
                measured = total;
                frames = f;
                samples = s;
                noisy = n;
            });
            Debug.Log($"[MutantFlowTests] GC: {measured}프레임 중 {frames}프레임, {samples}회 (잡음 제외 {noisy}), 측정 중 타격 {target.Hits.Count - hitsBefore}");
            Assert.Greater(target.Hits.Count, hitsBefore, "측정 중에도 타격");
            Assert.AreEqual(0, samples, "AiDirector.Update 경로 GC 할당");
        }

        private static float Flat(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        // AiPerformanceTests와 같은 방식(Recorder "GC.Alloc", 현재 스레드)으로 director.Update만 감싼다.
        private static IEnumerator MeasureDirectorGc(Ashfall.AI.AiDirector director, int minFrames, float minSeconds,
                                                     Action<int, int, int, int> result)
        {
            MethodInfo method = typeof(AiDirector).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method);
            var update = (Action)Delegate.CreateDelegate(typeof(Action), director, method);
            director.enabled = false;   // 엔진 Update 대신 직접 부른다

            Recorder recorder = Recorder.Get("GC.Alloc");
            Action empty = () => { };
            Sample(recorder, empty);
            Sample(recorder, update);   // 첫 호출(JIT) 제외

            int frames = 0, samples = 0, noisy = 0, total = 0;
            try
            {
                float until = Time.time + minSeconds;
                for (total = 0; total < minFrames || Time.time < until; total++)
                {
                    yield return null;
                    int n = Sample(recorder, update);
                    if (n > 0)
                    {
                        if (Sample(recorder, empty) > 0)
                        {
                            noisy++;
                            continue;
                        }
                        frames++;
                        samples += n;
                    }
                }
            }
            finally
            {
                director.enabled = true;
            }
            result(total, frames, samples, noisy);
        }

        private static int Sample(Recorder recorder, Action action)
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
    }

    // 죽을 수 있는 표적 (변이체 근접 확인용): IPlayerTarget + IDamageable(피해 기록, 할당 없음). 루트 = 자기 자신. CharacterController 없음
    // → 근접 피해는 부위 힌트(DamageInfo.HasPartHint)로 오고 지점 = 표적 중심이다(7B 리뷰 1).
    internal sealed class MortalTarget : MonoBehaviour, IPlayerTarget, IDamageable
    {
        public struct Hit
        {
            public float Time;
            public float Amount;
            public int Penetration;
            public float DistanceM;
            public Vector3 Point;
            public bool HasPartHint;
            public BodyPart Part;
        }

        private const float Height = 1.8f;
        private const float EyeBelowTop = 0.12f;

        private bool registered;

        public readonly List<Hit> Hits = new List<Hit>(512);
        public bool Dead { get; set; }

        public Transform Root => transform.root;
        public Vector3 Position => transform.position;
        public Vector3 EyePosition => transform.position + Vector3.up * (Height - EyeBelowTop);
        public Vector3 CenterPosition => transform.position + Vector3.up * (Height * 0.5f);
        public Vector3 Velocity => Vector3.zero;
        public TargetPosture Posture => TargetPosture.Standing;
        public bool IsDead => Dead;

        public static MortalTarget Create(Vector3 position)
        {
            var go = new GameObject("MortalTarget");
            go.transform.position = position;
            var capsule = go.AddComponent<CapsuleCollider>();
            capsule.radius = 0.3f;
            capsule.height = Height;
            capsule.center = new Vector3(0f, Height * 0.5f, 0f);
            var body = go.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            Physics.SyncTransforms();
            return go.AddComponent<MortalTarget>();
        }

        public void Register()
        {
            if (GameManager.Instance == null)
                return;
            GameManager.Instance.Services.Register<IPlayerTarget>(this);
            registered = true;
        }

        // 사망 뒤에 온 피해도 기록한다 (사망 후 공격 없음 확인)
        public void ApplyDamage(in DamageInfo info)
        {
            Hits.Add(new Hit
            {
                Time = UnityEngine.Time.time, Amount = info.Amount, Penetration = info.Penetration,
                DistanceM = info.DistanceM, Point = info.Point, HasPartHint = info.HasPartHint, Part = info.PartHint,
            });
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
