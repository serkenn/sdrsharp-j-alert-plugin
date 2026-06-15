#pragma once
// Arbitrary-ratio polyphase resampler. Used to re-pin the VFO stream to the
// J-ALERT working rate (4 sps × 256 ksym/s = 1.024 MHz).

#include <vector>

#include "complex.h"

namespace jalert::dsp {

class PolyphaseResampler {
public:
    PolyphaseResampler(double input_rate_hz, double output_rate_hz, double cutoff_hz,
                       int nphases = 32);

    void reset();

    // Push one input sample; append zero or more output samples to output.
    // Caller owns output and is expected to have cleared it for this call.
    void step(Complex32 z, std::vector<Complex32>& output);

private:
    int nphases_;
    int taps_per_arm_;
    int half_taps_;
    std::vector<std::vector<float>> arms_;
    double step_;
    std::vector<float> buf_re_;
    std::vector<float> buf_im_;
    int buf_len_;
    int buf_start_ = 0;
    int buf_count_ = 0;
    long long in_index_ = 0;
    double next_out_ = 0.0;
};

} // namespace jalert::dsp
