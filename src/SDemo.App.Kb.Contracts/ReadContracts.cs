namespace SDemo.App.Kb.Contracts;

/// <summary>读取面：<c>/v1/resolve</c> 只给元数据，不给正文（kb-design §5.1）。</summary>
public sealed class ResolveRequest
{
    public string Uri { get; set; } = string.Empty;
}

public sealed class ResolveResult
{
    public string Uri { get; set; } = string.Empty;

    public bool Exists { get; set; }

    public string? Title { get; set; }

    public int? CurrentVersion { get; set; }

    /// <summary>引用时点的版本已不是当前版本。</summary>
    public bool Stale { get; set; }

    /// <summary>引用时点的块在当前版本已不存在（或 hash 变了）。</summary>
    public bool BlockMissing { get; set; }

    public bool Deleted { get; set; }

    public KbKind? Kind { get; set; }

    public int? ReferencedVersion { get; set; }
}

public sealed class DocSummary
{
    public string DocId { get; set; } = string.Empty;

    public KbKind Kind { get; set; }

    public string Title { get; set; } = string.Empty;

    public RetentionLevel Retention { get; set; }

    public string OwnerApp { get; set; } = string.Empty;

    public string Uri { get; set; } = string.Empty;

    public string? CurrentVersionId { get; set; }

    public int CurrentVersionSeq { get; set; }

    public DerivedState DerivedState { get; set; } = DerivedState.Ready;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>staging 的到期时间；durable 为 null。</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>列表摘要：当前版本第一段非标题正文，最多 160 字符。</summary>
    public string? Excerpt { get; set; }

    /// <summary>有多少条引用指向它（影响面红点用）。</summary>
    public int InboundRefCount { get; set; }

    public int BlockCount { get; set; }
}

public sealed class DocListRequest
{
    public string? Query { get; set; }

    public KbKind? Kind { get; set; }

    public RetentionLevel? Retention { get; set; }

    public bool IncludeDeleted { get; set; }

    public int Limit { get; set; } = 100;
}

public sealed class DocListResult
{
    public List<DocSummary> Items { get; set; } = [];

    public int Total { get; set; }
}

public sealed class ResolveBatchRequest
{
    public List<string> Uris { get; set; } = [];
}

public sealed class ResolveBatchResult
{
    public List<ResolveResult> Items { get; set; } = [];
}

public sealed class VersionSummary
{
    public string VersionId { get; set; } = string.Empty;

    public string DocId { get; set; } = string.Empty;

    public int Seq { get; set; }

    public string? ParentVersionId { get; set; }

    public string ContentHash { get; set; } = string.Empty;

    public VersionAuthor Author { get; set; }

    public string? Reason { get; set; }

    public string? NamedVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class BlockView
{
    public string BlockId { get; set; } = string.Empty;

    public int Ordinal { get; set; }

    public string Text { get; set; } = string.Empty;

    public string ContentHash { get; set; } = string.Empty;

    public string? AnchorJson { get; set; }

    public bool IsHeading { get; set; }

    public int HeadingLevel { get; set; }

    /// <summary>只在 history 视图里出现。</summary>
    public bool Superseded { get; set; }
}

public sealed class DocContent
{
    public DocSummary Doc { get; set; } = new();

    public VersionSummary Version { get; set; } = new();

    public List<BlockView> Blocks { get; set; } = [];
}

public sealed class KbGetRequest
{
    public string DocId { get; set; } = string.Empty;

    /// <summary>缺省取当前版本；可以是 seq（v7 / 7）。</summary>
    public string? Version { get; set; }
}

public sealed class OutlineNode
{
    public string BlockId { get; set; } = string.Empty;

    public int Ordinal { get; set; }

    public string Text { get; set; } = string.Empty;

    public int Level { get; set; }

    public bool IsHeading { get; set; }
}

public sealed class OutlineResult
{
    public string DocId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public int VersionSeq { get; set; }

    public List<OutlineNode> Nodes { get; set; } = [];
}

public sealed class KbDiffRequest
{
    public string DocId { get; set; } = string.Empty;

    /// <summary>起点 seq；缺省 = 上一版。</summary>
    public int? From { get; set; }

    /// <summary>终点 seq；缺省 = 当前版本。</summary>
    public int? To { get; set; }
}

public sealed class WordDiffSegment
{
    /// <summary>equal / insert / delete。</summary>
    public string Op { get; set; } = "equal";

