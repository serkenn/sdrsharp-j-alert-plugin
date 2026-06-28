// Post-Viterbi bit pipeline: decoded info bits → double-differential + invert →
// IESS-308 descramble → HDLC deframe → NowcastPacket → chunk reassembly →
// inflated alert. Ported from src/decode/bit_pipeline.h.

using System;
using System.Collections.Generic;
using SDRSharp.JAlert.Frame;

namespace SDRSharp.JAlert.Decode
{
    internal sealed class BitPipeline
    {
        private readonly Action<DecodedAlert> _sink;
        private readonly DifferentialDecoder _diff = new DifferentialDecoder();
        private readonly Descrambler _descr = new Descrambler();
        private readonly HdlcDeframer _hdlc;
        private readonly ChunkReassembler _reasm = new ChunkReassembler();
        private readonly List<AssembledFile> _done = new List<AssembledFile>();

        private long _packets;
        private long _statusPackets;
        private long _files;
        private long _alerts;

        public BitPipeline(Action<DecodedAlert> sink)
        {
            _sink = sink;
            _hdlc = new HdlcDeframer(OnFrame);
        }

        public void Reset()
        {
            _diff.Reset();
            _descr.Reset();
            _hdlc.Reset();
            _reasm.Reset();
            _packets = _statusPackets = _files = _alerts = 0;
        }

        public void PushBit(byte b)
        {
            _hdlc.Push(_descr.Push(_diff.Push(b)));
        }

        public long FramesOk => _hdlc.FramesOk;
        public long FramesBad => _hdlc.FramesBad;
        public long Packets => _packets;
        public long StatusPackets => _statusPackets;
        public long Files => _files;
        public long Alerts => _alerts;

        private void OnFrame(byte[] payload, int len)
        {
            if (!NowcastPacketParser.Parse(payload, len, out NowcastPacket pkt)) return;
            ++_packets;
            if (pkt.IsStatus) { ++_statusPackets; return; }

            _done.Clear();
            foreach (NowcastChunk c in pkt.Chunks) _reasm.Add(c, pkt.Timestamp, _done);
            foreach (AssembledFile f in _done)
            {
                ++_files;
                DecodedAlert a = AlertDecoder.Decode(f);
                if (a.Ok) ++_alerts;
                _sink?.Invoke(a);
            }
        }
    }
}
