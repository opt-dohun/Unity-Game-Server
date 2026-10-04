#include "battle_rng.h"

#include <assert.h>
#include <stdio.h>

int main(void) {
    BattleRng battle_a, replay;
    const int expected_damage[] = {11, 9, 18, 10, 4};
    battle_rng_seed(&battle_a, 12345, 7);
    battle_rng_seed(&replay, 12345, 7);

    int boss_hp = 100;
    for (int turn = 1; turn <= 5; ++turn) {
        int damage = battle_attack_damage(&battle_a);
        assert(damage == expected_damage[turn - 1]);
        assert(damage == battle_attack_damage(&replay));
        boss_hp -= damage;
        printf("turn %d: damage %d, boss HP %d\n", turn, damage, boss_hp);
    }
    puts("replay matched");
    return 0;
}