    public string Text { get; set; } = string.Empty;
}

public sealed class BlockDiff
{
    public ChangeOp Op { get; set; }

    public string? BlockId { get; set; }

    public string? BeforeBlockId { get; set; }

    public string? BeforeText { get; set; }

    public string? AfterText { get; set; }

    /// <summary>块内词级差异，供界面高亮「这一句改了」。</summary>
    public List<WordDiffSegment> WordDiffs { get; set; } = [];
}

public sealed class DiffStats
{
    public int Added { get; set; }

    public int Removed { get; set; }

    public int Replaced { get; set; }

    public int Unchanged { get; set; }
}

public sealed class DiffResult
{
    public string DocId { get; set; } = string.Empty;

    public int FromSeq { get; set; }

    public int ToSeq { get; set; }

    public List<BlockDiff> Blocks { get; set; } = [];

    public DiffStats Stats { get; set; } = new();
}

public sealed class KbChangesRequest
{
    public string? DocId { get; set; }

    public DateTimeOffset? Since { get; set; }

    public int Limit { get; set; } = KbConstants.DefaultSearchLimit;
}

public sealed class ChangeView
{
    public string ChangeId { get; set; } = string.Empty;

    public string DocId { get; set; } = string.Empty;

    public string VersionId { get; set; } = string.Empty;

    public int VersionSeq { get; set; }

    public ChangeOp Op { get; set; }

    public string? BlockId { get; set; }

    public string? BeforeText { get; set; }

    public string? AfterText { get; set; }

    public string? Rationale { get; set; }

    public RationaleSource? RationaleSource { get; set; }

    public string? Author { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ChangesResult
{
    public List<ChangeView> Changes { get; set; } = [];
}

public sealed class KbRefsRequest
{
    public string DocId { get; set; } = string.Empty;
}

public sealed class RefView
{
    public string FromUri { get; set; } = string.Empty;

    public string ToUri { get; set; } = string.Empty;

    public string Rel { get; set; } = "related";

    public string OwnerApp { get; set; } = string.Empty;

    public bool Stale { get; set; }

    public bool BlockMissing { get; set; }

    public string? Title { get; set; }

    public int? ReferencedVersion { get; set; }

    public int? CurrentVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class RefsResult
{
    public string DocId { get; set; } = string.Empty;

    /// <summary>谁引用我（影响面）。</summary>
    public List<RefView> Inbound { get; set; } = [];

    /// <summary>我引用谁。</summary>
    public List<RefView> Outbound { get; set; } = [];
}

public sealed class KbSearchFilter
{
    public KbKind? Kind { get; set; }

    public RetentionLevel? Retention { get; set; }

    public string? DocId { get; set; }

    public DateTimeOffset? Since { get; set; }

    public DateTimeOffset? Until { get; set; }

    public string? OwnerApp { get; set; }
}

public sealed class KbSearchRequest
{
    public string Query { get; set; } = string.Empty;

    public SearchScope Scope { get; set; } = SearchScope.Current;

    public KbSearchFilter Filter { get; set; } = new();

    public int Limit { get; set; } = KbConstants.DefaultSearchLimit;

    /// <summary>是否允许 KB 用关键词把 scope 放宽（默认允许）。</summary>
    public bool AllowRoute { get; set; } = true;
}

public sealed class KbSearchHit
{
    public string DocId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public KbKind Kind { get; set; }

    public RetentionLevel Retention { get; set; }

    public int VersionSeq { get; set; }

    public string? BlockId { get; set; }

    /// <summary>命中片段，已按查询词截断。</summary>
    public string Snippet { get; set; } = string.Empty;

    public string Uri { get; set; } = string.Empty;

    /// <summary>命中的检索面：current / history / change / staging。</summary>
    public string Matched { get; set; } = "current";

    public double Score { get; set; }

    public string? Rationale { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class KbSearchResponse
{
    public string Query { get; set; } = string.Empty;

    public SearchScope Scope { get; set; } = SearchScope.Current;

    public List<KbSearchHit> Hits { get; set; } = [];

    /// <summary>可解释性：命中了哪些词。</summary>
    public List<string> HitTerms { get; set; } = [];

    /// <summary>零命中标记，驱动 L3 浏览兜底与向量化判据。</summary>
    public bool NoHit { get; set; }

    /// <summary>是否放宽了检索面或 AND→OR。</summary>
    public bool Widened { get; set; }

    public int TotalCandidates { get; set; }
}
