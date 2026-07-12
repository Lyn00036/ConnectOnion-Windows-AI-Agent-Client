using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ConnectOnion.Protocol;
using ConnectOnion.Protocol.Runtime;
using ConnectOnion.WinUIClient.Common;
using ConnectOnion.WinUIClient.Models;
using ConnectOnion.WinUIClient.Services;
using ConnectOnion.WinUIClient.Services.Runtime;
using ConnectOnion.WinUIClient.Data;
using Microsoft.UI.Dispatching;

namespace ConnectOnion.WinUIClient.ViewModels;

/// <summary>
/// Backs the Chat page. It no longer owns the agent connection or the background turn —
/// those live in the app-level <see cref="AgentSessionManager"/>. This view model only:
/// loads persisted history, submits sends to the manager, subscribes to the conversation's
/// run, and projects the run's stream into bubbles for <b>this</b> page's UI thread. A
/// page created mid-turn therefore resumes the live stream instantly, and closing the page
/// (see <see cref="Cleanup"/>) detaches the subscription without touching the run.
/// </summary>
public sealed partial class ChatViewModel : PresenceAwareViewModel, IChatProjectionTarget
{
    private static readonly Regex GenericTitle = new(@"^Conversation \d+$", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();

    private AgentConfig? _agent;
    private SessionSummary? _session;
    private SessionsState _sessionsState = new();
    private long _createdAt;

    // ---- Live run subscription (this page's view of the app-level run) ----
    private IDisposable? _runSubscription;
    private ChatTurnProjection? _liveProjection;
    private string? _liveRunId;
    private int _appliedEventCount;
    // Set only when this page restored a history that already contains the current run's
    // projection. The run can still be non-terminal for a few milliseconds while the registry
    // finishes bookkeeping, so replaying its buffered events here would duplicate tool and
    // interactive cards.
    private string? _historyLoadedRunId;

    public ObservableCollection<ChatMessage> Messages { get; } = new();

    public ChatViewModel()
    {
        RetryCommand = new RelayCommand(Retry, () => CanRetry);
    }

    /// <summary>Re-runs the last failed turn. Bound to the failure bar's Retry button.</summary>
    public RelayCommand RetryCommand { get; }

    protected override AgentConfig? PresenceAgent => _agent;

    /// <summary>Detaches this page's run subscription and presence hook. Never cancels the
    /// run or closes the shared connection — the turn keeps going and persists on its own.</summary>
    public void Cleanup()
    {
        DetachPresence();
        _runSubscription?.Dispose();
        _runSubscription = null;
        _liveProjection = null;
        _liveRunId = null;
        _appliedEventCount = 0;
        _historyLoadedRunId = null;
    }

    private bool _hasAgent;
    public bool HasAgent
    {
        get => _hasAgent;
        private set
        {
            if (!SetProperty(ref _hasAgent, value)) return;
            OnPropertyChanged(nameof(CanSend));
            OnPropertyChanged(nameof(CanReconnect));
        }
    }

    private bool _isProcessing;
    public bool IsProcessing
    {
        get => _isProcessing;
        private set
        {
            if (!SetProperty(ref _isProcessing, value)) return;
            OnPropertyChanged(nameof(CanSend));
            OnPropertyChanged(nameof(CanReconnect));
        }
    }

    private bool _isConnecting;
    public bool IsConnecting
    {
        get => _isConnecting;
        private set
        {
            if (!SetProperty(ref _isConnecting, value)) return;
            OnPropertyChanged(nameof(CanSend));
            OnPropertyChanged(nameof(CanReconnect));
        }
    }

    // Sending is gated on the agent being confirmed online — matching the "green dot means
    // you can chat" model — not on holding a live socket (the transport connects lazily).
    public bool CanSend => HasAgent && IsOnline && !IsProcessing && !IsConnecting;
    public bool CanReconnect => HasAgent && !IsProcessing && !IsConnecting && Presence != AgentPresence.Checking;

    private bool _canRetry;
    /// <summary>True when the conversation's last run failed and is still retryable (only within
    /// the same app session — a restart clears the registry's retryable state). Drives the
    /// failure bar and <see cref="RetryCommand"/>.</summary>
    public bool CanRetry
    {
        get => _canRetry;
        private set
        {
            if (!SetProperty(ref _canRetry, value)) return;
            RetryCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(ShowRetryBar));
        }
    }

