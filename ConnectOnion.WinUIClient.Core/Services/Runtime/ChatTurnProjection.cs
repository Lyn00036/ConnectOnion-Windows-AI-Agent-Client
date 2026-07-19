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
public sealed partial class ChatTurnProjection
{
    private readonly IChatProjectionTarget _target;

    /// <summary>Stand-in root for a frame whose JSON didn't parse. Deliberately never disposed —
    /// it is two bytes, shared for the process's lifetime.</summary>
    private static readonly JsonDocument EmptyObject = JsonDocument.Parse("{}");

    // Accumulated across every llm_call/llm_result iteration of the current turn.
    private string? _turnModel;
    private long _turnTokensIn;
    private long _turnTokensOut;
    private bool _turnHasUsage;
    private double _turnDurationMs;
    private double? _turnContextPercent;
    private long _turnToolCallsCount;
    private ToolActivityProjector? _toolActivity;
    private ChatMessage? _streamedFinalReply;
    private readonly List<ChatMessage> _thinkingActivities = new();
    // The card the current run of consecutive `thinking` frames is accumulating into; cleared by
    // the first frame of any other type (see ApplyParsed).
    private ChatMessage? _currentThinking;
    // The turn's single spinner row, opened by the first llm_call. Distinct from _currentThinking:
    // that one holds reasoning text the agent sent, this one holds nothing and exists only so a
    // long LLM call does not look like a hung app.
    //
    // One row for the whole turn, reused across iterations. An agentic turn makes an llm_call per
    // iteration — fifteen tools is eight or more round trips — and a row each left a column of
    // "Thought for 2.4 s / 6.6 s / 5.1 s …" that says nothing an aggregate does not.
    private ChatMessage? _llmThinking;
    // Set once this turn has produced a thinking card with real reasoning text. A turn-level
    // latch rather than a "_currentThinking is open" check, because ApplyParsed clears that field
    // on every non-thinking frame — including the llm_call we are deciding about.
    private bool _sawThinkingText;
    // Inbound agent images are buffered as they stream in and only projected once the turn's
    // final output has been appended — the reference behaviour is that a received image belongs
    // *after* the agent's answer, not floating above the tool activity / summary / reply that
    // arrive later in the same turn. Flushed by AppendCompletedTurn / CompleteToolActivity.
    private readonly List<string> _pendingAgentImages = new();
    private bool _wasInterrupted;

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
        // The document is kept alive for the whole of Apply and disposed at the end, rather than
        // cloning its root out of a using-block: Clone() deep-copies the entire payload, which for
        // an llm_call frame is the conversation's whole message history and for an agent_image is
        // the base64 image — paid once per event, on both the live and the persist pass.
        JsonDocument? doc = null;
        try { doc = JsonDocument.Parse(e.RawJson); } catch { /* leave root undefined below */ }

        try
        {
            ApplyParsed(e, doc?.RootElement ?? default);
        }
        finally
        {
            doc?.Dispose();
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
        CompleteThinkingActivities(EventStatus.Done);
        if (string.IsNullOrEmpty(reply)) return;
        if (_streamedFinalReply is not null && _streamedFinalReply.Content == reply)
        {
            if (_wasInterrupted) _streamedFinalReply.EventMeta = "Stopped";
            return;
        }

        Add(new ChatMessage
        {
            Id = NextId(),
            Role = ChatRole.Agent,
            Content = reply,
            AgentName = AgentName,
            EventMeta = _wasInterrupted ? "Stopped" : null,
        });
    }

