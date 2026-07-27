using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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
    public string Address { get; set; } = "";
    public string? DirectUrl { get; set; }
    /// <summary>The agent's chosen icon, relative to the data root. Null falls back to
    /// <see cref="Initial"/>.</summary>
    public string? IconPath { get; set; }
    /// <summary>True when this agent has an icon the "remove custom icon" action can remove.</summary>
    public bool HasCustomIcon => !string.IsNullOrWhiteSpace(IconPath);
    /// <summary>User-facing name derived from <see cref="Name"/> (e.g. <c>Remote Admin Agent</c>).
    /// Never persisted — the internal id is unchanged.</summary>
    public string DisplayName => FriendlyAgentName.From(Name);
    public string Initial => NameInitial.FromPair(DisplayName);
    public int AvatarPaletteIndex => AvatarPalette.From(Name);
    public string ConnectionTarget
        => !string.IsNullOrWhiteSpace(Address) ? Address : DirectUrl ?? "";
    public string CopyAccessibilityName => $"Copy address for {DisplayName}";
    public string DeleteAccessibilityName => $"Delete {DisplayName}";
    public string OpenAccessibilityName => $"Open {DisplayName}";
    public int SessionCount => Sessions.Count;
    public ObservableCollection<ShellSessionItem> Sessions { get; } = new();
    public ObservableCollection<ShellSessionGroup> SessionGroups { get; } = new();

    public void RebuildSessionGroups()
    {
        SessionGroups.Clear();
        foreach (var label in new[] { "Today", "Yesterday", "Earlier" })
        {
            var sessions = Sessions.Where(session => session.TimeGroup == label).ToList();
            if (sessions.Count > 0)
            {
                SessionGroups.Add(new ShellSessionGroup(label, sessions));
            }
        }
    }

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

public sealed class ShellSessionGroup
{
    public ShellSessionGroup(string label, IReadOnlyList<ShellSessionItem> sessions)
    {
        Label = label;
        foreach (var session in sessions)
        {
            Sessions.Add(session);
        }
    }

    public string Label { get; }
    public ObservableCollection<ShellSessionItem> Sessions { get; } = new();
}
