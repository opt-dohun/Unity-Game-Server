using CrimsonTide.Server.Domain;

var battle = new BattleState { Id = "test" };
BattleRules.Seed(battle, 12345, 7);
int[] expected = [11, 9, 18, 10, 4];
for (int i = 0; i < expected.Length; i++)
{
    var result = BattleRules.Apply(battle, "attack");
    if (result.AttackDamage != expected[i])
        throw new Exception($"Turn {i + 1}: expected {expected[i]}, got {result.AttackDamage}");
}
Console.WriteLine("PCG32 C/C# replay matched: 11, 9, 18, 10, 4");
