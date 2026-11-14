using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SDemo.App.Kb.Core;

namespace SDemo.App.Kb.Viewer;

public partial class MainWindow : Window
{
    public MainWindow(KbService service)
    {
        InitializeComponent();

        var services = new ServiceCollection();
        services.AddWpfBlazorWebView();
        services.AddSingleton(service);
        Blazor.Services = services.BuildServiceProvider();
    }
}
