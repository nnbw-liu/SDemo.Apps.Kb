using SDemo.App.Kb.Contracts;
using SDemo.App.Kb.Storage;

namespace SDemo.App.Kb.Core;

/// <summary>读路径：get / outline / diff / changes / refs / resolve / search。</summary>
public sealed partial class KbService
{
    private static readonly string[] ChangeKeywords = ["为什么", "为何", "原因", "理由", "谁改", "谁修改", "什么时候改", "改成"];
    private static readonly string[] HistoryKeywords = ["以前", "之前", "原来", "历史", "第几版", "旧版", "早期", "当时"];
    private static readonly string[] RecentKeywords = ["最近", "昨天", "今天", "刚才", "上周", "本周", "最近一次"];
    private static readonly string[] CurrentKeywords = ["现在", "当前", "最新", "目前", "如今"];

    public DocContent? Get(KbGetRequest request, bool includeDeleted = false)
    {
        using var session = _store.Read();
        var doc = session.GetDoc(request.DocId, includeDeleted);
        if (doc is null)
        {
            return null;
        }

        var version = ResolveVersion(session, doc, request.Version);
        if (version is null)
        {
            return null;
        }

        var blocks = session.GetVersionBlocks(version.Id);
        var content = new DocContent
        {
            Doc = MapDoc(session, doc),
            Version = MapVersion(version),
        };

        for (var ordinal = 0; ordinal < blocks.Count; ordinal++)
        {
            content.Blocks.Add(MapBlock(blocks[ordinal], ordinal, superseded: version.Id != doc.CurrentVersionId));
        }

        foreach (var blob in session.ListBlobsForDoc(doc.Id))
        {
            content.Artifacts.Add(new ArtifactView
            {
                Sha256 = blob.Sha256,
                Mime = blob.Mime,
                Size = blob.Size,
                Name = blob.Name,
            });
        }

        return content;
    }

    public OutlineResult Outline(string docId)
    {
        using var session = _store.Read();
        var doc = session.GetDoc(docId) ?? throw KbException.NotFound($"文档不存在：{docId}");
        var version = session.GetCurrentVersion(doc.Id)
            ?? throw KbException.NotFound($"文档没有当前版本：{docId}");
        var blocks = session.GetVersionBlocks(version.Id);

        var result = new OutlineResult
        {
            DocId = doc.Id,
            Title = doc.Title,
            VersionSeq = version.Seq,
        };

        var headings = new List<BlockView>();
        for (var ordinal = 0; ordinal < blocks.Count; ordinal++)
        {
            var block = blocks[ordinal];
            if (block.IsHeading)
            {
                headings.Add(MapBlock(block, ordinal, superseded: false));
            }
        }

        // 没有标题时退化为「全部块」——浏览兜底仍要能定位。
        if (headings.Count == 0)
        {
            for (var ordinal = 0; ordinal < blocks.Count; ordinal++)
            {
                headings.Add(MapBlock(blocks[ordinal], ordinal, superseded: false));
            }
        }

        foreach (var block in headings)
        {
            result.Nodes.Add(new OutlineNode
            {
                BlockId = block.BlockId,
                Ordinal = block.Ordinal,
                Text = block.Text,
                Level = block.HeadingLevel == 0 ? 1 : block.HeadingLevel,
                IsHeading = block.IsHeading,
            });
        }

        return result;
    }

    public DiffResult Diff(KbDiffRequest request)
    {
        using var session = _store.Read();
        var doc = session.GetDoc(request.DocId) ?? throw KbException.NotFound($"文档不存在：{request.DocId}");
        var current = session.GetCurrentVersion(doc.Id)
            ?? throw KbException.NotFound($"文档没有当前版本：{request.DocId}");

        var toSeq = request.To ?? current.Seq;
        var fromSeq = request.From ?? Math.Max(1, toSeq - 1);

        var to = session.GetVersionBySeq(doc.Id, toSeq)
            ?? throw new KbException(KbErrorCodes.VersionNotFound, $"没有 v{toSeq}。");
        var from = session.GetVersionBySeq(doc.Id, fromSeq)
            ?? throw new KbException(KbErrorCodes.VersionNotFound, $"没有 v{fromSeq}。");

        return KbDiff.Build(
            doc.Id,
            from,
            to,
            session.GetVersionBlocks(from.Id),
            session.GetVersionBlocks(to.Id));
    }

