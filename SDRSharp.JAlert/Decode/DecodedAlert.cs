// Decoded J-ALERT message: an assembled NowcastPacket file whose JMA Socket
// Packet body has been inflated to JMA alert XML, plus a few headline fields
// lifted out for display / logging. Ported from src/decode/alert.h.

namespace SDRSharp.JAlert.Decode
{
    public sealed class DecodedAlert
    {
        public bool Ok;                   // gzip inflated to XML
        public string ChunkType = "";     // "wrmx"/"eprx"/"issx"/"ioeq"/…
        public string Timestamp = "";     // 17-digit packet timestamp
        public string Id = "";            // 4-char file id
        public string Filename = "";      // metadata filename
        public ushort Flags;              // 3 = gzipped
        public int GzipOffset = -1;       // gzip offset within the assembled data

        public byte[] Xml = System.Array.Empty<byte>();   // inflated XML bytes (when ok)
        public byte[] Data = System.Array.Empty<byte>();  // raw recovered file bytes, kept only
                                                          // when NOT inflated to XML so the
                                                          // payload is still accessible downstream

        // Lightweight (non-validating) extracts from the JMA XML:
        public string ControlTitle = "";  // <Control><Title>
        public string HeadTitle = "";     // <Head><Title>
        public string InfoType = "";      // <Head><InfoType> 発表/更新/取消
        public string ReportDateTime = ""; // <Head><ReportDateTime>
        public string HeadlineText = "";  // <Head><Headline><Text>

        // JMA telegram chunk types.
        public bool IsJma =>
            ChunkType == "wrmx" || ChunkType == "eprx" ||
            ChunkType == "issx" || ChunkType == "ioeq";
    }
}
