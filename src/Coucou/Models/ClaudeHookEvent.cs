using System.Text.Json;
using System.Text.Json.Serialization;

namespace Coucou.Models;

public sealed class ClaudeHookEvent
{
    [JsonPropertyName("hook_event_name")]
    public string HookEventName { get; init; } = "";

    [JsonPropertyName("session_id")]
    public string SessionId { get; init; } = "";

    [JsonPropertyName("cwd")]
    public string Cwd { get; init; } = "";

    [JsonPropertyName("tool_name")]
    public string ToolName { get; init; } = "";

    [JsonPropertyName("tool_input")]
    public JsonElement ToolInput { get; init; }

    [JsonPropertyName("notification_type")]
    public string NotificationType { get; init; } = "";

    [JsonPropertyName("message")]
    public string Message { get; init; } = "";
}
