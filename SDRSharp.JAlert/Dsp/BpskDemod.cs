// J-ALERT BPSK front end: complex IQ → soft symbols (one float per symbol).
// Ported from src/dsp/bpsk_demod.{h,cpp}.
//
// Chain:
//   coarse squaring carrier removal (input rate, handles a large offset)  →
//   polyphase resample to 1.024 MHz (4 sps × 256 ksym/s)  →  AGC  →
//   PfbClockSync (RRC matched filter + Müller-Müller timing)  →
//   decision-directed Costas loop  →  soft = Re{y}.
//
// BPSK squared is a pure tone at 2·fc (d²=1), so a block FFT peak of the squared
// signal gives the carrier offset directly. The 180° phase ambiguity it leaves
// is absorbed downstream by the differential decode.

using System;
using System.Collections.Generic;
using System.Numerics;

namespace SDRSharp.JAlert.Dsp
{
    public sealed class BpskDemod
    {
        public const double WorkRate = 1_024_000.0;   // 4 sps × 256 ksym/s
        public const float SymRate = 256_000.0f;
        public const int Sps = 4;

        private const double TwoPi = 6.283185307179586;

        // Costas loop (decision-directed BPSK), per symbol.
        private const double CostasFreqGain = 2.5e-5;
        private const double CostasPhaseGain = 1.0e-2;

        // Adaptive tracking: once locked, smooth the block coarse-carrier
        // estimate (instead of hard-replacing it every block) so per-block FFT
        // jitter doesn't step the carrier faster than the Costas can absorb.
        private const double CoarseTrackGain = 0.25;   // locked-state coarse EMA gain

        // Resampler anti-alias / channel cutoff (one-sided). Passes the (1+β)·Rs/2
        // ≈ 173 kHz occupied band with margin, blocks the resampler images.
        private const double ResampCutoffHz = 250_000.0;
        private const int ResampPhases = 32;

        private const int AgcWindow = 64;
        private const int PfbNFilt = 32;
        private const float PfbLoopBw = 0.01f;
        private const float PfbMaxDev = 1.5f;
        private const int PfbRrcSym = 12;
        private const float Rolloff = 0.35f;

        private const int CoarseBlock = 8192;
        private const int ConstCap = 2048;

        private readonly double _sampleRate;

        // Coarse carrier (input rate): block FFT-argmax on the (DC-removed)
        // squared signal — a tone at 2·fc — estimates the offset for the next
        // block.
        private readonly Complex[] _cBlk = new Complex[CoarseBlock];
        private int _cBlkLen;
        private double _cPhase;
        private double _cOmega;
        private bool _cManual;

        private readonly PolyphaseResampler _resamp;
        private readonly List<Complex32> _rbuf = new List<Complex32>(8);

        private readonly Agc _agc;
        private readonly PfbClockSync _sync;

        // Costas (per symbol).
        private double _costasPhase;
        private double _costasFreq;

        private Complex32 _lastSym;
        private float _symMagEma;
        private float _lockRe;
        private float _lockIm;
        private bool _locked;
        private long _symbols;
        private bool _adaptive = true;
        private double _carrierLoopScale = 1.0;   // Costas loop-bandwidth multiplier

        // Constellation ring (DSP thread writes, UI thread pulls).
        private readonly object _constMtx = new object();
        private readonly Complex32[] _constRing = new Complex32[ConstCap];
        private int _constIdx;
        private long _constCount;

        public BpskDemod(double sampleRateHz)
        {
            _sampleRate = sampleRateHz;
            _resamp = new PolyphaseResampler(sampleRateHz, WorkRate, ResampCutoffHz, ResampPhases);
            _agc = new Agc(AgcWindow, 1.0f);
            _sync = new PfbClockSync(PfbNFilt, Sps, PfbLoopBw, PfbMaxDev, PfbRrcSym, Rolloff);
        }

        private static double Wrap(double p)
        {
            p %= TwoPi;   // C# double % is fmod (truncated, sign of dividend)
            if (p > Math.PI) p -= TwoPi;
            else if (p < -Math.PI) p += TwoPi;
            return p;
        }

        private static Complex32 Rotate(Complex32 z, double phase)
        {
            float c = (float)Math.Cos(phase);
            float s = (float)Math.Sin(phase);
            // z · e^{-jφ}
            return new Complex32(z.Re * c + z.Im * s, z.Im * c - z.Re * s);
        }

        // Coarse carrier offset (per-sample removal phase increment) from a block
        // of input samples: FFT the DC-removed squared signal, take the peak bin
        // (a tone at 2·fc), halve. Returns omega = 2π·fc/Fs.
        private static double EstimateCoarseOmega(Complex[] blk, int n)
        {
            Complex mean = Complex.Zero;
            for (int i = 0; i < n; ++i)
            {
                Complex z = blk[i];
                blk[i] = z * z;                 // square in place
                mean += blk[i];
            }
            mean /= n;
            for (int i = 0; i < n; ++i) blk[i] -= mean;   // remove DC of squared

            Fft.Radix2(blk);

            int kmax = 0;
            double best = -1.0;
            for (int k = 0; k < n; ++k)
            {
                double p = blk[k].Real * blk[k].Real + blk[k].Imaginary * blk[k].Imaginary;
                if (p > best) { best = p; kmax = k; }
            }
            long signedBin = (kmax < n / 2) ? kmax : (long)kmax - n;
            double nu = (double)signedBin / n;   // 2fc/Fs
            return Math.PI * nu;                  // 2π·fc/Fs
        }

