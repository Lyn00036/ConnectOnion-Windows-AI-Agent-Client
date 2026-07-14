using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using ConnectOnion.Protocol;
using ConnectOnion.Protocol.Runtime;
using ConnectOnion.WinUIClient.Data;
using ConnectOnion.WinUIClient.Models;
using ConnectOnion.WinUIClient.Services.Attachments;
using ConnectOnion.WinUIClient.Services.Notifications;

namespace ConnectOnion.WinUIClient.Services.Runtime;

/// <summary>
/// The app-level facade tying the per-conversation connection registry to the headless
/// <see cref="ConversationRunRegistry"/>. Owns the turn lifecycle end to end: it persists
/// the user message, starts the run, drives the reply on an app-owned socket, and — when
/// the reply lands — projects the buffered stream into bubbles and writes them
/// <b>before</b> the run is marked Completed. Pages only ever subscribe.
/// </summary>
public sealed class AgentSessionManager : IAgentSessionManager, IRunPersistence
{
    private readonly HttpClient _http;
    private readonly ConversationRepository _conversations;
    private readonly UsageRepository _usage;
    private readonly AgentConnectionRegistry _connections;
    private readonly ConversationRunRegistry _runs;
    private readonly ConcurrentDictionary<string, AgentConfig> _agentsById = new();
    // The registry intentionally persists a reply before publishing its terminal state. Keep
    // that short transition observable so a page opened in the gap does not replay the same
    // stream events onto the history it just loaded from the database. Each marker is only
    // needed for that brief window, so the set is FIFO-capped (see MarkHistoryPersisted) rather
    // than grown one entry per turn for the whole app lifetime.
    private readonly ConcurrentDictionary<string, byte> _historyPersistedRuns = new();
    private readonly ConcurrentQueue<string> _historyPersistedOrder = new();
    private const int MaxPersistedRunMarkers = 128;

    // Interactive-turn answers (ask_user / approval / plan_review) captured from the page as the
    // user resolves them, keyed by conversation id and consumed at persist time in the order the
    // cards were created. The live stream carries each *request* but never the user's reply, so
    // without this the app-level persist (which re-projects cards from the reply-less event
    // stream) has no way to record the chosen result — a reloaded card would show dead controls.
    private readonly ConcurrentDictionary<string, ConcurrentQueue<InteractiveAnswer>> _interactiveAnswers = new();

    // The interactive turn (if any) the agent is currently blocked on, per conversation. Stop
    // reads this to pick its tier: only an "approval" can be turned into a real halt.
    private readonly ConcurrentDictionary<string, string> _pendingInteractive = new();

    // Conversations whose local run was force-stopped while the host's agent was (as far as we
    // know) still executing. Cleared once the host confirms the session went idle.
    private readonly ConcurrentDictionary<string, byte> _orphanedRuns = new();

    /// <summary>What a cooperative stop actually says to the agent. Phrased as an instruction the
    /// model can act on, because that is all it is: the host frames a runtime input with "do NOT
    /// abandon the original task unless they explicitly say to stop", so this has to be that
    /// explicit say-so, in those words.</summary>
    private const string StopPrompt =
        "Stop what you are doing and end this turn now. Do not start any new tool calls. "
        + "Briefly summarize what you completed and what is left unfinished.";

    /// <summary>Shown for an interactive turn that history reload finds was never answered
    /// (the WebSocket wait it replied to is gone, so it can never be resolved now).</summary>
    private const string NoSelectionMeta = "No selection";

    /// <summary>A resolved interactive answer: the meta text to show (e.g. "Answered: yes",
    /// "Approved once", "Rejected") and the status it resolves the card to.</summary>
    private readonly record struct InteractiveAnswer(string Meta, EventStatus Status);

