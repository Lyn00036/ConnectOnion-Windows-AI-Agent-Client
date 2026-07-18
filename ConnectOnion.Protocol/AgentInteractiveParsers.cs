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
    /// <summary>Parses an <c>ask_user</c> frame into the request the UI renders. Total by
    /// design — every field degrades to an empty/default value rather than throwing, because a
    /// parse failure here would strand the agent on a question the user can never answer. A
    /// malformed frame is better shown as a bare prompt than not shown at all.</summary>
    public static AskUserRequest ParseAskUser(WireMessage msg)
    {
        // Non-string entries are dropped rather than stringified: an option is a literal the
        // client sends straight back, so anything of another shape has no valid answer form.
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
                    // Required only when explicitly true — a missing or non-boolean value must
                    // not block submission on a field the agent never actually demanded.
                    f.TryGetProperty("required", out var r) && r.ValueKind == JsonValueKind.True,
                    f.TryGetProperty("type", out var t) ? t.GetString() : null));
            }
        }

        return new AskUserRequest(
            msg.GetString("id"),
            // "text" is current; "question" is what older agents send. Both spellings have to
            // keep working — the client cannot require an agent upgrade to ask a question.
            msg.GetString("text") ?? msg.GetString("question") ?? "",
            options,
            msg.GetBool("multi_select"),
            fields);
    }
}
