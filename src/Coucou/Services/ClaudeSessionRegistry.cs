using System.IO;
using Coucou.Models;

namespace Coucou.Services;

public sealed class ClaudeSessionRegistry
{
    private readonly Dictionary<string, ClaudeSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public IReadOnlyList<ClaudeSession> Snapshot()
    {
        lock (_gate)
            return _sessions.Values.OrderByDescending(s => s.State == SessionState.Working)
                .ThenByDescending(s => s.State == SessionState.Permission)
                .ThenBy(s => s.Project, StringComparer.OrdinalIgnoreCase)
                .ToList();
    }

    public void Apply(ClaudeHookEvent hook)
    {
        if (string.IsNullOrWhiteSpace(hook.SessionId)) return;

        lock (_gate)
        {
            _sessions.TryGetValue(hook.SessionId, out var current);
            var project = string.IsNullOrWhiteSpace(hook.Cwd)
                ? current?.Project ?? "Unknown project"
                : Path.GetFileName(hook.Cwd.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(project)) project = hook.Cwd;

            var state = hook.HookEventName switch
            {
                "PermissionRequest" => SessionState.Permission,
                "PostToolUseFailure" => SessionState.Error,
                "Stop" or "SessionEnd" => SessionState.Finished,
                "Notification" when string.Equals(hook.NotificationType, "permission_prompt", StringComparison.OrdinalIgnoreCase) => SessionState.Permission,
                "Notification" when string.Equals(hook.NotificationType, "idle_prompt", StringComparison.OrdinalIgnoreCase) => SessionState.WaitingForInput,
                "Notification" when string.Equals(hook.NotificationType, "agent_needs_input", StringComparison.OrdinalIgnoreCase) => SessionState.WaitingForInput,
                "SessionStart" or "PreToolUse" or "PostToolUse" or "UserPromptSubmit" => SessionState.Working,
                _ => current?.State ?? SessionState.Idle
            };

            _sessions[hook.SessionId] = new ClaudeSession(
                hook.SessionId,
                project,
                current?.Terminal ?? "Unknown terminal",
                state,
                hook.Message,
                string.IsNullOrWhiteSpace(hook.Cwd) ? current?.Cwd ?? "" : hook.Cwd);
        }
    }
}
