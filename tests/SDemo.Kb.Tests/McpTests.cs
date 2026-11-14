using System.Text.Json;
using System.Text.Json.Nodes;
using SDemo.App.Kb.Contracts;
using SDemo.App.Kb.Mcp;

namespace SDemo.Kb.Tests;

public sealed class McpTests
{
    private static JsonObject Call(McpServer server, string tool, object arguments)
    {
        var request = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 1,
            ["method"] = "tools/call",
            ["params"] = new JsonObject
            {
                ["name"] = tool,
                ["arguments"] = JsonNode.Parse(JsonSerializer.Serialize(arguments, KbJson.Wire)),
            },
        };

        var response = JsonNode.Parse(server.HandleJson(request.ToJsonString()))!.AsObject();
        return response["result"]!.AsObject();
    }

    private static T Payload<T>(JsonObject result) =>
        JsonSerializer.Deserialize<T>(result["content"]![0]!["text"]!.GetValue<string>(), KbJson.Wire)!;

    [Fact]
    public void 工具面暴露12个工具且写操作带风险()
    {
        using var kb = new TestKb();
        var server = new McpServer(kb.Service);

        var response = JsonNode.Parse(server.HandleJson(
            """{"jsonrpc":"2.0","id":1,"method":"tools/list"}"""))!.AsObject();
        var tools = response["result"]!["tools"]!.AsArray();

        Assert.Equal(12, tools.Count);

        var update = tools.Single(tool => tool!["name"]!.GetValue<string>() == "kb_update")!;
        Assert.Equal("high", update["_meta"]!["sdemo/risk"]!.GetValue<string>());
        Assert.Equal("always", update["_meta"]!["sdemo/approval"]!.GetValue<string>());
        Assert.True(update["_meta"]!["sdemo/writes"]!.GetValue<bool>());

        var search = tools.Single(tool => tool!["name"]!.GetValue<string>() == "kb_search")!;
        Assert.Equal("low", search["_meta"]!["sdemo/risk"]!.GetValue<string>());
        Assert.True(search["annotations"]!["readOnlyHint"]!.GetValue<bool>());
    }

    [Fact]
    public void create_get_update与search走通()
    {
        using var kb = new TestKb();
        var server = new McpServer(kb.Service);

        var created = Call(server, "kb_create", new
        {
            kind = "reference",
            title = "MCP 文档",
            blocks = new[] { new { text = "MCP 创建的第一段" }, new { text = "第二段" } },
        });

        Assert.False(created["isError"]!.GetValue<bool>());
        var write = Payload<WriteResult>(created);
        Assert.Equal(1, write.Seq);

        var got = Payload<DocContent>(Call(server, "kb_get", new { docId = write.DocId }));
        Assert.Equal(2, got.Blocks.Count);

        var updated = Call(server, "kb_update", new
        {
            docId = write.DocId,
            expectedVersion = 1,
            rationale = "用户确认改成新方案",
            ops = new[] { new { op = "replace", blockId = got.Blocks[0].BlockId, text = "MCP 改过的第一段" } },
        });

        Assert.False(updated["isError"]!.GetValue<bool>());
        Assert.Equal(2, Payload<WriteResult>(updated).Seq);

        var search = Payload<KbSearchResponse>(Call(server, "kb_search", new
        {
            query = "改过",
            scope = "recent",
        }));

        Assert.False(search.NoHit);
        Assert.Contains(search.Hits, hit => hit.DocId == write.DocId);
    }

    [Fact]
    public void 未知工具返回工具级错误()
    {
        using var kb = new TestKb();
        var server = new McpServer(kb.Service);

        var result = Call(server, "kb_nope", new { });

        Assert.True(result["isError"]!.GetValue<bool>());
        Assert.Contains(KbErrorCodes.InvalidRequest, result["content"]![0]!["text"]!.GetValue<string>());
    }

    [Fact]
    public void initialize与ping可用()
    {
        using var kb = new TestKb();
        var server = new McpServer(kb.Service);

        var init = JsonNode.Parse(server.HandleJson(
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18"}}"""))!.AsObject();

        Assert.Equal("sdemo.kb", init["result"]!["serverInfo"]!["name"]!.GetValue<string>());
        Assert.Equal("2025-06-18", init["result"]!["protocolVersion"]!.GetValue<string>());

        var ping = JsonNode.Parse(server.HandleJson(
            """{"jsonrpc":"2.0","id":2,"method":"ping"}"""))!.AsObject();
        Assert.NotNull(ping["result"]);
    }
}

