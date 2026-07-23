using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using ConnectOnion.WinUIClient.Models;
using ConnectOnion.WinUIClient.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ConnectOnion.WinUIClient.Controls;

/// <summary>
/// Settings → Agents. This is a projection over the same repositories, presence service,
/// add-agent overlay, and deletion path the shell already owns; it does not introduce a second
/// source of truth for connected agents.
/// </summary>
public sealed partial class AgentsSettingsContent : UserControl
{
    public ObservableCollection<ShellAgentItem> Agents { get; } = new();

    public AgentsSettingsContent()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public void Refresh() => _ = RefreshAsync();

    public async Task RefreshAsync()
    {
        try
        {
            var state = await AppServices.Agents.LoadAsync();
            Agents.Clear();
            foreach (var agent in state.Agents)
            {
                var item = new ShellAgentItem
                {
                    Id = agent.Id,
                    Name = agent.Name,
                    Address = agent.Address,
                    DirectUrl = agent.DirectUrl,
                    Presence = AppServices.Presence.GetPresence(agent.Id),
                };
                Agents.Add(item);
                _ = AppServices.Presence.EnsureCheckedAsync(agent);
            }

            var hasAgents = Agents.Count > 0;
            AgentListCard.Visibility = hasAgents ? Visibility.Visible : Visibility.Collapsed;
            EmptyState.Visibility = hasAgents ? Visibility.Collapsed : Visibility.Visible;
        }
        catch
        {
            // The list is a convenience projection; a transient database read failure must not
            // bring down Settings. Re-entering the category retries the load.
        }
    }

    private void AddAgent_Click(object sender, RoutedEventArgs e)
        => MainWindow.FromXamlRoot(XamlRoot)?.ShowAddAgentOverlay(sender as FrameworkElement);

    private void CopyAgent_Click(object sender, RoutedEventArgs e)
        => ClipboardService.CopyText((sender as FrameworkElement)?.Tag as string);

    private async void DeleteAgent_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string agentId) return;
        var host = MainWindow.FromXamlRoot(XamlRoot);
        if (host is null || !await host.DeleteAgentAsync(agentId)) return;
        await RefreshAsync();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AppServices.Presence.PresenceChanged -= OnPresenceChanged;
        AppServices.Presence.PresenceChanged += OnPresenceChanged;
        Refresh();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
        => AppServices.Presence.PresenceChanged -= OnPresenceChanged;

    private void OnPresenceChanged(string agentId)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var item = Agents.FirstOrDefault(agent => agent.Id == agentId);
            if (item is not null)
                item.Presence = AppServices.Presence.GetPresence(agentId);
        });
    }
}
