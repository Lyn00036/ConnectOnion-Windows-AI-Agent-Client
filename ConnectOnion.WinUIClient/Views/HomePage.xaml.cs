using System;
using System.Collections.ObjectModel;
using System.Linq;
using ConnectOnion.WinUIClient.Models;
using ConnectOnion.WinUIClient.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ConnectOnion.WinUIClient.Views;

public sealed class HomeAgentItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Initial { get; set; } = "?";
    public string ShortAddress { get; set; } = "";
}

public sealed partial class HomePage : Page
{
    public ObservableCollection<HomeAgentItem> Agents { get; } = new();

    public HomePage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadAsync();
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
            });
        }
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
        => Frame.Navigate(typeof(AgentsPage));

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
}
