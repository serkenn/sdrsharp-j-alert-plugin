#include "sink/alert_json.h"

#include <cstdio>

namespace jalert::sink {

namespace {
// JSON string escape. UTF-8 bytes pass through verbatim (valid in JSON); only
// the structural characters and C0 controls are escaped.
void append_escaped(std::string& out, const std::string& s) {
    out.push_back('"');
    for (unsigned char c : s) {
        switch (c) {
            case '"':  out += "\\\""; break;
            case '\\': out += "\\\\"; break;
            case '\n': out += "\\n";  break;
            case '\r': out += "\\r";  break;
            case '\t': out += "\\t";  break;
            default:
                if (c < 0x20) {
                    char buf[8];
                    std::snprintf(buf, sizeof(buf), "\\u%04x", c);
                    out += buf;
                } else {
                    out.push_back(static_cast<char>(c));
                }
        }
    }
    out.push_back('"');
}

void append_field(std::string& out, const char* key, const std::string& val,
                  bool& first) {
    if (!first) out.push_back(',');
    first = false;
    out.push_back('"');
    out += key;
    out += "\":";
    append_escaped(out, val);
}

// Standard base64 (RFC 4648) of binary data, wrapped in JSON quotes.
void append_base64(std::string& out, const uint8_t* d, size_t n) {
    static const char* B =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
    out.push_back('"');
    size_t i = 0;
    for (; i + 3 <= n; i += 3) {
        const uint32_t v = (uint32_t(d[i]) << 16) | (uint32_t(d[i + 1]) << 8) | d[i + 2];
        out.push_back(B[(v >> 18) & 63]); out.push_back(B[(v >> 12) & 63]);
        out.push_back(B[(v >> 6) & 63]);  out.push_back(B[v & 63]);
    }
    if (n - i == 1) {
        const uint32_t v = uint32_t(d[i]) << 16;
        out.push_back(B[(v >> 18) & 63]); out.push_back(B[(v >> 12) & 63]);
        out += "==";
    } else if (n - i == 2) {
        const uint32_t v = (uint32_t(d[i]) << 16) | (uint32_t(d[i + 1]) << 8);
        out.push_back(B[(v >> 18) & 63]); out.push_back(B[(v >> 12) & 63]);
        out.push_back(B[(v >> 6) & 63]);  out.push_back('=');
    }
    out.push_back('"');
}
} // namespace

std::string serialize_alert(const decode::DecodedAlert& a, long long timestamp_ms) {
    std::string out;
    out.reserve(a.xml.size() + a.data.size() * 2 + 512);
    out.push_back('{');
    bool first = true;

    char num[32];
    std::snprintf(num, sizeof(num), "%lld", timestamp_ms);
    out += "\"rx_time_ms\":";
    out += num;
    first = false;

    out += ",\"decoded\":";
    out += (a.ok ? "true" : "false");

    std::snprintf(num, sizeof(num), "%u", static_cast<unsigned>(a.flags));
    out += ",\"flags\":";
    out += num;

    append_field(out, "chunk_type", a.chunk_type, first);
    append_field(out, "packet_time", a.timestamp, first);
    if (!a.id.empty())              append_field(out, "id", a.id, first);
    if (!a.filename.empty())        append_field(out, "filename", a.filename, first);
    if (!a.control_title.empty())   append_field(out, "title", a.control_title, first);
    if (!a.head_title.empty())      append_field(out, "head_title", a.head_title, first);
    if (!a.info_type.empty())       append_field(out, "info_type", a.info_type, first);
    if (!a.report_datetime.empty()) append_field(out, "report_time", a.report_datetime, first);
    if (!a.headline_text.empty())   append_field(out, "headline", a.headline_text, first);

    if (a.ok) {
        // Gzipped JMA telegram: inflated XML.
        std::snprintf(num, sizeof(num), "%zu", a.xml.size());
        out += ",\"xml_bytes\":";
        out += num;
        if (!a.xml.empty()) {
            out += ",\"xml\":";
            append_escaped(out, std::string(reinterpret_cast<const char*>(a.xml.data()),
                                            a.xml.size()));
        }
    } else {
        // Non-XML telegram (non-gzipped / non-JMA chunk): expose the raw
        // recovered file so the payload is still retrievable.
        std::snprintf(num, sizeof(num), "%zu", a.data.size());
        out += ",\"data_bytes\":";
        out += num;
        if (!a.data.empty()) {
            out += ",\"data_b64\":";
            append_base64(out, a.data.data(), a.data.size());
        }
    }
    out.push_back('}');
    return out;
}

} // namespace jalert::sink
