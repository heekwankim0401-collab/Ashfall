using System;
using System.Collections.Generic;
using System.IO;
using Ashfall.Audio;
using Ashfall.Core;
using UnityEditor;
using UnityEngine;

namespace Ashfall.EditorTools
{
    // M6 자리표시 오디오 (M6 설계 §2.1·§2.2, Q18·Q21). 전부 TODO(GDD-?) 자리표시 — 실제 에셋 교체 위치는 설계 §2.3.
    // ① WAV 27개(M6 Recipes) + M7 13개(M7Recipes: 산탄·저격·경기관총 총성 근/중/원, 헛격발·고장 해제, 변이체 울음·공격)를 SfxSynth로 합성해 Audio/Placeholder/...에 쓴다
    //    — **파일이 없을 때만**(Q21: 사용자가 같은 경로에 덮어쓴 WAV 보존).
    // ② 임포트 설정: 모노 강제, 1초 미만 = DecompressOnLoad, 루프·긴 소리 = CompressedInMemory(Vorbis 0.7), preload.
    //    **이번에 새로 쓴 파일에만** 적용한다(리뷰 낮음 3 — 덮어쓴 사용자 WAV의 임포트 설정, 예: 스테레오 루프를 보존). 값이 다를 때만 다시 임포트.
    // ③ Data/Audio/AudioConfig.asset(1b가 만든다, 없으면 M6 데이터 메뉴를 먼저 부른다)의 **빈 클립 필드만** 채운다(Q21).
    // 멱등: 두 번째 실행은 파일·GUID를 바꾸지 않는다.
    // 배치모드: -executeMethod Ashfall.EditorTools.M6AudioSetup.CreateAssets
    public static class M6AudioSetup
    {
        private const string Tag = "[M6AudioSetup]";

        public const int SampleRate = SfxSynth.DefaultRate;
        public const int FootstepVariants = 3;
        public const int CollapseVariants = 2;
        // 이보다 짧으면 DecompressOnLoad (설계 §2.1 "1초 미만")
        public const float ShortClipSec = 1f;
        public const float VorbisQuality = 0.7f;

        public static readonly SurfaceKind[] Surfaces = { SurfaceKind.Dirt, SurfaceKind.Metal, SurfaceKind.Debris, SurfaceKind.Water };
        // M6 세트 (기존 에셋에 세트가 없으면 경고만 — 사용자 구성 보존)
        public static readonly WeaponClass[] GunshotClasses = { WeaponClass.Pistol, WeaponClass.Smg, WeaponClass.AssaultRifle };
        // M7 확장 세트 (7A-2c 산탄·저격, 7B-2c 경기관총 WPN-LMG-01). 기존 AudioConfig에 세트가 없으면 gunshots 끝에 추가한 뒤 빈 클립을 채운다(M7 설계 §2.1).
        // 새 분류(7B MachineGun)는 이 배열에 한 항목을 더하면 WAV 3개·세트·클립 연결이 함께 생긴다(음색은 SfxSynth.ClassParams, 없으면 기본).
        public static readonly WeaponClass[] M7GunshotClasses = { WeaponClass.Shotgun, WeaponClass.Sniper, WeaponClass.MachineGun };
        public static readonly GunshotLayer[] GunshotLayers = { GunshotLayer.Near, GunshotLayer.Mid, GunshotLayer.Far };

        public const string WindLoopPath = M6Names.AmbienceAudioFolder + "/AMB_Wind_Loop.wav";
        public const string StormLoopPath = M6Names.AmbienceAudioFolder + "/AMB_AshStorm_Loop.wav";
        public const string GeigerClickPath = M6Names.AmbienceAudioFolder + "/AMB_Geiger_Click.wav";
        public const string HideoutMusicPath = M6Names.MusicAudioFolder + "/MUS_Hideout_Loop.wav";

        // 루프로 쓰는 클립 (AudioSource.loop은 재생 쪽이 켠다 — 임포터에는 루프 설정이 없다)
        public static readonly string[] LoopPaths = { WindLoopPath, StormLoopPath, HideoutMusicPath };

        public static string FootstepPath(SurfaceKind kind, int variant) =>
            $"{M6Names.FootstepAudioFolder}/FS_{kind}_{variant + 1:00}.wav";

        public static string GunshotPath(WeaponClass cls, GunshotLayer layer) =>
            $"{M6Names.GunshotAudioFolder}/GS_{ClassFileName(cls)}_{layer}.wav";

        public static string CollapsePath(int variant) =>
            $"{M6Names.AmbienceAudioFolder}/AMB_MetalCollapse_{variant + 1:00}.wav";

