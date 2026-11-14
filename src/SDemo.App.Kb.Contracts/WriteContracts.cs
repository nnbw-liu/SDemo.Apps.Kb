using System.Text.Json;

namespace SDemo.App.Kb.Contracts;

/// <summary>写入面请求的公共来源块。appId 必须与 token 绑定一致（W-1）。</summary>
public sealed class IngestSource
{
    public string AppId { get; set; } = string.Empty;

    /// <summary>写入方自己的实体 URI，例如 <c>sdemo.meeting/meeting/01H8…</c>。</summary>
    public string EntityUri { get; set; } = string.Empty;

    /// <summary>投影 schema 版本，用于日后重建投影。</summary>
    public int ProjectionVersion { get; set; } = 1;
}

/// <summary>块级输入。写入与块级更新共用同一形状（铁律 3：禁止整篇覆盖）。</summary>
public sealed class BlockInput
{
    public ChangeOp Op { get; set; } = ChangeOp.Add;

    /// <summary><c>replace</c> / <c>remove</c> 必填。</summary>
    public string? BlockId { get; set; }

    public string? Text { get; set; }

    /// <summary>原件位置，见 kb-design §2.4；手写块为 null。</summary>
    public JsonElement? Anchor { get; set; }

    /// <summary><c>add</c> 的插入位置（0 基）；缺省追加到末尾。</summary>
    public int? Position { get; set; }

    /// <summary>块小标题层级（仅导入器使用；不参与 hash）。</summary>
    public int? HeadingLevel { get; set; }

    public bool IsHeading { get; set; }
}

/// <summary>写入面：新建（幂等）。<c>POST /v1/ingest</c>。</summary>
public sealed class IngestRequest
{
    public IngestSource Source { get; set; } = new();

    public KbKind Kind { get; set; } = KbKind.Note;

    public string Title { get; set; } = string.Empty;

    /// <summary>缺省由 kind 决定：requirement → durable，其余 → staging（kb-design §4.2）。</summary>
    public RetentionLevel? Retention { get; set; }

    /// <summary>staging 的 TTL 覆盖（天）；缺省按来源取策略。AI 摘要可传 7，图片可传 30。</summary>
    public int? TtlDays { get; set; }

    /// <summary>派生状态；扫描件由导入器置 pending，Agent 回写块后转 ready。</summary>
    public DerivedState? DerivedState { get; set; }

    public string? Rationale { get; set; }

    public RationaleSource? RationaleSource { get; set; }

    /// <summary>幂等键 = appId + entityUri + contentHash；缺省时由 Core 计算。</summary>
    public string? IdempotencyKey { get; set; }

    public List<BlockInput> Blocks { get; set; } = [];

    public List<IngestLink> Links { get; set; } = [];

    public List<IngestArtifact> Artifacts { get; set; } = [];
}

/// <summary>写入面：修订（块级 ops + expectedVersion）。<c>POST /v1/revise</c>。</summary>
public sealed class ReviseRequest
{
    public IngestSource Source { get; set; } = new();

    public string DocId { get; set; } = string.Empty;

    /// <summary>乐观并发：调用方看到的最新 seq；0 或缺省表示不校验。</summary>
    public int? ExpectedVersion { get; set; }

    /// <summary>本次修订的一句话理由。</summary>
    public string? Reason { get; set; }

    public string? Rationale { get; set; }

    public RationaleSource? RationaleSource { get; set; }

    public string? IdempotencyKey { get; set; }

    /// <summary>requirement 的语义版本名（如 v1.1 / v2.0）；缺省自动 minor。</summary>
    public string? NamedVersion { get; set; }

    public List<BlockInput> Blocks { get; set; } = [];

    public List<IngestLink> Links { get; set; } = [];
}

/// <summary>写入面：升格 / 降格保留等级。<c>POST /v1/promote</c>。</summary>
public sealed class PromoteRequest
{
    public IngestSource Source { get; set; } = new();

    public string DocId { get; set; } = string.Empty;

    public RetentionLevel Retention { get; set; } = RetentionLevel.Durable;

    public string? Rationale { get; set; }

    /// <summary>撤销归档（durable → staging）必须二次确认（Q2）。</summary>
    public bool Confirmed { get; set; }
}

/// <summary>写入面：软删。<c>POST /v1/retract</c>。</summary>
public sealed class RetractRequest
{
    public IngestSource Source { get; set; } = new();

