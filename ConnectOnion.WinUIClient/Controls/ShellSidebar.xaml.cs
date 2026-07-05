using System;
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

    public event Action<Type, bool>? NavigationRequested;
    public event PropertyChangedEventHandler? PropertyChanged;

    private readonly AgentPresenceService _presence = AppServices.Presence;
    private int _onlineAgentCount;

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

    public ShellSidebar()
    {
        InitializeComponent();
        DataContext = this;
        _presence.PresenceChanged += OnPresenceChanged;
    }

    public async System.Threading.Tasks.Task RefreshAsync()
    {
        var agentsState = await AppServices.Agents.LoadAsync();
        var sessionsState = await AppServices.Sessions.LoadAsync();

        ShellAgents.Clear();
        foreach (var agent in agentsState.Agents)
        {
            var item = new ShellAgentItem
            {
                Id = agent.Id,
                Name = agent.Name,
                IsSelected = agent.Id == agentsState.SelectedAgentId,
                IsExpanded = agent.Id == agentsState.SelectedAgentId,
                Presence = _presence.GetPresence(agent.Id),
            };

            foreach (var session in sessionsState.Sessions
                         .Where(s => s.AgentId == agent.Id)
                         .OrderByDescending(s => s.UpdatedAt, StringComparer.Ordinal))
            {
                item.Sessions.Add(new ShellSessionItem
                {
                    Id = session.Id,
                    Title = session.Title,
                });
            }

            ShellAgents.Add(item);
        }

        UpdateOnlineAgentCount();
        foreach (var agent in agentsState.Agents)
        {
            _ = _presence.EnsureCheckedAsync(agent);
        }
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

    private async System.Threading.Tasks.Task SelectSessionAsync(string sessionId)
    {
        var sessionsState = await AppServices.Sessions.LoadAsync();
        var session = sessionsState.Sessions.FirstOrDefault(s => s.Id == sessionId);
        if (session is null) return;

        sessionsState.ActiveSessionId = session.Id;
        await AppServices.Sessions.SaveAsync(sessionsState);

        var agentsState = await AppServices.Agents.LoadAsync();
        agentsState.SelectedAgentId = session.AgentId;
        await AppServices.Agents.SaveAsync(agentsState);

        await RefreshAsync();
        RequestNavigation(typeof(ChatPage), forceReload: true);
    }

    private ShellAgentItem? FindShellAgent(string agentId)
        => ShellAgents.FirstOrDefault(agent => agent.Id == agentId);

    private async void ChatHome_Click(object sender, RoutedEventArgs e)
    {
        var sessionsState = await AppServices.Sessions.LoadAsync();
        var recent = sessionsState.Sessions
            .OrderByDescending(s => s.UpdatedAt, StringComparer.Ordinal)
            .FirstOrDefault();

        if (recent is not null)
        {
            await SelectSessionAsync(recent.Id);
            return;
        }

        await RefreshAsync();
        RequestNavigation(typeof(HomePage), forceReload: true);
    }

    private async void Chat_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
        RequestNavigation(typeof(ChatPage), forceReload: true);
    }

    private async void Agent_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string agentId)
        {
            var agentsState = await AppServices.Agents.LoadAsync();
            agentsState.SelectedAgentId = agentId;
            await AppServices.Agents.SaveAsync(agentsState);
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
        }
    }

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
        await RefreshAsync();
        RequestNavigation(typeof(ChatPage), forceReload: true);
    }

    private async void DeleteAgent_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string agentId) return;

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

    private async void Session_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string sessionId)
        {
            await SelectSessionAsync(sessionId);
        }
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
