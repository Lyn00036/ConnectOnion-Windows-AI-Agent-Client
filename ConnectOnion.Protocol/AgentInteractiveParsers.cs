using System.Collections.Generic;
using System.Text.Json;

namespace ConnectOnion.Protocol;

/// <summary>
/// Public parsers for the interactive-turn wire frames (ask_user), shared by the
/// live <see cref="AgentConnectionService"/> and by any consumer that replays a
/// buffered raw frame (e.g. the desktop client's turn projection when reconstructing
/// an ask_user bubble for a page created mid-turn).
/// </summary>
public static class AgentInteractiveParsers
{
    public static AskUserRequest ParseAskUser(WireMessage msg)
    {
        var options = new List<string>();
        if (msg.TryGet("options", out var optsEl) && optsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var opt in optsEl.EnumerateArray())
            {
                if (opt.ValueKind == JsonValueKind.String) options.Add(opt.GetString()!);
            }
        }

        var fields = new List<AskUserField>();
        if (msg.TryGet("fields", out var fieldsEl) && fieldsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var f in fieldsEl.EnumerateArray())
            {
                if (f.ValueKind != JsonValueKind.Object) continue;
                fields.Add(new AskUserField(
                    f.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                    f.TryGetProperty("label", out var l) ? l.GetString() ?? "" : "",
                    f.TryGetProperty("placeholder", out var p) ? p.GetString() : null,
                    f.TryGetProperty("required", out var r) && r.ValueKind == JsonValueKind.True,
                    f.TryGetProperty("type", out var t) ? t.GetString() : null));
            }
        }

        return new AskUserRequest(
            msg.GetString("id"),
            msg.GetString("text") ?? msg.GetString("question") ?? "",
            options,
            msg.GetBool("multi_select"),
            fields);
    }
}
