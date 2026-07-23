using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using ConnectOnion.WinUIClient.Common;
using ConnectOnion.WinUIClient.Models;
using ConnectOnion.WinUIClient.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ConnectOnion.WinUIClient.Views;

/// <summary>A compact, view-only projection of one saved agent.</summary>
public sealed partial class HomeAgentItem : Common.ObservableObject
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Initial { get; set; } = "?";
    public string ShortAddress { get; set; } = "";
    public string FullAddress { get; set; } = "";
    public int AvatarPaletteIndex { get; set; }
    public string AutomationName => $"Open {Name}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChecking))]
    [NotifyPropertyChangedFor(nameof(StatusLabel))]
    [NotifyPropertyChangedFor(nameof(StatusAutomationName))]
    [NotifyPropertyChangedFor(nameof(IsStandardStatus))]
    public partial AgentPresence Presence { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusLabel))]
    [NotifyPropertyChangedFor(nameof(StatusAutomationName))]
    [NotifyPropertyChangedFor(nameof(IsStandardStatus))]
    public partial bool IsUnavailable { get; set; }

    [ObservableProperty]
    public partial bool IsOpening { get; set; }

    [ObservableProperty]
    public partial bool IsInteractionEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsHighlighted { get; set; }

    public bool IsChecking => Presence is AgentPresence.Unknown or AgentPresence.Checking;
    public bool IsStandardStatus => !IsChecking && !IsUnavailable;

    public string StatusLabel => IsUnavailable ? "Unavailable" : Presence switch
    {
        AgentPresence.Online => "Online",
        AgentPresence.Offline => "Offline",
        _ => "Checking",
    };

    public string StatusAutomationName => $"{Name}: {StatusLabel}";
}

/// <summary>
/// Agent picker. The virtualized list owns vertical scrolling; add-agent is a transient dialog.
/// </summary>
public sealed partial class HomePage : Page, IReloadablePage, IDisposable
{
    private readonly AgentPresenceService _presence = AppServices.Presence;
    private readonly CancellationTokenSource _lifetimeCts = new();
    private bool _isOpeningAgent;
    private int _disposed;

    public ObservableCollection<HomeAgentItem> Agents { get; } = new();

