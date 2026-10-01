using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using Coucou.Models;

namespace Coucou.Services;

public sealed class ClaudeBridgeServer : IAsyncDisposable
{
    public const string PipeName = "CoucouClaude";
    private readonly Func<ClaudeHookEvent, Task<string?>> _handler;
    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;

    public ClaudeBridgeServer(Func<ClaudeHookEvent, Task<string?>> handler) => _handler = handler;

    public void Start() => _loop ??= Task.Run(ListenLoopAsync);

    private async Task ListenLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    PipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

                await pipe.WaitForConnectionAsync(_stop.Token);
                using var reader = new StreamReader(pipe);
                using var writer = new StreamWriter(pipe) { AutoFlush = true };

                var line = await reader.ReadLineAsync(_stop.Token);
                if (string.IsNullOrWhiteSpace(line)) continue;

                var hook = JsonSerializer.Deserialize<ClaudeHookEvent>(line);
                if (hook is null) continue;

                var response = await _handler(hook);
                await writer.WriteLineAsync(response ?? "");
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                // A disconnected hook must never take Coucou down.
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        if (_loop is not null)
        {
            try { await _loop; } catch (OperationCanceledException) { }
        }
        _stop.Dispose();
    }
}