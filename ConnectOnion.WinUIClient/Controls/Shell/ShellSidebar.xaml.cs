using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
    /// <summary>Raised by the sidebar's "Add agent" button. Distinct from a plain
    /// <see cref="NavigationRequested"/> to <c>HomePage</c> because the click means "I want the
    /// add-agent form open", not merely "show me the landing page" — the shell answers it by
    /// navigating *and* telling the page to open and reveal its form.</summary>
    public event Action? AddAgentRequested;
    public event PropertyChangedEventHandler? PropertyChanged;

    private readonly AgentPresenceService _presence = AppServices.Presence;
    private readonly DispatcherQueue? _dispatcher = DispatcherQueue.GetForCurrentThread();
    /// <summary>Which agents the user has expanded. Held here rather than on the items because
    /// <see cref="RefreshAsync"/> discards and rebuilds every item — without this, any refresh
    /// (a rename, a new conversation, a presence sweep) would collapse the whole tree.</summary>
    private readonly HashSet<string> _expandedAgentIds = new(StringComparer.Ordinal);
    private Type? _currentPageType;
    private int _onlineAgentCount;
    /// <summary>False until the first refresh completes. Distinguishes "the user has collapsed
    /// every agent" from "we have not observed any expansion state yet" — the first refresh
    /// falls back to expanding the selected agent, later ones honour the empty set.</summary>
    private bool _hasExpansionState;
    private bool _isPinnedExpanded = true;

    public int OnlineAgentCount
    {
        get => _onlineAgentCount;
        private set
        {
            if (_onlineAgentCount == value) return;
            _onlineAgentCount = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OnlineAgentCountText));
        }
    }

    public string OnlineAgentCountText => $"{OnlineAgentCount} online";
    public bool HasPinnedSessions => PinnedSessions.Count > 0;
    public double PinnedChevronAngle => IsPinnedExpanded ? 0 : 270;

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

        // Must run *before* the Clear below, while the old items still hold their expansion.
        RememberExpandedAgentsFromUi();

        PinnedSessions.Clear();
        ShellAgents.Clear();
        // A lookup, not a per-session scan: pinned rows show their owning agent's name, and the
        // pinned list spans every agent.
        var agentNames = agentsState.Agents.ToDictionary(agent => agent.Id, agent => agent.Name, StringComparer.Ordinal);
        foreach (var session in sessionsState.Sessions
                     .Where(session => session.IsPinned)
                     .OrderByDescending(session => session.UpdatedAt, StringComparer.Ordinal))
        {
            PinnedSessions.Add(CreateShellSessionItem(session, sessionsState.ActiveSessionId, agentNames));
        }

        foreach (var agent in agentsState.Agents)
        {
            var item = new ShellAgentItem
            {
                Id = agent.Id,
                Name = agent.Name,
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
        IReadOnlyDictionary<string, string> agentNames)
        => new()
        {
            Id = session.Id,
            Title = session.Title,
            AgentName = agentNames.TryGetValue(session.AgentId, out var agentName) ? agentName : "",
            IsPinned = session.IsPinned,
            IsSelected = IsSessionSelected(session.Id, activeSessionId),
        };

    /// <summary>Whether an agent row renders as selected. Selection is page-dependent: an agent
    /// is only "current" while the user is on a page that is actually about an agent, so
    /// Settings or Home leaves the tree with nothing highlighted rather than pointing at
    /// whichever agent happened to be selected last.</summary>
    private bool IsAgentSelected(string agentId, string? selectedAgentId)
        => _currentPageType == typeof(ChatPage)
            || _currentPageType == typeof(AgentDetailPage)
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
        _hasExpansionState = true;
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
        }
    }

    private void TogglePinned_Click(object sender, RoutedEventArgs e)
        => IsPinnedExpanded = !IsPinnedExpanded;

    /// <summary>
    /// Starts the online-dot breathing animation once per realized Ellipse.
    /// WinUI's FrameworkElement.Triggers/EventTrigger has no RoutedEvent
    /// property (WPF-only), so a XAML-declared Loaded trigger isn't valid here
    /// — the Storyboard is built and started from code instead.
    /// </summary>
    private void OnlinePresenceDot_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Ellipse ellipse) return;

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
