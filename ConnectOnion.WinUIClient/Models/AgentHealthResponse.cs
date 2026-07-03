using System.Text.Json;
using System.Text.Json.Serialization;

namespace ConnectOnion.WinUIClient.Models;

/// <summary>
/// Response body of a Direct URL <c>/health</c> probe. Mirrors the TypeScript
/// <c>AgentHealthResponse</c> type; <see cref="Agent"/> is an opaque payload.
/// </summary>
public sealed class AgentHealthResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("agent")]
    public JsonElement Agent { get; set; }
}
