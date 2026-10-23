#if UNITY_EDITOR
using System;
using System.Collections;
using System.Reflection;
using Ashfall.Core;
using Ashfall.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ashfall.Tests.PlayMode
{
    // M7 7C-2a 생존 흐름 (§4.3 STAT-DEHY·HUNG·RAD, §8.3 방독면, M7 설계 §1.5, Q32~Q38·Q42). M2 씬(플레이어·실제 인벤토리)에
    // 가짜 IAshStormInfo·IRadiationField를 등록한다. 큰 시간은 모델에 직접 큰 dt를 준다(SurvivalModel.Tick), 률은 가짜 서비스 값으로.
    public class SurvivalFlowTests
    {
        private M2Scene scene;
        private FakeStorm storm;
        private FakeField field;
        private readonly System.Collections.Generic.List<Object> created = new System.Collections.Generic.List<Object>();

        private PlayerHealth Health => scene.Health;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            scene = new M2Scene();
            yield return M2Scene.Load(scene);
            Assert.IsNotNull(Health.Survival, "SurvivalModel 없음");
            Assert.IsNull(scene.Inventory.GetEquipped(EquipSlot.Helmet), "M2 씬은 헬멧 없이 시작");
            storm = new FakeStorm();
            field = new FakeField();
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = 0f;
            if (scene?.Gm != null)
            {
                scene.Gm.Services.Unregister<IAshStormInfo>(storm);
                scene.Gm.Services.Unregister<IRadiationField>(field);
            }
            foreach (Object o in created)
                if (o != null)
                    Object.Destroy(o);
            created.Clear();
        }

        private void RegisterFakes(float stormRate, float zoneRate)
        {
            storm.Rate = stormRate;
            field.Rate = zoneRate;
            scene.Gm.Services.Register<IAshStormInfo>(storm);
            scene.Gm.Services.Register<IRadiationField>(field);
        }

        private ArmorDefinitionSO GasMaskDefinition()
        {
            var mask = ScriptableObject.CreateInstance<ArmorDefinitionSO>();
            mask.id = "TEST-GSM"; mask.displayName = "방독면(테스트)"; mask.size = new Vector2Int(2, 2); mask.weightKg = 0.8f;
            mask.kind = ArmorKind.Helmet; mask.armorClass = 0; mask.covers = new BodyPart[0]; mask.blocksStormRadiation = true;
            created.Add(mask);
            return mask;
        }

        private MedicalDefinitionSO Systemic(string id, float hydration = 0f, float energy = 0f, bool curesRadiation = false)
        {
            var m = ScriptableObject.CreateInstance<MedicalDefinitionSO>();
            m.id = id; m.displayName = id; m.size = Vector2Int.one; m.weightKg = 0.1f;
            m.cures = new StatusEffect[0]; m.healAmount = 0f; m.useTime = 0.2f; m.uses = 1;
            m.hydrationRestore = hydration; m.energyRestore = energy; m.curesRadiation = curesRadiation;
            created.Add(m);
            return m;
        }

        private IBodyStatusView View()
        {
            Assert.IsTrue(scene.Gm.Services.TryGet(out IBodyStatusView view), "IBodyStatusView 미등록");
            return view;
        }

        // 프레임마다 쌓인 deltaTime 동안 선량 증가를 잰다 → 초당
        private IEnumerator MeasureDoseRate(int frames, Action<float> result)
        {
            yield return null;
            float dose0 = Health.RadiationDose;
            float elapsed = 0f;
            for (int i = 0; i < frames; i++)
            {
                yield return null;
                elapsed += Time.deltaTime;
            }
            result((Health.RadiationDose - dose0) / elapsed);
        }

        [UnityTest]
        public IEnumerator GasMask_BlocksStormRate_ZoneRateUnchanged()
        {
            Time.captureDeltaTime = 1f / 30f;
            RegisterFakes(1f, 0.5f);
            float rate = 0f;
            yield return MeasureDoseRate(30, r => rate = r);
            Assert.IsFalse(Health.GasMaskWorn);
            Assert.AreEqual(1.5f, Health.LastRadiationRatePerSec, 1e-5f, "폭풍 + 구역");
            Assert.AreEqual(1.5f, rate, 0.05f, "선량 증가율");

            var mask = ItemFactory.Create(GasMaskDefinition());
            Assert.IsTrue(scene.Inventory.TryEquipNew(mask, EquipSlot.Helmet), "방독면은 헬멧 슬롯");
            yield return MeasureDoseRate(30, r => rate = r);
            Assert.IsTrue(Health.GasMaskWorn, "헬멧 슬롯 정의의 blocksStormRadiation");
            Assert.AreEqual(0.5f, Health.LastRadiationRatePerSec, 1e-5f, "방독면 = 폭풍 률 0, 구역 률 그대로 (Q38)");
            Assert.AreEqual(0.5f, rate, 0.05f);

            field.Rate = 0f;
            yield return MeasureDoseRate(10, r => rate = r);
            Assert.AreEqual(0f, rate, 1e-6f, "방독면 + 구역 밖 = 0");
        }

        [UnityTest]
        public IEnumerator OrdinaryHelmet_DoesNotBlockStorm()
        {
            RegisterFakes(1f, 0f);
            var helmetDef = ScriptableObject.CreateInstance<ArmorDefinitionSO>();
            helmetDef.id = "TEST-HLM"; helmetDef.size = new Vector2Int(2, 2); helmetDef.kind = ArmorKind.Helmet; helmetDef.armorClass = 3;
            created.Add(helmetDef);
            Assert.IsTrue(scene.Inventory.TryEquipNew(ItemFactory.Create(helmetDef), EquipSlot.Helmet));
            yield return null;
            yield return null;
            Assert.IsFalse(Health.GasMaskWorn);
            Assert.AreEqual(1f, Health.LastRadiationRatePerSec, 1e-6f);
        }

        [UnityTest]
        public IEnumerator Dose100_ReducesMaxHp_TierEvent_AntidoteRestores()
        {
            IBodyStatusView view = View();
            using var status = new EventLog<PlayerBodyStatusChanged>(scene.Events);
            RegisterFakes(0f, 400f);   // 몇 프레임 안에 100을 넘긴다
            yield return M2Scene.WaitUntil(() => Health.Radiation.IsSick(100f), 5f);
            scene.Gm.Services.Unregister<IRadiationField>(field);
            yield return null;

            Assert.AreEqual(SurvivalTier.Critical, view.GetSurvivalTier(SurvivalStat.Radiation), "피폭 (≥ 100)");
            Assert.GreaterOrEqual(status.Count, 1, "등급 변화 = PlayerBodyStatusChanged");
            Assert.AreEqual(0.8f, Health.Body.MaxHpMultiplier, 1e-6f);
            Assert.AreEqual(80f * 0.8f, scene.Body.GetMaxHp(BodyPart.Thorax), 1e-3f, "흉부 80 × 0.8 (§4.2·§4.3)");
            Assert.AreEqual(35f * 0.8f, scene.Body.GetMaxHp(BodyPart.Head), 1e-3f, "머리 35 × 0.8");
            foreach (BodyPart part in Enum.GetValues(typeof(BodyPart)))
            {
                Assert.LessOrEqual(scene.Body.GetHp(part), scene.Body.GetMaxHp(part) + 1e-4f, $"{part} 현재 HP ≤ 새 최대");
            }
            float thorax = scene.Body.GetHp(BodyPart.Thorax);

            // 해독제: 실제 인벤토리 사용 경로 (MedicalUseRequested → Started → Completed)
            var antidote = new MedicalItem(Systemic("TEST-ANT", curesRadiation: true));
            Assert.IsTrue(scene.Inventory.TryAutoStore(antidote));
            using var completed = new EventLog<MedicalUseCompleted>(scene.Events);
            using var rejected = new EventLog<MedicalUseRejected>(scene.Events);
            Assert.IsTrue(scene.Inventory.TryUse(antidote));
            yield return M2Scene.WaitUntil(() => completed.Count > 0 || rejected.Count > 0, 3f);
            Assert.AreEqual(0, rejected.Count, "선량 > 0이면 해독제가 필요하다");
            Assert.AreEqual(BodyPart.Thorax, completed.Last.Target, "전신 표시 = 흉부");
            Assert.AreEqual(0f, Health.RadiationDose, "선량 0 (Q36)");
            Assert.AreEqual(1f, Health.Body.MaxHpMultiplier);
            Assert.AreEqual(thorax, scene.Body.GetHp(BodyPart.Thorax), 1e-4f, "HP 회복 없음");
            Assert.AreEqual(SurvivalTier.Normal, view.GetSurvivalTier(SurvivalStat.Radiation));
        }

        [UnityTest]
        public IEnumerator Dehydrated_DamagesPartsExceptHead_TierEvent()
        {
            IBodyStatusView view = View();
            using var status = new EventLog<PlayerBodyStatusChanged>(scene.Events);
            Health.Survival.Tick(1000f, false);   // 수분 0 (1000초를 한 번에)
            Assert.IsTrue(Health.Survival.IsDehydrated);
            Assert.AreEqual(SurvivalTier.Critical, view.GetSurvivalTier(SurvivalStat.Hydration), "탈수");
            Assert.GreaterOrEqual(status.Count, 1, "등급 변화 = PlayerBodyStatusChanged");

            var before = new float[Enum.GetValues(typeof(BodyPart)).Length];
            foreach (BodyPart part in Enum.GetValues(typeof(BodyPart)))
                before[(int)part] = scene.Body.GetHp(part);
            Time.captureDeltaTime = 1f / 30f;
            yield return M2Scene.WaitSeconds(1f);
            foreach (BodyPart part in Enum.GetValues(typeof(BodyPart)))
            {
                float lost = before[(int)part] - scene.Body.GetHp(part);
                if (part == BodyPart.Head)
                    Assert.AreEqual(0f, lost, 1e-4f, "머리는 탈수 피해 없음 (Q33, 리뷰 7C 1)");
                else
                    Assert.That(lost, Is.InRange(0.4f, 0.65f), $"{part}: 약 0.5 HP/s (머리 제외 부위마다)");
            }

            // 물로 회복하면 멈춘다
            var water = new MedicalItem(Systemic("TEST-WTR", hydration: 50f));
            Assert.IsTrue(scene.Inventory.TryAutoStore(water));
            using var completed = new EventLog<MedicalUseCompleted>(scene.Events);
            Assert.IsTrue(scene.Inventory.TryUse(water));
            yield return M2Scene.WaitUntil(() => completed.Count > 0, 3f);
            Assert.IsFalse(Health.Survival.IsDehydrated);
            float thorax = scene.Body.GetHp(BodyPart.Thorax);
            yield return M2Scene.WaitSeconds(0.5f);
            Assert.AreEqual(thorax, scene.Body.GetHp(BodyPart.Thorax), 1e-4f, "수분 > 0이면 피해 없음");
        }

        [UnityTest]
        public IEnumerator Starving_HalvesStaminaRegen()
        {
            Time.captureDeltaTime = 1f / 30f;
            StaminaModel stamina = scene.Motor.Stamina;
            float normal = 0f, starving = 0f;

            Assert.AreEqual(1f, scene.Motor.StaminaRegenMultiplier);
            Assert.IsTrue(stamina.TrySpend(80f));
            yield return MeasureStaminaRegen(15, r => normal = r);

            Health.Survival.Tick(2000f, false);       // 수분·에너지 0
            Health.Survival.Restore(1000f, 0f);       // 수분만 가득 (탈수 피해 제외)
            Assert.IsTrue(Health.Survival.IsStarving);
            Assert.AreEqual(SurvivalTier.Critical, View().GetSurvivalTier(SurvivalStat.Energy), "허기");
            Assert.AreEqual(0.5f, scene.Motor.StaminaRegenMultiplier, 1e-6f);
            Assert.IsTrue(stamina.TrySpend(stamina.Current - 20f));
            yield return MeasureStaminaRegen(15, r => starving = r);

            Debug.Log($"[SurvivalFlowTests] 스태미나 재생 정상 {normal:F2}/s, 허기 {starving:F2}/s");
            Assert.Greater(normal, 0f);
            Assert.AreEqual(0.5f, starving / normal, 0.05f, "에너지 0 → 재생 × 0.5");
        }

        private IEnumerator MeasureStaminaRegen(int frames, Action<float> result)
        {
            yield return null;
            float s0 = scene.Motor.Stamina.Current;
            float elapsed = 0f;
            for (int i = 0; i < frames; i++)
            {
                yield return null;
                elapsed += Time.deltaTime;
            }
            result((scene.Motor.Stamina.Current - s0) / elapsed);
        }

        [UnityTest]
        public IEnumerator SurvivalUpdate_NoGcAlloc()
        {
            RegisterFakes(1f, 0.5f);
            Assert.IsTrue(scene.Inventory.TryEquipNew(ItemFactory.Create(GasMaskDefinition()), EquipSlot.Helmet));
            Health.Survival.Tick(1000f, false);   // 탈수 피해 경로도 돈다
            yield return null;

            MethodInfo method = typeof(PlayerHealth).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method);
            var update = (Action)Delegate.CreateDelegate(typeof(Action), Health, method);
            Recorder recorder = Recorder.Get("GC.Alloc");
            Action empty = () => { };
            Sample(recorder, empty);
            Sample(recorder, update);   // 첫 호출(JIT) 제외

            int allocFrames = 0, noisy = 0;
            for (int i = 0; i < 60; i++)
            {
                yield return null;
                if (Sample(recorder, update) > 0)
                {
                    if (Sample(recorder, empty) > 0) { noisy++; continue; }
                    allocFrames++;
                }
            }
            Assert.IsFalse(Health.IsDead, "측정 중 살아 있어야 탈수·방사능 경로를 잰다");
            Assert.AreEqual(0, allocFrames, $"PlayerHealth.Update GC.Alloc (잡음 제외 {noisy})");
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

        private sealed class FakeStorm : IAshStormInfo
        {
            public float Rate;
            public AshStormPhase Phase => Rate > 0f ? AshStormPhase.Peak : AshStormPhase.Pending;
            public float Intensity => Rate > 0f ? 1f : 0f;
            public float VisibilityM => float.PositiveInfinity;
            public float RadiationPerSec => Rate;
        }

        private sealed class FakeField : IRadiationField
        {
            public float Rate;
            public float ZoneRatePerSec(Vector3 position) => Rate;
        }
    }
}
#endif
