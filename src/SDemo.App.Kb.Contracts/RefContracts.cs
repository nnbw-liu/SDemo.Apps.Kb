namespace SDemo.App.Kb.Contracts;

/// <summary>
/// 外部实体把「我引用了 KB 的某块」登记进 KB。这是影响面与引用保护成立的前提：
/// 只存在 App 自己的 links 表里，KB 看不到，保护就是假的。
/// </summary>
public sealed class RefRegistrationRequest
{
    public IngestSource Source { get; set; } = new();

    /// <summary>目标 KB URI，必须带版本：<c>sdemo.kb/doc/…@v7#b4</c>。</summary>
    public string To { get; set; } = string.Empty;

    /// <summary>关系词，如 <c>based_on</c> / <c>cites</c> / <c>mentions</c>。</summary>
    public string Rel { get; set; } = "related";
}

public sealed class RefRegistrationResult
{
    public string FromUri { get; set; } = string.Empty;

    public string ToUri { get; set; } = string.Empty;

    public string Rel { get; set; } = "related";

    /// <summary>true = 新增；false = 已存在、只刷新了时间。</summary>
    public bool Created { get; set; }

    public string? TargetTitle { get; set; }
}

/// <summary>撤销登记：缺省撤销该来源实体的全部引用。</summary>
public sealed class RefRetractRequest
{
    public IngestSource Source { get; set; } = new();

    public string? To { get; set; }

    public string? Rel { get; set; }
}

public sealed class RefRetractResult
{
    public string FromUri { get; set; } = string.Empty;

    public int Removed { get; set; }
}

public sealed class RefListResult
{
    public string FromUri { get; set; } = string.Empty;

    public List<RefView> Items { get; set; } = [];
}