    /// <param name="usage">Optional so existing callers and tests that don't care about accounting
    /// can omit it; a default instance is used when it isn't supplied.</param>
    public AgentSessionManager(
        HttpClient http, ConversationRepository conversations, UsageRepository? usage = null)
    {
        _http = http;
        _conversations = conversations;
        _usage = usage ?? new UsageRepository();
        _connections = new AgentConnectionRegistry(http);
        _runs = new ConversationRunRegistry(this, new DebugRunLogger(), onError: ex =>
            Debug.WriteLine($"[run] error: {ex}"));
    }

    /// <summary>The app-owned sockets, exposed so the view model can answer interactive turns
    /// and trigger explicit reconnects on the same connection the run uses.</summary>
    public AgentConnectionRegistry Connections => _connections;

    /// <summary>Records the result the user chose for an interactive turn (ask_user / approval /
    /// plan_review) so the app-level persist can stamp it onto the card it re-projects from the
    /// reply-less event stream. Called by the chat page the moment it resolves the turn; answers
    /// are consumed in creation order when the run is persisted.</summary>
    public void RecordInteractiveAnswer(string conversationId, string meta, EventStatus status)
        => _interactiveAnswers.GetOrAdd(conversationId, _ => new ConcurrentQueue<InteractiveAnswer>())
            .Enqueue(new InteractiveAnswer(meta, status));

    // ---- IAgentSessionManager ----

    public async Task<SendMessageResult> SendMessageAsync(
        AgentConfig agent, string conversationId, string prompt,
        IReadOnlyList<PendingAttachment>? attachments = null,
        string? mode = null)
    {
        _agentsById[agent.Id] = agent;
        // A fresh run starts clean: drop any interactive answers left over from an abandoned
        // prior turn so they can't leak onto this run's cards at persist time.
        _interactiveAnswers.TryRemove(conversationId, out _);
        _pendingInteractive.TryRemove(conversationId, out _);

        List<string>? images = null;
        List<OutgoingFileAttachment>? files = null;
        var userAttachments = new List<ChatAttachment>();

        if (attachments is { Count: > 0 })
        {
            images = new List<string>();
            files = new List<OutgoingFileAttachment>();
            foreach (var attachment in attachments)
            {
                userAttachments.Add(new ChatAttachment
                {
                    Kind = attachment.Kind,
                    FileName = attachment.FileName,
                    MimeType = attachment.MimeType,
                    SizeBytes = attachment.SizeBytes,
                    LocalCachePath = attachment.LocalPath,
                    Status = AttachmentStatus.Sent,
                });

                if (attachment.DataUrl is null) continue;
                if (attachment.Kind == AttachmentKind.Image) images.Add(attachment.DataUrl);
                else files.Add(new OutgoingFileAttachment(attachment.FileName, attachment.DataUrl));

                // Release the base64 payload immediately — the wire lists now hold it,
                // and it must not linger on the composer's draft.
                attachment.DataUrl = null;
            }
            if (images.Count == 0) images = null;
            if (files.Count == 0) files = null;
        }

        // Persist the user message up front so a page opening this conversation mid-turn
        // loads it from the DB and then replays the run's events on top. This is a pure append —
        // reading the whole conversation back just to take a max(id) and then rewriting every row
        // of it was two full table scans per turn for one new bubble.
        var userMessage = new ChatMessage
        {
            Id = await _conversations.GetNextMessageIdAsync(conversationId).ConfigureAwait(false),
            Role = ChatRole.User,
            Content = prompt,
        };
        foreach (var attachment in userAttachments) userMessage.Attachments.Add(attachment);
        await _conversations.UpsertMessagesAsync(conversationId, new[] { userMessage }).ConfigureAwait(false);

        var runId = Guid.NewGuid().ToString();
        var request = new TurnRequest(
            RunId: runId,
            ConversationId: conversationId,
            AgentId: agent.Id,
            UserMessageId: Guid.NewGuid().ToString(),
            AssistantMessageId: Guid.NewGuid().ToString(),
            Prompt: prompt,
            Images: images,
            Files: files,
            SessionId: conversationId,
            Mode: mode);

        await _conversations.InsertExecutionAsync(runId, conversationId, null, prompt, "running").ConfigureAwait(false);

        var connection = _connections.GetOrCreate(conversationId, agent);
        _runs.StartRun(request, NewExecutor(agent, conversationId, connection));

        return new SendMessageResult(runId, request.UserMessageId, request.AssistantMessageId);
    }

