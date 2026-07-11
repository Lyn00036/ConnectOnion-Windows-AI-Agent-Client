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
    private readonly AgentConnectionRegistry _connections;
    private readonly ConversationRunRegistry _runs;
    private readonly ConcurrentDictionary<string, AgentConfig> _agentsById = new();
    // The registry intentionally persists a reply before publishing its terminal state. Keep
    // that short transition observable so a page opened in the gap does not replay the same
    // stream events onto the history it just loaded from the database.
    private readonly ConcurrentDictionary<string, byte> _historyPersistedRuns = new();

    public AgentSessionManager(HttpClient http, ConversationRepository conversations)
    {
        _http = http;
        _conversations = conversations;
        _connections = new AgentConnectionRegistry(http);
        _runs = new ConversationRunRegistry(this, new DebugRunLogger(), onError: ex =>
            Debug.WriteLine($"[run] error: {ex}"));
    }

    /// <summary>The app-owned sockets, exposed so the view model can answer interactive turns
    /// and trigger explicit reconnects on the same connection the run uses.</summary>
    public AgentConnectionRegistry Connections => _connections;

    // ---- IAgentSessionManager ----

    public async Task<SendMessageResult> SendMessageAsync(
        AgentConfig agent, string conversationId, string prompt,
        IReadOnlyList<PendingAttachment>? attachments = null)
    {
        _agentsById[agent.Id] = agent;

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
        // loads it from the DB and then replays the run's events on top.
        var history = (await _conversations.LoadMessagesAsync(conversationId).ConfigureAwait(false)).ToList();
        var userMessage = new ChatMessage
        {
            Id = NextLongId(history),
            Role = ChatRole.User,
            Content = prompt,
        };
        foreach (var attachment in userAttachments) userMessage.Attachments.Add(attachment);
        history.Add(userMessage);
        await _conversations.SaveMessagesAsync(
            conversationId, history.Where(m => !m.IsOnboarding).ToList()).ConfigureAwait(false);

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
            SessionId: conversationId);

        await _conversations.InsertExecutionAsync(runId, conversationId, null, prompt, "running").ConfigureAwait(false);

        var connection = _connections.GetOrCreate(conversationId, agent);
        _runs.StartRun(request, new AgentTurnExecutor(connection, r => NotifyApproval(agent, conversationId, r)));

        return new SendMessageResult(runId, request.UserMessageId, request.AssistantMessageId);
    }

    public ConversationRunSnapshot? GetActiveRun(string conversationId) => _runs.GetActiveRun(conversationId);

    public bool IsRunHistoryPersisted(string runId) => _historyPersistedRuns.ContainsKey(runId);

    public IReadOnlyList<ConversationRunSnapshot> GetActiveRuns() => _runs.GetActiveRuns();

    public IDisposable Subscribe(string conversationId, Action<ConversationRunSnapshot> handler)
        => _runs.Subscribe(conversationId, handler);

    public Task CancelRunAsync(string conversationId, string runId) => _runs.CancelRunAsync(conversationId, runId);

    public ConversationRunSnapshot? RetryRun(AgentConfig agent, string conversationId)
    {
        _agentsById[agent.Id] = agent;
        var connection = _connections.GetOrCreate(conversationId, agent);
        return _runs.RetryRun(conversationId, new AgentTurnExecutor(connection, r => NotifyApproval(agent, conversationId, r)));
    }

    private static void NotifyApproval(AgentConfig agent, string conversationId, ApprovalRequest r)
        => AppServices.Notifications.PublishApprovalRequired(
            agent.Id, agent.Name, conversationId, r.Tool, r.Description, r.ArgumentsJson);

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

        var history = (await _conversations.LoadMessagesAsync(snapshot.ConversationId).ConfigureAwait(false)).ToList();
        var target = new ListProjectionTarget(history, agentName, _http);
        var projection = new ChatTurnProjection(target);
        foreach (var e in snapshot.Events) projection.Apply(e);
        projection.AppendFinalReply(finalReply);
        projection.AppendTurnSummary();
        if (target.PendingImages.Count > 0)
        {
            try { await Task.WhenAll(target.PendingImages).ConfigureAwait(false); }
            catch { /* a single bad image must not fail the whole persist */ }
        }

        await _conversations.SaveMessagesAsync(
            snapshot.ConversationId, history.Where(m => !m.IsOnboarding).ToList()).ConfigureAwait(false);

        // Set this immediately after the conversation write (rather than after trace/execution
        // bookkeeping) because a newly created page can subscribe during that interval.
        _historyPersistedRuns.TryAdd(snapshot.RunId, 0);

        await PersistTraceAsync(snapshot).ConfigureAwait(false);
        await _conversations.FinalizeExecutionAsync(
            snapshot.RunId, snapshot.ConversationId, finalReply, "done", projection.TurnDurationMs).ConfigureAwait(false);

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

        var history = (await _conversations.LoadMessagesAsync(snapshot.ConversationId).ConfigureAwait(false)).ToList();
        var target = new ListProjectionTarget(history, agentName, _http);
        var projection = new ChatTurnProjection(target);
        foreach (var e in snapshot.Events) projection.Apply(e);
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

        await _conversations.SaveMessagesAsync(
            snapshot.ConversationId, history.Where(m => !m.IsOnboarding).ToList()).ConfigureAwait(false);
        _historyPersistedRuns.TryAdd(snapshot.RunId, 0);
        await _conversations.FinalizeExecutionAsync(
            snapshot.RunId, snapshot.ConversationId, snapshot.ErrorMessage ?? "", "error", 0).ConfigureAwait(false);
    }

    private async Task PersistTraceAsync(ConversationRunSnapshot snapshot)
    {
        foreach (var e in snapshot.Events)
        {
            await _conversations.InsertTraceEventAsync(
                e.EventId ?? Guid.NewGuid().ToString(), snapshot.ConversationId, snapshot.RunId,
                snapshot.ConversationId, e.Type, e.RawJson, TryParseTs(e.RawJson)).ConfigureAwait(false);
        }
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

    private static long NextLongId(IReadOnlyList<ChatMessage> messages)
        => messages.Count == 0 ? 1 : messages.Max(m => m.Id) + 1;

    /// <summary>Projection target backed by a plain list, used by the headless persistence
    /// path. Agent-image resolution is collected as awaitable tasks so the caller can await
    /// them before saving (the cache path must be known when the row is written).</summary>
    private sealed class ListProjectionTarget : IChatProjectionTarget
    {
        private readonly List<ChatMessage> _messages;
        private readonly HttpClient _http;
        public readonly List<Task> PendingImages = new();

        public ListProjectionTarget(List<ChatMessage> messages, string? agentName, HttpClient http)
        {
            _messages = messages;
            AgentName = agentName;
            _http = http;
        }

        public IList<ChatMessage> Messages => _messages;
        public string? AgentName { get; }
        public long NextId() => _messages.Count == 0 ? 1 : _messages.Max(m => m.Id) + 1;
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
