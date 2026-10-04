#ifndef BATTLE_RNG_H
#define BATTLE_RNG_H

#include <stdint.h>

// Keep one instance per battle. Save both fields to resume an interrupted battle.
typedef struct {
    uint64_t state;
    uint64_t stream;
} BattleRng;

void battle_rng_seed(BattleRng *rng, uint64_t seed, uint64_t stream);
uint32_t battle_rng_next(BattleRng *rng);
// Returns a uniform integer in [0, bound). bound must be greater than zero.
uint32_t battle_rng_bounded(BattleRng *rng, uint32_t bound);
int battle_attack_damage(BattleRng *rng); // 1..20

#endif
