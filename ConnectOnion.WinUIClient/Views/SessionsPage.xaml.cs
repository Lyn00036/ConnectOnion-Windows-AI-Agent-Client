using System.Linq;
using ConnectOnion.WinUIClient.Models;
using ConnectOnion.WinUIClient.Services;
using ConnectOnion.WinUIClient.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ConnectOnion.WinUIClient.Views;

public sealed partial class SessionsPage : Page
{
    public SessionsViewModel Vm { get; } = App.GetService<SessionsViewModel>();

    // The agent new sessions are created under (the persisted selected agent).
    private AgentConfig? _currentAgent;

    public SessionsPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await Vm.LoadAsync();

        var agents = await AppServices.Agents.LoadAsync();
        _currentAgent = agents.Agents.FirstOrDefault(a => a.Id == agents.SelectedAgentId);

        var hasAgent = _currentAgent is not null;
        NewSessionButton.IsEnabled = hasAgent;
        AgentLabel.Text = hasAgent
            ? $"New conversations use: {_currentAgent!.Name}"
            : "Select an agent on the Agents page to start a conversation.";
    }

    private void NewSession_Click(object sender, RoutedEventArgs e)
    {
        if (_currentAgent is { } agent) Vm.StartNewSession(agent.Id);
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.Selected is { } session) await Vm.RemoveSessionAsync(session);
    }
}
