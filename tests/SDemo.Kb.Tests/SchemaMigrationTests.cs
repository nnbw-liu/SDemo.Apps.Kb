using Microsoft.Data.Sqlite;
using SDemo.App.Kb.Storage;

namespace SDemo.Kb.Tests;

public sealed class SchemaMigrationTests
{
    [Fact]
    public void P4a的库能补上expires与purged列()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sdemo-kb-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var database = new KbDatabase(directory);

            using (var connection = database.Open())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    CREATE TABLE kb_doc (
                        id                 TEXT PRIMARY KEY,
                        kind               TEXT NOT NULL,
                        title              TEXT NOT NULL,
                        owner_app          TEXT NOT NULL,
                        entity_uri         TEXT NOT NULL,
                        uri                TEXT NOT NULL,
                        retention          TEXT NOT NULL,
                        derived_state      TEXT NOT NULL DEFAULT 'ready',
                        current_version_id TEXT NULL,
                        created_at         TEXT NOT NULL,
                        updated_at         TEXT NOT NULL,
                        deleted_at         TEXT NULL
                    );
                    PRAGMA user_version = 1;
                    """;
                command.ExecuteNonQuery();
            }

            _ = new KbStore(database);

            using var check = database.Open();
            using var columns = check.CreateCommand();
            columns.CommandText = "PRAGMA table_info(kb_doc);";
            var names = new List<string>();
            using (var reader = columns.ExecuteReader())
            {
                while (reader.Read())
                {
                    names.Add(reader.GetString(1));
                }
            }

            Assert.Contains("expires_at", names);
            Assert.Contains("purged_at", names);

            using var version = check.CreateCommand();
            version.CommandText = "PRAGMA user_version;";
            Assert.Equal(3, Convert.ToInt32(version.ExecuteScalar()));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // 清理失败不影响断言。
            }
        }
    }
}