    private AgentTurnExecutor NewExecutor(
        AgentConfig agent, string conversationId, AgentConnectionService connection)
        => new(
            connection,
            r => NotifyApproval(agent, conversationId, r),
            kind => _pendingInteractive[conversationId] = kind);

    public ConversationRunSnapshot? GetActiveRun(string conversationId) => _runs.GetActiveRun(conversationId);

    public bool IsRunHistoryPersisted(string runId) => _historyPersistedRuns.ContainsKey(runId);

    public IReadOnlyList<ConversationRunSnapshot> GetActiveRuns() => _runs.GetActiveRuns();

    public IDisposable Subscribe(string conversationId, Action<ConversationRunSnapshot> handler)
        => _runs.Subscribe(conversationId, handler);

    public Task CancelRunAsync(string conversationId, string runId) => _runs.CancelRunAsync(conversationId, runId);

    public ConversationRunSnapshot? RetryRun(AgentConfig agent, string conversationId)
    {
        _agentsById[agent.Id] = agent;
        _interactiveAnswers.TryRemove(conversationId, out _);
        _pendingInteractive.TryRemove(conversationId, out _);
        var connection = _connections.GetOrCreate(conversationId, agent);
        return _runs.RetryRun(conversationId, NewExecutor(agent, conversationId, connection));
    }

    // ---- Stop, in the three tiers the protocol actually permits ----

    public void ClearPendingInteractive(string conversationId)
        => _pendingInteractive.TryRemove(conversationId, out _);

    public async Task<StopOutcome> RequestStopAsync(string conversationId)
    {
        var run = _runs.GetActiveRun(conversationId);
        if (run is null || run.IsTerminal) return StopOutcome.NotRunning;

        var connection = _connections.Get(conversationId);
        if (connection is not { IsConnected: true })
        {
            // No live socket to negotiate over: nothing cooperative is possible, and leaving the
            // user staring at a spinner they cannot dismiss is worse than abandoning the run.
            await ForceStopAsync(conversationId).ConfigureAwait(false);
            return StopOutcome.ForceStopped;
        }

        _pendingInteractive.TryRemove(conversationId, out var pending);

        // Tier 1 — the agent is parked at an approval gate. Rejecting hard is the one and only
        // thing in this protocol that halts the loop: it sets stop_signal, the rest of the tool
        // batch is dropped, and the turn ends properly with an OUTPUT.
        if (pending == "approval")
        {
            await connection.RespondApprovalAsync(
                approved: false,
                scope: "once",
                rejectMode: ApprovalRejectModes.Hard,
                feedback: "Stopped by the user.").ConfigureAwait(false);
            RecordInteractiveAnswer(conversationId, "Stopped by user", EventStatus.Error);
            PublishStopEvent(conversationId, stopped: true);
            return StopOutcome.Stopped;
        }

        // Tier 2 — the agent is blocked on a question or a plan review. We cannot halt it, but we
        // can unblock it with an answer that says to stop; anything else (a runtime input) would
        // sit in a queue the agent isn't reading while it waits on io.receive().
        if (pending == "ask_user")
        {
            await connection.RespondAskUserAsync(StopPrompt).ConfigureAwait(false);
            RecordInteractiveAnswer(conversationId, "Stop requested", EventStatus.Error);
            PublishStopEvent(conversationId, stopped: false);
            return StopOutcome.StopRequested;
        }
        if (pending == "plan_review")
        {
            await connection.RespondPlanReviewAsync(StopPrompt).ConfigureAwait(false);
            RecordInteractiveAnswer(conversationId, "Stop requested", EventStatus.Error);
            PublishStopEvent(conversationId, stopped: false);
            return StopOutcome.StopRequested;
        }

        // Tier 3 — the agent is mid-LLM-call or mid-tool. Nothing can preempt it, so all that is
        // left is to ask, as a runtime input it will read at its next iteration boundary.
        await connection.SendRuntimeInputAsync(StopPrompt).ConfigureAwait(false);
        PublishStopEvent(conversationId, stopped: false);
        return StopOutcome.StopRequested;
    }

