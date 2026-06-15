#pragma once
// Serialize a DecodedAlert to a single JSONL line for the file / TCP sinks.

#include <string>

#include "decode/alert.h"

namespace jalert::sink {

// One JSON object, no trailing newline. Includes packet metadata, the lifted
// JMA headline fields, and the inline payload: the full inflated alert XML for
// gzipped JMA telegrams, or base64 of the raw recovered file (data_b64) for
// non-XML telegrams.
std::string serialize_alert(const decode::DecodedAlert& a, long long timestamp_ms);

} // namespace jalert::sink
