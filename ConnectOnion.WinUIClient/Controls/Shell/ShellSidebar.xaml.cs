using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using ConnectOnion.WinUIClient.Models;
using ConnectOnion.WinUIClient.Common;
using ConnectOnion.WinUIClient.Services;
using ConnectOnion.WinUIClient.Data;
using ConnectOnion.WinUIClient.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI.Dispatching;
using Windows.UI.ViewManagement;

namespace ConnectOnion.WinUIClient.Controls;

/// <summary>
/// The agent/session tree down the left of the shell, plus the pinned-conversation section and
/// the online count.
///
/// Unlike the app's other surfaces this control has no view model — it owns its collections and
/// reaches storage through <c>AppServices</c> directly, because WinUI constructs it from XAML.
/// It also raises navigation as an event rather than performing it: the sidebar knows *what*
/// the user picked, and <c>MainWindow</c> owns the frame that acts on it.
///
/// The whole tree is rebuilt from storage on every <see cref="RefreshAsync"/> — there is no
/// incremental update path. That keeps it simple and correct at the cost of losing transient UI
/// state, which is exactly why expansion state is remembered separately (see
/// <see cref="_expandedAgentIds"/>).
/// </summary>
public sealed partial class ShellSidebar : UserControl, INotifyPropertyChanged
{
    public ObservableCollection<ShellAgentItem> ShellAgents { get; } = new();
    public ObservableCollection<ShellSessionItem> PinnedSessions { get; } = new();

    public event Action<Type, bool>? NavigationRequested;
    public event Action<FrameworkElement>? SettingsRequested;
    public event Action<FrameworkElement>? SessionSearchRequested;
    /// <summary>The sidebar click means "I want the
    /// add-agent form open", not merely "show me the landing page" — the shell answers it by
    /// opening the overlay without navigating. The button is passed back for focus restoration.</summary>
    public event Action<FrameworkElement>? AddAgentRequested;
    public event PropertyChangedEventHandler? PropertyChanged;

