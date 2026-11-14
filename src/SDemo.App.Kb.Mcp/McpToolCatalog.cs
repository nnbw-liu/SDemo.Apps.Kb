using System.Text.Json.Nodes;
using SDemo.App.Kb.Contracts;

namespace SDemo.App.Kb.Mcp;

/// <summary>
/// 一个 MCP 工具的完整声明：schema + 风险 + 审批。风险等级由 KB 自己声明，
/// Agent 只按等级执行，不硬编码工具名（docs/integration.md §4）。
/// </summary>
public sealed record KbToolDefinition(
    string Name,
    string Description,
    string InputSchemaJson,
    RiskLevel Risk,
    ApprovalMode Approval,
    bool ReadOnly,
    bool Destructive,
    bool Idempotent,
    bool Writes)
{
    public JsonObject ToJson()
    {
        var meta = new JsonObject
        {
            ["sdemo/risk"] = Risk.ToString().ToLowerInvariant(),
            ["sdemo/approval"] = Approval.ToString().ToLowerInvariant(),
            ["sdemo/writes"] = Writes,
        };

        return new JsonObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = JsonNode.Parse(InputSchemaJson),
            ["annotations"] = new JsonObject
            {
                ["readOnlyHint"] = ReadOnly,
                ["destructiveHint"] = Destructive,
                ["idempotentHint"] = Idempotent,
            },
            ["_meta"] = meta,
        };
    }
}

/// <summary>v1 的 11 个 <c>kb_*</c> 工具（kb-design §5.2）。写只有块级 ops，没有整篇正文字段。</summary>
public static class McpToolCatalog
{
    private const string SearchSchema = """
        {
          "type": "object",
          "required": ["query"],
          "additionalProperties": false,
          "properties": {
            "query": { "type": "string", "description": "关键词；中文两字即可，KB 走 2-gram FTS" },
            "scope": { "type": "string", "enum": ["current", "history", "change", "recent", "all"], "default": "current" },
            "filter": {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "kind": { "type": "string", "enum": ["note", "reference", "requirement", "decision"] },
                "retention": { "type": "string", "enum": ["staging", "durable"] },
                "docId": { "type": "string" },
                "ownerApp": { "type": "string" },
                "since": { "type": "string", "format": "date-time" },
                "until": { "type": "string", "format": "date-time" }
              }
            },
            "limit": { "type": "integer", "minimum": 1, "maximum": 50, "default": 20 },
            "allowRoute": { "type": "boolean", "default": true, "description": "允许按「现在 / 以前 / 为什么 / 最近」自动切检索面" }
          }
        }
        """;

    private const string GetSchema = """
        {
          "type": "object",
          "required": ["docId"],
          "additionalProperties": false,
          "properties": {
            "docId": { "type": "string" },
            "version": { "type": "string", "description": "v7 / 7 / 版本 id；缺省取当前版本" }
          }
        }
        """;

    private const string DocIdSchema = """
        {
          "type": "object",
          "required": ["docId"],
          "additionalProperties": false,
          "properties": { "docId": { "type": "string" } }
        }
        """;

    private const string DiffSchema = """
        {
          "type": "object",
          "required": ["docId"],
          "additionalProperties": false,
          "properties": {
            "docId": { "type": "string" },
            "from": { "type": "integer", "minimum": 1, "description": "起点 seq；缺省 = 上一版" },
            "to": { "type": "integer", "minimum": 1, "description": "终点 seq；缺省 = 当前版本" }
          }
        }
        """;

