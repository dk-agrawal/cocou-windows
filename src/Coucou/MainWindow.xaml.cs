using System.IO;
using System.Text.Json;
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
    private struct POINT { public int X; public int Y; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    private readonly ClaudeSessionDiscovery _discovery;
    private readonly ClaudeSessionRegistry _sessions;
    private readonly LocalSettings _settings;
    private readonly ClaudeBridgeServer _bridge;
    private readonly ClaudeCliService _claude;
    private readonly WindowsTerminalService _terminal;
    private readonly System.Windows.Forms.NotifyIcon _trayIcon;
    private readonly WindowsStartupService _startup;
    private string _workingDirectory = Environment.CurrentDirectory;
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _cursorPoll = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly DispatcherTimer _blink = new() { Interval = TimeSpan.FromSeconds(4) };
    private TaskCompletionSource<string?>? _permissionDecision;
    private int _pokes;

    public MainWindow(ClaudeSessionDiscovery discovery, LocalSettings settings)
    {
        InitializeComponent();
        _discovery = discovery;
        _sessions = new ClaudeSessionRegistry();
        _settings = settings;
        _bridge = new ClaudeBridgeServer(HandleClaudeHookAsync);
        _claude = new ClaudeCliService();
        _terminal = new WindowsTerminalService();
        _startup = new WindowsStartupService();
        _trayIcon = CreateTrayIcon();
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
        _bridge.Start();
        RefreshStatus();
    }

    protected override async void OnClosed(EventArgs e)
    {
        _poll.Stop();
        _cursorPoll.Stop();
        _blink.Stop();
        await _bridge.DisposeAsync();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        base.OnClosed(e);
    }

    private System.Windows.Forms.NotifyIcon CreateTrayIcon()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Show Coucou", null, (_, _) => Dispatcher.Invoke(ShowWindow));
        menu.Items.Add("Open Terminal", null, (_, _) => Dispatcher.Invoke(FocusTerminal));
        var startupItem = new System.Windows.Forms.ToolStripMenuItem("Start with Windows")
        {
            Checked = _startup.IsEnabled(),
            CheckOnClick = true
        };
        startupItem.Click += (_, _) =>
        {
            var enabled = startupItem.Checked;
            if (_startup.TrySetEnabled(enabled))
            {
                _settings.StartWithWindows = enabled;
                _settings.Save();
            }
            else
            {
                startupItem.Checked = _startup.IsEnabled();
            }
        };
        menu.Items.Add(startupItem);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Quit Coucou", null, (_, _) => Dispatcher.Invoke(() => System.Windows.Application.Current.Shutdown()));

        var icon = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "Coucou — Claude Code companion",
            Visible = true,
            ContextMenuStrip = menu
        };
        icon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowWindow);
        return icon;
    }

    private void ShowWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void PositionTopCenter()
    {
        var work = SystemParameters.WorkArea;
        Left = work.Left + (work.Width - Width) / 2;
        Top = Math.Max(work.Top + 6, 6);
    }

    private void RefreshStatus()
    {
        var sessions = _sessions.Snapshot();
        if (sessions.Count == 0) sessions = _discovery.Discover();
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
            SessionState.WaitingForInput => $"Claude is waiting • {active.Project}",
            SessionState.Error => $"Something broke • {active.Project}",
            SessionState.Finished => $"Finished • {active.Project}",
            _ => active.Project
        };
    }

    private void Blink()
    {
        var scale = new ScaleTransform(1, 1);
        Character.RenderTransformOrigin = new System.Windows.Point(.5, .5);
        Character.RenderTransform = scale;
        scale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(1, .18, TimeSpan.FromMilliseconds(90)) { AutoReverse = true });
        _blink.Interval = TimeSpan.FromSeconds(Random.Shared.Next(3, 7));
    }

    private void FollowGlobalCursor()
    {
        if (!_settings.CursorTracking || !GetCursorPos(out var cursor)) return;
        var screenPoint = PointFromScreen(new System.Windows.Point(cursor.X, cursor.Y));
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

    private void OnDragEnter(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop) ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None;
        Status.Text = "Ooh, a file 👀";
        e.Handled = true;
    }

    private void OnDrop(object sender, System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) return;
        var files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop)!;
        var first = files.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(first))
        {
            _workingDirectory = Directory.Exists(first) ? first : Path.GetDirectoryName(first) ?? _workingDirectory;
            Status.Text = $"Got {Path.GetFileName(first)}";
        }
        Hint.Text = "Ask Claude what to do with it";
        e.Handled = true;
    }

    private void OnFocusTerminal(object sender, RoutedEventArgs e) => FocusTerminal();

    private void FocusTerminal()
    {
        var active = _sessions.Snapshot().FirstOrDefault();
        var project = active?.Project ?? "";
        if (_terminal.FocusClaude(project))
        {
            Hint.Text = "Terminal focused ✓";
            Status.Text = string.IsNullOrWhiteSpace(project) ? "Claude focused" : $"Focused • {project}";
        }
        else
        {
            Hint.Text = "Couldn't find the terminal";
            Status.Text = "Claude terminal not found";
        }
    }

    private void OnAskClick(object sender, RoutedEventArgs e)
    {
        PermissionCard.Visibility = Visibility.Collapsed;
        AskCard.Visibility = Visibility.Visible;
        Height = 190;
        AskResponse.Text = "";
        AskInput.Focus();
        AskInput.SelectAll();
    }

    private async void OnSendAsk(object sender, RoutedEventArgs e)
    {
        var prompt = AskInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(prompt)) return;

        var sendButton = sender as System.Windows.Controls.Button;
        if (sendButton is not null) sendButton.IsEnabled = false;
        AskResponse.Text = "Claude is thinking…";
        Status.Text = "Thinking with Claude…";
        Hint.Text = "This can take a moment";

        try
        {
            var response = await _claude.AskAsync(prompt, _workingDirectory);
            AskResponse.Text = response;
            Status.Text = "Claude answered ✨";
            Hint.Text = "Ask another question";
        }
        finally
        {
            if (sendButton is not null) sendButton.IsEnabled = true;
        }
    }

    private async Task<string?> HandleClaudeHookAsync(ClaudeHookEvent hook)
    {
        _sessions.Apply(hook);
        await Dispatcher.InvokeAsync(RefreshStatus);

        if (!string.Equals(hook.HookEventName, "PermissionRequest", StringComparison.OrdinalIgnoreCase))
            return null;

        var toolInput = hook.ToolInput.ValueKind == JsonValueKind.Object ? hook.ToolInput.ToString() : "";
        var command = hook.ToolName switch
        {
            "Bash" or "PowerShell" => TryGetString(hook.ToolInput, "command"),
            "Write" or "Edit" => TryGetString(hook.ToolInput, "file_path"),
            _ => toolInput
        };

        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        await Dispatcher.InvokeAsync(() =>
        {
            _permissionDecision?.TrySetResult(null);
            _permissionDecision = tcs;
            PermissionTitle.Text = $"Allow Claude to use {hook.ToolName}?";
            PermissionCommand.Text = string.IsNullOrWhiteSpace(command) ? "Claude requested a tool permission." : command;
            AskCard.Visibility = Visibility.Collapsed;
            PermissionCard.Visibility = Visibility.Visible;
            Height = 190;
            Status.Text = "Needs your permission 👀";
            Hint.Text = "Claude is waiting";
            Activate();
        });

        return await tcs.Task;
    }

    private static string TryGetString(JsonElement input, string property)
    {
        return input.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
    }

    private void OnAllowPermission(object sender, RoutedEventArgs e) => ResolvePermission("allow");
    private void OnDenyPermission(object sender, RoutedEventArgs e) => ResolvePermission("deny");

    private void ResolvePermission(string behavior)
    {
        var response = JsonSerializer.Serialize(new
        {
            hookSpecificOutput = new
            {
                hookEventName = "PermissionRequest",
                decision = new { behavior }
            }
        });

        _permissionDecision?.TrySetResult(response);
        _permissionDecision = null;
        PermissionCard.Visibility = Visibility.Collapsed;
        Height = 112;
        Status.Text = behavior == "allow" ? "Allowed ✓" : "Denied";
        Hint.Text = "Watching Claude Code…";
    }
}