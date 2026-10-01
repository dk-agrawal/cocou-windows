using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.ComponentModel;

namespace Coucou.Services;

public sealed class ClaudeCliService
{
    private readonly LocalSettings _settings;
    private readonly LocalSettings _settings;

    public ClaudeCliService(LocalSettings settings)
    {
        _settings = settings;
    }

    public async Task<string> AskAsync(string prompt, string workingDirectory, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            return "Tell Claude what you want to ask.";

        if (!Directory.Exists(workingDirectory))
            workingDirectory = Environment.CurrentDirectory;

        // Ask mode intentionally runs as a fresh, stateless Claude Code request.
        // This avoids coupling the companion to Claude's internal session storage.
        // Conversation/session continuity can be added later using Claude's supported
        // resume flow once it is verified against the installed CLI version.
        var result = await RunClaudeAsync(prompt, workingDirectory, cancellationToken);

        if (result.ExitCode != 0)
        {
            return string.IsNullOrWhiteSpace(result.Error)
                ? $"Claude exited with code {result.ExitCode}."
                : result.Error.Trim();
        }

        return string.IsNullOrWhiteSpace(result.Output) ? "Claude returned no text." : result.Output.Trim();
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunClaudeAsync(
        string prompt,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
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

        using var process = new Process { StartInfo = start, EnableRaisingEvents = true };

        try
        {
            if (!process.Start())
                return (-1, "", "Couldn't start Claude Code.");

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return (process.ExitCode, await outputTask, await errorTask);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            return (-1, "", "Ask cancelled.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 2)
        {
            return (-1, "", "Claude Code was not found on PATH. Install Claude Code and make sure the `claude` command works in a new terminal.");
        }
        catch (Exception ex)
        {
            return (-1, "", $"Couldn't start Claude Code: {ex.Message}");
        }
    }
}
