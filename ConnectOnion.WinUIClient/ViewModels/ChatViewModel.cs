using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using ConnectOnion.Protocol;
using ConnectOnion.Protocol.Runtime;
using ConnectOnion.WinUIClient.Common;
using ConnectOnion.WinUIClient.Models;
using ConnectOnion.WinUIClient.Services;
using ConnectOnion.WinUIClient.Services.Notifications;
using ConnectOnion.WinUIClient.Services.Runtime;
using ConnectOnion.WinUIClient.Data;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

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

    // Coalescing slot for run snapshots on their way to the UI thread. See SubscribeToRun.
    private ConversationRunSnapshot? _pendingSnapshot;
    private int _snapshotDrainQueued;

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
        MessageFontSize = 14;
        EnterToSend = true;
        CurrentMode = AgentModes.Safe;

        // Live-apply message text-size changes made in Settings while this chat is open. The
        // page is cached (NavigationCacheMode.Required), so this instance and its subscription
        // live for the session — Cleanup detaches it for genuine disposal. Fired on the UI thread.
        MessageFontService.FontSizeChanged += ApplyLiveFontSize;

        // ListView.FontSize does not cascade into ListViewItemPresenter content, so each bubble
        // carries its own font size (bound by the templates). Stamp the current size onto every
        // message as it is added — projection appends bubbles from several call sites, and this is
        // the one place they all funnel through.
        Messages.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is null) return;
            foreach (ChatMessage message in e.NewItems)
                message.MessageFontSize = MessageFontSize;
        };

        // Ticks the "Thinking... 3s" counter. A DispatcherTimer (not a threading one) because it
        // writes straight into bound properties, and this type is resolved on the UI thread from
        // the page's constructor. It only runs while a thinking row is actually live — see
        // TickThinkingElapsed, which stops it as soon as there is nothing left to count.
        _thinkingTicker.Interval = TimeSpan.FromSeconds(1);
        _thinkingTicker.Tick += (_, _) => TickThinkingElapsed();
    }

    private readonly DispatcherTimer _thinkingTicker = new();

    /// <summary>
    /// Pushes elapsed seconds onto every live thinking row, and stops the timer once none are
    /// left. Driven from a timer rather than computed on read because nothing re-evaluates a
    /// computed property on a schedule — the value has to be pushed for the number to move.
    /// </summary>
    private void TickThinkingElapsed()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var anyRunning = false;
        foreach (var message in Messages)
        {
            if (!message.IsThinkingRunning) continue;
            anyRunning = true;
            var seconds = Math.Max(0, (now - message.CreatedAtUnixMs) / 1000);
            message.ThinkingElapsedLabel = $"{seconds}s";
        }
        if (!anyRunning) StopThinkingTicker();
    }

    /// <summary>Starts the elapsed-seconds ticker if a thinking row needs it. Idempotent.</summary>
    private void EnsureThinkingTicker()
    {
        if (!_thinkingTicker.IsEnabled) _thinkingTicker.Start();
    }

    /// <summary>
    /// Disarms the ticker. Called when a run reaches a terminal state and again from
    /// <c>ChatPage.OnUnloaded</c> — a DispatcherTimer left armed keeps firing after the page is
    /// gone, and the dispatcher goes on pumping past Window.Closed (see the shutdown notes in
    /// CLAUDE.md), so leaving one running is how a teardown-time access violation gets built.
    /// </summary>
    public void StopThinkingTicker()
    {
        _thinkingTicker.Stop();
        foreach (var message in Messages)
        {
            if (message.HasThinkingElapsed) message.ThinkingElapsedLabel = "";
        }
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
        MessageFontService.FontSizeChanged -= ApplyLiveFontSize;
        DetachPresence();
        DetachRun();
    }

    /// <summary>Applies a live message-font-size change from Settings. Assigning the property
    /// runs the generated <see cref="OnMessageFontSizeChanged"/> hook, which restamps every
    /// loaded bubble so the open chat resizes immediately.</summary>
    private void ApplyLiveFontSize(double size) => MessageFontSize = size;

    // Fires on every MessageFontSize assignment (ctor seed, load, and the live setting change).
    // Restamps bubbles already in the list; ones added afterwards are stamped on insert by the
    // Messages.CollectionChanged handler wired in the constructor.
    partial void OnMessageFontSizeChanged(double value)
    {
        foreach (var message in Messages)
            message.MessageFontSize = value;
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

    /// <summary>User-facing agent name for the chat header (e.g. <c>Remote Admin Agent</c>);
    /// the raw id stays in <see cref="SelectedAgentName"/> and on the config.</summary>
    public string SelectedAgentDisplayName => _agent is null
        ? "Agent"
        : Common.FriendlyAgentName.From(_agent.Name);

    public string SelectedAgentInitial => NameInitial.From(_agent?.Name);
    public string SessionShortId => _session is null ? "" : _session.Id[..Math.Min(8, _session.Id.Length)];
    public string? SessionId => _session?.Id;
    public string? AgentId => _agent?.Id;

    [ObservableProperty]
    public partial AgentAcceptedInputs? AcceptedInputs { get; private set; }

    [ObservableProperty]
    public partial string HeaderText { get; private set; }

    /// <summary>
    /// The conversation socket's current phase, shown under the composer. Derived rather than
    /// stored on the connection: the three facts that decide it arrive from three places — run
    /// snapshots (Connecting/Running), the reconnect markers the session manager publishes into
    /// the run's event stream (Reconnecting), and presence plus the connection registry for the
    /// idle baseline. <see cref="RefreshConnectionPhase"/> is the single place they are combined.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectionPhaseText))]
    public partial ConnectionPhase ConnectionPhase { get; private set; }

    /// <summary>Attempt number behind a <see cref="Models.ConnectionPhase.Reconnecting"/> phase,
    /// so the label can count down the tries left before the turn is given up on.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectionPhaseText))]
    public partial int ReconnectAttempt { get; private set; }

    public string ConnectionPhaseText => ConnectionPhase switch
    {
        Models.ConnectionPhase.Connecting => "Connecting",
        Models.ConnectionPhase.Connected => "Connected",
        Models.ConnectionPhase.Running => "Running",
        Models.ConnectionPhase.Waiting => "Waiting for you",
        Models.ConnectionPhase.Reconnecting =>
            $"Reconnecting {ReconnectAttempt}/{ReconnectPolicy.MaxAttempts}",
        Models.ConnectionPhase.Offline => "Offline",
        _ => "Idle",
    };

    /// <summary>
    /// Recomputes the phase for a conversation with no turn in flight. Only ever produces the
    /// three resting states — a live turn's phase is set from its snapshot instead, so this must
    /// not be called while one is running or it would overwrite Running with Connected.
    /// </summary>
    private void RefreshConnectionPhase()
    {
        if (_session is null)
        {
            ConnectionPhase = ConnectionPhase.Idle;
            return;
        }

        // Presence is the agent's reachability, checked over HTTP and independent of whether we
        // hold a socket; it outranks the socket because a live-looking socket to an agent that
        // has gone away is exactly the state a user must not be told is fine.
        if (Presence == AgentPresence.Offline)
        {
            ConnectionPhase = ConnectionPhase.Offline;
            return;
        }

        var connection = _runManager.Connections.Get(_session.Id);
        ConnectionPhase = connection is { IsConnected: true }
            ? ConnectionPhase.Connected
            : ConnectionPhase.Idle;
    }

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
        // Cleared here rather than only in RestoreConversationAsync, so the early returns below
        // (no agent, no session) can't leave the previous conversation's bubbles on screen.
        Messages.Clear();

        var prefs = await _preferences.LoadAsync(cancellationToken);
        EnterToSend = prefs.EnterToSend;
        MessageFontSize = MessageFontService.ToPixels(prefs.MessageFontSize);

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
        _runSubscription = SubscribeToRun(_session.Id);

        // Subscribe first, then probe: the resume publishes its first snapshot synchronously
        // inside StartRun, and a probe that ran ahead of the subscription would deliver that
        // snapshot to nobody — the page would sit on restored history while the turn streamed
        // past it.
        RefreshConnectionPhase();

        _ = TryResumeUnfinishedTurnAsync();
        _ = ProbePresenceOnOpenAsync();
        _ = RefreshAcceptedInputsAsync();
    }

    /// <summary>
    /// Subscribes to the app-level run and marshals its snapshots onto the UI thread,
    /// <b>coalescing</b> them: at most one drain is queued at a time, and a snapshot arriving
    /// while one is pending replaces it instead of queuing a second.
    /// <para>The registry publishes a snapshot per stream event <i>and</i> per partial-content
    /// update, so a streaming reply produces them far faster than the UI thread can apply one.
    /// Enqueuing each separately let the dispatcher queue grow without bound for the length of
    /// the turn, holding a closure and a snapshot per update — memory climbed for the whole
    /// reply and only came back once the queue drained.</para>
    /// <para>Dropping intermediate snapshots is safe, and is what the surrounding design was
    /// built for: snapshots are cumulative rather than incremental, and
    /// <see cref="ApplyRunSnapshot"/> replays from <see cref="_appliedEventCount"/>, so applying
    /// only the newest one still projects every event exactly once. Terminal snapshots are the
    /// exception — those are always queued, because a terminal transition arriving while an
    /// earlier one is in the slot must not be swallowed by a run that starts immediately after.</para>
    /// </summary>
    private IDisposable SubscribeToRun(string conversationId)
        => _runManager.Subscribe(conversationId, snapshot =>
        {
            Interlocked.Exchange(ref _pendingSnapshot, snapshot);
            // Only the thread that flips 0 -> 1 owns the queued drain; everyone else has already
            // handed its snapshot to that drain via the slot above.
            if (snapshot.IsTerminal || Interlocked.CompareExchange(ref _snapshotDrainQueued, 1, 0) == 0)
            {
                _dispatcher.TryEnqueue(DrainPendingSnapshot);
            }
        });

    private static readonly Action<ILogger, string, Exception?> LogResumeProbeFailed =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(10, "ResumeProbeFailed"),
            "Resume probe failed for conversation {ConversationId}");

    /// <summary>
    /// Asks the manager to rejoin a turn this conversation started before the app was last
    /// closed. Fire-and-forget: it opens a socket and waits on the host, and the page must not
    /// block its load on either. Anything it finds arrives through the run subscription like any
    /// other turn, so there is no result to handle here.
    /// </summary>
    private async Task TryResumeUnfinishedTurnAsync()
    {
        if (_agent is null || _session is null) return;

        var conversationId = _session.Id;
        try
        {
            await _runManager.TryResumeUnfinishedTurnAsync(conversationId, _agent);
        }
        catch (Exception ex)
        {
            // A conversation that cannot be resumed still opens normally — it just shows the
            // history it has. Never surface this as a load failure.
            LogResumeProbeFailed(_logger, conversationId, ex);
        }
    }

    /// <summary>Applies whatever snapshot is currently in the slot. Runs on the UI thread.</summary>
    private void DrainPendingSnapshot()
    {
        Interlocked.Exchange(ref _snapshotDrainQueued, 0);
        var snapshot = Interlocked.Exchange(ref _pendingSnapshot, null);
        // Null means a drain queued alongside a terminal snapshot already took it.
        if (snapshot is null) return;

        // A drain is queued on the dispatcher, so it can run after the page has switched
        // conversations and disposed the subscription that produced it. Applying a snapshot from
        // the conversation we just left would adopt its run id and project its events into the
        // new list; the conversation id is the authoritative check. (The old per-snapshot enqueue
        // had the same window and no guard.)
        if (!string.Equals(snapshot.ConversationId, _session?.Id, StringComparison.Ordinal)) return;

        ApplyRunSnapshot(snapshot);
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

        var loaded = await _conversations.LoadMessagesAsync(_session.Id, cancellationToken);
        var messages = ToolActivityMigration.UpgradeLegacyToolMessages(loaded);
        // A newer switch already owns the list — dropping this history is the whole point.
        if (generation != _loadGeneration) return;

        Messages.Clear();
        _createdAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        if (messages.Count > 0)
        {
            foreach (var message in messages) Messages.Add(message);
            // The whole conversation is loaded, so the first bubble is genuinely the start.
            if (messages[0].Id > 0) _createdAt = messages[0].Id;
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
        OnPropertyChanged(nameof(SelectedAgentDisplayName));
        OnPropertyChanged(nameof(SelectedAgentInitial));
        OnPropertyChanged(nameof(SessionShortId));
    }

    protected override void OnPresenceRaised()
    {
        OnPropertyChanged(nameof(CanSend));
        OnPropertyChanged(nameof(CanReconnect));
        OnPropertyChanged(nameof(ShowRetryBar));

        // Presence going offline must reach the status line, but only between turns: a live turn
        // owns the phase, and a presence probe that lands mid-stream must not relabel a working
        // agent as offline.
        if (!IsProcessing) RefreshConnectionPhase();
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
    // A page exists, so a failed tool card may open itself.
    bool IChatProjectionTarget.IsLiveView => true;
    long IChatProjectionTarget.NextId() => NextId();
    void IChatProjectionTarget.Add(ChatMessage message)
    {
        Messages.Add(message);
        // A live thinking row is the only thing the elapsed ticker exists for, so arming it here
        // is what keeps the timer off for every turn that never shows one.
        if (message.IsThinkingRunning) EnsureThinkingTicker();
    }
    void IChatProjectionTarget.ResolveAgentImage(string dataUrl, ChatAttachment attachment)
        => _ = ResolveAgentImageAsync(dataUrl, attachment);
}
