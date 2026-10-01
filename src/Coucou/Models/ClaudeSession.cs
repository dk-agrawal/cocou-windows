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
)
{
    public string DisplayName => State switch
    {
        SessionState.Working => $"{Project}  •  Working",
        SessionState.Permission => $"{Project}  •  Permission",
        SessionState.WaitingForInput => $"{Project}  •  Waiting",
        SessionState.Error => $"{Project}  •  Error",
        SessionState.Finished => $"{Project}  •  Finished",
        _ => Project
    };
};
