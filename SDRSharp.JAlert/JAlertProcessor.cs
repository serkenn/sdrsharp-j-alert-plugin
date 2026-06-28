// SDR# IQ stream hook: taps the DecimatedAndFilteredIQ stream, frequency-shifts
// the tuned carrier (Frequency − CenterFrequency) to baseband with an NCO, and
// feeds it to the J-ALERT Receiver. The Receiver/BpskDemod resamples internally
// to the 1.024 MHz work rate, so no separate channelizer is needed.
//
// This is the SDR# analogue of the SDR++ module's iq_handler + on_alert in the
// original main.cpp.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SDRSharp.Common;
using SDRSharp.JAlert.Decode;
using SDRSharp.JAlert.Dsp;
using SDRSharp.JAlert.Sink;
using SDRSharp.Radio;

namespace SDRSharp.JAlert
{
    public sealed unsafe class JAlertProcessor : IIQProcessor
    {
        private const int RecentAlerts = 32;

        private readonly ISharpControl _control;
        private readonly JAlertSettings _settings;
        private readonly object _uiMtx = new object();
        private readonly LinkedList<AlertSummary> _recent = new LinkedList<AlertSummary>();

        private bool _enabled = true;
        private double _sampleRate;

        private Receiver _receiver;
        private double _receiverRate;

        // NCO state (incremental complex rotator), shifting the tuned carrier to
        // baseband. cur = e^{-jθ}; multiplied by per-sample rotator m = e^{-jω}.
        private double _curRe = 1.0, _curIm = 0.0;
        private double _mRe = 1.0, _mIm = 0.0;
        private double _ncoOffsetHz = double.NaN;
        private int _ncoRenormCounter;

        private FileJsonlSink _fileSink;
        private TcpJsonlSink _tcpSink;

        public JAlertProcessor(ISharpControl control, JAlertSettings settings)
        {
            _control = control;
            _settings = settings;

            if (settings.JsonlFileEnabled && !string.IsNullOrEmpty(settings.JsonlFilePath))
                _fileSink = new FileJsonlSink(settings.JsonlFilePath);
            if (settings.JsonlTcpEnabled && settings.JsonlTcpPort > 0)
                _tcpSink = new TcpJsonlSink(settings.JsonlTcpPort);
        }

        // ── IBaseProcessor / IStreamProcessor ──
        public bool Enabled
        {
            get => _enabled;
            set => _enabled = value;
        }

        public double SampleRate
        {
            set
            {
                _sampleRate = value;
                // Receiver is rebuilt lazily in Process when the rate changes.
            }
        }

        // ── IIQProcessor ──
        public void Process(Complex* buffer, int length)
        {
            if (!_enabled) return;

            double fs = _sampleRate;
            if (fs <= 0.0) return;

            if (_receiver == null || _receiverRate != fs)
            {
                _receiver = new Receiver(fs, OnAlert);
                _receiverRate = fs;
                _curRe = 1.0; _curIm = 0.0;
                _ncoOffsetHz = double.NaN;
            }

            UpdateNco(fs);

            for (int i = 0; i < length; ++i)
            {
                float inRe = buffer[i].Real;
                float inIm = buffer[i].Imag;

                // out = in · cur  (complex multiply by the unit rotator).
                double zr = inRe * _curRe - inIm * _curIm;
                double zi = inRe * _curIm + inIm * _curRe;

                // Advance the rotator: cur *= m.
                double nr = _curRe * _mRe - _curIm * _mIm;
                double ni = _curRe * _mIm + _curIm * _mRe;
                _curRe = nr; _curIm = ni;
                if (++_ncoRenormCounter >= 1024)
                {
                    _ncoRenormCounter = 0;
                    double inv = 1.0 / Math.Sqrt(_curRe * _curRe + _curIm * _curIm);
                    _curRe *= inv; _curIm *= inv;
                }

                _receiver.Process(new Complex32((float)zr, (float)zi));
            }
        }

