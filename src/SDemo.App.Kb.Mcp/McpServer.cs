using System.Text.Json;
using System.Text.Json.Nodes;
using SDemo.App.Kb.Contracts;
using SDemo.App.Kb.Core;

namespace SDemo.App.Kb.Mcp;

/// <summary>
/// MCP 的 JSON-RPC 处理核心（传输在 Host 的 KbServer 里）。
/// 只实现工具面子集：initialize / ping / tools/list / tools/call。
/// 读面在这里，写面在回环 HTTP（kb-design §3.1：写面不经过 MCP）。
/// </summary>
public sealed class McpServer
{
    public const string ProtocolVersion = "2025-06-18";
    private const string Actor = "sdemo.agent";

    private static readonly string[] KnownProtocolVersions =
        ["2024-11-05", "2025-03-26", "2025-06-18"];

    private readonly KbService _service;

    public McpServer(KbService service)
    {
        _service = service;
    }

    public string HandleJson(string body)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            return ErrorResponse(null, -32700, "Parse error：请求体不是合法 JSON").ToJsonString();
        }

        if (root is JsonArray array)
        {
            var results = new JsonArray();
            foreach (var item in array)
            {
                var response = HandleSingle(item);
                if (response is not null)
                {
                    results.Add(response);
                }
            }

            return results.Count == 0 ? string.Empty : results.ToJsonString();
        }

        return HandleSingle(root)?.ToJsonString() ?? string.Empty;
    }

    private JsonObject? HandleSingle(JsonNode? node)
    {
        if (node is not JsonObject request)
        {
            return ErrorResponse(null, -32600, "Invalid Request：不是 JSON-RPC 对象");
        }

        var id = request["id"]?.DeepClone();
        var methodNode = request["method"];
        if (methodNode is null || methodNode.GetValueKind() != JsonValueKind.String)
        {
            return id is null ? null : ErrorResponse(id, -32600, "Invalid Request：缺少 method");
        }

        var method = methodNode.GetValue<string>();
        var isNotification = id is null;

        try
        {
            switch (method)
            {
                case "initialize":
                    return isNotification ? null : RpcResult(id, InitializeResult(request["params"]));

                case "notifications/initialized":
                case "notifications/cancelled":
                case "notifications/roots/list_changed":
                    return null;

                case "ping":
                    return isNotification ? null : RpcResult(id, new JsonObject());

                case "tools/list":
                    return isNotification ? null : RpcResult(id, ToolsListResult());

                case "tools/call":
                    return isNotification ? null : RpcResult(id, HandleToolCall(request["params"] as JsonObject));

                default:
                    return isNotification ? null : ErrorResponse(id, -32601, $"Method not found：{method}");
            }
        }
        catch (Exception ex)
        {
            return isNotification ? null : ErrorResponse(id, -32603, $"Internal error：{ex.GetType().Name}");
        }
    }

    private static JsonObject InitializeResult(JsonNode? parameters)
    {
        var requested = parameters?["protocolVersion"]?.GetValueKind() == JsonValueKind.String
            ? parameters["protocolVersion"]!.GetValue<string>()
            : ProtocolVersion;

        var negotiated = KnownProtocolVersions.Contains(requested, StringComparer.Ordinal)
            ? requested
            : ProtocolVersion;

        return new JsonObject
        {
            ["protocolVersion"] = negotiated,
            ["capabilities"] = new JsonObject
            {
                ["tools"] = new JsonObject { ["listChanged"] = false },
            },
            ["serverInfo"] = new JsonObject
            {
                ["name"] = KbConstants.AppId,
                ["title"] = KbConstants.AppTitle,
                ["version"] = KbConstants.AppVersion,
            },
            ["instructions"] =
                "sdemo.kb 只做「存 + 找」：检索、正文、历史、diff、引用与影响面。"
                + "KB 不持有模型凭证，回答由 DesktopAgent 生成。"
                + "kb_update 只接受块级 ops（add/replace/remove），禁止整篇提交；kb_update / kb_delete 为高风险，必须用户确认。"
                + "引用一律用 sdemo.kb/doc/<docId>@v<seq>#<blockId>，必须带版本。",
        };
    }

    private JsonObject ToolsListResult()
    {
        var tools = new JsonArray();
        foreach (var tool in McpToolCatalog.Tools)
        {
            tools.Add(tool.ToJson());
        }

        return new JsonObject { ["tools"] = tools };
    }

    private JsonObject HandleToolCall(JsonObject? parameters)
    {
        var name = parameters?["name"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(name))
        {
            return ToolError(KbErrorCodes.InvalidRequest, "缺少工具名。");
        }

        var definition = McpToolCatalog.Find(name);
        if (definition is null)
        {
            return ToolError(KbErrorCodes.InvalidRequest, $"未知工具：{name}");
        }

        if (parameters?["arguments"] is not JsonObject arguments)
        {
            arguments = new JsonObject();
        }

        try
        {
            var result = name switch
            {
                "kb_search" => _service.Search(Deserialize<KbSearchRequest>(arguments)),
                "kb_get" => _service.Get(Deserialize<KbGetRequest>(arguments))
                    ?? throw KbException.NotFound("文档或版本不存在。"),
                "kb_outline" => _service.Outline(Required(arguments, "docId")),
                "kb_diff" => _service.Diff(Deserialize<KbDiffRequest>(arguments)),
                "kb_changes" => _service.Changes(Deserialize<KbChangesRequest>(arguments)),
                "kb_refs" => _service.Refs(Required(arguments, "docId")),
                "kb_annotate" => Annotate(Deserialize<KbAnnotateRequest>(arguments)),
                "kb_create" => _service.Create(Deserialize<KbCreateRequest>(arguments), Actor),
                "kb_update" => _service.Update(Deserialize<KbUpdateRequest>(arguments), Actor),
                "kb_promote" => _service.Promote(Deserialize<KbPromoteRequest>(arguments), Actor),
                "kb_delete" => _service.Delete(Deserialize<KbDeleteRequest>(arguments), Actor),
                _ => throw KbException.Invalid($"未知工具：{name}"),
            };

            return ToolOk(result, definition);
        }
        catch (KbException ex)
        {
            return ToolError(ex.Code, ex.Message);
        }
        catch (JsonException ex)
        {
            return ToolError(KbErrorCodes.InvalidRequest, $"参数不合法：{ex.Message}");
        }
    }

    private object Annotate(KbAnnotateRequest request)
    {
        _service.Annotate(request, "ai");
        return new { ok = true, docId = request.DocId };
    }

    private static T Deserialize<T>(JsonObject arguments) =>
        JsonSerializer.Deserialize<T>(arguments.ToJsonString(), KbJson.Wire)
        ?? throw KbException.Invalid("参数不能为空。");

    private static string Required(JsonObject arguments, string name) =>
        arguments[name]?.GetValue<string>() is { Length: > 0 } value
            ? value
            : throw KbException.Invalid($"缺少参数 {name}。");

    private static JsonObject ToolOk(object? payload, KbToolDefinition definition) => new()
    {
        ["content"] = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "text",
                ["text"] = KbJson.Serialize(payload),
            },
        },
        ["isError"] = false,
        ["_meta"] = new JsonObject
        {
            ["sdemo/risk"] = definition.Risk.ToString().ToLowerInvariant(),
        },
    };

    private static JsonObject ToolError(string code, string message) => new()
    {
        ["content"] = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "text",
                ["text"] = KbJson.Serialize(new { error = new { code, message } }),
            },
        },
        ["isError"] = true,
    };

    private static JsonObject RpcResult(JsonNode? id, JsonNode result) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id?.DeepClone(),
        ["result"] = result,
    };

    private static JsonObject ErrorResponse(JsonNode? id, int code, string message) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id?.DeepClone(),
        ["error"] = new JsonObject
        {
            ["code"] = code,
            ["message"] = message,
        },
    };
}
