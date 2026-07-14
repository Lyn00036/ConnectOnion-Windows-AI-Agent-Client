using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using ConnectOnion.Protocol;
using ConnectOnion.WinUIClient.Models;

namespace ConnectOnion.WinUIClient.Services.Runtime;

/// <summary>
/// The single source of truth for turning a run's ordered raw stream events into
/// <see cref="ChatMessage"/> bubbles. Both the live chat view model (rendering on the
/// UI thread) and the app-level persistence path (saving a finished turn when no page
/// exists) drive the exact same projection, so a bubble shows/persists identically no
/// matter who built it. Relocated from the former <c>ChatViewModel.ApplyStreamEvent</c>
/// so there is one mapping, not two.
///
/// An instance is stateful for the duration of one turn (it accumulates the per-turn
/// model/token/duration metrics) and is <b>not</b> thread-safe: the caller applies
/// events on a single thread (the UI dispatcher, or the persistence task).
/// </summary>
public sealed class ChatTurnProjection
{
    private readonly IChatProjectionTarget _target;

    // Accumulated across every llm_call/llm_result iteration of the current turn.
    private string? _turnModel;
    private long _turnTokensIn;
    private long _turnTokensOut;
    private bool _turnHasUsage;
    private double _turnDurationMs;
    private double? _turnContextPercent;
    private long _turnToolCallsCount;
    private ToolActivityProjector? _toolActivity;

    public ChatTurnProjection(IChatProjectionTarget target) => _target = target;

    /// <summary>Tool calls the agent reported this turn (used for the completion notification).</summary>
    public long TurnToolCallsCount => _turnToolCallsCount;

    /// <summary>Total wall-clock the agent spent in LLM calls this turn (for the notification).</summary>
    public double TurnDurationMs => _turnDurationMs;

