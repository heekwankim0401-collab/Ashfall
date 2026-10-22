using System;

namespace Ashfall.EditorTools
{
    // float 샘플 → PCM16 RIFF WAV 바이트 (M6 설계 §2.1, 자리표시 소리). 순수 — 파일은 호출자(M6AudioSetup)가 쓴다.
    // 샘플은 [-1, 1]로 자르고(NaN = 0) 32767을 곱해 반올림한다. 다채널이면 샘플이 인터리브돼 있어야 한다.
    public static class WavWriter
    {
        public const int HeaderBytes = 44;
        public const int BitsPerSample = 16;

        public static byte[] EncodePcm16(float[] samples, int sampleRate, int channels = 1)
        {
            if (samples == null)
                throw new ArgumentNullException(nameof(samples));
            if (sampleRate <= 0)
                throw new ArgumentOutOfRangeException(nameof(sampleRate));
            if (channels <= 0)
                throw new ArgumentOutOfRangeException(nameof(channels));
            if (samples.Length % channels != 0)
                throw new ArgumentException("샘플 수가 채널 수의 배수가 아니다", nameof(samples));

            int blockAlign = channels * (BitsPerSample / 8);
            int dataBytes = samples.Length * (BitsPerSample / 8);
            var bytes = new byte[HeaderBytes + dataBytes];
            int p = 0;

            WriteAscii(bytes, ref p, "RIFF");
            WriteInt32(bytes, ref p, 36 + dataBytes);
            WriteAscii(bytes, ref p, "WAVE");

            WriteAscii(bytes, ref p, "fmt ");
            WriteInt32(bytes, ref p, 16);                       // fmt 청크 크기
            WriteInt16(bytes, ref p, 1);                        // PCM
            WriteInt16(bytes, ref p, (short)channels);
            WriteInt32(bytes, ref p, sampleRate);
            WriteInt32(bytes, ref p, sampleRate * blockAlign);  // byte rate
            WriteInt16(bytes, ref p, (short)blockAlign);
            WriteInt16(bytes, ref p, BitsPerSample);

            WriteAscii(bytes, ref p, "data");
            WriteInt32(bytes, ref p, dataBytes);
            for (int i = 0; i < samples.Length; i++)
                WriteInt16(bytes, ref p, ToPcm16(samples[i]));
            return bytes;
        }

        public static short ToPcm16(float sample)
        {
            if (float.IsNaN(sample))
                return 0;
            double v = sample;
            if (v > 1.0) v = 1.0;
            if (v < -1.0) v = -1.0;
            return (short)Math.Round(v * 32767.0, MidpointRounding.AwayFromZero);
        }

        private static void WriteAscii(byte[] b, ref int p, string s)
        {
            for (int i = 0; i < s.Length; i++)
                b[p++] = (byte)s[i];
        }

        private static void WriteInt32(byte[] b, ref int p, int v)
        {
            b[p++] = (byte)(v & 0xFF);
            b[p++] = (byte)((v >> 8) & 0xFF);
            b[p++] = (byte)((v >> 16) & 0xFF);
            b[p++] = (byte)((v >> 24) & 0xFF);
        }

        private static void WriteInt16(byte[] b, ref int p, short v)
        {
            b[p++] = (byte)(v & 0xFF);
            b[p++] = (byte)((v >> 8) & 0xFF);
        }
    }
}
