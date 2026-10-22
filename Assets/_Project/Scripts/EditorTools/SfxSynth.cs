using System;
using Ashfall.Audio;
using Ashfall.Core;

namespace Ashfall.EditorTools
{
    // 자리표시 효과음 합성 (M6 설계 §2.1·§2.2). 순수·결정적: 레시피마다 고정 시드 System.Random + 사인·노이즈·1극 필터·엔벨로프.
    // 결과는 float 모노 샘플(피크 TargetPeak로 정규화). 루프는 끝을 처음과 교차 페이드해 이음매를 없앤다.
    // 전부 TODO(GDD-?) 자리표시 — 실제 에셋은 설계 §2.3 위치에 덮어쓴다.
    public static class SfxSynth
    {
        public const int DefaultRate = 22050;
        public const float TargetPeak = 0.9f;

        // 길이 (초, 설계 §2.2)
        public const float FootstepDirtSec = 0.18f;
        public const float FootstepMetalSec = 0.3f;
        public const float FootstepDebrisSec = 0.25f;
        public const float FootstepWaterSec = 0.35f;
        public const float GunshotNearSec = 0.5f;
        public const float GunshotMidSec = 0.9f;
        public const float GunshotFarSec = 2.5f;
        public const float LoopSec = 10f;
        public const float LoopFadeSec = 1f;
        public const float CollapseSec = 3.5f;
        public const float GeigerClickSec = 0.004f;
        public const float DroneSec = 30f;   // 화음 주파수가 30초에 정수 주기 → 이음매 없음
        public const float DryFireSec = 0.1f;   // M7 헛격발 클릭 (3ms 클릭 + 공진)
        public const float JamClearSec = 0.4f;  // M7 고장 해제

        private const double TwoPi = Math.PI * 2.0;

        // ---------- 길이 ----------

        public static int SampleCount(float seconds, int rate) => Math.Max(1, (int)Math.Round(seconds * (double)rate));

        public static float FootstepLengthSec(SurfaceKind kind)
        {
            switch (kind)
            {
                case SurfaceKind.Metal: return FootstepMetalSec;
                case SurfaceKind.Debris: return FootstepDebrisSec;
                case SurfaceKind.Water: return FootstepWaterSec;
                default: return FootstepDirtSec;
            }
        }

        public static float GunshotLengthSec(WeaponClass cls, GunshotLayer layer)
        {
            float baseSec = layer == GunshotLayer.Near ? GunshotNearSec : layer == GunshotLayer.Mid ? GunshotMidSec : GunshotFarSec;
            return baseSec * ClassParams(cls).LengthScale;
        }

        // ---------- 레시피 ----------

        public static float[] Footstep(SurfaceKind kind, int variant, int rate = DefaultRate)
        {
            var r = new Random(1000 + (int)kind * 97 + variant * 13);
            int n = SampleCount(FootstepLengthSec(kind), rate);
            var s = new float[n];
            switch (kind)
            {
                case SurfaceKind.Metal: FootstepMetal(s, variant, rate, r); break;
                case SurfaceKind.Debris: FootstepDebris(s, variant, rate, r); break;
                case SurfaceKind.Water: FootstepWater(s, variant, rate, r); break;
                default: FootstepDirt(s, variant, rate, r); break;
            }
            FadeOutTail(s, rate, 0.01);
            return Normalize(s);
        }

        public static float[] Gunshot(WeaponClass cls, GunshotLayer layer, int rate = DefaultRate)
        {
            var r = new Random(2000 + (int)cls * 31 + (int)layer * 7);
            var p = ClassParams(cls);
            int n = SampleCount(GunshotLengthSec(cls, layer), rate);
            var s = new float[n];
            switch (layer)
            {
                case GunshotLayer.Mid: GunshotMid(s, p, rate, r); break;
                case GunshotLayer.Far: GunshotFar(s, p, rate, r); break;
                default: GunshotNear(s, p, rate, r); break;
            }
            FadeOutTail(s, rate, 0.03);
            return Normalize(s);
        }

