using System.Data.Common;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using CrimsonTide.Infra;
using CrimsonTide.Server.Domain;
using CrimsonTide.Server.Schema;

namespace CrimsonTide.Server.Application;

public class BattleApplicationService
{
    /// <summary>낙관적 락에서 version 충돌을 허용하는 재시도 횟수(5·10·20·40·80·160·320ms 지수 백오프).</summary>
    private const int MaxAttempts = 8;

    private readonly BattleStore _db;
    private readonly ServerMetrics _metrics;

    public BattleApplicationService(BattleStore db, ServerMetrics metrics)
    {
        _db = db;
        _metrics = metrics;
    }

    /// <summary>
    /// 전역 락(또는 낙관적 재시도 루프의 한 회) 진입까지의 대기 시간을 기록한다.
    /// 락이 없으면 호출 즉시 반환되어 대기 시간 0으로 집계된다.
    /// </summary>
    private async Task GateAsync(string name)
    {
        var stopwatch = Stopwatch.StartNew();
        await _db.Gate.WaitAsync();
        _metrics.Record(name, stopwatch.Elapsed.TotalMicroseconds);
    }

    /// <summary>SQLite busy/locked, MySQL lock-wait timeout/deadlock are transient and retryable.</summary>
    private bool IsTransientBusy(DbException ex) => ex switch
    {
        SqliteException sqlite => sqlite.SqliteErrorCode is 5 or 6,
        MySqlException mysql => mysql.Number is 1205 or 1213,
        _ => false
    };

    private static bool IsUniqueViolation(DbException ex) => ex switch
    {
        SqliteException sqlite => sqlite.SqliteErrorCode == 19,
        MySqlException mysql => mysql.Number == 1062,
        _ => false
    };

    private static string DatabaseErrorCode(DbException ex) => ex switch
    {
        SqliteException sqlite => sqlite.SqliteErrorCode.ToString(),
        MySqlException mysql => mysql.Number.ToString(),
        _ => ex.ErrorCode.ToString()
    };

    /// <summary>5·10·20·40·80·160·320ms 지수 백오프(attempt는 1부터).</summary>
    private static int RetryDelayMs(int attempt) => 5 * (1 << (attempt - 1));

    public async Task<IResult> CreateBattleAsync(string idempotencyKey)
    {
        if (idempotencyKey.Length is < 8 or > 128) return Results.BadRequest(new { error = "start_key_required" });

        if (!_db.Optimistic)
        {
            await GateAsync("lock_wait_create");
            var dbStart = Stopwatch.StartNew();
            try
            {
                using var connection = _db.Open();
                IResult result = await CreateBattleOnceAsync(connection, idempotencyKey);
                dbStart.Stop();
                _metrics.Record("db_create", dbStart.Elapsed.TotalMicroseconds);
                return result;
            }
            finally
            {
                _db.Gate.Release();
            }
        }

        // 낙관적 락: 전역 대기는 없으므로 lock_wait은 0으로 집계한다.
        // 동시 생성은 start_key UNIQUE 제약이 잡아 주며, 충돌 시 승자의 결과를 멱등 반환한다.
        // SQLite writer 대기 또는 MySQL 행 잠금 시간 초과/데드락이면 백오프 재시도한다.
        _metrics.Record("lock_wait_create", 0);
        for (int attempt = 1; ; attempt++)
        {
            var createDbStart = Stopwatch.StartNew();
            try
            {
                using var connection = _db.Open();
                IResult created = await CreateBattleOnceAsync(connection, idempotencyKey, immediate: true);
                createDbStart.Stop();
                _metrics.Record("db_create", createDbStart.Elapsed.TotalMicroseconds);
                return created;
            }
            catch (DbException ex) when (IsTransientBusy(ex))
            {
                createDbStart.Stop();
                _metrics.Record("db_create", createDbStart.Elapsed.TotalMicroseconds);
                _metrics.Record("db_busy_retry", createDbStart.Elapsed.TotalMicroseconds);
                if (attempt >= MaxAttempts)
                    return Results.Problem(statusCode: 509, title: "db_busy_exhausted",
                        detail: $"데이터베이스 잠금 경합이 {attempt}번 반복되어 포기했습니다(code={DatabaseErrorCode(ex)})");
                await Task.Delay(RetryDelayMs(attempt));
            }
        }
    }

