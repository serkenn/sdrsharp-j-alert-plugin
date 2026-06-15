#pragma once
// NowcastPacket parser. All fields little-endian.
//
//   packet      ::= "NWCS"(4) hdr_len(u16) SP(0x20) ts[17] hdr_crc32(u32)
//                   chunk-entry{n}  chunk{n}
//   hdr_len      = 28 + 6*n          (n = number of chunks)
//   chunk-entry ::= type[4] chunk_len(u16)                       (6 bytes)
//   chunk       ::= type[4] chunk_len(u16) body_crc32(u32) body
//   body        ::= seq(u32) num_chunks(u32) id[4]
//                   [ filename[256] data_size(i64) original_size(i64) flags(u16) ]?
//                   payload                       (metadata only when seq == 1)
//
// Notes:
//   * hdr_crc32 is ALWAYS WRONG (protocol-converter bug) — never validated.
//   * body_crc32 = zlib/Ethernet CRC-32 over body — valid, we verify it.
//   * flags == 3 means the carried file is gzipped.
//   * an empty packet (no chunks) is a status / keep-alive.
//   * a single file may span several chunks / packets (seq, num_chunks, id).

#include <cstddef>
#include <cstdint>
#include <string>
#include <vector>

namespace jalert::frame {

struct NowcastChunk {
    std::string type;            // 4-char chunk-type-code, lower-cased (e.g. "wrmx")
    uint32_t body_crc32 = 0;
    bool crc_ok = false;

    uint32_t seq = 0;            // 1-based position within the file
    uint32_t num_chunks = 0;     // total chunks comprising the file
    std::string id;              // 4-char file id (shared across a file's chunks)

    bool has_metadata = false;   // present iff seq == 1
    std::string filename;        // metadata filename (nul-trimmed)
    int64_t data_size = 0;       // joined-data size
    int64_t original_size = 0;   // original file size (often 0 / unset)
    uint16_t flags = 0;          // 3 = gzipped

    std::vector<uint8_t> payload;

    bool gzipped() const { return flags == 3; }
};

struct NowcastPacket {
    bool valid = false;
    std::string timestamp;       // 17-digit ASCII (empty if malformed)
    uint32_t hdr_crc32 = 0;      // present but unreliable (see note)
    std::vector<NowcastChunk> chunks;

    bool is_status() const { return chunks.empty(); }  // keep-alive / idle
};

// Parse one HDLC payload as a NowcastPacket. Returns false (out.valid=false) if
// the magic is absent or the buffer is too short / malformed.
bool parse_nowcast_packet(const uint8_t* data, size_t len, NowcastPacket& out);

} // namespace jalert::frame
