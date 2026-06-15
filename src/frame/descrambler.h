#pragma once
// IESS-308 self-synchronising descrambler, polynomial 1 + x³ + x²⁰:
// out[n] = in[n] ^ in[n-3] ^ in[n-20].
//
// Self-synchronising means no seed/lock is needed — the shift register fills
// from the received stream itself, so the descrambler is correct 20 bits after
// any entry point. Edge handling (n<3, n<20) matches numpy's
// "o[3:]^=v[:-3]; o[20:]^=v[:-20]".

#include <array>
#include <cstdint>

namespace jalert::frame {

class Descrambler {
public:
    void reset() { count_ = 0; buf_.fill(0); }

    uint8_t push(uint8_t v) {
        v &= 1u;
        uint8_t out = v;
        if (count_ >= 3)  out ^= buf_[(count_ - 3) & kMask];
        if (count_ >= 20) out ^= buf_[(count_ - 20) & kMask];
        buf_[count_ & kMask] = v;
        ++count_;
        return out;
    }

private:
    static constexpr int kMask = 31;          // ring size 32 (>= 20 taps)
    std::array<uint8_t, 32> buf_{};
    long long count_ = 0;
};

} // namespace jalert::frame
