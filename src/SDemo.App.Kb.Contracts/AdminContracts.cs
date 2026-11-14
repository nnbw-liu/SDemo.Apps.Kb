namespace SDemo.App.Kb.Contracts;

/// <summary>保留策略的可调参数。默认值来自 kb-design §4.3；改这里等于改全库行为。</summary>
public sealed class RetentionSettings
{
    /// <summary>staging 文档保留的版本数。</summary>
    public int StagingVersions { get; set; } = 5;

    /// <summary>durable 普通文档保留的版本数；requirement 不受限。</summary>
    public int NoteVersions { get; set; } = 30;

    /// <summary>会议纪要 / 决策的暂存天数。</summary>
    public int MeetingTtlDays { get; set; } = 30;

    /// <summary>Agent 产出（笔记 / 摘要 / 归档）的暂存天数。</summary>
    public int AgentTtlDays { get; set; } = 90;

    /// <summary>回收站天数。</summary>
    public int TrashDays { get; set; } = 7;
}

/// <summary>待归档收件箱的一行。到期倒计时由 <see cref="ExpiresAt"/> 与当前时间算。</summary>
public sealed class InboxItem
{
    public string DocId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public KbKind Kind { get; set; }

    public string OwnerApp { get; set; } = string.Empty;

    public int VersionSeq { get; set; }

    public int BlockCount { get; set; }

    /// <summary>当前版本正文的字符数（不是字节）。</summary>
    public long TextChars { get; set; }

    /// <summary>关联原件的总字节数。</summary>
    public long BlobBytes { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>距到期还有几天；负数表示已过期。</summary>
    public int DaysRemaining { get; set; }

    public DerivedState DerivedState { get; set; } = DerivedState.Ready;

    public string Uri { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class InboxResult
{
    public List<InboxItem> Items { get; set; } = [];

    /// <summary>未受 limit 限制的总数。</summary>
    public int Total { get; set; }

    /// <summary>3 天内到期（或已过期）的条数，驱动红点。</summary>
    public int ExpiringSoon { get; set; }
}

public sealed class PromoteBatchRequest
{
    public List<string> DocIds { get; set; } = [];

    public RetentionLevel Retention { get; set; } = RetentionLevel.Durable;

    public string? Rationale { get; set; }
}

public sealed class PromoteBatchResult
{
    public List<WriteResult> Promoted { get; set; } = [];

    public List<string> Failed { get; set; } = [];
}

public sealed class StorageStats
{
    public long DatabaseBytes { get; set; }

    public long BlobBytes { get; set; }

    public long TextChars { get; set; }

    public int DocCount { get; set; }

    public int StagingCount { get; set; }

    public int TrashCount { get; set; }

    public int BlockCount { get; set; }

    public int VersionCount { get; set; }

    /// <summary>无引用的原件字节数，即「可清理项」。</summary>
    public long ReclaimableBlobBytes { get; set; }
}

/// <summary>一次维护扫描的结果；Host 每小时跑一次，也可用 --maintain 手动触发。</summary>
public sealed class MaintenanceReport
{
    public DateTimeOffset RunAt { get; set; }

    public int ExpiredDocs { get; set; }

    public int PurgedDocs { get; set; }

    /// <summary>因仍被引用而整篇保留、未物理清理的文档数。</summary>
    public int ProtectedDocs { get; set; }

    public int CollectedBlocks { get; set; }

    public int CollectedBlobs { get; set; }

    public long ReclaimedBlobBytes { get; set; }
}
