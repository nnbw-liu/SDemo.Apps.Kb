using System.IO;
using System.Threading;
using System.Windows;
using SDemo.App.Kb.Contracts;
using SDemo.App.Kb.Core;
using SDemo.App.Kb.Host;
using SDemo.App.Kb.Storage;

namespace SDemo.App.Kb.Viewer;

/// <summary>
/// 选项 A：查看器是唯一 UI 进程，内嵌 KbServer。
/// 单实例互斥避免两个进程同时写同一个库；Host 仍保留为无界面/服务模式。
/// </summary>
public partial class App : Application
{
    private Mutex? _mutex;
    private KbServer? _server;
    private CancellationTokenSource? _maintenanceCts;
    private Task? _maintenanceTask;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(initiallyOwned: true, @"Local\SDemo.Kb.Viewer", out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show("sdemo.kb 查看器已经在运行。", "sdemo.kb", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        var dataDirectory = ResolveDataDirectory(e.Args);
        var database = new KbDatabase(dataDirectory);
        var store = new KbStore(database);
        var service = new KbService(store, new KbBlobStore(database.DataDirectory));

        _server = new KbServer(service);
        _server.Start();
        KbRuntimeFile.Write(_server.Port, database.DataDirectory);

        var window = new MainWindow(service);
        MainWindow = window;
        window.Show();

        _maintenanceCts = new CancellationTokenSource();
        _maintenanceTask = Task.Run(async () =>
        {
            RunMaintenance(service);
            using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
            try
            {
                while (await timer.WaitForNextTickAsync(_maintenanceCts.Token))
                {
                    RunMaintenance(service);
                }
            }
            catch (OperationCanceledException)
            {
                // 正常退出。
            }
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _maintenanceCts?.Cancel();
        try
        {
            _maintenanceTask?.Wait(TimeSpan.FromSeconds(3));
        }
        catch (AggregateException)
        {
            // 退出时的取消异常。
        }

        _server?.Dispose();
        KbRuntimeFile.Delete();
        _maintenanceCts?.Dispose();

        if (_mutex is not null)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // 未持有互斥量。
            }

            _mutex.Dispose();
        }

        base.OnExit(e);
    }

    private static string ResolveDataDirectory(string[] args)
    {
        var environment = Environment.GetEnvironmentVariable("SDEMO_KB_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(environment))
        {
            return Path.GetFullPath(environment);
        }

        for (var index = 0; index < args.Length; index++)
        {
            if (string.Equals(args[index], "--data-dir", StringComparison.OrdinalIgnoreCase)
                && index + 1 < args.Length)
            {
                return Path.GetFullPath(args[index + 1]);
            }

            if (args[index].StartsWith("--data-dir=", StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFullPath(args[index]["--data-dir=".Length..]);
            }
        }

        return KbPaths.DefaultDataDirectory;
    }

    private static void RunMaintenance(KbService service)
    {
        try
        {
            service.RunMaintenance();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[kb] maintenance failed: {ex.Message}");
        }
    }
}