        // 저역 노이즈 + 느린 LFO, 10초 루프
        public static float[] WindLoop(int rate = DefaultRate)
        {
            var r = new Random(3001);
            int fade = SampleCount(LoopFadeSec, rate);
            var raw = new float[SampleCount(LoopSec, rate) + fade];
            var lp1 = new OnePole(350, rate);
            var lp2 = new OnePole(350, rate);
            for (int i = 0; i < raw.Length; i++)
            {
                double t = i / (double)rate;
                double lfo = 0.65 + 0.35 * Math.Sin(TwoPi * 0.11 * t + 0.7 * Math.Sin(TwoPi * 0.037 * t));
                raw[i] = (float)(lp2.Run(lp1.Run(Noise(r))) * lfo);
            }
            return Normalize(CrossfadeLoop(raw, fade));
        }

        // 밝은 노이즈 쉿 소리 + 돌풍 LFO + 낮은 울림, 10초 루프
        public static float[] StormLoop(int rate = DefaultRate)
        {
            var r = new Random(3002);
            int fade = SampleCount(LoopFadeSec, rate);
            var raw = new float[SampleCount(LoopSec, rate) + fade];
            var hiLp = new OnePole(5000, rate);
            var hiCut = new OnePole(600, rate);
            var rumble1 = new OnePole(120, rate);
            var rumble2 = new OnePole(120, rate);
            for (int i = 0; i < raw.Length; i++)
            {
                double t = i / (double)rate;
                double x = Noise(r);
                double bright = hiLp.Run(x);
                bright -= hiCut.Run(bright);
                double gust = 0.55 + 0.45 * Math.Sin(TwoPi * 0.23 * t) * Math.Sin(TwoPi * 0.07 * t + 1.0);
                double low = rumble2.Run(rumble1.Run(x)) * 4.0;
                raw[i] = (float)(bright * gust + low * 0.5);
            }
            return Normalize(CrossfadeLoop(raw, fade));
        }

