// Streaming soft-decision Viterbi decoder for the K=7 (171,133) rate-1/2 code.
// Ported from src/fec/viterbi.{h,cpp}.
//
// Soft symbols arrive one at a time and are consumed in pairs (the two coded
// bits per info bit). Each completed pair advances the trellis one step; once the
// traceback window is primed, every step emits exactly one decoded info bit at a
// fixed latency (kTracebackDepth pairs). This continuous, self-synchronising
// operation matches a live receiver — no block boundaries, no known terminating
// state.
//
// Soft convention: coded bit 0 -> expected symbol +1, bit 1 -> -1, so a more
// positive soft value favours coded bit 0.

using System.Collections.Generic;

namespace SDRSharp.JAlert.Fec
{
    public sealed class ViterbiDecoder
    {
        // Traceback depth in trellis steps. ~5×K is the textbook minimum for this
        // code to reach the error floor; 96 gives generous margin at negligible
        // cost.
        public const int TracebackDepth = 96;
        private const int RingLen = TracebackDepth + 1;

        // EMA smoothing for the re-encode BER estimate. At 256 ksym/s (128 k
        // pairs/s) this averages over roughly a tenth of a second.
        private const float BerAlpha = 1.0e-4f;

        // Trellis tables (built once).
        private readonly byte[,] _pred = new byte[ConvCode.NumStates, 2];  // predecessor states
        private readonly float[,] _sym0 = new float[ConvCode.NumStates, 2]; // expected symbol, coded bit 0
        private readonly float[,] _sym1 = new float[ConvCode.NumStates, 2]; // expected symbol, coded bit 1

        private readonly float[] _pm = new float[ConvCode.NumStates];   // current path metrics
        private readonly float[] _npm = new float[ConvCode.NumStates];  // scratch next metrics

        // Traceback ring: _dec[t % L][ns] = surviving predecessor index (0/1).
        private readonly byte[][] _dec = new byte[RingLen][];

        // Re-encode BER estimate.
        private readonly byte[] _hardRing = new byte[RingLen]; // received hard pair per step (h0<<1|h1)
        private uint _reencState;                              // re-encoder state, driven by emitted bits
        private float _berEma = 0.5f;

        private long _step;          // total completed trellis steps
        private float _pending;      // first soft of an incomplete pair
        private bool _havePending;   // a soft is buffered awaiting its partner

        public ViterbiDecoder()
        {
            for (int i = 0; i < RingLen; ++i) _dec[i] = new byte[ConvCode.NumStates];
            Reset();
        }

        public float Ber => _berEma;

        // Decoder latency, in info bits, from a coded-symbol pair entering to its
        // info bit being emitted.
        public static int LatencyBits => TracebackDepth;

        private void BuildTables()
        {
            for (int ns = 0; ns < ConvCode.NumStates; ++ns)
            {
                // Input bit consumed entering this state is the high (memory) bit
                // of ns (encoder shifts the input in at the MSB).
                uint b = (uint)(ns >> (ConvCode.Memory - 1)) & 1u;
                // The two predecessors share s>>1 == ns & (2^(K-2)-1).
                int lo = ns & ((1 << (ConvCode.Memory - 1)) - 1);
                int p0 = lo << 1;
                int p1 = p0 | 1;
                _pred[ns, 0] = (byte)p0;
                _pred[ns, 1] = (byte)p1;
                for (int k = 0; k < 2; ++k)
                {
                    uint p = _pred[ns, k];
                    uint reg = (b << ConvCode.Memory) | p;
                    int o0 = ConvCode.Parity(reg & ConvCode.G1);
                    int o1 = ConvCode.Parity(reg & ConvCode.G2);
                    _sym0[ns, k] = o0 != 0 ? -1.0f : 1.0f;
                    _sym1[ns, k] = o1 != 0 ? -1.0f : 1.0f;
                }
            }
        }

        public void Reset()
        {
            BuildTables();
            for (int i = 0; i < ConvCode.NumStates; ++i) _pm[i] = 0.0f;
            for (int i = 0; i < RingLen; ++i)
            {
                System.Array.Clear(_dec[i], 0, ConvCode.NumStates);
                _hardRing[i] = 0;
            }
            _reencState = 0;
            _berEma = 0.5f;
            _step = 0;
            _pending = 0.0f;
            _havePending = false;
        }

        private void AcsStep(float r0, float r1, List<byte> outBits)
        {
            byte[] dec = _dec[(int)(_step % RingLen)];
            // Record the received hard decisions for this pair (coded bit = soft < 0).
            _hardRing[(int)(_step % RingLen)] =
                (byte)(((r0 < 0.0f) ? 2 : 0) | ((r1 < 0.0f) ? 1 : 0));

            float bestPm = float.NegativeInfinity;
            for (int ns = 0; ns < ConvCode.NumStates; ++ns)
            {
                int p0 = _pred[ns, 0];
                int p1 = _pred[ns, 1];
                float m0 = _pm[p0] + r0 * _sym0[ns, 0] + r1 * _sym1[ns, 0];
                float m1 = _pm[p1] + r0 * _sym0[ns, 1] + r1 * _sym1[ns, 1];
                if (m1 > m0)
                {
                    _npm[ns] = m1;
                    dec[ns] = 1;
                }
                else
                {
                    _npm[ns] = m0;
                    dec[ns] = 0;
                }
                if (_npm[ns] > bestPm) bestPm = _npm[ns];
            }
            // Normalise to keep metrics bounded over an unbounded stream.
            for (int ns = 0; ns < ConvCode.NumStates; ++ns) _pm[ns] = _npm[ns] - bestPm;

            ++_step;

            if (_step >= TracebackDepth)
            {
                // Best current state, then walk back kTracebackDepth steps to
                // recover the info bit emitted kTracebackDepth steps ago.
                int cur = 0;
                float top = float.NegativeInfinity;
                for (int ns = 0; ns < ConvCode.NumStates; ++ns)
                {
                    if (_pm[ns] > top) { top = _pm[ns]; cur = ns; }
                }
                // Walk back kTracebackDepth-1 steps so cur lands on the state
                // *after* the pair whose info bit we emit (its high bit is that
                // info bit).
                long head = _step - 1;  // step index just written
                for (int i = 0; i < TracebackDepth - 1; ++i)
                {
                    byte[] d = _dec[(int)((head - i) % RingLen)];
                    cur = _pred[cur, d[cur]];
                }
                byte bit = (byte)((cur >> (ConvCode.Memory - 1)) & 1);
                outBits.Add(bit);

                // Re-encode BER: encode the emitted bit and compare its two coded
                // bits to the received hard decisions of the pair it came from.
                long p = _step - TracebackDepth;       // emitted pair index
                uint reg = ((uint)bit << ConvCode.Memory) | _reencState;
                int c0 = ConvCode.Parity(reg & ConvCode.G1);
                int c1 = ConvCode.Parity(reg & ConvCode.G2);
                _reencState = (reg >> 1) & (ConvCode.NumStates - 1);
                byte h = _hardRing[(int)(p % RingLen)];
                int errs = (c0 != ((h >> 1) & 1) ? 1 : 0) + (c1 != (h & 1) ? 1 : 0);
                _berEma += BerAlpha * (errs * 0.5f - _berEma);
            }
        }

        // Push one soft symbol. Appends a decoded info bit to out for every
        // trellis step that completes past the traceback latency.
        public void Push(float soft, List<byte> outBits)
        {
            if (!_havePending)
            {
                _pending = soft;
                _havePending = true;
                return;
            }
            _havePending = false;
            AcsStep(_pending, soft, outBits);
        }
    }
}
