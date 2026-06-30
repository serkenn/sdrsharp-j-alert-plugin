// SDR# side panel for the J-ALERT decoder. Replaces the SDR++ ImGui menu
// (main.cpp draw_menu / draw_latest / draw_output) with a WinForms UserControl.
// A ~10 fps timer repaints the constellation and refreshes the diagnostics /
// sparklines (sparklines pushed every 0.5 s for a 60 s rolling window).

using System;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;
using SDRSharp.JAlert.Decode;
using SDRSharp.JAlert.Dsp;
using SDRSharp.JAlert.Sink;
using SDRSharp.JAlert.Ui;

namespace SDRSharp.JAlert
{
    public sealed class JAlertPanel : UserControl
    {
        private const double PushIntervalSec = 0.5;

        private static readonly Color Green = Color.FromArgb(70, 180, 85);
        private static readonly Color Amber = Color.FromArgb(220, 170, 40);
        private static readonly Color Red = Color.FromArgb(200, 70, 70);

        private readonly JAlertProcessor _processor;
        private readonly JAlertSettings _settings;

        private readonly Label _statusLabel = new Label();
        private readonly ConstellationView _constellation = new ConstellationView();
        private readonly Sparkline _sparkCoarse = new Sparkline();
        private readonly Sparkline _sparkCostas = new Sparkline();
        private readonly Sparkline _sparkQuality = new Sparkline();
        private readonly CheckBox _afcCheck = new CheckBox();
        private readonly CheckBox _adaptiveCheck = new CheckBox();
        private readonly ComboBox _carrierLoopCombo = new ComboBox();
        private readonly Label _counters = new Label();

        // Carrier-loop bandwidth presets (label, Costas-gain multiplier).
        private static readonly (string Label, double Scale)[] CarrierLoopPresets =
        {
            ("Narrow (0.5x)", 0.5),
            ("Normal (1x)", 1.0),
            ("Wide (2x)", 2.0),
            ("Wider (4x)", 4.0),
        };
        private readonly Label _latest = new Label();
        private readonly ListBox _recentList = new ListBox();

        private readonly CheckBox _fileOutputCheck = new CheckBox();
        private readonly TextBox _xmlDirBox = new TextBox();
        private readonly CheckBox _jsonlFileCheck = new CheckBox();
        private readonly TextBox _jsonlFileBox = new TextBox();
        private readonly Label _fileSinkStatus = new Label();
        private readonly CheckBox _jsonlTcpCheck = new CheckBox();
        private readonly NumericUpDown _tcpPort = new NumericUpDown();
        private readonly Label _tcpSinkStatus = new Label();

        private readonly Timer _timer = new Timer();
        private DateTime _lastPush = DateTime.UtcNow;

        public JAlertPanel(JAlertProcessor processor, JAlertSettings settings)
        {
            _processor = processor;
            _settings = settings;

            Width = 300;
            Height = 700;

            BuildUi();

            // SDR# applies its (dark) theme to the panel after construction by
            // setting our BackColor/ForeColor. Re-sync the controls it doesn't
            // theme itself (custom-painted sparklines + input controls) whenever
            // that happens, and once now for the non-themed default.
            BackColorChanged += (s, e) => ApplyTheme();
            ForeColorChanged += (s, e) => ApplyTheme();
            ApplyTheme();

            _sparkCoarse.SetFixedRange(-300000.0, 300000.0);
            _sparkCostas.SetFixedRange(-2000.0, 2000.0);
            _sparkQuality.SetFixedRange(0.0, 20.0);   // BER %, 0..20 (no-signal ~15%)

            _constellation.SetPull((dst, cap) =>
            {
                Receiver r = _processor.CurrentReceiver;
                return r != null ? r.Demod.PullConstellation(dst, cap) : 0;
            });

            _timer.Interval = 100;   // ~10 fps
            _timer.Tick += OnTick;
            _timer.Start();
        }

