using System.Collections.Generic;
using System.Text.Json;

namespace ConnectOnion.Protocol;

/// <summary>
/// A decoded inbound WebSocket frame. Frames are JSON objects with a
/// <c>type</c> discriminator; the rest is read on demand via the helpers.
/// </summary>
public sealed class WireMessage
{
    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        AllowDuplicateProperties = false,
    };

    private readonly JsonElement _root;

    private WireMessage(JsonElement root) => _root = root;

    public static WireMessage Parse(string json)
    {
        var doc = JsonDocument.Parse(json, JsonOptions);
        return new WireMessage(doc.RootElement.Clone());
    }

    public string Type => GetString("type") ?? "";

    public string? GetString(string name)
        => _root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    public bool GetBool(string name)
        => _root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    public bool TryGet(string name, out JsonElement value)
        => _root.TryGetProperty(name, out value);

    public JsonElement Root => _root;
}

/// <summary>A streamed intermediate event surfaced to the UI while the agent works.</summary>
public sealed record AgentStreamEvent(string Type, string Description);
