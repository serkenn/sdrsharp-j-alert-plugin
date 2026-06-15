#pragma once
// JSONL sink base interface. Implementations must be thread-safe w.r.t.
// write_line / stop and must not block the decoder thread; a slow consumer
// should drop rather than stall.

#include <string>

namespace jalert::sink {

enum class SinkPhase { Off, Active, Error };

const char* to_string(SinkPhase p);

class IJsonlSink {
public:
    virtual ~IJsonlSink() = default;
    virtual std::string status() const = 0;
    // Push the next JSONL line (no trailing newline; the sink appends it).
    virtual void write_line(const std::string& line) = 0;
    virtual void stop() = 0;
};

} // namespace jalert::sink
