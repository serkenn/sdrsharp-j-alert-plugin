// Double-differential decode + bit inversion, applied to the Viterbi output
// before descrambling. Ported from src/frame/differential.h.
//
// Single differential decode (out[n] = in[n] ^ in[n-1]) is invariant to a global
// bit inversion, which is exactly the 180° phase ambiguity a squaring / Costas
// BPSK recovery leaves behind — so the polarity the carrier loop happens to lock
// to does not matter. Applied twice it reduces to out[n] = in[n] ^ in[n-2] (the
// n=0,1 edge bits pass through, matching numpy's diffn). The final ⊕1 is the
// fixed convention this link expects.

namespace SDRSharp.JAlert.Frame
{
    internal sealed class DifferentialDecoder
    {
        private int _count;
        private byte _h1;
        private byte _h2;

        public void Reset() { _count = 0; _h1 = 0; _h2 = 0; }

        public byte Push(byte b)
        {
            b &= 1;
            byte d2;
            if (_count < 2)
            {
                d2 = b;                 // d2[0]=x[0], d2[1]=x[1]
                ++_count;
            }
            else
            {
                d2 = (byte)(b ^ _h2);   // d2[n]=x[n]^x[n-2]
            }
            _h2 = _h1;
            _h1 = b;
            return (byte)(d2 ^ 1);      // invert
        }
    }
}
