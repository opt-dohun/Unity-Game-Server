#include "battle_rng.h"

#include <assert.h>

// PCG-XSH-RR 32: advance a 64-bit state, then permute its old value.
uint32_t battle_rng_next(BattleRng *rng) {
    uint64_t old = rng->state;
    rng->state = old * UINT64_C(6364136223846793005) + rng->stream;
    uint32_t shifted = (uint32_t)(((old >> 18u) ^ old) >> 27u);
    uint32_t rotation = (uint32_t)(old >> 59u);
    return (shifted >> rotation) | (shifted << ((-rotation) & 31u));
}

void battle_rng_seed(BattleRng *rng, uint64_t seed, uint64_t stream) {
    rng->state = 0;
    rng->stream = (stream << 1u) | 1u; // PCG requires an odd increment.
    battle_rng_next(rng);
    rng->state += seed;
    battle_rng_next(rng);
}

uint32_t battle_rng_bounded(BattleRng *rng, uint32_t bound) {
    assert(bound > 0);
    // Reject the small remainder so all values have equal probability.
    uint32_t threshold = (uint32_t)(-bound) % bound;
    for (;;) {
        uint32_t value = battle_rng_next(rng);
        if (value >= threshold) return value % bound;
    }
}

int battle_attack_damage(BattleRng *rng) {
    return 1 + (int)battle_rng_bounded(rng, 20);
}
