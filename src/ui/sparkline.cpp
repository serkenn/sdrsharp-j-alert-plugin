#include "ui/sparkline.h"

#include <algorithm>

namespace jalert::ui {

void Sparkline::draw(const char* caption,
                     const std::string& current_text,
                     ImU32 bar_color,
                     float caption_w,
                     float row_h) const {
    const float avail = ImGui::GetContentRegionAvail().x;
    if (avail <= caption_w) {
        ImGui::TextUnformatted(caption);
        ImGui::SameLine();
        ImGui::TextUnformatted(current_text.c_str());
        return;
    }

    const ImVec2 cur = ImGui::GetCursorScreenPos();
    ImDrawList* dl = ImGui::GetWindowDrawList();
    const ImU32 caption_col = ImGui::GetColorU32(ImGuiCol_TextDisabled);

    dl->AddText(cur, caption_col, caption);

    const ImVec2 val_sz = ImGui::CalcTextSize(current_text.c_str());
    float val_left = std::max(cur.x + caption_w + 8.0f, cur.x + avail - val_sz.x);
    dl->AddText(ImVec2(val_left, cur.y), bar_color, current_text.c_str());

    const float bar_left  = cur.x + caption_w;
    const float bar_right = val_left - 4.0f;
    const float bar_w     = bar_right - bar_left;
    if (bar_w > 0.0f && count_ > 0) {
        double rmin = range_min_;
        double rmax = range_max_;
        const bool auto_min = std::isnan(rmin);
        const bool auto_max = std::isnan(rmax);
        if (auto_min || auto_max) {
            double mn = std::numeric_limits<double>::infinity();
            double mx = -std::numeric_limits<double>::infinity();
            for (int i = 0; i < count_; ++i) {
                const double v = ring_[i];
                if (v < mn) mn = v;
                if (v > mx) mx = v;
            }
            if (auto_min) rmin = mn;
            if (auto_max) rmax = mx;
        }
        if (rmax - rmin < 1e-9) rmax = rmin + 1.0;

        const float mid_y  = cur.y + row_h - 2.0f;
        const float bar_h  = row_h - 3.0f;

        ImU32 track = (caption_col & 0x00FFFFFFu) | (0x60u << 24);
        dl->AddLine(ImVec2(bar_left, mid_y), ImVec2(bar_right, mid_y), track, 1.0f);

        const int samples = count_;
        const int start = count_ < kCapacity ? 0 : write_idx_;
        const ImU32 bar_col = (bar_color & 0x00FFFFFFu) | (0xC8u << 24);

        const int pixels = static_cast<int>(bar_w);
        for (int x = 0; x < pixels; ++x) {
            const int i = static_cast<int>(
                static_cast<long long>(x) * samples / pixels);
            const int idx = (start + i) % kCapacity;
            const double v = ring_[idx];
            double t = (v - rmin) / (rmax - rmin);
            if (t < 0.0) t = 0.0;
            else if (t > 1.0) t = 1.0;
            const float h = std::max(1.0f, static_cast<float>(t * bar_h));
            dl->AddLine(ImVec2(bar_left + static_cast<float>(x), mid_y),
                        ImVec2(bar_left + static_cast<float>(x), mid_y - h),
                        bar_col, 1.0f);
        }
    }

    ImGui::Dummy(ImVec2(avail, row_h));
}

} // namespace jalert::ui
