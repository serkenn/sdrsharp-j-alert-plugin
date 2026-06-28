// Arbitrary-ratio polyphase resampler. Used to re-pin the input IQ stream to the
// J-ALERT working rate (4 sps × 256 ksym/s = 1.024 MHz).
// Ported from src/dsp/polyphase_resampler.{h,cpp}.

using System;
using System.Collections.Generic;

namespace SDRSharp.JAlert.Dsp
{
    internal sealed class PolyphaseResampler
    {
        private readonly int _nphases;
        private readonly int _tapsPerArm;
        private readonly int _halfTaps;
        private readonly float[][] _arms;
        private readonly double _step;
        private readonly float[] _bufRe;
        private readonly float[] _bufIm;
        private readonly int _bufLen;
        private int _bufStart;
        private int _bufCount;
        private long _inIndex;
        private double _nextOut;

        public PolyphaseResampler(double inputRateHz, double outputRateHz, double cutoffHz, int nphases = 32)
        {
            if (inputRateHz <= 0.0) throw new ArgumentException("inputRateHz must be positive");
            if (outputRateHz <= 0.0) throw new ArgumentException("outputRateHz must be positive");
            _nphases = nphases;

            // Cut-off must stay below 0.45×min(Nyquist) on both sides to avoid
            // aliasing / first-image leakage.
            double fc = Math.Min(cutoffHz, 0.45 * Math.Min(inputRateHz, outputRateHz));

            // Prototype span ~1.5 ms of input — gentle skirt — but capped so the
            // per-output convolution stays cheap at the MHz-class rates this plugin
            // runs.
            _tapsPerArm = Math.Max(8, (int)Math.Round(1.5e-3 * inputRateHz));
            _tapsPerArm = Math.Min(_tapsPerArm, 32);
            _halfTaps = _tapsPerArm / 2;

            int protoLen = _nphases * _tapsPerArm;
            int oddLen = (protoLen & 1) == 0 ? protoLen + 1 : protoLen;
            float[] protoOdd = FilterDesign.LowpassTaps(oddLen, (float)fc, (float)(nphases * inputRateHz));
            float[] proto = new float[protoLen];
            Array.Copy(protoOdd, proto, protoLen);
            for (int i = 0; i < protoLen; ++i) proto[i] *= _nphases;

            _arms = new float[_nphases][];
            for (int a = 0; a < _nphases; ++a)
            {
                float[] arm = new float[_tapsPerArm];
                for (int j = 0; j < _tapsPerArm; ++j) arm[j] = proto[a + j * _nphases];
                Array.Reverse(arm);
                _arms[a] = arm;
            }

            _step = inputRateHz / outputRateHz;
            _bufLen = _tapsPerArm + 16;
            _bufRe = new float[_bufLen];
            _bufIm = new float[_bufLen];
            Reset();
        }

        public void Reset()
        {
            Array.Clear(_bufRe, 0, _bufLen);
            Array.Clear(_bufIm, 0, _bufLen);
            _bufStart = 0;
            _bufCount = 0;
            _inIndex = 0;
            _nextOut = _halfTaps;
        }

        // Push one input sample; append zero or more output samples to output.
        public void Step(Complex32 z, List<Complex32> output)
        {
            // Push into the ring, dropping the oldest if it is full.
            if (_bufCount == _bufLen)
            {
                _bufStart = (_bufStart + 1 == _bufLen) ? 0 : _bufStart + 1;
                --_bufCount;
            }
            int w = _bufStart + _bufCount;
            if (w >= _bufLen) w -= _bufLen;
            _bufRe[w] = z.Re;
            _bufIm[w] = z.Im;
            ++_bufCount;
            ++_inIndex;

            long oldest = _inIndex - _bufCount;
            long newest = _inIndex - 1;

            while (true)
            {
                long ip = (long)Math.Floor(_nextOut);
                double frac = _nextOut - ip;
                int armIdx = (int)Math.Round(frac * _nphases);
                if (armIdx >= _nphases) { armIdx = 0; ++ip; }

                long lo = ip - _halfTaps;
                long hi = lo + _tapsPerArm - 1;
                if (hi > newest) break;
                if (lo < oldest)
                {
                    // Window underflowed the buffer — only possible if the ring is
                    // too small for the ratio. Skip rather than read stale data.
                    _nextOut += _step;
                    continue;
                }

                float[] arm = _arms[armIdx];
                int bi = (int)(_bufStart + (lo - oldest));
                if (bi >= _bufLen) bi -= _bufLen;
                float yr = 0.0f, yi = 0.0f;
                for (int j = 0; j < _tapsPerArm; ++j)
                {
                    float t = arm[j];
                    yr += _bufRe[bi] * t;
                    yi += _bufIm[bi] * t;
                    if (++bi == _bufLen) bi = 0;
                }
                output.Add(new Complex32(yr, yi));
                _nextOut += _step;
            }
        }
    }
}
