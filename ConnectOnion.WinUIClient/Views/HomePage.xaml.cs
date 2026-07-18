using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using ConnectOnion.WinUIClient.Common;
using ConnectOnion.WinUIClient.Models;
using ConnectOnion.WinUIClient.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ConnectOnion.WinUIClient.Views;

/// <summary>
/// One agent tile on the landing page. A view-only projection of <see cref="AgentConfig"/> —
/// the display strings are computed once at load, and only <see cref="IsOnline"/> is
/// observable, because presence is the sole field that changes while the page is open.
/// </summary>
public sealed partial class HomeAgentItem : Common.ObservableObject
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Initial { get; set; } = "?";
    public string ShortAddress { get; set; } = "";

    [ObservableProperty]
    public partial bool IsOnline { get; set; }
}

/// <summary>
/// The landing page: every configured agent as a tile, plus the inline add-agent form.
/// Clicking a tile makes that agent current, guarantees it has a conversation, and jumps
/// to the chat.
///
/// Unlike the rest of the app this page has no view model — it predates that convention and
/// reaches <c>AppServices</c> directly. New pages should take an injected view model instead
/// (see the DI notes in CLAUDE.md).
/// </summary>
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
        // Unsubscribe-then-subscribe: Loaded fires again every time the user navigates back
        // to this page, and the presence service outlives the page, so a plain += would stack
        // a new handler per visit and update each tile N times.
        _presence.PresenceChanged -= OnPresenceChanged;
        _presence.PresenceChanged += OnPresenceChanged;
        await LoadAsync();
    }

    private void HomePage_Unloaded(object sender, RoutedEventArgs e)
    {
        // Detaching here is what keeps the page collectable — the singleton presence service
        // would otherwise hold every HomePage ever navigated to alive through this handler.
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
                Initial = NameInitial.From(agent.Name),
                ShortAddress = ShortenAddress(agent),
                IsOnline = _presence.GetPresence(agent.Id) == AgentPresence.Online,
            });
        }

        UpdateAddAgentLabels();
    }

    /// <summary>Presence sweeps run on a background thread, so every touch of the observable
    /// item has to be marshalled — binding updates off the UI thread throw.</summary>
    private void OnPresenceChanged(string agentId)
    {
        // TryEnqueue (not the throwing overload): if the window is closing the queue is gone,
        // and a dropped presence update is the correct outcome, not an exception on a
        // background thread with no handler.
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

        // Re-load and re-check rather than trusting the tile: the agent could have been
        // deleted from another surface (sidebar, settings) since this page last loaded, and
        // navigating to a chat for a row that no longer exists strands the user on an empty
        // page. Bailing silently is right — the tile is about to disappear anyway.
        var agentsState = await AppServices.Agents.LoadAsync();
        if (agentsState.Agents.All(agent => agent.Id != agentId)) return;

        agentsState.SelectedAgentId = agentId;
        await AppServices.Agents.SaveAsync(agentsState);
        await EnsureSessionAsync(agentId);
        Frame.Navigate(typeof(ChatPage));
    }

    private void AddAnotherAgent_Click(object sender, RoutedEventArgs e)
        => AddAgentPanel.Show();

    private async void AddAgentForm_AgentAdded(AgentConfig _)
    {
        await LoadAsync();
        Frame.Navigate(typeof(AgentDetailPage));
    }

    /// <summary>
    /// Guarantees the agent has a current conversation before the chat page opens, so
    /// <c>ChatPage</c> never has to handle "selected agent, no session".
    ///
    /// NOTE: the selection rule here (keep the active conversation if it belongs to this
    /// agent, else the most recently updated one) is a duplicate of
    /// <see cref="SessionSelection.FindExisting"/>, which exists precisely to be the one
    /// testable copy. This should call it; the two will drift otherwise.
    /// </summary>
    private static async System.Threading.Tasks.Task EnsureSessionAsync(string agentId)
    {
        var sessionsState = await AppServices.Sessions.LoadAsync();
        var active = sessionsState.Sessions.FirstOrDefault(s => s.Id == sessionsState.ActiveSessionId && s.AgentId == agentId);
        if (active is not null) return;

        // Ordinal sort over ISO-8601 ("o" format) timestamps — lexicographic order is
        // chronological order, so this ranks without parsing every row.
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

        var session = SessionSummary.NewConversation(agentId, sessionsState.Sessions);
        sessionsState.Sessions.Add(session);
        sessionsState.ActiveSessionId = session.Id;
        await AppServices.Sessions.SaveAsync(sessionsState);
    }

    /// <summary>Subtitle for a tile: the relay address if there is one, else the direct URL.
    /// Both are far too long to show whole, so they are elided in the middle — the leading
    /// and trailing characters are what a user actually scans to tell two agents apart.</summary>
    private static string ShortenAddress(AgentConfig agent)
    {
        // An agent is configured by address *or* by direct URL, never neither; the ?? "" is
        // for a malformed row rather than a real case.
        var value = string.IsNullOrWhiteSpace(agent.Address)
            ? agent.DirectUrl ?? ""
            : agent.Address;

        // 14 is the break-even: the elided form ("8 chars" + "..." + "4 chars") is 15, so
        // shortening anything at or under 14 would make it longer.
        if (value.Length <= 14) return value;
        return $"{value[..8]}...{value[^4..]}";
    }

    /// <summary>The add-agent affordance is worded for whether this is the user's first
    /// agent, which is why all three labels are set from one place instead of in XAML.</summary>
    private void UpdateAddAgentLabels()
    {
        var label = Agents.Count > 0 ? "Add another agent" : "Add agent";
        AddAgentActionLabel.Text = label;
        AddAgentPanel.FormTitle = label;
        AddAgentPanel.SubmitLabel = label;
    }
}
