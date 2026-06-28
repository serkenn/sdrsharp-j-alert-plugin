// Lightweight per-file summary kept for the panel (no full XML — that goes to the
// sinks / output folder). Mirrors the AlertSummary struct in the SDR++ main.cpp.

namespace SDRSharp.JAlert
{
    public sealed class AlertSummary
    {
        public long RxTimeMs;
        public bool Decoded;
        public string ChunkType = "";
        public string PacketTime = "";
        public string ControlTitle = "";
        public string HeadTitle = "";
        public string InfoType = "";
        public string ReportTime = "";
        public string Headline = "";
        public int XmlBytes;
    }
}