    private readonly AgentPresenceService _presence = AppServices.Presence;
    private readonly DispatcherQueue? _dispatcher = DispatcherQueue.GetForCurrentThread();
    /// <summary>Which agents the user has expanded. Held here rather than on the items because
    /// <see cref="RefreshAsync"/> discards and rebuilds every item — without this, any refresh
    /// (a rename, a new conversation, a presence sweep) would collapse the whole tree.</summary>
    private readonly HashSet<string> _expandedAgentIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string UpdatedAt, string Preview)> _sessionPreviewCache =
        new(StringComparer.Ordinal);
    private Type? _currentPageType;
    private int _onlineAgentCount;
    /// <summary>False until the first refresh completes. Distinguishes "the user has collapsed
    /// every agent" from "we have not observed any expansion state yet" — the first refresh
    /// falls back to expanding the selected agent, later ones honour the empty set.</summary>
    private bool _hasExpansionState;
    private bool _loadedPersistedExpansionState;
    private bool _isPinnedExpanded = true;
    private int? _lastRenderSignature;
    /// <summary>The <see cref="Data.StorageRevision"/> and page the last full load was made
    /// against, so an unchanged refresh can return without touching SQLite at all.</summary>
    private long _lastLoadedRevision = -1;
    private Type? _lastLoadedPageType;
    /// <summary>The agents the last load saw, kept only so the skip path above can still keep the
    /// presence probes running — those are TTL-based and must not stall just because the tree
    /// did not need redrawing.</summary>
    private IReadOnlyList<AgentConfig> _knownAgents = Array.Empty<AgentConfig>();
    private int _shutdown;

    public int OnlineAgentCount
    {
        get => _onlineAgentCount;
        private set
        {
            if (_onlineAgentCount == value) return;
            _onlineAgentCount = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OnlineAgentCountText));
            OnPropertyChanged(nameof(OnlineAgentStatusText));
            OnPropertyChanged(nameof(HasOnlineAgents));
        }
    }

    public string AppVersionBadgeText => $"v{AppVersionService.DisplayVersion}";
    public bool HasOnlineAgents => OnlineAgentCount > 0;
    public string OnlineAgentCountText => OnlineAgentCount switch
    {
        0 => LocalizedStrings.Get("AgentCountNoneOnline", "No agents online"),
        1 => LocalizedStrings.Get("AgentCountOneOnline", "1 agent online"),
        _ => LocalizedStrings.Format(
            "AgentCountOnline",
            "{0} agents online",
            OnlineAgentCount),
    };
    public string OnlineAgentStatusText => OnlineAgentCountText.ToUpper(CultureInfo.CurrentUICulture);
    public bool HasPinnedSessions => PinnedSessions.Count > 0;
    public double PinnedChevronAngle => IsPinnedExpanded ? 0 : 270;
    public string PinnedToggleAccessibilityName => IsPinnedExpanded
        ? LocalizedStrings.Get("SidebarCollapsePinnedChats", "Collapse pinned shortcuts")
        : LocalizedStrings.Get("SidebarExpandPinnedChats", "Expand pinned shortcuts");
    public bool IsPinnedExpanded
    {
        get => _isPinnedExpanded;
        private set
        {
            if (_isPinnedExpanded == value) return;
            _isPinnedExpanded = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PinnedChevronAngle));
            OnPropertyChanged(nameof(PinnedToggleAccessibilityName));
        }
    }

    public ShellSidebar()
    {
        InitializeComponent();
        DataContext = this;
        _presence.PresenceChanged += OnPresenceChanged;

        // The presence event is app-level and long-lived. Without detaching, a torn-down
        // sidebar (e.g. after a forceReload navigation) keeps receiving events while its
        // DispatcherQueue is null — the NullReferenceException in OnPresenceChanged. Detach
        // on unload; re-attach on (re)load in case the control is moved in the tree.
        Loaded += OnLoadedAttachPresence;
        Unloaded += OnUnloadedDetachPresence;
    }

    private void OnLoadedAttachPresence(object sender, RoutedEventArgs e)
    {
        if (Volatile.Read(ref _shutdown) != 0) return;
        _presence.PresenceChanged -= OnPresenceChanged;
        _presence.PresenceChanged += OnPresenceChanged;
    }

    private void OnUnloadedDetachPresence(object sender, RoutedEventArgs e)
        => _presence.PresenceChanged -= OnPresenceChanged;

    /// <summary>
    /// Synchronously detaches every app-level callback before the window's native XAML tree is
    /// destroyed. Unloaded is not guaranteed on application exit, and removing the event alone
    /// cannot retract a presence update that is already queued on the dispatcher.
    /// </summary>
    public void Shutdown()
    {
        if (Interlocked.Exchange(ref _shutdown, 1) != 0) return;
        _presence.PresenceChanged -= OnPresenceChanged;
        Loaded -= OnLoadedAttachPresence;
        Unloaded -= OnUnloadedDetachPresence;
    }

    public void SetCurrentPage(Type? pageType)
    {
        _currentPageType = pageType;
        // Agent library is a navigation destination, so it carries a selected state like any
        // other. This is the only thing that can change it — selection is derived purely from
        // which page the frame is on, never stored.
        OnPropertyChanged(nameof(IsAgentLibrarySelected));
    }

    /// <summary>
    /// Whether the shell is currently *on* the agent library, which is what the nav button paints
    /// its selected background and accent rail from.
    ///
    /// <para>Deliberately not symmetric with <see cref="IsAgentSelected"/>: that one asks "is this
    /// agent the chosen one", which is only meaningful on a page about an agent. This asks which
    /// destination the frame is showing, so exactly one nav row is ever lit.</para>
    /// </summary>
    public bool IsAgentLibrarySelected => _currentPageType == typeof(HomePage);

    public void FocusFirstNavigation()
        => AgentsNavigationButton.Focus(FocusState.Programmatic);

    /// <summary>Rebuilds the entire tree from storage. Called after anything that could change
    /// what the sidebar shows — agent added/renamed/deleted, conversation created/pinned/removed,
    /// navigation. Cheap enough to be unconditional at this scale (tens of rows).</summary>
    public async System.Threading.Tasks.Task RefreshAsync()
    {
        if (Volatile.Read(ref _shutdown) != 0) return;

        // Cheapest possible short-circuit, and it has to come first. The render-signature guard
        // further down avoids rebuilding the tree, but it is computed *from* the data — so a
        // navigation that changed nothing still cost a full agent-table read, a full session-table
        // read and a batch of preview queries before it could conclude there was nothing to do.
        // This method runs on every navigation, so that was the common case, not the rare one.
        //
        // The comparison also carries the current page type, because row selection is
        // page-dependent (see IsAgentSelected): moving between Home and an agent page changes what
        // the tree should render without changing a byte of storage.
        var revision = Data.StorageRevision.Current;
        if (_lastLoadedRevision == revision
            && _lastLoadedPageType == _currentPageType
            && _lastRenderSignature is not null)
        {
            ProbeAgentPresence();
            return;
        }

        var agentsState = await AppServices.Agents.LoadAsync();
        if (Volatile.Read(ref _shutdown) != 0) return;
        var sessionsState = await AppServices.Sessions.LoadAsync();
        if (Volatile.Read(ref _shutdown) != 0) return;
        await RefreshSessionPreviewCacheAsync(sessionsState.Sessions);
        if (Volatile.Read(ref _shutdown) != 0) return;
        _lastLoadedRevision = revision;
        _lastLoadedPageType = _currentPageType;

        // Must run *before* the Clear below, while the old items still hold their expansion.
        RememberExpandedAgentsFromUi();
        if (!_loadedPersistedExpansionState)
        {
            var persisted = await AppServices.SidebarState.LoadAsync();
            if (!_hasExpansionState && persisted.HasAgentExpansionState)
            {
                _expandedAgentIds.Clear();
                _expandedAgentIds.UnionWith(persisted.ExpandedAgentIds);
                _hasExpansionState = true;
            }
            IsPinnedExpanded = persisted.IsPinnedExpanded;
            _loadedPersistedExpansionState = true;
        }

        var renderSignature = ComputeRenderSignature(agentsState, sessionsState);
        if (_lastRenderSignature == renderSignature)
        {
            foreach (var agent in agentsState.Agents) _ = _presence.EnsureCheckedAsync(agent);
            return;
        }
        _lastRenderSignature = renderSignature;
        _knownAgents = agentsState.Agents;

        PinnedSessions.Clear();
        ShellAgents.Clear();
        // A lookup, not a per-session scan: pinned rows show their owning agent's name, and the
        // pinned list spans every agent.
        var agentNames = agentsState.Agents.ToDictionary(agent => agent.Id, agent => agent.Name, StringComparer.Ordinal);
        foreach (var session in sessionsState.Sessions
                     .Where(session => session.IsPinned)
                     .OrderByDescending(session => session.UpdatedAt, StringComparer.Ordinal))
        {
            PinnedSessions.Add(CreateShellSessionItem(
                session,
                sessionsState.ActiveSessionId,
                agentNames));
        }

        foreach (var agent in agentsState.Agents)
        {
            var item = new ShellAgentItem
            {
                Id = agent.Id,
                Name = agent.Name,
                Address = agent.Address,
                DirectUrl = agent.DirectUrl,
                IconPath = agent.IconPath,
                IsSelected = IsAgentSelected(agent.Id, agentsState.SelectedAgentId),
                // First build: open the selected agent so the app doesn't start fully collapsed.
                // Afterwards the user's own expansion state is authoritative, including "all
                // collapsed" — which is why the flag exists rather than testing for an empty set.
                IsExpanded = _hasExpansionState
                    ? _expandedAgentIds.Contains(agent.Id)
                    : agent.Id == agentsState.SelectedAgentId,
                Presence = _presence.GetPresence(agent.Id),
            };

            foreach (var session in sessionsState.Sessions
                         .Where(s => s.AgentId == agent.Id)
                         .OrderByDescending(s => s.UpdatedAt, StringComparer.Ordinal))
            {
                item.Sessions.Add(CreateShellSessionItem(session, sessionsState.ActiveSessionId, agentNames));
            }
            item.RebuildSessionGroups();

            ShellAgents.Add(item);
        }

        OnPropertyChanged(nameof(HasPinnedSessions));
        UpdateOnlineAgentCount();
        // Fire-and-forget presence probes: the rows are already on screen with whatever the
        // service last knew, and each probe raises PresenceChanged to update its own dot when it
        // lands. Awaiting them would hold the whole sidebar behind the slowest network call.
        foreach (var agent in agentsState.Agents)
        {
            _ = _presence.EnsureCheckedAsync(agent);
        }

        _hasExpansionState = true;
    }

    /// <summary>Keeps the reachability probes ticking on the path that skips the reload. They are
    /// TTL-based and coalesced inside the service, so calling this on every navigation costs a
    /// dictionary lookup per agent when nothing is due.</summary>
    private void ProbeAgentPresence()
    {
        if (Volatile.Read(ref _shutdown) != 0) return;
        foreach (var agent in _knownAgents) _ = _presence.EnsureCheckedAsync(agent);
    }

    private int ComputeRenderSignature(AgentsState agentsState, SessionsState sessionsState)
    {
        var hash = new HashCode();
        // The page comes first because row selection is page-dependent, not purely storage-derived:
        // IsAgentSelected/IsSessionSelected both gate on _currentPageType, so the *same* selected
        // agent and active conversation must render differently on AgentDetailPage than on
        // ChatPage. Leaving it out made the common "open an agent, then click the conversation you
        // were already in" path skip the rebuild entirely — storage genuinely had not changed — so
        // the tree kept the agent row lit and the conversation row unselected.
        hash.Add(_currentPageType);
        hash.Add(agentsState.SelectedAgentId, StringComparer.Ordinal);
        hash.Add(sessionsState.ActiveSessionId, StringComparer.Ordinal);
        hash.Add(IsPinnedExpanded);
        foreach (var expanded in _expandedAgentIds.OrderBy(id => id, StringComparer.Ordinal))
            hash.Add(expanded, StringComparer.Ordinal);
        foreach (var agent in agentsState.Agents)
        {
            hash.Add(agent.Id, StringComparer.Ordinal);
            hash.Add(agent.Name, StringComparer.Ordinal);
            hash.Add(agent.Address, StringComparer.Ordinal);
            hash.Add(agent.DirectUrl, StringComparer.Ordinal);
            hash.Add(agent.IconPath, StringComparer.Ordinal);
        }
        foreach (var session in sessionsState.Sessions)
        {
            hash.Add(session.Id, StringComparer.Ordinal);
            hash.Add(session.AgentId, StringComparer.Ordinal);
            hash.Add(session.Title, StringComparer.Ordinal);
            hash.Add(session.UpdatedAt, StringComparer.Ordinal);
            hash.Add(session.IsPinned);
            if (_sessionPreviewCache.TryGetValue(session.Id, out var preview))
                hash.Add(preview.Preview, StringComparer.Ordinal);
        }
        return hash.ToHashCode();
    }

    /// <summary>Ctrl+N / "New chat". Returns false when there is no agent to chat with, which
    /// lets the window surface the add-agent requirement.</summary>
    public async System.Threading.Tasks.Task<bool> StartNewChatAsync()
    {
        var agentsState = await AppServices.Agents.LoadAsync();
        // Prefer the selected agent, else any agent: a new-chat shortcut pressed with nothing
        // selected should still do something sensible rather than silently no-op.
        var agent = agentsState.Agents.FirstOrDefault(entry => entry.Id == agentsState.SelectedAgentId)
            ?? agentsState.Agents.FirstOrDefault();
        if (agent is null) return false;

        await AppServices.Agents.SetSelectedAgentAsync(agent.Id);

        // No session is created here. AgentDetailPage is the start-a-chat surface and creates one
        // when the user actually sends their first message, so a shortcut pressed by accident
        // leaves nothing behind — the sidebar used to collect an empty conversation per stray
        // Same path the agent row's "+" takes (ShellSidebar.Events.AddChat_Click), so the sidebar's
        // two entry points into "new chat" behave identically.
        FocusAgentInSidebar(agent.Id);
        // forceReload because the target may already be the current page: a plain Navigate to the
        // page you are on is a no-op, and the composer would keep whatever was on it.
        RequestNavigation(typeof(AgentDetailPage), forceReload: true);
        return true;
    }

    private void OnPresenceChanged(string agentId)
    {
        if (Volatile.Read(ref _shutdown) != 0) return;

        // Fired from the app-level presence service, possibly on a background thread and
        // possibly after this control left the visual tree. Guard the dispatcher: a null
        // one means there is no live UI to update, so there is nothing to do.
        var dispatcher = _dispatcher ?? DispatcherQueue;
        if (dispatcher is null) return;

        dispatcher.TryEnqueue(() =>
        {
            // Shutdown may have started after TryEnqueue accepted this callback.
            if (Volatile.Read(ref _shutdown) != 0) return;
            if (FindShellAgent(agentId) is { } agent)
            {
                agent.Presence = _presence.GetPresence(agentId);
            }
            UpdateOnlineAgentCount();
        });
    }

    private void UpdateOnlineAgentCount()
        => OnlineAgentCount = ShellAgents.Count(agent => _presence.GetPresence(agent.Id) == AgentPresence.Online);

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private void RequestNavigation(Type page, bool forceReload = false)
        => NavigationRequested?.Invoke(page, forceReload);

    private ShellSessionItem CreateShellSessionItem(
        SessionSummary session,
        string? activeSessionId,
        IReadOnlyDictionary<string, string> agentNames)
    {
        var updatedAt = ParseUpdatedAt(session.UpdatedAt);
        var preview = _sessionPreviewCache.TryGetValue(session.Id, out var cached)
            ? cached.Preview
            : "No messages yet";

        return new()
        {
            Id = session.Id,
            Title = session.Title,
            Preview = preview,
            TimeGroup = FormatTimeGroup(updatedAt),
            AgentName = agentNames.TryGetValue(session.AgentId, out var agentName) ? agentName : "",
            IsPinned = session.IsPinned,
            // Both the pinned shortcut and the canonical tree row represent the current
            // conversation, so both carry the current-location accent.
            IsSelected = IsSessionSelected(session.Id, activeSessionId),
        };
    }

    private async System.Threading.Tasks.Task RefreshSessionPreviewCacheAsync(
        IReadOnlyCollection<SessionSummary> sessions)
    {
        var currentIds = sessions.Select(session => session.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var staleId in _sessionPreviewCache.Keys.Where(id => !currentIds.Contains(id)).ToArray())
        {
            _sessionPreviewCache.Remove(staleId);
        }

        var refresh = sessions
            .Where(session => !_sessionPreviewCache.TryGetValue(session.Id, out var cached)
                              || !string.Equals(cached.UpdatedAt, session.UpdatedAt, StringComparison.Ordinal))
            .ToArray();
        var lastMessages = await AppServices.Conversations.LoadLastMessagesAsync(
            refresh.Select(session => session.Id).ToArray());

        foreach (var session in refresh)
        {
            lastMessages.TryGetValue(session.Id, out var lastMessage);
            _sessionPreviewCache[session.Id] = (session.UpdatedAt, BuildMessagePreview(lastMessage));
        }
    }

    private static string BuildMessagePreview(ChatMessage? message)
    {
        if (message is null) return "No messages yet";

        var value = !string.IsNullOrWhiteSpace(message.Content)
            ? message.Content
            : !string.IsNullOrWhiteSpace(message.EventTitle)
                ? message.EventTitle
                : message.EventDetail;
        if (string.IsNullOrWhiteSpace(value)) return "Activity update";

        var normalized = string.Join(
            " ",
            value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return normalized.Length <= 72 ? normalized : $"{normalized[..71]}…";
    }

    private static DateTimeOffset? ParseUpdatedAt(string value)
        => DateTimeOffset.TryParse(value, out var parsed) ? parsed.ToLocalTime() : null;

    private static string FormatTimeGroup(DateTimeOffset? updatedAt)
    {
        if (updatedAt is null) return "Earlier";

        var date = updatedAt.Value.Date;
        var today = DateTimeOffset.Now.Date;
        if (date == today) return "Today";
        if (date == today.AddDays(-1)) return "Yesterday";
        return "Earlier";
    }

    /// <summary>Whether an agent row renders as selected. Selection is page-dependent: an agent
    /// is only "current" while the user is on a page that is actually about an agent, so
    /// Settings or Home leaves the tree with nothing highlighted rather than pointing at
    /// whichever agent happened to be selected last.</summary>
    private bool IsAgentSelected(string agentId, string? selectedAgentId)
        => _currentPageType == typeof(AgentDetailPage)
            || _currentPageType == typeof(SessionsPage)
            ? agentId == selectedAgentId
            : false;

    private bool IsSessionSelected(string sessionId, string? activeSessionId)
        => _currentPageType == typeof(ChatPage) && sessionId == activeSessionId;

    /// <summary>Captures the live expansion state before a rebuild discards the items.</summary>
    private void RememberExpandedAgentsFromUi()
    {
        // An empty tree carries no information — this is the pre-first-refresh case, and
        // clearing here would wipe a focus another method just set (see FocusAgentInSidebar).
        if (ShellAgents.Count == 0) return;

        _expandedAgentIds.Clear();
        foreach (var agent in ShellAgents.Where(agent => agent.IsExpanded))
        {
            _expandedAgentIds.Add(agent.Id);
        }
        _hasExpansionState = true;
    }

    /// <summary>Collapses everything except one agent. Used when navigating *to* an agent, so
    /// the tree focuses on where the user just went instead of accumulating open branches.
    /// Deliberately destructive — this is the one place expansion state is overridden rather
    /// than preserved.</summary>
    private void FocusAgentInSidebar(string agentId)
    {
        _expandedAgentIds.Clear();
        _expandedAgentIds.Add(agentId);
        foreach (var agent in ShellAgents)
        {
            agent.IsExpanded = agent.Id == agentId;
        }
        _hasExpansionState = true;
        QueueSidebarStateSave();
    }

    /// <summary>Reveals one agent without disturbing any other branches the user has open.
    /// Pinned conversations use this additive path so the canonical row becomes visible while
    /// ordinary tree navigation keeps the existing single-agent focus behaviour.</summary>
    private void RevealAgentInSidebar(string agentId)
    {
        _expandedAgentIds.Add(agentId);
        var agent = FindShellAgent(agentId);
        if (agent is not null) agent.IsExpanded = true;
        _hasExpansionState = true;
        QueueSidebarStateSave();
    }

    /// <summary>
    /// Snapshots the UI state before leaving the UI thread. The repository coalesces overlapping
    /// writes so rapid disclosure clicks still persist the last visible state.
    /// </summary>
    private void QueueSidebarStateSave()
    {
        var expandedAgentIds = _expandedAgentIds.ToArray();
        var isPinnedExpanded = IsPinnedExpanded;
        _ = SaveSidebarStateAsync(expandedAgentIds, isPinnedExpanded);
    }

    private static async System.Threading.Tasks.Task SaveSidebarStateAsync(
        string[] expandedAgentIds,
        bool isPinnedExpanded)
    {
        try
        {
            await AppServices.SidebarState
                .SaveAsync(expandedAgentIds, isPinnedExpanded)
                .ConfigureAwait(false);
        }
        catch
        {
            // Disclosure remains usable in memory if persistence is temporarily unavailable.
        }
    }

    /// <summary>Guarantees the agent has an active conversation, reusing the current one if it
    /// already belongs to this agent, else its most recent, else creating one.</summary>
    private async System.Threading.Tasks.Task<string> EnsureSessionAsync(string agentId)
    {
        var sessionsState = await AppServices.Sessions.LoadAsync();
        var active = sessionsState.Sessions.FirstOrDefault(s => s.Id == sessionsState.ActiveSessionId && s.AgentId == agentId);
        if (active is not null) return active.Id;

        var recent = sessionsState.Sessions
            .Where(s => s.AgentId == agentId)
            .OrderByDescending(s => s.UpdatedAt, StringComparer.Ordinal)
            .FirstOrDefault();
        if (recent is not null)
        {
            await AppServices.Sessions.SetActiveSessionAsync(recent.Id);
            return recent.Id;
        }

        return await CreateAndActivateSessionAsync(sessionsState, agentId);
    }

    /// <summary>Appends a fresh "Conversation N" session for the agent to the
    /// already-loaded state, makes it active, persists, and returns its id.</summary>
    private static async System.Threading.Tasks.Task<string> CreateAndActivateSessionAsync(
        SessionsState sessionsState, string agentId)
    {
        var session = SessionSummary.NewConversation(agentId, sessionsState.Sessions, Common.SessionTitles.PlaceholderFormat);
        sessionsState.Sessions.Add(session);
        sessionsState.ActiveSessionId = session.Id;
        // One INSERT plus the active pointer, not a whole-index reconcile — see AppendSessionAsync.
        await AppServices.Sessions.AppendSessionAsync(session);
        return session.Id;
    }

    private async System.Threading.Tasks.Task SelectAgentAsync(string agentId)
    {
        await AppServices.Agents.SetSelectedAgentAsync(agentId);
        RequestNavigation(typeof(ChatPage), forceReload: true);
    }

    /// <summary>Opens a conversation, moving agent selection with it (a pinned row can belong to
    /// an agent other than the current one).</summary>
    /// <param name="preserveOtherExpandedAgents">True for clicks in the pinned section: reveal
    /// the conversation's owning agent without collapsing any other open branches. Ordinary
    /// session clicks still focus the tree onto one agent.</param>
    private async System.Threading.Tasks.Task SelectSessionAsync(
        string sessionId,
        bool preserveOtherExpandedAgents = false)
    {
        var sessionsState = await AppServices.Sessions.LoadAsync();
        var session = sessionsState.Sessions.FirstOrDefault(s => s.Id == sessionId);
        if (session is null) return;

        await AppServices.Sessions.SetActiveSessionAsync(session.Id);
        await AppServices.Agents.SetSelectedAgentAsync(session.AgentId);

        if (preserveOtherExpandedAgents)
            RevealAgentInSidebar(session.AgentId);
        else
            FocusAgentInSidebar(session.AgentId);
        RequestNavigation(typeof(ChatPage), forceReload: true);
    }

    private ShellAgentItem? FindShellAgent(string agentId)
        => ShellAgents.FirstOrDefault(agent => agent.Id == agentId);

    /// <summary>Shows the shared Delete/Cancel confirmation dialog; true if the
    /// user confirmed deletion.</summary>
    private async System.Threading.Tasks.Task<bool> ConfirmDeleteAsync(string title, string content)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = content,
            PrimaryButtonText = LocalizedStrings.Get("CommonDelete", "Delete"),
            CloseButtonText = LocalizedStrings.Get("CommonCancel", "Cancel"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        return await dialog.ShowThemedAsync() == ContentDialogResult.Primary;
    }

    private void ChatHome_Click(object sender, RoutedEventArgs e)
    {
        RequestNavigation(typeof(HomePage), forceReload: true);
    }

    private async void Agent_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string agentId)
        {
            await AppServices.Agents.SetSelectedAgentAsync(agentId);
            FocusAgentInSidebar(agentId);
            RequestNavigation(typeof(AgentDetailPage), forceReload: true);
        }
    }

    private void ToggleAgent_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string agentId &&
            FindShellAgent(agentId) is { } agent)
        {
            agent.IsExpanded = !agent.IsExpanded;
            if (agent.IsExpanded)
            {
                _expandedAgentIds.Add(agentId);
            }
            else
            {
                _expandedAgentIds.Remove(agentId);
            }
            _hasExpansionState = true;
            QueueSidebarStateSave();
        }
    }

    private void TogglePinned_Click(object sender, RoutedEventArgs e)
    {
        IsPinnedExpanded = !IsPinnedExpanded;
        QueueSidebarStateSave();
    }

    /// <summary>Honours the OS "show animations" accessibility setting (Reduce Motion). Read once
    /// as a startup constant, matching <c>DisclosureAnimation</c>/<c>ThinkingIndicator</c> — the
    /// value only changes across an app restart, and a stale read costs at most the motion the user
    /// sees until then.</summary>
    private static readonly bool AnimationsEnabled = new UISettings().AnimationsEnabled;

    /// <summary>
    /// Starts the breathing animation on the "Connecting" presence dot once per
    /// realized Ellipse — an in-flight reachability probe pulses; a settled Online
    /// dot is solid and static. WinUI's FrameworkElement.Triggers/EventTrigger has
    /// no RoutedEvent property (WPF-only), so a XAML-declared Loaded trigger isn't
    /// valid here — the Storyboard is built and started from code instead.
    ///
    /// Under Reduce Motion the dot is left solid (full opacity): the "Connecting" state is still
    /// carried by its colour, tooltip and AutomationProperties.Name, so suppressing the pulse costs
    /// nothing but the motion.
    /// </summary>
    private void PresenceCheckingDot_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Ellipse ellipse) return;
        if (!AnimationsEnabled) return;

        var animation = new DoubleAnimation
        {
            From = 1.0,
            To = 0.35,
            Duration = TimeSpan.FromSeconds(1.2),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        Storyboard.SetTarget(animation, ellipse);
        Storyboard.SetTargetProperty(animation, "Opacity");

        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Begin();
    }

}
