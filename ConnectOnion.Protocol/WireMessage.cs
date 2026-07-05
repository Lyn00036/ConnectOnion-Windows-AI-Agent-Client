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

/// <summary>
/// A streamed intermediate event surfaced to the UI while the agent works.
/// <see cref="RawJson"/> carries the full frame so the client can read the
/// per-type fields (tool id, status, args, result, timings, …) it needs to merge
/// running/finished events into a single bubble.
/// </summary>
/// <param name="EventId">Server-assigned event identifier, used as
/// <c>last_msg_id</c> on reconnect so the server can replay only missed events.</param>
public sealed record AgentStreamEvent(string Type, string Description, string? EventId, string RawJson = "{}");
