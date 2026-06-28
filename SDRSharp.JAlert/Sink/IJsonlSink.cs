// JSONL sink base interface. Implementations must be thread-safe w.r.t.
// WriteLine / Stop and must not block the decoder thread; a slow consumer should
// drop rather than stall. Ported from src/sink/sink.h.

namespace SDRSharp.JAlert.Sink
{
    public enum SinkPhase { Off, Active, Error }

    public interface IJsonlSink
    {
        string Status { get; }
        // Push the next JSONL line (no trailing newline; the sink appends it).
        void WriteLine(string line);
        void Stop();
    }
}
