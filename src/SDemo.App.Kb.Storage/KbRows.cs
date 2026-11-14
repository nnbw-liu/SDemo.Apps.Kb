namespace SDemo.App.Kb.Storage;

/// <summary>存储层行模型：字段与表一一对应，枚举以字符串落库（与线上 JSON 一致）。</summary>
public sealed record DocRow(
    string Id,
    string Kind,
    string Title,
    string OwnerApp,
    string EntityUri,
    string Uri,
    string Retention,
    string DerivedState,
    string? CurrentVersionId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? DeletedAt)
{
    public bool IsDeleted => DeletedAt is not null;
}

public sealed record VersionRow(
    string Id,
    string DocId,
    int Seq,
    string? ParentId,
    string ContentHash,
    string Author,
    string? Reason,
    string? NamedVersion,
    DateTimeOffset CreatedAt);

public sealed record BlockRow(
    string Id,
    string DocId,
    string ContentHash,
    string Text,
    string? AnchorJson,
    bool IsHeading,
    int HeadingLevel,
    DateTimeOffset CreatedAt);

public sealed record ChangeRow(
    string Id,
    string DocId,
    string VersionId,
    string Op,
    string? BlockId,
    string? BeforeText,
    string? AfterText,
    string? Rationale,
    string? RationaleSource,
    DateTimeOffset CreatedAt);

public sealed record LinkRow(
    string Id,
    string DocId,
    string FromUri,
    string ToUri,
    string Rel,
    string OwnerApp,
    string TargetDocId,
    int? TargetVersion,
    string? TargetBlockId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record SearchRow(
    string BlockId,
    string DocId,
    string Title,
    string Kind,
    string Retention,
    int VersionSeq,
    string VersionId,
    string Text,
    double Rank,
    DateTimeOffset UpdatedAt,
    string Face,
    string? Rationale,
    DateTimeOffset CreatedAt);

public sealed record ChangeSearchRow(
    string ChangeId,
    string DocId,
    string Title,
    string Kind,
    string Retention,
    int VersionSeq,
    string Op,
    string? BlockId,
    string? BeforeText,
    string? AfterText,
    string? Rationale,
    string? RationaleSource,
    DateTimeOffset CreatedAt,
    double Rank);