        // 파일 이름 (설계 §2.2: GS_Rifle_*, GS_Shotgun_*, GS_Sniper_*, 7B GS_Lmg_*)
        private static string ClassFileName(WeaponClass cls)
        {
            switch (cls)
            {
                case WeaponClass.AssaultRifle: return "Rifle";
                case WeaponClass.MachineGun: return "Lmg";
                default: return cls.ToString();
            }
        }

        // 모든 총성 세트 분류 (M6 + M7)
        public static WeaponClass[] AllGunshotClasses()
        {
            var all = new WeaponClass[GunshotClasses.Length + M7GunshotClasses.Length];
            GunshotClasses.CopyTo(all, 0);
            M7GunshotClasses.CopyTo(all, GunshotClasses.Length);
            return all;
        }

        // M7 자리표시 WAV (7A-2c·7B-2c): 확장 총성 세트(산탄·저격·경기관총) + 무기 클릭 2개 + 변이체 울음·공격 2개(M7Names 경로)
        public static IReadOnlyList<(string path, Func<float[]> synth)> M7Recipes()
        {
            var list = new List<(string, Func<float[]>)>();
            AddGunshotRecipes(list, M7GunshotClasses);
            list.Add((M7Names.DryFireClipPath, () => SfxSynth.DryFireClick(SampleRate)));
            list.Add((M7Names.JamClearClipPath, () => SfxSynth.JamClear(SampleRate)));
            // 7B-2c 변이체 울음·공격 (EnemyVocalized, Q26). 계약 경로는 종류마다 1개(설계 §2.2 표의 _01~02 대신 M7Names 우선)
            list.Add((M7Names.MutantAlertClipPath, () => SfxSynth.MutantAlert(SampleRate)));
            list.Add((M7Names.MutantAttackClipPath, () => SfxSynth.MutantAttack(SampleRate)));
            return list;
        }

        // 메뉴가 쓰는 전체 레시피 (M6 + M7)
        public static IReadOnlyList<(string path, Func<float[]> synth)> AllRecipes()
        {
            var list = new List<(string, Func<float[]>)>(Recipes());
            list.AddRange(M7Recipes());
            return list;
        }

        private static void AddGunshotRecipes(List<(string, Func<float[]>)> list, WeaponClass[] classes)
        {
            foreach (WeaponClass cls in classes)
                foreach (GunshotLayer layer in GunshotLayers)
                {
                    WeaponClass c = cls;
                    GunshotLayer l = layer;
                    list.Add((GunshotPath(c, l), () => SfxSynth.Gunshot(c, l, SampleRate)));
                }
        }

        // 경로 → 합성 레시피 (M6 자리표시 WAV 27개. M7 추가분은 M7Recipes, 메뉴는 AllRecipes)
        public static IReadOnlyList<(string path, Func<float[]> synth)> Recipes()
        {
            var list = new List<(string, Func<float[]>)>();
            foreach (SurfaceKind kind in Surfaces)
                for (int v = 0; v < FootstepVariants; v++)
                {
                    SurfaceKind k = kind;
                    int variant = v;
                    list.Add((FootstepPath(k, variant), () => SfxSynth.Footstep(k, variant, SampleRate)));
                }
            foreach (WeaponClass cls in GunshotClasses)
                foreach (GunshotLayer layer in GunshotLayers)
                {
                    WeaponClass c = cls;
                    GunshotLayer l = layer;
                    list.Add((GunshotPath(c, l), () => SfxSynth.Gunshot(c, l, SampleRate)));
                }
            list.Add((WindLoopPath, () => SfxSynth.WindLoop(SampleRate)));
            list.Add((StormLoopPath, () => SfxSynth.StormLoop(SampleRate)));
            for (int v = 0; v < CollapseVariants; v++)
            {
                int variant = v;
                list.Add((CollapsePath(variant), () => SfxSynth.MetalCollapse(variant, SampleRate)));
            }
            list.Add((GeigerClickPath, () => SfxSynth.GeigerClick(SampleRate)));
            list.Add((HideoutMusicPath, () => SfxSynth.HideoutDrone(SampleRate)));
            return list;
        }

        [MenuItem("Ashfall/Setup/M6/Create Audio Assets")]
        public static void CreateAssets()
        {
            if (AssetDatabase.LoadAssetAtPath<AudioConfigSO>(M6Names.AudioConfigPath) == null)
                M6DataSetup.CreateData();

            // 임포트 설정은 이번에 새로 쓴 파일에만 강제한다 — 사용자가 같은 경로에 덮어쓴 WAV의 설정(스테레오 루프 등, 설계 §2.3)을
            // 다음 메뉴 실행이 되돌리지 않게(M6 리뷰 낮음 3).
            List<string> created = WritePlaceholderWavs(AllRecipes());
            int reimported = ApplyImportSettings(created);
            int written = created.Count;
            int filled = FillAudioConfig();
            AssetDatabase.SaveAssets();
            Debug.Log($"{Tag} 완료 — WAV 새로 씀 {written}, 임포트 설정 변경 {reimported}, AudioConfig 빈 필드 채움 {filled}");
        }