    public async Task ForceStopAsync(string conversationId)
    {
        var run = _runs.GetActiveRun(conversationId);
        if (run is null || run.IsTerminal) return;

        // Buffered before the cancel, because a terminal run stops accepting events — and this
        // notice is the whole point: the turn is gone from *this* app, not from the host.
        _runs.PublishLocalEvent(conversationId, new AgentStreamEvent(
            "stop_forced",
            "Force-stopped",
            null,
            """
            {"type":"stop_forced","detail":"Stopped locally. The agent may still be running on the host — the protocol has no way to interrupt it. Your next message could be delivered to that turn as a follow-up."}
            """));

        // The socket is deliberately left open: it is how we can still ask the host whether the
        // agent finished (GetServerSessionStatusAsync), and how a follow-up would reach it.
        _orphanedRuns[conversationId] = 0;
        _pendingInteractive.TryRemove(conversationId, out _);
        await _runs.CancelRunAsync(conversationId, run.RunId).ConfigureAwait(false);
    }

    public bool MayHaveOrphanedRun(string conversationId) => _orphanedRuns.ContainsKey(conversationId);

    public async Task<string> GetServerSessionStatusAsync(string conversationId)
    {
        var connection = _connections.Get(conversationId);
        if (connection is not { IsConnected: true }) return SessionStatuses.NotFound;

        var status = await connection.QuerySessionStatusAsync(conversationId).ConfigureAwait(false);
        if (status != SessionStatuses.Running) _orphanedRuns.TryRemove(conversationId, out _);
        return status;
    }

    /// <summary>Records the stop in the turn itself, so it survives into history and shows up in
    /// every window — not just the one whose button was clicked.</summary>
    private void PublishStopEvent(string conversationId, bool stopped)
        => _runs.PublishLocalEvent(conversationId, stopped
            ? new AgentStreamEvent("stop_requested", "Stopped", null,
                """{"type":"stop_requested","stopped":true}""")
            : new AgentStreamEvent("stop_requested", "Stop requested", null,
                """{"type":"stop_requested","stopped":false}"""));

    // ---- Mode ----

    public Task SetModeAsync(string conversationId, string mode)
    {
        var connection = _connections.Get(conversationId);
        if (connection is null) return Task.CompletedTask;

        // Remembered for the next CONNECT this socket makes (only a brand-new session keeps it —
        // the host's merge discards ours otherwise), and pushed now if a turn is live to receive
        // it. With no turn running the host would drop a mode_change on the floor, so we don't
        // send one: SendMessageAsync carries the mode with the INPUT instead.
        connection.InitialMode = mode;

        var run = _runs.GetActiveRun(conversationId);
        return run is { IsTerminal: false } ? connection.SetModeAsync(mode) : Task.CompletedTask;
    }

    private static void NotifyApproval(AgentConfig agent, string conversationId, ApprovalRequest r)
        => AppServices.Notifications.PublishApprovalRequired(
            agent.Id, agent.Name, conversationId, r.Tool, r.Description, r.ArgumentsJson);

    /// <summary>Releases everything tied to a deleted conversation: cancels its active run (if
    /// any), closes and drops its app-owned socket, and clears its pending interactive answers.
    /// Called when the user deletes a session or agent so a removed conversation leaves no live
    /// WebSocket or in-memory state behind.</summary>
    public async Task ReleaseConversationAsync(string conversationId)
    {
        var run = _runs.GetActiveRun(conversationId);
        if (run is not null)
        {
            try { await _runs.CancelRunAsync(conversationId, run.RunId).ConfigureAwait(false); }
            catch { /* best-effort: the socket is torn down next regardless */ }
        }
        _interactiveAnswers.TryRemove(conversationId, out _);
        _pendingInteractive.TryRemove(conversationId, out _);
        _orphanedRuns.TryRemove(conversationId, out _);
        await _connections.RemoveAsync(conversationId).ConfigureAwait(false);
    }

