#pragma once
// Thin zlib wrapper for the single gzip member embedded in a NowcastPacket.
// Inflates until Z_STREAM_END, so trailing NowcastPacket padding after the gzip
// stream is ignored.

#include <cstddef>
#include <cstdint>
#include <vector>

namespace jalert::decode {

// Inflate a gzip stream. Returns true and fills out on success; false on any
// zlib error or truncated input.
bool gunzip(const uint8_t* data, size_t len, std::vector<uint8_t>& out);

} // namespace jalert::decode
