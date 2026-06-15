#pragma once
// Real-time scatter / heatmap of the Costas-derotated BPSK symbols. BPSK
// clusters at (±amp, 0), so EVM and the ideal markers reference the real axis.

#include <functional>
#include <vector>

#include "dsp/complex.h"

namespace jalert::ui {

// Newest-first pull (matches BpskDemod::pull_constellation()).
using ConstellationPull = std::function<int(dsp::Complex32* dst, int capacity)>;

class ConstellationView {
public:
    static constexpr int kBinCount = 80;
    static constexpr float kAccumDecay = 0.88f;
    static constexpr int kPullPerFrame = 256;
    static constexpr int kDotsOverlayCount = 96;
    static constexpr float kAmpMax = 1.5f;
    static constexpr double kClipWarnRatio = 0.02;
    static constexpr double kObservedAmpAlpha = 0.05;

    explicit ConstellationView(ConstellationPull pull);

    void draw(float edge_px);
    void reset();

private:
    void decay_accumulator();
    void accumulate_and_stats(int n);

    ConstellationPull pull_;
    std::vector<dsp::Complex32> scratch_;
    float accum_[kBinCount * kBinCount] = {};

    double evm_percent_ = 0.0;
    int clipped_this_frame_ = 0;
    int samples_this_frame_ = 0;
    float last_accum_max_ = 0.0f;
    double observed_amp_ = 1.0;
};

} // namespace jalert::ui