    /// <summary>Records that a run's history is on disk, bounded FIFO so the marker set never
    /// grows past <see cref="MaxPersistedRunMarkers"/> — each entry only matters for the brief
    /// window between the conversation write and the terminal snapshot.</summary>
    private void MarkHistoryPersisted(string runId)
    {
        if (!_historyPersistedRuns.TryAdd(runId, 0)) return;
        _historyPersistedOrder.Enqueue(runId);
        while (_historyPersistedOrder.Count > MaxPersistedRunMarkers
            && _historyPersistedOrder.TryDequeue(out var old))
        {
            _historyPersistedRuns.TryRemove(old, out _);
        }
    }

    /// <summary>Real app exit: stop new runs, cancel in-flight ones within a bounded window,
    /// then release the sockets. Never blocks forever.</summary>
    public async Task ShutdownAsync()
    {
        await _runs.ShutdownAsync(TimeSpan.FromSeconds(8)).ConfigureAwait(false);
        await _connections.DisposeAllAsync().ConfigureAwait(false);
    }

    // ---- IRunPersistence (invoked by the registry off the UI thread) ----

    public async Task PersistCompletedAsync(ConversationRunSnapshot snapshot, string finalReply)
    {
        var agentName = _agentsById.TryGetValue(snapshot.AgentId, out var a) ? a.Name : null;

        // The turn reached OUTPUT, so the host's session is idle again — whatever we feared was
        // orphaned by an earlier force-stop has demonstrably finished.
        _pendingInteractive.TryRemove(snapshot.ConversationId, out _);
        _orphanedRuns.TryRemove(snapshot.ConversationId, out _);

        var (window, nextId) = await LoadTurnWindowAsync(snapshot.ConversationId).ConfigureAwait(false);
        var target = new ListProjectionTarget(window, agentName, _http, nextId);
        var projection = new ChatTurnProjection(target);
        foreach (var e in snapshot.Events) projection.Apply(e);
        projection.AppendFinalReply(finalReply);
        projection.AppendTurnSummary();
        if (target.PendingImages.Count > 0)
        {
            try { await Task.WhenAll(target.PendingImages).ConfigureAwait(false); }
            catch { /* a single bad image must not fail the whole persist */ }
        }

        ResolveInteractiveCards(snapshot.ConversationId, window);

        await _conversations.UpsertMessagesAsync(
            snapshot.ConversationId, window.Where(m => !m.IsOnboarding).ToList()).ConfigureAwait(false);

        // Set this immediately after the conversation write (rather than after trace/execution
        // bookkeeping) because a newly created page can subscribe during that interval.
        MarkHistoryPersisted(snapshot.RunId);

        await PersistTraceAsync(snapshot).ConfigureAwait(false);
        await PersistUsageAsync(snapshot, agentName).ConfigureAwait(false);
        await _conversations.FinalizeExecutionAsync(
            snapshot.RunId, snapshot.ConversationId, finalReply, "done", projection.TurnDurationMs).ConfigureAwait(false);

        // A completed turn is a natural point to release sockets for other conversations that
        // have gone idle, so a long session never accumulates live WebSockets without bound.
        await _connections.TrimIdleAsync(cid => _runs.GetActiveRun(cid) is not null).ConfigureAwait(false);

        // Completion notification is now an app-level concern (fires even in the tray with no
        // page open); the coordinator suppresses it when a window already shows this chat.
        if (agentName is not null)
        {
            AppServices.Notifications.NotifyConnectionRestored(snapshot.AgentId);
            AppServices.Notifications.PublishAgentTurnCompleted(
                snapshot.AgentId, agentName, snapshot.ConversationId, snapshot.RunId,
                finalReply, projection.TurnToolCallsCount, projection.TurnDurationMs);
        }
    }