    /// <summary>
    /// Completes a successful turn in display order: tool activity, consumption summary, then the
    /// final output. If the stream already emitted the final assistant bubble, it is temporarily
    /// removed and reinserted after the summary instead of being duplicated.
    /// </summary>
    public void AppendCompletedTurn(string reply)
    {
        _toolActivity?.Complete();
        CompleteThinkingActivities(EventStatus.Done);

        // Reuse the streamed bubble only when it *is* the final answer. An empty reply counts as
        // a match (the OUTPUT carried nothing new), but a reply that differs means the stream's
        // bubble was a partial, so it stays where it is and the real answer is appended below.
        var streamedFinal = _streamedFinalReply is not null
            && (string.IsNullOrEmpty(reply) || _streamedFinalReply.Content == reply)
                ? _streamedFinalReply
                : null;
        // Remove-and-reinsert rather than inserting the summary above it: the bubble keeps its
        // identity (and its id), so nothing downstream sees it as a new message.
        if (streamedFinal is not null) Messages.Remove(streamedFinal);

        AppendTurnSummary();

        if (streamedFinal is not null)
        {
            if (_wasInterrupted) streamedFinal.EventMeta = "Stopped";
            Add(streamedFinal);
        }
        else
        {
            AppendFinalReply(reply);
        }

        FlushPendingImages();
    }