    /// <summary>The failure/Retry bar is kept mutually exclusive with the offline bar so the two
    /// never stack in the same bottom-center slot; when the agent is offline, Recheck is the
    /// right action and retrying would just fail again.</summary>
    public bool ShowRetryBar => CanRetry && !ShowOfflineNotice;

    public string SelectedAgentName => _agent?.Name ?? "Agent";
    public string SelectedAgentInitial => string.IsNullOrWhiteSpace(_agent?.Name) ? "?" : _agent!.Name[..1].ToUpperInvariant();
    public string SessionShortId => _session is null ? "" : _session.Id[..Math.Min(8, _session.Id.Length)];
    public string? SessionId => _session?.Id;
    public string? AgentId => _agent?.Id;

    private AgentAcceptedInputs? _acceptedInputs;
    public AgentAcceptedInputs? AcceptedInputs
    {
        get => _acceptedInputs;
        private set => SetProperty(ref _acceptedInputs, value);
    }

    private string _headerText = "Chat";
    public string HeaderText { get => _headerText; private set => SetProperty(ref _headerText, value); }

    private string _statusText = "";
    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (!SetProperty(ref _statusText, value)) return;
            OnPropertyChanged(nameof(HasStatusText));
        }
    }

    public bool HasStatusText => !string.IsNullOrWhiteSpace(StatusText);

    private double _messageFontSize = 14;
    public double MessageFontSize { get => _messageFontSize; private set => SetProperty(ref _messageFontSize, value); }

    private bool _enterToSend = true;
    public bool EnterToSend { get => _enterToSend; private set => SetProperty(ref _enterToSend, value); }

    public async Task LoadAsync()
    {
        var prefs = await AppServices.Preferences.LoadAsync();
        EnterToSend = prefs.EnterToSend;
        MessageFontSize = prefs.MessageFontSize switch
        {
            Models.MessageFontSize.Sm => 12,
            Models.MessageFontSize.Lg => 18,
            _ => 14,
        };

        var agents = await AppServices.Agents.LoadAsync();
        _agent = agents.Agents.FirstOrDefault(a => a.Id == agents.SelectedAgentId);
        HasAgent = _agent is not null;
        OnPropertyChanged(nameof(CanSend));

        if (_agent is null)
        {
            HeaderText = "Select an agent to start chatting";
            RaiseHeaderProperties();
            return;
        }

        _session = await ResolveSessionAsync(_agent.Id);
        HeaderText = $"{_agent.Name} - {_session.Title}";
        RaiseHeaderProperties();

        await RestoreConversationAsync();

        // Attach to the app-level run for this conversation. The manager fires the current
        // snapshot immediately, so an in-flight turn is resumed (streaming continues) the
        // moment the page opens — the core "switch away and come back" fix.
        _runSubscription = AppServices.RunManager.Subscribe(
            _session.Id, snapshot => _dispatcher.TryEnqueue(() => ApplyRunSnapshot(snapshot)));

        _ = ProbePresenceOnOpenAsync();
        _ = RefreshAcceptedInputsAsync();
    }

    private async Task RefreshAcceptedInputsAsync()
    {
        if (_agent is null) return;

        string? infoJson;
        try
        {
            infoJson = await AgentInfoService.FetchAndPersistAsync(_agent).ConfigureAwait(false);
        }
        catch
        {
            return;
        }
        if (infoJson is null) return;

        var parsed = EndpointResolver.ParseAcceptedInputsFromInfoJson(infoJson);
        _dispatcher.TryEnqueue(() => AcceptedInputs = parsed);
    }

    /// <summary>
    /// Reuses the active session if it belongs to this agent, else the agent's most recent
    /// conversation, else creates a fresh one. Port of the store's <c>ensureSession</c>.
    /// </summary>
    private async Task<SessionSummary> ResolveSessionAsync(string agentId)
    {
        _sessionsState = await AppServices.Sessions.LoadAsync();

        var active = _sessionsState.Sessions
            .FirstOrDefault(s => s.Id == _sessionsState.ActiveSessionId && s.AgentId == agentId);
        if (active is not null) return active;

        var session = SessionSummary.NewConversation(agentId, _sessionsState.Sessions);
        _sessionsState.Sessions.Add(session);
        _sessionsState.ActiveSessionId = session.Id;
        await AppServices.Sessions.SaveAsync(_sessionsState);
        return session;
    }

    private async Task RestoreConversationAsync()
    {
        if (_agent is null || _session is null) return;

        // A conversation with an active/just-finished run must load from the DB (the manager
        // persists the user message and, before Completed, the whole reply) so the replayed
        // run events land on top of the authoritative history — never a stale cache.
        var run = AppServices.RunManager.GetActiveRun(_session.Id);
        var hasRun = run is not null;

        if (!hasRun && ConversationCache.Get(_session.Id, _agent.Id) is { } cached)
        {
            Messages.Clear();
            foreach (var message in cached.Messages) Messages.Add(message);
            _createdAt = cached.CreatedAt;
            ResetIdCounter();
            return;
        }

        Messages.Clear();
        var messages = ToolActivityMigration.UpgradeLegacyToolMessages(
            await AppServices.Conversations.LoadMessagesAsync(_session.Id));
        _createdAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        if (messages.Count > 0)
        {
            foreach (var message in messages) Messages.Add(message);
            _createdAt = messages[0].Id > 0 ? messages[0].Id : _createdAt;
        }
        ResetIdCounter();

        // A completed turn is saved before its terminal snapshot is broadcast. If the page is
        // opened in that tiny interval, the DB already contains every projected tool/event card
        // while the snapshot still says Running. Remember that fact and skip its replay below.
        // Re-read the snapshot because persistence may have completed while LoadMessagesAsync
        // was in flight.
        run = AppServices.RunManager.GetActiveRun(_session.Id);
        _historyLoadedRunId = run is { IsTerminal: false }
            && AppServices.RunManager.IsRunHistoryPersisted(run.RunId)
                ? run.RunId
                : null;

        if (!hasRun) StoreSessionCache();
    }

    public async Task SendAsync(string text, IReadOnlyList<PendingAttachment>? attachments = null)
    {
        text = text?.Trim() ?? "";
        var hasAttachments = attachments is { Count: > 0 };
        if ((text.Length == 0 && !hasAttachments) || _agent is null || _session is null || IsProcessing) return;

        // A new send supersedes any prior failure — the old attempt is no longer retryable.
        CanRetry = false;

        // Optimistic user bubble for instant feedback. The manager also persists an
        // equivalent user message (this page won't reload during the session, so there is
        // no double; a re-opened page loads the persisted one).
        var userMessage = new ChatMessage { Id = NextId(), Role = ChatRole.User, Content = text };
        if (hasAttachments)
        {
            foreach (var attachment in attachments!)
            {
                userMessage.Attachments.Add(new ChatAttachment
                {
                    Kind = attachment.Kind,
                    FileName = attachment.FileName,
                    MimeType = attachment.MimeType,
                    SizeBytes = attachment.SizeBytes,
                    LocalCachePath = attachment.LocalPath,
                    Status = AttachmentStatus.Sent,
                });
            }
        }
        Messages.Add(userMessage);
        TouchSession(text);

        IsProcessing = true;
        StatusText = "Connecting...";
        try
        {
            // Hand off to the app-level manager. It persists the user message, opens/reuses
            // the connection, and starts the run; the reply arrives back through our
            // subscription (ApplyRunSnapshot), not by awaiting here.
            await AppServices.RunManager.SendMessageAsync(_agent, _session.Id, text, attachments);
        }
        catch (InvalidOperationException)
        {
            // A run for this conversation is already active — ignore the duplicate send.
        }
        catch (Exception ex)
        {
            IsProcessing = false;
            StatusText = $"Send failed: {UserFacingError(ex)}";
        }
    }

    /// <summary>Applies a run snapshot to this page's message list on the UI thread. Replays
    /// any events not yet projected (so a resubscribing page catches up), updates busy/status,
    /// and finalizes on terminal states — all idempotent per <see cref="ConversationRunSnapshot.Sequence"/>.</summary>
    private void ApplyRunSnapshot(ConversationRunSnapshot snapshot)
    {
        if (snapshot.RunId == _historyLoadedRunId)
        {
            // The DB restore already contains this run's events, final reply, and summary.
            // We still mirror its busy/status state until the terminal notification arrives.
            IsProcessing = !snapshot.IsTerminal;
            StatusText = StatusTextFor(snapshot);
            if (snapshot.IsTerminal)
            {
                CanRetry = snapshot.Status == ConversationRunStatus.Failed;
                _historyLoadedRunId = null;
                StoreSessionCache();
            }
            return;
        }

        if (snapshot.RunId != _liveRunId)
        {
            if (snapshot.IsTerminal)
            {
                // The run finished before this page attached; the DB load already reflects
                // the whole turn, so just clear the busy state — never re-append it. A failed
                // run stays retryable, so reopening a failed conversation still offers Retry.
                IsProcessing = false;
                StatusText = "";
                CanRetry = snapshot.Status == ConversationRunStatus.Failed;
                return;
            }

            // First time we see this (active) run — start a fresh projection over our list,
            // which already holds the persisted history + optimistic user message.
            _liveRunId = snapshot.RunId;
            _liveProjection = new ChatTurnProjection(this);
            _appliedEventCount = 0;
        }

        var events = snapshot.Events;
        for (var i = _appliedEventCount; i < events.Count; i++) _liveProjection!.Apply(events[i]);
        _appliedEventCount = events.Count;

        IsProcessing = !snapshot.IsTerminal;
        StatusText = StatusTextFor(snapshot);

        if (!snapshot.IsTerminal) return;

        // Terminal: mirror what the manager persisted so the live view matches the DB.
        if (snapshot.Status == ConversationRunStatus.Completed)
        {
            _liveProjection!.AppendFinalReply(snapshot.PartialContent);
            _liveProjection!.AppendTurnSummary();
            CanRetry = false;
            _ = PersistSessionMetadataAsync();
        }
        else if (snapshot.Status == ConversationRunStatus.Failed)
        {
            _liveProjection?.CompleteToolActivity(ToolActivityStatus.Failed, snapshot.ErrorMessage);
            Messages.Add(new ChatMessage
            {
                Id = NextId(),
                Role = ChatRole.Agent,
                AgentName = _agent?.Name,
                Content = $"[connection error] {snapshot.ErrorMessage}",
            });
            // The error is shown as a bubble plus the Retry bar; suppress the redundant
            // "Send failed" status banner set above so the two don't stack.
            StatusText = "";
            CanRetry = true;
            if (_agent is not null)
            {
                _ = PresenceService.RefreshAsync(_agent);
                AppServices.Notifications.NotifyConnectionLost(_agent.Id, _agent.Name);
            }
        }
        else if (snapshot.Status == ConversationRunStatus.Cancelled)
        {
            _liveProjection?.CompleteToolActivity(ToolActivityStatus.Cancelled);
            CanRetry = false;
        }

        _liveProjection = null;
        _liveRunId = null;
        _appliedEventCount = 0;
        _historyLoadedRunId = null;
        StoreSessionCache();
    }

    private static string StatusTextFor(ConversationRunSnapshot snapshot) => snapshot.Status switch
    {
        ConversationRunStatus.Queued => "Working...",
        ConversationRunStatus.Connecting => "Connecting...",
        ConversationRunStatus.Running => snapshot.Events.Count > 0
            ? snapshot.Events[^1].Description : "Working...",
        ConversationRunStatus.Failed => $"Send failed: {snapshot.ErrorMessage}",
        _ => "",
    };

    public async Task NewConversationAsync()
    {
        if (_agent is null || IsProcessing) return;

        // Re-point the subscription at the new conversation.
        _runSubscription?.Dispose();
        _runSubscription = null;
        _liveProjection = null;
        _liveRunId = null;
        _appliedEventCount = 0;
        _historyLoadedRunId = null;
        CanRetry = false;

        _session = SessionSummary.NewConversation(_agent.Id, _sessionsState.Sessions);
        _sessionsState.Sessions.Add(_session);
        _sessionsState.ActiveSessionId = _session.Id;
        await AppServices.Sessions.SaveAsync(_sessionsState);

        Messages.Clear();
        ResetIdCounter();
        _createdAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        HeaderText = $"{_agent.Name} - {_session.Title}";
        RaiseHeaderProperties();

        _runSubscription = AppServices.RunManager.Subscribe(
            _session.Id, snapshot => _dispatcher.TryEnqueue(() => ApplyRunSnapshot(snapshot)));
    }

    /// <summary>Forces the app-owned socket for this conversation to reconnect to its session.</summary>
    public async Task ReconnectAsync()
    {
        if (_agent is null || _session is null) return;

        var connection = AppServices.RunManager.Connections.GetOrCreate(_session.Id, _agent);

        IsConnecting = true;
        StatusText = "Reconnecting...";
        try
        {
            await connection.ReconnectAsync(_session.Id);
            StatusText = "Connected.";
        }
        catch (Exception ex)
        {
            StatusText = $"Reconnect failed: {UserFacingError(ex)}";
        }
        finally
        {
            IsConnecting = false;
        }
    }

    /// <summary>Re-runs the last failed turn: the app-level manager reuses the original user
    /// message but mints a fresh run id. The page's existing run subscription streams the new
    /// run's events back through <see cref="ApplyRunSnapshot"/>, so no extra wiring is needed
    /// here. Bound to <see cref="RetryCommand"/>.</summary>
    public void Retry()
    {
        if (_agent is null || _session is null || IsProcessing || !CanRetry) return;

        // Ask the manager first: if the run is no longer retryable (the registry cleared it, or
        // a restart lost it), keep the error bubble as history rather than dropping it silently.
        if (AppServices.RunManager.RetryRun(_agent, _session.Id) is null)
        {
            CanRetry = false;
            return;
        }

        CanRetry = false;
        RemoveLastFailureBubble();
        IsProcessing = true;
        StatusText = "Connecting...";
    }

    /// <summary>Drops the "[connection error]" bubble for the last failed turn (whether it was
    /// added live or restored from history) so a successful retry doesn't leave a stale error
    /// above the fresh reply.</summary>
    private void RemoveLastFailureBubble()
    {
        for (var i = Messages.Count - 1; i >= 0; i--)
        {
            var m = Messages[i];
            if (m.Role == ChatRole.Agent
                && m.Content is not null
                && m.Content.StartsWith("[connection error]", StringComparison.Ordinal))
            {
                Messages.RemoveAt(i);
                return;
            }
        }
    }

    // Ids are handed out from a counter rather than rescanned off Messages: the projection asks
    // for one per bubble, so a Max() scan would make a turn cost O(messages x events) on the UI
    // thread. The counter only ever moves forward, so a removed bubble just leaves a gap — ids
    // stay unique within the conversation, which is all the (conversation_id, id) key needs.
    private long _nextId = 1;

    private void ResetIdCounter()
    {
        long max = 0;
        foreach (var message in Messages)
        {
            if (message.Id > max) max = message.Id;
        }
        _nextId = max + 1;
    }

    private long NextId() => _nextId++;

    private void TouchSession(string prompt)
    {
        if (_session is null) return;

        if (GenericTitle.IsMatch(_session.Title) && !string.IsNullOrWhiteSpace(prompt))
        {
            var title = Whitespace.Replace(prompt.Trim(), " ");
            _session.Title = title.Length > 48 ? title[..48] : title;
        }
        _session.UpdatedAt = DateTime.UtcNow.ToString("o");
        HeaderText = $"{_agent!.Name} - {_session.Title}";
        RaiseHeaderProperties();
        // Only this one row changed — no need to reconcile the whole session index on every send.
        _ = AppServices.Sessions.UpdateSessionAsync(_session);
    }

    private void RaiseHeaderProperties()
    {
        OnPropertyChanged(nameof(SelectedAgentName));
        OnPropertyChanged(nameof(SelectedAgentInitial));
        OnPropertyChanged(nameof(SessionShortId));
    }

    protected override void OnPresenceRaised()
    {
        OnPropertyChanged(nameof(CanSend));
        OnPropertyChanged(nameof(CanReconnect));
        OnPropertyChanged(nameof(ShowRetryBar));
    }

    private static string UserFacingError(Exception ex)
        => ex is OperationCanceledException ? "Connection timed out." : ex.Message;

    private void StoreSessionCache()
    {
        if (_session is null || _agent is null) return;
        ConversationCache.Store(
            _session.Id,
            new ConversationCache.Entry(_agent, _session, Messages.ToList(), _createdAt));
    }

    /// <summary>Persists the remote session id + last processed event id so a later reconnect
    /// can skip already-seen events.</summary>
    private async Task PersistSessionMetadataAsync()
    {
        if (_agent is null || _session is null) return;
        var connection = AppServices.RunManager.Connections.Get(_session.Id);
        if (connection is null) return;

        _session.RemoteSessionId = connection.SessionId;
        _session.LastProcessedEventId = connection.LastProcessedEventId;
        _session.UpdatedAt = DateTime.UtcNow.ToString("o");
        await AppServices.Sessions.UpdateSessionAsync(_session);
    }

    // ---- IChatProjectionTarget (live rendering into this page's list) ----

    IList<ChatMessage> IChatProjectionTarget.Messages => Messages;
    string? IChatProjectionTarget.AgentName => _agent?.Name;
    long IChatProjectionTarget.NextId() => NextId();
    void IChatProjectionTarget.Add(ChatMessage message) => Messages.Add(message);
    void IChatProjectionTarget.ResolveAgentImage(string dataUrl, ChatAttachment attachment)
        => _ = ResolveAgentImageAsync(dataUrl, attachment);
}
