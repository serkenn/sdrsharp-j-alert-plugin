#pragma once
// Post-Viterbi bit pipeline: decoded info bits → double-differential + invert →
// IESS-308 descramble → HDLC deframe → NowcastPacket → chunk reassembly →
// inflated alert. Reused by both the offline harness and the live SDR++ module.

#include <cstdint>
#include <functional>
#include <vector>

#include "decode/alert.h"
#include "frame/chunk_reassembler.h"
#include "frame/descrambler.h"
#include "frame/differential.h"
#include "frame/hdlc.h"
#include "frame/nowcast_packet.h"

namespace jalert::decode {

class BitPipeline {
public:
    using AlertSink = std::function<void(const DecodedAlert&)>;

    explicit BitPipeline(AlertSink sink)
        : sink_(std::move(sink)),
          hdlc_([this](const uint8_t* p, size_t n) { on_frame(p, n); }) {}

    void reset() {
        diff_.reset();
        descr_.reset();
        hdlc_.reset();
        reasm_.reset();
        packets_ = status_packets_ = files_ = alerts_ = 0;
    }

    void push_bit(uint8_t b) {
        hdlc_.push(descr_.push(diff_.push(b)));
    }

    long long frames_ok() const { return hdlc_.frames_ok(); }
    long long frames_bad() const { return hdlc_.frames_bad(); }
    long long packets() const { return packets_; }
    long long status_packets() const { return status_packets_; }
    long long files() const { return files_; }
    long long alerts() const { return alerts_; }

private:
    void on_frame(const uint8_t* payload, size_t len) {
        frame::NowcastPacket pkt;
        if (!parse_nowcast_packet(payload, len, pkt)) return;
        ++packets_;
        if (pkt.is_status()) { ++status_packets_; return; }

        std::vector<frame::AssembledFile> done;
        for (const auto& c : pkt.chunks) reasm_.add(c, pkt.timestamp, done);
        for (const auto& f : done) {
            ++files_;
            DecodedAlert a = decode_alert(f);
            if (a.ok) ++alerts_;
            if (sink_) sink_(a);
        }
    }

    AlertSink sink_;
    frame::DifferentialDecoder diff_;
    frame::Descrambler descr_;
    frame::HdlcDeframer hdlc_;
    frame::ChunkReassembler reasm_;
    long long packets_ = 0;
    long long status_packets_ = 0;
    long long files_ = 0;
    long long alerts_ = 0;
};

} // namespace jalert::decode
