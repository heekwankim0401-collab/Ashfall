using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Ashfall.Audio;
using Ashfall.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = NUnit.Framework.Is;

namespace Ashfall.Tests.PlayMode
{
    // M6 2b: 레이드 오디오 흐름 (설계 §7 AC2). 코드로 만든 장면: AudioDirector + 가짜 청취자(IPlayerTarget) + 지면.
    // 배치모드에 오디오 장치가 없을 수 있어 isPlaying 대신 AudioDirector.LastPlayed·풀 상태(요청 기록)로 확인한다(위험 R3).
    // 앞선 테스트가 남긴 씬과 겹치지 않게 높이 2000m에서 한다.
    public class AudioFlowTests
    {
        private static readonly Vector3 Origin = new Vector3(0f, 2000f, 0f);
        private const float EyeHeight = 1.6f;
        private const int Seed = 12345;

        private GameObject root;
        private AudioConfigSO cfg;
        private readonly List<Object> owned = new List<Object>();
        private FakeTarget target;
        private FakeStorm storm;
        private AudioDirector director;

        private static GameManager Gm => GameManager.Instance;

        // ---------- 준비 ----------

        private AudioClip Clip(string name, float seconds)
        {
            var clip = AudioClip.Create(name, Mathf.Max(1, (int)(seconds * 22050)), 1, 22050, false);
            owned.Add(clip);
            return clip;
        }

        private AudioConfigSO CreateConfig()
        {
            var c = ScriptableObject.CreateInstance<AudioConfigSO>();
            owned.Add(c);
            foreach (SurfaceFootsteps set in c.footsteps)
                set.clips = new[] { Clip($"{set.surface}_0", 0.2f), Clip($"{set.surface}_1", 0.2f), Clip($"{set.surface}_2", 0.2f) };
            foreach (GunshotSet set in c.gunshots)
            {
                set.near = Clip($"{set.weaponClass}_Near", 1f);
                set.mid = Clip($"{set.weaponClass}_Mid", 1.5f);
                set.far = Clip($"{set.weaponClass}_Far", 3f);
            }
            c.windLoop = Clip("Wind", 2f);
            c.stormLoop = Clip("Storm", 2f);
            c.collapseClips = new[] { Clip("Collapse_0", 3f), Clip("Collapse_1", 3f) };
            c.geigerClick = Clip("Geiger", 0.004f);
            c.hideoutMusic = Clip("Music", 2f);
            return c;
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Assert.IsNotNull(Gm, "GameManager 없음");
            cfg = CreateConfig();
            root = new GameObject("AudioFlowTestRoot");
            target = new FakeTarget { Eye = Origin + Vector3.up * EyeHeight, RootTransform = root.transform };
            Gm.Services.Register<IPlayerTarget>(target);

            var go = new GameObject("AudioDirector");
            go.SetActive(false);
            go.transform.SetParent(root.transform);
            director = go.AddComponent<AudioDirector>();
            director.Configure(cfg, Seed);
            go.SetActive(true);
            yield return null;   // Start (Prewarm·바람·서비스)
            director.ResolveServices();
            Assert.IsTrue(director.IsInitialized);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (Gm != null)
            {
                Gm.Flow.SetWorldPaused(false);
                if (target != null)
                    Gm.Services.Unregister<IPlayerTarget>(target);
                if (storm != null)
                    Gm.Services.Unregister<IAshStormInfo>(storm);
            }
            target = null;
            storm = null;
            if (root != null)
                Object.Destroy(root);
            yield return null;
            foreach (Object o in owned)
                if (o != null)
                    Object.Destroy(o);
            owned.Clear();
            AudioListener.pause = false;
        }

        private static void Publish<T>(T evt) where T : struct, IGameEvent => Gm.Events.Publish(evt);

        private GameObject Box(string name, Vector3 center, Vector3 size, SurfaceKind? kind, bool trigger = false, int layer = 0)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.layer = layer;
            go.transform.SetParent(root.transform);
            go.transform.position = center;
            go.transform.localScale = size;
            go.GetComponent<Collider>().isTrigger = trigger;
            if (kind.HasValue)
                go.AddComponent<GroundSurface>().Kind = kind.Value;
            Physics.SyncTransforms();
            return go;
        }

        private void MoveListener(Vector3 feet) => target.Eye = feet + Vector3.up * EyeHeight;

        private static bool InSet(AudioClip clip, SurfaceFootsteps set) => set.clips.Contains(clip);

        // ---------- 발소리 ----------

