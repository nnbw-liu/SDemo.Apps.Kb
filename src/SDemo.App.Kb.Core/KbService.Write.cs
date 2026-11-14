using SDemo.App.Kb.Contracts;
using SDemo.App.Kb.Storage;

namespace SDemo.App.Kb.Core;

/// <summary>
/// 写路径：ingest / revise / create / update / promote / retract / import。
/// 三条硬约束在这里变成代码：
/// <list type="bullet">
/// <item>appId 由 token 注入，不接受请求自报（W-1）；</item>
/// <item>内容 hash 相同直接跳过，不产生版本洪水（W-2）；</item>
/// <item>任何一次改动只新建一个版本，块按 hash 复用（铁律 2）。</item>
/// </list>
/// </summary>
public sealed partial class KbService
{
    private readonly KbStore _store;
    private readonly KbBlobStore _blobs;
    private readonly TimeProvider _time;

    public KbService(KbStore store, KbBlobStore? blobs = null, TimeProvider? time = null)
    {
        _store = store;
        _blobs = blobs ?? new KbBlobStore(store.Database.DataDirectory);
        _time = time ?? TimeProvider.System;
    }

    public KbStore Store => _store;

    private DateTimeOffset Now => _time.GetUtcNow();

    // ---------------------------------------------------------------- 写入面

    public WriteResult Ingest(IngestRequest request, string authenticatedAppId)
    {
        ArgumentNullException.ThrowIfNull(request);

        var source = request.Source ?? throw KbException.Invalid("缺少 source。");
        if (string.IsNullOrWhiteSpace(authenticatedAppId))
        {
            throw new KbException(KbErrorCodes.UnauthorizedApp, "缺少调用方身份。");
        }

        if (!string.IsNullOrWhiteSpace(source.AppId)
            && !string.Equals(source.AppId, authenticatedAppId, StringComparison.Ordinal))
        {
            throw new KbException(
                KbErrorCodes.UnauthorizedApp,
                $"source.appId「{source.AppId}」与 token 绑定的「{authenticatedAppId}」不一致。");
        }

        source.AppId = authenticatedAppId;

        var title = ValidateTitle(request.Title);
        ValidateBlocks(request.Blocks);

        var author = AuthorFor(authenticatedAppId, request.RationaleSource, import: false);
        var retention = request.Retention ?? DefaultRetention(request.Kind, author, authenticatedAppId);
        var rationaleSource = request.RationaleSource ?? RationaleSourceFor(authenticatedAppId);
        var idempotencyKey = string.IsNullOrWhiteSpace(request.IdempotencyKey)
            ? null
            : $"ingest:{authenticatedAppId}:{request.IdempotencyKey.Trim()}";

        using var session = _store.Begin();

        if (idempotencyKey is not null && session.GetMeta(idempotencyKey) is { } cached)
        {
            return KbJson.Deserialize<WriteResult>(cached) ?? throw KbException.Invalid("幂等记录损坏。");
        }

        var now = Now;
        WriteResult result;

        var existing = string.IsNullOrWhiteSpace(source.EntityUri)
            ? null
            : session.FindDocByEntity(authenticatedAppId, source.EntityUri.Trim());

        if (existing is null)
        {
            result = CreateDocumentCore(
                session,
                authenticatedAppId,
                request,
                title,
                retention,
                author,
                rationaleSource,
                now);
        }
        else
        {
            result = RefreshProjectionCore(
                session,
                existing,
                request,
                title,
                author,
                rationaleSource,
                now);
        }

        if (idempotencyKey is not null)
        {
            session.SetMeta(idempotencyKey, KbJson.Serialize(result));
        }

        session.Commit();
        return result;
    }

