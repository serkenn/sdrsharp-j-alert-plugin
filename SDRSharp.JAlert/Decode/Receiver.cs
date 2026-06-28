// Top-level J-ALERT receiver: complex IQ → decoded alerts.
// Ported from src/decode/receiver.h.
//
// The convolutional code is consumed in symbol pairs, so the decoder has a
// one-symbol pairing ambiguity. Rather than guess, we run two Viterbi+pipeline
// chains in parallel — one starting on each pairing phase — and let the HDLC CRC
// decide: the wrong phase produces only garbage that fails the FCS, so only the
// correctly-aligned chain ever emits a packet. (Carrier-phase / bit-polarity
// ambiguity is already handled by the differential decode, so two chains suffice
// — no need to also try inversions.)

using System;
using System.Collections.Generic;
using SDRSharp.JAlert.Dsp;
using SDRSharp.JAlert.Fec;

namespace SDRSharp.JAlert.Decode
{
    public sealed class Receiver
    {
        private readonly BpskDemod _demod;
        private readonly ViterbiDecoder _vit0 = new ViterbiDecoder();
        private readonly ViterbiDecoder _vit1 = new ViterbiDecoder();
        private readonly BitPipeline _pipe0;
        private readonly BitPipeline _pipe1;
        private readonly List<float> _softs = new List<float>(8);
        private readonly List<byte> _bits = new List<byte>(8);
        private bool _skipped;

        public Receiver(double sampleRateHz, Action<DecodedAlert> sink)
        {
            _demod = new BpskDemod(sampleRateHz);
            _pipe0 = new BitPipeline(sink);
            _pipe1 = new BitPipeline(sink);
        }

        public void Process(Complex32 z)
        {
            _softs.Clear();
            _demod.Process(z, _softs);
            for (int si = 0; si < _softs.Count; ++si)
            {
                float s = _softs[si];

                _bits.Clear();
                _vit0.Push(s, _bits);
                for (int bi = 0; bi < _bits.Count; ++bi) _pipe0.PushBit(_bits[bi]);

                if (!_skipped) { _skipped = true; continue; }  // off=1 pairing
                _bits.Clear();
                _vit1.Push(s, _bits);
                for (int bi = 0; bi < _bits.Count; ++bi) _pipe1.PushBit(_bits[bi]);
            }
        }

        public void Reset()
        {
            _demod.Reset();
            _vit0.Reset();
            _vit1.Reset();
            _pipe0.Reset();
            _pipe1.Reset();
            _skipped = false;
        }

        public BpskDemod Demod => _demod;

        // Estimated coded BER. The wrong pairing phase decodes noise (elevated
        // BER), so the live chain's quality is the lower of the two.
        public float Ber => Math.Min(_vit0.Ber, _vit1.Ber);

        public long FramesOk => _pipe0.FramesOk + _pipe1.FramesOk;
        public long FramesBad => _pipe0.FramesBad + _pipe1.FramesBad;
        public long Packets => _pipe0.Packets + _pipe1.Packets;
        public long StatusPackets => _pipe0.StatusPackets + _pipe1.StatusPackets;
        public long Files => _pipe0.Files + _pipe1.Files;
        public long Alerts => _pipe0.Alerts + _pipe1.Alerts;
    }
}
