using System;
using System.Collections.ObjectModel;
using System.Linq;
using ConnectOnion.WinUIClient.Common;
using ConnectOnion.WinUIClient.Models;
using ConnectOnion.WinUIClient.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ConnectOnion.WinUIClient.Views;

public sealed class HomeAgentItem : ObservableObject
{
    private bool _isOnline;

    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Initial { get; set; } = "?";
    public string ShortAddress { get; set; } = "";
    public bool IsOnline
    {
        get => _isOnline;
        set => SetProperty(ref _isOnline, value);
    }
}

public sealed partial class HomePage : Page
{
    private readonly AgentPresenceService _presence = AppServices.Presence;

    public ObservableCollection<HomeAgentItem> Agents { get; } = new();

    public HomePage()
    {
        InitializeComponent();
        Loaded += HomePage_Loaded;
        Unloaded += HomePage_Unloaded;
    }

    private async void HomePage_Loaded(object sender, RoutedEventArgs e)
    {
        _presence.PresenceChanged -= OnPresenceChanged;
        _presence.PresenceChanged += OnPresenceChanged;
        await LoadAsync();
    }

    private void HomePage_Unloaded(object sender, RoutedEventArgs e)
    {
        _presence.PresenceChanged -= OnPresenceChanged;
    }

    private async System.Threading.Tasks.Task LoadAsync()
    {
        var state = await AppServices.Agents.LoadAsync();
        Agents.Clear();

        foreach (var agent in state.Agents)
        {
            Agents.Add(new HomeAgentItem
            {
                Id = agent.Id,
                Name = agent.Name,
                Initial = string.IsNullOrWhiteSpace(agent.Name)
                    ? "?"
                    : agent.Name[..1].ToUpperInvariant(),
                ShortAddress = ShortenAddress(agent),
                IsOnline = _presence.GetPresence(agent.Id) == AgentPresence.Online,
            });
        }

        UpdateAddAgentLabels();
    }

    private void OnPresenceChanged(string agentId)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var item = Agents.FirstOrDefault(agent => agent.Id == agentId);
            if (item is not null)
            {
                item.IsOnline = _presence.GetPresence(agentId) == AgentPresence.Online;
            }
        });
    }

    private async void AgentCard_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string agentId) return;

        var agentsState = await AppServices.Agents.LoadAsync();
        if (agentsState.Agents.All(agent => agent.Id != agentId)) return;

        agentsState.SelectedAgentId = agentId;
        await AppServices.Agents.SaveAsync(agentsState);
        await EnsureSessionAsync(agentId);
        Frame.Navigate(typeof(ChatPage));
    }

    private void AddAnotherAgent_Click(object sender, RoutedEventArgs e)
    {
        AddAgentPanel.Visibility = AddAgentPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (AddAgentPanel.Visibility == Visibility.Visible)
        {
            AddAgentPanel.FocusFirstField();
        }
    }

    private void AddAgentForm_CloseRequested()
        => AddAgentPanel.Visibility = Visibility.Collapsed;

    private async void AddAgentForm_AgentAdded(AgentConfig _)
    {
        AddAgentPanel.Visibility = Visibility.Collapsed;
        await LoadAsync();
        Frame.Navigate(typeof(AgentDetailPage));
    }

    private static async System.Threading.Tasks.Task EnsureSessionAsync(string agentId)
    {
        var sessionsState = await AppServices.Sessions.LoadAsync();
        var active = sessionsState.Sessions.FirstOrDefault(s => s.Id == sessionsState.ActiveSessionId && s.AgentId == agentId);
        if (active is not null) return;

        var recent = sessionsState.Sessions
            .Where(s => s.AgentId == agentId)
            .OrderByDescending(s => s.UpdatedAt, StringComparer.Ordinal)
            .FirstOrDefault();
        if (recent is not null)
        {
            sessionsState.ActiveSessionId = recent.Id;
            await AppServices.Sessions.SaveAsync(sessionsState);
            return;
        }

        var timestamp = DateTime.UtcNow.ToString("o");
        var count = sessionsState.Sessions.Count(s => s.AgentId == agentId) + 1;
        sessionsState.Sessions.Add(new SessionSummary
        {
            Id = Guid.NewGuid().ToString(),
            AgentId = agentId,
            Title = $"Conversation {count}",
            CreatedAt = timestamp,
            UpdatedAt = timestamp,
        });
        sessionsState.ActiveSessionId = sessionsState.Sessions[^1].Id;
        await AppServices.Sessions.SaveAsync(sessionsState);
    }

    private static string ShortenAddress(AgentConfig agent)
    {
        var value = string.IsNullOrWhiteSpace(agent.Address)
            ? agent.DirectUrl ?? ""
            : agent.Address;

        if (value.Length <= 14) return value;
        return $"{value[..8]}...{value[^4..]}";
    }

    private void UpdateAddAgentLabels()
    {
        var label = Agents.Count > 0 ? "Add another agent" : "Add agent";
        AddAgentActionLabel.Text = label;
        AddAgentPanel.FormTitle = label;
        AddAgentPanel.SubmitLabel = label;
    }
}