        public void Reset()
        {
            _cBlkLen = 0;
            _cPhase = _cOmega = 0.0;
            _resamp.Reset();
            _sync.ResetLoop();
            _costasPhase = _costasFreq = 0.0;
            _symMagEma = _lockRe = _lockIm = 0.0f;
            _locked = false;
            _symbols = 0;
        }

        // Debug / manual tuning: pin the coarse carrier offset (Hz) and disable
        // the squaring estimator.
        public void SetManualCoarse(double hz)
        {
            _cManual = true;
            _cOmega = TwoPi * hz / _sampleRate;
        }

        // Feed one input sample; append zero or more soft symbols to softOut.
        public void Process(Complex32 z, List<float> softOut)
        {
            // ── Coarse carrier (block FFT-argmax on the squared signal) ──
            if (!_cManual)
            {
                _cBlk[_cBlkLen++] = new Complex(z.Re, z.Im);
                if (_cBlkLen >= CoarseBlock)
                {
                    double est = EstimateCoarseOmega(_cBlk, _cBlkLen);
                    // Gear-shift: hard-acquire while searching, smooth once
                    // locked so per-block FFT jitter doesn't step the carrier
                    // faster than the (slow) Costas loop can absorb it.
                    _cOmega = (_adaptive && _locked)
                        ? _cOmega + CoarseTrackGain * (est - _cOmega)
                        : est;
                    _cBlkLen = 0;
                }
            }
            _cPhase = Wrap(_cPhase + _cOmega);
            Complex32 zc = Rotate(z, _cPhase);

            // ── Resample to the 1.024 MHz work rate ──
            _rbuf.Clear();
            _resamp.Step(zc, _rbuf);
            for (int ri = 0; ri < _rbuf.Count; ++ri)
            {
                Complex32 zr = _rbuf[ri];

                // ── AGC ──
                if (!_agc.Step(zr, out Complex32 za)) continue;

                // ── Matched filter + symbol timing ──
                if (!_sync.Step(za, out Complex32 sym)) continue;

                // ── Costas (decision-directed BPSK) ──
                Complex32 y = Rotate(sym, _costasPhase);
                double dec = (y.Re >= 0.0f) ? 1.0 : -1.0;
                double e = (double)y.Im * dec;
                // Carrier loop bandwidth is user-selectable: a wider loop tracks
                // LNB phase noise / fast frequency wander (shrinks the quadrature
                // spread), a narrower one rejects more decision noise. The 180°
                // slips a wide loop may take are absorbed by the differential
                // decode, so widening is safe for this DBPSK link.
                double g = _carrierLoopScale;
                _costasFreq += CostasFreqGain * g * e;
                _costasPhase = Wrap(_costasPhase + _costasFreq + CostasPhaseGain * g * e);

                _lastSym = y;
                ++_symbols;
                lock (_constMtx)
                {
                    _constRing[_constIdx] = y;
                    _constIdx = (_constIdx + 1) % ConstCap;
                    ++_constCount;
                }

                float mag = (float)Math.Sqrt(y.Re * y.Re + y.Im * y.Im);
                _symMagEma += 0.01f * (mag - _symMagEma);
                _lockRe += 0.01f * (Math.Abs(y.Re) - _lockRe);
                _lockIm += 0.01f * (Math.Abs(y.Im) - _lockIm);
                _locked = (_lockRe > 0.3f) && (_lockIm < 0.5f * _lockRe);

                softOut.Add(y.Re);
            }
        }

        // Smooth the coarse carrier once locked (acquire/track gear-shift).
        public bool AdaptiveTracking
        {
            get => _adaptive;
            set => _adaptive = value;
        }

        // Costas loop-bandwidth multiplier (>1 tracks more phase noise).
        public double CarrierLoopScale
        {
            get => _carrierLoopScale;
            set => _carrierLoopScale = value > 0.0 ? value : 1.0;
        }

        // ── Diagnostics (UI) ──
        public double CoarseOffsetHz => _cOmega / TwoPi * _sampleRate;
        public double CostasOffsetHz => _costasFreq / TwoPi * SymRate;
        public bool Locked => _locked;
        public float SymMag => _symMagEma;
        public Complex32 LastSymbol => _lastSym;
        public long Symbols => _symbols;

        // Pull up to capacity of the most-recent Costas-derotated symbols (newest
        // first) for the constellation view. Thread-safe.
        public int PullConstellation(Complex32[] dst, int capacity)
        {
            if (dst == null || capacity <= 0) return 0;
            lock (_constMtx)
            {
                int n = (int)Math.Min(_constCount, capacity);
                int ringN = Math.Min(n, ConstCap);
                int idx = _constIdx - 1;
                if (idx < 0) idx += ConstCap;
                for (int i = 0; i < ringN; ++i)
                {
                    dst[i] = _constRing[idx];
                    idx = idx == 0 ? ConstCap - 1 : idx - 1;
                }
                return ringN;
            }
        }
    }
}
