using System.Diagnostics;
using System.Text;

namespace Coucou.Services;

public sealed class ClaudeCliService
{
    public async Task<string> AskAsync(string prompt, string workingDirectory, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            return "Tell Claude what you want to ask.";

        if (!Directory.Exists(workingDirectory))
            workingDirectory = Environment.CurrentDirectory;

        var start = new ProcessStartInfo
        {
            FileName = "claude",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
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
                return "Couldn't start Claude Code.";

            await process.WaitForExitAsync(cancellationToken);
            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);

            if (process.ExitCode != 0)
                return string.IsNullOrWhiteSpace(error)
                    ? $"Claude exited with code {process.ExitCode}."
                    : error.Trim();

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
}