using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using SDemo.App.Kb.Contracts;
using SDemo.App.Kb.Core;
using SDemo.App.Kb.Mcp;

namespace SDemo.App.Kb.Host;

/// <summary>
/// 回环 HTTP 面：写法（ingest / revise / promote / retract）+ resolve + MCP 传输。
/// 只监听 127.0.0.1；除 /v1/health 外全部校验 Bearer token（kb-design §3.2 / §5.1）。
/// </summary>
public sealed class KbServer : IDisposable
{
    private readonly KbService _service;
    private readonly McpServer _mcp;
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;

    private HttpListener? _listener;
    private Task? _loop;
    private int _stopped;

    public KbServer(KbService service)
    {
        _service = service;
        _mcp = new McpServer(service);
    }

    public int Port { get; private set; }

    public string BaseUrl => $"http://127.0.0.1:{Port}";

    public void Start(int port = 0)
    {
        Exception? last = null;

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var candidate = port > 0 && attempt == 0 ? port : FindFreePort();
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{candidate}/");

            try
            {
                listener.Start();
                _listener = listener;
                Port = candidate;
                _loop = Task.Run(LoopAsync);
                KbLog.Info($"KB 已启动：{BaseUrl}（数据目录 {_service.Store.Database.DataDirectory}）");
                return;
            }
            catch (HttpListenerException ex)
            {
                last = ex;
                listener.Close();
                KbLog.Warn($"绑定 {candidate} 失败：{ex.Message}");
            }
        }

