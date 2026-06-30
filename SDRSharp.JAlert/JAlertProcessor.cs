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

        // AFC (auto-follow LNB drift). Evaluated at most every AfcIntervalSec,
        // only while the demod is locked: nudge the SDR# VFO toward the recovered
        // carrier so the (drifting) signal stays inside the host channel filter.
        // LNB thermal drift is slow, so a relaxed cadence and a deadband keep the
        // tuning from chattering; the per-step clamp guards against a stray
        // estimate yanking the VFO. The correction is the bulk (coarse) offset
        // only — the small Costas residual doesn't affect filter centering.
        private const double AfcIntervalSec = 2.0;
        private const double AfcDeadbandHz = 20_000.0;
        private const double AfcGain = 0.9;
        private const double AfcMaxStepHz = 250_000.0;

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

        private bool _afcEnabled;
        private DateTime _afcLastTick = DateTime.MinValue;
        private bool _adaptiveTracking;
        private double _carrierLoopScale;

        public JAlertProcessor(ISharpControl control, JAlertSettings settings)
        {
            _control = control;
            _settings = settings;
            _afcEnabled = settings.AfcEnabled;
            _adaptiveTracking = settings.AdaptiveTracking;
            _carrierLoopScale = settings.CarrierLoopScale;

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
                _receiver.Demod.AdaptiveTracking = _adaptiveTracking;
                _receiver.Demod.CarrierLoopScale = _carrierLoopScale;
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

        // Acquire/track gear-shift in the demod (lower steady-state BER).
        public bool AdaptiveTracking
        {
            get => _adaptiveTracking;
            set
            {
                _adaptiveTracking = value;
                BpskDemod demod = _receiver?.Demod;
                if (demod != null) demod.AdaptiveTracking = value;
            }
        }

        // Costas carrier-loop bandwidth multiplier (wider tracks more phase noise).
        public double CarrierLoopScale
        {
            get => _carrierLoopScale;
            set
            {
                _carrierLoopScale = value;
                BpskDemod demod = _receiver?.Demod;
                if (demod != null) demod.CarrierLoopScale = value;
            }
        }

        // Auto-follow LNB drift: when on, ServiceAfc re-centers the VFO.
        public bool AfcEnabled
        {
            get => _afcEnabled;
            set
            {
                _afcEnabled = value;
                _afcLastTick = DateTime.MinValue;   // act on the next service tick
            }
        }

        // Automatic frequency control. Call periodically from the UI thread (it
        // writes ISharpControl.Frequency, the host tuning property). When the
        // demod is locked, slowly steer the SDR# VFO onto the recovered carrier
        // so a drifting LNB local oscillator keeps the signal inside the host
        // channel filter. No-op while unlocked, so it never chases noise between
        // bursts; the slow LNB drift is recovered on the next received burst.
        public void ServiceAfc()
        {
            if (!_afcEnabled) return;

            BpskDemod demod = _receiver?.Demod;
            if (demod == null || !demod.Locked) return;

            DateTime now = DateTime.UtcNow;
            if ((now - _afcLastTick).TotalSeconds < AfcIntervalSec) return;
            _afcLastTick = now;

            double offset = demod.CoarseOffsetHz;
            if (Math.Abs(offset) < AfcDeadbandHz) return;

            double step = offset * AfcGain;
            if (step > AfcMaxStepHz) step = AfcMaxStepHz;
            else if (step < -AfcMaxStepHz) step = -AfcMaxStepHz;

            long newFreq = _control.Frequency + (long)Math.Round(step);
            if (newFreq > 0) _control.Frequency = newFreq;
        }

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