    public string DocId { get; set; } = string.Empty;

    public string? Reason { get; set; }
}

/// <summary>引用：<c>to</c> 必须带版本（铁律 4）。</summary>
public sealed class IngestLink
{
    public string To { get; set; } = string.Empty;

    public string Rel { get; set; } = "related";
}

/// <summary>原件。kb_blob 以 sha256 寻址，天然去重。</summary>
public sealed class IngestArtifact
{
    public string Role { get; set; } = "source";

    public string Sha256 { get; set; } = string.Empty;

    public string? Mime { get; set; }

    public long Size { get; set; }

    public string? Name { get; set; }

    /// <summary>本机路径（KB 会拷贝进 blobs）；与 uploadUrl 二选一。</summary>
    public string? Path { get; set; }

    public string? UploadUrl { get; set; }
}

/// <summary>写入结果：新建与修订共用。</summary>
public sealed class WriteResult
{
    public string DocId { get; set; } = string.Empty;

    public string VersionId { get; set; } = string.Empty;

    public int Seq { get; set; }

    /// <summary>requirement 的语义版本名；其它 kind 为 null。</summary>
    public string? NamedVersion { get; set; }

    /// <summary>true = 本次真的新建了文档。</summary>
    public bool Created { get; set; }

    /// <summary>true = 幂等命中，未产生新版本。</summary>
    public bool Deduplicated { get; set; }

    /// <summary>true = 内容与当前版本一致，未产生新版本。</summary>
    public bool NoChange { get; set; }

    public int CurrentVersionSeq { get; set; }

    public List<string> BlockIds { get; set; } = [];

    public int BlockCount { get; set; }
}

/// <summary>MCP <c>kb_create</c> 的语义参数（与 ingest 同源，不带 source）。</summary>
public sealed class KbCreateRequest
{
    public KbKind Kind { get; set; } = KbKind.Note;

    public string Title { get; set; } = string.Empty;

    public RetentionLevel? Retention { get; set; }

    public int? TtlDays { get; set; }

    public string? Rationale { get; set; }

    public List<BlockInput> Blocks { get; set; } = [];
}

/// <summary>MCP <c>kb_update</c> 的语义参数：只有块级 ops，没有整篇正文字段。</summary>
public sealed class KbUpdateRequest
{
    public string DocId { get; set; } = string.Empty;

    public int ExpectedVersion { get; set; }

    public List<BlockInput> Ops { get; set; } = [];

    public string? Rationale { get; set; }

    public string? Reason { get; set; }

    public string? NamedVersion { get; set; }

    public RationaleSource RationaleSource { get; set; } = RationaleSource.Ai;
}

/// <summary>
/// 合并两个版本：调用方给出已裁决好的块序列，KB 只负责落成多父版本。
/// 不做自动三路合并（裁决需要人/Agent，KB 不调模型）。
/// </summary>
public sealed class KbMergeRequest
{
    public string DocId { get; set; } = string.Empty;

    public int LeftVersion { get; set; }

    public int RightVersion { get; set; }

    public int ExpectedVersion { get; set; }

    public List<BlockInput> Blocks { get; set; } = [];

    public string? Rationale { get; set; }

    public string? NamedVersion { get; set; }

    public RationaleSource RationaleSource { get; set; } = RationaleSource.Ai;
}

public sealed class KbPromoteRequest
{
    public string DocId { get; set; } = string.Empty;

    public RetentionLevel Retention { get; set; } = RetentionLevel.Durable;

    public string? Rationale { get; set; }

    public bool Confirmed { get; set; }
}

public sealed class KbDeleteRequest
{
    public string DocId { get; set; } = string.Empty;

    public string? Reason { get; set; }
}

/// <summary>软删结果：带上受影响的引用，供 Agent 提示「这些待办要复核」。</summary>
public sealed class DeleteResult
{
    public string DocId { get; set; } = string.Empty;

    public DateTimeOffset DeletedAt { get; set; }

    public List<RefView> AffectedRefs { get; set; } = [];
}

/// <summary>MCP <c>kb_annotate</c>：Agent 写「这次改动意味着什么」，不调模型。</summary>
public sealed class KbAnnotateRequest
{
    public string DocId { get; set; } = string.Empty;

    public string VersionId { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;
}
