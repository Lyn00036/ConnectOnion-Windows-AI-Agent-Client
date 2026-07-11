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
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI.Dispatching;

namespace ConnectOnion.WinUIClient.Controls;

public sealed partial class ShellSidebar : UserControl, INotifyPropertyChanged
{
    public ObservableCollection<ShellAgentItem> ShellAgents { get; } = new();
    public ObservableCollection<ShellSessionItem> PinnedSessions { get; } = new();

    public event Action<Type, bool>? NavigationRequested;
    public event Action<FrameworkElement>? SettingsRequested;
    public event PropertyChangedEventHandler? PropertyChanged;

    private readonly AgentPresenceService _presence = AppServices.Presence;
    private readonly DispatcherQueue? _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly HashSet<string> _expandedAgentIds = new(StringComparer.Ordinal);
    private Type? _currentPageType;
    private int _onlineAgentCount;
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

    public async System.Threading.Tasks.Task RefreshAsync()
    {
        var agentsState = await AppServices.Agents.LoadAsync();
        var sessionsState = await AppServices.Sessions.LoadAsync();

        RememberExpandedAgentsFromUi();

        PinnedSessions.Clear();
        ShellAgents.Clear();
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
        foreach (var agent in agentsState.Agents)
        {
            _ = _presence.EnsureCheckedAsync(agent);
        }

        _hasExpansionState = true;
    }

    public async System.Threading.Tasks.Task<bool> StartNewChatAsync()
    {
        var agentsState = await AppServices.Agents.LoadAsync();
        var agent = agentsState.Agents.FirstOrDefault(entry => entry.Id == agentsState.SelectedAgentId)
            ?? agentsState.Agents.FirstOrDefault();
        if (agent is null) return false;

        agentsState.SelectedAgentId = agent.Id;
        await AppServices.Agents.SaveAsync(agentsState);

        await CreateSessionAsync(agent.Id);
        FocusAgentInSidebar(agent.Id);
        await RefreshAsync();
        RequestNavigation(typeof(ChatPage), forceReload: true);
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

    private bool IsAgentSelected(string agentId, string? selectedAgentId)
        => _currentPageType == typeof(ChatPage)
            || _currentPageType == typeof(AgentDetailPage)
            || _currentPageType == typeof(SessionsPage)
            ? agentId == selectedAgentId
            : false;

    private bool IsSessionSelected(string sessionId, string? activeSessionId)
        => _currentPageType == typeof(ChatPage) && sessionId == activeSessionId;

    private void RememberExpandedAgentsFromUi()
    {
        if (ShellAgents.Count == 0) return;

        _expandedAgentIds.Clear();
        foreach (var agent in ShellAgents.Where(agent => agent.IsExpanded))
        {
            _expandedAgentIds.Add(agent.Id);
        }
        _hasExpansionState = true;
    }

    private void FocusAgentInSidebar(string agentId)
    {
        _expandedAgentIds.Clear();
        _expandedAgentIds.Add(agentId);
        _hasExpansionState = true;
    }

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

    private async System.Threading.Tasks.Task<string> CreateSessionAsync(string agentId)
    {
        var sessionsState = await AppServices.Sessions.LoadAsync();
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

        if (!preserveSidebarState)
        {
            FocusAgentInSidebar(session.AgentId);
            await RefreshAsync();
        }
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
