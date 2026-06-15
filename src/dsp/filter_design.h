#pragma once
// DSP filter design helpers. The J-ALERT chain uses rrc_taps() for the β=0.35
// matched filter and lowpass_taps() inside the polyphase resampler.

#include <vector>

namespace jalert::dsp {

// Hamming-windowed sinc low-pass FIR, normalised to unity DC gain.
// ntaps must be odd.
std::vector<float> lowpass_taps(int ntaps, float cutoff_hz, float fs_hz);

// Root-raised-cosine impulse response, DC-normalised so sum(taps) == gain.
// If ntaps is even it is increased by 1 (the implementation needs an odd length).
std::vector<float> rrc_taps(float gain, float fs, float sym_rate, float alpha, int ntaps);

// Polyphase-decompose a master filter into nfilt arms, stored in reverse order
// so the convolution downstream walks the buffer forward.
std::vector<std::vector<float>> polyphase_decompose(const std::vector<float>& taps, int nfilt);

// Centred [-1, 0, +1] difference of the master taps, scaled so the sum of
// magnitudes equals nfilt. Used by PFB-style clock sync to derive its timing
// error from the master RRC.
std::vector<float> create_diff_taps(const std::vector<float>& taps, int nfilt);

} // namespace jalert::dsp