    public WriteResult Revise(ReviseRequest request, string authenticatedAppId)
    {
        ArgumentNullException.ThrowIfNull(request);

        var source = request.Source ?? throw KbException.Invalid("缺少 source。");
        if (string.IsNullOrWhiteSpace(authenticatedAppId))
        {
            throw new KbException(KbErrorCodes.UnauthorizedApp, "缺少调用方身份。");
        }

        if (!string.IsNullOrWhiteSpace(source.AppId)
            && !string.Equals(source.AppId, authenticatedAppId, StringComparison.Ordinal))
        {
            throw new KbException(
                KbErrorCodes.UnauthorizedApp,
                $"source.appId「{source.AppId}」与 token 绑定的「{authenticatedAppId}」不一致。");
        }

        if (request.Blocks.Count == 0)
        {
            throw KbException.Invalid("修订必须至少包含一个块操作。");
        }

        ValidateBlocks(request.Blocks);

        using var session = _store.Begin();
        var doc = session.GetDoc(request.DocId)
            ?? throw KbException.NotFound($"文档不存在：{request.DocId}");

        EnsureCanWrite(doc, authenticatedAppId);

        var current = session.GetCurrentVersion(doc.Id)
            ?? throw KbException.NotFound($"文档没有当前版本：{doc.Id}");

        if (request.ExpectedVersion is > 0 && request.ExpectedVersion != current.Seq)
        {
            throw new KbException(
                KbErrorCodes.VersionConflict,
                $"expectedVersion={request.ExpectedVersion}，当前版本 v{current.Seq}。");
        }

        var now = Now;
        var author = AuthorFor(authenticatedAppId, request.RationaleSource, import: false);
        var rationaleSource = request.RationaleSource ?? RationaleSourceFor(authenticatedAppId);

        var result = ApplyOpsCore(
            session,
            doc,
            current,
            request.Blocks,
            request.Links,
            request.Reason,
            request.Rationale,
            rationaleSource,
            author,
            now);

        session.Commit();
        return result;
    }

    /// <summary>MCP / KB 界面入口：不带 source，owner 由服务端指定。</summary>
    public WriteResult Create(KbCreateRequest request, string ownerApp = "sdemo.agent")
    {
        ArgumentNullException.ThrowIfNull(request);

        var ingest = new IngestRequest
        {
            Source = new IngestSource { AppId = ownerApp },
            Kind = request.Kind,
            Title = request.Title,
            Retention = request.Retention,
            TtlDays = request.TtlDays,
            Rationale = request.Rationale,
            RationaleSource = ownerApp == "sdemo.kb" ? RationaleSource.Import : RationaleSource.Ai,
            Blocks = request.Blocks,
        };

        return Ingest(ingest, ownerApp);
    }

    public WriteResult Update(KbUpdateRequest request, string actorApp = "sdemo.agent")
    {
        if (request.ExpectedVersion <= 0)
        {
            throw KbException.Invalid("kb_update 必须带 expectedVersion（先 kb_get 拿当前 seq）。");
        }

        var revise = new ReviseRequest
        {
            Source = new IngestSource { AppId = actorApp },
            DocId = request.DocId,
            ExpectedVersion = request.ExpectedVersion,
            Reason = request.Reason ?? "AI 修订",
            Rationale = request.Rationale,
            RationaleSource = request.RationaleSource,
            Blocks = request.Ops,
        };

        return Revise(revise, actorApp);
    }

