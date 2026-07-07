using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ConnectOnion.Protocol;
using ConnectOnion.WinUIClient.Data;
using ConnectOnion.WinUIClient.Models;
using ConnectOnion.WinUIClient.Services;

namespace ConnectOnion.WinUIClient.ViewModels;

/// <summary>
/// Stream-event processing and trace management for <see cref="ChatViewModel"/>.
/// </summary>
public sealed partial class ChatViewModel
{
    private void AddOnboardingBubble()
    {
        if (Messages.Any(message => message.IsOnboarding && !message.IsOnboardingSubmitted)) return;

        Messages.Add(new ChatMessage
        {
            Id = NextId(),
            Role = ChatRole.Agent,
            AgentName = _agent?.Name,
            IsOnboarding = true,
            Content = "This agent requires an invite code before the conversation can continue.",
        });
        StatusText = "Waiting for invite code...";
    }

    /// <summary>
    /// Maps a streamed event onto the message list, C# port of the SDK's
    /// <c>chat-item-mapper.ts</c>. Paired events merge by id into one bubble that
    /// transitions running → done/error (tool_call+tool_result, llm_call+llm_result),
    /// so a tool shows its arguments, result, and timing in a single card.
    /// </summary>
    private void ApplyStreamEvent(AgentStreamEvent e)
    {
        StatusText = e.Description;

        // Buffer trace event in memory; flushed to DB on OUTPUT.
        if (_session is not null && _currentExecutionId is not null)
        {
            _pendingTraces.Add((e.EventId ?? Guid.NewGuid().ToString(), e.Type, e.RawJson, TryParseTs(e.RawJson)));
        }

        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(e.RawJson);
            root = doc.RootElement.Clone();
        }
        catch
        {
            root = default;
        }

        string? Str(string name) => root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() : null;
        double? Num(string name) => root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
                ? v.GetDouble() : null;
        bool? Flag(string name) => root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(name, out var v)
                ? v.ValueKind == JsonValueKind.True ? true
                    : v.ValueKind == JsonValueKind.False ? false : (bool?)null
                : null;
        string? RawObj(string name) => root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.Object or JsonValueKind.Array
                ? PrettyJson(v.GetRawText()) : null;

        switch (e.Type)
        {
            case "tool_call":
                Messages.Add(new ChatMessage
                {
                    Id = NextId(),
                    Role = ChatRole.Event,
                    EventKind = "tool",
                    EventKey = Str("tool_id") ?? Str("id"),
                    EventEyebrow = "Tool",
                    EventTitle = Str("name") ?? "tool",
                    EventArgs = RawObj("args"),
                    Status = EventStatus.Running,
                });
                break;

            case "tool_result":
            {
                var existing = FindEvent(Str("tool_id") ?? Str("id"), "tool");
                if (existing is not null)
                {
                    existing.Status = Str("status") == "error" ? EventStatus.Error : EventStatus.Done;
                    existing.EventResult = Str("result");
                    if (Num("timing_ms") is { } timing) existing.EventMeta = FormatDuration(timing);
                }
                break;
            }

            case "llm_call":
            {
                var id = Str("id");
                var iteration = Num("iteration");
                var model = Str("model");
                var title = iteration is not null
                    ? $"Iteration {iteration:0}"
                    : "Thinking...";
                Messages.Add(new ChatMessage
                {
                    Id = NextId(),
                    Role = ChatRole.Event,
                    EventKind = "activity",
                    EventKey = id,
                    EventTitle = title,
                    EventDetail = model,
                    Status = EventStatus.Running,
                });
                break;
            }

            case "llm_result":
            {
                var existing = FindEvent(Str("id"), "activity");
                if (existing is not null)
                {
                    existing.Status = Str("status") == "error" ? EventStatus.Error : EventStatus.Done;
                    var iteration = Num("iteration");
                    existing.EventTitle = iteration is not null
                        ? $"Iteration {iteration:0} complete"
                        : "Thinking complete";

                    var parts = new List<string>();
                    if (Num("duration_ms") is { } dur) parts.Add(FormatDuration(dur));
                    if (ParseTokenUsage(root) is (var tokIn, var tokOut) && tokIn is not null)
                        parts.Add($"{tokIn}→{tokOut} tok");
                    if (Num("context_percent") is { } ctx)
                        parts.Add($"ctx {ctx:0.#}%");
                    if (Num("tool_calls_count") is { } tcc and > 0)
                        parts.Add($"{tcc:0} tools");
                    existing.EventMeta = string.Join(" · ", parts);

                    existing.EventDetail = Str("model") ?? existing.EventDetail;
                }
                break;
            }

            case "thinking":
                Messages.Add(new ChatMessage
                {
                    Id = NextId(),
                    Role = ChatRole.Event,
                    EventKind = "activity",
                    EventTitle = "Thinking",
                    EventDetail = Str("content"),
                    Status = EventStatus.Done,
                });
                break;

            case "intent":
            {
                var id = Str("id");
                if (Str("status") == "analyzing")
                {
                    Messages.Add(NewActivity(id, "Understanding request...", EventStatus.Running));
                }
                else if (FindEvent(id, "activity") is { } existing)
                {
                    existing.EventTitle = "Request understood";
                    existing.EventDetail = Str("ack");
                    existing.Status = EventStatus.Done;
                }
                break;
            }

            case "eval":
            {
                var id = Str("id");
                if (Str("status") == "evaluating")
                {
                    var item = NewActivity(id, "Evaluating result...", EventStatus.Running);
                    item.EventDetail = Str("expected");
                    Messages.Add(item);
                }
                else if (FindEvent(id, "activity") is { } existing)
                {
                    existing.EventTitle = "Evaluation complete";
                    existing.EventDetail = Str("summary") ?? Str("expected");
                    existing.Status = Flag("passed") == false ? EventStatus.Error : EventStatus.Done;
                }
                break;
            }

            case "compact":
            {
                var id = Str("id");
                if (Str("status") == "compacting")
                {
                    Messages.Add(NewActivity(id, "Compacting context...", EventStatus.Running));
                }
                else if (FindEvent(id, "activity") is { } existing)
                {
                    existing.EventTitle = "Context compacted";
                    existing.Status = Str("status") == "error" ? EventStatus.Error : EventStatus.Done;
                    existing.EventDetail = Str("message") ?? Str("error");
                    if (Num("context_before") is { } before && Num("context_after") is { } after)
                        existing.EventMeta = $"{before:0} → {after:0}";
                }
                break;
            }

            case "tool_blocked":
                Messages.Add(new ChatMessage
                {
                    Id = NextId(),
                    Role = ChatRole.Event,
                    EventKind = "tool",
                    EventEyebrow = "Tool blocked",
                    EventTitle = Str("tool") ?? "tool",
                    EventDetail = Str("message"),
                    EventMeta = Str("reason"),
                    EventResult = Str("command"),
                    Status = EventStatus.Error,
                });
                break;

            case "files_received":
            {
                var names = new List<string>();
                if (root.ValueKind == JsonValueKind.Object
                    && root.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array)
                {
                    foreach (var file in files.EnumerateArray())
                        if (file.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
                            names.Add(n.GetString()!);
                }
                var item = NewActivity(null, $"Files received - {names.Count} file(s)", EventStatus.Done);
                if (names.Count > 0) item.EventDetail = string.Join(", ", names);
                Messages.Add(item);
                break;
            }

            case "assistant":
            {
                var content = Str("content");
                if (!string.IsNullOrEmpty(content))
                {
                    Messages.Add(new ChatMessage
                    {
                        Id = NextId(),
                        Role = ChatRole.Agent,
                        Content = content,
                        AgentName = _agent?.Name,
                    });
                }
                break;
            }

            case "ONBOARD_SUCCESS":
                Messages.Add(NewActivity(null, "Onboarding complete", EventStatus.Done));
                break;
        }
    }

