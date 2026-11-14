using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using SDemo.App.Kb.Contracts;

namespace SDemo.App.Kb.Core;

/// <summary>
/// 写入方客户端：优先直投 KB；网络失败自动落 outbox 后返回 <c>null</c>，绝不把异常抛回业务事务。
/// 只用于 App 侧；KB 自身不引入业务调用。
/// </summary>
public sealed class KbIngestClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly KbOutbox? _outbox;
    private readonly string _appId;

    public KbIngestClient(string baseUrl, string token, string appId, KbOutbox? outbox = null, HttpClient? http = null)
    {
        _http = http ?? new HttpClient();
        _http.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _http.Timeout = TimeSpan.FromSeconds(5);
        _outbox = outbox;
        _appId = appId;
    }

    public async Task<WriteResult?> IngestAsync(IngestRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.PostAsJsonAsync(
                KbConstants.IngestPath.TrimStart('/'),
                request,
                KbJson.Wire,
                cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                // 4xx 是契约错误（调用方 bug），不应无限重试；直接抛出。
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                throw new KbException(
                    KbErrorCodes.InvalidRequest,
                    $"KB 拒绝写入（{(int)response.StatusCode}）：{body}");
            }

            return await response.Content.ReadFromJsonAsync<WriteResult>(KbJson.Wire, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (KbException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            _outbox?.Enqueue(_appId, request.Source?.EntityUri ?? string.Empty, KbJson.Serialize(request));
            return null;
        }
    }

    public async Task<WriteResult?> ReviseAsync(ReviseRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.PostAsJsonAsync(
                KbConstants.RevisePath.TrimStart('/'),
                request,
                KbJson.Wire,
                cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                throw new KbException(KbErrorCodes.InvalidRequest, $"KB 拒绝修订：{body}");
            }

            return await response.Content.ReadFromJsonAsync<WriteResult>(KbJson.Wire, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (KbException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            _outbox?.Enqueue(_appId, "kb_revise:" + request.DocId, KbJson.Serialize(request));
            return null;
        }
    }

    /// <summary>把 outbox 里到期的条目补投；返回成功条数。</summary>
    public async Task<int> FlushAsync(CancellationToken cancellationToken = default)
    {
        if (_outbox is null)
        {
            return 0;
        }

        var delivered = 0;
        foreach (var item in _outbox.Due(DateTimeOffset.UtcNow))
        {
            try
            {
                using var content = new StringContent(item.Payload, Encoding.UTF8, "application/json");
                var path = item.EntityUri.StartsWith("kb_revise:", StringComparison.Ordinal)
                    ? KbConstants.RevisePath.TrimStart('/')
                    : KbConstants.IngestPath.TrimStart('/');

                using var response = await _http.PostAsync(path, content, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    _outbox.MarkDelivered(item.Id);
                    delivered++;
                }
                else
                {
                    _outbox.MarkFailed(item.Id, $"HTTP {(int)response.StatusCode}", DateTimeOffset.UtcNow);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
                _outbox.MarkFailed(item.Id, ex.Message, DateTimeOffset.UtcNow);
            }
        }

        return delivered;
    }

    public void Dispose() => _http.Dispose();
}