    private const string ChangesSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "docId": { "type": "string" },
            "since": { "type": "string", "format": "date-time" },
            "limit": { "type": "integer", "minimum": 1, "maximum": 200, "default": 20 }
          }
        }
        """;

    private const string AnnotateSchema = """
        {
          "type": "object",
          "required": ["docId", "summary"],
          "additionalProperties": false,
          "properties": {
            "docId": { "type": "string" },
            "versionId": { "type": "string", "description": "缺省 = 当前版本" },
            "summary": { "type": "string", "maxLength": 4000 }
          }
        }
        """;

    private const string CreateSchema = """
        {
          "type": "object",
          "required": ["kind", "title", "blocks"],
          "additionalProperties": false,
          "properties": {
            "kind": { "type": "string", "enum": ["note", "reference", "requirement", "decision"] },
            "title": { "type": "string", "maxLength": 300 },
            "retention": { "type": "string", "enum": ["staging", "durable"] },
            "rationale": { "type": "string" },
            "blocks": {
              "type": "array",
              "minItems": 1,
              "maxItems": 500,
              "items": {
                "type": "object",
                "required": ["text"],
                "additionalProperties": false,
                "properties": {
                  "text": { "type": "string" },
                  "isHeading": { "type": "boolean" },
                  "headingLevel": { "type": "integer", "minimum": 0, "maximum": 6 },
                  "anchor": { "type": "object" }
                }
              }
            }
          }
        }
        """;

    private const string UpdateSchema = """
        {
          "type": "object",
          "required": ["docId", "expectedVersion", "ops"],
          "additionalProperties": false,
          "properties": {
            "docId": { "type": "string" },
            "expectedVersion": { "type": "integer", "minimum": 1, "description": "乐观并发：先 kb_get 拿当前 seq" },
            "allowBranch": { "type": "boolean", "description": "expectedVersion 指向旧版本时允许拉分支" },
            "rationale": { "type": "string", "description": "为什么改——这是追溯里最值钱的字段" },
            "reason": { "type": "string" },
            "namedVersion": { "type": "string", "description": "requirement 语义版本，如 v1.1 / v2.0；缺省自动 minor" },
            "ops": {
              "type": "array",
              "minItems": 1,
              "maxItems": 200,
              "items": {
                "type": "object",
                "required": ["op"],
                "additionalProperties": false,
                "properties": {
                  "op": { "type": "string", "enum": ["add", "replace", "remove"] },
                  "blockId": { "type": "string", "description": "replace / remove 必填" },
                  "text": { "type": "string", "description": "add / replace 必填" },
                  "position": { "type": "integer", "minimum": 0, "description": "add 的插入位置" },
                  "anchor": { "type": "object" }
                }
              }
            }
          }
        }
        """;

    private const string PromoteSchema = """
        {
          "type": "object",
          "required": ["docId", "retention"],
          "additionalProperties": false,
          "properties": {
            "docId": { "type": "string" },
            "retention": { "type": "string", "enum": ["staging", "durable"] },
            "rationale": { "type": "string" },
            "confirmed": { "type": "boolean", "description": "durable → staging 必须 true" }
          }
        }
        """;

    private const string MergeSchema = """
        {
          "type": "object",
          "required": ["docId", "leftVersion", "rightVersion", "blocks"],
          "additionalProperties": false,
          "properties": {
            "docId": { "type": "string" },
            "leftVersion": { "type": "integer", "minimum": 1 },
            "rightVersion": { "type": "integer", "minimum": 1 },
            "expectedVersion": { "type": "integer", "minimum": 1 },
            "rationale": { "type": "string", "description": "冲突裁决理由" },
            "namedVersion": { "type": "string", "description": "requirement 合并版本，如 v2.0" },
            "blocks": {
              "type": "array",
              "minItems": 1,
              "maxItems": 500,
              "items": {
                "type": "object",
                "required": ["text"],
                "additionalProperties": false,
                "properties": {
                  "text": { "type": "string" },
                  "isHeading": { "type": "boolean" },
                  "headingLevel": { "type": "integer", "minimum": 0, "maximum": 6 }
                }
              }
            }
          }
        }
        """;

    private const string DeleteSchema = """
        {
          "type": "object",
          "required": ["docId"],
          "additionalProperties": false,
          "properties": {
            "docId": { "type": "string" },
            "reason": { "type": "string" }
          }
        }
        """;

    public static IReadOnlyList<KbToolDefinition> Tools { get; } =
    [
        new("kb_search", "检索文档块/历史/变更事实。scope=current 默认只含 durable；recent 含 staging。", SearchSchema,
            RiskLevel.Low, ApprovalMode.None, true, false, true, false),
        new("kb_get", "取某文档某版本的正文、块 id 与 anchor。", GetSchema,
            RiskLevel.Low, ApprovalMode.None, true, false, true, false),
        new("kb_outline", "取目录，供命中不足时浏览兜底。", DocIdSchema,
            RiskLevel.Low, ApprovalMode.None, true, false, true, false),
        new("kb_diff", "块级 + 词级差异。", DiffSchema,
            RiskLevel.Low, ApprovalMode.None, true, false, true, false),
        new("kb_changes", "变更事实流（含 rationale）。", ChangesSchema,
            RiskLevel.Low, ApprovalMode.None, true, false, true, false),
        new("kb_refs", "谁引用我 / 我引用谁 / 影响面清单。", DocIdSchema,
            RiskLevel.Low, ApprovalMode.None, true, false, true, false),
        new("kb_annotate", "写「这段在讲什么」的摘要，不调模型。", AnnotateSchema,
            RiskLevel.Low, ApprovalMode.None, false, false, true, true),
        new("kb_create", "建文档 + v1；块级输入，不接受整篇覆盖。", CreateSchema,
            RiskLevel.Medium, ApprovalMode.None, false, false, false, true),
        new("kb_update", "块级 ops 修订；高风险，必须用户确认。", UpdateSchema,
            RiskLevel.High, ApprovalMode.Always, false, true, false, true),
        new("kb_promote", "升格 / 降格保留等级；降格需二次确认。", PromoteSchema,
            RiskLevel.Medium, ApprovalMode.Once, false, false, true, true),
        new("kb_merge", "把两个分叉版本合并成多父版本；调用方给出已裁决的块序列。", MergeSchema,
            RiskLevel.Medium, ApprovalMode.Once, false, false, false, true),
        new("kb_delete", "软删文档；高风险，必须用户确认。", DeleteSchema,
            RiskLevel.High, ApprovalMode.Always, false, true, false, true),
    ];

    public static KbToolDefinition? Find(string name) =>
        Tools.FirstOrDefault(tool => string.Equals(tool.Name, name, StringComparison.Ordinal));
}
