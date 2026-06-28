// Thin gzip-inflate wrapper for the single gzip member embedded in a
// NowcastPacket. Inflates until the end of the gzip stream, so trailing
// NowcastPacket padding after the gzip stream is ignored. Ported from
// src/decode/gunzip.{h,cpp} (zlib → System.IO.Compression.GZipStream).

using System;
using System.IO;
using System.IO.Compression;

namespace SDRSharp.JAlert.Decode
{
    internal static class Gunzip
    {
        // Inflate a gzip stream starting at data[offset] for len bytes. Returns
        // true and sets `result` on success; false on any error or truncated
        // input.
        public static bool TryInflate(byte[] data, int offset, int len, out byte[] result)
        {
            result = null;
            if (data == null || len <= 0) return false;
            try
            {
                using (MemoryStream input = new MemoryStream(data, offset, len, writable: false))
                using (GZipStream gz = new GZipStream(input, CompressionMode.Decompress))
                using (MemoryStream output = new MemoryStream())
                {
                    gz.CopyTo(output);
                    if (output.Length == 0) return false;
                    result = output.ToArray();
                    return true;
                }
            }
            catch (Exception)
            {
                // Corrupt / truncated gzip — treat as "not inflatable".
                return false;
            }
        }
    }
}
