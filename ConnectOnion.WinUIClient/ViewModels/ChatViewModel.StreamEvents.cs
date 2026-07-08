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

    // ---- Interactive turns: ask_user / approval_needed / plan_review ----
    // Rendered as persistent inline chat bubbles (matching the reference web
    // client's chat-item feed) rather than transient ContentDialogs, so the
    // question/decision stays visible in history instead of vanishing once
    // the popup closes.

    private void AddAskUserBubble(AskUserRequest request)
    {
        var message = new ChatMessage
        {
            Id = NextId(),
            Role = ChatRole.Event,
            EventKind = "ask_user",
            EventKey = request.Id,
            EventEyebrow = "Question",
            EventTitle = request.Text,
            Status = EventStatus.Running,
        };

        message.AskUserMultiSelect = request.MultiSelect;
        foreach (var option in request.Options)
        {
            message.AskUserOptions.Add(option);
            if (request.MultiSelect) message.AskUserOptionEntries.Add(new AskUserOptionEntry { Text = option });
        }
        foreach (var field in request.Fields)
        {
            message.AskUserFields.Add(new AskUserFieldEntry
            {
                Name = field.Name,
                Label = field.Label,
                Placeholder = field.Placeholder,
            });
        }

        Messages.Add(message);
        StatusText = "Waiting for your answer...";
    }

    /// <summary>Submits an ask_user bubble's answer. Mirrors the composition logic the old ContentDialog used: dynamic fields → JSON, multi-select → comma-joined checked options, else free text or the selected radio option.</summary>
    public async Task SubmitAskUserAnswerAsync(ChatMessage message)
    {
        if (message.Status != EventStatus.Running) return;

        string answer;
        if (message.AskUserFields.Count > 0)
        {
            var values = new Dictionary<string, string>();
            foreach (var field in message.AskUserFields) values[field.Name] = field.Value;
            answer = JsonSerializer.Serialize(values, AppJsonContext.Default.DictionaryStringString);
        }
        else if (message.AskUserMultiSelect)
        {
            var selected = message.AskUserOptionEntries.Where(o => o.IsChecked).Select(o => o.Text).ToList();
            answer = selected.Count > 0 ? string.Join(", ", selected) : message.AskUserFreeText.Trim();
        }
        else
        {
            answer = !string.IsNullOrWhiteSpace(message.AskUserFreeText)
                ? message.AskUserFreeText.Trim()
                : message.SelectedAskUserOption ?? "";
        }

        if (string.IsNullOrEmpty(answer)) return;

        message.EventMeta = $"Answered: {answer}";
        message.Status = EventStatus.Done;
        await PersistConversationAsync();
        StatusText = "";
        await (_connection?.RespondAskUserAsync(answer) ?? Task.CompletedTask);
    }

    private void AddApprovalBubble(ApprovalRequest request)
    {
        Messages.Add(new ChatMessage
        {
            Id = NextId(),
            Role = ChatRole.Event,
            EventKind = "approval",
            EventEyebrow = "Approval needed",
            EventTitle = request.Tool,
            EventDetail = request.Description,
            EventArgs = PrettyJson(request.ArgumentsJson),
            Status = EventStatus.Running,
        });
        StatusText = "Waiting for approval...";
    }

    /// <summary><paramref name="scope"/> is "once" or "session", matching <see cref="AgentConnectionService.RespondApprovalAsync"/>.</summary>
    public async Task SubmitApprovalAsync(ChatMessage message, bool approved, string scope)
    {
        if (message.Status != EventStatus.Running) return;

        message.EventMeta = approved
            ? (scope == "session" ? "Approved for session" : "Approved once")
            : "Rejected";
        message.Status = approved ? EventStatus.Done : EventStatus.Error;
        await PersistConversationAsync();
        StatusText = "";
        await (_connection?.RespondApprovalAsync(approved, scope) ?? Task.CompletedTask);
    }

    private void AddPlanReviewBubble(PlanReviewRequest request)
    {
        Messages.Add(new ChatMessage
        {
            Id = NextId(),
            Role = ChatRole.Event,
            EventKind = "plan_review",
            EventEyebrow = "Plan review",
            EventTitle = "Review the plan",
            EventDetail = request.PlanContent,
            Status = EventStatus.Running,
        });
        StatusText = "Waiting for plan review...";
    }

    /// <summary>Approving with no feedback sends the SDK's default "proceed" message; requesting changes with no feedback is a no-op (nothing to send) — matches the previous dialog's behavior.</summary>
    public async Task SubmitPlanReviewAsync(ChatMessage message, bool approved)
    {
        if (message.Status != EventStatus.Running) return;

        var feedback = message.PlanFeedback.Trim();
        string responseMessage;
        if (approved)
        {
            responseMessage = feedback.Length == 0 ? "Plan approved. Proceed with implementation." : feedback;
            message.EventMeta = "Plan approved";
            message.Status = EventStatus.Done;
        }
        else
        {
            if (feedback.Length == 0) return;
            responseMessage = feedback;
            message.EventMeta = "Changes requested";
            message.Status = EventStatus.Error;
        }

        await PersistConversationAsync();
        StatusText = "";
        await (_connection?.RespondPlanReviewAsync(responseMessage) ?? Task.CompletedTask);
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
                // No per-iteration bubble: a turn can run several LLM calls via the
                // tool-use loop, and showing "Iteration 1", "Iteration 2", ... was
                // noise. Only the model name is worth remembering here; token usage
                // and a single summary bubble are handled on "llm_result" / at the
                // end of the turn (see AddTurnSummaryBubble).
                if (Str("model") is { } callModel) _turnModel = callModel;
                break;
            }

            case "llm_result":
            {
                if (Str("model") is { } resultModel) _turnModel = resultModel;

                var (tokIn, tokOut) = TryParseRawTokenUsage(root);
                if (tokIn is not null) { _turnTokensIn += tokIn.Value; _turnHasUsage = true; }
                if (tokOut is not null) { _turnTokensOut += tokOut.Value; _turnHasUsage = true; }

                // Total time actually spent in LLM calls this turn.
                if (Num("duration_ms") is { } dur) _turnDurationMs += dur;

                // Context fill is a point-in-time reading, not a per-iteration
                // delta — keep the latest one, not a sum.
                if (Num("context_percent") is { } ctx) _turnContextPercent = ctx;

                // Tools invoked as a result of this LLM call — sum across iterations
                // for a turn total.
                if (Num("tool_calls_count") is { } tcc) _turnToolCallsCount += (long)tcc;

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

            case "agent_image":
            {
                // Typed parser (not raw JsonDocument.Parse in this switch's usual
                // style) per the real `agent_image` wire event —
                // network/io/base.py:send_image → {"type":"agent_image","image":"data:...;base64,..."}.
                // A malformed/missing `image` field is ignored, not thrown, so one
                // bad frame can't take down the receive loop.
                if (!AttachmentWireEvents.TryGetAgentImageDataUrl(WireMessage.Parse(e.RawJson), out var dataUrl))
                {
                    System.Diagnostics.Debug.WriteLine("[Attachment] malformed agent_image event ignored");
                    break;
                }

                // Mirror the SDK's chat-item-mapper.ts: merge onto the last agent
                // bubble if one is open, else start a fresh one.
                var lastAgent = Messages.LastOrDefault(m => m.Role == ChatRole.Agent);
                ChatMessage target;
                if (lastAgent is not null)
                {
                    target = lastAgent;
                }
                else
                {
                    target = new ChatMessage { Id = NextId(), Role = ChatRole.Agent, AgentName = _agent?.Name };
                    Messages.Add(target);
                }

                var attachment = new ChatAttachment
                {
                    Kind = AttachmentKind.Image,
                    FileName = "image",
                    Status = AttachmentStatus.Encoding,
                };
                target.Attachments.Add(attachment);
                System.Diagnostics.Debug.WriteLine("[Attachment] received source=data-url");
                _ = ResolveAgentImageAsync(dataUrl, attachment);
                break;
            }

            case "files_received":
            {
                // Typed parser for the real `files_received` echo event
                // (agent.py:317-323) — just a name/path notification of what the
                // *user* uploaded, not a download mechanism (no local file exists
                // at the server's path from the client's point of view).
                var names = new List<string>();
                if (AttachmentWireEvents.TryGetFilesReceived(WireMessage.Parse(e.RawJson), out var files))
                {
                    foreach (var file in files) names.Add(file.Name);
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

    /// <summary>
    /// Decodes/downloads an inbound image off the UI thread and caches it to disk
    /// (see <see cref="Services.AttachmentImageCacheService"/>) so the message list
    /// and SQLite only ever hold a file path, never the base64 payload itself.
    /// Failure (invalid base64, unsupported scheme, download timeout, ...) marks
    /// the attachment Failed rather than throwing — a single bad image must not
    /// crash the receive loop or the rest of the conversation.
    /// </summary>
    private async Task ResolveAgentImageAsync(string dataUrl, ChatAttachment attachment)
    {
        var resolved = await AttachmentImageCacheService
            .ResolveAndCacheAsync(dataUrl, AppServices.Http)
            .ConfigureAwait(false);

        _dispatcher.TryEnqueue(() =>
        {
            if (resolved is { } value)
            {
                attachment.LocalCachePath = value.LocalPath;
                attachment.MimeType = value.MimeType;
                attachment.Status = AttachmentStatus.Sent;
            }
            else
            {
                attachment.Status = AttachmentStatus.Failed;
                attachment.Error = "Image could not be loaded.";
                System.Diagnostics.Debug.WriteLine("[Attachment] failed to decode/cache agent image");
            }
        });
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

    private static (long? input, long? output) TryParseRawTokenUsage(JsonElement root)
    {
        if (root.TryGetProperty("usage", out var u) && u.ValueKind == JsonValueKind.Object)
        {
            long? input = u.TryGetProperty("input_tokens", out var i) && i.TryGetInt64(out var iv) ? iv : null;
            long? output = u.TryGetProperty("output_tokens", out var o) && o.TryGetInt64(out var ov) ? ov : null;
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

    /// <summary>
    /// One summary bubble at the end of a turn: model name + total duration,
    /// tokens, final context usage, and tool-call count accumulated across every
    /// llm_call/llm_result iteration of that turn (replaces the old per-iteration
    /// "Iteration N" bubbles, which showed these same four fields but once per
    /// iteration). Skipped entirely if the agent never reported anything —
    /// nothing worth showing.
    /// </summary>
    private void AddTurnSummaryBubble()
    {
        var hasAnyMetric = _turnHasUsage || _turnDurationMs > 0
            || _turnContextPercent is not null || _turnToolCallsCount > 0;
        if (!hasAnyMetric && string.IsNullOrEmpty(_turnModel)) return;

        var parts = new List<string>();
        if (_turnDurationMs > 0) parts.Add(FormatDuration(_turnDurationMs));
        if (_turnHasUsage) parts.Add($"{FormatTokenCount(_turnTokensIn)}→{FormatTokenCount(_turnTokensOut)} tok");
        if (_turnContextPercent is { } ctx) parts.Add($"ctx {ctx:0.#}%");
        if (_turnToolCallsCount > 0) parts.Add($"{_turnToolCallsCount:0} tools");

        Messages.Add(new ChatMessage
        {
            Id = NextId(),
            Role = ChatRole.Event,
            EventKind = "activity",
            EventTitle = _turnModel ?? "Model",
            EventMeta = parts.Count > 0 ? string.Join(" · ", parts) : null,
            Status = EventStatus.Done,
        });
    }
}
