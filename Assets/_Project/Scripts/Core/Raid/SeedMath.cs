namespace Ashfall.Core
{
    // 레이드 시드 파생용 순수 정수 함수 (docs/design/MapDensity_design.md §5.3, ⓪ 계약).
    // string.GetHashCode는 런타임·플랫폼·실행마다 다를 수 있어 쓰지 않는다 — 같은 입력이면 어디서든 같은 값.
    // 결과 시드는 0이 되지 않는다(0 = "매번 무작위"라는 기존 약속과 섞이지 않게, LootContainer.seed 등).
    public static class SeedMath
    {
        private const uint FnvOffset = 2166136261u;
        private const uint FnvPrime = 16777619u;

        // FNV-1a 32비트, 문자열의 UTF-16 코드 단위를 하위·상위 바이트 순서로 넣는다. null·빈 문자열 = 오프셋 값.
        public static int StableHash(string text)
        {
            unchecked
            {
                uint hash = FnvOffset;
                if (text != null)
                    for (int i = 0; i < text.Length; i++)
                    {
                        char c = text[i];
                        hash ^= (byte)(c & 0xFF);
                        hash *= FnvPrime;
                        hash ^= (byte)(c >> 8);
                        hash *= FnvPrime;
                    }
                return (int)hash;
            }
        }

        // 두 값을 섞은 시드 (murmur3 finalizer). 순서가 다르면 다른 값. 0이면 1.
        public static int Mix(int a, int b)
        {
            unchecked
            {
                uint h = (uint)a * 0x9E3779B1u;
                h ^= (uint)b + 0x7F4A7C15u + (h << 6) + (h >> 2);
                h ^= h >> 16;
                h *= 0x85EBCA6Bu;
                h ^= h >> 13;
                h *= 0xC2B2AE35u;
                h ^= h >> 16;
                return NonZero((int)h);
            }
        }

        public static int NonZero(int seed) => seed == 0 ? 1 : seed;
    }
}
