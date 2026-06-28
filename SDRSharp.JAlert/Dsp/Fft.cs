// Minimal in-place iterative radix-2 FFT (power-of-two length). Used only by the
// coarse carrier estimator, so it favours simplicity over peak throughput.
// Ported from src/dsp/fft.h.

using System;
using System.Numerics;

namespace SDRSharp.JAlert.Dsp
{
    internal static class Fft
    {
        public static void Radix2(Complex[] a)
        {
            int n = a.Length;
            if (n < 2) return;

            // Bit-reversal permutation.
            for (int i = 1, j = 0; i < n; ++i)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j)
                {
                    Complex t = a[i];
                    a[i] = a[j];
                    a[j] = t;
                }
            }

            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = -2.0 * Math.PI / len;
                Complex wlen = new Complex(Math.Cos(ang), Math.Sin(ang));
                for (int i = 0; i < n; i += len)
                {
                    Complex w = Complex.One;
                    for (int k = 0; k < len / 2; ++k)
                    {
                        Complex u = a[i + k];
                        Complex v = a[i + k + len / 2] * w;
                        a[i + k] = u + v;
                        a[i + k + len / 2] = u - v;
                        w *= wlen;
                    }
                }
            }
        }
    }
}
