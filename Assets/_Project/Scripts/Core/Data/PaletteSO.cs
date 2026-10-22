using UnityEngine;

namespace Ashfall.Core
{
    // §10.1 팔레트 토큰 7개 (M6 설계 §3.1). 선언 순서 = GDD 표 순서.
    public enum PaletteToken
    {
        AshBlack,
        DriedBlood,
        RustRed,
        Ember,
        AshGrey,
        Bone,
        Toxic,
    }

    // §14 PaletteSO — 모든 머티리얼·안개·하늘 색의 원본 (M6 설계 §1.3, Q6). 에셋: Data/Art/Palette.asset.
    // 색은 sRGB(인스펙터·ColorUtility와 같은 공간)로 저장한다. 필드 초기값 = §10.1 HEX(CreateInstance만 해도 GDD 값).
    [CreateAssetMenu(menuName = "Ashfall/Palette", fileName = "Palette")]
    public class PaletteSO : ScriptableObject
    {
        [Tooltip("Ash_Black #0D0807 — 그림자, UI 배경")]
        public Color ashBlack = FromHex(0x0D0807);
        [Tooltip("Dried_Blood #3A0B0B — 지형 기본, 안개 원경")]
        public Color driedBlood = FromHex(0x3A0B0B);
        [Tooltip("Rust_Red #6E1A14 — 하늘, 구조물 녹")]
        public Color rustRed = FromHex(0x6E1A14);
        [Tooltip("Ember #B2321C — 강조, 위험 표시")]
        public Color ember = FromHex(0xB2321C);
        [Tooltip("Ash_Grey #5A524E — 재, 콘크리트")]
        public Color ashGrey = FromHex(0x5A524E);
        [Tooltip("Bone #C9BBA8 — UI 텍스트, 하이라이트")]
        public Color bone = FromHex(0xC9BBA8);
        [Tooltip("Toxic #7A8A2A — 방사능 구역 (유일한 비적색 강조)")]
        public Color toxic = FromHex(0x7A8A2A);

        // 토큰 → 색. 알 수 없는 값이면 마젠타(눈에 띄게).
        public Color Get(PaletteToken token)
        {
            switch (token)
            {
                case PaletteToken.AshBlack: return ashBlack;
                case PaletteToken.DriedBlood: return driedBlood;
                case PaletteToken.RustRed: return rustRed;
                case PaletteToken.Ember: return ember;
                case PaletteToken.AshGrey: return ashGrey;
                case PaletteToken.Bone: return bone;
                case PaletteToken.Toxic: return toxic;
                default: return Color.magenta;
            }
        }

        // §10.1 표기 이름 ("Ash_Black" …). USS 변수·로그·테스트에서 쓴다.
        public static string GddName(PaletteToken token)
        {
            switch (token)
            {
                case PaletteToken.AshBlack: return "Ash_Black";
                case PaletteToken.DriedBlood: return "Dried_Blood";
                case PaletteToken.RustRed: return "Rust_Red";
                case PaletteToken.Ember: return "Ember";
                case PaletteToken.AshGrey: return "Ash_Grey";
                case PaletteToken.Bone: return "Bone";
                case PaletteToken.Toxic: return "Toxic";
                default: return token.ToString();
            }
        }

        // §10.1 HEX ("#0D0807" …, 대문자). 데이터 메뉴·테스트가 GDD 값의 기준으로 쓴다.
        public static string GddHex(PaletteToken token)
        {
            switch (token)
            {
                case PaletteToken.AshBlack: return "#0D0807";
                case PaletteToken.DriedBlood: return "#3A0B0B";
                case PaletteToken.RustRed: return "#6E1A14";
                case PaletteToken.Ember: return "#B2321C";
                case PaletteToken.AshGrey: return "#5A524E";
                case PaletteToken.Bone: return "#C9BBA8";
                case PaletteToken.Toxic: return "#7A8A2A";
                default: return "#FF00FF";
            }
        }

        // 0xRRGGBB → 불투명 sRGB 색. ScriptableObject 생성자(필드 초기화)에서도 쓸 수 있게 Unity API를 부르지 않는다.
        private static Color FromHex(int rgb)
        {
            return new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
        }
    }
}