    public ChangesResult Changes(KbChangesRequest request)
    {
        using var session = _store.Read();
        var rows = session.ListChanges(request.DocId, request.Since, request.Limit);
        var result = new ChangesResult();

        foreach (var row in rows)
        {
            var version = session.GetVersionById(row.VersionId);
            result.Changes.Add(new ChangeView
            {
                ChangeId = row.Id,
                DocId = row.DocId,
                VersionId = row.VersionId,
                VersionSeq = version?.Seq ?? 0,
                Op = ParseEnum(row.Op, ChangeOp.Add),
                BlockId = row.BlockId,
                BeforeText = row.BeforeText,
                AfterText = row.AfterText,
                Rationale = row.Rationale,
                RationaleSource = row.RationaleSource is null
                    ? null
                    : ParseEnum<RationaleSource>(row.RationaleSource, RationaleSource.System),
                CreatedAt = row.CreatedAt,
            });
        }

        return result;
    }

    public RefsResult Refs(string docId)
    {
        using var session = _store.Read();
        var doc = session.GetDoc(docId) ?? throw KbException.NotFound($"文档不存在：{docId}");

        var result = new RefsResult { DocId = doc.Id };

        foreach (var link in session.ListInboundLinks(doc.Id))
        {
            result.Inbound.Add(BuildRef(session, link));
        }

        foreach (var link in session.ListOutboundLinks(doc.Id))
        {
            result.Outbound.Add(BuildRef(session, link));
        }

        return result;
    }

    public ResolveResult Resolve(string uriText)
    {
        var result = new ResolveResult { Uri = uriText ?? string.Empty };

        if (!KbUri.TryParse(uriText, out var uri, out _))
        {
            return result;
        }

        using var session = _store.Read();
        var doc = session.GetDoc(uri!.DocId, includeDeleted: true);
        if (doc is null)
        {
            return result;
        }

        result.Exists = true;
        result.Title = doc.Title;
        result.Kind = ParseEnum<KbKind>(doc.Kind, KbKind.Note);
        result.Deleted = doc.IsDeleted;

        var current = session.GetCurrentVersion(doc.Id);
        result.CurrentVersion = current?.Seq;

        if (uri.VersionSeq is { } refSeq)
        {
            result.ReferencedVersion = refSeq;
            result.Stale = current is null || refSeq != current.Seq;
        }

        if (uri.BlockId is { } blockId)
        {
            result.BlockMissing = current is null || !session.BlockExistsInVersion(current.Id, blockId);
        }

        return result;
    }

