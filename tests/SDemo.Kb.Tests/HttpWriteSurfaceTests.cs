using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using SDemo.App.Kb.Contracts;
using SDemo.App.Kb.Host;

namespace SDemo.Kb.Tests;

public sealed class HttpWriteSurfaceTests
{
    private static HttpClient Client(KbServer server, string? token)
    {
        var client = new HttpClient { BaseAddress = new Uri(server.BaseUrl), Timeout = TimeSpan.FromSeconds(10) };
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    [Fact]
    public async Task 健康检查免鉴权_写入需要token()
    {
        using var kb = new TestKb();
        using var server = new KbServer(kb.Service);
        server.Start();

        using var anonymous = Client(server, null);
        var health = await anonymous.GetAsync("/v1/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);

        var unauthorized = await anonymous.PostAsJsonAsync("/v1/ingest", new IngestRequest(), KbJson.Wire);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        using var bad = Client(server, "not-a-token");
        var rejected = await bad.PostAsJsonAsync("/v1/ingest", new IngestRequest(), KbJson.Wire);
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
    }

    [Fact]
    public async Task ingest_resolve与mcp走同一回环端口()
    {
        using var kb = new TestKb();
        using var server = new KbServer(kb.Service);
        server.Start();
        using var client = Client(server, kb.Token);

        var request = new IngestRequest
        {
            Source = new IngestSource { AppId = KbConstants.AppId, EntityUri = "sdemo.kb/import/http" },
            Kind = KbKind.Reference,
            Title = "HTTP 文档",
            RationaleSource = RationaleSource.Import,
            Blocks = [new BlockInput { Text = "通过回环 HTTP 写入的内容" }],
        };

        var ingest = await client.PostAsJsonAsync("/v1/ingest", request, KbJson.Wire);
        Assert.Equal(HttpStatusCode.OK, ingest.StatusCode);
        var write = await ingest.Content.ReadFromJsonAsync<WriteResult>(KbJson.Wire);
        Assert.NotNull(write);
        Assert.Equal(1, write!.Seq);

        var uri = KbUri.Format(write.DocId, 1, write.BlockIds[0]);
        var resolve = await client.PostAsJsonAsync(
            "/v1/resolve",
            new ResolveRequest { Uri = uri },
            KbJson.Wire);
        Assert.Equal(HttpStatusCode.OK, resolve.StatusCode);
        var resolved = await resolve.Content.ReadFromJsonAsync<ResolveResult>(KbJson.Wire);
        Assert.True(resolved!.Exists);
        Assert.False(resolved.Stale);

        var mcp = await client.PostAsJsonAsync(
            "/mcp",
            JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}"""),
            KbJson.Wire);
        Assert.Equal(HttpStatusCode.OK, mcp.StatusCode);
        var mcpBody = JsonNode.Parse(await mcp.Content.ReadAsStringAsync())!;
        Assert.Equal(11, mcpBody["result"]!["tools"]!.AsArray().Count);
    }

    [Fact]
    public async Task resolve对不存在的URI返回404()
    {
        using var kb = new TestKb();
        using var server = new KbServer(kb.Service);
        server.Start();
        using var client = Client(server, kb.Token);

        var resolve = await client.PostAsJsonAsync(
            "/v1/resolve",
            new ResolveRequest { Uri = "sdemo.kb/doc/not-exist@v1" },
            KbJson.Wire);

        Assert.Equal(HttpStatusCode.NotFound, resolve.StatusCode);
    }
}