        [UnityTest]
        public IEnumerator Footstep_OnMetalFloor_PlaysMetalClip_At3DPosition_WithGain()
        {
            Box("MetalFloor", Origin + new Vector3(0f, -0.5f, 0f), new Vector3(20f, 1f, 20f), SurfaceKind.Metal);
            Vector3 feet = Origin + new Vector3(3f, 0f, 0f);
            int before = director.PlayedCount;

            Publish(new FootstepTaken(feet, FootstepGait.Walk, SoundSource.Enemy));

            Assert.AreEqual(before + 1, director.PlayedCount);
            PlayedSound p = director.LastPlayed;
            Assert.AreEqual(PlayedSoundKind.Footstep, p.Kind);
            Assert.AreEqual(SurfaceKind.Metal, p.Surface);
            Assert.IsTrue(InSet(p.Clip, FootstepMix.FindSurface(cfg, SurfaceKind.Metal)), $"금속 세트 클립이 아니다: {p.Clip.name}");
            Assert.AreEqual(feet, p.Position);
            float d = Vector3.Distance(feet, target.Eye);
            float expected = 0.7f * Mathf.Pow(1f - d / 25f, 2f);
            Assert.AreEqual(expected, p.Gain, 1e-4f);
            Assert.AreEqual(FootstepMix.PriorityEnemyFootstep, p.Priority);
            Assert.That(p.Pitch, Is.InRange(1f - cfg.pitchJitter, 1f + cfg.pitchJitter));

            AudioSource voice = director.Voices.Source(p.Voice);
            Assert.AreSame(p.Clip, voice.clip);
            Assert.AreEqual(1f, voice.spatialBlend, "3D");
            Assert.AreEqual(0f, voice.dopplerLevel);
            Assert.AreEqual(AudioRolloffMode.Custom, voice.rolloffMode);
            Assert.AreEqual(feet, voice.transform.position);
            Assert.AreEqual(expected, voice.volume, 1e-4f);
            Assert.IsFalse(voice.loop);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Footstep_PlayerSelf_QuieterAndLowestPriority()
        {
            Box("MetalFloor", Origin + new Vector3(0f, -0.5f, 0f), new Vector3(20f, 1f, 20f), SurfaceKind.Metal);
            Publish(new FootstepTaken(Origin, FootstepGait.Walk, SoundSource.Player));
            PlayedSound p = director.LastPlayed;
            float expected = 0.7f * Mathf.Pow(1f - EyeHeight / 25f, 2f) * 0.6f;
            Assert.AreEqual(expected, p.Gain, 1e-4f);
            Assert.AreEqual(FootstepMix.PriorityPlayerFootstep, p.Priority);
            Assert.AreEqual(SoundSource.Player, p.Source);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Footstep_Terrain_IsDirt_PuddleTrigger_IsWater_UntaggedBox_IsDebris()
        {
            int cached0 = director.Probe.CachedColliders;
            var data = new TerrainData { heightmapResolution = 33, size = new Vector3(50f, 10f, 50f) };
            owned.Add(data);
            GameObject terrain = Terrain.CreateTerrainGameObject(data);
            terrain.transform.SetParent(root.transform);
            terrain.transform.position = Origin + new Vector3(100f, 0f, 100f);
            Physics.SyncTransforms();

            Vector3 onTerrain = Origin + new Vector3(110f, 0f, 110f);
            MoveListener(onTerrain);
            Publish(new FootstepTaken(onTerrain, FootstepGait.Walk, SoundSource.Enemy));
            Assert.AreEqual(SurfaceKind.Dirt, director.LastPlayed.Surface, "지형 = 흙");
            Assert.IsTrue(InSet(director.LastPlayed.Clip, FootstepMix.FindSurface(cfg, SurfaceKind.Dirt)));

            // 물웅덩이: 지형 위 얇은 트리거(Ignore Raycast 레이어) + GroundSurface(Water)
            Vector3 inPuddle = Origin + new Vector3(130f, 0f, 130f);
            Box("Puddle", inPuddle + new Vector3(0f, 0.02f, 0f), new Vector3(4f, 0.05f, 4f), SurfaceKind.Water, trigger: true, layer: 2);
            MoveListener(inPuddle);
            Publish(new FootstepTaken(inPuddle, FootstepGait.Sprint, SoundSource.Enemy));
            Assert.AreEqual(SurfaceKind.Water, director.LastPlayed.Surface, "웅덩이 트리거 = 물");
            Assert.IsTrue(InSet(director.LastPlayed.Clip, FootstepMix.FindSurface(cfg, SurfaceKind.Water)));

            // 태그 없는 트리거(탈출 구역 등)는 지면이 아니다
            Vector3 inZone = Origin + new Vector3(140f, 0f, 120f);
            Box("ZoneTrigger", inZone, new Vector3(4f, 3f, 4f), null, trigger: true);
            MoveListener(inZone);
            Publish(new FootstepTaken(inZone, FootstepGait.Walk, SoundSource.Enemy));
            Assert.AreEqual(SurfaceKind.Dirt, director.LastPlayed.Surface, "태그 없는 트리거 무시");

            // 태그 없는 콜라이더 = 잔해(untaggedSurface)
            Vector3 onBox = Origin + new Vector3(-100f, 0f, -100f);
            Box("UntaggedSlab", onBox + new Vector3(0f, -0.5f, 0f), new Vector3(4f, 1f, 4f), null);
            MoveListener(onBox);
            Publish(new FootstepTaken(onBox, FootstepGait.Walk, SoundSource.Enemy));
            Assert.AreEqual(SurfaceKind.Debris, director.LastPlayed.Surface);
            Assert.IsTrue(director.LastPlayed.HasSurface);
            // 리뷰 낮음 8: 미리 등록하지 않은 콜라이더(지형·트리거·태그 없는 상자, 늦게 생긴 웅덩이)는 사전에 쌓이지 않는다
            Assert.AreEqual(cached0, director.Probe.CachedColliders, "지면 판정 캐시가 레이드 중 자라지 않는다");
            yield return null;
        }

        [UnityTest]
        public IEnumerator Footstep_BeyondAudibleRange_NotPlayed_NoRaycast()
        {
            Box("MetalFloor", Origin + new Vector3(0f, -0.5f, 0f), new Vector3(100f, 1f, 100f), SurfaceKind.Metal);
            int played = director.PlayedCount, culled = director.CulledCount;
            Publish(new FootstepTaken(Origin + new Vector3(30f, 0f, 0f), FootstepGait.Walk, SoundSource.Enemy));   // 걷기 25m
            Publish(new FootstepTaken(Origin + new Vector3(12f, 0f, 0f), FootstepGait.Crouch, SoundSource.Enemy)); // 웅크림 10m
            Assert.AreEqual(played, director.PlayedCount);
            Assert.AreEqual(culled + 2, director.CulledCount);
            Assert.AreEqual(0, director.Probe.LastHitCount, "들리는 거리 밖이면 레이캐스트도 안 한다");

            Publish(new FootstepTaken(Origin + new Vector3(30f, 0f, 0f), FootstepGait.Sprint, SoundSource.Enemy)); // 질주 40m
            Assert.AreEqual(played + 1, director.PlayedCount);
            yield return null;
        }

        // ---------- 총성 ----------

        [UnityTest]
        public IEnumerator Gunshot_DistanceLayers_ByWeaponClass()
        {
            var smg = ScriptableObject.CreateInstance<WeaponDefinitionSO>();
            smg.cls = WeaponClass.Smg;
            owned.Add(smg);
            GunshotSet smgSet = GunshotMix.FindSet(cfg, WeaponClass.Smg);

            var cases = new (float d, GunshotLayer layer, AudioClip clip)[]
            {
                (30f, GunshotLayer.Near, smgSet.near),
                (100f, GunshotLayer.Mid, smgSet.mid),
                (400f, GunshotLayer.Far, smgSet.far),
            };
            foreach (var c in cases)
            {
                Vector3 at = target.Eye + Vector3.right * c.d;
                Publish(new ShotFired(at, smg, SoundSource.Enemy));
                PlayedSound p = director.LastPlayed;
                Assert.AreEqual(PlayedSoundKind.Gunshot, p.Kind, $"{c.d}m");
                Assert.AreEqual(c.layer, p.Layer, $"{c.d}m");
                Assert.AreSame(c.clip, p.Clip, $"{c.d}m");
                Assert.AreEqual(at, p.Position);
                Assert.IsTrue(GunshotMix.Evaluate(c.d, cfg, out _, out float gain));
                Assert.AreEqual(gain, p.Gain, 1e-4f);
                Assert.AreEqual(FootstepMix.PriorityGunshot, p.Priority);
                Assert.AreEqual(1f, director.Voices.Source(p.Voice).spatialBlend);
            }

            int played = director.PlayedCount;
            Publish(new ShotFired(target.Eye + Vector3.right * 1500f, smg, SoundSource.Enemy));
            Assert.AreEqual(played, director.PlayedCount, "1500m = 안 들림");

            Publish(new ShotFired(target.Eye + Vector3.forward * 10f, null, SoundSource.Player));
            Assert.AreSame(GunshotMix.FindSet(cfg, WeaponClass.AssaultRifle).near, director.LastPlayed.Clip, "무기 없음 → 대체 세트");
            Assert.AreEqual(SoundSource.Player, director.LastPlayed.Source);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Gunshots_25InARow_StealVoices_NoException_FootstepDroppedWhenFull()
        {
            Box("MetalFloor", Origin + new Vector3(0f, -0.5f, 0f), new Vector3(20f, 1f, 20f), SurfaceKind.Metal);
            int played = director.PlayedCount;
            int voices = director.Voices.Count;
            Assert.AreEqual(24, voices);
            for (int i = 0; i < 25; i++)
                Publish(new ShotFired(target.Eye + Vector3.right * (10f + i), null, SoundSource.Enemy));
            Assert.AreEqual(played + 25, director.PlayedCount);
            Assert.GreaterOrEqual(director.Voices.StealCount, 1, "25번째 총성은 가장 오래된 총성을 빼앗는다");
            Assert.AreEqual(voices, director.Voices.Allocator.BusyCount(Time.time));

            int dropped = director.Voices.DroppedCount;
            Publish(new FootstepTaken(Origin + Vector3.right, FootstepGait.Walk, SoundSource.Enemy));
            Assert.AreEqual(dropped + 1, director.Voices.DroppedCount, "총성으로 가득 차면 발소리는 버린다");
            Assert.AreEqual(PlayedSoundKind.Gunshot, director.LastPlayed.Kind);
            yield return null;
        }

        // ---------- 일시정지 (Q19) ----------

        [UnityTest]
        public IEnumerator WorldPause_PausesAudioListener()
        {
            Assert.IsFalse(AudioListener.pause);
            Gm.Flow.SetWorldPaused(true);
            yield return null;
            yield return null;
            Assert.IsTrue(AudioListener.pause, "세계 정지 → AudioListener.pause");
            Gm.Flow.SetWorldPaused(false);
            yield return null;
            yield return null;
            Assert.IsFalse(AudioListener.pause);

            Gm.Flow.SetWorldPaused(true);
            yield return null;
            Object.Destroy(director.gameObject);
            yield return null;
            Assert.IsFalse(AudioListener.pause, "디렉터가 사라지면 되돌린다");
            Gm.Flow.SetWorldPaused(false);
        }

        // ---------- 환경음 ----------

        [UnityTest]
        public IEnumerator Ambient_Wind_StormLoop_Geiger_FollowStorm()
        {
            AmbientSoundscape amb = director.Ambient;
            Assert.AreSame(cfg.windLoop, amb.Wind.clip);
            Assert.IsTrue(amb.Wind.loop);
            Assert.AreEqual(0f, amb.Wind.spatialBlend, "환경음 = 2D");
            Assert.IsTrue(amb.WindRequested, "바람 상시");
            Assert.AreEqual(cfg.windVolume, amb.Wind.volume, 1e-4f);
            Assert.IsFalse(amb.StormLoopActive);

            storm = new FakeStorm { Phase = AshStormPhase.Rising, Intensity = 1f, RadiationPerSec = 3f };
            Gm.Services.Register<IAshStormInfo>(storm);
            director.ResolveServices();
            Publish(new AshStormChanged(AshStormPhase.Rising));
            Assert.IsTrue(amb.StormLoopActive, "Rising → 폭풍 루프");

            float until = Time.realtimeSinceStartup + 1.5f;
            while (Time.realtimeSinceStartup < until)
                yield return null;
            Assert.AreEqual(cfg.windStormVolume, amb.Wind.volume, 1e-4f, "폭풍 최대 = 바람 커짐");
            Assert.AreEqual(cfg.stormLoopMaxVolume, amb.Storm.volume, 1e-4f);
            Assert.IsTrue(amb.Storm.loop);
            Assert.Greater(amb.GeigerClicks, 0, "가이거 클릭 (방사능 3/s → 18클릭/s)");

            storm.Phase = AshStormPhase.Over;
            storm.Intensity = 0f;
            storm.RadiationPerSec = 0f;
            Publish(new AshStormChanged(AshStormPhase.Over));
            yield return null;
            Assert.IsFalse(amb.StormLoopActive, "Over → 폭풍 루프 정지");
            Assert.AreEqual(cfg.windVolume, amb.Wind.volume, 1e-4f);
            int clicks = amb.GeigerClicks;
            yield return null;
            yield return null;
            Assert.AreEqual(clicks, amb.GeigerClicks, "방사능 0 → 클릭 없음");
        }

        [UnityTest]
        public IEnumerator Collapse_DebugNow_PlaysFarAway()
        {
            director.DebugCollapseNow();
            PlayedSound p = director.LastPlayed;
            Assert.AreEqual(PlayedSoundKind.Collapse, p.Kind);
            Assert.IsTrue(cfg.collapseClips.Contains(p.Clip));
            Vector2 flat = new Vector2(p.Position.x - target.Eye.x, p.Position.z - target.Eye.z);
            Assert.That(flat.magnitude, Is.InRange(150f - 0.01f, 400f + 0.01f));
            Assert.AreEqual(FootstepMix.PriorityCollapse, p.Priority);
            Assert.AreEqual(1f, director.Voices.Source(p.Voice).spatialBlend);
            yield return null;
        }

        [UnityTest]
        public IEnumerator DebugGunshot_Layers()
        {
            director.DebugGunshot(GunshotLayer.Near);
            Assert.AreEqual(GunshotLayer.Near, director.LastPlayed.Layer);
            director.DebugGunshot(GunshotLayer.Mid);
            Assert.AreEqual(GunshotLayer.Mid, director.LastPlayed.Layer);
            director.DebugGunshot(GunshotLayer.Far);
            Assert.AreEqual(GunshotLayer.Far, director.LastPlayed.Layer);
            yield return null;
        }

        // ---------- 할당 ----------

        [UnityTest]
        public IEnumerator EventPath_300Events_NoGcAlloc()
        {
            Box("MetalFloor", Origin + new Vector3(0f, -0.5f, 0f), new Vector3(20f, 1f, 20f), SurfaceKind.Metal);
            Box("Debris", Origin + new Vector3(15f, -0.5f, 0f), new Vector3(4f, 1f, 4f), null);
            var rifle = ScriptableObject.CreateInstance<WeaponDefinitionSO>();
            rifle.cls = WeaponClass.AssaultRifle;
            owned.Add(rifle);
            EventBus bus = Gm.Events;
            var stepMetal = new FootstepTaken(Origin + new Vector3(2f, 0f, 0f), FootstepGait.Walk, SoundSource.Enemy);
            var stepDebris = new FootstepTaken(Origin + new Vector3(15f, 0f, 0f), FootstepGait.Sprint, SoundSource.Enemy);
            var stepFar = new FootstepTaken(Origin + new Vector3(80f, 0f, 0f), FootstepGait.Walk, SoundSource.Enemy);
            var shotNear = new ShotFired(Origin + new Vector3(0f, 0f, 20f), rifle, SoundSource.Enemy);
            var shotFar = new ShotFired(Origin + new Vector3(0f, 0f, 600f), null, SoundSource.Enemy);
            int before = director.PlayedCount;

            AssertNoAlloc(() =>
            {
                for (int i = 0; i < 60; i++)
                {
                    bus.Publish(stepMetal);
                    bus.Publish(stepDebris);
                    bus.Publish(stepFar);
                    bus.Publish(shotNear);
                    bus.Publish(shotFar);
                }
            });
            Assert.Greater(director.PlayedCount, before);
            yield return null;
        }

        // 워밍업 + 측정 잡음 재시도 (EditMode AiTest.AssertNoAlloc과 같은 방식)
        private static void AssertNoAlloc(TestDelegate act, int attempts = 6)
        {
            act();
            for (int i = 0; i < attempts - 1; i++)
            {
                if (((NUnit.Framework.Constraints.IResolveConstraint)Is.Not.AllocatingGCMemory()).Resolve().ApplyTo(act).IsSuccess)
                    return;
                System.Threading.Thread.Sleep(50 * (i + 1));
            }
            Assert.That(act, Is.Not.AllocatingGCMemory());
        }

        // ---------- 은신처 음악 (Q18) ----------

        [UnityTest]
        public IEnumerator HideoutMusic_2DLoop_IgnoresListenerPause()
        {
            var go = new GameObject("HideoutMusic");
            go.SetActive(false);
            go.transform.SetParent(root.transform);
            var music = go.AddComponent<HideoutMusic>();
            music.Configure(cfg);
            go.SetActive(true);
            yield return null;

            Assert.IsNotNull(music.Source);
            Assert.AreSame(cfg.hideoutMusic, music.Source.clip);
            Assert.IsTrue(music.Source.loop);
            Assert.AreEqual(0f, music.Source.spatialBlend);
            Assert.IsTrue(music.Source.ignoreListenerPause, "은신처 음악은 일시정지 무시");
            Assert.AreEqual(cfg.hideoutMusicVolume, music.Source.volume, 1e-4f);
            Assert.IsTrue(music.PlayRequested);
        }

        [UnityTest]
        public IEnumerator HideoutMusic_NoClip_Silent()
        {
            var empty = ScriptableObject.CreateInstance<AudioConfigSO>();
            owned.Add(empty);
            var go = new GameObject("HideoutMusic");
            go.SetActive(false);
            go.transform.SetParent(root.transform);
            var music = go.AddComponent<HideoutMusic>();
            music.Configure(empty);
            go.SetActive(true);
            yield return null;
            Assert.IsFalse(music.PlayRequested);
            Assert.IsNull(music.Source.clip);
        }

        // ---------- M7 7A-2c: 무기 클릭 (WeaponClicked)·산탄·저격 총성 ----------

        private void GiveClickClips()
        {
            cfg.dryFireClip = Clip("DryFire", 0.1f);
            cfg.jamClearClip = Clip("JamClear", 0.4f);
        }

        [UnityTest]
        public IEnumerator WeaponClicked_EachKind_PlaysClip_At3DPosition_PriorityAbovePlayerFootstep()
        {
            GiveClickClips();
            Vector3 muzzle = target.Eye + new Vector3(0.2f, -0.1f, 0.6f);
            var cases = new (WeaponClickKind kind, AudioClip clip, float volume)[]
            {
                (WeaponClickKind.DryFire, cfg.dryFireClip, cfg.dryFireVolume),
                (WeaponClickKind.Jammed, cfg.dryFireClip, cfg.dryFireVolume),
                (WeaponClickKind.JamCleared, cfg.jamClearClip, cfg.jamClearVolume),
            };
            foreach (var c in cases)
            {
                int before = director.PlayedCount;
                Publish(new WeaponClicked(muzzle, c.kind));
                Assert.AreEqual(before + 1, director.PlayedCount, $"{c.kind}");
                PlayedSound p = director.LastPlayed;
                Assert.AreEqual(PlayedSoundKind.WeaponClick, p.Kind, $"{c.kind}");
                Assert.AreEqual(c.kind, director.LastWeaponClick);
                Assert.AreSame(c.clip, p.Clip, $"{c.kind}");
                Assert.AreEqual(muzzle, p.Position);
                Assert.AreEqual(c.volume, p.Gain, 1e-4f);
                Assert.AreEqual(1f, p.Pitch);
                Assert.AreEqual(SoundSource.Player, p.Source);
                Assert.AreEqual(FootstepMix.PriorityWeaponClick, p.Priority);
                Assert.Less(p.Priority, FootstepMix.PriorityPlayerFootstep, "내 발소리보다 높다(작을수록 높음)");
                Assert.IsFalse(p.HasLayer);
                Assert.IsFalse(p.HasSurface);

                AudioSource voice = director.Voices.Source(p.Voice);
                Assert.AreSame(c.clip, voice.clip);
                Assert.AreEqual(1f, voice.spatialBlend, "3D");
                Assert.AreEqual(muzzle, voice.transform.position);
                Assert.AreEqual(c.volume, voice.volume, 1e-4f);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator WeaponClicked_NoClip_Silent()
        {
            int before = director.PlayedCount;
            Publish(new WeaponClicked(target.Eye, WeaponClickKind.DryFire));
            Publish(new WeaponClicked(target.Eye, WeaponClickKind.JamCleared));
            Assert.AreEqual(before, director.PlayedCount, "클립이 없으면 소리 없음(예외 없음)");
            yield return null;
        }

        [UnityTest]
        public IEnumerator WeaponClicked_StealsPlayerFootstep_WhenPoolFull()
        {
            GiveClickClips();
            Box("MetalFloor", Origin + new Vector3(0f, -0.5f, 0f), new Vector3(20f, 1f, 20f), SurfaceKind.Metal);
            int voices = director.Voices.Count;
            for (int i = 0; i < voices; i++)
                Publish(new FootstepTaken(Origin, FootstepGait.Walk, SoundSource.Player));
            Assert.AreEqual(voices, director.Voices.Allocator.BusyCount(Time.time), "내 발소리로 가득");
            int steals = director.Voices.StealCount;
            Publish(new WeaponClicked(target.Eye, WeaponClickKind.Jammed));
            Assert.AreEqual(PlayedSoundKind.WeaponClick, director.LastPlayed.Kind);
            Assert.AreEqual(steals + 1, director.Voices.StealCount, "클릭이 내 발소리를 빼앗는다");
            yield return null;
        }

        [UnityTest]
        public IEnumerator WeaponClicked_DroppedWhenGunshotsFillPool()
        {
            GiveClickClips();
            int voices = director.Voices.Count;
            for (int i = 0; i < voices; i++)
                Publish(new ShotFired(target.Eye + Vector3.right * (10f + i), null, SoundSource.Enemy));
            Assert.AreEqual(voices, director.Voices.Allocator.BusyCount(Time.time), "총성으로 가득");
            int dropped = director.Voices.DroppedCount;
            Publish(new WeaponClicked(target.Eye, WeaponClickKind.DryFire));
            Assert.AreEqual(dropped + 1, director.Voices.DroppedCount, "총성으로 가득 차면 클릭은 버린다(총성 > 클릭)");
            Assert.AreEqual(PlayedSoundKind.Gunshot, director.LastPlayed.Kind);
            yield return null;
        }

        [UnityTest]
        public IEnumerator WeaponClicked_Disabled_Unsubscribed_ReenableResubscribes()
        {
            GiveClickClips();
            director.enabled = false;
            int before = director.PlayedCount;
            Publish(new WeaponClicked(target.Eye, WeaponClickKind.DryFire));
            Assert.AreEqual(before, director.PlayedCount, "OnDisable → 구독 해제");
            director.enabled = true;
            Publish(new WeaponClicked(target.Eye, WeaponClickKind.DryFire));
            Assert.AreEqual(before + 1, director.PlayedCount, "OnEnable → 다시 구독");
            yield return null;
        }

        [UnityTest]
        public IEnumerator Gunshot_ShotgunAndDmr_UseOwnSets()
        {
            var shotgun = ScriptableObject.CreateInstance<WeaponDefinitionSO>();
            shotgun.cls = WeaponClass.Shotgun;
            var dmr = ScriptableObject.CreateInstance<WeaponDefinitionSO>();
            dmr.cls = WeaponClass.Sniper;
            owned.Add(shotgun);
            owned.Add(dmr);
            // 실제 에셋은 M6 오디오 메뉴가 세트를 추가한다(AudioConfigAssetTests). 여기서는 같은 모양으로 끝에 붙인다.
            Assert.IsNull(GunshotMix.FindSet(cfg, WeaponClass.Shotgun), "기본 인스턴스에는 산탄 세트가 없다(대체 규칙 유지)");
            var shotgunSet = new GunshotSet(WeaponClass.Shotgun, 1f)
                { near = Clip("Shotgun_Near", 1f), mid = Clip("Shotgun_Mid", 1.5f), far = Clip("Shotgun_Far", 3f) };
            var sniperSet = new GunshotSet(WeaponClass.Sniper, 1f)
                { near = Clip("Sniper_Near", 1f), mid = Clip("Sniper_Mid", 1.5f), far = Clip("Sniper_Far", 3f) };
            cfg.gunshots = cfg.gunshots.Concat(new[] { shotgunSet, sniperSet }).ToArray();
            Publish(new ShotFired(target.Eye + Vector3.forward * 20f, shotgun, SoundSource.Player));
            Assert.AreSame(shotgunSet.near, director.LastPlayed.Clip, "디렉터는 설정의 현재 세트 배열을 읽는다");

            var cases = new (WeaponDefinitionSO weapon, float d, GunshotLayer layer, AudioClip clip)[]
            {
                (shotgun, 20f, GunshotLayer.Near, shotgunSet.near),
                (shotgun, 150f, GunshotLayer.Mid, shotgunSet.mid),
                (dmr, 30f, GunshotLayer.Near, sniperSet.near),
                (dmr, 600f, GunshotLayer.Far, sniperSet.far),
            };
            foreach (var c in cases)
            {
                Publish(new ShotFired(target.Eye + Vector3.forward * c.d, c.weapon, SoundSource.Player));
                PlayedSound p = director.LastPlayed;
                Assert.AreEqual(PlayedSoundKind.Gunshot, p.Kind, $"{c.weapon.cls} {c.d}m");
                Assert.AreEqual(c.layer, p.Layer, $"{c.weapon.cls} {c.d}m");
                Assert.AreSame(c.clip, p.Clip, $"{c.weapon.cls} {c.d}m");
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator WeaponClicked_300Events_NoGcAlloc()
        {
            GiveClickClips();
            EventBus bus = Gm.Events;
            var dry = new WeaponClicked(target.Eye, WeaponClickKind.DryFire);
            var jam = new WeaponClicked(target.Eye, WeaponClickKind.Jammed);
            var clear = new WeaponClicked(target.Eye, WeaponClickKind.JamCleared);
            int before = director.PlayedCount;
            AssertNoAlloc(() =>
            {
                for (int i = 0; i < 100; i++)
                {
                    bus.Publish(dry);
                    bus.Publish(jam);
                    bus.Publish(clear);
                }
            });
            Assert.Greater(director.PlayedCount, before);
            yield return null;
        }

        // ---------- M7 7B-2c: 변이체 울음 (EnemyVocalized)·경기관총 총성 ----------

        private void GiveVocalClips()
        {
            cfg.mutantAlertClips = new[] { Clip("MutAlert_0", 1.2f), Clip("MutAlert_1", 1.2f) };
            cfg.mutantAttackClips = new[] { Clip("MutAttack_0", 0.5f) };
        }

        [UnityTest]
        public IEnumerator EnemyVocalized_EachKind_PlaysClip_At3DPosition_LinearGain_PriorityAboveEnemyFootstep()
        {
            GiveVocalClips();
            Vector3 mouth = target.Eye + new Vector3(0f, 0f, 20f);
            float t = 1f - 20f / cfg.vocalAudibleM;
            var cases = new (EnemyVocalKind kind, AudioClip[] clips, float volume)[]
            {
                (EnemyVocalKind.MutantAlert, cfg.mutantAlertClips, cfg.mutantAlertVolume),
                (EnemyVocalKind.MutantAttack, cfg.mutantAttackClips, cfg.mutantAttackVolume),
            };
            foreach (var c in cases)
            {
                int before = director.PlayedCount;
                Publish(new EnemyVocalized(mouth, c.kind));
                Assert.AreEqual(before + 1, director.PlayedCount, $"{c.kind}");
                PlayedSound p = director.LastPlayed;
                Assert.AreEqual(PlayedSoundKind.EnemyVocal, p.Kind, $"{c.kind}");
                Assert.AreEqual(c.kind, director.LastEnemyVocal);
                CollectionAssert.Contains(c.clips, p.Clip, $"{c.kind}: 그 종류의 클립");
                Assert.AreEqual(mouth, p.Position);
                Assert.AreEqual(c.volume * t, p.Gain, 1e-4f, $"{c.kind}: 음량 × (1 − d/들리는 거리)");
                Assert.That(p.Pitch, Is.InRange(1f - cfg.pitchJitter - 1e-4f, 1f + cfg.pitchJitter + 1e-4f));
                Assert.AreEqual(SoundSource.Enemy, p.Source);
                Assert.AreEqual(FootstepMix.PriorityEnemyVocal, p.Priority);
                Assert.Less(p.Priority, FootstepMix.PriorityEnemyFootstep, "적 발소리보다 높다(작을수록 높음)");
                Assert.IsFalse(p.HasLayer);
                Assert.IsFalse(p.HasSurface);

                AudioSource voice = director.Voices.Source(p.Voice);
                Assert.AreSame(p.Clip, voice.clip);
                Assert.AreEqual(1f, voice.spatialBlend, "3D");
                Assert.AreEqual(mouth, voice.transform.position);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator EnemyVocalized_BeyondAudible_Culled_NoClip_Silent()
        {
            GiveVocalClips();
            int played = director.PlayedCount;
            int culled = director.CulledCount;
            Publish(new EnemyVocalized(target.Eye + Vector3.forward * (cfg.vocalAudibleM + 1f), EnemyVocalKind.MutantAlert));
            Assert.AreEqual(played, director.PlayedCount, "들리는 거리 밖 → 재생 없음");
            Assert.AreEqual(culled + 1, director.CulledCount);

            cfg.mutantAttackClips = new AudioClip[0];
            Publish(new EnemyVocalized(target.Eye + Vector3.forward * 5f, EnemyVocalKind.MutantAttack));
            cfg.mutantAttackClips = new AudioClip[] { null };
            Publish(new EnemyVocalized(target.Eye + Vector3.forward * 5f, EnemyVocalKind.MutantAttack));
            Assert.AreEqual(played, director.PlayedCount, "클립이 없으면 소리 없음(예외 없음)");
            yield return null;
        }

        [UnityTest]
        public IEnumerator EnemyVocalized_TwoAlertClips_NeverSameTwiceInRow()
        {
            GiveVocalClips();
            AudioClip last = null;
            for (int i = 0; i < 12; i++)
            {
                Publish(new EnemyVocalized(target.Eye + Vector3.forward * 10f, EnemyVocalKind.MutantAlert));
                AudioClip clip = director.LastPlayed.Clip;
                Assert.AreNotSame(last, clip, $"{i}: 직전과 같은 울음");
                last = clip;
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator EnemyVocalized_StealsEnemyFootstep_WhenPoolFull()
        {
            GiveVocalClips();
            Box("MetalFloor", Origin + new Vector3(0f, -0.5f, 0f), new Vector3(20f, 1f, 20f), SurfaceKind.Metal);
            int voices = director.Voices.Count;
            for (int i = 0; i < voices; i++)
                Publish(new FootstepTaken(Origin + Vector3.right * 2f, FootstepGait.Sprint, SoundSource.Enemy));
            Assert.AreEqual(voices, director.Voices.Allocator.BusyCount(Time.time), "적 발소리로 가득");
            int steals = director.Voices.StealCount;
            Publish(new EnemyVocalized(target.Eye + Vector3.forward * 10f, EnemyVocalKind.MutantAlert));
            Assert.AreEqual(PlayedSoundKind.EnemyVocal, director.LastPlayed.Kind);
            Assert.AreEqual(steals + 1, director.Voices.StealCount, "울음이 적 발소리를 빼앗는다");
            yield return null;
        }

        // 빈 풀에서 총성으로 채운 뒤(같은 프레임 빼앗기는 가장 오래된 첫 칸만 돌기 때문에 빈 풀에서 시작 — WeaponClicked 테스트와 같음)
        [UnityTest]
        public IEnumerator EnemyVocalized_DroppedWhenGunshotsFillPool()
        {
            GiveVocalClips();
            int voices = director.Voices.Count;
            for (int i = 0; i < voices; i++)
                Publish(new ShotFired(target.Eye + Vector3.right * (10f + i), null, SoundSource.Enemy));
            Assert.AreEqual(voices, director.Voices.Allocator.BusyCount(Time.time), "총성으로 가득");
            int dropped = director.Voices.DroppedCount;
            Publish(new EnemyVocalized(target.Eye + Vector3.forward * 10f, EnemyVocalKind.MutantAttack));
            Assert.AreEqual(dropped + 1, director.Voices.DroppedCount, "총성으로 가득 차면 울음은 버린다(총성 > 울음)");
            Assert.AreEqual(PlayedSoundKind.Gunshot, director.LastPlayed.Kind);
            yield return null;
        }

        [UnityTest]
        public IEnumerator EnemyVocalized_Disabled_Unsubscribed_ReenableResubscribes()
        {
            GiveVocalClips();
            director.enabled = false;
            int before = director.PlayedCount;
            Publish(new EnemyVocalized(target.Eye + Vector3.forward * 10f, EnemyVocalKind.MutantAlert));
            Assert.AreEqual(before, director.PlayedCount, "OnDisable → 구독 해제");
            director.enabled = true;
            Publish(new EnemyVocalized(target.Eye + Vector3.forward * 10f, EnemyVocalKind.MutantAlert));
            Assert.AreEqual(before + 1, director.PlayedCount, "OnEnable → 다시 구독");
            yield return null;
        }

        [UnityTest]
        public IEnumerator EnemyVocalized_300Events_NoGcAlloc()
        {
            GiveVocalClips();
            EventBus bus = Gm.Events;
            var alert = new EnemyVocalized(target.Eye + Vector3.forward * 15f, EnemyVocalKind.MutantAlert);
            var attack = new EnemyVocalized(target.Eye + Vector3.left * 3f, EnemyVocalKind.MutantAttack);
            var far = new EnemyVocalized(target.Eye + Vector3.forward * 500f, EnemyVocalKind.MutantAlert);
            int before = director.PlayedCount;
            AssertNoAlloc(() =>
            {
                for (int i = 0; i < 100; i++)
                {
                    bus.Publish(alert);
                    bus.Publish(attack);
                    bus.Publish(far);
                }
            });
            Assert.Greater(director.PlayedCount, before);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Gunshot_MachineGun_UsesOwnSet()
        {
            var lmg = ScriptableObject.CreateInstance<WeaponDefinitionSO>();
            lmg.cls = WeaponClass.MachineGun;
            owned.Add(lmg);
            // 실제 에셋은 M6 오디오 메뉴가 M7GunshotClasses(MachineGun 포함)로 세트를 끝에 추가한다(AudioConfigAssetTests). 여기서는 같은 모양으로.
            Assert.IsNull(GunshotMix.FindSet(cfg, WeaponClass.MachineGun), "기본 인스턴스에는 경기관총 세트가 없다(대체 규칙 유지)");
            var lmgSet = new GunshotSet(WeaponClass.MachineGun, 1f)
                { near = Clip("Lmg_Near", 1f), mid = Clip("Lmg_Mid", 1.5f), far = Clip("Lmg_Far", 3f) };
            cfg.gunshots = cfg.gunshots.Concat(new[] { lmgSet }).ToArray();

            var cases = new (float d, GunshotLayer layer, AudioClip clip)[]
            {
                (20f, GunshotLayer.Near, lmgSet.near),
                (150f, GunshotLayer.Mid, lmgSet.mid),
                (600f, GunshotLayer.Far, lmgSet.far),
            };
            foreach (var c in cases)
            {
                Publish(new ShotFired(target.Eye + Vector3.forward * c.d, lmg, SoundSource.Enemy));
                PlayedSound p = director.LastPlayed;
                Assert.AreEqual(PlayedSoundKind.Gunshot, p.Kind, $"{c.d}m");
                Assert.AreEqual(c.layer, p.Layer, $"{c.d}m");
                Assert.AreSame(c.clip, p.Clip, $"{c.d}m");
                Assert.AreEqual(SoundSource.Enemy, p.Source);
            }
            yield return null;
        }

        // ---------- 가짜 서비스 ----------

        private sealed class FakeTarget : IPlayerTarget
        {
            public Vector3 Eye;
            public Transform RootTransform;
            public Transform Root => RootTransform;
            public Vector3 Position => Eye - Vector3.up * EyeHeight;
            public Vector3 EyePosition => Eye;
            public Vector3 CenterPosition => Eye - Vector3.up * 0.8f;
            public Vector3 Velocity => Vector3.zero;
            public TargetPosture Posture => TargetPosture.Standing;
            public bool IsDead => false;
        }

        private sealed class FakeStorm : IAshStormInfo
        {
            public AshStormPhase Phase { get; set; }
            public float Intensity { get; set; }
            public float VisibilityM => Intensity > 0f ? 30f : float.PositiveInfinity;
            public float RadiationPerSec { get; set; }
        }
    }

    // §11 "BGM 없음(은신처 제외)": 레이드·메인 메뉴 씬에 음악 소스가 없다(빌드 설정 씬을 이름으로 불러온다).
    public class AudioSceneMusicTests
    {
        private const float LoadTimeoutSec = 60f;

        private static IEnumerator Load(string sceneName)
        {
            AsyncOperation op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            Assert.IsNotNull(op, $"{sceneName} 로드 실패 (빌드 설정 확인)");
            float deadline = Time.realtimeSinceStartup + LoadTimeoutSec;
            while (!op.isDone)
            {
                Assert.Less(Time.realtimeSinceStartup, deadline, $"{sceneName} 로드 시간 초과");
                yield return null;
            }
            for (int i = 0; i < 3; i++)
                yield return null;
        }

        private static void AssertNoMusic(string sceneName)
        {
            Assert.AreEqual(0, Object.FindObjectsByType<HideoutMusic>(FindObjectsSortMode.None).Length, $"{sceneName}: 음악 오브젝트");
            foreach (AudioDirector d in Object.FindObjectsByType<AudioDirector>(FindObjectsSortMode.None))
            {
                if (d.Config == null || d.Config.hideoutMusic == null)
                    continue;
                foreach (AudioSource s in d.GetComponentsInChildren<AudioSource>(true))
                    Assert.AreNotSame(d.Config.hideoutMusic, s.clip, $"{sceneName}: 음악 클립을 트는 소스 {s.name}");
            }
        }

        [UnityTest]
        public IEnumerator MainMenu_And_Raid_HaveNoMusic()
        {
            yield return Load(SceneNames.MainMenu);
            AssertNoMusic(SceneNames.MainMenu);
            Assert.AreEqual(0, Object.FindObjectsByType<AudioDirector>(FindObjectsSortMode.None).Length, "메인 메뉴에 레이드 오디오 없음");

            yield return Load(SceneNames.RaidCrimsonBasin);
            AssertNoMusic(SceneNames.RaidCrimsonBasin);
            Assert.LessOrEqual(Object.FindObjectsByType<AudioDirector>(FindObjectsSortMode.None).Length, 1, "AudioDirector는 씬당 하나");
        }
    }
}