    /// <summary>
    /// 트랜잭션 시작. 낙관적 경로(<paramref name="immediate"/>=true)는 read→write 승격
    /// (BEGIN DEFERRED) 대신 처음부터 write 락을 잡는다. 승격 방식은 WAL에서 스냅샷이
    /// 낡으면 SQLITE_BUSY로 실패하고, 그 동안 락을 오래 붙잡아 tail을 만든다.
    /// </summary>
    private DbTransaction Begin(DbConnection connection, bool immediate)
        => immediate && _db.Provider == "sqlite"
            ? ((SqliteConnection)connection).BeginTransaction(deferred: false)
            : connection.BeginTransaction();

    /// <summary>전투 생성 1회 시도. UNIQUE 충돌 시 승자의 결과를 멱등 반환한다.</summary>
    private async Task<IResult> CreateBattleOnceAsync(DbConnection connection, string idempotencyKey, bool immediate = false)
    {
        var existing = BattleStore.ReadJson<BattleState>(connection, null,
            "SELECT state_json FROM battles WHERE start_key = $key", ("$key", idempotencyKey));
        if (existing != null) return Results.Ok(ToResponse(existing));

        var battle = new BattleState { Id = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant() };
        BattleRules.Seed(battle, RandomUInt64(), RandomUInt64());

        try
        {
            using var tx = Begin(connection, immediate);
            BattleStore.Execute(connection, tx,
                "INSERT INTO battles(id,start_key,state_json,version,created_at) VALUES($id,$key,$state,0,$now)",
                ("$id", battle.Id), ("$key", idempotencyKey), ("$state", JsonSerializer.Serialize(battle)),
                ("$now", DateTimeOffset.UtcNow.ToString("O")));
            tx.Commit();
            return Results.Created($"/api/battles/{battle.Id}", ToResponse(battle));
        }
        catch (DbException ex) when (IsUniqueViolation(ex))
        {
            // 같은 start_key를 다른 요청이 먼저 쓴 경우: 승자의 결과를 그대로 반환해 멱등성을 유지한다.
            var winner = BattleStore.ReadJson<BattleState>(connection, null,
                "SELECT state_json FROM battles WHERE start_key = $key", ("$key", idempotencyKey));
            return winner != null
                ? Results.Ok(ToResponse(winner))
                : Results.Conflict(new { error = "start_key_conflict" });
        }
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

        if (!_db.Optimistic)
        {
            await GateAsync("lock_wait_turn");
            var dbStart = Stopwatch.StartNew();
            try
            {
                using var connection = _db.Open();
                TurnAttempt attemptResult = await PlayTurnOnceAsync(connection, id, input);
                dbStart.Stop();
                _metrics.Record("db_turn", dbStart.Elapsed.TotalMicroseconds);
                return attemptResult.Result!;
            }
            finally
            {
                _db.Gate.Release();
            }
        }

        // 낙관적 락: 전역 대기는 없으므로 lock_wait은 0으로 집계하고,
        // version 충돌이나 SQLite writer 경합(SQLITE_BUSY)이 나면 지수 백오프 후 재시도한다.
        _metrics.Record("lock_wait_turn", 0);
        for (int attempt = 1; ; attempt++)
        {
            var dbStart = Stopwatch.StartNew();
            TurnAttempt attemptResult;
            try
            {
                using var connection = _db.Open();
                attemptResult = await PlayTurnOnceAsync(connection, id, input, immediate: true);
            }
            catch (DbException ex) when (IsTransientBusy(ex))
            {
                dbStart.Stop();
                _metrics.Record("db_turn", dbStart.Elapsed.TotalMicroseconds);
                _metrics.Record("db_busy_retry", dbStart.Elapsed.TotalMicroseconds);
                if (attempt >= MaxAttempts)
                    return Results.Problem(statusCode: 509, title: "db_busy_exhausted",
                        detail: $"데이터베이스 잠금 경합이 {attempt}번 반복되어 포기했습니다(code={DatabaseErrorCode(ex)})");
                await Task.Delay(RetryDelayMs(attempt));
                continue;
            }
            dbStart.Stop();
            _metrics.Record("db_turn", dbStart.Elapsed.TotalMicroseconds);

            if (!attemptResult.VersionConflict)
                return attemptResult.Result!;

            if (attempt >= MaxAttempts)
                return Results.Problem(statusCode: 509, title: "conflict_exhausted", detail: $"version 충돌이 {attempt}번 반복되어 포기했습니다");

            var backoff = Stopwatch.StartNew();
            await Task.Delay(RetryDelayMs(attempt));
            _metrics.Record("conflict_retry", backoff.Elapsed.TotalMicroseconds);
        }
    }

