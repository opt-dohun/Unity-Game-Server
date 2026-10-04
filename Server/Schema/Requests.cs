namespace CrimsonTide.Server.Schema;

public sealed record TurnRequest(int Turn, string Action);
public sealed record RegisterRequest(string Name);
