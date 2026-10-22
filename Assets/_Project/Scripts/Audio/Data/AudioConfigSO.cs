using System;
using Ashfall.Core;
using UnityEngine;

namespace Ashfall.Audio
{
    // 재질 하나의 발소리 세트 (§11). clips는 무작위(직전과 다르게) 재생.
    [Serializable]
    public class SurfaceFootsteps
    {
        public SurfaceKind surface;
        public AudioClip[] clips = new AudioClip[0];
        [Range(0f, 1f)] public float volume = 1f;

        public SurfaceFootsteps() { }

        public SurfaceFootsteps(SurfaceKind surface, float volume)
        {
            this.surface = surface;
            this.volume = volume;
        }
    }

    // 무기 분류 하나의 총성 세트 (§11 거리 레이어 근/중/원, Q15).
    [Serializable]
    public class GunshotSet
    {
        public WeaponClass weaponClass;
        [Tooltip("근거리 (≤ nearMaxM)")]
        public AudioClip near;
        [Tooltip("중거리 (≤ midMaxM)")]
        public AudioClip mid;
        [Tooltip("원거리 메아리 꼬리 (≤ farMaxM)")]
        public AudioClip far;
        [Range(0f, 1f)] public float volume = 1f;

        public GunshotSet() { }

        public GunshotSet(WeaponClass weaponClass, float volume)
        {
            this.weaponClass = weaponClass;
            this.volume = volume;
        }
    }

    // 걸음 종류별 값 하나씩 (FootstepGait 순서와 같은 필드).
    [Serializable]
    public struct GaitValues
    {
        public float walk;
        public float sprint;
        public float crouch;
        public float prone;

        public GaitValues(float walk, float sprint, float crouch, float prone)
        {
            this.walk = walk;
            this.sprint = sprint;
            this.crouch = crouch;
            this.prone = prone;
        }

        public float Get(FootstepGait gait)
        {
            switch (gait)
            {
                case FootstepGait.Sprint: return sprint;
                case FootstepGait.Crouch: return crouch;
                case FootstepGait.Prone: return prone;
                default: return walk;
            }
        }
    }

    // 오디오 수치·클립 (§11, M6 설계 §3.5). 에셋: Data/Audio/AudioConfig.asset (M6 오디오 메뉴가 만들고 빈 클립 필드만 자리표시로 채운다, Q21).
    // 음량은 순수 함수가 계산하고 Unity는 방향(팬)만 준다(3D, 감쇠 없는 롤오프). AudioMixer 없음(Q20).
    // 필드 초기값 = 설계값, 전부 TODO(GDD-?) 임시값.
    [CreateAssetMenu(menuName = "Ashfall/Audio Config", fileName = "AudioConfig")]
    public class AudioConfigSO : ScriptableObject
    {
        [Header("지면 판정 (Q11)")]
        [Tooltip("발 위치 위에서 아래로 쏘는 레이 길이 (m)")]
        public float surfaceProbeLengthM = 1.5f;
        [Tooltip("레이 시작 높이 = 발 위치 + 이 값 (m). 경사·계단에서 발이 지면에 살짝 묻혀도 맞도록. TODO(GDD-?) Q11")]
        public float surfaceProbeLiftM = 0.5f;
        [Tooltip("판정 레이어 (Default + Ignore Raycast — 물웅덩이 트리거 포함)")]
        public LayerMask surfaceMask = (1 << 0) | (1 << 2);
        [Tooltip("GroundSurface 없는 Terrain")]
        public SurfaceKind terrainSurface = SurfaceKind.Dirt;
        [Tooltip("GroundSurface 없는 그 밖 콜라이더")]
        public SurfaceKind untaggedSurface = SurfaceKind.Debris;

        [Header("발소리 (§11)")]
        public SurfaceFootsteps[] footsteps =
        {
            new SurfaceFootsteps(SurfaceKind.Dirt, 1f),
            new SurfaceFootsteps(SurfaceKind.Metal, 1f),
            new SurfaceFootsteps(SurfaceKind.Debris, 1f),
            new SurfaceFootsteps(SurfaceKind.Water, 1f),
        };
        [Tooltip("걸음별 음량 배율")]
        public GaitValues gaitVolume = new GaitValues(0.7f, 1.0f, 0.35f, 0.2f);
        [Tooltip("걸음별 들리는 거리 (m). 이보다 멀면 재생·레이캐스트 없음")]
        public GaitValues gaitAudibleM = new GaitValues(25f, 40f, 10f, 5f);
        [Tooltip("내 발소리 음량 배율")]
        [Range(0f, 1f)] public float playerSelfVolume = 0.6f;
        [Tooltip("피치 무작위 ± 비율")]
        [Range(0f, 0.5f)] public float pitchJitter = 0.06f;

