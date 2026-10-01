using System.IO.Pipes;

const string pipeName = "CoucouClaude";

var input = await Console.In.ReadToEndAsync();
if (string.IsNullOrWhiteSpace(input))
    return;

try
{
    await using var pipe = new NamedPipeClientStream(
        ".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

    await pipe.ConnectAsync(1000);
    using var writer = new StreamWriter(pipe) { AutoFlush = true };
    using var reader = new StreamReader(pipe);

    await writer.WriteLineAsync(input);
    var response = await reader.ReadLineAsync();

    if (!string.IsNullOrWhiteSpace(response))
        Console.Out.WriteLine(response);
}
catch
{
    // Coucou is optional. If it is not running, Claude Code keeps its normal flow.
}