using System.IO;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Diagnostics;
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
    private string? _droppedFile;
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _blink = new() { Interval = TimeSpan.FromSeconds(4) };
    private readonly Stopwatch _greetingClock = Stopwatch.StartNew();
    private double _greetingTime;
    private bool _greetingActive = true;
    private bool _greetingHeld;
    private readonly double _autoCollapseAt = 4.9;
    private sealed record PermissionPrompt(Guid Id, ClaudeHookEvent Hook, string Command, TaskCompletionSource<string?> Completion);
    private readonly Queue<PermissionPrompt> _permissionQueue = new();
    private PermissionPrompt? _activePermission;
    private string? _activeSessionId;
    private bool _updatingSessionPicker;
    private int _pokes;
    private double _targetEyeShift;
    private double _eyeShift;

    public MainWindow(ClaudeSessionDiscovery discovery, LocalSettings settings)
    {
        InitializeComponent();
        _discovery = discovery;
        _sessions = new ClaudeSessionRegistry();
        _settings = settings;
        _bridge = new ClaudeBridgeServer(HandleClaudeHookAsync);
        _claude = new ClaudeCliService(_settings);
        _terminal = new WindowsTerminalService();
        _startup = new WindowsStartupService();
        _trayIcon = CreateTrayIcon();
        _poll.Tick += (_, _) => RefreshStatus();
        _blink.Tick += (_, _) => Blink();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PositionTopCenter();
        _greetingClock.Restart();
        _greetingActive = true;
        _poll.Start();
        System.Windows.Media.CompositionTarget.Rendering += OnRendering;
        _blink.Start();
        _bridge.Start();
        RefreshStatus();
    }

    protected override async void OnClosed(EventArgs e)
    {
        _poll.Stop();
        System.Windows.Media.CompositionTarget.Rendering -= OnRendering;
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
        var cursorItem = new System.Windows.Forms.ToolStripMenuItem("Cursor Tracking")
        {
            Checked = _settings.CursorTracking,
            CheckOnClick = true
        };
        cursorItem.Click += (_, _) =>
        {
            _settings.CursorTracking = cursorItem.Checked;
            _settings.Save();
            Dispatcher.Invoke(() => Hint.Text = cursorItem.Checked ? "Eyes are watching 👀" : "Cursor tracking off");
        };
        menu.Items.Add(cursorItem);
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
            _activeSessionId = null;
            _updatingSessionPicker = true;
            SessionPicker.ItemsSource = null;
            _updatingSessionPicker = false;
            Status.Text = "Waiting for Claude Code…";
            return;
        }

        var active = sessions.FirstOrDefault(s => s.Id == _activeSessionId) ?? sessions[0];
        if (_activeSessionId != active.Id) _activeSessionId = active.Id;
        _updatingSessionPicker = true;
        SessionPicker.ItemsSource = sessions;
        SessionPicker.SelectedValue = active.Id;
        _updatingSessionPicker = false;
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

    private static double Clamp01(double v) => Math.Clamp(v, 0, 1);
    private static double Seg(double t, double a, double b) => Clamp01((t - a) / (b - a));
    private static double Out(double t) => 1 - Math.Pow(1 - Clamp01(t), 3);
    private static double In(double t) => Math.Pow(Clamp01(t), 3);
    private static double InOut(double t)
    {
        t = Clamp01(t);
        return t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
    }
    private static double Back(double t)
    {
        t = Clamp01(t);
        const double c1 = 1.70158;
        const double c3 = c1 + 1;
        return 1 + c3 * Math.Pow(t - 1, 3) + c1 * Math.Pow(t - 1, 2);
    }

    private void TickGreeting()
    {
        if (!_greetingActive) return;
        _greetingTime = _greetingClock.Elapsed.TotalSeconds;
        var t = _greetingTime;

        // Ported choreography from the reference: 0→0.45 rise, 1.25→1.52 dip,
        // 1.52→2.80 sway/wave, 2.72 badge, 3.85→4.15 state tint.
        var grow = Back(Seg(t, .02, .45));
        CharacterOffset.Y = 28 - 25 * Out(Seg(t, .02, .45));
        CharacterScale.ScaleY = Math.Max(.08, grow);
        CharacterScale.ScaleX = 1 + .04 * Math.Sin(Math.PI * Seg(t, 1.25, 1.52));
        CharacterRotate.Angle = t >= 1.52 && t < 2.58
            ? Math.Sin((t - 1.52) * 2 * Math.PI * .9) * 4.5
            : 0;

        if (t >= 1.25 && t < 1.52)
        {
            var k = Math.Sin(Math.PI * Seg(t, 1.25, 1.52));
            CharacterOffset.Y += 9 * k;
        }
        else if (t >= 1.52 && t < 2.80)
        {
            var w = t - 1.52;
            var fade = 1 - Seg(t, 2.58, 2.80);
            CharacterOffset.X = Math.Sin(w * 2 * Math.PI * .9) * 3.2 * fade;
        }
        else CharacterOffset.X = 0;

        var happy = t >= .60 && t < .82;
        var content = (t >= 2.45 && t < 2.58) || (t >= 2.85 && t < 3.20);
        var open = 1.0;
        foreach (var tb in new[] { 1.95, 3.80 })
        {
            var k = Seg(t, tb, tb + .12);
            if (k > 0 && k < 1) open = Math.Min(open, 1 - Math.Sin(Math.PI * k) * .94);
        }
        LeftEyeScale.ScaleY = Math.Max(.08, open);
        RightEyeScale.ScaleY = Math.Max(.08, open);

        if (happy || content)
        {
            LeftEye.Height = 4; RightEye.Height = 4;
            LeftEye.Margin = new Thickness(15, 18, 0, 0);
            RightEye.Margin = new Thickness(0, 18, 15, 0);
        }
        else
        {
            LeftEye.Height = 9; RightEye.Height = 9;
            LeftEye.Margin = new Thickness(15, 15, 0, 0);
            RightEye.Margin = new Thickness(0, 15, 15, 0);
        }

        var lookX = t >= 1.52 && t < 2.45 ? .55 : (t >= 2.45 && t < 3.20 ? -.3 : 0);
        var lookY = t >= 1.52 && t < 2.45 ? -.45 : (t >= 2.45 && t < 3.20 ? .6 : 0);
        var maxShift = 3.0;
        _targetEyeShift = lookX * maxShift;
        _eyeShift += (_targetEyeShift - _eyeShift) * .18;
        if (LeftEye.RenderTransform is TranslateTransform lt) lt.X = _eyeShift;
        else LeftEye.RenderTransform = new TranslateTransform(_eyeShift, 0);
        if (RightEye.RenderTransform is TranslateTransform rt) rt.X = _eyeShift;
        else RightEye.RenderTransform = new TranslateTransform(_eyeShift, 0);

        var badge = Back(Seg(t, 2.72, 3.0));
        Hint.Text = badge > .01 ? "✨ Claude is working" : "Coucou • watching Claude";
        RootBorder.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(t >= 3.85 && t < 4.15 ? "#E9185FA3" : "#E9000000")!;

        if (!_greetingHeld && t >= _autoCollapseAt)
        {
            CollapseGreeting();
        }
    }

    private void CollapseGreeting()
    {
        if (!_greetingActive) return;
        _greetingActive = false;
        MainControls.Visibility = Visibility.Collapsed;

        var began = Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            var k = InOut(Seg(began.Elapsed.TotalSeconds, 0, .34));
            CharacterOffset.Y = 3 + 25 * k;
            CharacterOffset.X = 0;
            CharacterScale.ScaleX = 1;
            CharacterScale.ScaleY = 1 - .72 * k;
            IslandScale.ScaleX = 1 - .05 * k;
            if (k >= 1)
            {
                timer.Stop();
                MainControls.Visibility = Visibility.Visible;
                Height = 150;
                IslandScale.ScaleX = 1;
                IslandScale.ScaleY = 1;
                CharacterOffset.Y = 3;
                CharacterScale.ScaleY = 1;
            }
        };
        timer.Start();
    }

    private void Blink()
    {
        var left = new DoubleAnimation(1, 0.12, TimeSpan.FromMilliseconds(75))
        {
            AutoReverse = true,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        };
        var right = left.Clone();
        LeftEyeScale.BeginAnimation(ScaleTransform.ScaleYProperty, left);
        RightEyeScale.BeginAnimation(ScaleTransform.ScaleYProperty, right);
        _blink.Interval = TimeSpan.FromSeconds(Random.Shared.Next(3, 7));
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        FollowGlobalCursor();
        if (_greetingActive)
            TickGreeting();
    }

    private void FollowGlobalCursor()
    {
        if (!_settings.CursorTracking || !GetCursorPos(out var cursor))
            return;

        var screenPoint = PointFromScreen(new System.Windows.Point(cursor.X, cursor.Y));
        var normalizedX = Math.Clamp(screenPoint.X / Math.Max(1, ActualWidth), 0, 1);
        _targetEyeShift = (normalizedX - .5) * 8;
        _eyeShift += (_targetEyeShift - _eyeShift) * 0.28;

        if (LeftEye.RenderTransform is not TranslateTransform left)
        {
            left = new TranslateTransform();
            LeftEye.RenderTransform = left;
        }
        if (RightEye.RenderTransform is not TranslateTransform right)
        {
            right = new TranslateTransform();
            RightEye.RenderTransform = right;
        }
        left.X = _eyeShift;
        right.X = _eyeShift;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _pokes++;
        var duration = TimeSpan.FromMilliseconds(_pokes >= 5 ? 150 : 110);
        CharacterScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(.82, 1, duration)
            {
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.25 }
            });
        CharacterScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(.86, 1, duration)
            {
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.25 }
            });
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
            _droppedFile = first;
            Status.Text = $"Got {Path.GetFileName(first)}";
        }
        Hint.Text = "Ask Claude what to do with it";
        e.Handled = true;
    }

    private void OnSessionSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_updatingSessionPicker || SessionPicker.SelectedItem is not ClaudeSession active) return;
        _activeSessionId = active.Id;
        if (!string.IsNullOrWhiteSpace(active.Cwd) && Directory.Exists(active.Cwd))
        {
            _workingDirectory = active.Cwd;
            _droppedFile = null;
        }
        RefreshStatus();
    }

    private ClaudeSession? GetActiveSession()
    {
        var sessions = _sessions.Snapshot();
        return sessions.FirstOrDefault(s => s.Id == _activeSessionId) ?? sessions.FirstOrDefault();
    }

    private void OnFocusTerminal(object sender, RoutedEventArgs e) => FocusTerminal();

    private void FocusTerminal()
    {
        var active = GetActiveSession();
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

    private void ClearAskState()
    {
        AskInput.Text = "";
        AskResponse.Text = "";
        AskResponseScroll.ScrollToTop();
        _droppedFile = null;
    }

    private void OnAskClick(object sender, RoutedEventArgs e)
    {
        PermissionCard.Visibility = Visibility.Collapsed;
        MainControls.Visibility = Visibility.Collapsed;
        AskCard.Visibility = Visibility.Visible;
        Height = 390;
        var active = GetActiveSession();
        if (active is not null && !string.IsNullOrWhiteSpace(active.Cwd) && Directory.Exists(active.Cwd))
            _workingDirectory = active.Cwd;
        AskResponse.Text = "";
        AskResponseScroll.ScrollToTop();
        if (!string.IsNullOrWhiteSpace(_droppedFile))
            AskInput.Text = $"Review this file and tell me what I should do next: \"{_droppedFile}\"";
        AskInput.Focus();
        AskInput.SelectAll();
    }

    private void OnCloseCard(object sender, RoutedEventArgs e)
    {
        AskCard.Visibility = Visibility.Collapsed;
        PermissionCard.Visibility = Visibility.Collapsed;
        ClearAskState();
        MainControls.Visibility = Visibility.Visible;
        Height = 150;
        Status.Text = "Watching Claude Code…";
        Hint.Text = "Drop a file •";
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
            AskResponseScroll.ScrollToHome();
            Status.Text = "Claude answered ✨";
            Hint.Text = "Ask another question";
        }
        catch (OperationCanceledException)
        {
            AskResponse.Text = "Claude stopped before answering. Try again when you're ready.";
            Status.Text = "Claude request stopped";
            Hint.Text = "Try again";
        }
        catch (Exception ex)
        {
            AskResponse.Text = $"Couldn't reach Claude: {ex.Message}";
            Status.Text = "Claude request failed";
            Hint.Text = "Check Claude Code and try again";
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
        var prompt = new PermissionPrompt(Guid.NewGuid(), hook, command, tcs);
        await Dispatcher.InvokeAsync(() => EnqueuePermission(prompt));
        return await tcs.Task;
    }

    private static string TryGetString(JsonElement input, string property)
    {
        return input.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
    }

    private void EnqueuePermission(PermissionPrompt prompt)
    {
        _permissionQueue.Enqueue(prompt);
        ShowNextPermission();
    }

    private void ShowNextPermission()
    {
        if (_activePermission is not null || _permissionQueue.Count == 0) return;

        _activePermission = _permissionQueue.Dequeue();
        var prompt = _activePermission;
        var project = string.IsNullOrWhiteSpace(prompt.Hook.Cwd)
            ? "Claude"
            : Path.GetFileName(prompt.Hook.Cwd.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        PermissionTitle.Text = $"Allow {project} to use {prompt.Hook.ToolName}?";
        PermissionCommand.Text = string.IsNullOrWhiteSpace(prompt.Command)
            ? "Claude requested a tool permission."
            : prompt.Command;
        MainControls.Visibility = Visibility.Collapsed;
        AskCard.Visibility = Visibility.Collapsed;
        PermissionCard.Visibility = Visibility.Visible;
        Height = 190;
        Status.Text = _permissionQueue.Count == 0
            ? "Needs your permission 👀"
            : $"Needs permission 👀 (+{_permissionQueue.Count} queued)";
        Hint.Text = "Claude is waiting";
        Activate();
    }

    private void OnAllowPermission(object sender, RoutedEventArgs e) => ResolvePermission("allow");
    private void OnDenyPermission(object sender, RoutedEventArgs e) => ResolvePermission("deny");

    private void ResolvePermission(string behavior)
    {
        var prompt = _activePermission;
        if (prompt is null) return;

        var response = JsonSerializer.Serialize(new
        {
            hookSpecificOutput = new
            {
                hookEventName = "PermissionRequest",
                decision = new { behavior }
            }
        });

        prompt.Completion.TrySetResult(response);
        _activePermission = null;
        PermissionCard.Visibility = Visibility.Collapsed;
        MainControls.Visibility = Visibility.Visible;
        Height = 150;
        Status.Text = behavior == "allow" ? "Allowed ✓" : "Denied";
        Hint.Text = _permissionQueue.Count > 0 ? "Next permission waiting…" : "Watching Claude Code…";
        ShowNextPermission();
    }
}