    /// <summary>One summary bubble before the final output: model + duration/tokens/context/tools.</summary>
    public void AppendTurnSummary()
    {
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
            EventKey = "turn_usage",
            EventTitle = _turnModel ?? "Turn usage",
            // The bubble is added even when the agent reported nothing measurable. "Usage not
            // reported" is an honest answer; omitting the row entirely would leave the user
            // unable to tell a free turn from an unreported one.
            EventMeta = parts.Count > 0 ? string.Join(" · ", parts) : "Usage not reported",
            Status = EventStatus.Done,
        });
    }

    /// <summary>Finalizes the current compact timeline when the whole assistant turn fails
    /// or is cancelled. The error stays in the activity details rather than becoming a second
    /// oversized tool bubble.</summary>
    public void CompleteToolActivity(ToolActivityStatus status, string? error = null)
    {
        _toolActivity?.Complete(status, error);
        CompleteThinkingActivities(status == ToolActivityStatus.Failed
            ? EventStatus.Error
            : EventStatus.Done);
        // A turn that failed or was cancelled may still have received images before it stopped;
        // don't lose them — flush onto whatever output exists so far.
        FlushPendingImages();
    }

    /// <summary>
    /// Projects the images buffered from this turn's <c>agent_image</c> events, attaching them to
    /// the final agent bubble (the reply) if that is the last message, or to a fresh agent bubble
    /// otherwise — so a received image always lands after the turn's final output, never above it.
    /// </summary>
    private void FlushPendingImages()
    {
        if (_pendingAgentImages.Count == 0) return;

        var target = Messages.Count > 0 && Messages[^1].Role == ChatRole.Agent
            ? Messages[^1]
            : null;
        if (target is null)
        {
            target = new ChatMessage { Id = NextId(), Role = ChatRole.Agent, AgentName = AgentName };
            Add(target);
        }

        foreach (var dataUrl in _pendingAgentImages)
        {
            // Attached in the Encoding state and handed to the target, which decodes/caches it
            // to disk off this path and then flips the status. The bubble therefore renders a
            // placeholder immediately instead of blocking the turn on image I/O — and the
            // base64 payload never lands on the ChatMessage, only the resulting cache path.
            // "image" is a placeholder name; the real one comes from the content hash.
            var attachment = new ChatAttachment
            {
                Kind = AttachmentKind.Image,
                FileName = "image",
                Status = AttachmentStatus.Encoding,
            };
            target.Attachments.Add(attachment);
            _target.ResolveAgentImage(dataUrl, attachment);
        }
        _pendingAgentImages.Clear();
    }

    /// <summary>Stops every thinking spinner this turn opened. There is no "thinking finished"
    /// frame on the wire — the agent simply moves on — so the loader is retired by whatever
    /// comes next (a reply, a failure, the end of the turn). Only Running rows are touched, so
    /// an already-settled one keeps the status it was settled with.</summary>
    private void CompleteThinkingActivities(EventStatus status)
    {
        SettleLlmThinking();
        foreach (var thinking in _thinkingActivities)
        {
            if (thinking.Status != EventStatus.Running) continue;
            thinking.Status = status;
            thinking.ThinkingElapsedLabel = "";
        }
    }

    /// <summary>
    /// Settles the "Thinking..." row an <c>llm_call</c> opened, stamping how long the call took.
    /// The duration is the row's whole reason to survive: <c>llm_call</c> carries only a model and
    /// an iteration number, so without it a finished row says nothing the transcript does not
    /// already show. With it, "Thought for 2.0 s" is the one fact about the pause the user just
    /// sat through.
    ///
    /// <paramref name="durationMs"/> is the agent's own measurement when <c>llm_result</c>
    /// supplies one; otherwise it is wall-clock since the row appeared, which also covers the run
    /// that fails or is stopped mid-call and never gets a result frame at all.
    /// </summary>
    /// <summary>Opens the spinner row for one <c>llm_call</c>, if one is not already up.</summary>
    private void StartLlmThinking()
    {
        if (_llmThinking is not null) return;
        _llmThinking = new ChatMessage
        {
            Id = NextId(),
            Role = ChatRole.Event,
            EventKind = "activity",
            EventTitle = "Thinking",
            Status = EventStatus.Running,
        };
        _thinkingActivities.Add(_llmThinking);
        Add(_llmThinking);
    }

    /// <summary>
    /// Takes the spinner row away once the call comes back. It is removed, not settled with a
    /// duration: the elapsed seconds are the live counter's job while the model is working, and
    /// once it has finished the pause is over and there is nothing left to report — the turn's
    /// total time is already on the usage line. Removing also means a turn with a dozen
    /// iterations never stacks up a dozen spent rows.
    ///
    /// Called on <c>llm_result</c>, on the first <c>thinking</c> frame (real reasoning text
    /// supersedes the bare indicator), and at turn end — a run that dies mid-call never gets its
    /// result frame and must not leave a spinner turning forever.
    /// </summary>
    private void SettleLlmThinking()
    {
        if (_llmThinking is null) return;
        Messages.Remove(_llmThinking);
        _thinkingActivities.Remove(_llmThinking);
        _llmThinking = null;
    }

    /// <summary>The turn's one tool-activity card, created on first use. Lazily, because most
    /// turns call no tools at all and an empty card is pure clutter; one per turn, because the
    /// card's job is to collapse a turn's whole tool timeline into a single expandable row.</summary>
    private ToolActivityProjector GetToolActivity()
    {
        if (_toolActivity is not null) return _toolActivity;
        var activity = new ToolActivityViewModel
        {
            TurnId = Guid.NewGuid().ToString("N"),
            DisplayMode = ToolDisplayMode.Compact,
            // Starts collapsed (the model's default) — stated explicitly because this is the
            // card a live turn creates, and it is the one users see most.
            IsExpanded = false,
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
        _toolActivity = new ToolActivityProjector(activity, _target.IsLiveView);
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

    /// <summary>Finds the open row a completion frame belongs to. <c>LastOrDefault</c>, not
    /// <c>FirstOrDefault</c>: agents reuse ids across a long conversation, and the most recent
    /// row carrying that key is always the one this frame is completing.</summary>
    private ChatMessage? FindEvent(string? key, string kind)
        => string.IsNullOrEmpty(key)
            ? null
            : Messages.LastOrDefault(m => m.IsEvent && m.EventKind == kind && m.EventKey == key);

    private static string FormatDuration(double ms)
        => ms < 1000 ? $"{ms:0} ms" : $"{ms / 1000:0.0} s";

    /// <summary>Re-indents a JSON blob for the approval card's argument pane. Unparseable input
    /// is returned untouched rather than swallowed — the user still needs to see what the agent
    /// is asking to run, even if it isn't valid JSON.</summary>
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
            _ => tokens.ToString(System.Globalization.CultureInfo.InvariantCulture),
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

    /// <summary>
    /// True when this projection is driving a chat page the user is actually looking at, false
    /// when it is the headless persistence pass. The only thing it gates is auto-expansion of a
    /// failed tool card: springing a card open is a way of interrupting someone, which only makes
    /// sense if they are there to be interrupted. Persisted history opens collapsed regardless, so
    /// reopening an old conversation does not replay every past failure at full height.
    /// </summary>
    bool IsLiveView { get; }
    long NextId();
    void Add(ChatMessage message);

    /// <summary>Decode/cache an inbound agent image and fill in the attachment's local path.
    /// Live UI kicks this off-thread; persistence awaits it synchronously.</summary>
    void ResolveAgentImage(string dataUrl, ChatAttachment attachment);
}
