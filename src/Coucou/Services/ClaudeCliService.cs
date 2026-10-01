using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;

namespace Coucou.Services;

public sealed class ClaudeCliService
{
    private readonly ConcurrentDictionary<string, Guid> _sessions = new(StringComparer.OrdinalIgnoreCase);

    public async Task<string> AskAsync(string prompt, string workingDirectory, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            return "Tell Claude what you want to ask.";

        if (!Directory.Exists(workingDirectory))
            workingDirectory = Environment.CurrentDirectory;

        var sessionId = _sessions.GetOrAdd(
            Path.GetFullPath(workingDirectory),
            _ => Guid.NewGuid());

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

        if (WasSessionStarted(workingDirectory))
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
                    _sessions.TryRemove(Path.GetFullPath(workingDirectory), out _);
                }

                return string.IsNullOrWhiteSpace(error)
                    ? $"Claude exited with code {process.ExitCode}."
                    : error.Trim();
            }

            MarkSessionStarted(workingDirectory);
            return string.IsNullOrWhiteSpace(output) ? "Claude returned no text." : output.Trim();
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            return "Ask cancelled.";
        }
        catch (Exception ex)
        {
            return $"Couldn't start Claude Code: {ex.Message}";
        }
    }

    private readonly ConcurrentDictionary<string, bool> _started =
        new(StringComparer.OrdinalIgnoreCase);

    private bool WasSessionStarted(string workingDirectory) =>
        _started.ContainsKey(Path.GetFullPath(workingDirectory));

    private void MarkSessionStarted(string workingDirectory) =>
        _started[Path.GetFullPath(workingDirectory)] = true;
}
