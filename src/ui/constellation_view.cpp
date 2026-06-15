#include "ui/constellation_view.h"

#include <imgui.h>

#include <algorithm>
#include <cmath>
#include <cstdio>
#include <cstring>

namespace jalert::ui {

ConstellationView::ConstellationView(ConstellationPull pull)
    : pull_(std::move(pull)) {
    scratch_.resize(kPullPerFrame);
}

void ConstellationView::reset() {
    std::memset(accum_, 0, sizeof(accum_));
    last_accum_max_ = 0.0f;
    observed_amp_ = 1.0;
}

void ConstellationView::decay_accumulator() {
    for (int i = 0; i < kBinCount * kBinCount; ++i) accum_[i] *= kAccumDecay;
}

void ConstellationView::accumulate_and_stats(int n) {
    samples_this_frame_ = n;
    clipped_this_frame_ = 0;

    const float ideal_axis = static_cast<float>(observed_amp_);  // BPSK: |Re|≈amp
    double evm_sq = 0.0, amp_sq = 0.0;
    int evm_n = 0;

    for (int i = 0; i < n; ++i) {
        const dsp::Complex32 z = scratch_[i];
        const float re = z.re, im = z.im;

        const float ideal_re = re >= 0.0f ? ideal_axis : -ideal_axis;
        const float d_re = re - ideal_re;
        const float d_im = im;                          // ideal Im = 0
        evm_sq += d_re * d_re + d_im * d_im;
        amp_sq += re * re + im * im;
        ++evm_n;

        if (std::fabs(re) > kAmpMax || std::fabs(im) > kAmpMax) {
            ++clipped_this_frame_;
            continue;
        }
        const int bx = static_cast<int>((re + kAmpMax) * (kBinCount / (2.0f * kAmpMax)));
        const int by = static_cast<int>((kAmpMax - im) * (kBinCount / (2.0f * kAmpMax)));
        if (static_cast<unsigned>(bx) >= kBinCount ||
            static_cast<unsigned>(by) >= kBinCount) continue;
        accum_[by * kBinCount + bx] += 1.0f;
    }

    if (evm_n > 0) {
        const double frame_amp = std::sqrt(amp_sq / evm_n);
        if (frame_amp > 0.05) {
            observed_amp_ = (1.0 - kObservedAmpAlpha) * observed_amp_
                            + kObservedAmpAlpha * frame_amp;
        }
        const double denom = std::max(0.1, observed_amp_);
        evm_percent_ = std::sqrt(evm_sq / evm_n) / denom * 100.0;
    } else {
        evm_percent_ = 0.0;
    }
}

void ConstellationView::draw(float edge_px) {
    if (edge_px < 16.0f) edge_px = 16.0f;

    ImDrawList* dl = ImGui::GetWindowDrawList();
    const ImVec2 cur = ImGui::GetCursorScreenPos();
    const float ox = cur.x, oy = cur.y, size = edge_px;

    dl->AddRectFilled(ImVec2(ox, oy), ImVec2(ox + size, oy + size),
                      IM_COL32(0, 0, 0, 255));

    const int pull_n = pull_ ? pull_(scratch_.data(),
                                     static_cast<int>(scratch_.size())) : 0;
    decay_accumulator();
    accumulate_and_stats(pull_n);

    const float cx = ox + size * 0.5f;
    const float cy = oy + size * 0.5f;
    const float scale = (size * 0.5f) / kAmpMax;

    const ImU32 dim    = IM_COL32(50, 220, 50, 40);
    const ImU32 marker = IM_COL32(50, 220, 50, 110);

    dl->AddRect(ImVec2(ox, oy), ImVec2(ox + size - 1, oy + size - 1), dim, 0.0f, 0, 1.0f);
    dl->AddLine(ImVec2(ox, cy), ImVec2(ox + size, cy), dim, 1.0f);
    dl->AddLine(ImVec2(cx, oy), ImVec2(cx, oy + size), dim, 1.0f);

    const float orbit = std::max(0.1f, std::min(kAmpMax, static_cast<float>(observed_amp_)));
    const float ideal_r = scale * orbit;
    const float m = std::max(3.0f, size / 50.0f);
    auto cross = [&](float px, float py) {
        dl->AddLine(ImVec2(px - m, py - m), ImVec2(px + m, py + m), marker, 1.0f);
        dl->AddLine(ImVec2(px - m, py + m), ImVec2(px + m, py - m), marker, 1.0f);
    };
    cross(cx + ideal_r, cy);    // BPSK ideal points on the real axis
    cross(cx - ideal_r, cy);

    float max_v = 0.0f;
    const float bin_px = size / static_cast<float>(kBinCount);
    constexpr float kAlphaScale = 32.0f;
    for (int i = 0; i < kBinCount * kBinCount; ++i) {
        const float v = accum_[i];
        if (v > max_v) max_v = v;
        int a = static_cast<int>(v * kAlphaScale);
        if (a <= 0) continue;
        if (a > 255) a = 255;
        const int by = i / kBinCount, bx = i % kBinCount;
        const float x0 = ox + bx * bin_px, y0 = oy + by * bin_px;
        dl->AddRectFilled(ImVec2(x0, y0), ImVec2(x0 + bin_px + 1.0f, y0 + bin_px + 1.0f),
                          IM_COL32(50, 220, 50, a));
    }
    last_accum_max_ = max_v;

    const ImU32 dot_col = IM_COL32(50, 220, 50, 220);
    const int take = std::min(pull_n, kDotsOverlayCount);
    const float l = ox, t = oy, r = ox + size, b_ = oy + size;
    for (int i = 0; i < take; ++i) {
        const dsp::Complex32 z = scratch_[i];
        const float px = cx + z.re * scale;
        const float py = cy - z.im * scale;
        if (px < l || px >= r || py < t || py >= b_) continue;
        dl->AddRectFilled(ImVec2(px - 1.0f, py - 1.0f), ImVec2(px + 1.0f, py + 1.0f), dot_col);
    }

    if (pull_n > 0) {
        char buf[32];
        std::snprintf(buf, sizeof(buf), "EVM %.1f%%", evm_percent_);
        const ImVec2 sz = ImGui::CalcTextSize(buf);
        dl->AddText(ImVec2(ox + size - sz.x - 4.0f, oy + 2.0f),
                    IM_COL32(50, 220, 50, 190), buf);

        if (samples_this_frame_ > 0 &&
            static_cast<double>(clipped_this_frame_) / samples_this_frame_ >= kClipWarnRatio) {
            const double pct = 100.0 * clipped_this_frame_ / samples_this_frame_;
            char cbuf[40];
            std::snprintf(cbuf, sizeof(cbuf), "! clip %.0f%%", pct);
            const ImVec2 csz = ImGui::CalcTextSize(cbuf);
            dl->AddText(ImVec2(ox + size - csz.x - 4.0f, oy + size - csz.y - 2.0f),
                        IM_COL32(255, 140, 0, 220), cbuf);
        }
    }

    if (pull_n == 0 && last_accum_max_ < 0.05f) {
        const char* msg = "no signal";
        const ImVec2 sz = ImGui::CalcTextSize(msg);
        dl->AddText(ImVec2(cx - sz.x * 0.5f, cy - sz.y * 0.5f),
                    IM_COL32(50, 220, 50, 140), msg);
    }

    ImGui::Dummy(ImVec2(size, size));
}

} // namespace jalert::ui
