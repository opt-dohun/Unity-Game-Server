namespace CrimsonTide.Server.Domain;

public record BattleTurnOutcome(int PlayedTurn, string Action, int AttackDamage, int BossDamage, int Stage, int PlayedPattern);

public static class BattleRules
{
    public static void Seed(BattleState battle, ulong seed, ulong stream)
    {
        battle.RngState = 0;
        battle.RngStream = (stream << 1) | 1;
        NextRandom(battle);
        battle.RngState = unchecked(battle.RngState + seed);
        NextRandom(battle);
    }

    private static uint NextRandom(BattleState battle)
    {
        ulong old = battle.RngState;
        battle.RngState = unchecked(old * 6364136223846793005UL + battle.RngStream);
        uint shifted = (uint)(((old >> 18) ^ old) >> 27);
        int rotation = (int)(old >> 59);
        return (shifted >> rotation) | (shifted << ((-rotation) & 31));
    }

    private static uint Bounded(BattleState battle, uint bound)
    {
        uint threshold = unchecked(0u - bound) % bound;
        while (true)
        {
            uint value = NextRandom(battle);
            if (value >= threshold) return value % bound;
        }
    }

    public static BattleTurnOutcome Apply(BattleState battle, string action)
    {
        int playedTurn = battle.Turn;
        int playedPattern = battle.Pattern;
        int attackDamage = action == "attack" ? 1 + (int)Bounded(battle, 20) : 0;
        battle.BossHp = Math.Max(0, battle.BossHp - attackDamage);
        int stage = battle.BossHp <= 50 ? 2 : 1;
        int bossDamage = 0;
        
        if (battle.BossHp == 0) battle.Status = "won";
        else
        {
            int[] baseDamage = [7, 6, 8];
            bossDamage = stage == 1 ? baseDamage[playedPattern] : (baseDamage[playedPattern] * 135 + 99) / 100;
            if (action == "guard") bossDamage = (bossDamage + 4) / 5;
            battle.HeroHp = Math.Max(0, battle.HeroHp - bossDamage);
            if (battle.HeroHp == 0) battle.Status = "lost";
        }
        
        if (battle.Status == "playing")
        {
            battle.Turn++;
            battle.Pattern = (battle.Pattern + 1) % 3;
        }
        
        return new BattleTurnOutcome(playedTurn, action, attackDamage, bossDamage, stage, playedPattern);
    }
}
