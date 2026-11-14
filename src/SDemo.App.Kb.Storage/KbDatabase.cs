using Microsoft.Data.Sqlite;

namespace SDemo.App.Kb.Storage;

/// <summary>
/// KB 的 SQLite 连接工厂。每次操作开一条短连接（WAL + 共享缓存），
/// 索引与正文在同一个库里，不额外维护一份存储。
/// </summary>
public sealed class KbDatabase
{
    private readonly string _connectionString;

    public KbDatabase(string? dataDirectory = null)
    {
        DataDirectory = dataDirectory ?? Contracts.KbPaths.DefaultDataDirectory;
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(Contracts.KbPaths.BlobsDirectory(DataDirectory));
        Directory.CreateDirectory(Contracts.KbPaths.CacheDirectory(DataDirectory));

        DatabasePath = Contracts.KbPaths.DatabaseFile(DataDirectory);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
        }.ToString();
    }

    public string DataDirectory { get; }

    public string DatabasePath { get; }

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA foreign_keys = ON;
            PRAGMA busy_timeout = 5000;
            """;
        command.ExecuteNonQuery();

        return connection;
    }

    /// <summary>幂等建表；<c>PRAGMA user_version</c> 记录 schema 版本，便于以后迁移。</summary>
    public void EnsureSchema()
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();

        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = KbSchema.Sql;
            command.ExecuteNonQuery();
        }

        KbSchema.Migrate(connection, transaction);

        transaction.Commit();
    }
}
