using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ConnectOnion.WinUIClient.Common;
using ConnectOnion.WinUIClient.Services;

namespace ConnectOnion.WinUIClient.Models;

/// <summary>
/// Lightweight projection for the desktop shell's agent -> session navigation.
/// </summary>
public sealed partial class ShellAgentItem : RowStateItem
{
    public string Id { get; set; } = "";
    /// <summary>The raw, stored agent identifier (e.g. <c>remote-admin-agent</c>). Kept
    /// verbatim for tooltips and to drive avatar/lookup logic; the sidebar shows
    /// <see cref="DisplayName"/> instead.</summary>
    public string Name { get; set; } = "";
    /// <summary>User-facing name derived from <see cref="Name"/> (e.g. <c>Remote Admin Agent</c>).
    /// Never persisted — the internal id is unchanged.</summary>
    public string DisplayName => FriendlyAgentName.From(Name);
    public string Initial => NameInitial.From(DisplayName);
    public int SessionCount => Sessions.Count;
    public ObservableCollection<ShellSessionItem> Sessions { get; } = new();

    public double ChevronAngle => IsExpanded ? 0 : 270;
    public bool IsOnline => Presence == AgentPresence.Online;
    /// <summary>A reachability probe is in flight — shown as a breathing neutral dot.</summary>
    public bool IsChecking => Presence == AgentPresence.Checking;
    /// <summary>Offline or never-probed — shown as a hollow gray dot.</summary>
    public bool IsOfflineIconVisible => Presence == AgentPresence.Offline || Presence == AgentPresence.Unknown;
    public string PresenceLabel => Presence switch
    {
        AgentPresence.Online => "Online",
        AgentPresence.Checking => "Connecting",
        AgentPresence.Offline => "Offline",
        _ => "Not connected",
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChevronAngle))]
    public partial bool IsExpanded { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOnline))]
    [NotifyPropertyChangedFor(nameof(IsChecking))]
    [NotifyPropertyChangedFor(nameof(IsOfflineIconVisible))]
    [NotifyPropertyChangedFor(nameof(PresenceLabel))]
    public partial AgentPresence Presence { get; set; }
}
