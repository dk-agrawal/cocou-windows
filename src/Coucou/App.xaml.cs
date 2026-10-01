using System.Windows;
using Coucou.Services;

namespace Coucou;

public partial class App : Application
{
    private MainWindow? _window;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _window = new MainWindow(new ClaudeSessionDiscovery(), LocalSettings.Load());
        _window.Show();
    }
}
