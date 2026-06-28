// Lightweight single-precision complex value used throughout the J-ALERT DSP
// chain. Ported from src/dsp/complex.h. The BPSK front end leans on these
// helpers for carrier recovery (squaring, derotation), so they live here rather
// than being open-coded at every call site.

namespace SDRSharp.JAlert.Dsp
{
    public struct Complex32
    {
        public float Re;
        public float Im;

        public Complex32(float r, float i)
        {
            Re = r;
            Im = i;
        }

        public static Complex32 operator +(Complex32 a, Complex32 b) => new Complex32(a.Re + b.Re, a.Im + b.Im);
        public static Complex32 operator -(Complex32 a, Complex32 b) => new Complex32(a.Re - b.Re, a.Im - b.Im);
        public static Complex32 operator *(Complex32 a, Complex32 b) => new Complex32(a.Re * b.Re - a.Im * b.Im, a.Re * b.Im + a.Im * b.Re);
        public static Complex32 operator *(Complex32 a, float s) => new Complex32(a.Re * s, a.Im * s);

        public static Complex32 Conj(Complex32 a) => new Complex32(a.Re, -a.Im);
    }
}
