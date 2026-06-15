#include "conv_code.h"

namespace jalert::fec {

std::vector<uint8_t> conv_encode(const std::vector<uint8_t>& bits,
                                 unsigned g1, unsigned g2) {
    std::vector<uint8_t> out(bits.size() * 2);
    unsigned s = 0;
    for (size_t i = 0; i < bits.size(); ++i) {
        const unsigned reg = (static_cast<unsigned>(bits[i] & 1u) << kMemory) | s;
        out[2 * i]     = static_cast<uint8_t>(parity(reg & g1));
        out[2 * i + 1] = static_cast<uint8_t>(parity(reg & g2));
        s = (reg >> 1) & (kNumStates - 1);
    }
    return out;
}

} // namespace jalert::fec
