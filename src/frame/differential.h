#pragma once
// Double-differential decode + bit inversion, applied to the Viterbi output
// before descrambling.
//
// Single differential decode (out[n] = in[n] ^ in[n-1]) is invariant to a
// global bit inversion, which is exactly the 180° phase ambiguity a squaring /
// Costas BPSK recovery leaves behind — so the polarity the carrier loop happens
// to lock to does not matter. Applied twice it reduces to out[n] = in[n] ^
// in[n-2] (the n=0,1 edge bits pass through, matching numpy's diffn). The final
// ⊕1 is the fixed convention this link expects.

#include <cstdint>

namespace jalert::frame {

class DifferentialDecoder {
public:
    void reset() { count_ = 0; h1_ = 0; h2_ = 0; }

    uint8_t push(uint8_t b) {
        b &= 1u;
        uint8_t d2;
        if (count_ < 2) {
            d2 = b;                 // d2[0]=x[0], d2[1]=x[1]
            ++count_;
        } else {
            d2 = b ^ h2_;           // d2[n]=x[n]^x[n-2]
        }
        h2_ = h1_;
        h1_ = b;
        return d2 ^ 1u;             // invert
    }

private:
    int count_ = 0;
    uint8_t h1_ = 0;
    uint8_t h2_ = 0;
};

} // namespace jalert::frame
