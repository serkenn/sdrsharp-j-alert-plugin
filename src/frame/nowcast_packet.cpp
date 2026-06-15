#include "frame/nowcast_packet.h"

#include <cctype>
#include <cstring>

#include <zlib.h>

namespace jalert::frame {

namespace {
constexpr int kBaseHeader = 28;      // magic(4)+hdrlen(2)+SP(1)+ts(17)+crc(4)
constexpr int kEntrySize = 6;        // type(4)+chunk_len(2)
constexpr int kChunkHeader = 12;     // seq(4)+num_chunks(4)+id(4)
constexpr int kMetadata = 256 + 8 + 8 + 2;  // filename+data_size+original_size+flags
constexpr int kTsOffset = 7;
constexpr int kTsLen = 17;

uint16_t rd_u16(const uint8_t* p) { return static_cast<uint16_t>(p[0] | (p[1] << 8)); }
uint32_t rd_u32(const uint8_t* p) {
    return static_cast<uint32_t>(p[0]) | (static_cast<uint32_t>(p[1]) << 8) |
           (static_cast<uint32_t>(p[2]) << 16) | (static_cast<uint32_t>(p[3]) << 24);
}
int64_t rd_i64(const uint8_t* p) {
    uint64_t v = 0;
    for (int i = 0; i < 8; ++i) v |= static_cast<uint64_t>(p[i]) << (8 * i);
    return static_cast<int64_t>(v);
}

std::string lower4(const uint8_t* p) {
    std::string s(reinterpret_cast<const char*>(p), 4);
    for (char& c : s) c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));
    return s;
}

bool parse_chunk(const uint8_t* d, size_t len, NowcastChunk& c) {
    if (len < static_cast<size_t>(kEntrySize + 4 + kChunkHeader)) return false;
    c.type = lower4(d);
    const uint16_t chunk_len = rd_u16(d + 4);
    if (chunk_len < kEntrySize + 4 || chunk_len > len) return false;
    c.body_crc32 = rd_u32(d + 6);

    const uint8_t* body = d + 10;
    const size_t body_len = static_cast<size_t>(chunk_len) - 10;
    c.crc_ok = (crc32(crc32(0L, Z_NULL, 0), body, static_cast<uInt>(body_len)) ==
                c.body_crc32);

    c.seq = rd_u32(body);
    c.num_chunks = rd_u32(body + 4);
    c.id.assign(reinterpret_cast<const char*>(body + 8), 4);

    size_t off = kChunkHeader;
    if (c.seq == 1 && body_len >= static_cast<size_t>(kChunkHeader + kMetadata)) {
        const uint8_t* m = body + off;
        const size_t fn_len = strnlen(reinterpret_cast<const char*>(m), 256);
        c.filename.assign(reinterpret_cast<const char*>(m), fn_len);
        c.data_size = rd_i64(m + 256);
        c.original_size = rd_i64(m + 256 + 8);
        c.flags = rd_u16(m + 256 + 16);
        c.has_metadata = true;
        off += kMetadata;
    }
    c.payload.assign(body + off, body + body_len);
    return true;
}
} // namespace

bool parse_nowcast_packet(const uint8_t* data, size_t len, NowcastPacket& out) {
    out = NowcastPacket{};
    if (len < static_cast<size_t>(kBaseHeader)) return false;
    if (std::memcmp(data, "NWCS", 4) != 0) return false;

    const uint16_t hdr_len = rd_u16(data + 4);
    if (hdr_len < kBaseHeader || hdr_len > len) return false;
    if ((hdr_len - kBaseHeader) % kEntrySize != 0) return false;
    const int n = (hdr_len - kBaseHeader) / kEntrySize;

    bool ts_ok = true;
    for (int i = 0; i < kTsLen; ++i)
        if (!std::isdigit(data[kTsOffset + i])) { ts_ok = false; break; }
    if (ts_ok)
        out.timestamp.assign(reinterpret_cast<const char*>(data + kTsOffset), kTsLen);

    out.hdr_crc32 = rd_u32(data + 24);

    // Chunk bodies follow the header; each entry's chunk_len gives the body span.
    size_t body_off = hdr_len;
    out.chunks.reserve(static_cast<size_t>(n));
    for (int i = 0; i < n; ++i) {
        const int e = kBaseHeader + i * kEntrySize;
        const uint16_t chunk_len = rd_u16(data + e + 4);
        if (chunk_len < kEntrySize + 4 || body_off + chunk_len > len) break;
        NowcastChunk c;
        if (parse_chunk(data + body_off, chunk_len, c)) out.chunks.push_back(std::move(c));
        body_off += chunk_len;
    }

    out.valid = true;
    return true;
}

} // namespace jalert::frame
