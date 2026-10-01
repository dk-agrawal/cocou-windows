using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.ComponentModel;

namespace Coucou.Services;

public sealed class ClaudeCliService
{
    private readonly LocalSettings _settings;
    private readonly ConcurrentDictionary<string, Guid> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, bool> _started = new(StringComparer.OrdinalIgnoreCase);

    public ClaudeCliService(LocalSettings settings)
    {
        _settings = settings;
        foreach (var pair in settings.ClaudeSessions.ToArray())
        {
            if (Guid.TryParse(pair.Value, out var id))
            {
                _sessions[pair.Key] = id;
                _started[pair.Key] = true;
            }
        }
    }

    public async Task<string> AskAsync(string prompt, string workingDirectory, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            return "Tell Claude what you want to ask.";

        if (!Directory.Exists(workingDirectory))
            workingDirectory = Environment.CurrentDirectory;

        var projectPath = Path.GetFullPath(workingDirectory);
        var sessionId = _sessions.GetOrAdd(projectPath, _ => Guid.NewGuid());

        var start = new ProcessStartInfo
        {
            FileName = "claude",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        start.ArgumentList.Add("-p");
        start.ArgumentList.Add(prompt);
        start.ArgumentList.Add("--output-format");
        start.ArgumentList.Add("text");
        start.ArgumentList.Add("--max-turns");
        start.ArgumentList.Add("1");

        if (_started.ContainsKey(projectPath))
        {
            start.ArgumentList.Add("--resume");
            start.ArgumentList.Add(sessionId.ToString());
        }
        else
        {
            start.ArgumentList.Add("--session-id");
            start.ArgumentList.Add(sessionId.ToString());
        }

        using var process = new Process { StartInfo = start, EnableRaisingEvents = true };

        try
        {
            if (!process.Start())
                return "Couldn't start Claude Code.";

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var output = await outputTask;
            var error = await errorTask;

            if (process.ExitCode != 0)
            {
                if (error.Contains("session", StringComparison.OrdinalIgnoreCase) &&
                    error.Contains("not found", StringComparison.OrdinalIgnoreCase))
                {
                    _sessions.TryRemove(projectPath, out _);
                    _started.TryRemove(projectPath, out _);
                    _settings.ClaudeSessions.Remove(projectPath);
                    _settings.Save();
                }

                return string.IsNullOrWhiteSpace(error)
                    ? $"Claude exited with code {process.ExitCode}."
                    : error.Trim();
            }

            _started[projectPath] = true;
            _settings.ClaudeSessions[projectPath] = sessionId.ToString();
            _settings.Save();
            return string.IsNullOrWhiteSpace(output) ? "Claude returned no text." : output.Trim();
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            return "Ask cancelled.";
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 2)
        {
            return "Claude Code was not found on PATH. Install Claude Code and make sure the `claude` command works in a new terminal.";
        }
        catch (Exception ex)
        {
            return $"Couldn't start Claude Code: {ex.Message}";
        }
    }
}
