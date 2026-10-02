using System.Diagnostics;
using System.IO;
using System.ComponentModel;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Coucou.Services;

public sealed class ClaudeCliService
{
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

        // When Claude Code is configured through OpenRouter, its internal
        // generate_session_title call can reject the special openrouter/free
        // router before the actual answer is generated. Coucou Ask mode is a
        // standalone Q&A feature, so call OpenRouter directly in this case.
        var baseUrl = Environment.GetEnvironmentVariable("ANTHROPIC_BASE_URL") ?? "";
        var authToken = new[]
        {
            Environment.GetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN"),
            Environment.GetEnvironmentVariable("OPENROUTER_API_KEY")
        }.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";

        if (baseUrl.Contains("openrouter.ai", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(authToken))
        {
            return await AskOpenRouterAsync(prompt, authToken, cancellationToken);
        }

        var result = await RunClaudeAsync(prompt, workingDirectory, cancellationToken);

        if (result.ExitCode != 0)
        {
            return string.IsNullOrWhiteSpace(result.Error)
                ? $"Claude exited with code {result.ExitCode}."
                : result.Error.Trim();
        }

        return string.IsNullOrWhiteSpace(result.Output) ? "Claude returned no text." : result.Output.Trim();
    }

    private static string ExtractMessageContent(JsonElement content)
    {
        if (content.ValueKind == JsonValueKind.String)
            return content.GetString() ?? "";

        if (content.ValueKind == JsonValueKind.Array)
        {
            var parts = new List<string>();
            foreach (var item in content.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    parts.Add(item.GetString() ?? "");
                    continue;
                }

                if (item.ValueKind == JsonValueKind.Object
                    && item.TryGetProperty("text", out var text)
                    && text.ValueKind == JsonValueKind.String)
                {
                    parts.Add(text.GetString() ?? "");
                }
            }
            return string.Join("\n", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        }

        return content.ToString();
    }

    private static async Task<string> AskOpenRouterAsync(
        string prompt,
        string authToken,
        CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authToken);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var payload = new
        {
            model = "openrouter/free",
            messages = new[]
            {
                new { role = "user", content = prompt }
            },
            max_tokens = 4096
        };

        using var content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        try
        {
            using var response = await client.PostAsync(
                "https://openrouter.ai/api/v1/chat/completions",
                content,
                cancellationToken);

            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                try
                {
                    using var errorJson = JsonDocument.Parse(body);
                    var message = errorJson.RootElement
                        .GetProperty("error")
                        .GetProperty("message")
                        .GetString();
                    return string.IsNullOrWhiteSpace(message)
                        ? $"OpenRouter returned HTTP {(int)response.StatusCode}."
                        : $"OpenRouter: {message}";
                }
                catch
                {
                    return $"OpenRouter returned HTTP {(int)response.StatusCode}.";
                }
            }

            using var json = JsonDocument.Parse(body);
            var messageElement = json.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message");

            if (!messageElement.TryGetProperty("content", out var contentElement))
                return "OpenRouter returned no answer content.";

            var answer = ExtractMessageContent(contentElement);

            return string.IsNullOrWhiteSpace(answer)
                ? "OpenRouter returned an empty answer."
                : answer.Trim();
        }
        catch (OperationCanceledException)
        {
            return "Ask cancelled.";
        }
        catch (HttpRequestException ex)
        {
            // Windows ships with curl.exe, which uses the OS TLS stack and can
            // succeed when a .NET HTTPS handler cannot negotiate the connection.
            // This is a fallback only; we never disable certificate validation.
            var fallback = await AskOpenRouterWithCurlAsync(prompt, authToken, cancellationToken);
            if (fallback is not null)
                return fallback;

            return $"Couldn't contact OpenRouter over HTTPS: {ex.Message}";
        }
        catch (Exception ex)
        {
            return $"Couldn't contact OpenRouter: {ex.Message}";
        }
    }

    private static async Task<string?> AskOpenRouterWithCurlAsync(
        string prompt,
        string authToken,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new
        {
            model = "openrouter/free",
            messages = new[]
            {
                new { role = "user", content = prompt }
            },
            max_tokens = 4096
        });

        var start = new ProcessStartInfo
        {
            FileName = "curl.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        start.ArgumentList.Add("--silent");
        start.ArgumentList.Add("--show-error");
        start.ArgumentList.Add("--location");
        start.ArgumentList.Add("--max-time");
        start.ArgumentList.Add("300");
        start.ArgumentList.Add("--request");
        start.ArgumentList.Add("POST");
        start.ArgumentList.Add("https://openrouter.ai/api/v1/chat/completions");
        start.ArgumentList.Add("--header");
        start.ArgumentList.Add($"Authorization: Bearer {authToken}");
        start.ArgumentList.Add("--header");
        start.ArgumentList.Add("Content-Type: application/json");
        start.ArgumentList.Add("--data");
        start.ArgumentList.Add(payload);

        using var process = new Process { StartInfo = start };

        try
        {
            if (!process.Start())
                return null;

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            var body = await outputTask;
            var error = await errorTask;

            if (process.ExitCode != 0)
                return $"Couldn't contact OpenRouter (native HTTPS fallback): {error.Trim()}";

            using var json = JsonDocument.Parse(body);
            if (!json.RootElement.TryGetProperty("choices", out var choices)
                || choices.GetArrayLength() == 0)
                return "OpenRouter returned no choices.";

            var message = choices[0].GetProperty("message");
            if (!message.TryGetProperty("content", out var contentElement))
                return "OpenRouter returned no answer content.";

            var answer = ExtractMessageContent(contentElement);
            return string.IsNullOrWhiteSpace(answer)
                ? "OpenRouter returned an empty answer."
                : answer.Trim();
        }
        catch (OperationCanceledException)
        {
            return "Ask cancelled.";
        }
        catch (Exception ex)
        {
            return $"Couldn't contact OpenRouter (native HTTPS fallback): {ex.Message}";
        }
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