    /// <summary>
    /// Applies one stream event to the message list. Interactive turns
    /// (ask_user/approval/plan_review) and onboarding arrive here as synthetic events
    /// carrying their raw JSON (see <c>AgentTurnExecutor</c>) so a page re-created mid-turn
    /// reconstructs those bubbles too, not just the plain streaming ones.
    /// </summary>
    public void Apply(AgentStreamEvent e)
    {
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
        switch (e.Type)
        {
            case "tool_call":
                GetToolActivity().ApplyCall(e);
                break;

            case "tool_result":
                _toolActivity?.ApplyResult(e);
                break;

            case "llm_call":
                if (Str("model") is { } callModel) _turnModel = callModel;
                break;

            case "llm_result":
            {
                if (Str("model") is { } resultModel) _turnModel = resultModel;

                var (tokIn, tokOut) = TryParseRawTokenUsage(root);
                if (tokIn is not null) { _turnTokensIn += tokIn.Value; _turnHasUsage = true; }
                if (tokOut is not null) { _turnTokensOut += tokOut.Value; _turnHasUsage = true; }
                if (Num("duration_ms") is { } dur) _turnDurationMs += dur;
                if (Num("context_percent") is { } ctx) _turnContextPercent = ctx;
                if (Num("tool_calls_count") is { } tcc) _turnToolCallsCount += (long)tcc;
                break;
            }

            case "thinking":
                Add(new ChatMessage
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
                    Add(NewActivity(id, "Understanding request...", EventStatus.Running));
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
                    Add(item);
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
                    Add(NewActivity(id, "Compacting context...", EventStatus.Running));
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
                GetToolActivity().AddBlocked(e);
                break;

            case "agent_image":
            {
                if (!AttachmentWireEvents.TryGetAgentImageDataUrl(WireMessage.Parse(e.RawJson), out var dataUrl))
                    break;

                var lastAgent = Messages.LastOrDefault(m => m.Role == ChatRole.Agent);
                ChatMessage messageTarget;
                if (lastAgent is not null)
                {
                    messageTarget = lastAgent;
                }
                else
                {
                    messageTarget = new ChatMessage { Id = NextId(), Role = ChatRole.Agent, AgentName = AgentName };
                    Add(messageTarget);
                }

                var attachment = new ChatAttachment
                {
                    Kind = AttachmentKind.Image,
                    FileName = "image",
                    Status = AttachmentStatus.Encoding,
                };
                messageTarget.Attachments.Add(attachment);
                _target.ResolveAgentImage(dataUrl, attachment);
                break;
            }

            case "files_received":
            {
                var names = new List<string>();
                if (AttachmentWireEvents.TryGetFilesReceived(WireMessage.Parse(e.RawJson), out var files))
                {
                    foreach (var file in files) names.Add(file.Name);
                }
                var item = NewActivity(null, $"Files received - {names.Count} file(s)", EventStatus.Done);
                if (names.Count > 0) item.EventDetail = string.Join(", ", names);
                Add(item);
                break;
            }

            case "assistant":
            {
                var content = Str("content");
                if (!string.IsNullOrEmpty(content))
                {
                    Add(new ChatMessage
                    {
                        Id = NextId(),
                        Role = ChatRole.Agent,
                        Content = content,
                        AgentName = AgentName,
                    });
                }
                break;
            }

            case "ONBOARD_SUCCESS":
                Add(NewActivity(null, "Onboarding complete", EventStatus.Done));
                break;

            case "mode_changed":
            {
                // The agent can enter plan mode by itself (triggered_by: "agent"), so this card is
                // not an echo of the user's own click — it is the only place a mode the *agent*
                // chose becomes visible.
                var item = NewActivity(null, $"Mode: {AgentModes.DisplayName(Str("mode"))}", EventStatus.Done);
                item.EventMeta = Str("triggered_by") switch
                {
                    "agent" => "by agent",
                    "ulw_checkpoint" => "by checkpoint",
                    _ => null,
                };
                Add(item);
                break;
            }

            // ---- locally-originated (see ConversationRunRegistry.PublishLocalEvent) ----

            case "stop_requested":
            {
                var stopped = Flag("stopped") == true;
                var item = NewActivity(null, stopped ? "Stopped" : "Stop requested", EventStatus.Done);
                // Worded to promise exactly what happened. A cooperative stop is a message to the
                // model, not an interrupt: the turn is still running until the agent chooses to
                // wind down, and telling the user otherwise would be a lie the spinner contradicts.
                if (!stopped) item.EventDetail = "Asked the agent to stop. It will finish the step it is on.";
                Add(item);
                break;
            }

            case "stop_forced":
                Add(new ChatMessage
                {
                    Id = NextId(),
                    Role = ChatRole.Event,
                    EventKind = "activity",
                    EventTitle = "Force-stopped",
                    EventDetail = Str("detail"),
                    Status = EventStatus.Error,
                });
                break;

            // ---- interactive turns & onboarding (synthetic events, see AgentTurnExecutor) ----

            case "ONBOARD_REQUIRED":
                if (!Messages.Any(m => m.IsOnboarding && !m.IsOnboardingSubmitted))
                {
                    Add(new ChatMessage
                    {
                        Id = NextId(),
                        Role = ChatRole.Agent,
                        AgentName = AgentName,
                        IsOnboarding = true,
                        Content = "This agent requires an invite code before the conversation can continue.",
                    });
                }
                break;

            case "ask_user":
            {
                var request = AgentInteractiveParsers.ParseAskUser(WireMessage.Parse(e.RawJson));
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
                Add(message);
                break;
            }

            case "approval_needed":
                _toolActivity?.WaitForConfirmation();
                Add(new ChatMessage
                {
                    Id = NextId(),
                    Role = ChatRole.Event,
                    EventKind = "approval",
                    EventEyebrow = "Approval needed",
                    EventTitle = Str("tool") ?? "",
                    EventDetail = Str("description"),
                    EventArgs = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("arguments", out var a)
                        ? PrettyJson(a.GetRawText()) : PrettyJson("{}"),
                    Status = EventStatus.Running,
                });
                break;

            case "plan_review":
                Add(new ChatMessage
                {
                    Id = NextId(),
                    Role = ChatRole.Event,
                    EventKind = "plan_review",
                    EventEyebrow = "Plan review",
                    EventTitle = "Review the plan",
                    EventDetail = Str("plan_content") ?? "",
                    Status = EventStatus.Running,
                });
                break;
        }
    }

    /// <summary>
    /// Adds the final agent reply exactly once — only if it differs from the last agent
    /// bubble the stream already produced (an <c>assistant</c> event), matching the SDK so
    /// the same text never renders twice.
    /// </summary>
    public void AppendFinalReply(string reply)
    {
        _toolActivity?.Complete();
        if (string.IsNullOrEmpty(reply)) return;
        var lastAgent = Messages.LastOrDefault(m => m.Role == ChatRole.Agent);
        if (lastAgent is not null && lastAgent.Content == reply) return;

        Add(new ChatMessage
        {
            Id = NextId(),
            Role = ChatRole.Agent,
            Content = reply,
            AgentName = AgentName,
        });
    }

    /// <summary>One summary bubble at the end of a turn: model + duration/tokens/context/tools.</summary>
    public void AppendTurnSummary()
    {
        var hasAnyMetric = _turnHasUsage || _turnDurationMs > 0
            || _turnContextPercent is not null || _turnToolCallsCount > 0;
        if (!hasAnyMetric && string.IsNullOrEmpty(_turnModel)) return;

        var parts = new List<string>();
        if (_turnDurationMs > 0) parts.Add(FormatDuration(_turnDurationMs));
        if (_turnHasUsage) parts.Add($"{FormatTokenCount(_turnTokensIn)}→{FormatTokenCount(_turnTokensOut)} tok");
        if (_turnContextPercent is { } ctx) parts.Add($"ctx {ctx:0.#}%");
        if (_turnToolCallsCount > 0) parts.Add($"{_turnToolCallsCount:0} tools");

        Add(new ChatMessage
        {
            Id = NextId(),
            Role = ChatRole.Event,
            EventKind = "activity",
            EventTitle = _turnModel ?? "Model",
            EventMeta = parts.Count > 0 ? string.Join(" · ", parts) : null,
            Status = EventStatus.Done,
        });
    }

    /// <summary>Finalizes the current compact timeline when the whole assistant turn fails
    /// or is cancelled. The error stays in the activity details rather than becoming a second
    /// oversized tool bubble.</summary>
    public void CompleteToolActivity(ToolActivityStatus status, string? error = null)
        => _toolActivity?.Complete(status, error);

    private ToolActivityProjector GetToolActivity()
    {
        if (_toolActivity is not null) return _toolActivity;
        var activity = new ToolActivityViewModel
        {
            TurnId = Guid.NewGuid().ToString("N"),
            DisplayMode = ToolDisplayMode.Compact,
            IsExpanded = true,
        };
        Add(new ChatMessage
        {
            Id = NextId(),
            Role = ChatRole.Event,
            EventKind = "tool_activity",
            EventTitle = "Tool execution",
            Status = EventStatus.Running,
            ToolActivity = activity,
        });
        _toolActivity = new ToolActivityProjector(activity);
        return _toolActivity;
    }

    // ---- helpers (relocated verbatim) ----

    private IList<ChatMessage> Messages => _target.Messages;
    private string? AgentName => _target.AgentName;
    private long NextId() => _target.NextId();
    private void Add(ChatMessage message) => _target.Add(message);

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

    internal static string? PrettyJson(string? raw)
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

    private static (long? input, long? output) TryParseRawTokenUsage(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("usage", out var u) && u.ValueKind == JsonValueKind.Object)
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
}

/// <summary>
/// The surface a <see cref="ChatTurnProjection"/> writes into. Implemented over a live
/// <c>ObservableCollection&lt;ChatMessage&gt;</c> by the view model and over a plain list by
/// the persistence path, so both share one projection with different side-effect handling.
/// </summary>
public interface IChatProjectionTarget
{
    IList<ChatMessage> Messages { get; }
    string? AgentName { get; }
    long NextId();
    void Add(ChatMessage message);

    /// <summary>Decode/cache an inbound agent image and fill in the attachment's local path.
    /// Live UI kicks this off-thread; persistence awaits it synchronously.</summary>
    void ResolveAgentImage(string dataUrl, ChatAttachment attachment);
}
