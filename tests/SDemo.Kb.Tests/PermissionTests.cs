using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using SDemo.App.Kb.Contracts;
using SDemo.App.Kb.Host;

namespace SDemo.Kb.Tests;

public sealed class PermissionTests
{
    [Fact]
    public void 其他App不能修订别人的文档()
    {
        using var kb = new TestKb();
        var created = kb.Ingest("KB 自己的文档", ["原文"]);
        var v1 = kb.Service.Get(new KbGetRequest { DocId = created.DocId })!;

        var request = new ReviseRequest
        {
            Source = new IngestSource { AppId = "sdemo.meeting" },
            DocId = created.DocId,
            ExpectedVersion = 1,
            Blocks = [new BlockInput { Op = ChangeOp.Replace, BlockId = v1.Blocks[0].BlockId, Text = "偷改" }],
        };

        var error = Assert.Throws<KbException>(() => kb.Service.Revise(request, "sdemo.meeting"));
        Assert.Equal(KbErrorCodes.Forbidden, error.Code);
    }

    [Fact]
    public async Task 普通App不能走MCP也不能promote()
    {
        using var kb = new TestKb();
        using var server = new KbServer(kb.Service);
        server.Start();

        var appToken = kb.Service.IssueToken("sdemo.meeting");
        using var client = new HttpClient { BaseAddress = new Uri(server.BaseUrl) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", appToken);

        var mcp = await client.PostAsJsonAsync(
            "/mcp",
            System.Text.Json.Nodes.JsonNode.Parse(
                """{"jsonrpc":"2.0","id":1,"method":"tools/list"}"""),
            KbJson.Wire);
        Assert.Equal(HttpStatusCode.Forbidden, mcp.StatusCode);

        var promote = await client.PostAsJsonAsync(
            "/v1/promote",
            new PromoteRequest { DocId = "any", Retention = RetentionLevel.Durable },
            KbJson.Wire);
        Assert.Equal(HttpStatusCode.Forbidden, promote.StatusCode);
    }
}
