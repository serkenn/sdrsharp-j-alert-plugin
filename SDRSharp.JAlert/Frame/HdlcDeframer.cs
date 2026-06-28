// Streaming HDLC deframer for the J-ALERT on-air framing:
// bit-stuffed, flag-delimited (0x7E) frames with a CRC-16/X.25 FCS.
// Ported from src/frame/hdlc.{h,cpp}.
//
// Fed the descrambled bit stream one bit at a time. A run of ones terminated by a
// zero is resolved as: ≤4 ones → data; exactly 5 → a stuffed zero is dropped;
// exactly 6 → flag (frame boundary); ≥7 → abort. Frame bytes are packed LSB-first
// (HDLC bit order). On each flag the accumulated frame is validated by
// CRC-16/X.25 (good residual 0xF0B8); valid frames have their 2-byte FCS stripped
// and the payload handed to the sink.

using System;
using System.Collections.Generic;

namespace SDRSharp.JAlert.Frame
{
    internal sealed class HdlcDeframer
    {
        // Payload = frame with the trailing 2-byte FCS removed (CRC already verified).
        public delegate void FrameSink(byte[] frame, int len);

        // CRC-16/X.25: reflected poly 0x1021 (=0x8408), init 0xFFFF, no final xor
        // here so a frame INCLUDING its FCS yields the standard good residual 0xF0B8.
        private const ushort GoodResidual = 0xF0B8;

        private readonly FrameSink _sink;
        private readonly List<byte> _bytes = new List<byte>(512);
        private byte _curByte;
        private int _nbits;
        private int _ones;
        private long _framesOk;
        private long _framesBad;

        public HdlcDeframer(FrameSink sink) { _sink = sink; }

        public long FramesOk => _framesOk;
        public long FramesBad => _framesBad;

        private static ushort CrcX25Residual(List<byte> data)
        {
            ushort crc = 0xFFFF;
            for (int i = 0; i < data.Count; ++i)
            {
                crc ^= data[i];
                for (int k = 0; k < 8; ++k)
                    crc = (ushort)(((crc & 1) != 0) ? ((crc >> 1) ^ 0x8408) : (crc >> 1));
            }
            return crc;
        }

        public void Reset()
        {
            _bytes.Clear();
            _curByte = 0;
            _nbits = 0;
            _ones = 0;
            _framesOk = 0;
            _framesBad = 0;
        }

        private void AddDataBit(int b)
        {
            // LSB-first byte packing.
            _curByte |= (byte)((b & 1) << _nbits);
            if (++_nbits == 8)
            {
                _bytes.Add(_curByte);
                _curByte = 0;
                _nbits = 0;
            }
        }

        private void ResetFrame()
        {
            _bytes.Clear();
            _curByte = 0;
            _nbits = 0;
        }

        private void CloseFrame()
        {
            // A well-formed frame is octet-aligned; trailing partial bits (if any)
            // are dropped.
            if (_bytes.Count >= 4 && CrcX25Residual(_bytes) == GoodResidual)
            {
                ++_framesOk;
                if (_sink != null)
                {
                    int payloadLen = _bytes.Count - 2;  // strip FCS
                    byte[] payload = new byte[payloadLen];
                    _bytes.CopyTo(0, payload, 0, payloadLen);
                    _sink(payload, payloadLen);
                }
            }
            else if (_bytes.Count != 0 || _nbits != 0)
            {
                ++_framesBad;
            }
            ResetFrame();
        }

        public void Push(byte bit)
        {
            if ((bit & 1) != 0)
            {
                ++_ones;
                return;
            }
            // A zero terminates the current run of ones.
            if (_ones < 5)
            {
                for (int i = 0; i < _ones; ++i) AddDataBit(1);
                AddDataBit(0);
            }
            else if (_ones == 5)
            {
                for (int i = 0; i < 5; ++i) AddDataBit(1);  // drop the stuffed zero
            }
            else if (_ones == 6)
            {
                CloseFrame();                                // flag (0x7E)
            }
            else
            {
                ResetFrame();                                // abort / idle (>=7 ones)
            }
            _ones = 0;
        }
    }
}
