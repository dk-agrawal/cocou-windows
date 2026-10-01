using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Coucou.Services;

public sealed class WindowsTerminalService
{
    private const int SwRestore = 9;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    public bool FocusClaude(string project)
    {
        var claudeWindow = FindClaudeWindow();
        if (claudeWindow != IntPtr.Zero)
            return Activate(claudeWindow);

        var projectWindow = FindWindowByTitle(project);
        if (projectWindow != IntPtr.Zero)
            return Activate(projectWindow);

        return false;
    }

    private static IntPtr FindClaudeWindow()
    {
        foreach (var process in Process.GetProcessesByName("claude"))
        {
            try
            {
                if (process.MainWindowHandle != IntPtr.Zero)
                    return process.MainWindowHandle;
            }
            catch
            {
                // Process may exit while we inspect it.
            }
            finally
            {
                process.Dispose();
            }
        }

        return IntPtr.Zero;
    }

    private static IntPtr FindWindowByTitle(string project)
    {
        if (string.IsNullOrWhiteSpace(project))
            return IntPtr.Zero;

        IntPtr match = IntPtr.Zero;
        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd))
                return true;

            var length = GetWindowTextLength(hWnd);
            if (length <= 0)
                return true;

            var title = new StringBuilder(length + 1);
            GetWindowText(hWnd, title, title.Capacity);

            if (title.ToString().Contains(project, StringComparison.OrdinalIgnoreCase))
            {
                match = hWnd;
                return false;
            }

            return true;
        }, IntPtr.Zero);

        return match;
    }

    private static bool Activate(IntPtr hWnd)
    {
        ShowWindow(hWnd, SwRestore);
        return SetForegroundWindow(hWnd);
    }
}
