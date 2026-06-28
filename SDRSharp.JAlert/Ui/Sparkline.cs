// One-row "caption · history-bars · current value" sparkline, rendered with
// GDI+. Ring capacity 120; the panel pushes every 0.5 s for a 60 s rolling
// window. Ported from src/ui/sparkline.{h,cpp} (ImGui → WinForms).

using System;
using System.Drawing;
using System.Windows.Forms;

namespace SDRSharp.JAlert.Ui
{
    public sealed class Sparkline : Control
    {
        private const int Capacity = 120;

        private readonly double[] _ring = new double[Capacity];
        private int _writeIdx;
        private int _count;
        private double _rangeMin = double.NaN;
        private double _rangeMax = double.NaN;

        private float _captionW = 90.0f;

        public string Caption { get; set; } = "";
        public string CurrentText { get; set; } = "";
        public Color BarColor { get; set; } = Color.FromArgb(70, 180, 85);

        public Sparkline()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.UserPaint |
                     ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Height = 18;
        }

        public void SetCaptionWidth(float w) => _captionW = w;

        public void Push(double v)
        {
            _ring[_writeIdx] = v;
            _writeIdx = (_writeIdx + 1) % Capacity;
            if (_count < Capacity) ++_count;
        }

        public void ClearHistory()
        {
            _writeIdx = 0;
            _count = 0;
        }

        public void SetFixedRange(double rmin, double rmax)
        {
            _rangeMin = rmin;
            _rangeMax = rmax;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);

            float rowH = Height;
            float avail = Width;
            Color captionCol = Color.FromArgb(160, ForeColor);

            using (Font font = Font)
            using (SolidBrush capBrush = new SolidBrush(captionCol))
            using (SolidBrush valBrush = new SolidBrush(BarColor))
            {
                g.DrawString(Caption, font, capBrush, 0, 1);

                SizeF valSz = g.MeasureString(CurrentText, font);
                float valLeft = Math.Max(_captionW + 8.0f, avail - valSz.Width);
                g.DrawString(CurrentText, font, valBrush, valLeft, 1);

                float barLeft = _captionW;
                float barRight = valLeft - 4.0f;
                float barW = barRight - barLeft;
                if (barW > 0.0f && _count > 0)
                {
                    double rmin = _rangeMin;
                    double rmax = _rangeMax;
                    bool autoMin = double.IsNaN(rmin);
                    bool autoMax = double.IsNaN(rmax);
                    if (autoMin || autoMax)
                    {
                        double mn = double.PositiveInfinity;
                        double mx = double.NegativeInfinity;
                        for (int i = 0; i < _count; ++i)
                        {
                            double v = _ring[i];
                            if (v < mn) mn = v;
                            if (v > mx) mx = v;
                        }
                        if (autoMin) rmin = mn;
                        if (autoMax) rmax = mx;
                    }
                    if (rmax - rmin < 1e-9) rmax = rmin + 1.0;

                    float midY = rowH - 2.0f;
                    float barH = rowH - 3.0f;

                    using (Pen track = new Pen(Color.FromArgb(0x60, captionCol)))
                        g.DrawLine(track, barLeft, midY, barRight, midY);

                    int samples = _count;
                    int start = _count < Capacity ? 0 : _writeIdx;
                    int pixels = (int)barW;
                    using (Pen barPen = new Pen(Color.FromArgb(0xC8, BarColor)))
                    {
                        for (int x = 0; x < pixels; ++x)
                        {
                            int i = (int)((long)x * samples / pixels);
                            int idx = (start + i) % Capacity;
                            double v = _ring[idx];
                            double t = (v - rmin) / (rmax - rmin);
                            if (t < 0.0) t = 0.0;
                            else if (t > 1.0) t = 1.0;
                            float h = Math.Max(1.0f, (float)(t * barH));
                            g.DrawLine(barPen, barLeft + x, midY, barLeft + x, midY - h);
                        }
                    }
                }
            }
        }
    }
}