        private void BuildUi()
        {
            TableLayoutPanel root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                AutoScroll = true,
                Padding = new Padding(6),
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            _statusLabel.AutoSize = true;
            _statusLabel.Font = new Font(Font, FontStyle.Bold);
            _statusLabel.Text = "● Disabled";

            _constellation.Height = 240;
            _constellation.Dock = DockStyle.Fill;

            ConfigureSparkline(_sparkCoarse);
            ConfigureSparkline(_sparkCostas);
            ConfigureSparkline(_sparkQuality);

            _afcCheck.Text = "Auto-follow LNB drift (AFC)";
            _afcCheck.AutoSize = true;
            _afcCheck.Checked = _settings.AfcEnabled;
            _processor.AfcEnabled = _settings.AfcEnabled;
            _afcCheck.CheckedChanged += (s, e) =>
            {
                _settings.AfcEnabled = _afcCheck.Checked;
                _processor.AfcEnabled = _afcCheck.Checked;
                _settings.Save();
            };

            _adaptiveCheck.Text = "Adaptive tracking (lower BER when locked)";
            _adaptiveCheck.AutoSize = true;
            _adaptiveCheck.Checked = _settings.AdaptiveTracking;
            _processor.AdaptiveTracking = _settings.AdaptiveTracking;
            _adaptiveCheck.CheckedChanged += (s, e) =>
            {
                _settings.AdaptiveTracking = _adaptiveCheck.Checked;
                _processor.AdaptiveTracking = _adaptiveCheck.Checked;
                _settings.Save();
            };

            _carrierLoopCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            foreach ((string label, double _) in CarrierLoopPresets) _carrierLoopCombo.Items.Add(label);
            _carrierLoopCombo.SelectedIndex = NearestCarrierLoopIndex(_settings.CarrierLoopScale);
            _processor.CarrierLoopScale = CarrierLoopPresets[_carrierLoopCombo.SelectedIndex].Scale;
            _carrierLoopCombo.SelectedIndexChanged += (s, e) =>
            {
                double scale = CarrierLoopPresets[_carrierLoopCombo.SelectedIndex].Scale;
                _settings.CarrierLoopScale = scale;
                _processor.CarrierLoopScale = scale;
                _settings.Save();
            };

            _counters.AutoSize = true;
            _counters.Text = "";

            Label latestHeader = SectionHeader("Latest alert");
            _latest.AutoSize = false;
            _latest.Dock = DockStyle.Fill;
            _latest.Height = 80;
            _latest.Text = "(none yet)";

            _recentList.Dock = DockStyle.Fill;
            _recentList.Height = 90;
            _recentList.IntegralHeight = false;

            Label outputHeader = SectionHeader("Output");

            _fileOutputCheck.Text = "File output (save XML/raw to folder)";
            _fileOutputCheck.AutoSize = true;
            _fileOutputCheck.Checked = _settings.FileOutputEnabled;
            _fileOutputCheck.CheckedChanged += (s, e) =>
            {
                _settings.FileOutputEnabled = _fileOutputCheck.Checked;
                _settings.Save();
            };

            Label dirLabel = new Label { Text = "Output folder", AutoSize = true };
            _xmlDirBox.Dock = DockStyle.Fill;
            _xmlDirBox.Text = _settings.XmlOutputDir;
            _xmlDirBox.Leave += (s, e) =>
            {
                _settings.XmlOutputDir = _xmlDirBox.Text;
                _settings.Save();
            };

            _jsonlFileCheck.Text = "JSONL file output";
            _jsonlFileCheck.AutoSize = true;
            _jsonlFileCheck.Checked = _settings.JsonlFileEnabled;
            _jsonlFileCheck.CheckedChanged += OnJsonlFileToggled;

            _jsonlFileBox.Dock = DockStyle.Fill;
            _jsonlFileBox.Text = _settings.JsonlFilePath;
            _jsonlFileBox.Leave += (s, e) =>
            {
                _settings.JsonlFilePath = _jsonlFileBox.Text;
                _settings.Save();
            };

            _fileSinkStatus.AutoSize = true;
            _fileSinkStatus.ForeColor = SystemColors.GrayText;

            _jsonlTcpCheck.Text = "JSONL TCP output";
            _jsonlTcpCheck.AutoSize = true;
            _jsonlTcpCheck.Checked = _settings.JsonlTcpEnabled;
            _jsonlTcpCheck.CheckedChanged += OnJsonlTcpToggled;

            Label portLabel = new Label { Text = "TCP port", AutoSize = true };
            _tcpPort.Minimum = 1;
            _tcpPort.Maximum = 65535;
            _tcpPort.Value = Math.Min(65535, Math.Max(1, _settings.JsonlTcpPort));
            _tcpPort.ValueChanged += (s, e) =>
            {
                _settings.JsonlTcpPort = (int)_tcpPort.Value;
                _settings.Save();
            };

            _tcpSinkStatus.AutoSize = true;
            _tcpSinkStatus.ForeColor = SystemColors.GrayText;

            root.Controls.Add(_statusLabel);
            root.Controls.Add(Separator());
            root.Controls.Add(_constellation);
            root.Controls.Add(_sparkCoarse);
            root.Controls.Add(_sparkCostas);
            root.Controls.Add(_sparkQuality);
            root.Controls.Add(_adaptiveCheck);
            root.Controls.Add(new Label { Text = "Carrier loop bandwidth", AutoSize = true });
            root.Controls.Add(_carrierLoopCombo);
            root.Controls.Add(_afcCheck);
            root.Controls.Add(Separator());
            root.Controls.Add(_counters);
            root.Controls.Add(Separator());
            root.Controls.Add(latestHeader);
            root.Controls.Add(_latest);
            root.Controls.Add(_recentList);
            root.Controls.Add(Separator());
            root.Controls.Add(outputHeader);
            root.Controls.Add(_fileOutputCheck);
            root.Controls.Add(dirLabel);
            root.Controls.Add(_xmlDirBox);
            root.Controls.Add(_jsonlFileCheck);
            root.Controls.Add(_jsonlFileBox);
            root.Controls.Add(_fileSinkStatus);
            root.Controls.Add(_jsonlTcpCheck);
            root.Controls.Add(portLabel);
            root.Controls.Add(_tcpPort);
            root.Controls.Add(_tcpSinkStatus);

            Controls.Add(root);
        }

