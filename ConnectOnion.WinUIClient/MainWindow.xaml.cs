using System;
using System.Collections.ObjectModel;
using System.Linq;
using ConnectOnion.WinUIClient.Services;
using ConnectOnion.WinUIClient.Models;
using ConnectOnion.WinUIClient.Views;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.UI;

namespace ConnectOnion.WinUIClient;

/// <summary>
/// Shell window with a custom WinUI title bar and an agent -> session sidebar.
/// </summary>
public sealed partial class MainWindow : Window
{
    public ObservableCollection<ShellAgentItem> ShellAgents { get; } = new();
    private readonly AppWindow _appWindow;

    public MainWindow()
    {
        InitializeComponent();
        _appWindow = GetAppWindow();
        ConfigureCustomTitleBar();
        ThemeService.ThemeApplied += ApplyTitleBarTheme;
        Closed += (_, _) => ThemeService.ThemeApplied -= ApplyTitleBarTheme;
        RootGrid.DataContext = this;
        ThemeService.RegisterRoot(RootGrid);
        _ = ApplyStartupThemeAsync();
        RootGrid.Loaded += async (_, _) =>
        {
            await RefreshShellAsync();
            NavigateTo(typeof(ChatPage));
        };
    }

    private AppWindow GetAppWindow()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        return AppWindow.GetFromWindowId(windowId);
    }

    private void ConfigureCustomTitleBar()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarDragRegion);
        _appWindow.Title = "ConnectOnion";
        _appWindow.SetIcon("Assets\\icon.ico");
        ApplyTitleBarTheme(ThemeMode.Light);
    }

    private void ApplyTitleBarTheme(ThemeMode theme)
    {
        var titleBar = _appWindow.TitleBar;
        titleBar.BackgroundColor = Colors.Transparent;
        titleBar.InactiveBackgroundColor = Colors.Transparent;
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;

        if (theme == ThemeMode.Dark)
        {
            titleBar.ForegroundColor = Color.FromArgb(255, 245, 245, 247);
            titleBar.InactiveForegroundColor = Color.FromArgb(255, 146, 146, 154);
            titleBar.ButtonForegroundColor = Color.FromArgb(255, 245, 245, 247);
            titleBar.ButtonInactiveForegroundColor = Color.FromArgb(255, 146, 146, 154);
            titleBar.ButtonHoverBackgroundColor = Color.FromArgb(255, 50, 50, 56);
            titleBar.ButtonHoverForegroundColor = Color.FromArgb(255, 255, 255, 255);
            titleBar.ButtonPressedBackgroundColor = Color.FromArgb(255, 68, 68, 76);
            titleBar.ButtonPressedForegroundColor = Color.FromArgb(255, 255, 255, 255);
            return;
        }

        titleBar.ForegroundColor = Color.FromArgb(255, 48, 49, 54);
        titleBar.InactiveForegroundColor = Color.FromArgb(255, 120, 120, 126);
        titleBar.ButtonForegroundColor = Color.FromArgb(255, 48, 49, 54);
        titleBar.ButtonInactiveForegroundColor = Color.FromArgb(255, 120, 120, 126);
        titleBar.ButtonHoverBackgroundColor = Color.FromArgb(255, 232, 232, 234);
        titleBar.ButtonHoverForegroundColor = Color.FromArgb(255, 20, 20, 24);
        titleBar.ButtonPressedBackgroundColor = Color.FromArgb(255, 215, 215, 218);
        titleBar.ButtonPressedForegroundColor = Color.FromArgb(255, 20, 20, 24);
    }

    private async System.Threading.Tasks.Task ApplyStartupThemeAsync()
    {
        var prefs = await AppServices.Preferences.LoadAsync();
        ThemeService.Apply(prefs.Theme);
    }

    private void NavigateTo(Type page, bool forceReload = false)
    {
        if (forceReload || ContentFrame.CurrentSourcePageType != page)
        {
            ContentFrame.Navigate(page);
        }
    }

    private async System.Threading.Tasks.Task RefreshShellAsync()
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
        await RefreshShellAsync();
        NavigateTo(typeof(ChatPage), forceReload: true);
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

        await RefreshShellAsync();
        NavigateTo(typeof(ChatPage), forceReload: true);
    }

    private ShellAgentItem? FindShellAgent(string agentId)
        => ShellAgents.FirstOrDefault(agent => agent.Id == agentId);

    private async void ChatHome_Click(object sender, RoutedEventArgs e)
    {
        await RefreshShellAsync();
        NavigateTo(typeof(ChatPage), forceReload: true);
    }

    private async void Agent_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string agentId)
        {
            await SelectAgentAsync(agentId);
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

    private async void AddChat_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string agentId) return;

        var agentsState = await AppServices.Agents.LoadAsync();
        agentsState.SelectedAgentId = agentId;
        await AppServices.Agents.SaveAsync(agentsState);
        await CreateSessionAsync(agentId);
        await RefreshShellAsync();
        NavigateTo(typeof(ChatPage), forceReload: true);
    }

    private async void EditAgent_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string agentId) return;

        var agentsState = await AppServices.Agents.LoadAsync();
        agentsState.SelectedAgentId = agentId;
        await AppServices.Agents.SaveAsync(agentsState);
        await RefreshShellAsync();
        NavigateTo(typeof(AgentsPage));
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
            XamlRoot = RootGrid.XamlRoot,
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

        await RefreshShellAsync();
        NavigateTo(typeof(ChatPage), forceReload: true);
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
            XamlRoot = RootGrid.XamlRoot,
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

        await RefreshShellAsync();
        NavigateTo(typeof(ChatPage), forceReload: true);
    }

    private async void AddAgent_Click(object sender, RoutedEventArgs e)
    {
        await RefreshShellAsync();
        NavigateTo(typeof(AgentsPage));
    }

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        await RefreshShellAsync();
        NavigateTo(typeof(SettingsPage));
    }
}
