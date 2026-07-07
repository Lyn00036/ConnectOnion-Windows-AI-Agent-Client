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

namespace ConnectOnion.WinUIClient.Controls;

public sealed partial class ShellSidebar : UserControl, INotifyPropertyChanged
{
    public ObservableCollection<ShellAgentItem> ShellAgents { get; } = new();
    public ObservableCollection<ShellSessionItem> PinnedSessions { get; } = new();

    public event Action<Type, bool>? NavigationRequested;
    public event PropertyChangedEventHandler? PropertyChanged;

    private readonly AgentPresenceService _presence = AppServices.Presence;
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
    }

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
        => DispatcherQueue.TryEnqueue(() =>
        {
            if (FindShellAgent(agentId) is { } agent)
            {
                agent.Presence = _presence.GetPresence(agentId);
            }
            UpdateOnlineAgentCount();
        });

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

        var timestamp = DateTime.UtcNow.ToString("o");
        var count = sessionsState.Sessions.Count(s => s.AgentId == agentId) + 1;
        var session = new SessionSummary
        {
            Id = Guid.NewGuid().ToString(),
            AgentId = agentId,
            Title = $"Conversation {count}",
            CreatedAt = timestamp,
            UpdatedAt = timestamp,
        };
        sessionsState.Sessions.Add(session);
        sessionsState.ActiveSessionId = session.Id;
        await AppServices.Sessions.SaveAsync(sessionsState);
        return session.Id;
    }

    private async System.Threading.Tasks.Task<string> CreateSessionAsync(string agentId)
    {
        var sessionsState = await AppServices.Sessions.LoadAsync();
        var timestamp = DateTime.UtcNow.ToString("o");
        var count = sessionsState.Sessions.Count(s => s.AgentId == agentId) + 1;
        var session = new SessionSummary
        {
            Id = Guid.NewGuid().ToString(),
            AgentId = agentId,
            Title = $"Conversation {count}",
            CreatedAt = timestamp,
            UpdatedAt = timestamp,
        };
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

    private void AgentRow_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ShellAgentItem agent)
        {
            agent.AreActionsVisible = true;
        }
    }

    private void AgentRow_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ShellAgentItem agent)
        {
            agent.AreActionsVisible = false;
        }
    }

    private void AgentRow_GotFocus(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ShellAgentItem agent)
        {
            agent.AreActionsVisible = true;
        }
    }

    private void AgentRow_LostFocus(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ShellAgentItem agent)
        {
            agent.AreActionsVisible = false;
        }
    }

    private async void AddChat_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string agentId) return;

        var agentsState = await AppServices.Agents.LoadAsync();
        agentsState.SelectedAgentId = agentId;
        await AppServices.Agents.SaveAsync(agentsState);
        await CreateSessionAsync(agentId);
        FocusAgentInSidebar(agentId);
        await RefreshAsync();
        RequestNavigation(typeof(ChatPage), forceReload: true);
    }

    private async void DeleteAgent_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string agentId) return;

        try
        {
            var agentsState = await AppServices.Agents.LoadAsync();
        var agent = agentsState.Agents.FirstOrDefault(entry => entry.Id == agentId);
        if (agent is null) return;

        var dialog = new ContentDialog
        {
            Title = $"Delete {agent.Name}?",
            Content = "This agent and all of its local sessions will be removed.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        agentsState.Agents.Remove(agent);
        if (agentsState.SelectedAgentId == agentId)
        {
            agentsState.SelectedAgentId = agentsState.Agents.FirstOrDefault()?.Id;
        }
        await AppServices.Agents.SaveAsync(agentsState);

        var sessionsState = await AppServices.Sessions.LoadAsync();
        sessionsState.Sessions = sessionsState.Sessions
            .Where(session => session.AgentId != agentId)
            .ToList();
        if (sessionsState.ActiveSessionId is not null &&
            sessionsState.Sessions.All(session => session.Id != sessionsState.ActiveSessionId))
        {
            sessionsState.ActiveSessionId = sessionsState.Sessions.FirstOrDefault()?.Id;
        }
        await AppServices.Sessions.SaveAsync(sessionsState);

        await RefreshAsync();
        RequestNavigation(typeof(ChatPage), forceReload: true);
        }
        catch { /* Ignore errors during agent deletion. */ }
    }

    private async void Session_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string sessionId)
        {
            await SelectSessionAsync(sessionId);
        }
    }

    private async void TogglePinSession_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string sessionId) return;

        var sessionsState = await AppServices.Sessions.LoadAsync();
        var session = sessionsState.Sessions.FirstOrDefault(entry => entry.Id == sessionId);
        if (session is null) return;

        session.IsPinned = !session.IsPinned;
        await AppServices.Sessions.SaveAsync(sessionsState);
        await RefreshAsync();
    }

    private void SessionRow_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ShellSessionItem session)
        {
            session.AreActionsVisible = true;
        }
    }

    private void SessionRow_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ShellSessionItem session)
        {
            session.AreActionsVisible = false;
        }
    }

    private void SessionRow_GotFocus(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ShellSessionItem session)
        {
            session.AreActionsVisible = true;
        }
    }

    private void SessionRow_LostFocus(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ShellSessionItem session)
        {
            session.AreActionsVisible = false;
        }
    }

    private async void DeleteSession_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string sessionId) return;

        try
        {
            var sessionsState = await AppServices.Sessions.LoadAsync();
        var session = sessionsState.Sessions.FirstOrDefault(entry => entry.Id == sessionId);
        if (session is null) return;

        var dialog = new ContentDialog
        {
            Title = $"Delete {session.Title}?",
            Content = "This local conversation will be removed.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        sessionsState.Sessions.Remove(session);
        await AppServices.Conversations.DeleteMessagesAsync(sessionId);
        await AppServices.Conversations.DeleteExecutionsAndTracesAsync(sessionId);
        if (sessionsState.ActiveSessionId == sessionId)
        {
            sessionsState.ActiveSessionId = sessionsState.Sessions
                .Where(entry => entry.AgentId == session.AgentId)
                .OrderByDescending(entry => entry.UpdatedAt, StringComparer.Ordinal)
                .FirstOrDefault()?.Id
                ?? sessionsState.Sessions
                    .OrderByDescending(entry => entry.UpdatedAt, StringComparer.Ordinal)
                    .FirstOrDefault()?.Id;
        }
        await AppServices.Sessions.SaveAsync(sessionsState);

        await RefreshAsync();
        RequestNavigation(typeof(ChatPage), forceReload: true);
        }
        catch { /* Ignore errors during session deletion. */ }
    }

    private async void AddAgent_Click(object sender, RoutedEventArgs e)
    {
        AddAgentPanel.Visibility = AddAgentPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
        if (AddAgentPanel.Visibility == Visibility.Visible)
        {
            AddAgentPanel.FocusFirstField();
        }
        await RefreshAsync();
    }

    private void AddAgentForm_CloseRequested()
        => AddAgentPanel.Visibility = Visibility.Collapsed;

    private async void AddAgentForm_AgentAdded(AgentConfig _)
    {
        AddAgentPanel.Visibility = Visibility.Collapsed;
        await RefreshAsync();
        RequestNavigation(typeof(AgentDetailPage), forceReload: true);
    }

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
        RequestNavigation(typeof(SettingsPage));
    }
}