    public KbSearchResponse Search(KbSearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var query = (request.Query ?? string.Empty).Trim();
        var scope = request.AllowRoute ? Route(query, request.Scope) : request.Scope;

        // 路由词（以前 / 为什么 / 最近）只用来选检索面，不应该再要求它出现在正文里。
        var searchText = scope != request.Scope ? StripRouteCues(query) : query;
        if (searchText.Length == 0)
        {
            searchText = query;
        }

        var tokens = KbTokenizer.Tokenize(searchText)
            .Distinct(StringComparer.Ordinal)
            .Take(16)
            .ToList();

        var response = new KbSearchResponse
        {
            Query = query,
            Scope = scope,
        };

        if (tokens.Count == 0)
        {
            response.NoHit = true;
            return response;
        }

        request.Filter ??= new KbSearchFilter();

        using var session = _store.Read();
        var limit = Math.Clamp(request.Limit, 1, KbConstants.MaxSearchLimit);

        var collected = Collect(session, tokens, scope, request.Filter, limit);

        // 只有长查询才放宽 AND→OR；两三个 2-gram 的短查询放宽后precision 会掉得很难看。
        if (collected.Count == 0 && tokens.Count > 3)
        {
            collected = Collect(session, tokens, scope, request.Filter, limit, useOr: true);
            response.Widened = collected.Count > 0;
        }

        response.Hits = collected
            .GroupBy(item => (item.Hit.DocId, item.Hit.BlockId, item.Hit.Matched))
            .Select(group => group.OrderByDescending(item => item.Hit.Score).First())
            .OrderByDescending(item => item.Hit.Score)
            .ThenByDescending(item => item.Hit.CreatedAt)
            .Take(limit)
            .Select(item => item.Hit)
            .ToList();

        response.HitTerms = tokens
            .Where(token => collected.Any(item =>
                item.Hit.Title.Contains(token, StringComparison.OrdinalIgnoreCase)
                || (item.Hit.Rationale ?? string.Empty).Contains(token, StringComparison.OrdinalIgnoreCase)
                || (item.HitText ?? string.Empty).Contains(token, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        response.NoHit = response.Hits.Count == 0;
        return response;
    }

    private List<HitWithText> Collect(
        KbSession session,
        IReadOnlyList<string> tokens,
        SearchScope scope,
        KbSearchFilter filter,
        int limit,
        bool useOr = false)
    {
        var match = BuildMatchQuery(tokens, useOr);
        var collected = new List<HitWithText>();

        if (scope == SearchScope.Change)
        {
            foreach (var row in session.SearchChanges(match, filter, limit))
            {
                var text = row.Rationale ?? row.AfterText ?? row.BeforeText ?? string.Empty;
                collected.Add(new HitWithText(
                    new KbSearchHit
                    {
                        DocId = row.DocId,
                        Title = row.Title,
                        Kind = ParseEnum(row.Kind, KbKind.Note),
                        Retention = ParseEnum(row.Retention, RetentionLevel.Staging),
                        VersionSeq = row.VersionSeq,
                        BlockId = row.BlockId,
                        Snippet = KbText.Snippet(text, FirstHit(text, tokens)),
                        Uri = row.BlockId is null
                            ? KbUri.Format(row.DocId, row.VersionSeq)
                            : KbUri.Format(row.DocId, row.VersionSeq, row.BlockId),
                        Matched = "change",
                        Score = Math.Round(-row.Rank, 4),
                        Rationale = row.Rationale,
                        CreatedAt = row.CreatedAt,
                    },
                    text));
            }

            return collected;
        }

        var faces = scope switch
        {
            SearchScope.History => new[] { "history" },
            SearchScope.All => new[] { "current", "history" },
            _ => new[] { "current" },
        };

        var includeStaging = scope == SearchScope.Recent;

        foreach (var face in faces)
        {
            foreach (var row in session.SearchBlocks(match, face, filter, includeStaging, limit))
            {
                collected.Add(new HitWithText(
                    new KbSearchHit
                    {
                        DocId = row.DocId,
                        Title = row.Title,
                        Kind = ParseEnum(row.Kind, KbKind.Note),
                        Retention = ParseEnum(row.Retention, RetentionLevel.Staging),
                        VersionSeq = row.VersionSeq,
                        BlockId = row.BlockId,
                        Snippet = KbText.Snippet(row.Text, FirstHit(row.Text, tokens)),
                        Uri = KbUri.Format(row.DocId, row.VersionSeq, row.BlockId),
                        Matched = face,
                        Score = Math.Round(-row.Rank, 4),
                        CreatedAt = row.CreatedAt,
                    },
                    row.Text));
            }
        }

        return collected;
    }

    private static string FirstHit(string text, IReadOnlyList<string> tokens) =>
        tokens.FirstOrDefault(token => text.Contains(token, StringComparison.OrdinalIgnoreCase))
        ?? tokens.FirstOrDefault()
        ?? string.Empty;

    private static string BuildMatchQuery(IReadOnlyList<string> tokens, bool useOr) =>
        string.Join(useOr ? " OR " : " AND ", tokens.Select(KbTokenizer.Quote));

    /// <summary>关键词路由，不调模型：出现「现在 / 以前 / 为什么 / 最近」就把检索面切过去。</summary>
    public static SearchScope Route(string query, SearchScope requested)
    {
        if (requested != SearchScope.Current)
        {
            return requested;
        }

        var text = query ?? string.Empty;

        if (ChangeKeywords.Any(keyword => text.Contains(keyword, StringComparison.Ordinal)))
        {
            return SearchScope.Change;
        }

        if (HistoryKeywords.Any(keyword => text.Contains(keyword, StringComparison.Ordinal)))
        {
            return SearchScope.History;
        }

        if (RecentKeywords.Any(keyword => text.Contains(keyword, StringComparison.Ordinal)))
        {
            return SearchScope.Recent;
        }

        return requested;
    }

    internal static string StripRouteCues(string query)
    {
        var text = query ?? string.Empty;

        foreach (var keyword in ChangeKeywords
                     .Concat(HistoryKeywords)
                     .Concat(RecentKeywords)
                     .Concat(CurrentKeywords)
                     .OrderByDescending(keyword => keyword.Length))
        {
            text = text.Replace(keyword, " ", StringComparison.Ordinal);
        }

        return text.Trim();
    }

    public IReadOnlyList<DocSummary> ListDocs(bool includeDeleted = false)
    {
        using var session = _store.Read();
        return session.ListDocs(includeDeleted).Select(doc => MapDoc(session, doc)).ToList();
    }

    public DocListResult ListDocuments(DocListRequest request)
    {
        request ??= new DocListRequest();

        using var session = _store.Read();
        var rows = session.ListDocuments(
            request.Query,
            request.Kind is { } kind ? ToWire(kind) : null,
            request.Retention is { } retention ? ToWire(retention) : null,
            request.IncludeDeleted,
            request.Limit);

        var result = new DocListResult { Total = rows.Total };
        foreach (var row in rows.Items)
        {
            var summary = MapDoc(session, row.Doc);
            summary.Excerpt = Truncate(row.Excerpt, 160);
            summary.InboundRefCount = row.InboundRefCount;
            result.Items.Add(summary);
        }

        return result;
    }

    public ResolveBatchResult ResolveMany(ResolveBatchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Uris.Count > 100)
        {
            throw new KbException(KbErrorCodes.TooLarge, "单次最多 resolve 100 条 URI。");
        }

        var result = new ResolveBatchResult();
        foreach (var uri in request.Uris)
        {
            result.Items.Add(Resolve(uri));
        }

        return result;
    }

    private static string? Truncate(string? text, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var value = text.Trim().Replace('\n', ' ');
        return value.Length <= maxChars ? value : value[..maxChars] + "…";
    }

    public IReadOnlyList<VersionSummary> History(string docId, int limit = 50)
    {
        using var session = _store.Read();
        _ = session.GetDoc(docId) ?? throw KbException.NotFound($"文档不存在：{docId}");
        return session.ListVersions(docId, limit).Select(MapVersion).ToList();
    }

    private static VersionRow? ResolveVersion(KbSession session, DocRow doc, string? version)
    {
        if (string.IsNullOrWhiteSpace(version) || string.Equals(version, "current", StringComparison.OrdinalIgnoreCase))
        {
            return session.GetCurrentVersion(doc.Id);
        }

        var text = version.Trim();
        text = text.StartsWith('v') || text.StartsWith('V') ? text[1..] : text;

        if (int.TryParse(text, out var seq))
        {
            return session.GetVersionBySeq(doc.Id, seq);
        }

        var byId = session.GetVersionById(text);
        return byId is not null && string.Equals(byId.DocId, doc.Id, StringComparison.Ordinal) ? byId : null;
    }

    private static DocSummary MapDoc(KbSession session, DocRow doc)
    {
        var current = doc.CurrentVersionId is null ? null : session.GetVersionById(doc.CurrentVersionId);
        return new DocSummary
        {
            DocId = doc.Id,
            Kind = ParseEnum(doc.Kind, KbKind.Note),
            Title = doc.Title,
            Retention = ParseEnum(doc.Retention, RetentionLevel.Staging),
            OwnerApp = doc.OwnerApp,
            Uri = doc.Uri,
            CurrentVersionId = doc.CurrentVersionId,
            CurrentVersionSeq = current?.Seq ?? 0,
            DerivedState = ParseEnum(doc.DerivedState, DerivedState.Ready),
            CreatedAt = doc.CreatedAt,
            UpdatedAt = doc.UpdatedAt,
            DeletedAt = doc.DeletedAt,
            ExpiresAt = doc.ExpiresAt,
            BlockCount = current is null ? 0 : session.CountBlocksInVersion(current.Id),
        };
    }

    private static VersionSummary MapVersion(VersionRow version) => new()
    {
        VersionId = version.Id,
        DocId = version.DocId,
        Seq = version.Seq,
        ParentVersionId = version.ParentId,
        ContentHash = version.ContentHash,
        Author = ParseEnum(version.Author, VersionAuthor.System),
        Reason = version.Reason,
        NamedVersion = version.NamedVersion,
        CreatedAt = version.CreatedAt,
    };

    private static BlockView MapBlock(BlockRow block, int ordinal, bool superseded) => new()
    {
        BlockId = block.Id,
        Ordinal = ordinal,
        Text = block.Text,
        ContentHash = block.ContentHash,
        AnchorJson = block.AnchorJson,
        IsHeading = block.IsHeading,
        HeadingLevel = block.HeadingLevel,
        Superseded = superseded,
    };

    private static RefView BuildRef(KbSession session, LinkRow link)
    {
        var target = session.GetDoc(link.TargetDocId, includeDeleted: true);
        var current = target is null ? null : session.GetCurrentVersion(target.Id);

        var stale = current is null
            || (link.TargetVersion is { } version && version != current.Seq);

        var blockMissing = link.TargetBlockId is { } blockId
            && (current is null || !session.BlockExistsInVersion(current.Id, blockId));

        return new RefView
        {
            FromUri = link.FromUri,
            ToUri = link.ToUri,
            Rel = link.Rel,
            OwnerApp = link.OwnerApp,
            Stale = stale,
            BlockMissing = blockMissing,
            Title = target?.Title,
            ReferencedVersion = link.TargetVersion,
            CurrentVersion = current?.Seq,
            CreatedAt = link.CreatedAt,
        };
    }

    private sealed record HitWithText(KbSearchHit Hit, string? HitText);
}
