#include "filter_design.h"

#include <algorithm>
#include <cmath>
#include <stdexcept>

namespace jalert::dsp {

std::vector<float> lowpass_taps(int ntaps, float cutoff_hz, float fs_hz) {
    if ((ntaps & 1) == 0) {
        throw std::invalid_argument("ntaps must be odd for a symmetric LPF");
    }
    const float m = static_cast<float>(ntaps - 1);
    const float fc = cutoff_hz / fs_hz;
    const float two_pi_fc = 2.0f * static_cast<float>(M_PI) * fc;
    std::vector<float> taps(ntaps);
    float sum = 0.0f;
    for (int n = 0; n < ntaps; ++n) {
        const float k = n - m / 2.0f;
        float sinc;
        if (std::fabs(k) < 1e-9f) {
            sinc = two_pi_fc;
        } else {
            sinc = std::sin(two_pi_fc * k) / k;
        }
        const float w = 0.54f - 0.46f * std::cos(2.0f * static_cast<float>(M_PI) * n / m);
        const float v = sinc * w;
        taps[n] = v;
        sum += v;
    }
    for (int i = 0; i < ntaps; ++i) taps[i] /= sum;
    return taps;
}

std::vector<float> rrc_taps(float gain, float fs, float sym_rate, float alpha, int ntaps) {
    if ((ntaps & 1) == 0) ntaps += 1;
    const float spb = fs / sym_rate;
    const float mid = (ntaps - 1) / 2.0f;
    std::vector<float> taps(ntaps);
    float scale = 0.0f;
    const float four_alpha = 4.0f * alpha;

    for (int k = 0; k < ntaps; ++k) {
        const float n = k - mid;
        const float t = n / spb;
        float v;
        if (std::fabs(n) < 1e-9f) {
            v = 1.0f - alpha + 4.0f * alpha / static_cast<float>(M_PI);
        } else if (std::fabs(std::fabs(four_alpha * t) - 1.0f) < 1e-7f) {
            // l'Hôpital singularity at t = ±1/(4α).
            const float s = std::sin(static_cast<float>(M_PI) / (4.0f * alpha));
            const float c = std::cos(static_cast<float>(M_PI) / (4.0f * alpha));
            v = (alpha / std::sqrt(2.0f))
                * ((1.0f + 2.0f / static_cast<float>(M_PI)) * s
                 + (1.0f - 2.0f / static_cast<float>(M_PI)) * c);
        } else {
            const float pt = static_cast<float>(M_PI) * t;
            const float num = std::sin(pt * (1.0f - alpha))
                            + four_alpha * t * std::cos(pt * (1.0f + alpha));
            const float den = pt * (1.0f - (four_alpha * t) * (four_alpha * t));
            v = num / den;
        }
        taps[k] = v;
        scale += v;
    }

    const float inv = gain / scale;
    for (int i = 0; i < ntaps; ++i) taps[i] *= inv;
    return taps;
}

std::vector<std::vector<float>> polyphase_decompose(const std::vector<float>& taps, int nfilt) {
    const int taps_per_arm = static_cast<int>((taps.size() + nfilt - 1) / nfilt);
    const int total = nfilt * taps_per_arm;
    std::vector<float> padded(total, 0.0f);
    std::copy(taps.begin(), taps.end(), padded.begin());

    std::vector<std::vector<float>> arms(nfilt);
    for (int i = 0; i < nfilt; ++i) {
        std::vector<float> arm(taps_per_arm);
        for (int j = 0; j < taps_per_arm; ++j) arm[j] = padded[i + j * nfilt];
        std::reverse(arm.begin(), arm.end());
        arms[i] = std::move(arm);
    }
    return arms;
}

std::vector<float> create_diff_taps(const std::vector<float>& taps, int nfilt) {
    const int n = static_cast<int>(taps.size());
    std::vector<float> dt(n, 0.0f);
    float pwr = 0.0f;
    if (n >= 3) {
        for (int i = 0; i < n - 2; ++i) {
            const float v = -taps[i] + taps[i + 2];
            dt[i + 1] = v;
            pwr += std::fabs(v);
        }
    }
    if (pwr > 0.0f) {
        const float scale = static_cast<float>(nfilt) / pwr;
        for (int i = 0; i < n; ++i) dt[i] *= scale;
    }
    return dt;
}

} // namespace jalert::dsp