    /// <summary>한 턴 처리의 결과. <c>VersionConflict</c>가 true면 version 충돌이므로 호출자가 재시도한다.</summary>
    private sealed record TurnAttempt(IResult? Result, bool VersionConflict);

    /// <summary>
    /// 턴 처리 1회 시도. 동일 턴 재요청(멱등성)은 항상 같은 결과를 돌려주고,
    /// 읽은 version과 일치하는 트랜잭션만 상태를 덮어 쓴다(낙관적 락의 핵심).
    /// </summary>
    private async Task<TurnAttempt> PlayTurnOnceAsync(DbConnection connection, string id, TurnRequest input, bool immediate = false)
    {
        using var tx = Begin(connection, immediate);

        var (battle, version) = await ReadBattleWithVersionAsync(connection, tx, id);
        if (battle == null)
        {
            tx.Rollback();
            return new TurnAttempt(Results.NotFound(), false);
        }

        using (var previous = BattleStore.Command(connection, tx,
            "SELECT action,result_json FROM turns WHERE battle_id=$id AND turn=$turn",
            ("$id", id), ("$turn", input.Turn)))
        using (var reader = previous.ExecuteReader())
        {
            if (reader.Read())
            {
                string previousAction = reader.GetString(0);
                string previousResult = reader.GetString(1);
                reader.Close();
                tx.Rollback();
                if (previousAction != input.Action)
                    return new TurnAttempt(Results.Conflict(new { error = "turn_already_used" }), false);
                return new TurnAttempt(Results.Content(previousResult, "application/json"), false);
            }
        }

        if (battle.Status != "playing" || battle.Turn != input.Turn)
        {
            tx.Rollback();
            return new TurnAttempt(Results.Conflict(new { error = "turn_out_of_order", nextTurn = battle.Turn, status = battle.Status }), false);
        }

        var outcome = BattleRules.Apply(battle, input.Action);
        var result = new TurnResult(battle.Id, outcome.PlayedTurn, outcome.Action, outcome.AttackDamage, outcome.BossDamage,
            battle.HeroHp, battle.BossHp, outcome.Stage, outcome.PlayedPattern, battle.Turn, battle.Status);

        string json = JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        try
        {
            BattleStore.Execute(connection, tx,
                "INSERT INTO turns(battle_id,turn,action,result_json) VALUES($id,$turn,$action,$json)",
                ("$id", id), ("$turn", input.Turn), ("$action", input.Action), ("$json", json));
        }
        catch (DbException ex) when (IsUniqueViolation(ex))
        {
            tx.Rollback();
            return new TurnAttempt(null, true);
        }

        // The version predicate is the optimistic-lock check for both database providers.
        long updated = BattleStore.Command(connection, tx,
            "UPDATE battles SET state_json=$state, version=version+1 WHERE id=$id AND version=$v",
            ("$state", JsonSerializer.Serialize(battle)), ("$id", id), ("$v", version)).ExecuteNonQuery();
        if (updated == 0)
        {
            tx.Rollback();
            return new TurnAttempt(null, true);
        }

        tx.Commit();
        return new TurnAttempt(Results.Content(json, "application/json"), false);
    }

