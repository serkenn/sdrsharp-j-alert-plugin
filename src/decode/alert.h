#pragma once
// Decoded J-ALERT message: an assembled NowcastPacket file whose JMA Socket
// Packet body has been inflated to JMA alert XML, plus a few headline fields
// lifted out for display / logging.

#include <cstdint>
#include <string>
#include <vector>

#include "frame/chunk_reassembler.h"

namespace jalert::decode {

struct DecodedAlert {
    bool ok = false;                 // gzip inflated to XML
    std::string chunk_type;          // "wrmx"/"eprx"/"issx"/"ioeq"/…
    std::string timestamp;           // 17-digit packet timestamp
    std::string id;                  // 4-char file id
    std::string filename;            // metadata filename
    uint16_t flags = 0;              // 3 = gzipped
    int gzip_offset = -1;            // gzip offset within the assembled data

    std::vector<uint8_t> xml;        // inflated XML bytes (when ok)
    std::vector<uint8_t> data;       // raw recovered file bytes, kept only when
                                     // NOT inflated to XML (non-gzipped /
                                     // non-JMA chunks) so the payload is still
                                     // accessible downstream

    // Lightweight (non-validating) extracts from the JMA XML, UTF-8:
    std::string control_title;       // <Control><Title>
    std::string head_title;          // <Head><Title>
    std::string info_type;           // <Head><InfoType> 発表/更新/取消
    std::string report_datetime;     // <Head><ReportDateTime>
    std::string headline_text;       // <Head><Headline><Text>

    // JMA telegram chunk types.
    bool is_jma() const {
        return chunk_type == "wrmx" || chunk_type == "eprx" ||
               chunk_type == "issx" || chunk_type == "ioeq";
    }
};

// Inflate + parse an assembled file into a DecodedAlert. Always copies the
// metadata; sets ok + xml + field extracts only when the gzip member inflates.
DecodedAlert decode_alert(const frame::AssembledFile& f);

// Extract the text of the first <tag>…</tag> occurrence (namespace-insensitive,
// non-validating). Exposed for testing.
std::string xml_first_tag(const std::string& xml, const std::string& tag);

} // namespace jalert::decode
