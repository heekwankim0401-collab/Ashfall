using UnityEngine;

namespace Ashfall.Weapons
{
    // 정조준 뷰모델 자세 (정조준·탄도 AB-Q1, docs/design/AdsBallistics_design.md §2).
    // 뷰모델 자식 SightAnchor = 가늠자 뒤쪽 기준점, 앵커 +z = 가늠자 → 가늠쇠 시선. 정조준(AimProgress 1)에서 앵커가 눈(카메라) 정면 축 위
    // eyeRelief 앞에 오고 앵커 +z가 카메라 정면과 같도록 뷰모델 루트 자세를 무기 컨트롤러 로컬로 구한다.
    // 좌표: E = 눈(카메라) 로컬, C = 흔들림 없는 컨트롤러 로컬. eyeFromController = E ← C 강체 변환(보통 항등 — 컨트롤러가 카메라 바로 아래 원점).
    public static class AdsAlignment
    {
        public readonly struct Pose
        {
            public readonly Vector3 Position;
            public readonly Quaternion Rotation;

            public Pose(Vector3 position, Quaternion rotation)
            {
                Position = position;
                Rotation = rotation;
            }
        }

        // anchorInRoot: 뷰모델 루트 로컬의 앵커 위치(루트 배율 적용 후 = 루트 부모 단위), anchorRotationInRoot: 루트 기준 앵커 회전.
        public static Pose ViewRootPose(Vector3 eyeFromControllerPosition, Quaternion eyeFromControllerRotation,
            Vector3 anchorInRoot, Quaternion anchorRotationInRoot, float eyeReliefM)
        {
            Quaternion controllerFromEye = Quaternion.Inverse(eyeFromControllerRotation);
            // 앵커 회전(E) = qC · q · r = 항등 → q = qC⁻¹ · r⁻¹
            Quaternion rootRotation = controllerFromEye * Quaternion.Inverse(anchorRotationInRoot);
            // 앵커 위치(E) = qC · (p + q · a) + tC = (0, 0, e)
            Vector3 anchorTargetInController = controllerFromEye * (new Vector3(0f, 0f, eyeReliefM) - eyeFromControllerPosition);
            Vector3 rootPosition = anchorTargetInController - rootRotation * anchorInRoot;
            return new Pose(rootPosition, rootRotation);
        }

        // 뷰모델 루트 자세(C)에서 루트 로컬 점(배율 적용 후)을 눈 좌표(E)로
        public static Vector3 ToEye(Vector3 eyeFromControllerPosition, Quaternion eyeFromControllerRotation, Pose root, Vector3 pointInRoot) =>
            eyeFromControllerRotation * (root.Position + root.Rotation * pointInRoot) + eyeFromControllerPosition;

        // 눈(E) ← 흔들림 없는 컨트롤러(C) 변환을 월드 값으로 구한다 (Transform.InverseTransformPoint와 같은 식, 눈 배율 포함).
        // controllerRotation = 컨트롤러 부모 월드 회전 × 흔들림 전 로컬 회전.
        public static void EyeFromController(Vector3 eyePosition, Quaternion eyeRotation, Vector3 eyeLossyScale,
            Vector3 controllerPosition, Quaternion controllerRotation, out Vector3 position, out Quaternion rotation)
        {
            Quaternion inverseEye = Quaternion.Inverse(eyeRotation);
            rotation = inverseEye * controllerRotation;
            Vector3 local = inverseEye * (controllerPosition - eyePosition);
            position = new Vector3(SafeDiv(local.x, eyeLossyScale.x), SafeDiv(local.y, eyeLossyScale.y), SafeDiv(local.z, eyeLossyScale.z));
        }

        private static float SafeDiv(float a, float b) => Mathf.Abs(b) > 1e-6f ? a / b : a;

        // 정조준 계산 입력 (모두 값 — 순수 함수 Solve가 Transform 없이 계산한다)
        public struct Input
        {
            public Vector3 EyeFromControllerPosition;
            public Quaternion EyeFromControllerRotation;
            public Vector3 FallbackPosition;        // 컨트롤러 adsLocalPosition
            public bool HasAnchor;
            public Vector3 AnchorInRoot;            // 루트 배율 적용 후
            public Quaternion AnchorRotationInRoot;
            public float EyeReliefM;                // 0 이하 = 폴백
            public bool HasMuzzle;
            public Vector3 MuzzleInRoot;            // 루트 배율 적용 후
            public float ZeroRangeM;                // 0 이하 = 영점 없음
            public float Speed, Gravity, DragPerM, FixedDt, MaxFlightSec;
        }

        public struct Result
        {
            public Pose Root;                       // 정조준 뷰모델 루트 자세 (컨트롤러 로컬)
            public bool HasZero;
            public Vector3 ZeroDirection;           // 흔들림 없는 컨트롤러 로컬 발사 방향
            public bool ZeroFailed;                 // 영점을 구하려 했지만 못 구함 (경고용)
        }

        // 정조준 자세 + 영점 방향 (WeaponController가 장착 시 1회 부른다). 할당 없음.
        public static Result Solve(in Input input)
        {
            var result = new Result { Root = new Pose(input.FallbackPosition, Quaternion.identity) };
            if (input.HasAnchor && input.EyeReliefM > 0f)
                result.Root = ViewRootPose(input.EyeFromControllerPosition, input.EyeFromControllerRotation,
                    input.AnchorInRoot, input.AnchorRotationInRoot, input.EyeReliefM);

            if (input.HasMuzzle && input.ZeroRangeM > 0f)
            {
                Vector3 muzzleInEye = ToEye(input.EyeFromControllerPosition, input.EyeFromControllerRotation, result.Root, input.MuzzleInRoot);
                if (BallisticZero.TrySolve(muzzleInEye, input.ZeroRangeM, input.Speed, input.Gravity, input.DragPerM,
                        input.FixedDt, input.MaxFlightSec, out Vector3 eyeDirection, out _))
                {
                    result.HasZero = true;
                    result.ZeroDirection = Quaternion.Inverse(input.EyeFromControllerRotation) * eyeDirection;
                }
                else
                {
                    result.ZeroFailed = true;
                }
            }
            return result;
        }
    }
}
