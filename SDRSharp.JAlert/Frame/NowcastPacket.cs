// NowcastPacket parser. All fields little-endian.
// Ported from src/frame/nowcast_packet.{h,cpp}.
//
//   packet      ::= "NWCS"(4) hdr_len(u16) SP(0x20) ts[17] hdr_crc32(u32)
//                   chunk-entry{n}  chunk{n}
//   hdr_len      = 28 + 6*n          (n = number of chunks)
//   chunk-entry ::= type[4] chunk_len(u16)                       (6 bytes)
//   chunk       ::= type[4] chunk_len(u16) body_crc32(u32) body
//   body        ::= seq(u32) num_chunks(u32) id[4]
//                   [ filename[256] data_size(i64) original_size(i64) flags(u16) ]?
//                   payload                       (metadata only when seq == 1)
//
// Notes:
//   * hdr_crc32 is ALWAYS WRONG (protocol-converter bug) — never validated.
//   * body_crc32 = zlib/Ethernet CRC-32 over body — valid, we verify it.
//   * flags == 3 means the carried file is gzipped.
//   * an empty packet (no chunks) is a status / keep-alive.
//   * a single file may span several chunks / packets (seq, num_chunks, id).

using System.Collections.Generic;
using System.Text;

namespace SDRSharp.JAlert.Frame
{
    public sealed class NowcastChunk
    {
        public string Type = "";          // 4-char chunk-type-code, lower-cased (e.g. "wrmx")
        public uint BodyCrc32;
        public bool CrcOk;

        public uint Seq;                  // 1-based position within the file
        public uint NumChunks;            // total chunks comprising the file
        public string Id = "";            // 4-char file id (shared across a file's chunks)

        public bool HasMetadata;          // present iff seq == 1
        public string Filename = "";      // metadata filename (nul-trimmed)
        public long DataSize;             // joined-data size
        public long OriginalSize;         // original file size (often 0 / unset)
        public ushort Flags;              // 3 = gzipped

        public byte[] Payload = System.Array.Empty<byte>();

        public bool Gzipped => Flags == 3;
    }

    public sealed class NowcastPacket
    {
        public bool Valid;
        public string Timestamp = "";     // 17-digit ASCII (empty if malformed)
        public uint HdrCrc32;             // present but unreliable (see note)
        public List<NowcastChunk> Chunks = new List<NowcastChunk>();

        public bool IsStatus => Chunks.Count == 0;  // keep-alive / idle
    }

    internal static class NowcastPacketParser
    {
        private const int BaseHeader = 28;     // magic(4)+hdrlen(2)+SP(1)+ts(17)+crc(4)
        private const int EntrySize = 6;       // type(4)+chunk_len(2)
        private const int ChunkHeader = 12;    // seq(4)+num_chunks(4)+id(4)
        private const int Metadata = 256 + 8 + 8 + 2;  // filename+data_size+original_size+flags
        private const int TsOffset = 7;
        private const int TsLen = 17;

        private static ushort RdU16(byte[] p, int o) => (ushort)(p[o] | (p[o + 1] << 8));

        private static uint RdU32(byte[] p, int o) =>
            (uint)p[o] | ((uint)p[o + 1] << 8) | ((uint)p[o + 2] << 16) | ((uint)p[o + 3] << 24);

        private static long RdI64(byte[] p, int o)
        {
            ulong v = 0;
            for (int i = 0; i < 8; ++i) v |= (ulong)p[o + i] << (8 * i);
            return (long)v;
        }

        private static string Lower4(byte[] p, int o)
        {
            char[] c = new char[4];
            for (int i = 0; i < 4; ++i)
            {
                char ch = (char)p[o + i];
                if (ch >= 'A' && ch <= 'Z') ch = (char)(ch - 'A' + 'a');
                c[i] = ch;
            }
            return new string(c);
        }

        private static bool IsDigit(byte b) => b >= (byte)'0' && b <= (byte)'9';

        // Parse a chunk that begins at offset `o` in `d`, spanning `chunkLen` bytes.
        private static bool ParseChunk(byte[] d, int o, int chunkLen, NowcastChunk c)
        {
            if (chunkLen < EntrySize + 4 + ChunkHeader) return false;
            c.Type = Lower4(d, o);
            ushort declaredLen = RdU16(d, o + 4);
            if (declaredLen < EntrySize + 4 || declaredLen > chunkLen) return false;
            c.BodyCrc32 = RdU32(d, o + 6);

            int bodyOff = o + 10;
            int bodyLen = declaredLen - 10;
            c.CrcOk = Crc32.Compute(d, bodyOff, bodyLen) == c.BodyCrc32;

            c.Seq = RdU32(d, bodyOff);
            c.NumChunks = RdU32(d, bodyOff + 4);
            c.Id = Encoding.ASCII.GetString(d, bodyOff + 8, 4);

            int off = ChunkHeader;
            if (c.Seq == 1 && bodyLen >= ChunkHeader + Metadata)
            {
                int m = bodyOff + off;
                int fnLen = 0;
                while (fnLen < 256 && d[m + fnLen] != 0) ++fnLen;
                c.Filename = Encoding.UTF8.GetString(d, m, fnLen);
                c.DataSize = RdI64(d, m + 256);
                c.OriginalSize = RdI64(d, m + 256 + 8);
                c.Flags = RdU16(d, m + 256 + 16);
                c.HasMetadata = true;
                off += Metadata;
            }

            int payloadLen = bodyLen - off;
            c.Payload = new byte[payloadLen];
            System.Array.Copy(d, bodyOff + off, c.Payload, 0, payloadLen);
            return true;
        }

        // Parse one HDLC payload as a NowcastPacket. Returns false if the magic is
        // absent or the buffer is too short / malformed.
        public static bool Parse(byte[] data, int len, out NowcastPacket outPkt)
        {
            outPkt = new NowcastPacket();
            if (len < BaseHeader) return false;
            if (data[0] != 'N' || data[1] != 'W' || data[2] != 'C' || data[3] != 'S') return false;

            ushort hdrLen = RdU16(data, 4);
            if (hdrLen < BaseHeader || hdrLen > len) return false;
            if ((hdrLen - BaseHeader) % EntrySize != 0) return false;
            int n = (hdrLen - BaseHeader) / EntrySize;

            bool tsOk = true;
            for (int i = 0; i < TsLen; ++i)
                if (!IsDigit(data[TsOffset + i])) { tsOk = false; break; }
            if (tsOk)
                outPkt.Timestamp = Encoding.ASCII.GetString(data, TsOffset, TsLen);

            outPkt.HdrCrc32 = RdU32(data, 24);

            // Chunk bodies follow the header; each entry's chunk_len gives the
            // body span.
            int bodyOff = hdrLen;
            for (int i = 0; i < n; ++i)
            {
                int e = BaseHeader + i * EntrySize;
                ushort chunkLen = RdU16(data, e + 4);
                if (chunkLen < EntrySize + 4 || bodyOff + chunkLen > len) break;
                NowcastChunk c = new NowcastChunk();
                if (ParseChunk(data, bodyOff, chunkLen, c)) outPkt.Chunks.Add(c);
                bodyOff += chunkLen;
            }

            outPkt.Valid = true;
            return true;
        }
    }
}
