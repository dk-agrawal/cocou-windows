using System.IO;
using Microsoft.Win32;

namespace Coucou.Services;

public sealed class WindowsStartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Coucou";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
    }

    public bool TrySetEnabled(bool enabled)
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe) ||
            !string.Equals(Path.GetFileName(exe), "Coucou.exe", StringComparison.OrdinalIgnoreCase))
            return false;

        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKey);

        if (enabled)
            key.SetValue(ValueName, $"\"{exe}\"");
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);

        return true;
    }
}