        // ---------- ① WAV (없을 때만) ----------

        // 레시피 중 파일이 없는 것만 합성해 쓰고 임포트한다. 새로 쓴 경로 목록을 돌려준다(테스트는 임시 폴더 레시피로 부른다).
        public static List<string> WritePlaceholderWavs(IReadOnlyList<(string path, Func<float[]> synth)> recipes)
        {
            var created = new List<string>();
            foreach (var (path, synth) in recipes)
            {
                string full = Path.GetFullPath(path);
                if (File.Exists(full))
                    continue;
                SetupUtil.EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
                File.WriteAllBytes(full, WavWriter.EncodePcm16(synth(), SampleRate));
                created.Add(path);
            }
            foreach (string path in created)
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            if (created.Count > 0)
                Debug.Log($"{Tag} 자리표시 WAV {created.Count}개 생성");
            return created;
        }

        // ---------- ② 임포트 설정 (새로 쓴 파일만) ----------

        // 주어진 경로의 임포트 설정을 자리표시 규칙으로 맞춘다. 바꾼 수.
        public static int ApplyImportSettings(IEnumerable<string> paths)
        {
            int changed = 0;
            foreach (string path in paths)
            {
                if (ConfigureImporter(path))
                    changed++;
            }
            return changed;
        }

        // 원하는 설정과 다를 때만 바꾸고 다시 임포트한다. 바꿨으면 true.
        public static bool ConfigureImporter(string path)
        {
            if (!(AssetImporter.GetAtPath(path) is AudioImporter importer))
            {
                Debug.LogWarning($"{Tag} 오디오 임포터 없음: {path}");
                return false;
            }
            AudioClipLoadType loadType = DesiredLoadType(path);
            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            bool dirty = false;
            if (!importer.forceToMono)
            {
                importer.forceToMono = true;
                dirty = true;
            }
            if (importer.loadInBackground)
            {
                importer.loadInBackground = false;
                dirty = true;
            }
            if (settings.loadType != loadType || settings.compressionFormat != AudioCompressionFormat.Vorbis ||
                !Mathf.Approximately(settings.quality, VorbisQuality) || !settings.preloadAudioData)
            {
                settings.loadType = loadType;
                settings.compressionFormat = AudioCompressionFormat.Vorbis;
                settings.quality = VorbisQuality;
                settings.preloadAudioData = true;
                importer.defaultSampleSettings = settings;
                dirty = true;
            }
            if (dirty)
                importer.SaveAndReimport();
            return dirty;
        }

        // 1초 미만 원샷 = DecompressOnLoad, 루프·긴 소리 = CompressedInMemory (WAV 헤더의 길이로 판단 — 임포트 전에도 같다)
        public static AudioClipLoadType DesiredLoadType(string path)
        {
            if (Array.IndexOf(LoopPaths, path) >= 0)
                return AudioClipLoadType.CompressedInMemory;
            float seconds = WavSeconds(path);
            return seconds < ShortClipSec ? AudioClipLoadType.DecompressOnLoad : AudioClipLoadType.CompressedInMemory;
        }

        // PCM WAV 길이 (초). 헤더를 읽지 못하면 임포트된 클립 길이.
        public static float WavSeconds(string path)
        {
            string full = Path.GetFullPath(path);
            try
            {
                using (var stream = File.OpenRead(full))
                using (var reader = new BinaryReader(stream))
                {
                    if (stream.Length >= WavWriter.HeaderBytes)
                    {
                        stream.Position = 22;
                        int channels = reader.ReadInt16();
                        int rate = reader.ReadInt32();
                        stream.Position = 34;
                        int bits = reader.ReadInt16();
                        stream.Position = 40;
                        int dataBytes = reader.ReadInt32();
                        int frameBytes = channels * bits / 8;
                        if (rate > 0 && frameBytes > 0)
                            return dataBytes / (float)(frameBytes * rate);
                    }
                }
            }
            catch (IOException)
            {
            }
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            return clip != null ? clip.length : 0f;
        }

        // ---------- ③ AudioConfig 빈 클립 필드 (Q21) ----------

        public static int FillAudioConfig()
        {
            var config = SetupUtil.Load<AudioConfigSO>(M6Names.AudioConfigPath);
            if (config == null)
                return 0;
            return FillAudioConfig(config);
        }

