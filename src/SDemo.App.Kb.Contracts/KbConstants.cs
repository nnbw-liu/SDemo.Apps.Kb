namespace SDemo.App.Kb.Contracts;

/// <summary>KB 的全局常量。协议里的 URI、白名单与上限都从这里取，避免多处硬编码。</summary>
public static class KbConstants
{
    public const string AppId = "sdemo.kb";
    public const string AppTitle = "知识库";
    public const string AppVersion = "0.1.0";

    /// <summary>文档实体 URI 前缀：<c>sdemo.kb/doc/&lt;docId&gt;</c>。</summary>
    public const string DocUriPrefix = "sdemo.kb/doc/";

    public const string IngestPath = "/v1/ingest";
    public const string RevisePath = "/v1/revise";
    public const string PromotePath = "/v1/promote";
    public const string RetractPath = "/v1/retract";
    public const string ResolvePath = "/v1/resolve";
    public const string HealthPath = "/v1/health";
    public const string McpPath = "/mcp";

    /// <summary>单次写入的块数上限；超过说明调用方在做整篇覆盖，直接拒绝。</summary>
    public const int MaxBlocksPerRequest = 500;

    /// <summary>单个块的字符上限（约 100 KB 正文）。</summary>
    public const int MaxBlockChars = 100_000;

    /// <summary>标题字符上限。</summary>
    public const int MaxTitleChars = 300;

    public const int DefaultSearchLimit = 20;
    public const int MaxSearchLimit = 50;

    /// <summary>单次请求体上限（与 Planner 一致）。</summary>
    public const int MaxRequestBytes = 8 * 1024 * 1024;

    /// <summary>普通文档只保留最近版本数；requirement 全留。</summary>
    public const int DefaultRetentionVersions = 30;

    /// <summary>回收站保留天数。</summary>
    public const int TrashRetentionDays = 7;

    /// <summary>staging 版本保留数。</summary>
    public const int StagingRetentionVersions = 5;

    /// <summary>标题在 BM25 里的权重倍数（与 workspace-memory 的 ×4 对齐）。</summary>
    public const int TitleTermWeight = 4;

    /// <summary>首次启动自动签发 token 的 App 白名单；新增写入方要同时加到这里。</summary>
    public static readonly string[] ProvisionedApps =
    [
        AppId,
        "sdemo.agent",
        "sdemo.planner",
        "sdemo.meeting",
    ];
}