        private void ConfigureSparkline(Sparkline s)
        {
            s.Dock = DockStyle.Fill;
            s.Height = 18;
            s.BackColor = SystemColors.Control;
            s.SetCaptionWidth(90.0f);
        }

        // Index of the preset whose scale is closest to a stored value.
        private static int NearestCarrierLoopIndex(double scale)
        {
            int best = 1;   // default "Normal"
            double bestDiff = double.MaxValue;
            for (int i = 0; i < CarrierLoopPresets.Length; ++i)
            {
                double d = Math.Abs(CarrierLoopPresets[i].Scale - scale);
                if (d < bestDiff) { bestDiff = d; best = i; }
            }
            return best;
        }

        private static Label SectionHeader(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Font = new Font(FontFamily.GenericSansSerif, 8.25f, FontStyle.Bold),
            };
        }

        private static Control Separator()
        {
            return new Label
            {
                Height = 2,
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.Fixed3D,
                Margin = new Padding(0, 4, 0, 4),
            };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyTheme();
        }

        private bool _applyingTheme;

        // Match the controls SDR#'s theme engine does not recolor (custom-painted
        // sparklines + input controls) to the panel's effective colors, deriving
        // a readable text/field shade from the background luminance so it works in
        // both the dark and light themes.
        private void ApplyTheme()
        {
            if (_applyingTheme) return;
            _applyingTheme = true;
            try
            {
                Color back = BackColor;
                Color fore = ForeColor;
                bool dark = (0.299 * back.R + 0.587 * back.G + 0.114 * back.B) < 128.0;
                // A field shade slightly offset from the panel background.
                Color field = dark ? ControlPaint.Light(back, 0.04f) : Color.White;
                Color border = dark ? ControlPaint.Light(back, 0.3f) : SystemColors.ControlDark;

                foreach (Sparkline s in new[] { _sparkCoarse, _sparkCostas, _sparkQuality })
                {
                    s.BackColor = back;
                    s.CaptionColor = fore;
                }

                _recentList.BackColor = field;
                _recentList.ForeColor = fore;

                foreach (TextBox tb in new[] { _xmlDirBox, _jsonlFileBox })
                {
                    tb.BackColor = field;
                    tb.ForeColor = fore;
                    tb.BorderStyle = BorderStyle.FixedSingle;
                }

                _tcpPort.BackColor = field;
                _tcpPort.ForeColor = fore;

                _carrierLoopCombo.BackColor = field;
                _carrierLoopCombo.ForeColor = fore;
                _carrierLoopCombo.FlatStyle = dark ? FlatStyle.Flat : FlatStyle.Standard;

                _counters.ForeColor = fore;
                _latest.ForeColor = fore;
                _ = border; // reserved for future custom borders
            }
            finally
            {
                _applyingTheme = false;
            }
        }

        private void OnJsonlFileToggled(object sender, EventArgs e)
        {
            if (_jsonlFileCheck.Checked && !string.IsNullOrEmpty(_jsonlFileBox.Text))
            {
                _settings.JsonlFilePath = _jsonlFileBox.Text;
                _processor.StartFileSink(_jsonlFileBox.Text);
                _settings.JsonlFileEnabled = true;
            }
            else
            {
                _processor.StopFileSink();
                _settings.JsonlFileEnabled = false;
                _jsonlFileCheck.Checked = false;
            }
            _settings.Save();
        }

        private void OnJsonlTcpToggled(object sender, EventArgs e)
        {
            if (_jsonlTcpCheck.Checked)
            {
                _processor.StartTcpSink((int)_tcpPort.Value);
                _settings.JsonlTcpEnabled = true;
            }
            else
            {
                _processor.StopTcpSink();
                _settings.JsonlTcpEnabled = false;
            }
            _settings.Save();
        }

