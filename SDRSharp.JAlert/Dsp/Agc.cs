// Feed-forward AGC. Buffers nsamples of look-ahead and emits the oldest sample
// scaled by reference / peak-envelope-of-the-window. Ported from src/dsp/agc.h.

using System;

namespace SDRSharp.JAlert.Dsp
{
    internal sealed class Agc
    {
        private readonly Complex32[] _window;
        private readonly int _nsamples;
        private readonly float _reference;
        private int _head;
        private int _fill;

        public Agc(int nsamples, float reference)
        {
            _window = new Complex32[nsamples];
            _nsamples = nsamples;
            _reference = reference;
        }

        // Pushes z; returns true and writes output when the look-ahead window is
        // full; returns false during the initial fill.
        public bool Step(Complex32 z, out Complex32 output)
        {
            _window[_head] = z;
            _head = (_head + 1) % _nsamples;
            if (_fill < _nsamples) ++_fill;
            if (_fill < _nsamples)
            {
                output = default;
                return false;
            }

            Complex32 oldest = _window[_head];
            float maxEnv = 1e-4f;
            for (int i = 0; i < _nsamples; ++i)
            {
                float e = Envelope(_window[i]);
                if (e > maxEnv) maxEnv = e;
            }
            float gain = _reference / maxEnv;
            output = new Complex32(oldest.Re * gain, oldest.Im * gain);
            return true;
        }

        // max(|I|,|Q|) + 0.4·min(|I|,|Q|) — ~0.2 dB approximation of √(I²+Q²).
        private static float Envelope(Complex32 z)
        {
            float r = Math.Abs(z.Re);
            float i = Math.Abs(z.Im);
            return r > i ? r + 0.4f * i : i + 0.4f * r;
        }
    }
}