        throw new InvalidOperationException("无法绑定本机回环端口。", last);
    }

    public void Stop()
    {
        if (Interlocked.Exchange(ref _stopped, 1) == 1)
        {
            return;
        }

        try
        {
            _listener?.Stop();
        }
        catch (ObjectDisposedException)
        {
            // 已释放。
        }
    }

    public void Dispose()
    {
        Stop();

        try
        {
            _listener?.Close();
        }
        catch (ObjectDisposedException)
        {
            // 已释放。
        }

        try
        {
            _loop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // 停止时的取消异常。
        }
    }

    public static int FindFreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private async Task LoopAsync()
    {
        while (_stopped == 0)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener!.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception) when (_stopped == 1)
            {
                return;
            }
            catch (HttpListenerException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            _ = Task.Run(() => HandleAsync(context));
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            var path = request.Url?.AbsolutePath.TrimEnd('/') ?? string.Empty;
            if (path.Length == 0)
            {
                path = "/";
            }

            if (path is "/v1/health" or "/health")
            {
                await WriteJsonAsync(response, 200, new
                {
                    ok = true,
                    appId = KbConstants.AppId,
                    version = KbConstants.AppVersion,
                    uptimeSeconds = (int)(DateTimeOffset.UtcNow - _startedAt).TotalSeconds,
                }).ConfigureAwait(false);
                return;
            }

            var appId = Authenticate(request);
            if (appId is null)
            {
                await WriteJsonAsync(response, 401, Error(KbErrorCodes.UnauthorizedApp, "缺少或无效的 Bearer token。"))
                    .ConfigureAwait(false);
                return;
            }

            // 权限矩阵（kb-design §5.3）：写面归各 App；promote / retract / 收件箱 / 统计 / 设置 / MCP 只给 KB 与 Agent。
            var privilegedOnly = path is "/v1/promote" or "/v1/retract" or "/v1/promote-batch"
                or "/v1/inbox" or "/v1/stats" or "/v1/settings/retention" or "/v1/maintain" or "/mcp";

            if (privilegedOnly && !KbService.IsPrivileged(appId))
            {
                await WriteJsonAsync(response, 403, Error(KbErrorCodes.Forbidden, "该 app 没有这项能力。"))
                    .ConfigureAwait(false);
                return;
            }

            switch (path)
            {
                case "/v1/ingest" when IsPost(request):
                {
                    var body = await ReadBodyAsync(request).ConfigureAwait(false);
                    var payload = KbJson.Deserialize<IngestRequest>(body)
                        ?? throw KbException.Invalid("请求体为空。");
                    var result = _service.Ingest(payload, appId);
                    await WriteJsonAsync(response, 200, result).ConfigureAwait(false);
                    return;
                }

                case "/v1/revise" when IsPost(request):
                {
                    var body = await ReadBodyAsync(request).ConfigureAwait(false);
                    var payload = KbJson.Deserialize<ReviseRequest>(body)
                        ?? throw KbException.Invalid("请求体为空。");
                    var result = _service.Revise(payload, appId);
                    await WriteJsonAsync(response, 200, result).ConfigureAwait(false);
                    return;
                }

                case "/v1/promote" when IsPost(request):
                {
                    var body = await ReadBodyAsync(request).ConfigureAwait(false);
                    var payload = KbJson.Deserialize<PromoteRequest>(body)
                        ?? throw KbException.Invalid("请求体为空。");
                    var result = _service.Promote(
                        new KbPromoteRequest
                        {
                            DocId = payload.DocId,
                            Retention = payload.Retention,
                            Rationale = payload.Rationale,
                            Confirmed = payload.Confirmed,
                        },
                        appId);
                    await WriteJsonAsync(response, 200, result).ConfigureAwait(false);
                    return;
                }

                case "/v1/retract" when IsPost(request):
                {
                    var body = await ReadBodyAsync(request).ConfigureAwait(false);
                    var payload = KbJson.Deserialize<RetractRequest>(body)
                        ?? throw KbException.Invalid("请求体为空。");
                    var result = _service.Delete(
                        new KbDeleteRequest { DocId = payload.DocId, Reason = payload.Reason },
                        appId);
                    await WriteJsonAsync(response, 200, result).ConfigureAwait(false);
                    return;
                }

                case "/v1/resolve":
                {
                    var uri = request.QueryString["uri"];
                    if (string.IsNullOrWhiteSpace(uri) && IsPost(request))
                    {
                        var body = await ReadBodyAsync(request).ConfigureAwait(false);
                        uri = KbJson.Deserialize<ResolveRequest>(body)?.Uri;
                    }

                    var result = _service.Resolve(uri ?? string.Empty);
                    await WriteJsonAsync(response, result.Exists ? 200 : 404, result).ConfigureAwait(false);
                    return;
                }

                case "/v1/inbox":
                {
                    var limit = int.TryParse(request.QueryString["limit"], out var parsed) ? parsed : 100;
                    await WriteJsonAsync(response, 200, _service.Inbox(limit)).ConfigureAwait(false);
                    return;
                }

                case "/v1/promote-batch" when IsPost(request):
                {
                    var body = await ReadBodyAsync(request).ConfigureAwait(false);
                    var payload = KbJson.Deserialize<PromoteBatchRequest>(body)
                        ?? throw KbException.Invalid("请求体为空。");
                    await WriteJsonAsync(response, 200, _service.PromoteMany(payload, appId)).ConfigureAwait(false);
                    return;
                }

                case "/v1/stats":
                {
                    await WriteJsonAsync(response, 200, _service.GetStorageStats()).ConfigureAwait(false);
                    return;
                }

                case "/v1/settings/retention" when IsPost(request) || string.Equals(request.HttpMethod, "PUT", StringComparison.OrdinalIgnoreCase):
                {
                    var body = await ReadBodyAsync(request).ConfigureAwait(false);
                    var payload = KbJson.Deserialize<RetentionSettings>(body)
                        ?? throw KbException.Invalid("请求体为空。");
                    _service.SaveRetentionSettings(payload, appId);
                    await WriteJsonAsync(response, 200, _service.GetRetentionSettings()).ConfigureAwait(false);
                    return;
                }

                case "/v1/settings/retention":
                {
                    await WriteJsonAsync(response, 200, _service.GetRetentionSettings()).ConfigureAwait(false);
                    return;
                }

                case "/v1/maintain" when IsPost(request):
                {
                    await WriteJsonAsync(response, 200, _service.RunMaintenance()).ConfigureAwait(false);
                    return;
                }

                case "/mcp" when IsPost(request):
                {
                    var body = await ReadBodyAsync(request).ConfigureAwait(false);
                    var json = _mcp.HandleJson(body);
                    if (string.IsNullOrEmpty(json))
                    {
                        response.StatusCode = 202;
                        response.Close();
                        return;
                    }

                    await WriteRawAsync(response, 200, json).ConfigureAwait(false);
                    return;
                }

                default:
                    await WriteJsonAsync(response, 404, Error(KbErrorCodes.NotFound, $"未知路径：{path}"))
                        .ConfigureAwait(false);
                    return;
            }
        }
        catch (KbException ex)
        {
            await WriteJsonAsync(response, StatusFor(ex.Code), Error(ex.Code, ex.Message)).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            await WriteJsonAsync(response, 400, Error(KbErrorCodes.InvalidRequest, $"JSON 不合法：{ex.Message}"))
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            KbLog.Error($"处理 {request.Url} 失败：{ex}");
            await WriteJsonAsync(response, 500, Error(KbErrorCodes.Internal, "KB 内部错误。"))
                .ConfigureAwait(false);
        }
    }

    private string? Authenticate(HttpListenerRequest request)
    {
        var header = request.Headers["Authorization"];
        if (string.IsNullOrWhiteSpace(header)
            || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return _service.Authenticate(header["Bearer ".Length..].Trim());
    }

    private static bool IsPost(HttpListenerRequest request) =>
        string.Equals(request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase);

    private static async Task<string> ReadBodyAsync(HttpListenerRequest request)
    {
        if (request.ContentLength64 > KbConstants.MaxRequestBytes)
        {
            throw new KbException(KbErrorCodes.TooLarge, "请求体超过上限。");
        }

        using var stream = request.InputStream;
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;

        while ((read = await stream.ReadAsync(chunk).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > KbConstants.MaxRequestBytes)
            {
                throw new KbException(KbErrorCodes.TooLarge, "请求体超过上限。");
            }

            buffer.Write(chunk, 0, read);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, int status, object payload)
    {
        var json = JsonSerializer.Serialize(payload, KbJson.Wire);
        await WriteRawAsync(response, status, json).ConfigureAwait(false);
    }

    private static async Task WriteRawAsync(HttpListenerResponse response, int status, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        response.StatusCode = status;
        response.ContentType = "application/json; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        response.Close();
    }

    private static object Error(string code, string message) => new { error = new { code, message } };

    private static int StatusFor(string code) => code switch
    {
        KbErrorCodes.UnauthorizedApp => 401,
        KbErrorCodes.Forbidden => 403,
        KbErrorCodes.NotFound or KbErrorCodes.VersionNotFound or KbErrorCodes.BlockNotFound => 404,
        KbErrorCodes.VersionConflict => 409,
        KbErrorCodes.DocDeleted => 410,
        KbErrorCodes.TooLarge => 413,
        _ => 400,
    };
}

/// <summary>控制台日志；KB 无窗口，日志只走 stdout。</summary>
internal static class KbLog
{
    public static void Info(string message) =>
        Console.WriteLine($"[{DateTimeOffset.Now:HH:mm:ss}] INFO  {message}");

    public static void Warn(string message) =>
        Console.WriteLine($"[{DateTimeOffset.Now:HH:mm:ss}] WARN  {message}");

    public static void Error(string message) =>
        Console.Error.WriteLine($"[{DateTimeOffset.Now:HH:mm:ss}] ERROR {message}");
}
