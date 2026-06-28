// Rate-1/2 convolutional code used by the J-ALERT satellite link:
// K=7, generator polynomials (171, 133) in octal — i.e. the classic NASA /
// CCSDS "Voyager" code, the standard "sequential 1/2" inner FEC of COTS
// satellite modems. Ported from src/fec/conv_code.{h,cpp}.
//
// Bit convention:
//   reg = (input_bit << (K-1)) | state      // input shifts in at the MSB
//   out0 = parity(reg & G1),  out1 = parity(reg & G2)
//   next_state = (reg >> 1) & (NSTATES-1)
// Coded bit b maps to BPSK symbol (1 - 2b): 0 -> +1, 1 -> -1.

namespace SDRSharp.JAlert.Fec
{
    internal static class ConvCode
    {
        public const int Constraint = 7;                 // K
        public const int Memory = Constraint - 1;        // 6
        public const int NumStates = 1 << Memory;        // 64
        public const uint G1 = 0x79;                     // octal 171
        public const uint G2 = 0x5B;                     // octal 133

        public static int Parity(uint x)
        {
            // Even parity (XOR of all set bits).
            x ^= x >> 16;
            x ^= x >> 8;
            x ^= x >> 4;
            x ^= x >> 2;
            x ^= x >> 1;
            return (int)(x & 1u);
        }

        // Convolutional-encode info bits (0/1) into 2× coded bits (0/1), starting
        // from the zero state. Used by the self-test and to seed re-encode BER
        // checks.
        public static byte[] Encode(byte[] bits, uint g1 = G1, uint g2 = G2)
        {
            byte[] outBits = new byte[bits.Length * 2];
            uint s = 0;
            for (int i = 0; i < bits.Length; ++i)
            {
                uint reg = ((uint)(bits[i] & 1u) << Memory) | s;
                outBits[2 * i] = (byte)Parity(reg & g1);
                outBits[2 * i + 1] = (byte)Parity(reg & g2);
                s = (reg >> 1) & (NumStates - 1);
            }
            return outBits;
        }
    }
}
