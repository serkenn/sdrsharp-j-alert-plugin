#include "hdlc.h"

namespace jalert::frame {

namespace {
// CRC-16/X.25: reflected poly 0x1021 (=0x8408), init 0xFFFF, no final xor here
// so a frame INCLUDING its FCS yields the standard good residual 0xF0B8.
constexpr uint16_t kGoodResidual = 0xF0B8;

uint16_t crc_x25_residual(const uint8_t* data, size_t len) {
    uint16_t crc = 0xFFFF;
    for (size_t i = 0; i < len; ++i) {
        crc ^= data[i];
        for (int k = 0; k < 8; ++k) {
            crc = (crc & 1) ? static_cast<uint16_t>((crc >> 1) ^ 0x8408)
                            : static_cast<uint16_t>(crc >> 1);
        }
    }
    return crc;
}
} // namespace

void HdlcDeframer::reset() {
    bytes_.clear();
    cur_byte_ = 0;
    nbits_ = 0;
    ones_ = 0;
    frames_ok_ = 0;
    frames_bad_ = 0;
}

void HdlcDeframer::add_data_bit(uint8_t b) {
    // LSB-first byte packing.
    cur_byte_ |= static_cast<uint8_t>((b & 1u) << nbits_);
    if (++nbits_ == 8) {
        bytes_.push_back(cur_byte_);
        cur_byte_ = 0;
        nbits_ = 0;
    }
}

void HdlcDeframer::reset_frame() {
    bytes_.clear();
    cur_byte_ = 0;
    nbits_ = 0;
}

void HdlcDeframer::close_frame() {
    // A well-formed frame is octet-aligned; trailing partial bits (if any) are
    // dropped.
    if (bytes_.size() >= 4 &&
        crc_x25_residual(bytes_.data(), bytes_.size()) == kGoodResidual) {
        ++frames_ok_;
        if (sink_) sink_(bytes_.data(), bytes_.size() - 2);  // strip FCS
    } else if (!bytes_.empty() || nbits_ != 0) {
        ++frames_bad_;
    }
    reset_frame();
}

void HdlcDeframer::push(uint8_t bit) {
    if (bit & 1u) {
        ++ones_;
        return;
    }
    // A zero terminates the current run of ones.
    if (ones_ < 5) {
        for (int i = 0; i < ones_; ++i) add_data_bit(1);
        add_data_bit(0);
    } else if (ones_ == 5) {
        for (int i = 0; i < 5; ++i) add_data_bit(1);  // drop the stuffed zero
    } else if (ones_ == 6) {
        close_frame();                                // flag (0x7E)
    } else {
        reset_frame();                                // abort / idle (>=7 ones)
    }
    ones_ = 0;
}

} // namespace jalert::frame
