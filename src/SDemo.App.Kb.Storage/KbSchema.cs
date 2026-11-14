namespace SDemo.App.Kb.Storage;

using Microsoft.Data.Sqlite;

/// <summary>
/// 表结构（kb-design §2.1）。三条硬约束落在 schema 上：
/// <list type="bullet">
/// <item>版本只增不改：没有 update 版本的语句，seq 唯一；</item>
/// <item>块内容寻址：doc_id + content_hash 复用块；</item>
/// <item>变更事实永久保留：kb_change 独立于版本，不参与清理。</item>
/// </list>
/// </summary>
public static class KbSchema
{
    public const int Version = 2;

    public const string Sql = """
        CREATE TABLE IF NOT EXISTS kb_doc (
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
            deleted_at         TEXT NULL,
            expires_at         TEXT NULL,
            purged_at          TEXT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_kb_doc_kind ON kb_doc(kind);
        CREATE INDEX IF NOT EXISTS ix_kb_doc_owner ON kb_doc(owner_app, entity_uri);
        CREATE INDEX IF NOT EXISTS ix_kb_doc_deleted ON kb_doc(deleted_at);

        CREATE TABLE IF NOT EXISTS kb_version (
            id            TEXT PRIMARY KEY,
            doc_id        TEXT NOT NULL REFERENCES kb_doc(id),
            seq           INTEGER NOT NULL,
            parent_id     TEXT NULL,
            content_hash  TEXT NOT NULL,
            author        TEXT NOT NULL,
            reason        TEXT NULL,
            named_version TEXT NULL,
            created_at    TEXT NOT NULL,
            UNIQUE(doc_id, seq)
        );
        CREATE INDEX IF NOT EXISTS ix_kb_version_doc ON kb_version(doc_id, seq DESC);

        CREATE TABLE IF NOT EXISTS kb_block (
            id            TEXT PRIMARY KEY,
            doc_id        TEXT NOT NULL REFERENCES kb_doc(id),
            content_hash  TEXT NOT NULL,
            text          TEXT NOT NULL,
            anchor_json   TEXT NULL,
            is_heading    INTEGER NOT NULL DEFAULT 0,
            heading_level INTEGER NOT NULL DEFAULT 0,
            created_at    TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_kb_block_doc_hash ON kb_block(doc_id, content_hash);

        CREATE TABLE IF NOT EXISTS kb_block_edge (
            version_id TEXT NOT NULL REFERENCES kb_version(id) ON DELETE CASCADE,
            block_id   TEXT NOT NULL REFERENCES kb_block(id),
            ordinal    INTEGER NOT NULL,
            PRIMARY KEY (version_id, ordinal)
        );
        CREATE INDEX IF NOT EXISTS ix_kb_edge_block ON kb_block_edge(block_id);

        CREATE TABLE IF NOT EXISTS kb_change (
            id               TEXT PRIMARY KEY,
            doc_id           TEXT NOT NULL,
            version_id       TEXT NOT NULL,
            op               TEXT NOT NULL,
            block_id         TEXT NULL,
            before_text      TEXT NULL,
            after_text       TEXT NULL,
            rationale        TEXT NULL,
            rationale_source TEXT NULL,
            created_at       TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_kb_change_doc ON kb_change(doc_id, created_at DESC);
        CREATE INDEX IF NOT EXISTS ix_kb_change_version ON kb_change(version_id);

        CREATE TABLE IF NOT EXISTS kb_link (
            id                TEXT PRIMARY KEY,
            doc_id            TEXT NOT NULL,
            from_uri          TEXT NOT NULL,
            to_uri            TEXT NOT NULL,
            rel               TEXT NOT NULL,
            owner_app         TEXT NOT NULL,
            target_doc_id     TEXT NOT NULL,
            target_version    INTEGER NULL,
            target_block_id   TEXT NULL,
            created_at        TEXT NOT NULL,
            updated_at        TEXT NOT NULL,
            UNIQUE(from_uri, to_uri, rel)
        );
        CREATE INDEX IF NOT EXISTS ix_kb_link_target ON kb_link(target_doc_id);
        CREATE INDEX IF NOT EXISTS ix_kb_link_target_block ON kb_link(target_block_id);
        CREATE INDEX IF NOT EXISTS ix_kb_link_doc ON kb_link(doc_id);
        CREATE INDEX IF NOT EXISTS ix_kb_link_from_uri ON kb_link(from_uri);

        CREATE TABLE IF NOT EXISTS kb_blob (
            sha256      TEXT PRIMARY KEY,
            mime        TEXT NULL,
            size        INTEGER NOT NULL,
            name        TEXT NULL,
            stored_path TEXT NULL,
            created_at  TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS kb_blob_ref (
            blob_sha   TEXT NOT NULL,
            doc_id     TEXT NOT NULL,
            version_id TEXT NULL,
            PRIMARY KEY (blob_sha, doc_id, version_id)
        );

        CREATE TABLE IF NOT EXISTS kb_annotation (
            version_id  TEXT PRIMARY KEY,
            doc_id      TEXT NOT NULL,
            summary     TEXT NOT NULL,
            author      TEXT NOT NULL,
            created_at  TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS kb_token (
            app_id     TEXT PRIMARY KEY,
            token_hash TEXT NOT NULL,
            issued_at  TEXT NOT NULL,
            revoked_at TEXT NULL
        );

        CREATE TABLE IF NOT EXISTS kb_audit (
            id      INTEGER PRIMARY KEY AUTOINCREMENT,
            at      TEXT NOT NULL,
            actor   TEXT NOT NULL,
            action  TEXT NOT NULL,
            doc_id  TEXT NULL,
            detail  TEXT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_kb_audit_at ON kb_audit(at DESC);

        CREATE TABLE IF NOT EXISTS kb_meta (
            key   TEXT PRIMARY KEY,
            value TEXT NOT NULL
        );

        -- 块级 FTS：一行一个块，terms 里含「标题 ×4 + 正文」的 2-gram。
        CREATE VIRTUAL TABLE IF NOT EXISTS kb_fts USING fts5(
            block_id UNINDEXED,
            terms,
            tokenize = 'unicode61 remove_diacritics 2'
        );

        -- 变更事实 FTS：rationale / before / after 一起索引。
        CREATE VIRTUAL TABLE IF NOT EXISTS kb_change_fts USING fts5(
            change_id UNINDEXED,
            terms,
            tokenize = 'unicode61 remove_diacritics 2'
        );
        """;

    /// <summary>
    /// 在旧库上补列。P4-a 的库 user_version=1，没有 expires_at / purged_at；
    /// 不能靠 CREATE TABLE IF NOT EXISTS 补列，必须 ALTER。
    /// </summary>
    public static void Migrate(SqliteConnection connection, SqliteTransaction transaction)
    {
        var version = GetUserVersion(connection, transaction);

        if (version < 2)
        {
            AddColumnIfMissing(connection, transaction, "kb_doc", "expires_at", "TEXT NULL");
            AddColumnIfMissing(connection, transaction, "kb_doc", "purged_at", "TEXT NULL");
        }

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA user_version = " + Version + ";";
        command.ExecuteNonQuery();
    }

    private static int GetUserVersion(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void AddColumnIfMissing(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        string column,
        string definition)
    {
        using var info = connection.CreateCommand();
        info.Transaction = transaction;
        info.CommandText = $"PRAGMA table_info({table});";

        using (var reader = info.ExecuteReader())
        {
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
        }

        using var alter = connection.CreateCommand();
        alter.Transaction = transaction;
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
        alter.ExecuteNonQuery();
    }
}
