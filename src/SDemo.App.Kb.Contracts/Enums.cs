namespace SDemo.App.Kb.Contracts;

/// <summary>文档类型白名单。新增 kind 必须同时改契约与 schema（kb-design §4.2）。</summary>
public enum KbKind
{
    Note,
    Reference,
    Requirement,
    Decision,
}

/// <summary>两级保留：staging 可被 TTL 清理，durable 只增不减（除非显式删除）。</summary>
public enum RetentionLevel
{
    Staging,
    Durable,
}

/// <summary>版本作者。投影写入方不能覆盖人工版本（W-5）。</summary>
public enum VersionAuthor
{
    User,
    Ai,
    Import,
    Meeting,
    System,
}

/// <summary>变更事实的操作类型。</summary>
public enum ChangeOp
{
    Add,
    Replace,
    Remove,
}

/// <summary>变更理由来源（kb-versioning §4）。</summary>
public enum RationaleSource
{
    User,
    Ai,
    Meeting,
    Import,
    System,
}

/// <summary>检索面。C 面是变更事实，D 面是暂存。</summary>
public enum SearchScope
{
    /// <summary>A 当前态。</summary>
    Current,

    /// <summary>B 历史态。</summary>
    History,

    /// <summary>C 变更事实 + rationale。</summary>
    Change,

    /// <summary>A + D，最近内容。</summary>
    Recent,

    /// <summary>A + B。</summary>
    All,
}

/// <summary>anchor 的来源类型（kb-design §2.4）。</summary>
public enum AnchorKind
{
    Offset,
    Para,
    Slide,
    Sheet,
    Page,
    Region,
}

/// <summary>派生状态：OCR / caption 等昂贵派生不能卡在写入同步路径上。</summary>
public enum DerivedState
{
    Pending,
    Ready,
    Failed,
}

/// <summary>MCP 工具风险等级，由应用声明，Agent 只按等级执行。</summary>
public enum RiskLevel
{
    Low,
    Medium,
    High,
}

/// <summary>审批策略。</summary>
public enum ApprovalMode
{
    None,
    Once,
    Always,
}