        [Header("총성 (§11 거리 레이어, Q15)")]
        public GunshotSet[] gunshots =
        {
            new GunshotSet(WeaponClass.Pistol, 1f),
            new GunshotSet(WeaponClass.Smg, 1f),
            new GunshotSet(WeaponClass.AssaultRifle, 1f),
            // M7 산탄·저격 세트(7A-2c)는 코드 기본값에 넣지 않는다 — 기존 테스트가 "세트 없는 분류 → 대체"를 기본 인스턴스로 확인한다.
            // 에셋에는 M6 오디오 메뉴(M6AudioSetup.M7GunshotClasses)가 없는 세트를 끝에 추가한다(M7 설계 §2.1). 7B MachineGun도 같은 방식.
        };
        [Tooltip("무기 정의가 없거나 세트가 없는 분류일 때 쓰는 세트")]
        public WeaponClass fallbackGunshotClass = WeaponClass.AssaultRifle;
        [Tooltip("근거리 레이어 끝 (m)")]
        public float nearMaxM = 50f;
        [Tooltip("중거리 레이어 끝 (m)")]
        public float midMaxM = 250f;
        [Tooltip("원거리 레이어 끝 (m). 이보다 멀면 안 들림")]
        public float farMaxM = 1200f;
        [Tooltip("0m 게인 (구간 선형)")]
        public float gainAt0 = 1.0f;
        [Tooltip("nearMaxM 게인")]
        public float gainAtNear = 0.7f;
        [Tooltip("midMaxM 게인")]
        public float gainAtMid = 0.35f;
        [Tooltip("farMaxM 게인")]
        public float gainAtFar = 0.1f;

        [Header("무기 클릭 (M7 WeaponClicked, 설계 §1.2·§4.9, Q10 — 소리만)")]
        [Tooltip("공이 헛격발 클릭 (DryFire·Jammed가 같이 쓴다)")]
        public AudioClip dryFireClip;
        [Tooltip("고장 해제 완료 (슬라이드 긁힘 + 클릭)")]
        public AudioClip jamClearClip;
        [Tooltip("헛격발·고장 클릭 음량. TODO(GDD-?)")]
        [Range(0f, 1f)] public float dryFireVolume = 0.8f;
        [Tooltip("고장 해제 음량. TODO(GDD-?)")]
        [Range(0f, 1f)] public float jamClearVolume = 0.8f;

        [Header("voice 풀")]
        [Tooltip("효과음 AudioSource 수 (Awake 1회 생성)")]
        public int voiceCount = 24;

        [Header("서비스 조회 (기술 값, TODO(GDD-?))")]
        [Tooltip("IPlayerTarget·IAshStormInfo 재조회 주기 (초, 설계 §4.3)")]
        public float serviceRetrySec = 0.5f;
        [Tooltip("표적이 없을 때 씬의 AudioListener를 다시 찾는 주기 (초). 못 찾으면 이 간격으로만 다시 찾는다")]
        public float listenerSearchRetrySec = 5f;

        [Header("환경음 (§11)")]
        public AudioClip windLoop;
        [Range(0f, 1f)] public float windVolume = 0.35f;
        [Tooltip("재 폭풍 최대 강도의 바람 음량")]
        [Range(0f, 1f)] public float windStormVolume = 0.8f;
        public AudioClip stormLoop;
        [Range(0f, 1f)] public float stormLoopMaxVolume = 0.7f;
        [Tooltip("멀리서 무너지는 금속")]
        public AudioClip[] collapseClips = new AudioClip[0];
        [Tooltip("금속 붕괴 간격 최소~최대 (초)")]
        public Vector2 collapseIntervalSec = new Vector2(40f, 120f);
        [Tooltip("청취자에서 떨어진 거리 최소~최대 (m)")]
        public Vector2 collapseDistanceM = new Vector2(150f, 400f);
        [Range(0f, 1f)] public float collapseVolume = 0.6f;
        [Tooltip("가이거 계수기 클릭 한 번 (간격은 코드가 만든다). Q22: 재 폭풍 방사능률에만 반응")]
        public AudioClip geigerClick;
        [Tooltip("방사능 1/s당 초당 클릭 수")]
        public float geigerClicksPerRad = 6f;
        public float geigerMaxClicksPerSec = 20f;
        [Range(0f, 1f)] public float geigerVolume = 0.5f;

        [Header("은신처 BGM (§11 BGM 없음(은신처 제외), Q18)")]
        public AudioClip hideoutMusic;
        [Range(0f, 1f)] public float hideoutMusicVolume = 0.4f;
    }
}
