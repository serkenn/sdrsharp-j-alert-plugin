// Standard CRC-32 (ISO-HDLC / zlib / Ethernet): reflected polynomial 0xEDB88320,
// init 0xFFFFFFFF, final xor 0xFFFFFFFF. Matches zlib's crc32() used by the
// original C++ NowcastPacket body-CRC verification.

namespace SDRSharp.JAlert.Frame
{
    internal static class Crc32
    {
        private static readonly uint[] Table = BuildTable();

        private static uint[] BuildTable()
        {
            uint[] t = new uint[256];
            for (uint i = 0; i < 256; ++i)
            {
                uint c = i;
                for (int k = 0; k < 8; ++k)
                    c = ((c & 1) != 0) ? (0xEDB88320u ^ (c >> 1)) : (c >> 1);
                t[i] = c;
            }
            return t;
        }

        public static uint Compute(byte[] data, int offset, int len)
        {
            uint crc = 0xFFFFFFFFu;
            for (int i = 0; i < len; ++i)
                crc = Table[(crc ^ data[offset + i]) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }
    }
}
