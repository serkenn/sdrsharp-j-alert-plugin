// Real-time scatter / heatmap of the Costas-derotated BPSK symbols, rendered with
// GDI+. BPSK clusters at (±amp, 0), so EVM and the ideal markers reference the
// real axis. Ported from src/ui/constellation_view.{h,cpp} (ImGui → WinForms).

using System;
using System.Drawing;
using System.Windows.Forms;
using SDRSharp.JAlert.Dsp;

namespace SDRSharp.JAlert.Ui
{
    // Newest-first pull (matches BpskDemod.PullConstellation()).
    public delegate int ConstellationPull(Complex32[] dst, int capacity);

    public sealed class ConstellationView : Control
    {
        private const int BinCount = 80;
        private const float AccumDecay = 0.88f;
        private const int PullPerFrame = 256;
        private const int DotsOverlayCount = 96;
        private const float AmpMax = 1.5f;
        private const double ClipWarnRatio = 0.02;
        private const double ObservedAmpAlpha = 0.05;

        private static readonly Color Green = Color.FromArgb(50, 220, 50);

        private ConstellationPull _pull;
        private readonly Complex32[] _scratch = new Complex32[PullPerFrame];
        private readonly float[] _accum = new float[BinCount * BinCount];

        private double _evmPercent;
        private double _evmIPercent;   // decision-axis (Re) error — drives BER
        private double _evmQPercent;   // quadrature (Im) error — phase/benign
        private double _snrDb;         // Es/N0 estimate from the decision axis
        private int _clippedThisFrame;
        private int _samplesThisFrame;
        private float _lastAccumMax;
        private double _observedAmp = 1.0;

        public ConstellationView()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.UserPaint |
                     ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Color.Black;
        }

        public void SetPull(ConstellationPull pull) => _pull = pull;

        public void ResetView()
        {
            Array.Clear(_accum, 0, _accum.Length);
            _lastAccumMax = 0.0f;
            _observedAmp = 1.0;
        }

        private void DecayAccumulator()
        {
            for (int i = 0; i < _accum.Length; ++i) _accum[i] *= AccumDecay;
        }