        // 주어진 설정의 빈 클립 필드만 자리표시 WAV로 채운다(테스트는 메모리 인스턴스로 부른다 — 커밋된 AudioConfig를 건드리지 않게).
        public static int FillAudioConfig(AudioConfigSO config)
        {
            if (config == null)
                return 0;
            int filled = 0;

            if (config.footsteps != null)
            {
                foreach (SurfaceKind kind in Surfaces)
                {
                    SurfaceFootsteps set = FootstepMix.FindSurface(config, kind);
                    if (set == null)
                    {
                        Debug.LogWarning($"{Tag} AudioConfig.footsteps에 {kind} 세트가 없다 — 채우지 않음(사용자 구성 보존)");
                        continue;
                    }
                    if (set.clips != null && set.clips.Length > 0)
                        continue;
                    var clips = new AudioClip[FootstepVariants];
                    for (int v = 0; v < FootstepVariants; v++)
                        clips[v] = SetupUtil.Load<AudioClip>(FootstepPath(kind, v));
                    set.clips = clips;
                    filled++;
                }
            }

            foreach (WeaponClass cls in GunshotClasses)
            {
                GunshotSet set = GunshotMix.FindSet(config, cls);
                if (set == null)
                {
                    Debug.LogWarning($"{Tag} AudioConfig.gunshots에 {cls} 세트가 없다 — 채우지 않음(사용자 구성 보존)");
                    continue;
                }
                filled += FillGunshotSet(set, cls);
            }

            // M7 확장 세트: 없으면 끝에 추가(기존 세트·순서는 그대로), 그다음 빈 클립만 채운다
            foreach (WeaponClass cls in M7GunshotClasses)
            {
                GunshotSet set = GunshotMix.FindSet(config, cls);
                if (set == null)
                {
                    set = new GunshotSet(cls, 1f);
                    var list = new List<GunshotSet>(config.gunshots ?? new GunshotSet[0]) { set };
                    config.gunshots = list.ToArray();
                    filled++;
                    Debug.Log($"{Tag} AudioConfig.gunshots에 {cls} 세트 추가(M7)");
                }
                filled += FillGunshotSet(set, cls);
            }

            // M7 무기 클릭 (WeaponClicked)
            if (config.dryFireClip == null) { config.dryFireClip = SetupUtil.Load<AudioClip>(M7Names.DryFireClipPath); filled++; }
            if (config.jamClearClip == null) { config.jamClearClip = SetupUtil.Load<AudioClip>(M7Names.JamClearClipPath); filled++; }

            // M7 7B-2c 변이체 울음 (EnemyVocalized): 배열이 비었을 때만(사용자가 넣은 울음 여러 개 보존)
            if (IsEmpty(config.mutantAlertClips)) { config.mutantAlertClips = new[] { SetupUtil.Load<AudioClip>(M7Names.MutantAlertClipPath) }; filled++; }
            if (IsEmpty(config.mutantAttackClips)) { config.mutantAttackClips = new[] { SetupUtil.Load<AudioClip>(M7Names.MutantAttackClipPath) }; filled++; }

            if (config.windLoop == null) { config.windLoop = SetupUtil.Load<AudioClip>(WindLoopPath); filled++; }
            if (config.stormLoop == null) { config.stormLoop = SetupUtil.Load<AudioClip>(StormLoopPath); filled++; }
            if (config.collapseClips == null || config.collapseClips.Length == 0)
            {
                var clips = new AudioClip[CollapseVariants];
                for (int v = 0; v < CollapseVariants; v++)
                    clips[v] = SetupUtil.Load<AudioClip>(CollapsePath(v));
                config.collapseClips = clips;
                filled++;
            }
            if (config.geigerClick == null) { config.geigerClick = SetupUtil.Load<AudioClip>(GeigerClickPath); filled++; }
            if (config.hideoutMusic == null) { config.hideoutMusic = SetupUtil.Load<AudioClip>(HideoutMusicPath); filled++; }

            if (filled > 0)
            {
                EditorUtility.SetDirty(config);
                Debug.Log($"{Tag} AudioConfig 빈 클립 필드 {filled}개 채움");
            }
            return filled;
        }

        // 비었다 = null, 길이 0, 또는 전부 null (빈 칸만 있는 배열도 채운다)
        private static bool IsEmpty(AudioClip[] clips)
        {
            if (clips == null)
                return true;
            foreach (AudioClip clip in clips)
                if (clip != null)
                    return false;
            return true;
        }

        // 세트의 빈 근/중/원 클립만 자리표시로. 채운 수.
        private static int FillGunshotSet(GunshotSet set, WeaponClass cls)
        {
            int filled = 0;
            if (set.near == null) { set.near = SetupUtil.Load<AudioClip>(GunshotPath(cls, GunshotLayer.Near)); filled++; }
            if (set.mid == null) { set.mid = SetupUtil.Load<AudioClip>(GunshotPath(cls, GunshotLayer.Mid)); filled++; }
            if (set.far == null) { set.far = SetupUtil.Load<AudioClip>(GunshotPath(cls, GunshotLayer.Far)); filled++; }
            return filled;
        }
    }
}
