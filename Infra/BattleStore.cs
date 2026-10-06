using System.Data.Common;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using MySqlConnector;

namespace CrimsonTide.Infra;

public sealed class BattleStore(string provider, string databasePath, string? mysqlConnectionString, string lockMode, string journalMode = "wal")
{
    private readonly string normalizedProvider = provider.Trim().ToLowerInvariant();
    private readonly string requestedJournalMode = journalMode.Trim().ToLowerInvariant();
    private readonly string sqliteConnectionString = new SqliteConnectionStringBuilder
    {
        DataSource = databasePath,
        DefaultTimeout = 1,
    }.ToString();

    public string Provider => normalizedProvider;
    public SemaphoreSlim Gate { get; } = new(1, 1);
    public bool Optimistic { get; } = lockMode.Trim().Equals("optimistic", StringComparison.OrdinalIgnoreCase);
    public string JournalMode { get; private set; } = "not_applicable";

    public void Initialize()
    {
        if (normalizedProvider == "sqlite")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
            using var db = Open();
            if (requestedJournalMode == "wal")
            {
                Execute(db, null, "PRAGMA journal_mode = WAL");
                Execute(db, null, "PRAGMA synchronous = NORMAL");
            }
            else
            {
                Execute(db, null, "PRAGMA journal_mode = DELETE");
                Execute(db, null, "PRAGMA synchronous = FULL");
            }
            using (var mode = db.CreateCommand())
            {
                mode.CommandText = "PRAGMA journal_mode";
                JournalMode = mode.ExecuteScalar() as string ?? "unknown";
            }
            Execute(db, null, """
                CREATE TABLE IF NOT EXISTS battles (
                    id TEXT PRIMARY KEY, start_key TEXT NOT NULL UNIQUE,
                    state_json TEXT NOT NULL, version INTEGER NOT NULL DEFAULT 0,
                    created_at TEXT NOT NULL
                )
                """);
            Execute(db, null, """
                CREATE TABLE IF NOT EXISTS turns (
                    battle_id TEXT NOT NULL, turn INTEGER NOT NULL, action TEXT NOT NULL,
                    result_json TEXT NOT NULL, PRIMARY KEY (battle_id, turn),
                    FOREIGN KEY (battle_id) REFERENCES battles(id)
                )
                """);
            Execute(db, null, """
                CREATE TABLE IF NOT EXISTS scores (
                    battle_id TEXT PRIMARY KEY, name TEXT NOT NULL, turns INTEGER NOT NULL,
                    remaining_hp INTEGER NOT NULL, created_at TEXT NOT NULL,
                    FOREIGN KEY (battle_id) REFERENCES battles(id)
                )
                """);
            Execute(db, null, "CREATE INDEX IF NOT EXISTS scores_rank ON scores(turns, remaining_hp DESC, created_at)");
            EnsureVersionColumn(db, "PRAGMA table_info(battles)");
            return;
        }

        if (normalizedProvider != "mysql")
            throw new InvalidOperationException($"지원하지 않는 BattleStore provider입니다: {provider}");
        if (string.IsNullOrWhiteSpace(mysqlConnectionString))
            throw new InvalidOperationException("MySQL 사용 시 ConnectionStrings:BattleStore 또는 CRIMSON_MYSQL_CONNECTION_STRING이 필요합니다.");

        using var mysql = Open();
        Execute(mysql, null, """
            CREATE TABLE IF NOT EXISTS battles (
                id VARCHAR(48) NOT NULL PRIMARY KEY,
                start_key VARCHAR(128) NOT NULL UNIQUE,
                state_json LONGTEXT NOT NULL,
                version BIGINT NOT NULL DEFAULT 0,
                created_at VARCHAR(35) NOT NULL
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
            """);
        Execute(mysql, null, """
            CREATE TABLE IF NOT EXISTS turns (
                battle_id VARCHAR(48) NOT NULL,
                turn INT NOT NULL,
                action VARCHAR(16) NOT NULL,
                result_json LONGTEXT NOT NULL,
                PRIMARY KEY (battle_id, turn),
                CONSTRAINT fk_turns_battle FOREIGN KEY (battle_id) REFERENCES battles(id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
            """);
        Execute(mysql, null, """
            CREATE TABLE IF NOT EXISTS scores (
                battle_id VARCHAR(48) NOT NULL PRIMARY KEY,
                name VARCHAR(48) NOT NULL,
                turns INT NOT NULL,
                remaining_hp INT NOT NULL,
                created_at VARCHAR(35) NOT NULL,
                CONSTRAINT fk_scores_battle FOREIGN KEY (battle_id) REFERENCES battles(id),
                INDEX scores_rank (turns, remaining_hp DESC, created_at)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
            """);
        EnsureVersionColumn(mysql,
            "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'battles' AND COLUMN_NAME = 'version'");
    }

    private static void EnsureVersionColumn(DbConnection db, string columnQuery)
    {
        using var command = db.CreateCommand();
        command.CommandText = columnQuery;
        bool hasVersion;
        if (db is SqliteConnection)
        {
            using var reader = command.ExecuteReader();
            hasVersion = false;
            while (reader.Read())
                if (reader.GetString(1) == "version") hasVersion = true;
        }
        else
        {
            hasVersion = command.ExecuteScalar() is not null;
        }
        if (!hasVersion)
            Execute(db, null, "ALTER TABLE battles ADD COLUMN version BIGINT NOT NULL DEFAULT 0");
    }

    public DbConnection Open()
    {
        DbConnection db = normalizedProvider switch
        {
            "sqlite" => new SqliteConnection(sqliteConnectionString),
            "mysql" when !string.IsNullOrWhiteSpace(mysqlConnectionString) => new MySqlConnection(mysqlConnectionString),
            "mysql" => throw new InvalidOperationException("MySQL 연결 문자열이 설정되지 않았습니다."),
            _ => throw new InvalidOperationException($"지원하지 않는 BattleStore provider입니다: {provider}")
        };
        db.Open();
        if (db is SqliteConnection)
        {
            using var pragma = db.CreateCommand();
            pragma.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 1000;";
            pragma.ExecuteNonQuery();
        }
        return db;
    }

    public static void Execute(DbConnection db, DbTransaction? tx, string sql, params (string Name, object Value)[] values)
    {
        using var cmd = Command(db, tx, sql, values);
        cmd.ExecuteNonQuery();
    }

    public static DbCommand Command(DbConnection db, DbTransaction? tx, string sql, params (string Name, object Value)[] values)
    {
        var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql.Replace('$', '@');
        foreach (var (name, value) in values)
        {
            var parameter = cmd.CreateParameter();
            parameter.ParameterName = "@" + name.TrimStart('$', '@');
            parameter.Value = value;
            cmd.Parameters.Add(parameter);
        }
        return cmd;
    }

    public static T? ReadJson<T>(DbConnection db, DbTransaction? tx, string sql, params (string Name, object Value)[] values)
    {
        using var cmd = Command(db, tx, sql, values);
        object? raw = cmd.ExecuteScalar();
        return raw is string json ? JsonSerializer.Deserialize<T>(json) : default;
    }
}
