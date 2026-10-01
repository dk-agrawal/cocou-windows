using System.Diagnostics;
using Coucou.Models;

namespace Coucou.Services;

public sealed class ClaudeSessionDiscovery
{
    public IReadOnlyList<ClaudeSession> Discover()
    {
        return Process.GetProcesses()
            .Where(p => SafeName(p).Contains("claude", StringComparison.OrdinalIgnoreCase))
            .Select(p => new ClaudeSession(
                p.Id.ToString(),
                SafeName(p),
                "Unknown terminal",
                SessionState.Working))
            .ToList();
    }

    private static string SafeName(Process p)
    {
        try { return p.ProcessName; }
        catch { return "Claude"; }
    }
}