    public HomePage()
    {
        InitializeComponent();
        Loaded += HomePage_Loaded;
        Unloaded += HomePage_Unloaded;
    }

    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        Dispose();
        base.OnNavigatedFrom(e);
    }

    public async Task ReloadAsync()
    {
        if (_disposed != 0) return;
        try { await LoadAsync(_lifetimeCts.Token); }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested) { return; }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _presence.PresenceChanged -= OnPresenceChanged;
        _lifetimeCts.Cancel();
        _lifetimeCts.Dispose();
        Loaded -= HomePage_Loaded;
        Unloaded -= HomePage_Unloaded;

        Agents.Clear();
    }

    private async void HomePage_Loaded(object sender, RoutedEventArgs e)
    {
        _presence.PresenceChanged -= OnPresenceChanged;
        _presence.PresenceChanged += OnPresenceChanged;
        try { await LoadAsync(_lifetimeCts.Token); }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested) { return; }
    }

    private void HomePage_Unloaded(object sender, RoutedEventArgs e)
    {
        _presence.PresenceChanged -= OnPresenceChanged;
        Dispose();
    }

    private async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var state = await AppServices.Agents.LoadAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        Agents.Clear();

        foreach (var agent in state.Agents)
        {
            var item = new HomeAgentItem
            {
                Id = agent.Id,
                Name = agent.Name,
                Initial = NameInitial.From(agent.Name),
                ShortAddress = ShortenAddress(agent),
                FullAddress = GetAddress(agent),
                AvatarPaletteIndex = AvatarPalette.From(agent.Name),
                IsInteractionEnabled = true,
            };
            ApplyPresence(item);
            Agents.Add(item);

            _ = _presence.EnsureCheckedAsync(agent);
        }
    }

    private void OnPresenceChanged(string agentId)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var item = Agents.FirstOrDefault(agent => agent.Id == agentId);
            if (item is not null) ApplyPresence(item);
        });
    }

    private void ApplyPresence(HomeAgentItem item)
    {
        var presence = _presence.GetPresence(item.Id);
        item.Presence = presence;
        item.IsUnavailable = presence == AgentPresence.Offline &&
            !_presence.GetDetail(item.Id).Contains("currently offline", StringComparison.OrdinalIgnoreCase);
    }

    private async void AgentCard_Click(object sender, RoutedEventArgs e)
    {
        if (_isOpeningAgent || (sender as FrameworkElement)?.Tag is not string agentId) return;

        _isOpeningAgent = true;
        PageErrorBar.IsOpen = false;
        var selected = Agents.FirstOrDefault(agent => agent.Id == agentId);
        foreach (var item in Agents) item.IsInteractionEnabled = false;
        if (selected is not null) selected.IsOpening = true;

        try
        {
            var agentsState = await AppServices.Agents.LoadAsync(_lifetimeCts.Token);
            if (agentsState.Agents.All(agent => agent.Id != agentId))
                throw new InvalidOperationException("This agent is no longer available.");

            agentsState.SelectedAgentId = agentId;
            await AppServices.Agents.SaveAsync(agentsState, _lifetimeCts.Token);
            await EnsureSessionAsync(agentId);
            Frame.Navigate(typeof(ChatPage));
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            PageErrorBar.Message = ex.Message;
            PageErrorBar.IsOpen = true;
        }
        finally
        {
            _isOpeningAgent = false;
            if (_disposed == 0)
            {
                foreach (var item in Agents) item.IsInteractionEnabled = true;
                if (selected is not null) selected.IsOpening = false;
            }
        }
    }

    private void AddAnotherAgent_Click(object sender, RoutedEventArgs e)
        => MainWindow.FromXamlRoot(XamlRoot)?.ShowAddAgentOverlay(sender as FrameworkElement);

    public async Task RefreshAfterAgentAddedAsync(string agentId)
    {
        if (_disposed != 0) return;
        try { await LoadAsync(_lifetimeCts.Token); }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested) { return; }

        var item = Agents.FirstOrDefault(agent => agent.Id == agentId);
        if (item is null) return;
        item.IsHighlighted = true;
        try { await Task.Delay(TimeSpan.FromSeconds(1.6), _lifetimeCts.Token); }
        finally { if (!_lifetimeCts.IsCancellationRequested) item.IsHighlighted = false; }
    }

    private static async Task EnsureSessionAsync(string agentId)
    {
        var sessionsState = await AppServices.Sessions.LoadAsync();
        var recent = SessionSelection.FindExisting(
            sessionsState.Sessions, sessionsState.ActiveSessionId, agentId);
        if (recent?.Id == sessionsState.ActiveSessionId) return;

        if (recent is not null)
        {
            sessionsState.ActiveSessionId = recent.Id;
            await AppServices.Sessions.SaveAsync(sessionsState);
            return;
        }

        var session = SessionSummary.NewConversation(agentId, sessionsState.Sessions);
        sessionsState.Sessions.Add(session);
        sessionsState.ActiveSessionId = session.Id;
        await AppServices.Sessions.SaveAsync(sessionsState);
    }

    private static string ShortenAddress(AgentConfig agent)
    {
        var value = GetAddress(agent);
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
        {
            var authority = uri.IsDefaultPort ? uri.IdnHost : $"{uri.IdnHost}:{uri.Port}";
            var path = uri.AbsolutePath.TrimEnd('/');
            return string.IsNullOrEmpty(path) ? authority : $"{authority}{path}";
        }

        if (value.Length <= 20) return value;
        return $"{value[..10]}…{value[^6..]}";
    }

    private static string GetAddress(AgentConfig agent)
        => string.IsNullOrWhiteSpace(agent.Address) ? agent.DirectUrl ?? "" : agent.Address;

}
