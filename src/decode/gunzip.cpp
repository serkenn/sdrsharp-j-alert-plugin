#include "gunzip.h"

#include <zlib.h>

namespace jalert::decode {

bool gunzip(const uint8_t* data, size_t len, std::vector<uint8_t>& out) {
    out.clear();
    if (!data || len == 0) return false;

    z_stream zs{};
    // windowBits = 15 (max) + 16 → expect a gzip header/trailer.
    if (inflateInit2(&zs, 15 + 16) != Z_OK) return false;

    zs.next_in = const_cast<Bytef*>(reinterpret_cast<const Bytef*>(data));
    zs.avail_in = static_cast<uInt>(len);

    unsigned char buf[16384];
    int ret = Z_OK;
    do {
        zs.next_out = buf;
        zs.avail_out = sizeof(buf);
        ret = inflate(&zs, Z_NO_FLUSH);
        if (ret != Z_OK && ret != Z_STREAM_END && ret != Z_BUF_ERROR) {
            inflateEnd(&zs);
            out.clear();
            return false;
        }
        const size_t produced = sizeof(buf) - zs.avail_out;
        out.insert(out.end(), buf, buf + produced);
        if (ret == Z_BUF_ERROR && produced == 0) break;  // no progress
    } while (ret != Z_STREAM_END);

    inflateEnd(&zs);
    return ret == Z_STREAM_END && !out.empty();
}

} // namespace jalert::decode
