// Append-mode JSONL file sink. Each line is written and flushed before WriteLine
// returns. No fsync per line (a slow disk must not stall the decoder). Ported
// from src/sink/file_sink.{h,cpp}.

using System;
using System.IO;
using System.Text;

namespace SDRSharp.JAlert.Sink
{
    public struct FileSinkSnapshot
    {
        public SinkPhase Phase;
        public string Path;
        public long BytesWritten;
        public long RecordsWritten;
        public string ErrorMessage;
    }

    public sealed class FileJsonlSink : IJsonlSink
    {
        private readonly object _gate = new object();
        private readonly string _path;
        private FileStream _fp;
        private readonly byte[] _newline = { (byte)'\n' };
        private string _status = "";
        private string _error = "";
        private long _bytesWritten;
        private long _recordsWritten;

        public FileJsonlSink(string path)
        {
            _path = path;
            try
            {
                string dir = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            }
            catch
            {
                // best-effort directory create — fall through to open
            }

            try
            {
                _fp = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read, 1 << 16);
                _status = "writing → " + System.IO.Path.GetFileName(path);
            }
            catch (Exception ex)
            {
                _status = "error: " + ex.Message;
                _error = ex.Message;
            }
        }

        public string Status
        {
            get { lock (_gate) return _status; }
        }

        public void WriteLine(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            lock (_gate)
            {
                if (_fp == null) return;
                try
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(line);
                    _fp.Write(bytes, 0, bytes.Length);
                    _fp.Write(_newline, 0, 1);
                    _fp.Flush();
                    _bytesWritten += bytes.Length + 1;
                    ++_recordsWritten;
                }
                catch (Exception ex)
                {
                    _error = ex.Message;
                    _status = "error: " + _error;
                    try { _fp.Dispose(); } catch { }
                    _fp = null;
                }
            }
        }

        public void Stop()
        {
            lock (_gate)
            {
                if (_fp != null)
                {
                    try { _fp.Flush(); _fp.Dispose(); } catch { }
                    _fp = null;
                }
                _status = "off";
            }
        }

        public FileSinkSnapshot Snapshot()
        {
            lock (_gate)
            {
                FileSinkSnapshot s = new FileSinkSnapshot
                {
                    Path = _path,
                    BytesWritten = _bytesWritten,
                    RecordsWritten = _recordsWritten,
                    ErrorMessage = _error,
                };
                if (!string.IsNullOrEmpty(_error)) s.Phase = SinkPhase.Error;
                else if (_fp != null) s.Phase = SinkPhase.Active;
                else s.Phase = SinkPhase.Off;
                return s;
            }
        }
    }
}