        private void AccumulateAndStats(int n)
        {
            _samplesThisFrame = n;
            _clippedThisFrame = 0;

            float idealAxis = (float)_observedAmp;   // BPSK: |Re|≈amp
            double evmReSq = 0.0, evmImSq = 0.0, ampSq = 0.0;
            int evmN = 0;

            for (int i = 0; i < n; ++i)
            {
                float re = _scratch[i].Re, im = _scratch[i].Im;

                float idealRe = re >= 0.0f ? idealAxis : -idealAxis;
                float dRe = re - idealRe;
                float dIm = im;                          // ideal Im = 0
                evmReSq += dRe * dRe;                     // decision-axis error
                evmImSq += dIm * dIm;                     // quadrature error
                ampSq += re * re + im * im;
                ++evmN;

                if (Math.Abs(re) > AmpMax || Math.Abs(im) > AmpMax)
                {
                    ++_clippedThisFrame;
                    continue;
                }
                int bx = (int)((re + AmpMax) * (BinCount / (2.0f * AmpMax)));
                int by = (int)((AmpMax - im) * (BinCount / (2.0f * AmpMax)));
                if ((uint)bx >= BinCount || (uint)by >= BinCount) continue;
                _accum[by * BinCount + bx] += 1.0f;
            }

            if (evmN > 0)
            {
                double frameAmp = Math.Sqrt(ampSq / evmN);
                if (frameAmp > 0.05)
                    _observedAmp = (1.0 - ObservedAmpAlpha) * _observedAmp + ObservedAmpAlpha * frameAmp;
                double denom = Math.Max(0.1, _observedAmp);
                double sigmaI = Math.Sqrt(evmReSq / evmN);
                double sigmaQ = Math.Sqrt(evmImSq / evmN);
                _evmPercent = Math.Sqrt((evmReSq + evmImSq) / evmN) / denom * 100.0;
                _evmIPercent = sigmaI / denom * 100.0;
                _evmQPercent = sigmaQ / denom * 100.0;
                // Decision-axis Es/N0: signal amp vs the noise std on Re. This is
                // what predicts BPSK BER (the quadrature spread does not).
                _snrDb = sigmaI > 1e-6 ? 20.0 * Math.Log10(denom / sigmaI) : 99.0;
            }
            else
            {
                _evmPercent = _evmIPercent = _evmQPercent = 0.0;
                _snrDb = 0.0;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            int side = Math.Max(16, Math.Min(Width, Height));
            float size = side;
            float ox = 0, oy = 0;

            g.Clear(Color.Black);

            int pullN = _pull != null ? _pull(_scratch, _scratch.Length) : 0;
            DecayAccumulator();
            AccumulateAndStats(pullN);

            float cx = ox + size * 0.5f;
            float cy = oy + size * 0.5f;
            float scale = (size * 0.5f) / AmpMax;

            using (Pen dim = new Pen(Color.FromArgb(40, Green)))
            {
                g.DrawRectangle(dim, ox, oy, size - 1, size - 1);
                g.DrawLine(dim, ox, cy, ox + size, cy);
                g.DrawLine(dim, cx, oy, cx, oy + size);
            }

            float orbit = Math.Max(0.1f, Math.Min(AmpMax, (float)_observedAmp));
            float idealR = scale * orbit;
            float m = Math.Max(3.0f, size / 50.0f);
            using (Pen marker = new Pen(Color.FromArgb(110, Green)))
            {
                DrawCross(g, marker, cx + idealR, cy, m);  // BPSK ideal points on the real axis
                DrawCross(g, marker, cx - idealR, cy, m);
            }

            float maxV = 0.0f;
            float binPx = size / BinCount;
            const float alphaScale = 32.0f;
            for (int i = 0; i < _accum.Length; ++i)
            {
                float v = _accum[i];
                if (v > maxV) maxV = v;
                int a = (int)(v * alphaScale);
                if (a <= 0) continue;
                if (a > 255) a = 255;
                int by = i / BinCount, bx = i % BinCount;
                float x0 = ox + bx * binPx, y0 = oy + by * binPx;
                using (SolidBrush br = new SolidBrush(Color.FromArgb(a, Green)))
                    g.FillRectangle(br, x0, y0, binPx + 1.0f, binPx + 1.0f);
            }
            _lastAccumMax = maxV;

            int take = Math.Min(pullN, DotsOverlayCount);
            float l = ox, t = oy, r = ox + size, b = oy + size;
            using (SolidBrush dot = new SolidBrush(Color.FromArgb(220, Green)))
            {
                for (int i = 0; i < take; ++i)
                {
                    float px = cx + _scratch[i].Re * scale;
                    float py = cy - _scratch[i].Im * scale;
                    if (px < l || px >= r || py < t || py >= b) continue;
                    g.FillRectangle(dot, px - 1.0f, py - 1.0f, 2.0f, 2.0f);
                }
            }

            using (Font font = new Font(FontFamily.GenericSansSerif, 7.0f))
            {
                if (pullN > 0)
                {
                    string buf = "EVM " + _evmPercent.ToString("0.0") + "%";
                    SizeF sz = g.MeasureString(buf, font);
                    using (SolidBrush br = new SolidBrush(Color.FromArgb(190, Green)))
                        g.DrawString(buf, font, br, ox + size - sz.Width - 4.0f, oy + 2.0f);

                    // I/Q split + decision-axis SNR: tells whether the EVM is
                    // decision-relevant (I) or benign phase/quadrature (Q).
                    string buf2 = "I " + _evmIPercent.ToString("0") + " Q " + _evmQPercent.ToString("0") +
                                  "  " + _snrDb.ToString("0.0") + "dB";
                    SizeF sz2 = g.MeasureString(buf2, font);
                    using (SolidBrush br = new SolidBrush(Color.FromArgb(150, Green)))
                        g.DrawString(buf2, font, br, ox + size - sz2.Width - 4.0f, oy + 2.0f + sz.Height);

                    if (_samplesThisFrame > 0 &&
                        (double)_clippedThisFrame / _samplesThisFrame >= ClipWarnRatio)
                    {
                        double pct = 100.0 * _clippedThisFrame / _samplesThisFrame;
                        string cbuf = "! clip " + pct.ToString("0") + "%";
                        SizeF csz = g.MeasureString(cbuf, font);
                        using (SolidBrush br = new SolidBrush(Color.FromArgb(220, 255, 140, 0)))
                            g.DrawString(cbuf, font, br, ox + size - csz.Width - 4.0f, oy + size - csz.Height - 2.0f);
                    }
                }

                if (pullN == 0 && _lastAccumMax < 0.05f)
                {
                    string msg = "no signal";
                    SizeF sz = g.MeasureString(msg, font);
                    using (SolidBrush br = new SolidBrush(Color.FromArgb(140, Green)))
                        g.DrawString(msg, font, br, cx - sz.Width * 0.5f, cy - sz.Height * 0.5f);
                }
            }
        }

        private static void DrawCross(Graphics g, Pen p, float px, float py, float m)
        {
            g.DrawLine(p, px - m, py - m, px + m, py + m);
            g.DrawLine(p, px - m, py + m, px + m, py - m);
        }
    }
}