    public async Task PersistFailedAsync(ConversationRunSnapshot snapshot)
    {
        var agentName = _agentsById.TryGetValue(snapshot.AgentId, out var a) ? a.Name : null;
        _pendingInteractive.TryRemove(snapshot.ConversationId, out _);

        var (window, nextId) = await LoadTurnWindowAsync(snapshot.ConversationId).ConfigureAwait(false);
        var target = new ListProjectionTarget(window, agentName, _http, nextId);
        var projection = new ChatTurnProjection(target);
        foreach (var e in snapshot.Events) projection.Apply(e);
        projection.CompleteToolActivity(
            snapshot.Status == ConversationRunStatus.Cancelled ? ToolActivityStatus.Cancelled : ToolActivityStatus.Failed,
            snapshot.ErrorMessage);
        if (target.PendingImages.Count > 0)
        {
            try { await Task.WhenAll(target.PendingImages).ConfigureAwait(false); } catch { /* best-effort */ }
        }

        // Keep whatever was generated, and surface the error inline (mirrors the old
        // "[connection error] …" bubble) so the failed turn is visible in history.
        if (snapshot.Status == ConversationRunStatus.Failed)
        {
            target.Add(new ChatMessage
            {
                Id = target.NextId(),
                Role = ChatRole.Agent,
                AgentName = agentName,
                Content = $"[connection error] {snapshot.ErrorMessage}",
            });
        }

        ResolveInteractiveCards(snapshot.ConversationId, window);

        await _conversations.UpsertMessagesAsync(
            snapshot.ConversationId, window.Where(m => !m.IsOnboarding).ToList()).ConfigureAwait(false);
        MarkHistoryPersisted(snapshot.RunId);

        // A failed or cancelled turn still burned tokens on every LLM call it managed to make, so
        // it is recorded exactly like a successful one. A ledger that only counts successes does
        // not add up — and the turns that fail late are often the expensive ones.
        await PersistUsageAsync(snapshot, agentName).ConfigureAwait(false);

        await _conversations.FinalizeExecutionAsync(
            snapshot.RunId, snapshot.ConversationId, snapshot.ErrorMessage ?? "", "error", 0).ConfigureAwait(false);

        await _connections.TrimIdleAsync(cid => _runs.GetActiveRun(cid) is not null).ConfigureAwait(false);
    }

    /// <summary>
    /// The slice of the conversation a finished turn's projection actually needs, plus the id its
    /// first new bubble takes. The projection only ever appends bubbles or edits ones it created
    /// itself, with one exception: an <c>agent_image</c> event hangs its attachment on the last
    /// agent bubble, and the final reply is deduped against that same bubble. So seeding the list
    /// with that one row reproduces the old full-history behaviour exactly, while the persist that
    /// follows writes only the rows in this window — a turn no longer costs a full read of the
    /// conversation and a full rewrite of every row in it.
    /// </summary>
    private async Task<(List<ChatMessage> Window, long NextId)> LoadTurnWindowAsync(string conversationId)
    {
        var nextId = await _conversations.GetNextMessageIdAsync(conversationId).ConfigureAwait(false);
        var lastAgent = await _conversations.LoadLastAgentMessageAsync(conversationId).ConfigureAwait(false);

        var window = new List<ChatMessage>();
        if (lastAgent is not null) window.Add(lastAgent);
        return (window, nextId);
    }

    /// <summary>
    /// Appends this run's token spend to the usage ledger. Runs on the app-level persist path, so a
    /// turn that completed with no page open (or with the window in the tray) is still accounted
    /// for — same reason the bubbles are written here rather than by a page.
    /// </summary>
    private Task PersistUsageAsync(ConversationRunSnapshot snapshot, string? agentName)
    {
        var rows = UsageProjector.Extract(snapshot, agentName);
        return rows.Count == 0 ? Task.CompletedTask : _usage.InsertAsync(rows);
    }

