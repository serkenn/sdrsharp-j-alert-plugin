// Reassembles a file that the NowcastPacket layer split across several chunks
// (and possibly several packets), keyed by the 4-char chunk id, ordered by seq.
// The common case is a single chunk (seq==1, num_chunks==1), completed
// immediately. Ported from src/frame/chunk_reassembler.h.

using System.Collections.Generic;
using System.IO;

namespace SDRSharp.JAlert.Frame
{
    public sealed class AssembledFile
    {
        public string Type = "";          // chunk-type-code (e.g. "wrmx", "eprx")
        public string Id = "";
        public string Timestamp = "";     // packet timestamp of the seq==1 chunk
        public string Filename = "";
        public long DataSize;
        public long OriginalSize;
        public ushort Flags;
        public byte[] Data = System.Array.Empty<byte>();  // concatenated payloads in seq order

        public bool Gzipped => Flags == 3;
    }

    internal sealed class ChunkReassembler
    {
        private const int MaxPending = 64;

        private sealed class Partial
        {
            public uint Num;
            public bool HaveMeta;
            public string Type = "";
            public string Filename = "";
            public string Timestamp = "";
            public long DataSize;
            public long OriginalSize;
            public ushort Flags;
            public readonly SortedDictionary<uint, byte[]> Parts = new SortedDictionary<uint, byte[]>();
        }

        private readonly Dictionary<string, Partial> _pending = new Dictionary<string, Partial>();

        // Append one chunk; appends any file(s) it completes to out (usually 0/1).
        public void Add(NowcastChunk c, string ts, List<AssembledFile> outFiles)
        {
            if (c.Seq == 1 && c.NumChunks == 1)        // fast path: whole file
            {
                outFiles.Add(MakeSingle(c, ts));
                return;
            }
            if (c.Seq == 0 || c.NumChunks == 0) return;  // malformed

            if (!_pending.TryGetValue(c.Id, out Partial p))
            {
                p = new Partial();
                _pending[c.Id] = p;
            }
            if (p.Num == 0) p.Num = c.NumChunks;
            if (c.Seq == 1)
            {
                p.HaveMeta = true;
                p.Type = c.Type;
                p.Filename = c.Filename;
                p.DataSize = c.DataSize;
                p.OriginalSize = c.OriginalSize;
                p.Flags = c.Flags;
                p.Timestamp = ts;
            }
            if (string.IsNullOrEmpty(p.Type)) p.Type = c.Type;
            p.Parts[c.Seq] = c.Payload;

            if (p.Parts.Count >= p.Num)
            {
                AssembledFile f = new AssembledFile
                {
                    Type = p.Type,
                    Id = c.Id,
                    Timestamp = string.IsNullOrEmpty(p.Timestamp) ? ts : p.Timestamp,
                    Filename = p.Filename,
                    DataSize = p.DataSize,
                    OriginalSize = p.OriginalSize,
                    Flags = p.Flags,
                };
                using (MemoryStream ms = new MemoryStream())
                {
                    foreach (KeyValuePair<uint, byte[]> kv in p.Parts)
                        ms.Write(kv.Value, 0, kv.Value.Length);
                    f.Data = ms.ToArray();
                }
                outFiles.Add(f);
                _pending.Remove(c.Id);
            }
            else if (_pending.Count > MaxPending)
            {
                // Bound memory; drop a stale partial.
                foreach (string key in _pending.Keys)
                {
                    _pending.Remove(key);
                    break;  // safe: we stop enumerating immediately after removal
                }
            }
        }

        public void Reset() => _pending.Clear();

        private static AssembledFile MakeSingle(NowcastChunk c, string ts)
        {
            return new AssembledFile
            {
                Type = c.Type,
                Id = c.Id,
                Timestamp = ts,
                Filename = c.Filename,
                DataSize = c.DataSize,
                OriginalSize = c.OriginalSize,
                Flags = c.Flags,
                Data = c.Payload,
            };
        }
    }
}
