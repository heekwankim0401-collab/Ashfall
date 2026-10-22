using System;
using System.Text;
using Ashfall.EditorTools;
using NUnit.Framework;

namespace Ashfall.Tests.EditMode
{
    // M6 2b: 자리표시 WAV 인코더 (설계 §2.1 PCM16 RIFF)
    public class WavWriterTests
    {
        private static string Ascii(byte[] b, int offset) => Encoding.ASCII.GetString(b, offset, 4);
        private static int I32(byte[] b, int offset) => BitConverter.ToInt32(b, offset);
        private static short I16(byte[] b, int offset) => BitConverter.ToInt16(b, offset);

        [Test]
        public void Header_RiffFmtData_SizesMatch()
        {
            var samples = new float[100];
            byte[] b = WavWriter.EncodePcm16(samples, 22050);

            Assert.AreEqual(44 + 200, b.Length);
            Assert.AreEqual("RIFF", Ascii(b, 0));
            Assert.AreEqual(b.Length - 8, I32(b, 4), "RIFF 크기 = 전체 − 8");
            Assert.AreEqual("WAVE", Ascii(b, 8));
            Assert.AreEqual("fmt ", Ascii(b, 12));
            Assert.AreEqual(16, I32(b, 16));
            Assert.AreEqual(1, I16(b, 20), "PCM");
            Assert.AreEqual(1, I16(b, 22), "모노");
            Assert.AreEqual(22050, I32(b, 24));
            Assert.AreEqual(22050 * 2, I32(b, 28), "byte rate");
            Assert.AreEqual(2, I16(b, 32), "block align");
            Assert.AreEqual(16, I16(b, 34), "bits");
            Assert.AreEqual("data", Ascii(b, 36));
            Assert.AreEqual(200, I32(b, 40));
        }

        [Test]
        public void Stereo_ByteRateAndBlockAlign()
        {
            byte[] b = WavWriter.EncodePcm16(new float[8], 44100, 2);
            Assert.AreEqual(2, I16(b, 22));
            Assert.AreEqual(44100 * 4, I32(b, 28));
            Assert.AreEqual(4, I16(b, 32));
            Assert.AreEqual(16, I32(b, 40));
        }

        [Test]
        public void Samples_ClampedAndRounded_LittleEndian()
        {
            byte[] b = WavWriter.EncodePcm16(new[] { 0f, 1f, -1f, 2f, -3f, 0.5f, float.NaN }, 8000);
            Assert.AreEqual(0, I16(b, 44));
            Assert.AreEqual(32767, I16(b, 46));
            Assert.AreEqual(-32767, I16(b, 48));
            Assert.AreEqual(32767, I16(b, 50), "1 초과는 자른다");
            Assert.AreEqual(-32767, I16(b, 52), "−1 미만은 자른다");
            Assert.AreEqual(16384, I16(b, 54), "0.5 × 32767 반올림");
            Assert.AreEqual(0, I16(b, 56), "NaN = 0");
        }

        [Test]
        public void InvalidArguments_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => WavWriter.EncodePcm16(null, 22050));
            Assert.Throws<ArgumentOutOfRangeException>(() => WavWriter.EncodePcm16(new float[2], 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => WavWriter.EncodePcm16(new float[2], 22050, 0));
            Assert.Throws<ArgumentException>(() => WavWriter.EncodePcm16(new float[3], 22050, 2));
        }

        [Test]
        public void Empty_IsHeaderOnly()
        {
            byte[] b = WavWriter.EncodePcm16(new float[0], 22050);
            Assert.AreEqual(44, b.Length);
            Assert.AreEqual(0, I32(b, 40));
        }
    }
}
