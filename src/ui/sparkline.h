#pragma once
// One-row "caption · history-bars · current value" sparkline. Ring capacity
// 120; the panel pushes every 0.5 s for a 60 s rolling window.

#include <imgui.h>
#include <cmath>
#include <limits>
#include <string>

namespace jalert::ui {

class Sparkline {
public:
    static constexpr int kCapacity = 120;

    void push(double v) {
        ring_[write_idx_] = v;
        write_idx_ = (write_idx_ + 1) % kCapacity;
        if (count_ < kCapacity) ++count_;
    }

    void clear() { write_idx_ = 0; count_ = 0; }

    void set_fixed_range(double rmin, double rmax) {
        range_min_ = rmin;
        range_max_ = rmax;
    }

    void draw(const char* caption,
              const std::string& current_text,
              ImU32 bar_color,
              float caption_w = 110.0f,
              float row_h = 18.0f) const;

private:
    double ring_[kCapacity] = {};
    int write_idx_ = 0;
    int count_ = 0;
    double range_min_ = std::numeric_limits<double>::quiet_NaN();
    double range_max_ = std::numeric_limits<double>::quiet_NaN();
};

} // namespace jalert::ui