    /// <summary>같은 트랜잭션에서 상태와 version을 함께 읽는다(충돌 감지 기준이므로 한 번에 읽어야 한다).</summary>
    private static async Task<(BattleState? Battle, long Version)> ReadBattleWithVersionAsync(DbConnection connection, DbTransaction tx, string id)
    {
        using var command = BattleStore.Command(connection, tx,
            "SELECT state_json, version FROM battles WHERE id=$id", ("$id", id));
        using var reader = await command.ExecuteReaderAsync();
        if (!reader.Read()) return (null, 0);
        string json = reader.GetString(0);
        return (JsonSerializer.Deserialize<BattleState>(json), reader.GetInt64(1));
    }

    public async Task<IResult> RegisterScoreAsync(string id, RegisterRequest input)
    {
        string name = input.Name?.Trim() ?? "";
        if (name.Length == 0 || name.EnumerateRunes().Count() > 12 || name.Any(char.IsControl))
            return Results.BadRequest(new { error = "invalid_name" });
        if (!_db.Optimistic)
        {
            await GateAsync("lock_wait_score");
            var dbStart = Stopwatch.StartNew();
            try
            {
                using var connection = _db.Open();
                IResult result = await RegisterScoreWithRetryAsync(connection, id, name);
                dbStart.Stop();
                _metrics.Record("db_score", dbStart.Elapsed.TotalMicroseconds);
                return result;
            }
            finally
            {
                _db.Gate.Release();
            }
        }

        // 낙관적 락: scores.battle_id UNIQUE 제약이 동시 등록을 막는다.
        // 승리 후에는 전투 상태가 더 바뀌지 않으므로 version 체크는 불필요하다.
        _metrics.Record("lock_wait_score", 0);
        var scoreDbStart = Stopwatch.StartNew();
        try
        {
            using var connection = _db.Open();
            return await RegisterScoreWithRetryAsync(connection, id, name);
        }
        finally
        {
            scoreDbStart.Stop();
            _metrics.Record("db_score", scoreDbStart.Elapsed.TotalMicroseconds);
        }
    }

    private async Task<IResult> RegisterScoreWithRetryAsync(DbConnection connection, string id, string name)
    {
        for (int attempt = 1; ; attempt++)
        {
            var dbStart = Stopwatch.StartNew();
            try
            {
                return await RegisterScoreOnceAsync(connection, id, name);
            }
            catch (DbException ex) when (IsTransientBusy(ex))
            {
                dbStart.Stop();
                _metrics.Record("db_busy_retry", dbStart.Elapsed.TotalMicroseconds);
                if (attempt >= MaxAttempts)
                    return Results.Problem(statusCode: 509, title: "db_busy_exhausted",
                        detail: $"데이터베이스 잠금 경합이 {attempt}번 반복되어 포기했습니다(code={DatabaseErrorCode(ex)})");
                await Task.Delay(RetryDelayMs(attempt));
            }
        }
    }

    private static async Task<IResult> RegisterScoreOnceAsync(DbConnection connection, string id, string name)
    {
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
                tx.Rollback();
                if (reader.GetString(0) != name) return Results.Conflict(new { error = "score_already_registered" });
                return Results.Ok(new ScoreResponse(name, reader.GetInt32(1), reader.GetInt32(2), reader.GetString(3)));
            }
        }
        string now = DateTimeOffset.UtcNow.ToString("O");
        try
        {
            BattleStore.Execute(connection, tx,
                "INSERT INTO scores(battle_id,name,turns,remaining_hp,created_at) VALUES($id,$name,$turns,$hp,$now)",
                ("$id", id), ("$name", name), ("$turns", battle.Turn), ("$hp", battle.HeroHp), ("$now", now));
            tx.Commit();
            return Results.Created($"/api/battles/{id}/score", new ScoreResponse(name, battle.Turn, battle.HeroHp, now));
        }
        catch (DbException ex) when (IsUniqueViolation(ex))
        {
            tx.Rollback();
            var winner = BattleStore.ReadJson<ScoreResponse>(connection, null,
                "SELECT json_object('name',name,'turns',turns,'remaining_hp',remaining_hp,'createdAt',created_at) FROM scores WHERE battle_id=$id",
                ("$id", id));
            return winner != null
                ? Results.Ok(winner)
                : Results.Conflict(new { error = "score_already_registered" });
        }
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
