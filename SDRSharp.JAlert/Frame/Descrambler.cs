// IESS-308 self-synchronising descrambler, polynomial 1 + x³ + x²⁰:
// out[n] = in[n] ^ in[n-3] ^ in[n-20]. Ported from src/frame/descrambler.h.
//
// Self-synchronising means no seed/lock is needed — the shift register fills from
// the received stream itself, so the descrambler is correct 20 bits after any
// entry point. Edge handling (n<3, n<20) matches numpy's
// "o[3:]^=v[:-3]; o[20:]^=v[:-20]".

namespace SDRSharp.JAlert.Frame
{
    internal sealed class Descrambler
    {
        private const int Mask = 31;          // ring size 32 (>= 20 taps)
        private readonly byte[] _buf = new byte[32];
        private long _count;

        public void Reset()
        {
            _count = 0;
            System.Array.Clear(_buf, 0, _buf.Length);
        }

        public byte Push(byte v)
        {
            v &= 1;
            byte outBit = v;
            if (_count >= 3) outBit ^= _buf[(_count - 3) & Mask];
            if (_count >= 20) outBit ^= _buf[(_count - 20) & Mask];
            _buf[_count & Mask] = v;
            ++_count;
            return outBit;
        }
    }
}
