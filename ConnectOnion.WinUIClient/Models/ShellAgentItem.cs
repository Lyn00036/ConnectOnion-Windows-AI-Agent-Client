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
    public string Name { get; set; } = "";
    public string Initial => NameInitial.From(Name);
    public int SessionCount => Sessions.Count;
    public ObservableCollection<ShellSessionItem> Sessions { get; } = new();

    public double ChevronAngle => IsExpanded ? 0 : 270;
    public bool IsOnline => Presence == AgentPresence.Online;
    public bool IsOfflineIconVisible => Presence != AgentPresence.Online;
    public string PresenceLabel => Presence switch
    {
        AgentPresence.Online => "Online",
        AgentPresence.Checking => "Checking",
        AgentPresence.Offline => "Offline",
        _ => "Unknown",
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChevronAngle))]
    public partial bool IsExpanded { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOnline))]
    [NotifyPropertyChangedFor(nameof(IsOfflineIconVisible))]
    [NotifyPropertyChangedFor(nameof(PresenceLabel))]
    public partial AgentPresence Presence { get; set; }
}
