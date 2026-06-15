#include "decode/alert.h"

#include <cctype>
#include <cstddef>
#include <cstring>

#include "decode/gunzip.h"

namespace jalert::decode {

namespace {
// Locate the gzip member inside an assembled JMA file. The body is a JMA Socket
// Packet:
//   len[8 ASCII] type[2]("BI", case-insensitive) BCH(bch_len) AN(an_len) gzip…
//   bch_len = (data[10] & 0x0f) * 4,  an_len = data[19],  gzip @ 10+bch_len+an_len
// Falls back to scanning for the gzip magic if the header doesn't fit.
int find_gzip_offset(const std::vector<uint8_t>& d) {
    if (d.size() >= 20 &&
        (d[8] == 'B' || d[8] == 'b') && (d[9] == 'I' || d[9] == 'i')) {
        const int bch_len = (d[10] & 0x0f) * 4;
        const int an_len = d[19];
        const size_t off = static_cast<size_t>(10 + bch_len + an_len);
        if (off + 3 <= d.size() && d[off] == 0x1f && d[off + 1] == 0x8b &&
            d[off + 2] == 0x08) {
            return static_cast<int>(off);
        }
    }
    for (size_t i = 0; i + 3 <= d.size(); ++i) {
        if (d[i] == 0x1f && d[i + 1] == 0x8b && d[i + 2] == 0x08) {
            return static_cast<int>(i);
        }
    }
    return -1;
}

std::string find_tag_from(const std::string& xml, const std::string& tag, size_t& from) {
    const std::string open = "<" + tag;
    const std::string close = "</" + tag + ">";
    size_t p = xml.find(open, from);
    while (p != std::string::npos) {
        const size_t after = p + open.size();
        if (after < xml.size() &&
            (xml[after] == '>' || xml[after] == ' ' || xml[after] == '/' ||
             xml[after] == '\t' || xml[after] == '\n' || xml[after] == '\r')) {
            const size_t gt = xml.find('>', after);
            if (gt == std::string::npos) break;
            if (xml[gt - 1] == '/') { from = gt + 1; return {}; }
            const size_t c = xml.find(close, gt + 1);
            if (c == std::string::npos) break;
            from = c + close.size();
            return xml.substr(gt + 1, c - (gt + 1));
        }
        p = xml.find(open, after);
    }
    from = std::string::npos;
    return {};
}

std::string trim(const std::string& s) {
    size_t a = 0, b = s.size();
    while (a < b && (s[a] == ' ' || s[a] == '\n' || s[a] == '\r' || s[a] == '\t')) ++a;
    while (b > a && (s[b-1] == ' ' || s[b-1] == '\n' || s[b-1] == '\r' || s[b-1] == '\t')) --b;
    return s.substr(a, b - a);
}
} // namespace

std::string xml_first_tag(const std::string& xml, const std::string& tag) {
    size_t from = 0;
    return trim(find_tag_from(xml, tag, from));
}

DecodedAlert decode_alert(const frame::AssembledFile& f) {
    DecodedAlert a;
    a.chunk_type = f.type;
    a.timestamp = f.timestamp;
    a.id = f.id;
    a.filename = f.filename;
    a.flags = f.flags;

    a.gzip_offset = find_gzip_offset(f.data);
    std::vector<uint8_t> xml;
    if (a.gzip_offset >= 0 &&
        gunzip(f.data.data() + a.gzip_offset,
               f.data.size() - static_cast<size_t>(a.gzip_offset), xml)) {
        a.ok = true;
        a.xml = std::move(xml);
    } else {
        // Not a gzipped JMA telegram — keep the raw recovered file so the
        // payload is still reported (e.g. via JSONL data_b64).
        a.data = f.data;
        return a;
    }

    const std::string s(reinterpret_cast<const char*>(a.xml.data()), a.xml.size());
    size_t from = 0;
    a.control_title = trim(find_tag_from(s, "Title", from));   // first = Control
    a.head_title = trim(find_tag_from(s, "Title", from));      // second = Head
    a.info_type = xml_first_tag(s, "InfoType");
    a.report_datetime = xml_first_tag(s, "ReportDateTime");
    a.headline_text = xml_first_tag(s, "Text");
    return a;
}

} // namespace jalert::decode
