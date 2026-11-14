using System.Globalization;
using Microsoft.Data.Sqlite;
using SDemo.App.Kb.Contracts;

namespace SDemo.App.Kb.Core;

/// <summary>
/// 写入方 outbox（kb-design §3.6）：写入先落本地库，后台补投。
/// KB 离线时业务事务不失败——这是 W-3 的落点，不是可选项。
/// </summary>
public sealed class KbOutbox
{
    private readonly string _connectionString;

    public KbOutbox(string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true,
        }.ToString();

        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS kb_outbox (
                id              INTEGER PRIMARY KEY AUTOINCREMENT,
                app_id          TEXT NOT NULL,
                entity_uri      TEXT NOT NULL,
                payload         TEXT NOT NULL,
                created_at      TEXT NOT NULL,
                attempts        INTEGER NOT NULL DEFAULT 0,
                next_attempt_at TEXT NOT NULL,
                delivered_at    TEXT NULL,
                last_error      TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_kb_outbox_due ON kb_outbox(delivered_at, next_attempt_at);
            """;
        command.ExecuteNonQuery();
    }

    public long Enqueue(string appId, string entityUri, string payload, DateTimeOffset? now = null)
    {
        var at = now ?? DateTimeOffset.UtcNow;

        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO kb_outbox (app_id, entity_uri, payload, created_at, next_attempt_at)
            VALUES ($app, $entity, $payload, $at, $at);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$app", appId);
        command.Parameters.AddWithValue("$entity", entityUri ?? string.Empty);
        command.Parameters.AddWithValue("$payload", payload);
        command.Parameters.AddWithValue("$at", Format(at));

        return (long)(command.ExecuteScalar() ?? 0L);
    }

    public IReadOnlyList<OutboxItem> Due(DateTimeOffset now, int limit = 20)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, app_id, entity_uri, payload, attempts
            FROM kb_outbox
            WHERE delivered_at IS NULL AND next_attempt_at <= $now
            ORDER BY id ASC
            LIMIT $limit
            """;
        command.Parameters.AddWithValue("$now", Format(now));
        command.Parameters.AddWithValue("$limit", limit);

        using var reader = command.ExecuteReader();
        var items = new List<OutboxItem>();
        while (reader.Read())
        {
            items.Add(new OutboxItem(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt32(4)));
        }

        return items;
    }

    public void MarkDelivered(long id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE kb_outbox SET delivered_at = $at, last_error = NULL WHERE id = $id";
        command.Parameters.AddWithValue("$at", Format(DateTimeOffset.UtcNow));
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    /// <summary>指数退避，最长 7 天。</summary>
    public void MarkFailed(long id, string error, DateTimeOffset now)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE kb_outbox
            SET attempts = attempts + 1,
                last_error = $error,
                next_attempt_at = $next
            WHERE id = $id;
            """;

        var attempts = GetAttempts(connection, id) + 1;
        var delayMinutes = Math.Min(7 * 24 * 60, Math.Pow(2, Math.Min(attempts, 10)));

        command.Parameters.AddWithValue("$error", error);
        command.Parameters.AddWithValue("$next", Format(now.AddMinutes(delayMinutes)));
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public int CountPending()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM kb_outbox WHERE delivered_at IS NULL";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public int PurgeDelivered(DateTimeOffset before)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM kb_outbox WHERE delivered_at IS NOT NULL AND delivered_at < $before";
        command.Parameters.AddWithValue("$before", Format(before));
        return command.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static int GetAttempts(SqliteConnection connection, long id)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT attempts FROM kb_outbox WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        return Convert.ToInt32(command.ExecuteScalar() ?? 0, CultureInfo.InvariantCulture);
    }

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}

public sealed record OutboxItem(long Id, string AppId, string EntityUri, string Payload, int Attempts);