    private ChatMessage NewActivity(string? key, string title, EventStatus status) => new()
    {
        Id = NextId(),
        Role = ChatRole.Event,
        EventKind = "activity",
        EventKey = key,
        EventTitle = title,
        Status = status,
    };

    private ChatMessage? FindEvent(string? key, string kind)
        => string.IsNullOrEmpty(key)
            ? null
            : Messages.LastOrDefault(m => m.IsEvent && m.EventKind == kind && m.EventKey == key);

    private static string FormatDuration(double ms)
        => ms < 1000 ? $"{ms:0} ms" : $"{ms / 1000:0.0} s";

    private static string? PrettyJson(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                doc.RootElement.WriteTo(writer);
            }
            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch
        {
            return raw;
        }
    }

    private async Task FlushPendingTracesAsync()
    {
        if (_session is null || _pendingTraces.Count == 0) return;
        var snap = _pendingTraces.ToList();
        _pendingTraces.Clear();

        foreach (var (eventId, type, rawJson, ts) in snap)
        {
            await AppServices.Conversations.InsertTraceEventAsync(
                eventId, _session.Id, _currentExecutionId,
                _connection?.SessionId, type, rawJson, ts);
        }
    }

    private static double? TryParseTs(string rawJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            if (doc.RootElement.TryGetProperty("ts", out var v) && v.ValueKind == JsonValueKind.Number)
                return v.GetDouble();
        }
        catch { }
        return null;
    }

    private static (string? input, string? output) ParseTokenUsage(JsonElement root)
    {
        if (root.TryGetProperty("usage", out var u) && u.ValueKind == JsonValueKind.Object)
        {
            var input = u.TryGetProperty("input_tokens", out var i) && i.TryGetInt64(out var iv)
                ? FormatTokenCount(iv) : null;
            var output = u.TryGetProperty("output_tokens", out var o) && o.TryGetInt64(out var ov)
                ? FormatTokenCount(ov) : null;
            return (input, output);
        }
        return (null, null);
    }

    private static string FormatTokenCount(long tokens) =>
        tokens switch
        {
            >= 1_000_000 => $"{tokens / 1_000_000.0:0.#}M",
            >= 1_000 => $"{tokens / 1000.0:0.#}K",
            _ => tokens.ToString(),
        };
}
