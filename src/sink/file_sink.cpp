#include "sink/file_sink.h"

#include <cerrno>
#include <cstring>
#include <filesystem>

namespace jalert::sink {

const char* to_string(SinkPhase p) {
    switch (p) {
        case SinkPhase::Active: return "Active";
        case SinkPhase::Error:  return "Error";
        case SinkPhase::Off:
        default:                return "Off";
    }
}

FileJsonlSink::FileJsonlSink(const std::string& path) : path_(path) {
    try {
        std::filesystem::path p(path);
        if (p.has_parent_path() && !p.parent_path().empty()) {
            std::error_code ec;
            std::filesystem::create_directories(p.parent_path(), ec);
        }
    } catch (...) {
        // best-effort directory create — fall through to fopen
    }

    fp_ = std::fopen(path.c_str(), "ab");
    if (fp_) {
        std::setvbuf(fp_, nullptr, _IOFBF, 1 << 16);
        status_ = std::string("writing → ") +
                  std::filesystem::path(path).filename().string();
    } else {
        const std::string msg = std::strerror(errno);
        status_ = "error: " + msg;
        error_ = msg;
    }
}

FileJsonlSink::~FileJsonlSink() { stop(); }

std::string FileJsonlSink::status() const {
    std::lock_guard<std::mutex> lk(gate_);
    return status_;
}

void FileJsonlSink::write_line(const std::string& line) {
    if (line.empty()) return;
    std::lock_guard<std::mutex> lk(gate_);
    if (!fp_) return;

    const size_t n = std::fwrite(line.data(), 1, line.size(), fp_);
    const int wrote_nl = std::fputc('\n', fp_);
    const int flush_rc = std::fflush(fp_);
    if (n != line.size() || wrote_nl == EOF || flush_rc != 0) {
        error_ = std::strerror(errno);
        status_ = "error: " + error_;
        std::fclose(fp_);
        fp_ = nullptr;
        return;
    }
    bytes_written_ += static_cast<long long>(line.size() + 1);
    ++records_written_;
}

void FileJsonlSink::stop() {
    std::lock_guard<std::mutex> lk(gate_);
    if (fp_) {
        std::fflush(fp_);
        std::fclose(fp_);
        fp_ = nullptr;
    }
    status_ = "off";
}

FileSinkSnapshot FileJsonlSink::snapshot() const {
    std::lock_guard<std::mutex> lk(gate_);
    FileSinkSnapshot s;
    s.path = path_;
    s.bytes_written = bytes_written_;
    s.records_written = records_written_;
    s.error_message = error_;
    if (!error_.empty())   s.phase = SinkPhase::Error;
    else if (fp_)          s.phase = SinkPhase::Active;
    else                   s.phase = SinkPhase::Off;
    return s;
}

} // namespace jalert::sink
