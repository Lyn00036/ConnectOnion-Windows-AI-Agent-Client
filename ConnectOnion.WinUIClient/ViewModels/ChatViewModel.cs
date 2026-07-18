using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ConnectOnion.Protocol;
using ConnectOnion.Protocol.Runtime;
using ConnectOnion.WinUIClient.Common;
using ConnectOnion.WinUIClient.Models;
using ConnectOnion.WinUIClient.Services;
using ConnectOnion.WinUIClient.Services.Attachments;
using ConnectOnion.WinUIClient.Services.Notifications;
using ConnectOnion.WinUIClient.Services.Runtime;
using ConnectOnion.WinUIClient.Data;
using Microsoft.Extensions.Logging;
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
    private static readonly Action<ILogger, string?, Exception?> LogSendFailure =
        LoggerMessage.Define<string?>(LogLevel.Error, new EventId(1, "ChatSendFailure"),
            "Send failed for session {SessionId}");
    private static readonly Regex GenericTitle = new(@"^Conversation \d+$", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();

    private AgentConfig? _agent;
    private SessionSummary? _session;
    private SessionsState _sessionsState = new();
    private long _createdAt;

    // Bumped by every LoadAsync. One view model now serves every conversation the page shows
    // (switching reuses the page), so two loads can be in flight at once; only the newest one is
    // allowed to touch the message list or subscribe. See LoadAsync.
    private long _loadGeneration;

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

    private readonly PreferencesRepository _preferences;
    private readonly AgentRepository _agents;
    private readonly SessionRepository _sessions;
    private readonly ConversationRepository _conversations;
    private readonly AgentSessionManager _runManager;
    private readonly NotificationCoordinator _notifications;
    private readonly HttpClient _http;
    private readonly ILogger<ChatViewModel> _logger;

    public ChatViewModel(
        AgentPresenceService presence,
        PreferencesRepository preferences,
        AgentRepository agents,
        SessionRepository sessions,
        ConversationRepository conversations,
        AgentSessionManager runManager,
        NotificationCoordinator notifications,
        HttpClient http,
        ILogger<ChatViewModel> logger)
        : base(presence)
    {
        _preferences = preferences;
        _agents = agents;
        _sessions = sessions;
        _conversations = conversations;
        _runManager = runManager;
        _notifications = notifications;
        _http = http;
        _logger = logger;

        // Partial-property [ObservableProperty] declarations can't carry initializers, so the
        // non-default seeds live here.
        HeaderText = "Chat";
        StatusText = "";
        MessageFontSize = 14;
        EnterToSend = true;
        CurrentMode = AgentModes.Safe;
    }

    protected override AgentConfig? PresenceAgent => _agent;

    /// <summary>Drops this page's live run subscription and per-run projection state, leaving the
    /// presence hook attached. Called when the cached, reused page is navigated away from: the
    /// turn keeps running and persists on its own, and the page re-subscribes via
    /// <see cref="LoadAsync"/> on return. Never cancels the run or closes the shared connection.</summary>
    public void DetachRun()
    {
        _runSubscription?.Dispose();
        _runSubscription = null;
        _liveProjection = null;
        _liveRunId = null;
        _appliedEventCount = 0;
        _historyLoadedRunId = null;
    }

    /// <summary>Full teardown: the run subscription plus the shared presence hook. For genuine
    /// disposal of the view model (the page instance is actually being discarded, not cached).</summary>
    public void Cleanup()
    {
        DetachPresence();
        DetachRun();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSend))]
    [NotifyPropertyChangedFor(nameof(CanReconnect))]
    public partial bool HasAgent { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSend))]
    [NotifyPropertyChangedFor(nameof(CanReconnect))]
    public partial bool IsProcessing { get; private set; }

    /// <summary>True only after INPUT reached the socket and before OUTPUT/ERROR.</summary>
    [ObservableProperty]
    public partial bool CanStop { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSend))]
    [NotifyPropertyChangedFor(nameof(CanReconnect))]
    public partial bool IsConnecting { get; private set; }

    // Sending is gated on the agent being confirmed online — matching the "green dot means
    // you can chat" model — not on holding a live socket (the transport connects lazily).
    public bool CanSend => HasAgent && _session is not null && IsOnline && !IsProcessing && !IsConnecting;
    public bool CanReconnect => HasAgent && !IsProcessing && !IsConnecting && Presence != AgentPresence.Checking;

    /// <summary>True when the conversation's last run failed and is still retryable (only within
    /// the same app session — a restart clears the registry's retryable state). Drives the
    /// failure bar and <see cref="RetryCommand"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowRetryBar))]
    [NotifyCanExecuteChangedFor(nameof(RetryCommand))]
    public partial bool CanRetry { get; private set; }

    /// <summary>The failure/Retry bar is kept mutually exclusive with the offline bar so the two
    /// never stack in the same bottom-center slot; when the agent is offline, Recheck is the
    /// right action and retrying would just fail again.</summary>
    public bool ShowRetryBar => CanRetry && !ShowOfflineNotice;

    public string SelectedAgentName => _agent?.Name ?? "Agent";

    public string SelectedAgentInitial => NameInitial.From(_agent?.Name);
    public string SessionShortId => _session is null ? "" : _session.Id[..Math.Min(8, _session.Id.Length)];
    public string? SessionId => _session?.Id;
    public string? AgentId => _agent?.Id;

    [ObservableProperty]
    public partial AgentAcceptedInputs? AcceptedInputs { get; private set; }

    [ObservableProperty]
    public partial string HeaderText { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusText))]
    public partial string StatusText { get; private set; }

    public bool HasStatusText => !string.IsNullOrWhiteSpace(StatusText);

    [ObservableProperty]
    public partial double MessageFontSize { get; private set; }

    [ObservableProperty]
    public partial bool EnterToSend { get; private set; }

    // ---- Approval mode ----

    /// <summary>The conversation's approval mode. Client-owned state (see <see cref="SessionSummary.Mode"/>):
    /// re-sent with every turn, and overwritten if the agent reports a different one — it can put
    /// itself into plan mode without being asked.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentModeLabel))]
    public partial string CurrentMode { get; private set; }

    public string CurrentModeLabel => AgentModes.DisplayName(CurrentMode);

    // ---- Stop ----

    /// <summary>A graceful stop was sent and the current iteration is still finishing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StopButtonTooltip))]
    public partial bool IsStopping { get; private set; }

    public string StopButtonTooltip => IsStopping ? "Stopping..." : "Stop";

    /// <summary>
    /// Loads (or re-loads) whichever conversation is currently active. Re-entrant: switching
    /// conversations drives this on the same view model rather than building a new page, so it
    /// first lets go of the previous conversation's run subscription and per-run state. The
    /// presence hook is deliberately *not* touched — this view model outlives the switch, and
    /// only <see cref="Cleanup"/> (page unload) detaches it.
    /// </summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        // Clicking through the sidebar fires one of these per click, on one shared view model, and
        // each awaits the database. Without a generation the loser of that race would still append
        // its history and subscribe its run — two subscriptions projecting into one list. A load
        // that has been superseded drops out at the next checkpoint instead.
        var generation = ++_loadGeneration;

        _runSubscription?.Dispose();
        _runSubscription = null;
        _liveProjection = null;
        _liveRunId = null;
        _appliedEventCount = 0;
        _historyLoadedRunId = null;
        CanRetry = false;
        IsProcessing = false;
        CanStop = false;
        IsStopping = false;
        StatusText = "";
        // Cleared here rather than only in RestoreConversationAsync, so the early returns below
        // (no agent, no session) can't leave the previous conversation's bubbles on screen.
        Messages.Clear();

        var prefs = await _preferences.LoadAsync(cancellationToken);
        EnterToSend = prefs.EnterToSend;
        MessageFontSize = prefs.MessageFontSize switch
        {
            Models.MessageFontSize.Sm => 12,
            Models.MessageFontSize.Lg => 18,
            _ => 14,
        };

        var agents = await _agents.LoadAsync(cancellationToken);
        if (generation != _loadGeneration) return;

        _agent = agents.Agents.FirstOrDefault(a => a.Id == agents.SelectedAgentId);
        HasAgent = _agent is not null;
        OnPropertyChanged(nameof(CanSend));

        if (_agent is null)
        {
            HeaderText = "Select an agent to start chatting";
            RaiseHeaderProperties();
            return;
        }

        _session = await ResolveSessionAsync(_agent.Id, cancellationToken);
        if (generation != _loadGeneration) return;

        OnPropertyChanged(nameof(CanSend));
        if (_session is null)
        {
            HeaderText = $"{_agent.Name} - No conversation selected";
            RaiseHeaderProperties();
            return;
        }

        HeaderText = $"{_agent.Name} - {_session.Title}";
        CurrentMode = _session.Mode;
        RaiseHeaderProperties();

        await RestoreConversationAsync(generation, cancellationToken);
        if (generation != _loadGeneration) return;

        // Attach to the app-level run for this conversation. The manager fires the current
        // snapshot immediately, so an in-flight turn is resumed (streaming continues) the
        // moment the page opens — the core "switch away and come back" fix.
        _runSubscription = _runManager.Subscribe(
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
    /// Reuses the active session if it belongs to this agent, otherwise the agent's most recent
    /// conversation. Session creation is reserved for explicit user actions such as New chat;
    /// loading a page must never recreate a conversation that the user just deleted.
    /// </summary>
    private async Task<SessionSummary?> ResolveSessionAsync(
        string agentId,
        CancellationToken cancellationToken)
    {
        _sessionsState = await _sessions.LoadAsync(cancellationToken);
        var session = SessionSelection.FindExisting(
            _sessionsState.Sessions, _sessionsState.ActiveSessionId, agentId);
        if (session is null || session.Id == _sessionsState.ActiveSessionId)
        {
            return session;
        }

        _sessionsState.ActiveSessionId = session.Id;
        await _sessions.SaveAsync(_sessionsState);
        return session;
    }

    private async Task RestoreConversationAsync(long generation, CancellationToken cancellationToken)
    {
        if (_agent is null || _session is null) return;

        // A conversation with an active/just-finished run must load from the DB (the manager
        // persists the user message and, before Completed, the whole reply) so the replayed
        // run events land on top of the authoritative history — never a stale cache.
        var run = _runManager.GetActiveRun(_session.Id);
        var hasRun = run is not null;

        if (!hasRun && ConversationCache.Get(_session.Id, _agent.Id) is { } cached)
        {
            Messages.Clear();
            foreach (var message in cached.Messages) Messages.Add(message);
            _createdAt = cached.CreatedAt;
            ResetIdCounter();
            return;
        }

        var messages = ToolActivityMigration.UpgradeLegacyToolMessages(
            await _conversations.LoadMessagesAsync(_session.Id, cancellationToken));
        // A newer switch already owns the list — dropping this history is the whole point.
        if (generation != _loadGeneration) return;

        Messages.Clear();
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
        run = _runManager.GetActiveRun(_session.Id);
        _historyLoadedRunId = run is { IsTerminal: false }
            && _runManager.IsRunHistoryPersisted(run.RunId)
                ? run.RunId
                : null;

        if (!hasRun) StoreSessionCache();
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
        _ = _sessions.UpdateSessionAsync(_session);
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


    // ---- IChatProjectionTarget (live rendering into this page's list) ----

    IList<ChatMessage> IChatProjectionTarget.Messages => Messages;
    string? IChatProjectionTarget.AgentName => _agent?.Name;
    long IChatProjectionTarget.NextId() => NextId();
    void IChatProjectionTarget.Add(ChatMessage message) => Messages.Add(message);
    void IChatProjectionTarget.ResolveAgentImage(string dataUrl, ChatAttachment attachment)
        => _ = ResolveAgentImageAsync(dataUrl, attachment);
}
