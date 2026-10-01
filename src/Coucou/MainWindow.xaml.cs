using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Coucou.Models;
using Coucou.Services;

namespace Coucou;

public partial class MainWindow : Window
{
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    private readonly ClaudeSessionDiscovery _discovery;
    private readonly LocalSettings _settings;
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _cursorPoll = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly DispatcherTimer _blink = new() { Interval = TimeSpan.FromSeconds(4) };
    private int _pokes;

    public MainWindow(ClaudeSessionDiscovery discovery, LocalSettings settings)
    {
        InitializeComponent();
        _discovery = discovery;
        _settings = settings;
        _poll.Tick += (_, _) => RefreshStatus();
        _cursorPoll.Tick += (_, _) => FollowGlobalCursor();
        _blink.Tick += (_, _) => Blink();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PositionTopCenter();
        _poll.Start();
        _cursorPoll.Start();
        _blink.Start();
        RefreshStatus();
    }

    private void PositionTopCenter()
    {
        var work = SystemParameters.WorkArea;
        Left = work.Left + (work.Width - Width) / 2;
        Top = Math.Max(work.Top + 6, 6);
    }

    private void RefreshStatus()
    {
        var sessions = _discovery.Discover();
        if (sessions.Count == 0)
        {
            Status.Text = "Waiting for Claude Code…";
            return;
        }

        var active = sessions[0];
        Status.Text = active.State switch
        {
            SessionState.Working => $"Working • {active.Project}",
            SessionState.Permission => $"Needs permission • {active.Project}",
            SessionState.Error => $"Something broke • {active.Project}",
            _ => active.Project
        };
    }

    private void Blink()
    {
        var scale = new ScaleTransform(1, 1);
        Character.RenderTransformOrigin = new Point(.5, .5);
        Character.RenderTransform = scale;
        scale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(1, .18, TimeSpan.FromMilliseconds(90))
            { AutoReverse = true });
        _blink.Interval = TimeSpan.FromSeconds(Random.Shared.Next(3, 7));
    }

    private void FollowGlobalCursor()
    {
        if (!_settings.CursorTracking || !GetCursorPos(out var cursor)) return;

        var screenPoint = PointFromScreen(new Point(cursor.X, cursor.Y));
        var normalizedX = Math.Clamp(screenPoint.X / Math.Max(1, ActualWidth), 0, 1);
        var shift = (normalizedX - .5) * 5;

        LeftEye.RenderTransform = new TranslateTransform(shift, 0);
        RightEye.RenderTransform = new TranslateTransform(shift, 0);
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _pokes++;
        var scale = new ScaleTransform(.78, .84);
        Character.RenderTransformOrigin = new Point(.5, .5);
        Character.RenderTransform = scale;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(.78, 1, TimeSpan.FromMilliseconds(_pokes >= 5 ? 130 : 90)));
        scale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(.84, 1, TimeSpan.FromMilliseconds(_pokes >= 5 ? 130 : 90)));
        Hint.Text = _pokes >= 5 ? "Okay okay 😵‍💫" : "boop!";
        e.Handled = true;
    }

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy : DragDropEffects.None;
        Status.Text = "Ooh, a file 👀";
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
        Status.Text = $"Got {Path.GetFileName(files.FirstOrDefault() ?? "file")}";
        Hint.Text = "Ask Claude what to do with it";
        e.Handled = true;
    }
}
