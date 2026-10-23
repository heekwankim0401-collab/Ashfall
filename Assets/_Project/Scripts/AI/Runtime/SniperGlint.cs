using UnityEngine;
using UnityEngine.Rendering;

namespace Ashfall.AI
{
    // 저격 섬광 (§7 AI-SNP "레이저 반사 신호", M7 설계 §1.4, Q23). 저격수 자식 오브젝트(M7Names.SniperGlintObject = "SniperGlint")의
    // 발광 쿼드(머티리얼 M_SniperGlint는 배치 메뉴가 붙인다)에 붙는다. EnemyAgent가 Awake에서 자식 중 이 컴포넌트를 한 번 찾고,
    // EnemyIntent.Telegraph일 때만 켠 뒤 매 프레임 Face(표적 눈, AiConfigSO.glintAngularSizeDeg)를 부른다. 사망 시 끈다.
    // - 켜기/끄기 = 같은 오브젝트의 Renderer.enabled (오브젝트 자체는 켜 둔다 — GetComponentInChildren이 항상 찾게)
    // - 그림자·그림자 받기 끔, 콜라이더가 있으면 끔(탄·시야 레이를 막지 않게). 배치 메뉴는 콜라이더를 붙이지 않는 것이 원칙.
    // - Face: 쿼드 앞면(Unity Quad는 -Z 쪽에서 보인다)이 눈을 향하게, 월드 크기 = 거리 × tan(각) (먼 거리에서도 몇 픽셀).
    //   부모 배율은 나눠서 상쇄한다. 위치는 배치된 로컬 위치 그대로(저격수 머리 앞).
    // Update 없음, 할당 없음.
    [DisallowMultipleComponent]
    public sealed class SniperGlint : MonoBehaviour
    {
        private Renderer quad;
        private bool prepared;

        public bool IsOn { get; private set; }

        // 마지막 Face의 월드 한 변 크기(m). 테스트·디버그용
        public float WorldSize { get; private set; }

        private void Awake()
        {
            Prepare();
            ApplyVisibility();
        }

        // 같은 값이면 아무것도 하지 않는다(매 프레임 불러도 된다).
        // EnemyAgent.Awake가 이 컴포넌트의 Awake보다 먼저 부를 수 있어 처음 한 번은 렌더러 상태를 반드시 맞춘다.
        public void SetActive(bool on)
        {
            Prepare();
            if (IsOn == on && quad != null && quad.enabled == on)
                return;
            IsOn = on;
            ApplyVisibility();
        }

        // 켜져 있을 때 매 프레임: 표적 눈 쪽을 보고, 크기 = 거리 × tan(angularSizeDeg).
        public void Face(Vector3 eyePosition, float angularSizeDeg)
        {
            Transform t = transform;
            Vector3 away = t.position - eyePosition;   // 쿼드 +Z가 눈 반대쪽 → 보이는 면(-Z)이 눈 쪽
            float distance = away.magnitude;
            if (distance < 1e-3f)
                return;
            t.rotation = Quaternion.LookRotation(away / distance, Vector3.up);

            float size = distance * Mathf.Tan(Mathf.Max(0f, angularSizeDeg) * Mathf.Deg2Rad);
            WorldSize = size;
            Vector3 parentScale = t.parent != null ? t.parent.lossyScale : Vector3.one;
            t.localScale = new Vector3(Divide(size, parentScale.x), Divide(size, parentScale.y), Divide(size, parentScale.z));
        }

        private static float Divide(float size, float scale) => Mathf.Abs(scale) > 1e-6f ? size / scale : size;

        private void Prepare()
        {
            if (prepared)
                return;
            prepared = true;
            quad = GetComponent<Renderer>();
            if (quad != null)
            {
                quad.shadowCastingMode = ShadowCastingMode.Off;
                quad.receiveShadows = false;
            }
            if (TryGetComponent(out Collider c))
                c.enabled = false;
        }

        private void ApplyVisibility()
        {
            if (quad != null)
                quad.enabled = IsOn;
        }
    }
}
