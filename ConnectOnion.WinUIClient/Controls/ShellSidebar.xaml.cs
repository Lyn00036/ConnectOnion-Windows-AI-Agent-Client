using System;
using System.Collections.ObjectModel;
using System.Linq;
using ConnectOnion.WinUIClient.Models;
using ConnectOnion.WinUIClient.Services;
using ConnectOnion.WinUIClient.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace ConnectOnion.WinUIClient.Controls;

public sealed partial class ShellSidebar : UserControl
{
    public ObservableCollection<ShellAgentItem> ShellAgents { get; } = new();

    public event Action<Type, bool>? NavigationRequested;

    private bool _addAgentConnectionTested;
    private string? _addAgentDetectedName;

    public ShellSidebar()
    {
        InitializeComponent();
        DataContext = this;
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
    }

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
        await EnsureSessionAsync(agentId);
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

    private async void EditAgent_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string agentId) return;

        var agentsState = await AppServices.Agents.LoadAsync();
        agentsState.SelectedAgentId = agentId;
        await AppServices.Agents.SaveAsync(agentsState);
        await RefreshAsync();
        RequestNavigation(typeof(AgentsPage));
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
            AddAgentAddressBox.Focus(FocusState.Programmatic);
        }
        await RefreshAsync();
    }

    private void CloseAddAgentPanel_Click(object sender, RoutedEventArgs e)
        => ResetAddAgentPanel(collapse: true);

    private void AddAgentField_TextChanged(object sender, TextChangedEventArgs e)
    {
        SetAddAgentConnectionTested(false);
        AddAgentStatusText.Visibility = Visibility.Collapsed;
    }

    private async void TestAddAgentConnection_Click(object sender, RoutedEventArgs e)
    {
        await TestAddAgentConnectionAsync();
    }

    private async void SubmitAddAgent_Click(object sender, RoutedEventArgs e)
    {
        var address = AddAgentAddressBox.Text.Trim();
        var directUrl = AddAgentDirectUrlBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(address) && string.IsNullOrWhiteSpace(directUrl))
        {
            ShowAddAgentStatus("Enter a 0x address or Direct URL.", isError: true);
            AddAgentAddressBox.Focus(FocusState.Programmatic);
            return;
        }

        if (!_addAgentConnectionTested)
        {
            ShowAddAgentStatus("Test connection successfully before adding this agent.", isError: true);
            return;
        }

        var name = _addAgentDetectedName ?? $"Agent {address[..Math.Min(8, address.Length)]}";

        var agentsState = await AppServices.Agents.LoadAsync();
        var agent = new AgentConfig
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            Address = address,
            DirectUrl = string.IsNullOrWhiteSpace(directUrl) ? null : directUrl,
        };

        agentsState.Agents.Add(agent);
        agentsState.SelectedAgentId = agent.Id;
        await AppServices.Agents.SaveAsync(agentsState);
        await EnsureSessionAsync(agent.Id);
        await RefreshAsync();

        // Fetch /info now so the detail page loads from cache instantly.
        await AgentInfoService.FetchAndPersistAsync(agent);
        ResetAddAgentPanel(collapse: true);
        RequestNavigation(typeof(ChatPage), forceReload: true);
    }

    private async System.Threading.Tasks.Task<bool> TestAddAgentConnectionAsync()
    {
        var address = AddAgentAddressBox.Text.Trim();
        var directUrl = AddAgentDirectUrlBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(address) && string.IsNullOrWhiteSpace(directUrl))
        {
            ShowAddAgentStatus("Enter a 0x address or Direct URL first.", isError: true);
            return false;
        }

        SetAddAgentBusy(true);
        try
        {
            var result = await ConnectionTester.TestAsync(address, directUrl);
            SetAddAgentConnectionTested(result.Ok);
            if (result.Ok && result.AgentName is not null)
                _addAgentDetectedName = result.AgentName;
            ShowAddAgentStatus(result.Detail, !result.Ok);
            return result.Ok;
        }
        finally
        {
            SetAddAgentBusy(false);
        }
    }

    private void SetAddAgentBusy(bool isBusy)
    {
        AddAgentTestButton.IsEnabled = !isBusy;
        AddAgentSubmitButton.IsEnabled = !isBusy && _addAgentConnectionTested;
        AddAgentTestRing.IsActive = isBusy;
        AddAgentTestRing.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetAddAgentConnectionTested(bool isTested)
    {
        _addAgentConnectionTested = isTested;
        AddAgentSubmitButton.IsEnabled = isTested;
    }

    private void ShowAddAgentStatus(string message, bool isError)
    {
        AddAgentStatusText.Text = message;
        AddAgentStatusText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
            isError ? "StatusErrorTextBrush" : "StatusSuccessTextBrush"];
        AddAgentStatusText.Visibility = Visibility.Visible;
    }

    private void ResetAddAgentPanel(bool collapse)
    {
        AddAgentAddressBox.Text = "";
        AddAgentDirectUrlBox.Text = "";
        _addAgentDetectedName = null;
        SetAddAgentConnectionTested(false);
        AddAgentStatusText.Visibility = Visibility.Collapsed;
        AddAgentTestRing.IsActive = false;
        AddAgentTestRing.Visibility = Visibility.Collapsed;
        AddAgentTestButton.IsEnabled = true;
        if (collapse)
        {
            AddAgentPanel.Visibility = Visibility.Collapsed;
        }
    }

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
        RequestNavigation(typeof(SettingsPage));
    }
}
