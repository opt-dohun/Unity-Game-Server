using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using CrimsonTide.Infra;
using CrimsonTide.Server.Domain;
using CrimsonTide.Server.Schema;

namespace CrimsonTide.Server.Application;

public class BattleApplicationService
{
    private readonly BattleStore _db;

    public BattleApplicationService(BattleStore db)
    {
        _db = db;
    }

    public async Task<IResult> CreateBattleAsync(string idempotencyKey)
    {
        if (idempotencyKey.Length is < 8 or > 128) return Results.BadRequest(new { error = "start_key_required" });
        await _db.Gate.WaitAsync();
        try
        {
            using var connection = _db.Open();
            var existing = BattleStore.ReadJson<BattleState>(connection, null,
                "SELECT state_json FROM battles WHERE start_key = $key", ("$key", idempotencyKey));
            if (existing != null) return Results.Ok(ToResponse(existing));
            
            var battle = new BattleState { Id = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant() };
            BattleRules.Seed(battle, RandomUInt64(), RandomUInt64());
            
            using var tx = connection.BeginTransaction();
            BattleStore.Execute(connection, tx,
                "INSERT INTO battles(id,start_key,state_json,created_at) VALUES($id,$key,$state,$now)",
                ("$id", battle.Id), ("$key", idempotencyKey), ("$state", JsonSerializer.Serialize(battle)),
                ("$now", DateTimeOffset.UtcNow.ToString("O")));
            tx.Commit();
            return Results.Created($"/api/battles/{battle.Id}", ToResponse(battle));
        }
        finally { _db.Gate.Release(); }
    }

    public IResult GetBattle(string id)
    {
        using var connection = _db.Open();
        var battle = BattleStore.ReadJson<BattleState>(connection, null,
            "SELECT state_json FROM battles WHERE id = $id", ("$id", id));
        return battle == null ? Results.NotFound() : Results.Ok(ToResponse(battle));
    }

    public async Task<IResult> PlayTurnAsync(string id, TurnRequest input)
    {
        if (input.Action is not ("attack" or "guard") || input.Turn < 1)
            return Results.BadRequest(new { error = "invalid_turn" });
        await _db.Gate.WaitAsync();
        try
        {
            using var connection = _db.Open();
            using var tx = connection.BeginTransaction();
            using var previous = BattleStore.Command(connection, tx,
                "SELECT action,result_json FROM turns WHERE battle_id=$id AND turn=$turn",
                ("$id", id), ("$turn", input.Turn));
            using (var reader = previous.ExecuteReader())
            {
                if (reader.Read())
                {
                    if (reader.GetString(0) != input.Action) return Results.Conflict(new { error = "turn_already_used" });
                    return Results.Content(reader.GetString(1), "application/json");
                }
            }
            var battle = BattleStore.ReadJson<BattleState>(connection, tx,
                "SELECT state_json FROM battles WHERE id=$id", ("$id", id));
            if (battle == null) return Results.NotFound();
            if (battle.Status != "playing" || battle.Turn != input.Turn)
                return Results.Conflict(new { error = "turn_out_of_order", nextTurn = battle.Turn, status = battle.Status });
            
            var outcome = BattleRules.Apply(battle, input.Action);
            var result = new TurnResult(battle.Id, outcome.PlayedTurn, outcome.Action, outcome.AttackDamage, outcome.BossDamage,
                battle.HeroHp, battle.BossHp, outcome.Stage, outcome.PlayedPattern, battle.Turn, battle.Status);
            
            string json = JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            BattleStore.Execute(connection, tx,
                "INSERT INTO turns(battle_id,turn,action,result_json) VALUES($id,$turn,$action,$json)",
                ("$id", id), ("$turn", input.Turn), ("$action", input.Action), ("$json", json));
            BattleStore.Execute(connection, tx, "UPDATE battles SET state_json=$state WHERE id=$id",
                ("$state", JsonSerializer.Serialize(battle)), ("$id", id));
            tx.Commit();
            return Results.Content(json, "application/json");
        }
        finally { _db.Gate.Release(); }
    }

    public async Task<IResult> RegisterScoreAsync(string id, RegisterRequest input)
    {
        string name = input.Name?.Trim() ?? "";
        if (name.Length == 0 || name.EnumerateRunes().Count() > 12 || name.Any(char.IsControl))
            return Results.BadRequest(new { error = "invalid_name" });
        await _db.Gate.WaitAsync();
        try
        {
            using var connection = _db.Open();
            using var tx = connection.BeginTransaction();
            var battle = BattleStore.ReadJson<BattleState>(connection, tx,
                "SELECT state_json FROM battles WHERE id=$id", ("$id", id));
            if (battle == null) return Results.NotFound();
            if (battle.Status != "won") return Results.Conflict(new { error = "battle_not_won" });
            using var existing = BattleStore.Command(connection, tx,
                "SELECT name,turns,remaining_hp,created_at FROM scores WHERE battle_id=$id", ("$id", id));
            using (var reader = existing.ExecuteReader())
            {
                if (reader.Read())
                {
                    if (reader.GetString(0) != name) return Results.Conflict(new { error = "score_already_registered" });
                    return Results.Ok(new ScoreResponse(name, reader.GetInt32(1), reader.GetInt32(2), reader.GetString(3)));
                }
            }
            string now = DateTimeOffset.UtcNow.ToString("O");
            BattleStore.Execute(connection, tx,
                "INSERT INTO scores(battle_id,name,turns,remaining_hp,created_at) VALUES($id,$name,$turns,$hp,$now)",
                ("$id", id), ("$name", name), ("$turns", battle.Turn), ("$hp", battle.HeroHp), ("$now", now));
            tx.Commit();
            return Results.Created($"/api/battles/{id}/score", new ScoreResponse(name, battle.Turn, battle.HeroHp, now));
        }
        finally { _db.Gate.Release(); }
    }

    public IResult GetScores()
    {
        using var connection = _db.Open();
        using var command = BattleStore.Command(connection, null,
            "SELECT name,turns,remaining_hp,created_at FROM scores ORDER BY turns ASC, remaining_hp DESC, created_at ASC LIMIT 20");
        using var reader = command.ExecuteReader();
        var scores = new List<ScoreResponse>();
        while (reader.Read()) scores.Add(new ScoreResponse(reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetString(3)));
        return Results.Ok(new { scores });
    }

    private static ulong RandomUInt64() => BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8));
    
    private static BattleResponse ToResponse(BattleState b) => new BattleResponse(
        b.Id, b.Turn, b.HeroHp, b.BossHp, b.BossHp <= 50 ? 2 : 1, b.Pattern, b.Status);
}
