namespace Coucou.Models;

public enum SessionState
{
    Idle, Working, WaitingForInput, Permission, Error, Finished
}

public sealed record ClaudeSession(
    string Id,
    string Project,
    string Terminal,
    SessionState State,
    string? Detail = null,
    string Cwd = ""
);