        private void UpdateNco(double fs)
        {
            // DecimatedAndFilteredIQ is centered on CenterFrequency; the tuned VFO
            // sits at Frequency. Shift that offset down to baseband.
            double offset = (double)_control.Frequency - _control.CenterFrequency;
            if (offset == _ncoOffsetHz) return;
            _ncoOffsetHz = offset;
            double omega = -2.0 * Math.PI * offset / fs;   // e^{-jω} per sample
            _mRe = Math.Cos(omega);
            _mIm = Math.Sin(omega);
        }

        public Receiver CurrentReceiver => _receiver;

        // Called from the DSP thread when a NowcastPacket is decoded.
        private void OnAlert(DecodedAlert a)
        {
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            // Fan out to JSONL sinks.
            FileJsonlSink fs = _fileSink;
            TcpJsonlSink ts = _tcpSink;
            if (fs != null || ts != null)
            {
                string line = AlertJson.Serialize(a, nowMs);
                fs?.WriteLine(line);
                ts?.WriteLine(line);
            }

            // Auto-save every recovered file to the configured folder: decoded JMA
            // telegrams as <timestamp>.xml, other (non-XML) telegrams as the raw
            // recovered bytes in <timestamp>.<chunk_type>.bin.
            if (!string.IsNullOrEmpty(a.Timestamp))
            {
                string dir;
                bool fileOut;
                lock (_uiMtx) { dir = _settings.XmlOutputDir; fileOut = _settings.FileOutputEnabled; }
                if (fileOut && !string.IsNullOrEmpty(dir))
                {
                    try
                    {
                        Directory.CreateDirectory(dir);
                        string path = null;
                        byte[] bytes = null;
                        if (a.Ok)
                        {
                            path = Path.Combine(dir, a.Timestamp + ".xml");
                            bytes = a.Xml;
                        }
                        else if (a.Data.Length > 0)
                        {
                            path = Path.Combine(dir, a.Timestamp + "." + SafeName(a.ChunkType) + ".bin");
                            bytes = a.Data;
                        }
                        if (bytes != null) File.WriteAllBytes(path, bytes);
                    }
                    catch
                    {
                        // Best-effort file output; ignore write failures.
                    }
                }
            }

            AlertSummary s = new AlertSummary
            {
                RxTimeMs = nowMs,
                Decoded = a.Ok,
                ChunkType = a.ChunkType,
                PacketTime = a.Timestamp,
                ControlTitle = a.ControlTitle,
                HeadTitle = a.HeadTitle,
                InfoType = a.InfoType,
                ReportTime = a.ReportDateTime,
                Headline = a.HeadlineText,
                XmlBytes = a.Xml.Length,
            };
            lock (_uiMtx)
            {
                _recent.AddFirst(s);
                while (_recent.Count > RecentAlerts) _recent.RemoveLast();
            }
        }

        // Snapshot of recent summaries (newest first) for the panel.
        public List<AlertSummary> SnapshotRecent()
        {
            lock (_uiMtx) return new List<AlertSummary>(_recent);
        }

        // Filename-safe component from a chunk-type code (keep [a-z0-9], else "dat").
        private static string SafeName(string s)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in s)
            {
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) sb.Append(c);
                else if (c >= 'A' && c <= 'Z') sb.Append((char)(c - 'A' + 'a'));
            }
            return sb.Length == 0 ? "dat" : sb.ToString();
        }

        // ── Output sink controls (driven by the panel) ──
        public FileJsonlSink FileSink => _fileSink;
        public TcpJsonlSink TcpSink => _tcpSink;

        public void StartFileSink(string path)
        {
            StopFileSink();
            _fileSink = new FileJsonlSink(path);
        }

        public void StopFileSink()
        {
            FileJsonlSink fs = _fileSink;
            _fileSink = null;
            fs?.Stop();
        }

        public void StartTcpSink(int port)
        {
            StopTcpSink();
            _tcpSink = new TcpJsonlSink(port);
        }

        public void StopTcpSink()
        {
            TcpJsonlSink ts = _tcpSink;
            _tcpSink = null;
            ts?.Stop();
        }

        public void Close()
        {
            StopFileSink();
            StopTcpSink();
        }
    }
}
