#pragma once
// Reassembles a file that the NowcastPacket layer split across several chunks
// (and possibly several packets), keyed by the 4-char chunk id, ordered by seq.
// The common case is a single chunk (seq==1, num_chunks==1), completed
// immediately.

#include <cstdint>
#include <map>
#include <string>
#include <unordered_map>
#include <vector>

#include "frame/nowcast_packet.h"

namespace jalert::frame {

struct AssembledFile {
    std::string type;             // chunk-type-code (e.g. "wrmx", "eprx")
    std::string id;
    std::string timestamp;        // packet timestamp of the seq==1 chunk
    std::string filename;
    int64_t data_size = 0;
    int64_t original_size = 0;
    uint16_t flags = 0;
    std::vector<uint8_t> data;    // concatenated payloads in seq order
    bool gzipped() const { return flags == 3; }
};

class ChunkReassembler {
public:
    // Append one chunk; appends any file(s) it completes to out (usually 0/1).
    void add(const NowcastChunk& c, const std::string& ts,
             std::vector<AssembledFile>& out) {
        if (c.seq == 1 && c.num_chunks == 1) {       // fast path: whole file
            out.push_back(make_single(c, ts));
            return;
        }
        if (c.seq == 0 || c.num_chunks == 0) return;  // malformed

        Partial& p = pending_[c.id];
        if (p.num == 0) p.num = c.num_chunks;
        if (c.seq == 1) {
            p.have_meta = true;
            p.type = c.type;
            p.filename = c.filename;
            p.data_size = c.data_size;
            p.original_size = c.original_size;
            p.flags = c.flags;
            p.timestamp = ts;
        }
        if (p.type.empty()) p.type = c.type;
        p.parts[c.seq] = c.payload;

        if (p.parts.size() >= p.num) {
            AssembledFile f;
            f.type = p.type;
            f.id = c.id;
            f.timestamp = p.timestamp.empty() ? ts : p.timestamp;
            f.filename = p.filename;
            f.data_size = p.data_size;
            f.original_size = p.original_size;
            f.flags = p.flags;
            for (auto& kv : p.parts)
                f.data.insert(f.data.end(), kv.second.begin(), kv.second.end());
            out.push_back(std::move(f));
            pending_.erase(c.id);
        } else if (pending_.size() > kMaxPending) {
            pending_.erase(pending_.begin());  // bound memory; drop a stale partial
        }
    }

    void reset() { pending_.clear(); }

private:
    struct Partial {
        uint32_t num = 0;
        bool have_meta = false;
        std::string type, filename, timestamp;
        int64_t data_size = 0, original_size = 0;
        uint16_t flags = 0;
        std::map<uint32_t, std::vector<uint8_t>> parts;  // seq -> payload
    };

    static AssembledFile make_single(const NowcastChunk& c, const std::string& ts) {
        AssembledFile f;
        f.type = c.type;
        f.id = c.id;
        f.timestamp = ts;
        f.filename = c.filename;
        f.data_size = c.data_size;
        f.original_size = c.original_size;
        f.flags = c.flags;
        f.data = c.payload;
        return f;
    }

    static constexpr size_t kMaxPending = 64;
    std::unordered_map<std::string, Partial> pending_;
};

} // namespace jalert::frame
