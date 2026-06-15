#pragma once

namespace jalert::dsp {

struct Complex32 {
    float re;
    float im;

    constexpr Complex32() : re(0.0f), im(0.0f) {}
    constexpr Complex32(float r, float i) : re(r), im(i) {}
};

// Convenience helpers — the BPSK front end leans on these for carrier
// recovery (squaring, derotation), so they live here rather than being
// open-coded at every call site.
constexpr Complex32 operator+(Complex32 a, Complex32 b) {
    return Complex32(a.re + b.re, a.im + b.im);
}
constexpr Complex32 operator-(Complex32 a, Complex32 b) {
    return Complex32(a.re - b.re, a.im - b.im);
}
constexpr Complex32 operator*(Complex32 a, Complex32 b) {
    return Complex32(a.re * b.re - a.im * b.im, a.re * b.im + a.im * b.re);
}
constexpr Complex32 operator*(Complex32 a, float s) {
    return Complex32(a.re * s, a.im * s);
}
constexpr Complex32 conj(Complex32 a) { return Complex32(a.re, -a.im); }

} // namespace jalert::dsp
