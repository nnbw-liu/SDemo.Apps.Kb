using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using SDemo.App.Kb.Contracts;

namespace SDemo.App.Kb.Storage;

/// <summary>
/// 一个短连接 + 可选事务。所有 SQL 都在这里；领域规则（版本链、diff、路由）在 Core。
/// 复合写入必须走事务：文档、版本、块边、变更事实要么一起成功，要么一起回滚。
/// </summary>
public sealed class KbSession : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SqliteTransaction? _transaction;

    internal KbSession(KbDatabase database, bool write)
    {
        _connection = database.Open();
        if (write)
        {
            _transaction = _connection.BeginTransaction();
        }
    }

    public void Commit()
    {
        _transaction?.Commit();
    }

    public void Dispose()
    {
        _transaction?.Dispose();
        _connection.Dispose();
    }

    // ---------------------------------------------------------------- 文档

    public DocRow? GetDoc(string docId, bool includeDeleted = false)
    {
        using var command = Command(
            """
            SELECT id, kind, title, owner_app, entity_uri, uri, retention, derived_state,
                   current_version_id, created_at, updated_at, deleted_at, expires_at, purged_at
            FROM kb_doc WHERE id = $id
            """,
            ("$id", docId));

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        var row = ReadDoc(reader);
        return includeDeleted || !row.IsDeleted ? row : null;
    }

    public DocRow? FindDocByEntity(string ownerApp, string entityUri)
    {
        using var command = Command(
            """
            SELECT id, kind, title, owner_app, entity_uri, uri, retention, derived_state,
                   current_version_id, created_at, updated_at, deleted_at, expires_at, purged_at
            FROM kb_doc
            WHERE owner_app = $owner AND entity_uri = $entity
            ORDER BY created_at ASC
            LIMIT 1
            """,
            ("$owner", ownerApp),
            ("$entity", entityUri));

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadDoc(reader) : null;
    }

    public void InsertDoc(DocRow row) => Execute(
        """
        INSERT INTO kb_doc (id, kind, title, owner_app, entity_uri, uri, retention, derived_state,
                            current_version_id, created_at, updated_at, deleted_at, expires_at, purged_at)
        VALUES ($id, $kind, $title, $owner, $entity, $uri, $retention, $derived,
                $current, $created, $updated, $deleted, $expires, $purged)
        """,
        ("$id", row.Id),
        ("$kind", row.Kind),
        ("$title", row.Title),
        ("$owner", row.OwnerApp),
        ("$entity", row.EntityUri),
        ("$uri", row.Uri),
        ("$retention", row.Retention),
        ("$derived", row.DerivedState),
        ("$current", row.CurrentVersionId),
        ("$created", Format(row.CreatedAt)),
        ("$updated", Format(row.UpdatedAt)),
        ("$deleted", row.DeletedAt is { } deleted ? Format(deleted) : null),
        ("$expires", row.ExpiresAt is { } expires ? Format(expires) : null),
        ("$purged", row.PurgedAt is { } purged ? Format(purged) : null));

    public void SetCurrentVersion(string docId, string versionId, DateTimeOffset updatedAt) => Execute(
        "UPDATE kb_doc SET current_version_id = $version, updated_at = $updated WHERE id = $id",
        ("$version", versionId),
        ("$updated", Format(updatedAt)),
        ("$id", docId));

    public void SetRetention(string docId, string retention, DateTimeOffset updatedAt) => Execute(
        "UPDATE kb_doc SET retention = $retention, updated_at = $updated WHERE id = $id",
        ("$retention", retention),
        ("$updated", Format(updatedAt)),
        ("$id", docId));

    public void SetDerivedState(string docId, string derivedState) => Execute(
        "UPDATE kb_doc SET derived_state = $state WHERE id = $id",
        ("$state", derivedState),
        ("$id", docId));

    public void SetDeleted(string docId, DateTimeOffset deletedAt) => Execute(
        "UPDATE kb_doc SET deleted_at = $at, updated_at = $at WHERE id = $id AND deleted_at IS NULL",
        ("$at", Format(deletedAt)),
        ("$id", docId));

    public void RestoreDoc(string docId) => Execute(
        "UPDATE kb_doc SET deleted_at = NULL WHERE id = $id",
        ("$id", docId));

    public void SetExpiry(string docId, DateTimeOffset? expiresAt) => Execute(
        "UPDATE kb_doc SET expires_at = $expires WHERE id = $id",
        ("$expires", expiresAt is { } value ? Format(value) : null),
        ("$id", docId));

    /// <summary>回收站到期后的墓碑标记：保留 doc 行让旧引用仍能 resolve 成「已删除」。</summary>
    public void MarkPurged(string docId, DateTimeOffset at) => Execute(
        "UPDATE kb_doc SET purged_at = $at, current_version_id = NULL WHERE id = $id",
        ("$at", Format(at)),
        ("$id", docId));

    public IReadOnlyList<DocRow> ListExpired(DateTimeOffset now)
    {
        using var command = Command(
            """
            SELECT id, kind, title, owner_app, entity_uri, uri, retention, derived_state,
                   current_version_id, created_at, updated_at, deleted_at, expires_at, purged_at
            FROM kb_doc
            WHERE deleted_at IS NULL AND purged_at IS NULL
              AND expires_at IS NOT NULL AND expires_at <= $now
            ORDER BY expires_at ASC
            """,
            ("$now", Format(now)));

        using var reader = command.ExecuteReader();
        var rows = new List<DocRow>();
        while (reader.Read())
        {
            rows.Add(ReadDoc(reader));
        }

        return rows;
    }

    public IReadOnlyList<DocRow> ListPurgeable(DateTimeOffset deletedBefore)
    {
        using var command = Command(
            """
            SELECT id, kind, title, owner_app, entity_uri, uri, retention, derived_state,
                   current_version_id, created_at, updated_at, deleted_at, expires_at, purged_at
            FROM kb_doc
            WHERE deleted_at IS NOT NULL AND purged_at IS NULL AND deleted_at <= $before
            ORDER BY deleted_at ASC
            """,
            ("$before", Format(deletedBefore)));

        using var reader = command.ExecuteReader();
        var rows = new List<DocRow>();
        while (reader.Read())
        {
            rows.Add(ReadDoc(reader));
        }

        return rows;
    }

    public int PurgeDocVersions(string docId)
    {
        Execute(
            "DELETE FROM kb_block_edge WHERE version_id IN (SELECT id FROM kb_version WHERE doc_id = $doc)",
            ("$doc", docId));

        using var command = Command("DELETE FROM kb_version WHERE doc_id = $doc", ("$doc", docId));
        return command.ExecuteNonQuery();
    }

    public int DeleteAnnotations(string docId) => Execute(
        "DELETE FROM kb_annotation WHERE doc_id = $doc",
        ("$doc", docId));

    public int DeleteBlobRefs(string docId) => Execute(
        "DELETE FROM kb_blob_ref WHERE doc_id = $doc",
        ("$doc", docId));

    /// <summary>块 GC：没有任何版本边、也没有被引用指向的块才回收；引用保护是硬底线。</summary>
    public int CollectUnreferencedBlocks()
    {
        var ids = new List<string>();
        using (var command = Command(
            """
            SELECT b.id FROM kb_block b
            WHERE NOT EXISTS (SELECT 1 FROM kb_block_edge e WHERE e.block_id = b.id)
              AND NOT EXISTS (SELECT 1 FROM kb_link l WHERE l.target_block_id = b.id)
            """))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                ids.Add(reader.GetString(0));
            }
        }

        foreach (var id in ids)
        {
            Execute("DELETE FROM kb_fts WHERE block_id = $id", ("$id", id));
            Execute("DELETE FROM kb_block WHERE id = $id", ("$id", id));
        }

        return ids.Count;
    }

    public IReadOnlyList<BlobRow> CollectUnreferencedBlobs()
    {
        var rows = new List<BlobRow>();
        using (var command = Command(
            """
            SELECT b.sha256, b.mime, b.size, b.name, b.stored_path
            FROM kb_blob b
            WHERE NOT EXISTS (SELECT 1 FROM kb_blob_ref r WHERE r.blob_sha = b.sha256)
            """))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                rows.Add(new BlobRow(
                    reader.GetString(0),
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.GetInt64(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4)));
            }
        }

        foreach (var row in rows)
        {
            Execute("DELETE FROM kb_blob WHERE sha256 = $sha", ("$sha", row.Sha256));
        }

        return rows;
    }

    public IReadOnlyList<InboxRow> ListStaging(int limit)
    {
        using var command = Command(
            """
            SELECT d.id, d.kind, d.title, d.owner_app, d.entity_uri, d.uri, d.retention, d.derived_state,
                   d.current_version_id, d.created_at, d.updated_at, d.deleted_at, d.expires_at, d.purged_at,
                   COALESCE(v.seq, 0),
                   (SELECT COUNT(*) FROM kb_block_edge e WHERE e.version_id = d.current_version_id),
                   (SELECT COALESCE(SUM(LENGTH(b.text)), 0)
                      FROM kb_block_edge e JOIN kb_block b ON b.id = e.block_id
                     WHERE e.version_id = d.current_version_id),
                   (SELECT COALESCE(SUM(bl.size), 0)
                      FROM kb_blob_ref r JOIN kb_blob bl ON bl.sha256 = r.blob_sha
                     WHERE r.doc_id = d.id)
            FROM kb_doc d
            LEFT JOIN kb_version v ON v.id = d.current_version_id
            WHERE d.deleted_at IS NULL AND d.retention = 'staging' AND d.purged_at IS NULL
            ORDER BY CASE WHEN d.expires_at IS NULL THEN 1 ELSE 0 END, d.expires_at ASC, d.updated_at DESC
            LIMIT $limit
            """,
            ("$limit", Math.Clamp(limit, 1, 500)));

        using var reader = command.ExecuteReader();
        var rows = new List<InboxRow>();
        while (reader.Read())
        {
            rows.Add(new InboxRow(
                ReadDoc(reader),
                reader.GetInt32(14),
                reader.GetInt32(15),
                reader.GetInt64(16),
                reader.GetInt64(17)));
        }

        return rows;
    }

    public int CountStaging()
    {
        using var command = Command("SELECT COUNT(*) FROM kb_doc WHERE deleted_at IS NULL AND retention = 'staging'");
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public int CountExpiringSoon(DateTimeOffset threshold)
    {
        using var command = Command(
            """
            SELECT COUNT(*) FROM kb_doc
            WHERE deleted_at IS NULL AND retention = 'staging'
              AND expires_at IS NOT NULL AND expires_at <= $threshold
            """,
            ("$threshold", Format(threshold)));
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public StorageCounters GetCounters()
    {
        using var command = Command(
            """
            SELECT
              (SELECT COUNT(*) FROM kb_doc WHERE deleted_at IS NULL),
              (SELECT COUNT(*) FROM kb_doc WHERE deleted_at IS NULL AND retention = 'staging'),
              (SELECT COUNT(*) FROM kb_doc WHERE deleted_at IS NOT NULL AND purged_at IS NULL),
              (SELECT COUNT(*) FROM kb_block),
              (SELECT COUNT(*) FROM kb_version),
              (SELECT COALESCE(SUM(LENGTH(text)), 0) FROM kb_block),
              (SELECT COALESCE(SUM(size), 0) FROM kb_blob),
              (SELECT COALESCE(SUM(b.size), 0) FROM kb_blob b
                WHERE NOT EXISTS (SELECT 1 FROM kb_blob_ref r WHERE r.blob_sha = b.sha256))
            """);

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return new StorageCounters(0, 0, 0, 0, 0, 0, 0, 0);
        }

        return new StorageCounters(
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetInt32(3),
            reader.GetInt32(4),
            reader.GetInt64(5),
            reader.GetInt64(6),
            reader.GetInt64(7));
    }

    public void UpdateTitle(string docId, string title, DateTimeOffset updatedAt) => Execute(
        "UPDATE kb_doc SET title = $title, updated_at = $updated WHERE id = $id",
        ("$title", title),
        ("$updated", Format(updatedAt)),
        ("$id", docId));

    public IReadOnlyList<DocRow> ListDocs(bool includeDeleted = false, int limit = 500)
    {
        using var command = Command(
            $"""
            SELECT id, kind, title, owner_app, entity_uri, uri, retention, derived_state,
                   current_version_id, created_at, updated_at, deleted_at, expires_at, purged_at
            FROM kb_doc
            {(includeDeleted ? string.Empty : "WHERE deleted_at IS NULL")}
            ORDER BY updated_at DESC
            LIMIT $limit
            """,
            ("$limit", limit));

        using var reader = command.ExecuteReader();
        var rows = new List<DocRow>();
        while (reader.Read())
        {
            rows.Add(ReadDoc(reader));
        }

        return rows;
    }

    /// <summary>列表页：摘要 + 引用数 + 标题/正文过滤。查询走 LIKE，规模在万级以内足够。</summary>
    public DocListRows ListDocuments(
        string? query,
        string? kind,
        string? retention,
        bool includeDeleted,
        int limit)
    {
        var where = new StringBuilder("WHERE 1 = 1");
        var parameters = new List<(string, object?)>();

        if (!includeDeleted)
        {
            where.Append(" AND d.deleted_at IS NULL");
        }

        if (!string.IsNullOrWhiteSpace(kind))
        {
            where.Append(" AND d.kind = $kind");
            parameters.Add(("$kind", kind));
        }

        if (!string.IsNullOrWhiteSpace(retention))
        {
            where.Append(" AND d.retention = $retention");
            parameters.Add(("$retention", retention));
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            where.Append(
                """
                 AND (
                   d.title LIKE $q ESCAPE '\'
                   OR EXISTS (
                     SELECT 1 FROM kb_block_edge e JOIN kb_block b ON b.id = e.block_id
                     WHERE e.version_id = d.current_version_id AND b.text LIKE $q ESCAPE '\'
                   )
                 )
                """);
            parameters.Add(("$q", "%" + EscapeLike(query.Trim()) + "%"));
        }

        var total = 0;
        using (var count = Command($"SELECT COUNT(*) FROM kb_doc d {where}", parameters.ToArray()))
        {
            total = Convert.ToInt32(count.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        var listParameters = new List<(string, object?)>(parameters)
        {
            ("$limit", Math.Clamp(limit, 1, 500)),
        };

        using var command = Command(
            $"""
            SELECT d.id, d.kind, d.title, d.owner_app, d.entity_uri, d.uri, d.retention, d.derived_state,
                   d.current_version_id, d.created_at, d.updated_at, d.deleted_at, d.expires_at, d.purged_at,
                   (SELECT b.text
                      FROM kb_block_edge e JOIN kb_block b ON b.id = e.block_id
                     WHERE e.version_id = d.current_version_id AND b.is_heading = 0
                     ORDER BY e.ordinal ASC LIMIT 1) AS excerpt,
                   (SELECT COUNT(*) FROM kb_link l WHERE l.target_doc_id = d.id) AS refs
            FROM kb_doc d
            {where}
            ORDER BY d.updated_at DESC
            LIMIT $limit
            """,
            listParameters.ToArray());

        using var reader = command.ExecuteReader();
        var rows = new List<DocListRow>();
        while (reader.Read())
        {
            rows.Add(new DocListRow(
                ReadDoc(reader),
                reader.IsDBNull(14) ? null : reader.GetString(14),
                reader.GetInt32(15)));
        }

        return new DocListRows(rows, total);
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    // ---------------------------------------------------------------- 版本

    public void InsertVersion(VersionRow row)
    {
        Execute(
            """
            INSERT INTO kb_version (id, doc_id, seq, parent_id, content_hash, author, reason, named_version, created_at)
            VALUES ($id, $doc, $seq, $parent, $hash, $author, $reason, $named, $created)
            """,
            ("$id", row.Id),
            ("$doc", row.DocId),
            ("$seq", row.Seq),
            ("$parent", row.ParentId),
            ("$hash", row.ContentHash),
            ("$author", row.Author),
            ("$reason", row.Reason),
            ("$named", row.NamedVersion),
            ("$created", Format(row.CreatedAt)));

        if (!string.IsNullOrWhiteSpace(row.ParentId))
        {
            InsertVersionParent(row.Id, row.ParentId, 0);
        }
    }

    public void InsertVersionParent(string versionId, string parentId, int ordinal) => Execute(
        "INSERT OR IGNORE INTO kb_version_parent (version_id, parent_id, ordinal) VALUES ($version, $parent, $ordinal)",
        ("$version", versionId),
        ("$parent", parentId),
        ("$ordinal", ordinal));

    public IReadOnlyList<string> GetParentIds(string versionId)
    {
        using var command = Command(
            "SELECT parent_id FROM kb_version_parent WHERE version_id = $version ORDER BY ordinal ASC",
            ("$version", versionId));

        using var reader = command.ExecuteReader();
        var parents = new List<string>();
        while (reader.Read())
        {
            parents.Add(reader.GetString(0));
        }

        return parents;
    }

    public VersionRow? GetVersionById(string versionId)
    {
        using var command = Command(
            """
            SELECT id, doc_id, seq, parent_id, content_hash, author, reason, named_version, created_at
            FROM kb_version WHERE id = $id
            """,
            ("$id", versionId));

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadVersion(reader) : null;
    }

    public VersionRow? GetVersionBySeq(string docId, int seq)
    {
        using var command = Command(
            """
            SELECT id, doc_id, seq, parent_id, content_hash, author, reason, named_version, created_at
            FROM kb_version WHERE doc_id = $doc AND seq = $seq
            """,
            ("$doc", docId),
            ("$seq", seq));

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadVersion(reader) : null;
    }

    public VersionRow? GetCurrentVersion(string docId)
    {
        using var command = Command(
            """
            SELECT v.id, v.doc_id, v.seq, v.parent_id, v.content_hash, v.author, v.reason, v.named_version, v.created_at
            FROM kb_doc d JOIN kb_version v ON v.id = d.current_version_id
            WHERE d.id = $doc
            """,
            ("$doc", docId));

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadVersion(reader) : null;
    }

    public IReadOnlyList<VersionRow> ListVersions(string docId, int limit = 100)
    {
        using var command = Command(
            """
            SELECT id, doc_id, seq, parent_id, content_hash, author, reason, named_version, created_at
            FROM kb_version WHERE doc_id = $doc
            ORDER BY seq DESC LIMIT $limit
            """,
            ("$doc", docId),
            ("$limit", limit));

        using var reader = command.ExecuteReader();
        var rows = new List<VersionRow>();
        while (reader.Read())
        {
            rows.Add(ReadVersion(reader));
        }

        return rows;
    }

    public IReadOnlyList<VersionRow> ListVersionsForPruning(string docId)
    {
        using var command = Command(
            """
            SELECT id, doc_id, seq, parent_id, content_hash, author, reason, named_version, created_at
            FROM kb_version WHERE doc_id = $doc ORDER BY seq DESC
            """,
            ("$doc", docId));

        using var reader = command.ExecuteReader();
        var rows = new List<VersionRow>();
        while (reader.Read())
        {
            rows.Add(ReadVersion(reader));
        }

        return rows;
    }

    public int GetMaxSeq(string docId)
    {
        using var command = Command(
            "SELECT COALESCE(MAX(seq), 0) FROM kb_version WHERE doc_id = $doc",
            ("$doc", docId));
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public int CountVersions(string docId)
    {
        using var command = Command(
            "SELECT COUNT(*) FROM kb_version WHERE doc_id = $doc",
            ("$doc", docId));
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    /// <summary>保留策略清理旧版本：先删变更事实之外的边，再删版本。块与变更事实保留。</summary>
    public int PruneVersions(string docId, int keep)
    {
        var versions = ListVersionsForPruning(docId);
        if (versions.Count <= keep)
        {
            return 0;
        }

        var victims = versions.Skip(keep).ToList();
        foreach (var victim in victims)
        {
            Execute("DELETE FROM kb_version_parent WHERE version_id = $id OR parent_id = $id", ("$id", victim.Id));
            Execute("DELETE FROM kb_blob_ref WHERE version_id = $id", ("$id", victim.Id));
            Execute("DELETE FROM kb_block_edge WHERE version_id = $id", ("$id", victim.Id));
            Execute("DELETE FROM kb_version WHERE id = $id", ("$id", victim.Id));
        }

        return victims.Count;
    }

    // ---------------------------------------------------------------- 块与边

    public void InsertBlock(BlockRow row) => Execute(
        """
        INSERT INTO kb_block (id, doc_id, content_hash, text, anchor_json, is_heading, heading_level, created_at)
        VALUES ($id, $doc, $hash, $text, $anchor, $heading, $level, $created)
        """,
        ("$id", row.Id),
        ("$doc", row.DocId),
        ("$hash", row.ContentHash),
        ("$text", row.Text),
        ("$anchor", row.AnchorJson),
        ("$heading", row.IsHeading ? 1 : 0),
        ("$level", row.HeadingLevel),
        ("$created", Format(row.CreatedAt)));

    public BlockRow? GetBlock(string blockId)
    {
        using var command = Command(
            """
            SELECT id, doc_id, content_hash, text, anchor_json, is_heading, heading_level, created_at
            FROM kb_block WHERE id = $id
            """,
            ("$id", blockId));

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadBlock(reader) : null;
    }

    public BlockRow? FindBlockByHash(string docId, string contentHash)
    {
        using var command = Command(
            """
            SELECT id, doc_id, content_hash, text, anchor_json, is_heading, heading_level, created_at
            FROM kb_block WHERE doc_id = $doc AND content_hash = $hash
            ORDER BY created_at ASC LIMIT 1
            """,
            ("$doc", docId),
            ("$hash", contentHash));

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadBlock(reader) : null;
    }

    public void InsertEdge(string versionId, string blockId, int ordinal) => Execute(
        "INSERT INTO kb_block_edge (version_id, block_id, ordinal) VALUES ($version, $block, $ordinal)",
        ("$version", versionId),
        ("$block", blockId),
        ("$ordinal", ordinal));

    public IReadOnlyList<BlockRow> GetVersionBlocks(string versionId)
    {
        using var command = Command(
            """
            SELECT b.id, b.doc_id, b.content_hash, b.text, b.anchor_json, b.is_heading, b.heading_level, b.created_at
            FROM kb_block_edge e JOIN kb_block b ON b.id = e.block_id
            WHERE e.version_id = $version
            ORDER BY e.ordinal ASC
            """,
            ("$version", versionId));

        using var reader = command.ExecuteReader();
        var rows = new List<BlockRow>();
        while (reader.Read())
        {
            rows.Add(ReadBlock(reader));
        }

        return rows;
    }

    public IReadOnlyList<BlockRow> ListBlocksForDoc(string docId)
    {
        using var command = Command(
            """
            SELECT id, doc_id, content_hash, text, anchor_json, is_heading, heading_level, created_at
            FROM kb_block WHERE doc_id = $doc ORDER BY created_at ASC
            """,
            ("$doc", docId));

        using var reader = command.ExecuteReader();
        var rows = new List<BlockRow>();
        while (reader.Read())
        {
            rows.Add(ReadBlock(reader));
        }

        return rows;
    }

    public bool BlockExistsInVersion(string versionId, string blockId)
    {
        using var command = Command(
            "SELECT 1 FROM kb_block_edge WHERE version_id = $version AND block_id = $block LIMIT 1",
            ("$version", versionId),
            ("$block", blockId));
        return command.ExecuteScalar() is not null;
    }

    public int CountBlocksInVersion(string versionId)
    {
        using var command = Command(
            "SELECT COUNT(*) FROM kb_block_edge WHERE version_id = $version",
            ("$version", versionId));
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    // ---------------------------------------------------------------- 变更

    public void InsertChange(ChangeRow row) => Execute(
        """
        INSERT INTO kb_change (id, doc_id, version_id, op, block_id, before_text, after_text,
                               rationale, rationale_source, created_at)
        VALUES ($id, $doc, $version, $op, $block, $before, $after, $rationale, $source, $created)
        """,
        ("$id", row.Id),
        ("$doc", row.DocId),
        ("$version", row.VersionId),
        ("$op", row.Op),
        ("$block", row.BlockId),
        ("$before", row.BeforeText),
        ("$after", row.AfterText),
        ("$rationale", row.Rationale),
        ("$source", row.RationaleSource),
        ("$created", Format(row.CreatedAt)));

    public IReadOnlyList<ChangeRow> ListChanges(string? docId, DateTimeOffset? since, int limit)
    {
        var where = new StringBuilder("WHERE 1 = 1");
        var parameters = new List<(string, object?)>();

        if (!string.IsNullOrEmpty(docId))
        {
            where.Append(" AND c.doc_id = $doc");
            parameters.Add(("$doc", docId));
        }

        if (since is { } from)
        {
            where.Append(" AND c.created_at >= $since");
            parameters.Add(("$since", Format(from)));
        }

        parameters.Add(("$limit", Math.Clamp(limit, 1, 200)));

        using var command = Command(
            $"""
            SELECT c.id, c.doc_id, c.version_id, c.op, c.block_id, c.before_text, c.after_text,
                   c.rationale, c.rationale_source, c.created_at
            FROM kb_change c
            {where}
            ORDER BY c.created_at DESC
            LIMIT $limit
            """,
            parameters.ToArray());

        using var reader = command.ExecuteReader();
        var rows = new List<ChangeRow>();
        while (reader.Read())
        {
            rows.Add(ReadChange(reader));
        }

        return rows;
    }

    public IReadOnlyList<ChangeRow> ListChangesForVersion(string versionId)
    {
        using var command = Command(
            """
            SELECT id, doc_id, version_id, op, block_id, before_text, after_text,
                   rationale, rationale_source, created_at
            FROM kb_change WHERE version_id = $version ORDER BY rowid ASC
            """,
            ("$version", versionId));

        using var reader = command.ExecuteReader();
        var rows = new List<ChangeRow>();
        while (reader.Read())
        {
            rows.Add(ReadChange(reader));
        }

        return rows;
    }

    // ---------------------------------------------------------------- 引用

    public void ReplaceOutboundLinks(
        string docId,
        string fromUri,
        string ownerApp,
        IReadOnlyList<LinkRow> links,
        DateTimeOffset updatedAt)
    {
        Execute("DELETE FROM kb_link WHERE doc_id = $doc", ("$doc", docId));

        foreach (var link in links)
        {
            Execute(
                """
                INSERT OR REPLACE INTO kb_link
                    (id, doc_id, from_uri, to_uri, rel, owner_app, target_doc_id,
                     target_version, target_block_id, created_at, updated_at)
                VALUES ($id, $doc, $from, $to, $rel, $owner, $targetDoc, $targetVersion,
                        $targetBlock, $created, $updated)
                """,
                ("$id", link.Id),
                ("$doc", docId),
                ("$from", fromUri),
                ("$to", link.ToUri),
                ("$rel", link.Rel),
                ("$owner", link.OwnerApp),
                ("$targetDoc", link.TargetDocId),
                ("$targetVersion", link.TargetVersion),
                ("$targetBlock", link.TargetBlockId),
                ("$created", Format(link.CreatedAt)),
                ("$updated", Format(updatedAt)));
        }
    }

    public IReadOnlyList<LinkRow> ListInboundLinks(string docId)
    {
        using var command = Command(
            """
            SELECT id, doc_id, from_uri, to_uri, rel, owner_app, target_doc_id,
                   target_version, target_block_id, created_at, updated_at
            FROM kb_link WHERE target_doc_id = $doc
            ORDER BY updated_at DESC
            """,
            ("$doc", docId));
        return ReadLinks(command);
    }

    public IReadOnlyList<LinkRow> ListOutboundLinks(string docId)
    {
        using var command = Command(
            """
            SELECT id, doc_id, from_uri, to_uri, rel, owner_app, target_doc_id,
                   target_version, target_block_id, created_at, updated_at
            FROM kb_link WHERE doc_id = $doc
            ORDER BY updated_at DESC
            """,
            ("$doc", docId));
        return ReadLinks(command);
    }

    /// <summary>外部实体（todo / 会议议题 / …）登记一条指向 KB 的引用；from 不属于任何 KB 文档。</summary>
    public void UpsertExternalRef(LinkRow link) => Execute(
        """
        INSERT INTO kb_link
            (id, doc_id, from_uri, to_uri, rel, owner_app, target_doc_id,
             target_version, target_block_id, created_at, updated_at)
        VALUES ($id, '', $from, $to, $rel, $owner, $targetDoc, $targetVersion, $targetBlock, $created, $updated)
        ON CONFLICT(from_uri, to_uri, rel) DO UPDATE SET
            owner_app = excluded.owner_app,
            target_doc_id = excluded.target_doc_id,
            target_version = excluded.target_version,
            target_block_id = excluded.target_block_id,
            updated_at = excluded.updated_at
        """,
        ("$id", link.Id),
        ("$from", link.FromUri),
        ("$to", link.ToUri),
        ("$rel", link.Rel),
        ("$owner", link.OwnerApp),
        ("$targetDoc", link.TargetDocId),
        ("$targetVersion", link.TargetVersion),
        ("$targetBlock", link.TargetBlockId),
        ("$created", Format(link.CreatedAt)),
        ("$updated", Format(link.UpdatedAt)));

    public bool ExistsExternalRef(string fromUri, string toUri, string rel)
    {
        using var command = Command(
            """
            SELECT 1 FROM kb_link
            WHERE doc_id = '' AND from_uri = $from AND to_uri = $to AND rel = $rel
            LIMIT 1
            """,
            ("$from", fromUri),
            ("$to", toUri),
            ("$rel", rel));
        return command.ExecuteScalar() is not null;
    }

    public int DeleteExternalRefs(string fromUri, string ownerApp, string? toUri, string? rel)
    {
        var where = new StringBuilder("DELETE FROM kb_link WHERE doc_id = '' AND from_uri = $from AND owner_app = $owner");
        var parameters = new List<(string, object?)>
        {
            ("$from", fromUri),
            ("$owner", ownerApp),
        };

        if (!string.IsNullOrWhiteSpace(toUri))
        {
            where.Append(" AND to_uri = $to");
            parameters.Add(("$to", toUri));
        }

        if (!string.IsNullOrWhiteSpace(rel))
        {
            where.Append(" AND rel = $rel");
            parameters.Add(("$rel", rel));
        }

        return Execute(where.ToString(), parameters.ToArray());
    }

    public IReadOnlyList<LinkRow> ListExternalRefs(string fromUri, string? ownerApp)
    {
        var where = new StringBuilder("WHERE doc_id = '' AND from_uri = $from");
        var parameters = new List<(string, object?)> { ("$from", fromUri) };

        if (!string.IsNullOrWhiteSpace(ownerApp))
        {
            where.Append(" AND owner_app = $owner");
            parameters.Add(("$owner", ownerApp));
        }

        using var command = Command(
            $"""
            SELECT id, doc_id, from_uri, to_uri, rel, owner_app, target_doc_id,
                   target_version, target_block_id, created_at, updated_at
            FROM kb_link {where}
            ORDER BY updated_at DESC
            """,
            parameters.ToArray());
        return ReadLinks(command);
    }

    public int CountInboundLinks(string docId)
    {
        using var command = Command(
            "SELECT COUNT(*) FROM kb_link WHERE target_doc_id = $doc",
            ("$doc", docId));
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public bool IsBlockReferenced(string blockId)
    {
        using var command = Command(
            "SELECT 1 FROM kb_link WHERE target_block_id = $block LIMIT 1",
            ("$block", blockId));
        return command.ExecuteScalar() is not null;
    }

    // ---------------------------------------------------------------- 索引

    public void IndexBlock(string blockId, string title, string text)
    {
        Execute("DELETE FROM kb_fts WHERE block_id = $block", ("$block", blockId));
        Execute(
            "INSERT INTO kb_fts (block_id, terms) VALUES ($block, $terms)",
            ("$block", blockId),
            ("$terms", KbTokenizer.BuildWeightedTerms(title, text, KbConstants.TitleTermWeight)));
    }

    public void IndexChange(ChangeRow change)
    {
        Execute("DELETE FROM kb_change_fts WHERE change_id = $id", ("$id", change.Id));
        var text = string.Join('\n', change.Rationale, change.BeforeText, change.AfterText);
        Execute(
            "INSERT INTO kb_change_fts (change_id, terms) VALUES ($id, $terms)",
            ("$id", change.Id),
            ("$terms", KbTokenizer.BuildTerms(text, distinct: true)));
    }

    /// <summary>整库重建索引（自愈入口）。</summary>
    public void RebuildIndexes()
    {
        Execute("DELETE FROM kb_fts");
        Execute("DELETE FROM kb_change_fts");

        using (var command = Command(
            """
            SELECT b.id, d.title, b.text
            FROM kb_block b JOIN kb_doc d ON d.id = b.doc_id
            """))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                IndexBlock(reader.GetString(0), reader.GetString(1), reader.GetString(2));
            }
        }

        using (var command = Command(
            """
            SELECT id, doc_id, version_id, op, block_id, before_text, after_text,
                   rationale, rationale_source, created_at
            FROM kb_change
            """))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                IndexChange(ReadChange(reader));
            }
        }
    }

    // ---------------------------------------------------------------- 检索

    /// <summary>A 面（current）或 B 面（history）。<paramref name="matchQuery"/> 必须是已加引号的 FTS 表达式。</summary>
    public IReadOnlyList<SearchRow> SearchBlocks(
        string matchQuery,
        string face,
        KbSearchFilter filter,
        bool includeStaging,
        int limit)
    {
        var where = new StringBuilder("WHERE kb_fts MATCH $q AND d.deleted_at IS NULL AND d.derived_state = 'ready'");
        var parameters = new List<(string, object?)>
        {
            ("$q", matchQuery),
            ("$limit", Math.Clamp(limit, 1, KbConstants.MaxSearchLimit)),
        };

        if (face == "history")
        {
            where.Append(" AND e.version_id <> d.current_version_id");
            where.Append(" AND NOT EXISTS (SELECT 1 FROM kb_block_edge ce WHERE ce.version_id = d.current_version_id AND ce.block_id = b.id)");
            where.Append(" AND e.version_id = (SELECT e2.version_id FROM kb_block_edge e2 JOIN kb_version v2 ON v2.id = e2.version_id WHERE e2.block_id = b.id AND v2.id <> d.current_version_id ORDER BY v2.seq DESC LIMIT 1)");
        }
        else
        {
            where.Append(" AND e.version_id = d.current_version_id");
            if (!includeStaging && filter.Retention is null)
            {
                where.Append(" AND d.retention = 'durable'");
            }
        }

        AppendFilter(where, parameters, filter);

        using var command = Command(
            $"""
            SELECT b.id, b.doc_id, d.title, d.kind, d.retention, v.seq, v.id, b.text,
                   bm25(kb_fts) AS rank, d.updated_at, v.created_at
            FROM kb_fts
            JOIN kb_block b ON b.id = kb_fts.block_id
            JOIN kb_block_edge e ON e.block_id = b.id
            JOIN kb_version v ON v.id = e.version_id
            JOIN kb_doc d ON d.id = b.doc_id
            {where}
            ORDER BY rank ASC
            LIMIT $limit
            """,
            parameters.ToArray());

        using var reader = command.ExecuteReader();
        var rows = new List<SearchRow>();
        while (reader.Read())
        {
            rows.Add(new SearchRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetInt32(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetDouble(8),
                ParseDate(reader.GetString(9)),
                face,
                null,
                ParseDate(reader.GetString(10))));
        }

        return rows;
    }

    /// <summary>C 面：变更事实 + rationale。</summary>
    public IReadOnlyList<ChangeSearchRow> SearchChanges(
        string matchQuery,
        KbSearchFilter filter,
        int limit)
    {
        var where = new StringBuilder("WHERE kb_change_fts MATCH $q AND d.deleted_at IS NULL AND d.derived_state = 'ready'");
        var parameters = new List<(string, object?)>
        {
            ("$q", matchQuery),
            ("$limit", Math.Clamp(limit, 1, KbConstants.MaxSearchLimit)),
        };

        AppendFilter(where, parameters, filter);

        using var command = Command(
            $"""
            SELECT c.id, c.doc_id, d.title, d.kind, d.retention, COALESCE(v.seq, 0) AS seq, c.op, c.block_id,
                   c.before_text, c.after_text, c.rationale, c.rationale_source, c.created_at,
                   bm25(kb_change_fts) AS rank
            FROM kb_change_fts
            JOIN kb_change c ON c.id = kb_change_fts.change_id
            JOIN kb_doc d ON d.id = c.doc_id
            LEFT JOIN kb_version v ON v.id = c.version_id
            {where}
            ORDER BY rank ASC
            LIMIT $limit
            """,
            parameters.ToArray());

        using var reader = command.ExecuteReader();
        var rows = new List<ChangeSearchRow>();
        while (reader.Read())
        {
            rows.Add(new ChangeSearchRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetInt32(5),
                reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetString(11),
                ParseDate(reader.GetString(12)),
                reader.GetDouble(13)));
        }

        return rows;
    }

    private static void AppendFilter(
        StringBuilder where,
        List<(string, object?)> parameters,
        KbSearchFilter filter)
    {
        if (filter.Kind is { } kind)
        {
            where.Append(" AND d.kind = $kind");
            parameters.Add(("$kind", kind.ToString().ToLowerInvariant()));
        }

        if (filter.Retention is { } retention)
        {
            where.Append(" AND d.retention = $retention");
            parameters.Add(("$retention", retention.ToString().ToLowerInvariant()));
        }

        if (!string.IsNullOrEmpty(filter.DocId))
        {
            where.Append(" AND d.id = $doc");
            parameters.Add(("$doc", filter.DocId));
        }

        if (!string.IsNullOrEmpty(filter.OwnerApp))
        {
            where.Append(" AND d.owner_app = $owner");
            parameters.Add(("$owner", filter.OwnerApp));
        }

        if (filter.Since is { } since)
        {
            where.Append(" AND v.created_at >= $since");
            parameters.Add(("$since", Format(since)));
        }

        if (filter.Until is { } until)
        {
            where.Append(" AND v.created_at <= $until");
            parameters.Add(("$until", Format(until)));
        }
    }

    // ---------------------------------------------------------------- 批注 / 审计 / token

    public void UpsertAnnotation(string docId, string versionId, string summary, string author, DateTimeOffset at) => Execute(
        """
        INSERT INTO kb_annotation (version_id, doc_id, summary, author, created_at)
        VALUES ($version, $doc, $summary, $author, $at)
        ON CONFLICT(version_id) DO UPDATE SET summary = excluded.summary,
                                             author = excluded.author,
                                             created_at = excluded.created_at
        """,
        ("$version", versionId),
        ("$doc", docId),
        ("$summary", summary),
        ("$author", author),
        ("$at", Format(at)));

    public string? GetAnnotation(string versionId)
    {
        using var command = Command(
            "SELECT summary FROM kb_annotation WHERE version_id = $version",
            ("$version", versionId));
        return command.ExecuteScalar() as string;
    }

    public void WriteAudit(string actor, string action, string? docId, string? detail) => Execute(
        "INSERT INTO kb_audit (at, actor, action, doc_id, detail) VALUES ($at, $actor, $action, $doc, $detail)",
        ("$at", Format(DateTimeOffset.UtcNow)),
        ("$actor", actor),
        ("$action", action),
        ("$doc", docId),
        ("$detail", detail));

    public string? GetMeta(string key)
    {
        using var command = Command("SELECT value FROM kb_meta WHERE key = $key", ("$key", key));
        return command.ExecuteScalar() as string;
    }

    public void SetMeta(string key, string value) => Execute(
        """
        INSERT INTO kb_meta (key, value) VALUES ($key, $value)
        ON CONFLICT(key) DO UPDATE SET value = excluded.value
        """,
        ("$key", key),
        ("$value", value));

    public void UpsertBlob(string sha256, string? mime, long size, string? name, string? storedPath, DateTimeOffset at) => Execute(
        """
        INSERT INTO kb_blob (sha256, mime, size, name, stored_path, created_at)
        VALUES ($sha, $mime, $size, $name, $path, $at)
        ON CONFLICT(sha256) DO UPDATE SET mime = COALESCE(excluded.mime, kb_blob.mime),
                                          name = COALESCE(excluded.name, kb_blob.name),
                                          stored_path = COALESCE(excluded.stored_path, kb_blob.stored_path)
        """,
        ("$sha", sha256),
        ("$mime", mime),
        ("$size", size),
        ("$name", name),
        ("$path", storedPath),
        ("$at", Format(at)));

    public void AddBlobRef(string sha256, string docId, string versionId) => Execute(
        "INSERT OR IGNORE INTO kb_blob_ref (blob_sha, doc_id, version_id) VALUES ($sha, $doc, $version)",
        ("$sha", sha256),
        ("$doc", docId),
        ("$version", versionId));

    public IReadOnlyList<BlobRow> ListBlobsForDoc(string docId)
    {
        using var command = Command(
            """
            SELECT b.sha256, b.mime, b.size, b.name, b.stored_path
            FROM kb_blob_ref r JOIN kb_blob b ON b.sha256 = r.blob_sha
            WHERE r.doc_id = $doc
            GROUP BY b.sha256
            ORDER BY b.size DESC
            """,
            ("$doc", docId));

        using var reader = command.ExecuteReader();
        var rows = new List<BlobRow>();
        while (reader.Read())
        {
            rows.Add(new BlobRow(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.GetInt64(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4)));
        }

        return rows;
    }

    public void UpsertToken(string appId, string tokenHash, DateTimeOffset at) => Execute(
        """
        INSERT INTO kb_token (app_id, token_hash, issued_at, revoked_at)
        VALUES ($app, $hash, $at, NULL)
        ON CONFLICT(app_id) DO UPDATE SET token_hash = excluded.token_hash,
                                         issued_at = excluded.issued_at,
                                         revoked_at = NULL
        """,
        ("$app", appId),
        ("$hash", tokenHash),
        ("$at", Format(at)));

    public string? GetTokenHash(string appId)
    {
        using var command = Command(
            "SELECT token_hash FROM kb_token WHERE app_id = $app AND revoked_at IS NULL",
            ("$app", appId));
        return command.ExecuteScalar() as string;
    }

    public string? FindAppByTokenHash(string tokenHash)
    {
        using var command = Command(
            "SELECT app_id FROM kb_token WHERE token_hash = $hash AND revoked_at IS NULL LIMIT 1",
            ("$hash", tokenHash));
        return command.ExecuteScalar() as string;
    }

    public void RevokeToken(string appId) => Execute(
        "UPDATE kb_token SET revoked_at = $at WHERE app_id = $app",
        ("$at", Format(DateTimeOffset.UtcNow)),
        ("$app", appId));

    // ---------------------------------------------------------------- 基础设施

    private SqliteCommand Command(string sql, params (string Name, object? Value)[] parameters)
    {
        var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = _transaction;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return command;
    }

    private int Execute(string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = Command(sql, parameters);
        return command.ExecuteNonQuery();
    }

    private IReadOnlyList<LinkRow> ReadLinks(SqliteCommand command)
    {
        using var reader = command.ExecuteReader();
        var rows = new List<LinkRow>();
        while (reader.Read())
        {
            rows.Add(new LinkRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetInt32(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                ParseDate(reader.GetString(9)),
                ParseDate(reader.GetString(10))));
        }

        return rows;
    }

    private static DocRow ReadDoc(SqliteDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        reader.GetString(5),
        reader.GetString(6),
        reader.GetString(7),
        reader.IsDBNull(8) ? null : reader.GetString(8),
        ParseDate(reader.GetString(9)),
        ParseDate(reader.GetString(10)),
        reader.IsDBNull(11) ? null : ParseDate(reader.GetString(11)),
        reader.IsDBNull(12) ? null : ParseDate(reader.GetString(12)),
        reader.IsDBNull(13) ? null : ParseDate(reader.GetString(13)));

    private static VersionRow ReadVersion(SqliteDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        reader.GetInt32(2),
        reader.IsDBNull(3) ? null : reader.GetString(3),
        reader.GetString(4),
        reader.GetString(5),
        reader.IsDBNull(6) ? null : reader.GetString(6),
        reader.IsDBNull(7) ? null : reader.GetString(7),
        ParseDate(reader.GetString(8)));

    private static BlockRow ReadBlock(SqliteDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetString(4),
        reader.GetInt32(5) != 0,
        reader.GetInt32(6),
        ParseDate(reader.GetString(7)));

    private static ChangeRow ReadChange(SqliteDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetString(4),
        reader.IsDBNull(5) ? null : reader.GetString(5),
        reader.IsDBNull(6) ? null : reader.GetString(6),
        reader.IsDBNull(7) ? null : reader.GetString(7),
        reader.IsDBNull(8) ? null : reader.GetString(8),
        ParseDate(reader.GetString(9)));

    internal static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseDate(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}

