#pragma once
// Streaming HDLC deframer for the J-ALERT on-air framing:
// bit-stuffed, flag-delimited (0x7E) frames with a CRC-16/X.25 FCS.
//
// Fed the descrambled bit stream one bit at a time. A run of ones terminated by
// a zero is resolved as: ≤4 ones → data; exactly 5 → a stuffed zero is dropped;
// exactly 6 → flag (frame boundary); ≥7 → abort. Frame bytes are packed
// LSB-first (HDLC bit order). On each flag the accumulated frame is validated by
// CRC-16/X.25 (good residual 0xF0B8); valid frames have their 2-byte FCS
// stripped and the payload handed to the sink.

#include <cstddef>
#include <cstdint>
#include <functional>
#include <vector>

namespace jalert::frame {

class HdlcDeframer {
public:
    // Payload = frame with the trailing 2-byte FCS removed (CRC already verified).
    using FrameSink = std::function<void(const uint8_t* payload, size_t len)>;

    explicit HdlcDeframer(FrameSink sink) : sink_(std::move(sink)) {}

    void reset();
    void push(uint8_t bit);

    // Running counters for the UI / diagnostics.
    long long frames_ok() const { return frames_ok_; }
    long long frames_bad() const { return frames_bad_; }

private:
    void add_data_bit(uint8_t b);
    void close_frame();   // flag seen: validate + emit
    void reset_frame();   // abort / discard partial

    FrameSink sink_;
    std::vector<uint8_t> bytes_;
    uint8_t cur_byte_ = 0;
    int nbits_ = 0;
    int ones_ = 0;
    long long frames_ok_ = 0;
    long long frames_bad_ = 0;
};

} // namespace jalert::frame
