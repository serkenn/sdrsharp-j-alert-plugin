#include "polyphase_resampler.h"

#include <algorithm>
#include <cmath>
#include <stdexcept>

#include "filter_design.h"

namespace jalert::dsp {

PolyphaseResampler::PolyphaseResampler(double input_rate_hz, double output_rate_hz,
                                       double cutoff_hz, int nphases)
    : nphases_(nphases) {
    if (input_rate_hz <= 0.0) throw std::invalid_argument("input_rate_hz must be positive");
    if (output_rate_hz <= 0.0) throw std::invalid_argument("output_rate_hz must be positive");

    // Cut-off must stay below 0.45×min(Nyquist) on both sides to avoid aliasing /
    // first-image leakage.
    const double fc = std::min(cutoff_hz, 0.45 * std::min(input_rate_hz, output_rate_hz));

    // Prototype span ~1.5 ms of input — gentle skirt — but capped so the
    // per-output convolution stays cheap at the MHz-class rates this plugin
    // runs (1.5 ms × 2 MHz would be 3000 taps/arm). With nphases arms the
    // capped prototype is still nphases × taps_per_arm taps, plenty for a clean
    // anti-alias well below the 250 kHz channel cutoff.
    taps_per_arm_ = std::max(8, static_cast<int>(std::round(1.5e-3 * input_rate_hz)));
    taps_per_arm_ = std::min(taps_per_arm_, 32);
    half_taps_ = taps_per_arm_ / 2;

    const int proto_len = nphases_ * taps_per_arm_;
    const int odd_len = (proto_len & 1) == 0 ? proto_len + 1 : proto_len;
    std::vector<float> proto_odd = lowpass_taps(odd_len, static_cast<float>(fc),
                                                static_cast<float>(nphases * input_rate_hz));
    std::vector<float> proto(proto_len);
    std::copy(proto_odd.begin(), proto_odd.begin() + proto_len, proto.begin());
    for (int i = 0; i < proto_len; ++i) proto[i] *= nphases_;

    arms_.resize(nphases_);
    for (int a = 0; a < nphases_; ++a) {
        std::vector<float> arm(taps_per_arm_);
        for (int j = 0; j < taps_per_arm_; ++j) arm[j] = proto[a + j * nphases_];
        std::reverse(arm.begin(), arm.end());
        arms_[a] = std::move(arm);
    }

    step_ = input_rate_hz / output_rate_hz;
    buf_len_ = taps_per_arm_ + 16;
    buf_re_.assign(buf_len_, 0.0f);
    buf_im_.assign(buf_len_, 0.0f);
    reset();
}

void PolyphaseResampler::reset() {
    std::fill(buf_re_.begin(), buf_re_.end(), 0.0f);
    std::fill(buf_im_.begin(), buf_im_.end(), 0.0f);
    buf_start_ = 0;
    buf_count_ = 0;
    in_index_ = 0;
    next_out_ = static_cast<double>(half_taps_);
}

void PolyphaseResampler::step(Complex32 z, std::vector<Complex32>& output) {
    // Push into the ring, dropping the oldest if it is full.
    if (buf_count_ == buf_len_) {
        buf_start_ = (buf_start_ + 1 == buf_len_) ? 0 : buf_start_ + 1;
        --buf_count_;
    }
    int w = buf_start_ + buf_count_;
    if (w >= buf_len_) w -= buf_len_;
    buf_re_[w] = z.re;
    buf_im_[w] = z.im;
    ++buf_count_;
    ++in_index_;

    const long long oldest = in_index_ - buf_count_;
    const long long newest = in_index_ - 1;

    while (true) {
        long long ip = static_cast<long long>(std::floor(next_out_));
        double frac = next_out_ - ip;
        int arm_idx = static_cast<int>(std::round(frac * nphases_));
        if (arm_idx >= nphases_) { arm_idx = 0; ++ip; }

        const long long lo = ip - half_taps_;
        const long long hi = lo + taps_per_arm_ - 1;
        if (hi > newest) break;
        if (lo < oldest) {
            // Window underflowed the buffer — only possible if the ring is too
            // small for the ratio. Skip rather than read stale data.
            next_out_ += step_;
            continue;
        }

        const std::vector<float>& arm = arms_[arm_idx];
        int bi = static_cast<int>(buf_start_ + (lo - oldest));
        if (bi >= buf_len_) bi -= buf_len_;
        float yr = 0.0f, yi = 0.0f;
        for (int j = 0; j < taps_per_arm_; ++j) {
            const float t = arm[j];
            yr += buf_re_[bi] * t;
            yi += buf_im_[bi] * t;
            if (++bi == buf_len_) bi = 0;
        }
        output.emplace_back(yr, yi);
        next_out_ += step_;
    }
}

} // namespace jalert::dsp
