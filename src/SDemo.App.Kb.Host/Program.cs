using System.Text;
using SDemo.App.Kb.Contracts;
using SDemo.App.Kb.Core;
using SDemo.App.Kb.Host;
using SDemo.App.Kb.Storage;

Console.OutputEncoding = Encoding.UTF8;

var options = HostOptions.Parse(args);
if (options.ShowHelp)
{
    HostOptions.PrintHelp();
    return 0;
}

var database = new KbDatabase(options.DataDirectory);
var store = new KbStore(database);
var service = new KbService(store, new KbBlobStore(database.DataDirectory));

if (options.IssueTokenFor is { Length: > 0 } appId)
{
    var token = service.IssueToken(appId);
    Console.WriteLine(token);
    return 0;
}

if (options.RebuildIndex)
{
    service.RebuildIndexes();
    Console.WriteLine("索引已重建。");
    return 0;
}

if (options.Maintain)
{
    var report = service.RunMaintenance();
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(report, KbJson.Wire));
    return 0;
}

using var server = new KbServer(service);
server.Start(options.Port);
KbRuntimeFile.Write(server.Port, database.DataDirectory);

using var maintenanceCts = new CancellationTokenSource();
var maintenanceTask = Task.Run(async () =>
{
    RunMaintenance(service);
    using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
    try
    {
        while (await timer.WaitForNextTickAsync(maintenanceCts.Token))
        {
            RunMaintenance(service);
        }
    }
    catch (OperationCanceledException)
    {
        // 正常退出。
    }
});

using var stopped = new ManualResetEventSlim(false);
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    stopped.Set();
};
AppDomain.CurrentDomain.ProcessExit += (_, _) => stopped.Set();

Console.WriteLine("sdemo.kb 已就绪：Ctrl+C 退出。");
stopped.Wait();

maintenanceCts.Cancel();
try
{
    maintenanceTask.Wait(TimeSpan.FromSeconds(5));
}
catch (AggregateException)
{
    // 退出时的取消异常。
}

server.Stop();
KbRuntimeFile.Delete();
return 0;

static void RunMaintenance(KbService kb)
{
    try
    {
        var report = kb.RunMaintenance();
        if (report.ExpiredDocs + report.PurgedDocs + report.CollectedBlocks + report.CollectedBlobs > 0)
        {
            Console.WriteLine(
                $"[maintenance] expired={report.ExpiredDocs} purged={report.PurgedDocs} "
                + $"blocks={report.CollectedBlocks} blobs={report.CollectedBlobs} bytes={report.ReclaimedBlobBytes}");
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[maintenance] 失败：{ex.Message}");
    }
}

internal sealed class HostOptions
{
    public string DataDirectory { get; private set; } = KbPaths.DefaultDataDirectory;

    public int Port { get; private set; }

    public string? IssueTokenFor { get; private set; }

    public bool RebuildIndex { get; private set; }

    public bool Maintain { get; private set; }

    public bool ShowHelp { get; private set; }

    public static HostOptions Parse(string[] args)
    {
        var options = new HostOptions();

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            switch (arg)
            {
                case "--help" or "-h":
                    options.ShowHelp = true;
                    break;

                case "--data-dir" when index + 1 < args.Length:
                    options.DataDirectory = Path.GetFullPath(args[++index]);
                    break;

                case "--port" when index + 1 < args.Length:
                    options.Port = int.TryParse(args[++index], out var port) ? port : 0;
                    break;

                case "--issue-token" when index + 1 < args.Length:
                    options.IssueTokenFor = args[++index];
                    break;

                case "--rebuild-index":
                    options.RebuildIndex = true;
                    break;

                case "--maintain":
                    options.Maintain = true;
                    break;

                default:
                    if (arg.StartsWith("--data-dir=", StringComparison.Ordinal))
                    {
                        options.DataDirectory = Path.GetFullPath(arg["--data-dir=".Length..]);
                    }
                    else if (arg.StartsWith("--port=", StringComparison.Ordinal))
                    {
                        options.Port = int.TryParse(arg["--port=".Length..], out var parsedPort) ? parsedPort : 0;
                    }

                    break;
            }
        }

        return options;
    }

    public static void PrintHelp()
    {
        Console.WriteLine("""
            sdemo.kb —— 文档版本、差异、检索与引用追溯的数据面。

            用法：
              SDemo.Kb [--data-dir <目录>] [--port <端口>]
              SDemo.Kb --issue-token <appId>        签发某个 app 的写入 token 并退出
              SDemo.Kb --rebuild-index              整库重建 FTS 索引并退出
              SDemo.Kb --maintain                   跑一次 TTL / 回收站 / GC 并退出

            默认数据目录：%APPDATA%\SDemo\Apps\sdemo.kb
            """);
    }
}