        private void OnTick(object sender, EventArgs e)
        {
            _constellation.Invalidate();

            // Auto-follow LNB drift: re-center the VFO from the UI thread (safe
            // to write the host tuning here). Self-throttled and gated on lock.
            _processor.ServiceAfc();

            Receiver r = _processor.CurrentReceiver;
            bool enabled = _processor.Enabled;

            BpskDemod demod = r?.Demod;
            bool locked = demod?.Locked ?? false;
            double coarse = demod?.CoarseOffsetHz ?? 0.0;
            double costas = demod?.CostasOffsetHz ?? 0.0;
            double berPct = (r?.Ber ?? 0.0f) * 100.0;

            // Status lamp + label.
            Color lamp;
            string label;
            if (!enabled) { lamp = Color.Gray; label = "Disabled"; }
            else if (r == null) { lamp = Red; label = "Waiting for IQ"; }
            else if (locked) { lamp = Green; label = "Locked"; }
            else if ((demod?.SymMag ?? 0f) > 0.3f) { lamp = Amber; label = "Signal, acquiring"; }
            else { lamp = Red; label = "Searching"; }
            _statusLabel.ForeColor = lamp;
            _statusLabel.Text = "● " + label;

            // Sparklines: push every 0.5 s for a 60 s rolling window.
            DateTime now = DateTime.UtcNow;
            if ((now - _lastPush).TotalSeconds >= PushIntervalSec)
            {
                _sparkCoarse.Push(coarse);
                _sparkCostas.Push(costas);
                _sparkQuality.Push(berPct);
                _lastPush = now;
            }
            _sparkCoarse.Caption = "Carrier offset";
            _sparkCoarse.CurrentText = coarse.ToString("+0;-0", CultureInfo.InvariantCulture) + " Hz";
            _sparkCoarse.BarColor = Green;
            _sparkCostas.Caption = "Costas resid.";
            _sparkCostas.CurrentText = costas.ToString("+0;-0", CultureInfo.InvariantCulture) + " Hz";
            _sparkCostas.BarColor = Green;
            _sparkQuality.Caption = "Bit error rate";
            _sparkQuality.CurrentText = berPct.ToString("0.00") + "%";
            _sparkQuality.BarColor = berPct < 1.0 ? Green : berPct < 5.0 ? Amber : Red;
            _sparkCoarse.Invalidate();
            _sparkCostas.Invalidate();
            _sparkQuality.Invalidate();

            // Counters.
            long syms = demod?.Symbols ?? 0;
            _counters.Text =
                "Symbols: " + syms + "\n" +
                "HDLC frames: " + (r?.FramesOk ?? 0) + " ok / " + (r?.FramesBad ?? 0) + " rejected\n" +
                "NowcastPackets: " + (r?.Packets ?? 0) + "  (status " + (r?.StatusPackets ?? 0) + ")\n" +
                "Files: " + (r?.Files ?? 0) + "  decoded " + (r?.Alerts ?? 0);

            UpdateLatest();
            UpdateSinkStatus();
        }

        private void UpdateLatest()
        {
            System.Collections.Generic.List<AlertSummary> recent = _processor.SnapshotRecent();
            AlertSummary latest = null;
            foreach (AlertSummary a in recent)
                if (a.Decoded) { latest = a; break; }

            if (latest == null)
            {
                _latest.Text = "(none yet)";
            }
            else
            {
                StringBuilder sb = new StringBuilder();
                string title = !string.IsNullOrEmpty(latest.HeadTitle) ? latest.HeadTitle : latest.ControlTitle;
                if (!string.IsNullOrEmpty(title)) sb.AppendLine(title);
                if (!string.IsNullOrEmpty(latest.InfoType) || !string.IsNullOrEmpty(latest.ReportTime))
                    sb.AppendLine(latest.InfoType + "  " + latest.ReportTime);
                if (!string.IsNullOrEmpty(latest.Headline)) sb.AppendLine(latest.Headline);
                sb.Append("[" + latest.ChunkType + "] packet " + latest.PacketTime +
                          "  (" + latest.XmlBytes + " B XML)");
                _latest.Text = sb.ToString();
            }

            // Recent list.
            _recentList.BeginUpdate();
            _recentList.Items.Clear();
            foreach (AlertSummary a in recent)
            {
                string title = string.IsNullOrEmpty(a.HeadTitle) ? a.ControlTitle : a.HeadTitle;
                _recentList.Items.Add("[" + a.ChunkType + "] " + a.PacketTime + "  " + title);
            }
            _recentList.EndUpdate();
        }

        private void UpdateSinkStatus()
        {
            FileJsonlSink fs = _processor.FileSink;
            _fileSinkStatus.Text = fs != null ? fs.Snapshot().RecordsWritten + " records" : "";
            if (_jsonlFileCheck.Checked != (fs != null)) _jsonlFileCheck.Checked = fs != null;

            TcpJsonlSink ts = _processor.TcpSink;
            _tcpSinkStatus.Text = ts != null ? ts.Snapshot().ClientCount + " client(s)" : "";
            if (_jsonlTcpCheck.Checked != (ts != null)) _jsonlTcpCheck.Checked = ts != null;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer.Stop();
                _timer.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
