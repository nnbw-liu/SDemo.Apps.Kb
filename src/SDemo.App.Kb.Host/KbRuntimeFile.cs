using System.Diagnostics;
using System.Text.Json;
using SDemo.App.Kb.Contracts;

namespace SDemo.App.Kb.Host;

/// <summary>运行期连接信息：只写回环地址与端口，不写任何旧 token。</summary>
internal static class KbRuntimeFile
{
    public static void Write(int port, string dataDirectory)
    {
        Directory.CreateDirectory(KbPaths.RuntimeDirectory);

        var payload = new
        {
            appId = KbConstants.AppId,
            pid = Environment.ProcessId,
            port,
            startedAt = DateTimeOffset.UtcNow.ToString("O"),
            appVersion = KbConstants.AppVersion,
            ingestUrl = $"http://127.0.0.1:{port}{KbConstants.IngestPath}",
            resolveUrl = $"http://127.0.0.1:{port}{KbConstants.ResolvePath}",
            mcpUrl = $"http://127.0.0.1:{port}{KbConstants.McpPath}",
            healthUrl = $"http://127.0.0.1:{port}{KbConstants.HealthPath}",
            dataDirectory,
        };

        var path = KbPaths.RuntimeFile();
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(payload, KbJson.Wire));
        File.Move(temp, path, overwrite: true);
    }

    public static void Delete()
    {
        try
        {
            var path = KbPaths.RuntimeFile();
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // 退出清理失败不阻塞。
        }
    }
}
