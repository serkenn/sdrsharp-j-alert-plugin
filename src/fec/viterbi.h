#pragma once
// Streaming soft-decision Viterbi decoder for the K=7 (171,133) rate-1/2 code.
//
// Soft symbols arrive one at a time and are consumed in pairs (the two coded
// bits per info bit). Each completed pair advances the trellis one step; once
// the traceback window is primed, every step emits exactly one decoded info bit
// at a fixed latency (kTracebackDepth pairs). This continuous, self-
// synchronising operation matches a live receiver — no block boundaries, no
// known terminating state.
//
// Soft convention: coded bit 0 -> expected symbol +1, bit 1 -> -1, so a more
// positive soft value favours coded bit 0.

#include <array>
#include <cstdint>
#include <vector>

#include "conv_code.h"

namespace jalert::fec {

class ViterbiDecoder {
public:
    // Traceback depth in trellis steps. ~5×K is the textbook minimum for this
    // code to reach the error floor; 96 gives generous margin at negligible cost.
    static constexpr int kTracebackDepth = 96;

    ViterbiDecoder() { reset(); }

    void reset();

    // Push one soft symbol. Appends a decoded info bit to out for every trellis
    // step that completes past the traceback latency.
    void push(float soft, std::vector<uint8_t>& out);

    // Decoder latency, in info bits, from a coded-symbol pair entering to its
    // info bit being emitted.
    static constexpr int latency_bits() { return kTracebackDepth; }

    // Estimated coded bit-error rate: re-encodes each decoded info bit and
    // compares the two coded bits against the received hard decisions,
    // exponentially averaged. A few ×1e-3 on a clean signal; with no signal the
    // ML decoder still finds the nearest codeword to noise, so it floors around
    // ~0.15 for this code rather than 0.5. Used as the GUI signal-quality metric.
    float ber() const { return ber_ema_; }

private:
    void build_tables();
    void acs_step(float r0, float r1, std::vector<uint8_t>& out);

    // Trellis tables (built once).
    std::array<std::array<uint8_t, 2>, kNumStates> pred_{};   // predecessor states
    std::array<std::array<float, 2>, kNumStates> sym0_{};     // expected symbol, coded bit 0
    std::array<std::array<float, 2>, kNumStates> sym1_{};     // expected symbol, coded bit 1

    std::array<float, kNumStates> pm_{};      // current path metrics
    std::array<float, kNumStates> npm_{};     // scratch next metrics

    // Traceback ring: dec_[t % L][ns] = surviving predecessor index (0/1).
    static constexpr int kRingLen = kTracebackDepth + 1;
    std::vector<std::array<uint8_t, kNumStates>> dec_;

    // Re-encode BER estimate.
    std::array<uint8_t, kRingLen> hard_ring_{};  // received hard pair per step (h0<<1|h1)
    unsigned reenc_state_ = 0;                    // re-encoder state, driven by emitted bits
    float ber_ema_ = 0.5f;

    long long step_ = 0;        // total completed trellis steps
    float pending_ = 0.0f;      // first soft of an incomplete pair
    bool have_pending_ = false; // a soft is buffered awaiting its partner
};

} // namespace jalert::fec