    public WriteResult ImportFile(
        string path,
        string? title = null,
        KbKind kind = KbKind.Reference,
        string? rationale = null)
    {
        if (!File.Exists(path))
        {
            throw KbException.Invalid($"文件不存在：{path}");
        }

        var key = Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToLowerInvariant())));

        using var stream = File.OpenRead(path);
        return ImportStream(key, Path.GetFileName(path), stream, title, kind, rationale);
    }

    /// <summary>
    /// 从界面导入：没有本机路径，只按文件名做幂等键；同名文件重新导入会按块 diff 生成新版本。
    /// </summary>
    public WriteResult ImportContent(
        string fileName,
        byte[] content,
        string? title = null,
        KbKind kind = KbKind.Reference,
        string? rationale = null)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw KbException.Invalid("文件名不能为空。");
        }

        if (content.Length == 0)
        {
            throw KbException.Invalid("文件没有可导入的内容。");
        }

        using var stream = new MemoryStream(content, writable: false);
        return ImportStream(NameKey(fileName), Path.GetFileName(fileName), stream, title, kind, rationale);
    }

    public WriteResult ImportText(
        string fileName,
        string content,
        string? title = null,
        KbKind kind = KbKind.Reference,
        string? rationale = null)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw KbException.Invalid("文件名不能为空。");
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            throw KbException.Invalid("文件没有可导入的文本。");
        }

        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        return ImportContent(fileName, bytes, title, kind, rationale);
    }

    private static string NameKey(string fileName) =>
        Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes("name:" + Path.GetFileName(fileName).ToLowerInvariant())));

    private WriteResult ImportStream(
        string key,
        string fileName,
        Stream stream,
        string? title,
        KbKind kind,
        string? rationale)
    {
        var specs = KbDocumentImporter.Extract(fileName, stream);
        if (specs.Count == 0)
        {
            throw KbException.Invalid("文件没有可导入的文本块。");
        }

        var blocks = specs
            .Select(spec => new BlockInput
            {
                Op = ChangeOp.Add,
                Text = spec.Text,
                IsHeading = spec.IsHeading,
                HeadingLevel = spec.HeadingLevel,
                Anchor = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(spec.AnchorJson!),
            })
            .ToList();

        var ingest = new IngestRequest
        {
            Source = new IngestSource
            {
                AppId = KbConstants.AppId,
                EntityUri = $"sdemo.kb/import/{key}",
            },
            Kind = kind,
            Title = title ?? Path.GetFileNameWithoutExtension(fileName),
            Retention = RetentionLevel.Durable,
            Rationale = rationale ?? $"导入 {fileName}",
            RationaleSource = RationaleSource.Import,
            Blocks = blocks,
        };

        return Ingest(ingest, KbConstants.AppId);
    }

    // ---------------------------------------------------------------- 核心写入

    private WriteResult CreateDocumentCore(
        KbSession session,
        string ownerApp,
        IngestRequest request,
        string title,
        RetentionLevel retention,
        string author,
        RationaleSource rationaleSource,
        DateTimeOffset now)
    {
        var docId = KbIds.NewDocId();
        var expiresAt = ComputeExpiry(
            retention,
            request.Kind,
            ownerApp,
            request.TtlDays,
            now,
            ReadRetentionSettings(session));

        var doc = new DocRow(
            docId,
            ToWire(request.Kind),
            title,
            ownerApp,
            request.Source.EntityUri.Trim(),
            KbUri.Doc(docId),
            ToWire(retention),
            ToWire(DerivedState.Ready),
            null,
            now,
            now,
            null,
            expiresAt,
            null);

        session.InsertDoc(doc);

        var blocks = MaterializeAll(session, doc, request.Blocks, now);
        if (blocks.Count == 0)
        {
            throw KbException.Invalid("文档至少要有一个非空块。");
        }

        var versionId = KbIds.NewVersionId();
        var hash = KbText.ContentHash(blocks.Select(block => block.ContentHash));
        var version = new VersionRow(
            versionId,
            docId,
            1,
            null,
            hash,
            author,
            request.Rationale,
            null,
            now);

        session.InsertVersion(version);

        for (var ordinal = 0; ordinal < blocks.Count; ordinal++)
        {
            session.InsertEdge(versionId, blocks[ordinal].Id, ordinal);
            var change = new ChangeRow(
                KbIds.NewDocId(),
                docId,
                versionId,
                ToWire(ChangeOp.Add),
                blocks[ordinal].Id,
                null,
                blocks[ordinal].Text,
                request.Rationale,
                ToWire(rationaleSource),
                now);

            session.InsertChange(change);
            session.IndexChange(change);
        }

        session.SetCurrentVersion(docId, versionId, now);
        ReplaceLinks(session, doc, version.Seq, request.Links, ownerApp, now);
        RecordArtifacts(session, doc, versionId, request.Artifacts, now);

        session.WriteAudit(ownerApp, "ingest.create", docId, $"kind={ToWire(request.Kind)}; blocks={blocks.Count}");
        MaybePrune(session, doc);

        return new WriteResult
        {
            DocId = docId,
            VersionId = versionId,
            Seq = 1,
            Created = true,
            CurrentVersionSeq = 1,
            BlockIds = blocks.Select(block => block.Id).ToList(),
            BlockCount = blocks.Count,
        };
    }

    private WriteResult RefreshProjectionCore(
        KbSession session,
        DocRow doc,
        IngestRequest request,
        string title,
        string author,
        RationaleSource rationaleSource,
        DateTimeOffset now)
    {
        if (!string.Equals(doc.Kind, ToWire(request.Kind), StringComparison.Ordinal))
        {
            throw KbException.Invalid($"投影不能修改 kind（原 {doc.Kind}，新 {ToWire(request.Kind)}）。");
        }

        if (doc.IsDeleted)
        {
            throw new KbException(KbErrorCodes.DocDeleted, $"文档已软删：{doc.Id}");
        }

        var current = session.GetCurrentVersion(doc.Id)
            ?? throw KbException.NotFound($"文档没有当前版本：{doc.Id}");
        var currentBlocks = session.GetVersionBlocks(current.Id);

        // P4-a 的暂存文档没有 expires_at；首次重投时补上，避免永远不过期。
        if (string.Equals(doc.Retention, ToWire(RetentionLevel.Staging), StringComparison.Ordinal)
            && doc.ExpiresAt is null)
        {
            var expires = ComputeExpiry(
                RetentionLevel.Staging,
                ParseEnum(doc.Kind, KbKind.Note),
                doc.OwnerApp,
                request.TtlDays,
                now,
                ReadRetentionSettings(session));

            session.SetExpiry(doc.Id, expires);
            doc = doc with { ExpiresAt = expires };
        }

        if (!string.Equals(doc.Title, title, StringComparison.Ordinal))
        {
            session.UpdateTitle(doc.Id, title, now);
            doc = doc with { Title = title, UpdatedAt = now };

            // 标题参与 FTS terms（×4），改名后必须重建该文档所有块的索引。
            foreach (var block in session.ListBlocksForDoc(doc.Id))
            {
                session.IndexBlock(block.Id, title, block.Text);
            }
        }

        var blocks = MaterializeAll(session, doc, request.Blocks, now);
        if (blocks.Count == 0)
        {
            throw KbException.Invalid("投影至少要有一个非空块。");
        }

        var hash = KbText.ContentHash(blocks.Select(block => block.ContentHash));
        if (string.Equals(hash, current.ContentHash, StringComparison.Ordinal))
        {
            ReplaceLinks(session, doc, current.Seq, request.Links, doc.OwnerApp, now);
            session.WriteAudit(doc.OwnerApp, "ingest.noop", doc.Id, "内容未变，跳过建版本");
            return new WriteResult
            {
                DocId = doc.Id,
                VersionId = current.Id,
                Seq = current.Seq,
                Created = false,
                Deduplicated = true,
                NoChange = true,
                CurrentVersionSeq = current.Seq,
                BlockIds = blocks.Select(block => block.Id).ToList(),
                BlockCount = blocks.Count,
            };
        }

        var version = NewVersion(session, doc, current, blocks, hash, author, request.Rationale, now);
        var diffs = KbDiff.BlockDiffs(currentBlocks, blocks);
        WriteChanges(session, doc, version.Id, diffs, request.Rationale, rationaleSource, now);

        session.SetCurrentVersion(doc.Id, version.Id, now);
        ReplaceLinks(session, doc, version.Seq, request.Links, doc.OwnerApp, now);
        RecordArtifacts(session, doc, version.Id, request.Artifacts, now);
        session.WriteAudit(doc.OwnerApp, "ingest.refresh", doc.Id, $"v{version.Seq}; blocks={blocks.Count}");
        MaybePrune(session, doc);

        return new WriteResult
        {
            DocId = doc.Id,
            VersionId = version.Id,
            Seq = version.Seq,
            Created = false,
            CurrentVersionSeq = version.Seq,
            BlockIds = blocks.Select(block => block.Id).ToList(),
            BlockCount = blocks.Count,
        };
    }

    private WriteResult ApplyOpsCore(
        KbSession session,
        DocRow doc,
        VersionRow current,
        IReadOnlyList<BlockInput> ops,
        IReadOnlyList<IngestLink> links,
        string? reason,
        string? rationale,
        RationaleSource rationaleSource,
        string author,
        DateTimeOffset now)
    {
        var before = session.GetVersionBlocks(current.Id).ToList();
        var after = new List<BlockRow>(before);
        var pending = new List<(ChangeOp Op, BlockRow? Before, BlockRow? After)>();

        foreach (var op in ops)
        {
            switch (op.Op)
            {
                case ChangeOp.Add:
                {
                    var block = MaterializeBlock(session, doc, op, now);
                    var position = op.Position ?? after.Count;
                    position = Math.Clamp(position, 0, after.Count);
                    after.Insert(position, block);
                    pending.Add((ChangeOp.Add, null, block));
                    break;
                }

                case ChangeOp.Replace:
                {
                    var index = FindIndex(after, op.BlockId);
                    if (index < 0)
                    {
                        throw new KbException(
                            KbErrorCodes.BlockNotFound,
                            $"当前版本里没有块 {op.BlockId}。");
                    }

                    var old = after[index];
                    var block = MaterializeBlock(session, doc, op, now);
                    after[index] = block;
                    pending.Add((ChangeOp.Replace, old, block));
                    break;
                }

                case ChangeOp.Remove:
                {
                    var index = FindIndex(after, op.BlockId);
                    if (index < 0)
                    {
                        throw new KbException(
                            KbErrorCodes.BlockNotFound,
                            $"当前版本里没有块 {op.BlockId}。");
                    }

                    var old = after[index];
                    after.RemoveAt(index);
                    pending.Add((ChangeOp.Remove, old, null));
                    break;
                }

                default:
                    throw KbException.Invalid($"未知块操作：{op.Op}");
            }
        }

        if (after.Count == 0)
        {
            throw KbException.Invalid("修订后文档不能为空。");
        }

        var hash = KbText.ContentHash(after.Select(block => block.ContentHash));
        if (string.Equals(hash, current.ContentHash, StringComparison.Ordinal))
        {
            return new WriteResult
            {
                DocId = doc.Id,
                VersionId = current.Id,
                Seq = current.Seq,
                Created = false,
                NoChange = true,
                CurrentVersionSeq = current.Seq,
                BlockIds = after.Select(block => block.Id).ToList(),
                BlockCount = after.Count,
            };
        }

        var version = NewVersion(session, doc, current, after, hash, author, reason ?? rationale, now);

        foreach (var (op, old, fresh) in pending)
        {
            var change = new ChangeRow(
                KbIds.NewDocId(),
                doc.Id,
                version.Id,
                ToWire(op),
                (fresh ?? old)?.Id,
                old?.Text,
                fresh?.Text,
                rationale ?? reason,
                ToWire(rationaleSource),
                now);

            session.InsertChange(change);
            session.IndexChange(change);
        }

        session.SetCurrentVersion(doc.Id, version.Id, now);
        if (links.Count > 0)
        {
            ReplaceLinks(session, doc, version.Seq, links, doc.OwnerApp, now);
        }

        session.WriteAudit(doc.OwnerApp, "revise", doc.Id, $"v{version.Seq}; ops={ops.Count}");
        MaybePrune(session, doc);

        return new WriteResult
        {
            DocId = doc.Id,
            VersionId = version.Id,
            Seq = version.Seq,
            Created = false,
            CurrentVersionSeq = version.Seq,
            BlockIds = after.Select(block => block.Id).ToList(),
            BlockCount = after.Count,
        };
    }

    private VersionRow NewVersion(
        KbSession session,
        DocRow doc,
        VersionRow current,
        IReadOnlyList<BlockRow> blocks,
        string hash,
        string author,
        string? reason,
        DateTimeOffset now)
    {
        var seq = current.Seq + 1;
        var version = new VersionRow(
            KbIds.NewVersionId(),
            doc.Id,
            seq,
            current.Id,
            hash,
            author,
            reason,
            null,
            now);

        session.InsertVersion(version);

        for (var ordinal = 0; ordinal < blocks.Count; ordinal++)
        {
            session.InsertEdge(version.Id, blocks[ordinal].Id, ordinal);
        }

        return version;
    }

    private void WriteChanges(
        KbSession session,
        DocRow doc,
        string versionId,
        IReadOnlyList<BlockDiff> diffs,
        string? rationale,
        RationaleSource rationaleSource,
        DateTimeOffset now)
    {
        foreach (var diff in diffs)
        {
            var change = new ChangeRow(
                KbIds.NewDocId(),
                doc.Id,
                versionId,
                ToWire(diff.Op),
                diff.BlockId,
                diff.BeforeText,
                diff.AfterText,
                rationale,
                ToWire(rationaleSource),
                now);

            session.InsertChange(change);
            session.IndexChange(change);
        }
    }

    private static BlockRow MaterializeBlock(
        KbSession session,
        DocRow doc,
        BlockInput input,
        DateTimeOffset now)
    {
        var normalized = KbText.Normalize(input.Text);
        if (normalized.Length == 0)
        {
            throw KbException.Invalid("块正文不能为空。");
        }

        if (normalized.Length > KbConstants.MaxBlockChars)
        {
            throw new KbException(
                KbErrorCodes.TooLarge,
                $"块正文超过上限（{normalized.Length} > {KbConstants.MaxBlockChars}）。");
        }

        var hash = KbText.Hash(normalized);
        var existing = session.FindBlockByHash(doc.Id, hash);
        if (existing is not null)
        {
            return existing;
        }

        var row = new BlockRow(
            KbIds.NewBlockId(),
            doc.Id,
            hash,
            normalized,
            input.Anchor?.GetRawText(),
            input.IsHeading,
            Math.Clamp(input.HeadingLevel ?? 0, 0, 6),
            now);

        session.InsertBlock(row);
        session.IndexBlock(row.Id, doc.Title, row.Text);
        return row;
    }

    private static List<BlockRow> MaterializeAll(
        KbSession session,
        DocRow doc,
        IReadOnlyList<BlockInput> inputs,
        DateTimeOffset now)
    {
        var blocks = new List<BlockRow>(inputs.Count);
        foreach (var input in inputs)
        {
            blocks.Add(MaterializeBlock(session, doc, input, now));
        }

        return blocks;
    }

    private static void ReplaceLinks(
        KbSession session,
        DocRow doc,
        int versionSeq,
        IReadOnlyList<IngestLink> links,
        string ownerApp,
        DateTimeOffset now)
    {
        var rows = new List<LinkRow>(links.Count);
        foreach (var link in links)
        {
            if (!KbUri.TryParse(link.To, out var target, out var error))
            {
                throw KbException.Invalid($"引用 URI 不合法：{error}");
            }

            if (!target!.HasVersion)
            {
                throw KbException.Invalid($"引用必须带版本：{link.To}（铁律 4）");
            }

            rows.Add(new LinkRow(
                KbIds.NewDocId(),
                doc.Id,
                KbUri.Format(doc.Id, versionSeq),
                target.ToString(),
                string.IsNullOrWhiteSpace(link.Rel) ? "related" : link.Rel.Trim(),
                ownerApp,
                target.DocId,
                target.VersionSeq,
                target.BlockId,
                now,
                now));
        }

        session.ReplaceOutboundLinks(doc.Id, KbUri.Format(doc.Id, versionSeq), ownerApp, rows, now);
    }

    private void RecordArtifacts(
        KbSession session,
        DocRow doc,
        string versionId,
        IReadOnlyList<IngestArtifact> artifacts,
        DateTimeOffset now)
    {
        foreach (var artifact in artifacts)
        {
            var imported = _blobs.Import(artifact);
            if (imported is null)
            {
                continue;
            }

            session.UpsertBlob(imported.Sha256, imported.Mime, imported.Size, imported.Name, imported.StoredPath, now);
            session.AddBlobRef(imported.Sha256, doc.Id, versionId);
        }
    }

    private void MaybePrune(KbSession session, DocRow doc)
    {
        if (string.Equals(doc.Kind, ToWire(KbKind.Requirement), StringComparison.Ordinal))
        {
            return;
        }

        var settings = ReadRetentionSettings(session);
        var keep = string.Equals(doc.Retention, ToWire(RetentionLevel.Staging), StringComparison.Ordinal)
            ? settings.StagingVersions
            : settings.NoteVersions;

        session.PruneVersions(doc.Id, Math.Max(1, keep));
    }

    // ---------------------------------------------------------------- 校验与映射

    private static string ValidateTitle(string? title)
    {
        var value = (title ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            throw KbException.Invalid("标题不能为空。");
        }

        if (value.Length > KbConstants.MaxTitleChars)
        {
            throw new KbException(KbErrorCodes.TooLarge, $"标题超过 {KbConstants.MaxTitleChars} 字符。");
        }

        return value;
    }

    private static void ValidateBlocks(IReadOnlyList<BlockInput>? blocks)
    {
        if (blocks is null || blocks.Count == 0)
        {
            throw KbException.Invalid("至少要有一个块。");
        }

        if (blocks.Count > KbConstants.MaxBlocksPerRequest)
        {
            throw new KbException(
                KbErrorCodes.TooLarge,
                $"单次块数超过 {KbConstants.MaxBlocksPerRequest}。");
        }
    }

    private static int FindIndex(IReadOnlyList<BlockRow> blocks, string? blockId)
    {
        if (string.IsNullOrWhiteSpace(blockId))
        {
            return -1;
        }

        for (var index = 0; index < blocks.Count; index++)
        {
            if (string.Equals(blocks[index].Id, blockId, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>W-5：App 只能改自己投影的内容；Agent 与 KB 自身可以编辑任意文档。</summary>
    internal static void EnsureCanWrite(DocRow doc, string appId)
    {
        if (IsPrivileged(appId)
            || string.Equals(doc.OwnerApp, appId, StringComparison.Ordinal))
        {
            return;
        }

        throw new KbException(
            KbErrorCodes.Forbidden,
            $"app「{appId}」不能修改 owner 为「{doc.OwnerApp}」的文档。");
    }

    public static bool IsPrivileged(string appId) =>
        string.Equals(appId, KbConstants.AppId, StringComparison.Ordinal)
        || string.Equals(appId, "sdemo.agent", StringComparison.Ordinal);

    private static string AuthorFor(string appId, RationaleSource? source, bool import)
    {
        if (import)
        {
            return "import";
        }

        return source switch
        {
            RationaleSource.User => "user",
            RationaleSource.Import => "import",
            RationaleSource.Meeting => "meeting",
            RationaleSource.Ai => "ai",
            _ => appId switch
            {
                "sdemo.meeting" => "meeting",
                KbConstants.AppId => "import",
                _ => "ai",
            },
        };
    }

    private static RationaleSource RationaleSourceFor(string appId) => appId switch
    {
        "sdemo.meeting" => RationaleSource.Meeting,
        KbConstants.AppId => RationaleSource.Import,
        _ => RationaleSource.Ai,
    };

    internal static RetentionLevel DefaultRetention(KbKind kind, string author, string appId)
    {
        if (kind == KbKind.Requirement || author == "import" || appId == KbConstants.AppId)
        {
            return RetentionLevel.Durable;
        }

        return RetentionLevel.Staging;
    }

    internal static string ToWire(KbKind kind) => kind.ToString().ToLowerInvariant();

    internal static string ToWire(RetentionLevel retention) => retention.ToString().ToLowerInvariant();

    internal static string ToWire(ChangeOp op) => op.ToString().ToLowerInvariant();

    internal static string ToWire(RationaleSource source) => source.ToString().ToLowerInvariant();

    internal static string ToWire(DerivedState state) => state.ToString().ToLowerInvariant();

    internal static string ToWire(VersionAuthor author) => author.ToString().ToLowerInvariant();

    internal static string ToWire(SearchScope scope) => scope.ToString().ToLowerInvariant();

    internal static TEnum ParseEnum<TEnum>(string? value, TEnum fallback)
        where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) ? parsed : fallback;
}

