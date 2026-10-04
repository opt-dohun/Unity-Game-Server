namespace CrimsonTide.Server.Schema;

public sealed record TurnResult(string BattleId, int Turn, string Action, int AttackDamage,
    int BossDamage, int HeroHp, int BossHp, int BossStage, int Pattern, int NextTurn, string Status);

public sealed record BattleResponse(string BattleId, int Turn, int HeroHp, int BossHp, int BossStage, int Pattern, string Status);

public sealed record ScoreResponse(string Name, int Turns, int RemainingHp, string CreatedAt);
