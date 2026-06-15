#pragma once
// Rate-1/2 convolutional code used by the J-ALERT satellite link:
// K=7, generator polynomials (171, 133) in octal — i.e. the classic NASA /
// CCSDS "Voyager" code, the standard "sequential 1/2" inner FEC of COTS
// satellite modems.
//
// Bit convention:
//   reg = (input_bit << (K-1)) | state      // input shifts in at the MSB
//   out0 = parity(reg & G1),  out1 = parity(reg & G2)
//   next_state = (reg >> 1) & (NSTATES-1)
// Coded bit b maps to BPSK symbol (1 - 2b): 0 -> +1, 1 -> -1.

#include <cstdint>
#include <vector>

namespace jalert::fec {

constexpr int kConstraint = 7;                       // K
constexpr int kMemory = kConstraint - 1;             // 6
constexpr int kNumStates = 1 << kMemory;             // 64
constexpr unsigned kG1 = 0171;                       // octal 171 = 0x79
constexpr unsigned kG2 = 0133;                       // octal 133 = 0x5B

inline int parity(unsigned x) {
    // Even parity (XOR of all set bits).
    x ^= x >> 16;
    x ^= x >> 8;
    x ^= x >> 4;
    x ^= x >> 2;
    x ^= x >> 1;
    return static_cast<int>(x & 1u);
}

// Convolutional-encode info bits (0/1) into 2× coded bits (0/1), starting from
// the zero state. Used by the self-test and to seed re-encode BER checks.
std::vector<uint8_t> conv_encode(const std::vector<uint8_t>& bits,
                                 unsigned g1 = kG1, unsigned g2 = kG2);

} // namespace jalert::fec