    private Task PersistTraceAsync(ConversationRunSnapshot snapshot)
    {
        var rows = new List<ConversationRepository.TraceEventRow>(snapshot.Events.Count);
        foreach (var e in snapshot.Events)
        {
            rows.Add(new ConversationRepository.TraceEventRow(
                EventId: e.EventId ?? Guid.NewGuid().ToString(),
                ExecutionId: snapshot.RunId,
                SessionId: snapshot.ConversationId,
                Type: e.Type,
                PayloadJson: e.RawJson,
                Ts: TryParseTs(e.RawJson)));
        }
        return _conversations.InsertTraceEventsAsync(snapshot.ConversationId, rows);
    }

    private static double? TryParseTs(string rawJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("ts", out var v) && v.ValueKind == JsonValueKind.Number)
                return v.GetDouble();
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Stamps the user's recorded interactive answers onto the cards this run just re-projected
    /// from the (reply-less) event stream, in creation order, then seals any that were never
    /// answered as <see cref="NoSelectionMeta"/>. Only freshly projected cards are touched —
    /// a card is "unresolved" iff it is still <see cref="EventStatus.Running"/> with no
    /// <c>EventMeta</c>, which cleanly skips interactive turns resolved in earlier turns of the
    /// same conversation (they already carry their own meta/status). This is what makes the
    /// chosen result — or "no selection" — survive into reloaded history.
    /// </summary>
    private void ResolveInteractiveCards(string conversationId, IReadOnlyList<ChatMessage> history)
    {
        _interactiveAnswers.TryRemove(conversationId, out var answers);

        foreach (var m in history)
        {
            if (m.Role != ChatRole.Event
                || m.Status != EventStatus.Running
                || m.EventMeta is not null
                || m.EventKind is not ("ask_user" or "approval" or "plan_review"))
            {
                continue;
            }

            if (answers is not null && answers.TryDequeue(out var answer))
            {
                m.EventMeta = answer.Meta;
                m.Status = answer.Status;
            }
            else
            {
                m.EventMeta = NoSelectionMeta;
                m.Status = EventStatus.Done;
            }
        }
    }

    /// <summary>Projection target backed by a plain list, used by the headless persistence
    /// path. Agent-image resolution is collected as awaitable tasks so the caller can await
    /// them before saving (the cache path must be known when the row is written).</summary>
    private sealed class ListProjectionTarget : IChatProjectionTarget
    {
        private readonly List<ChatMessage> _messages;
        private readonly HttpClient _http;
        private long _nextId;
        public readonly List<Task> PendingImages = new();

        /// <param name="nextId">The id for the first bubble this turn adds. Passed in (rather than
        /// derived from <paramref name="messages"/>) because the list is a window onto the tail of
        /// the conversation, not the whole of it — the max id lives in the database.</param>
        public ListProjectionTarget(List<ChatMessage> messages, string? agentName, HttpClient http, long nextId)
        {
            _messages = messages;
            AgentName = agentName;
            _http = http;
            _nextId = nextId;
        }

        public IList<ChatMessage> Messages => _messages;
        public string? AgentName { get; }
        public long NextId() => _nextId++;
        public void Add(ChatMessage message) => _messages.Add(message);

        public void ResolveAgentImage(string dataUrl, ChatAttachment attachment)
            => PendingImages.Add(ResolveAsync(dataUrl, attachment));

        private async Task ResolveAsync(string dataUrl, ChatAttachment attachment)
        {
            var resolved = await AttachmentImageCacheService.ResolveAndCacheAsync(dataUrl, _http).ConfigureAwait(false);
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
            }
        }
    }

    private sealed class DebugRunLogger : IRunLogger
    {
        public void Log(ConversationRunSnapshot s, string phase, TimeSpan elapsed) => Debug.WriteLine(
            $"[run] {phase} run={s.RunId[..Math.Min(8, s.RunId.Length)]} conv={s.ConversationId} " +
            $"agent={s.AgentId} status={s.Status} seq={s.Sequence} elapsed={elapsed.TotalMilliseconds:0}ms" +
            (s.ErrorMessage is null ? "" : $" error={s.ErrorMessage}"));
    }
}
