using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using ConnectOnion.WinUIClient.Models;
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
    public event Action? NewChatRequested;
    public event Action<FrameworkElement>? SettingsRequested;
    public event Action<FrameworkElement>? KeyboardShortcutsRequested;
    public event Action<FrameworkElement>? AboutRequested;
    public event Action? ExitRequested;
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
    private string _newChatTargetText = "Start a new chat";

    public int OnlineAgentCount
    {
        get => _onlineAgentCount;
        private set
        {
            if (_onlineAgentCount == value) return;
            _onlineAgentCount = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OnlineAgentCountText));
            OnPropertyChanged(nameof(HasOnlineAgents));
        }
    }

    public bool HasOnlineAgents => OnlineAgentCount > 0;
    public string OnlineAgentCountText => OnlineAgentCount switch
    {
        0 => "No agents online",
        1 => "1 agent online",
        _ => $"{OnlineAgentCount} agents online",
    };
    public bool HasPinnedSessions => PinnedSessions.Count > 0;
    public double PinnedChevronAngle => IsPinnedExpanded ? 0 : 270;
    public string NewChatTargetText
    {
        get => _newChatTargetText;
        private set
        {
            if (_newChatTargetText == value) return;
            _newChatTargetText = value;
            OnPropertyChanged();
        }
    }

    public bool IsPinnedExpanded
    {
        get => _isPinnedExpanded;
        private set
        {
            if (_isPinnedExpanded == value) return;
            _isPinnedExpanded = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PinnedChevronAngle));
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
        _presence.PresenceChanged -= OnPresenceChanged;
        _presence.PresenceChanged += OnPresenceChanged;
    }

    private void OnUnloadedDetachPresence(object sender, RoutedEventArgs e)
        => _presence.PresenceChanged -= OnPresenceChanged;

    public void SetCurrentPage(Type? pageType)
        => _currentPageType = pageType;

    /// <summary>Rebuilds the entire tree from storage. Called after anything that could change
    /// what the sidebar shows — agent added/renamed/deleted, conversation created/pinned/removed,
    /// navigation. Cheap enough to be unconditional at this scale (tens of rows).</summary>
    public async System.Threading.Tasks.Task RefreshAsync()
    {
        var agentsState = await AppServices.Agents.LoadAsync();
        var sessionsState = await AppServices.Sessions.LoadAsync();
        var newChatAgent = agentsState.Agents.FirstOrDefault(agent => agent.Id == agentsState.SelectedAgentId)
            ?? agentsState.Agents.FirstOrDefault();
        NewChatTargetText = newChatAgent is null
            ? "Add an agent to start a new chat"
            : $"Start a new chat with {newChatAgent.Name}";
        await RefreshSessionPreviewCacheAsync(sessionsState.Sessions);

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
                agentNames,
                isPinnedShortcut: true));
        }

        foreach (var agent in agentsState.Agents)
        {
            var item = new ShellAgentItem
            {
                Id = agent.Id,
                Name = agent.Name,
                Address = agent.Address,
                DirectUrl = agent.DirectUrl,
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

    /// <summary>Ctrl+N / "New chat". Returns false when there is no agent to chat with at all,
    /// which is the caller's cue to send the user to the add-agent flow instead.</summary>
    public async System.Threading.Tasks.Task<bool> StartNewChatAsync()
    {
        var agentsState = await AppServices.Agents.LoadAsync();
        // Prefer the selected agent, else any agent: a new-chat shortcut pressed with nothing
        // selected should still do something sensible rather than silently no-op.
        var agent = agentsState.Agents.FirstOrDefault(entry => entry.Id == agentsState.SelectedAgentId)
            ?? agentsState.Agents.FirstOrDefault();
        if (agent is null) return false;

        agentsState.SelectedAgentId = agent.Id;
        await AppServices.Agents.SaveAsync(agentsState);

        // No session is created here. AgentDetailPage is the start-a-chat surface and creates one
        // when the user actually sends their first message, so a shortcut pressed by accident
        // leaves nothing behind — the sidebar used to collect an empty conversation per stray
        // Ctrl+N. Same path the agent row's "+" takes (ShellSidebar.Events.AddChat_Click), so the
        // two entry points into "new chat" behave identically.
        FocusAgentInSidebar(agent.Id);
        await RefreshAsync();
        // forceReload because the target may already be the current page: a plain Navigate to the
        // page you are on is a no-op, and the composer would keep whatever was on it.
        RequestNavigation(typeof(AgentDetailPage), forceReload: true);
        return true;
    }

    private void OnPresenceChanged(string agentId)
    {
        // Fired from the app-level presence service, possibly on a background thread and
        // possibly after this control left the visual tree. Guard the dispatcher: a null
        // one means there is no live UI to update, so there is nothing to do.
        var dispatcher = _dispatcher ?? DispatcherQueue;
        if (dispatcher is null) return;

        dispatcher.TryEnqueue(() =>
        {
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
        IReadOnlyDictionary<string, string> agentNames,
        bool isPinnedShortcut = false)
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
            UpdatedAt = session.UpdatedAt,
            RelativeTime = FormatRelativeTime(updatedAt),
            TimeGroup = FormatTimeGroup(updatedAt),
            AgentName = agentNames.TryGetValue(session.AgentId, out var agentName) ? agentName : "",
            IsPinned = session.IsPinned,
            // The pinned section is a shortcut to the same destination rendered in the agent
            // tree. Only the canonical tree row carries the current-location accent.
            IsSelected = !isPinnedShortcut && IsSessionSelected(session.Id, activeSessionId),
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

        foreach (var session in sessions)
        {
            if (_sessionPreviewCache.TryGetValue(session.Id, out var cached)
                && string.Equals(cached.UpdatedAt, session.UpdatedAt, StringComparison.Ordinal))
            {
                continue;
            }

            var lastMessage = await AppServices.Conversations.LoadLastMessageAsync(session.Id);
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

    private static string FormatRelativeTime(DateTimeOffset? updatedAt)
    {
        if (updatedAt is null) return "";

        var elapsed = DateTimeOffset.Now - updatedAt.Value;
        if (elapsed < TimeSpan.Zero || elapsed < TimeSpan.FromMinutes(1)) return "Now";
        if (elapsed < TimeSpan.FromHours(1)) return $"{Math.Max(1, (int)elapsed.TotalMinutes)} min ago";
        if (elapsed < TimeSpan.FromDays(1)) return $"{Math.Max(1, (int)elapsed.TotalHours)} hr ago";
        if (elapsed < TimeSpan.FromDays(7))
            return updatedAt.Value.ToString("ddd", CultureInfo.CurrentCulture);
        return updatedAt.Value.ToString("MMM d", CultureInfo.CurrentCulture);
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
    /// already belongs to this agent, else its most recent, else creating one.
    /// <para>NOTE: this rule is a third copy of the same logic — see
    /// <c>SessionSelection.FindExisting</c> (the testable one) and
    /// <c>HomePage.EnsureSessionAsync</c>. They should all call the shared helper.</para></summary>
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
            sessionsState.ActiveSessionId = recent.Id;
            await AppServices.Sessions.SaveAsync(sessionsState);
            return recent.Id;
        }

        return await CreateAndActivateSessionAsync(sessionsState, agentId);
    }

    /// <summary>Appends a fresh "Conversation N" session for the agent to the
    /// already-loaded state, makes it active, persists, and returns its id.</summary>
    private static async System.Threading.Tasks.Task<string> CreateAndActivateSessionAsync(
        SessionsState sessionsState, string agentId)
    {
        var session = SessionSummary.NewConversation(agentId, sessionsState.Sessions);
        sessionsState.Sessions.Add(session);
        sessionsState.ActiveSessionId = session.Id;
        await AppServices.Sessions.SaveAsync(sessionsState);
        return session.Id;
    }

    private async System.Threading.Tasks.Task SelectAgentAsync(string agentId)
    {
        var agentsState = await AppServices.Agents.LoadAsync();
        agentsState.SelectedAgentId = agentId;
        await AppServices.Agents.SaveAsync(agentsState);
        await RefreshAsync();
        RequestNavigation(typeof(ChatPage), forceReload: true);
    }

    /// <summary>Opens a conversation, moving agent selection with it (a pinned row can belong to
    /// an agent other than the current one).</summary>
    /// <param name="preserveSidebarState">True for clicks in the pinned section: the tree keeps
    /// whatever was expanded instead of collapsing onto this conversation's agent. Pinned rows
    /// are a jump-list, and reorganizing the tree below — possibly for a different agent
    /// entirely — moves content out from under a user whose attention is at the top of the
    /// sidebar. The tree is still rebuilt either way, so selection highlighting stays correct.</param>
    private async System.Threading.Tasks.Task SelectSessionAsync(string sessionId, bool preserveSidebarState = false)
    {
        var sessionsState = await AppServices.Sessions.LoadAsync();
        var session = sessionsState.Sessions.FirstOrDefault(s => s.Id == sessionId);
        if (session is null) return;

        sessionsState.ActiveSessionId = session.Id;
        await AppServices.Sessions.SaveAsync(sessionsState);

        var agentsState = await AppServices.Agents.LoadAsync();
        agentsState.SelectedAgentId = session.AgentId;
        await AppServices.Agents.SaveAsync(agentsState);

        // Only the *focus* is conditional. RefreshAsync always runs, because it is what moves
        // the selection highlight onto the newly-opened conversation; skipping it would leave
        // the sidebar pointing at the previous one. RefreshAsync preserves whatever is expanded
        // (RememberExpandedAgentsFromUi), so without the focus call the tree is left untouched.
        if (!preserveSidebarState) FocusAgentInSidebar(session.AgentId);
        await RefreshAsync();
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
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async void ChatHome_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
        RequestNavigation(typeof(HomePage), forceReload: true);
    }

    private async void Agent_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string agentId)
        {
            var agentsState = await AppServices.Agents.LoadAsync();
            agentsState.SelectedAgentId = agentId;
            await AppServices.Agents.SaveAsync(agentsState);
            FocusAgentInSidebar(agentId);
            await RefreshAsync();
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
