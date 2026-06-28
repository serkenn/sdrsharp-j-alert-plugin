// DSP filter design helpers. The J-ALERT chain uses RrcTaps() for the β=0.35
// matched filter and LowpassTaps() inside the polyphase resampler.
// Ported from src/dsp/filter_design.{h,cpp}.

using System;

namespace SDRSharp.JAlert.Dsp
{
    internal static class FilterDesign
    {
        // Hamming-windowed sinc low-pass FIR, normalised to unity DC gain.
        // ntaps must be odd.
        public static float[] LowpassTaps(int ntaps, float cutoffHz, float fsHz)
        {
            if ((ntaps & 1) == 0)
                throw new ArgumentException("ntaps must be odd for a symmetric LPF");

            float m = ntaps - 1;
            float fc = cutoffHz / fsHz;
            float twoPiFc = 2.0f * (float)Math.PI * fc;
            float[] taps = new float[ntaps];
            float sum = 0.0f;
            for (int n = 0; n < ntaps; ++n)
            {
                float k = n - m / 2.0f;
                float sinc;
                if (Math.Abs(k) < 1e-9f)
                    sinc = twoPiFc;
                else
                    sinc = (float)Math.Sin(twoPiFc * k) / k;
                float w = 0.54f - 0.46f * (float)Math.Cos(2.0f * (float)Math.PI * n / m);
                float v = sinc * w;
                taps[n] = v;
                sum += v;
            }
            for (int i = 0; i < ntaps; ++i) taps[i] /= sum;
            return taps;
        }

        // Root-raised-cosine impulse response, DC-normalised so sum(taps) == gain.
        // If ntaps is even it is increased by 1 (the implementation needs an odd length).
        public static float[] RrcTaps(float gain, float fs, float symRate, float alpha, int ntaps)
        {
            if ((ntaps & 1) == 0) ntaps += 1;
            float spb = fs / symRate;
            float mid = (ntaps - 1) / 2.0f;
            float[] taps = new float[ntaps];
            float scale = 0.0f;
            float fourAlpha = 4.0f * alpha;
            float pi = (float)Math.PI;

            for (int k = 0; k < ntaps; ++k)
            {
                float n = k - mid;
                float t = n / spb;
                float v;
                if (Math.Abs(n) < 1e-9f)
                {
                    v = 1.0f - alpha + 4.0f * alpha / pi;
                }
                else if (Math.Abs(Math.Abs(fourAlpha * t) - 1.0f) < 1e-7f)
                {
                    // l'Hôpital singularity at t = ±1/(4α).
                    float s = (float)Math.Sin(pi / (4.0f * alpha));
                    float c = (float)Math.Cos(pi / (4.0f * alpha));
                    v = (alpha / (float)Math.Sqrt(2.0f))
                        * ((1.0f + 2.0f / pi) * s
                         + (1.0f - 2.0f / pi) * c);
                }
                else
                {
                    float pt = pi * t;
                    float num = (float)Math.Sin(pt * (1.0f - alpha))
                              + fourAlpha * t * (float)Math.Cos(pt * (1.0f + alpha));
                    float den = pt * (1.0f - (fourAlpha * t) * (fourAlpha * t));
                    v = num / den;
                }
                taps[k] = v;
                scale += v;
            }

            float inv = gain / scale;
            for (int i = 0; i < ntaps; ++i) taps[i] *= inv;
            return taps;
        }

        // Polyphase-decompose a master filter into nfilt arms, stored in reverse
        // order so the convolution downstream walks the buffer forward.
        public static float[][] PolyphaseDecompose(float[] taps, int nfilt)
        {
            int tapsPerArm = (taps.Length + nfilt - 1) / nfilt;
            int total = nfilt * tapsPerArm;
            float[] padded = new float[total];
            Array.Copy(taps, padded, taps.Length);

            float[][] arms = new float[nfilt][];
            for (int i = 0; i < nfilt; ++i)
            {
                float[] arm = new float[tapsPerArm];
                for (int j = 0; j < tapsPerArm; ++j) arm[j] = padded[i + j * nfilt];
                Array.Reverse(arm);
                arms[i] = arm;
            }
            return arms;
        }

        // Centred [-1, 0, +1] difference of the master taps, scaled so the sum of
        // magnitudes equals nfilt. Used by PFB-style clock sync to derive its
        // timing error from the master RRC.
        public static float[] CreateDiffTaps(float[] taps, int nfilt)
        {
            int n = taps.Length;
            float[] dt = new float[n];
            float pwr = 0.0f;
            if (n >= 3)
            {
                for (int i = 0; i < n - 2; ++i)
                {
                    float v = -taps[i] + taps[i + 2];
                    dt[i + 1] = v;
                    pwr += Math.Abs(v);
                }
            }
            if (pwr > 0.0f)
            {
                float scale = nfilt / pwr;
                for (int i = 0; i < n; ++i) dt[i] *= scale;
            }
            return dt;
        }
    }
}
