namespace CrimsonTide.Server.Domain;

public sealed class BattleState
{
    public required string Id { get; init; }
    public int Turn { get; set; } = 1;
    public int HeroHp { get; set; } = 100;
    public int BossHp { get; set; } = 100;
    public int Pattern { get; set; }
    public string Status { get; set; } = "playing";
    public ulong RngState { get; set; }
    public ulong RngStream { get; set; }
}
