using System.IO;
using System.Windows;
using Coucou.Services;

namespace Coucou;

public partial class App : System.Windows.Application
{
    private MainWindow? _window;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        _window = new MainWindow(new ClaudeSessionDiscovery(), LocalSettings.Load());
        _window.Show();
    }

    private static void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Coucou");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "errors.log"),
                $"[{DateTime.Now:O}] {e.Exception}\n\n");
        }
        catch { }

        e.Handled = true;
        MessageBox.Show("Coucou hit an unexpected error. Details were saved to %LOCALAPPDATA%\\Coucou\\errors.log.",
            "Coucou", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