        // 저음 우르릉 + 금속 공진 + 삐걱임 (3.5초)
        public static float[] MetalCollapse(int variant, int rate = DefaultRate)
        {
            var r = new Random(4000 + variant * 17);
            int n = SampleCount(CollapseSec, rate);
            var s = new float[n];
            var lp1 = new OnePole(90 + 20 * variant, rate);
            var lp2 = new OnePole(90 + 20 * variant, rate);
            double[] partials = { 180, 311, 527, 843 };
            double[] amps = { 0.5, 0.35, 0.25, 0.15 };
            double hit = 0.3 + 0.2 * variant;
            double creakPhase = 0;
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)rate;
                double rumble = lp2.Run(lp1.Run(Noise(r))) * 6.0 * Env(t, 0.25, 1.2);
                double metal = 0;
                if (t >= hit)
                {
                    double th = t - hit;
                    for (int k = 0; k < partials.Length; k++)
                        metal += amps[k] * Math.Sin(TwoPi * partials[k] * (1 + 0.05 * variant) * th) * Env(th, 0.01, 0.9 / (k + 1));
                }
                double creak = 0;
                if (t >= 1.2 && t <= 2.6)
                {
                    double u = (t - 1.2) / 1.4;
                    double freq = 210 - 50 * u + 6 * Math.Sin(TwoPi * 7 * t);
                    creakPhase += TwoPi * freq / rate;
                    double window = 0.5 - 0.5 * Math.Cos(TwoPi * u);
                    creak = (Math.Sin(creakPhase) + 0.5 * Math.Sin(2 * creakPhase)) * window * 0.25;
                }
                s[i] = (float)(rumble + metal + creak);
            }
            FadeOutTail(s, rate, 0.05);
            return Normalize(s);
        }

        // 4ms 클릭
        public static float[] GeigerClick(int rate = DefaultRate)
        {
            var r = new Random(5001);
            int n = SampleCount(GeigerClickSec, rate);
            var s = new float[n];
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)rate;
                s[i] = (float)((i == 0 ? 1.0 : 0.0) + Noise(r) * Math.Exp(-t / 0.0006));
            }
            FadeOutTail(s, rate, 0.001);
            return Normalize(s);
        }

        // M7 공이 헛격발: 3ms 금속 클릭 + 짧은 공진 (설계 §2.2, 0.1초)
        public static float[] DryFireClick(int rate = DefaultRate)
        {
            var r = new Random(6001);
            int n = SampleCount(DryFireSec, rate);
            var s = new float[n];
            var lp = new OnePole(1500, rate);
            double[] partials = { 2650, 4180, 6230 };
            double[] amps = { 1.0, 0.6, 0.35 };
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)rate;
                double x = Noise(r) * (t < 0.003 ? 1.0 : 0.0);
                double click = (x - lp.Run(x)) * 1.5;
                double ring = 0;
                for (int k = 0; k < partials.Length; k++)
                    ring += amps[k] * Math.Sin(TwoPi * partials[k] * t) * Env(t, 0.0005, 0.025 / (k + 1));
                s[i] = (float)(click + ring * 0.5);
            }
            FadeOutTail(s, rate, 0.01);
            return Normalize(s);
        }

        // M7 고장 해제: 슬라이드 긁힘 노이즈 + 클릭 2개 (설계 §2.2, 0.4초)
        public static float[] JamClear(int rate = DefaultRate)
        {
            var r = new Random(6002);
            int n = SampleCount(JamClearSec, rate);
            var s = new float[n];
            var bandLo = new OnePole(900, rate);
            var bandHi = new OnePole(4500, rate);
            double[] clickAt = { 0.04, 0.30 };
            double[] clickPartials = { 1900, 3300 };
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)rate;
                double x = Noise(r);
                double band = bandHi.Run(x);
                band -= bandLo.Run(band);
                // 0.06~0.28초 긁힘 (가운데가 가장 크다)
                double scrape = 0;
                if (t >= 0.06 && t <= 0.28)
                {
                    double u = (t - 0.06) / 0.22;
                    scrape = band * Math.Sin(Math.PI * u) * (0.7 + 0.3 * Math.Sin(TwoPi * 38 * t)) * 1.2;
                }
                double clicks = 0;
                for (int c = 0; c < clickAt.Length; c++)
                {
                    double tc = t - clickAt[c];
                    if (tc < 0)
                        continue;
                    clicks += (x * (tc < 0.002 ? 1.0 : 0.0)) * 1.2
                              + Math.Sin(TwoPi * clickPartials[c] * tc) * Env(tc, 0.0005, 0.015) * 0.8;
                }
                s[i] = (float)(scrape + clicks);
            }
            FadeOutTail(s, rate, 0.02);
            return Normalize(s);
        }

        // 저음 사인 화음 드론 + 느린 트레몰로, 30초 루프 (모든 주파수가 30초에 정수 주기)
        public static float[] HideoutDrone(int rate = DefaultRate)
        {
            int n = SampleCount(DroneSec, rate);
            var s = new float[n];
            double[] freqs = { 55.0, 82.5, 110.0, 110.2, 164.8 };
            double[] amps = { 0.5, 0.35, 0.3, 0.12, 0.15 };
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)rate;
                double v = 0;
                for (int k = 0; k < freqs.Length; k++)
                    v += amps[k] * Math.Sin(TwoPi * freqs[k] * t);
                double tremolo = 0.75 + 0.25 * Math.Sin(TwoPi * 0.2 * t);
                s[i] = (float)(v * tremolo);
            }
            return Normalize(s);
        }

        // ---------- 공용 ----------

        // 최대 절댓값을 peak로 맞춘다(제자리, 같은 배열 반환). 무음이면 그대로.
        public static float[] Normalize(float[] samples, float peak = TargetPeak)
        {
            double max = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                double a = Math.Abs(samples[i]);
                if (a > max)
                    max = a;
            }
            if (max <= 0)
                return samples;
            double k = peak / max;
            for (int i = 0; i < samples.Length; i++)
                samples[i] = (float)(samples[i] * k);
            return samples;
        }

        // 길이 L 원본 → 길이 L − fade 루프. 앞 fade 구간에 원본 끝 fade 구간을 교차 페이드해 넣는다
        // (마지막 샘플 다음 = 원본에서 바로 이어지는 샘플 → 이음매가 원본의 이웃 샘플 차와 같다).
        public static float[] CrossfadeLoop(float[] raw, int fadeSamples)
        {
            if (raw == null)
                throw new ArgumentNullException(nameof(raw));
            int fade = Math.Max(0, Math.Min(fadeSamples, raw.Length / 2));
            int n = raw.Length - fade;
            var result = new float[n];
            for (int i = 0; i < n; i++)
            {
                if (i < fade)
                {
                    double w = i / (double)fade;   // 0 → 1: 끝(tail) → 처음(head)
                    result[i] = (float)(raw[i] * w + raw[n + i] * (1.0 - w));
                }
                else
                {
                    result[i] = raw[i];
                }
            }
            return result;
        }

        // ---------- 발소리 ----------

        private static void FootstepDirt(float[] s, int variant, int rate, Random r)
        {
            var lp1 = new OnePole(500 + 120 * variant, rate);
            var lp2 = new OnePole(500 + 120 * variant, rate);
            double thumpFreq = 70 + 8 * variant;
            for (int i = 0; i < s.Length; i++)
            {
                double t = i / (double)rate;
                double grit = lp2.Run(lp1.Run(Noise(r) * Env(t, 0.003, 0.035 + 0.008 * variant))) * 3.0;
                double thump = Math.Sin(TwoPi * thumpFreq * t) * Math.Exp(-t / 0.03) * 0.6;
                s[i] = (float)(grit + thump);
            }
        }

        private static void FootstepMetal(float[] s, int variant, int rate, Random r)
        {
            var lp = new OnePole(2000, rate);
            double[] partials = { 420, 1130, 2370 };
            double[] amps = { 1.0, 0.6, 0.35 };
            double[] taus = { 0.12, 0.08, 0.05 };
            double detune = 1 + 0.07 * variant;
            for (int i = 0; i < s.Length; i++)
            {
                double t = i / (double)rate;
                double x = Noise(r) * Env(t, 0.001, 0.008);
                double tick = (x - lp.Run(x)) * 0.5;
                double ring = 0;
                for (int k = 0; k < partials.Length; k++)
                    ring += amps[k] * Math.Sin(TwoPi * partials[k] * detune * t) * Env(t, 0.001, taus[k]);
                s[i] = (float)(tick + ring * 0.6);
            }
        }

        private static void FootstepDebris(float[] s, int variant, int rate, Random r)
        {
            var lpThud = new OnePole(300, rate);
            for (int i = 0; i < s.Length; i++)
            {
                double t = i / (double)rate;
                s[i] = (float)(lpThud.Run(Noise(r)) * Env(t, 0.002, 0.03) * 1.5);
            }
            int grains = 4 + (variant % 3);
            for (int g = 0; g < grains; g++)
            {
                int start = (int)(r.NextDouble() * 0.15 * rate);
                double dur = 0.008 + r.NextDouble() * 0.012;
                double amp = 0.4 + r.NextDouble() * 0.6;
                var lp = new OnePole(2500 + 1500 * r.NextDouble(), rate);
                int len = (int)(dur * rate * 4);
                for (int i = 0; i < len && start + i < s.Length; i++)
                {
                    double t = i / (double)rate;
                    s[start + i] += (float)(lp.Run(Noise(r)) * amp * Env(t, 0.0005, dur));
                }
            }
        }

        private static void FootstepWater(float[] s, int variant, int rate, Random r)
        {
            var lpHi = new OnePole(1800, rate);
            var lpLo = new OnePole(350, rate);
            double phase = 0;
            double len = s.Length / (double)rate;
            for (int i = 0; i < s.Length; i++)
            {
                double t = i / (double)rate;
                double x = Noise(r);
                double hi = lpHi.Run(x);
                double band = hi - lpLo.Run(hi);
                double splash = band * Env(t, 0.01, 0.09) * 2.0;
                double freq = 900 - 600 * (t / len) + 60 * variant;
                phase += TwoPi * freq / rate;
                double sweep = Math.Sin(phase) * Env(t, 0.005, 0.06) * 0.4;
                double drip = t >= 0.12 ? Math.Sin(TwoPi * (1400 + 100 * variant) * (t - 0.12)) * Env(t - 0.12, 0.002, 0.02) * 0.2 : 0;
                s[i] = (float)(splash + sweep + drip);
            }
        }

        // ---------- 총성 ----------

        private struct GunParams
        {
            public double Pitch;
            public float LengthScale;
            // M7 7A-2c 음색 배율 (1 = M6 그대로 — 기존 분류 샘플은 비트 단위로 같다)
            public double Crack;        // 근거리 고역 크랙
            public double Body;         // 근·중거리 몸통·펀치 (산탄 = 넓은 폭발)
            public double Echo;         // 원거리 메아리 크기
            public double EchoSpread;   // 원거리 메아리 간격 (저격 = 긴 메아리)
        }

        // 분류별 음높이·길이 (권총 높고 짧게, 소총 낮고 길게). TODO(GDD-?) 자리표시
        // M7(설계 §2.2): 산탄 = 낮고 넓은 폭발(크랙 약, 몸통 강), 저격 = 날카로운 크랙 + 긴 메아리.
        // 새 분류(7B MachineGun 등)는 여기 case 하나 + M6AudioSetup.M7GunshotClasses에 한 항목이면 된다(없으면 기본 음색).
        private static GunParams ClassParams(WeaponClass cls)
        {
            switch (cls)
            {
                case WeaponClass.Pistol: return new GunParams { Pitch = 1.25, LengthScale = 0.85f, Crack = 1, Body = 1, Echo = 1, EchoSpread = 1 };
                case WeaponClass.Smg: return new GunParams { Pitch = 1.1, LengthScale = 0.92f, Crack = 1, Body = 1, Echo = 1, EchoSpread = 1 };
                case WeaponClass.Shotgun: return new GunParams { Pitch = 0.8, LengthScale = 1.1f, Crack = 0.6, Body = 1.6, Echo = 1, EchoSpread = 1 };
                case WeaponClass.Sniper: return new GunParams { Pitch = 0.85, LengthScale = 1.15f, Crack = 2.2, Body = 0.9, Echo = 1.3, EchoSpread = 1.25 };
                default: return new GunParams { Pitch = 0.9, LengthScale = 1f, Crack = 1, Body = 1, Echo = 1, EchoSpread = 1 };
            }
        }

        private static void GunshotNear(float[] s, GunParams p, int rate, Random r)
        {
            var crackLp = new OnePole(2500, rate);
            var bodyLp = new OnePole(2200 * p.Pitch, rate);
            var tailLp = new OnePole(700, rate);
            double phase = 0;
            double ls = p.LengthScale;
            for (int i = 0; i < s.Length; i++)
            {
                double t = i / (double)rate;
                double x = Noise(r);
                double crack = (x - crackLp.Run(x)) * Env(t, 0.0005, 0.006) * p.Crack;
                double body = bodyLp.Run(x) * Env(t, 0.001, 0.07 * ls) * 1.6 * p.Body;
                double freq = 60 * p.Pitch * (1 + 1.5 * Math.Exp(-t / 0.02));
                phase += TwoPi * freq / rate;
                double punch = Math.Sin(phase) * Env(t, 0.001, 0.08) * 0.9 * p.Body;
                double tail = tailLp.Run(x) * Env(t, 0.01, 0.18 * ls) * 0.8;
                s[i] = (float)(crack + body + punch + tail);
            }
        }

        private static void GunshotMid(float[] s, GunParams p, int rate, Random r)
        {
            var lp1 = new OnePole(900 * p.Pitch, rate);
            var lp2 = new OnePole(900 * p.Pitch, rate);
            var tailLp = new OnePole(400, rate);
            double phase = 0;
            double ls = p.LengthScale;
            for (int i = 0; i < s.Length; i++)
            {
                double t = i / (double)rate;
                double x = Noise(r);
                double body = lp2.Run(lp1.Run(x)) * Env(t, 0.004, 0.12 * ls) * 3.0 * p.Body;
                double freq = 50 * p.Pitch * (1 + Math.Exp(-t / 0.03));
                phase += TwoPi * freq / rate;
                double punch = Math.Sin(phase) * Env(t, 0.003, 0.15) * 0.8 * p.Body;
                double tail = tailLp.Run(x) * Env(t, 0.02, 0.3 * ls) * 1.2;
                s[i] = (float)(body + punch + tail);
            }
        }

        private static void GunshotFar(float[] s, GunParams p, int rate, Random r)
        {
            var lp1 = new OnePole(300 * p.Pitch, rate);
            var lp2 = new OnePole(300 * p.Pitch, rate);
            var dry = new double[s.Length];
            double ls = p.LengthScale;
            for (int i = 0; i < s.Length; i++)
            {
                double t = i / (double)rate;
                double thump = lp2.Run(lp1.Run(Noise(r))) * Env(t, 0.01, 0.2 * ls) * 6.0;
                double low = Math.Sin(TwoPi * 40 * p.Pitch * t) * Env(t, 0.01, 0.25) * 0.5;
                dry[i] = thump + low;
            }
            // 지연 반복 메아리 꼬리 (점점 작고 둔하게)
            double[] delays = { 0.35, 0.8, 1.3 };
            double[] gains = { 0.5, 0.3, 0.18 };
            var echoLp = new OnePole(220 * p.Pitch, rate);
            for (int i = 0; i < s.Length; i++)
            {
                double echo = 0;
                for (int k = 0; k < delays.Length; k++)
                {
                    int d = (int)(delays[k] * ls * p.EchoSpread * rate);
                    if (i - d >= 0)
                        echo += dry[i - d] * gains[k] * p.Echo;
                }
                s[i] = (float)(dry[i] + echoLp.Run(echo) * 1.5);
            }
        }

        // ---------- 도우미 ----------

        private static double Noise(Random r) => r.NextDouble() * 2.0 - 1.0;

        // 선형 어택 후 지수 감쇠
        private static double Env(double t, double attack, double tau)
        {
            if (t < 0)
                return 0;
            if (attack > 0 && t < attack)
                return t / attack;
            return Math.Exp(-(t - attack) / Math.Max(1e-6, tau));
        }

        // 끝 seconds 동안 선형으로 0까지 (원샷 끝 클릭 방지)
        private static void FadeOutTail(float[] s, int rate, double seconds)
        {
            int n = Math.Min(s.Length, Math.Max(1, (int)(seconds * rate)));
            for (int i = 0; i < n; i++)
            {
                int idx = s.Length - 1 - i;
                s[idx] = (float)(s[idx] * (i / (double)n));
            }
        }

        // 1극 저역 통과
        private sealed class OnePole
        {
            private readonly double a;
            private double y;

            public OnePole(double cutoffHz, int rate)
            {
                a = 1.0 - Math.Exp(-TwoPi * cutoffHz / rate);
            }

            public double Run(double x)
            {
                y += a * (x - y);
                return y;
            }
        }
    }
}
