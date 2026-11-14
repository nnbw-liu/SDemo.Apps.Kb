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
        Assert.Equal(12, mcpBody["result"]!["tools"]!.AsArray().Count);
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

    [Fact]
    public async Task 收件箱统计设置与批量归档走回环接口()
    {
        using var kb = new TestKb(DateTimeOffset.UtcNow);
        var staged = kb.CreateStaging("待归档", ["内容"], ttlDays: 5);

        using var server = new KbServer(kb.Service);
        server.Start();
        using var client = Client(server, kb.Token);

        var inbox = await client.GetFromJsonAsync<InboxResult>("/v1/inbox", KbJson.Wire);
        Assert.NotNull(inbox);
        Assert.Equal(1, inbox!.Total);
        Assert.Single(inbox.Items);

        var stats = await client.GetFromJsonAsync<StorageStats>("/v1/stats", KbJson.Wire);
        Assert.NotNull(stats);
        Assert.Equal(1, stats!.StagingCount);

        var settings = await client.GetFromJsonAsync<RetentionSettings>("/v1/settings/retention", KbJson.Wire);
        Assert.NotNull(settings);
        settings!.AgentTtlDays = 3;
        var put = await client.PutAsJsonAsync("/v1/settings/retention", settings, KbJson.Wire);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var saved = await put.Content.ReadFromJsonAsync<RetentionSettings>(KbJson.Wire);
        Assert.Equal(3, saved!.AgentTtlDays);

        var batch = await client.PostAsJsonAsync(
            "/v1/promote-batch",
            new PromoteBatchRequest
            {
                DocIds = [staged.DocId],
                Retention = RetentionLevel.Durable,
            },
            KbJson.Wire);
        Assert.Equal(HttpStatusCode.OK, batch.StatusCode);
        var promoted = await batch.Content.ReadFromJsonAsync<PromoteBatchResult>(KbJson.Wire);
        Assert.Single(promoted!.Promoted);
        Assert.Empty(promoted.Failed);
    }

    [Fact]
    public async Task 普通App不能读收件箱()
    {
        using var kb = new TestKb();
        using var server = new KbServer(kb.Service);
        server.Start();

        var appToken = kb.Service.IssueToken("sdemo.meeting");
        using var client = Client(server, appToken);

        var inbox = await client.GetAsync("/v1/inbox");
        Assert.Equal(HttpStatusCode.Forbidden, inbox.StatusCode);
    }

    [Fact]
    public async Task 外部应用可以登记与撤销引用()
    {
        using var kb = new TestKb();
        var target = kb.Ingest("目标文档", ["被引用的块"]);
        var blockId = kb.Service.Get(new KbGetRequest { DocId = target.DocId })!.Blocks[0].BlockId;

        using var server = new KbServer(kb.Service);
        server.Start();

        var plannerToken = kb.Service.IssueToken("sdemo.planner");
        using var client = Client(server, plannerToken);

        var registration = new RefRegistrationRequest
        {
            Source = new IngestSource
            {
                AppId = "sdemo.planner",
                EntityUri = "sdemo.planner/todo/http-ref",
            },
            To = KbUri.Format(target.DocId, 1, blockId),
            Rel = "based_on",
        };

        var register = await client.PostAsJsonAsync("/v1/refs", registration, KbJson.Wire);
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);
        var registered = await register.Content.ReadFromJsonAsync<RefRegistrationResult>(KbJson.Wire);
        Assert.True(registered!.Created);

        var list = await client.GetFromJsonAsync<RefListResult>(
            "/v1/refs?fromUri=sdemo.planner%2Ftodo%2Fhttp-ref",
            KbJson.Wire);
        Assert.NotNull(list);
        Assert.Single(list!.Items);
        Assert.Equal("sdemo.planner", list.Items[0].OwnerApp);

        var retract = await client.PostAsJsonAsync(
            "/v1/refs/retract",
            new RefRetractRequest
            {
                Source = new IngestSource
                {
                    AppId = "sdemo.planner",
                    EntityUri = "sdemo.planner/todo/http-ref",
                },
            },
            KbJson.Wire);
        Assert.Equal(HttpStatusCode.OK, retract.StatusCode);
        var retracted = await retract.Content.ReadFromJsonAsync<RefRetractResult>(KbJson.Wire);
        Assert.Equal(1, retracted!.Removed);
    }
}

