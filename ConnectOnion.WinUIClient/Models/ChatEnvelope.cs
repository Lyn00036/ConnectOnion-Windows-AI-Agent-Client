using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ConnectOnion.WinUIClient.Models;

/// <summary>
/// Persisted conversation blob. Mirrors the web app's <c>{ state, version }</c>
/// envelope shape so the two stores stay conceptually aligned. For now the state
/// holds the rendered message list; Step 9 extends it with SDK session state.
/// </summary>
public sealed class ChatEnvelope
{
    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("state")]
    public ChatEnvelopeState State { get; set; } = new();
}

public sealed class ChatEnvelopeState
{
    [JsonPropertyName("messages")]
    public List<ChatMessage> Messages { get; set; } = new();

    [JsonPropertyName("createdAt")]
    public long CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public long UpdatedAt { get; set; }
}
