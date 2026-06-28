// Polyphase clock sync. Master RRC at nfilt × sps virtual rate, with a
// derivative arm set driving a Mueller-Müller-style timing-error detector. This
// block is also the chain's matched filter. The TED is modulation-agnostic and
// works for the J-ALERT BPSK. Ported from src/dsp/pfb_clock_sync.{h,cpp}.

using System;

namespace SDRSharp.JAlert.Dsp
{
    internal sealed class PfbClockSync
    {
        private sealed class LinkedListBuf
        {
            private Complex32[] _data;
            private int _start;
            private int _count;

            public LinkedListBuf(int initialCapacity)
            {
                _data = new Complex32[Math.Max(initialCapacity, 16)];
            }

            public int Count => _count;

            public Complex32 this[int idx]
            {
                get
                {
                    int n = _data.Length;
                    return _data[(_start + idx) % n];
                }
            }

            public void PushBack(Complex32 z)
            {
                int n = _data.Length;
                if (_count == n) Grow();
                int slot = (_start + _count) % _data.Length;
                _data[slot] = z;
                ++_count;
            }

            public void PopFront()
            {
                if (_count == 0) return;
                _start = (_start + 1) % _data.Length;
                --_count;
            }

            private void Grow()
            {
                int oldN = _data.Length;
                Complex32[] bigger = new Complex32[oldN * 2];
                for (int i = 0; i < _count; ++i) bigger[i] = _data[(_start + i) % oldN];
                _data = bigger;
                _start = 0;
            }
        }

        private readonly int _nfilt;
        private readonly int _sps;
        private readonly int _tapsPerArm;
        private readonly float[][] _arms;
        private readonly float[][] _diffArms;

        private readonly float _alpha;
        private readonly float _beta;
        private readonly float _maxDev;

        private float _rateF;
        private float _rateI;
        private float _k;
        private float _error;

        private readonly float _initialRateF;
        private readonly float _initialRateI;

        private LinkedListBuf _buf;
        private int _readPos;
        private readonly int _prefixKeep;
        private bool _warmed;

        public float Error => _error;
        public float RateF => _rateF;

        public PfbClockSync(int nfilt, float spsActual, float loopBw, float maxDev, int rrcSymbols, float rolloff)
        {
            _nfilt = nfilt;
            _sps = (int)Math.Max(Math.Floor(spsActual), 1.0f);
            _buf = new LinkedListBuf(16);

            const int kPfbMasterSymbols = 8;
            int totalTaps = nfilt * Math.Max(rrcSymbols, kPfbMasterSymbols * _sps);
            float[] master = FilterDesign.RrcTaps(nfilt, nfilt * _sps, 1.0f, rolloff, totalTaps);
            _arms = FilterDesign.PolyphaseDecompose(master, nfilt);
            float[] dtaps = FilterDesign.CreateDiffTaps(master, nfilt);
            _diffArms = FilterDesign.PolyphaseDecompose(dtaps, nfilt);
            _tapsPerArm = _arms[0].Length;

            // Critically damped: damping = 2 · nfilt; α/β derived from loop_bw.
            float damping = 2.0f * nfilt;
            float denom = 1.0f + 2.0f * damping * loopBw + loopBw * loopBw;
            _alpha = 4.0f * damping * loopBw / denom;
            _beta = 4.0f * loopBw * loopBw / denom;
            _maxDev = maxDev;

            float fracSps = spsActual - _sps;
            float total = fracSps * nfilt;
            _rateI = (float)Math.Floor(total);
            _rateF = (total - _rateI) / (_sps + 1);
            _initialRateI = _rateI;
            _initialRateF = _rateF;

            _k = nfilt / 2.0f;
            _prefixKeep = _sps + 2;

            _buf = new LinkedListBuf(_tapsPerArm + 4 * _sps);
        }

        // Push z; returns true and writes output when the loop emits a symbol.
        public bool Step(Complex32 z, out Complex32 output)
        {
            _buf.PushBack(z);
            return TryEmit(out output);
        }

        // Snap the PI-loop state back to the ctor defaults; leaves the sample ring.
        public void ResetLoop()
        {
            _k = _nfilt / 2.0f;
            _rateF = _initialRateF;
            _rateI = _initialRateI;
            _error = 0.0f;
        }

        private bool TryEmit(out Complex32 output)
        {
            output = default;
            int m = _tapsPerArm;
            int prefix = _prefixKeep;

            if (!_warmed)
            {
                int needed = prefix + m + _sps;
                if (_buf.Count < needed) return false;
                _readPos = prefix;
                _warmed = true;
            }

            float kSave = _k;
            int count = 0;
            int filtnum = (int)Math.Floor(_k);
            while (filtnum >= _nfilt) { _k -= _nfilt; filtnum -= _nfilt; ++count; }
            while (filtnum < 0) { _k += _nfilt; filtnum += _nfilt; --count; }

            int start = _readPos + count;
            int end = start + m;
            if (start < 0 || end > _buf.Count)
            {
                _k = kSave;
                return false;
            }

            float[] arm = _arms[filtnum];
            float[] darm = _diffArms[filtnum];
            float outRe = 0.0f, outIm = 0.0f;
            float diffRe = 0.0f, diffIm = 0.0f;
            for (int j = 0; j < m; ++j)
            {
                Complex32 s = _buf[start + j];
                outRe += s.Re * arm[j];
                outIm += s.Im * arm[j];
                diffRe += s.Re * darm[j];
                diffIm += s.Im * darm[j];
            }

            _k += _rateI + _rateF;

            float err = (outRe * diffRe + outIm * diffIm) * 0.5f;
            _error = err;

            for (int i = 0; i < _sps; ++i)
            {
                _rateF += _beta * err;
                _k += _rateF + _alpha * err;
            }
            if (_rateF > _maxDev) _rateF = _maxDev;
            else if (_rateF < -_maxDev) _rateF = -_maxDev;

            int advance = count + _sps;
            if (advance >= 0)
            {
                _readPos += advance;
            }
            else
            {
                int back = -advance;
                _readPos = _readPos > back ? _readPos - back : 0;
            }

            while (_readPos > prefix * 2)
            {
                _buf.PopFront();
                --_readPos;
            }

            output = new Complex32(outRe, outIm);
            return true;
        }
    }
}
