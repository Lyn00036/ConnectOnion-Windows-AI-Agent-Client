using System.Collections.ObjectModel;
using ConnectOnion.WinUIClient.Common;
using ConnectOnion.WinUIClient.Services;

namespace ConnectOnion.WinUIClient.Models;

/// <summary>
/// Lightweight projection for the desktop shell's agent -> session navigation.
/// </summary>
public sealed class ShellAgentItem : ObservableObject
{
    private bool _isExpanded;
    private bool _isSelected;
    private bool _isHovered;
    private bool _areActionsVisible;
    private AgentPresence _presence;

    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Initial => string.IsNullOrWhiteSpace(Name) ? "?" : Name[..1].ToUpperInvariant();
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

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (!SetProperty(ref _isExpanded, value)) return;
            OnPropertyChanged(nameof(ChevronAngle));
        }
    }
    public bool IsSelected
    {
        get => _isSelected;
        set { if (SetProperty(ref _isSelected, value)) OnPropertyChanged(nameof(ShowRowBackground)); }
    }
    public bool IsHovered
    {
        get => _isHovered;
        set { if (SetProperty(ref _isHovered, value)) OnPropertyChanged(nameof(ShowRowBackground)); }
    }
    /// <summary>True when the row should paint its unified background layer (selected and/or hovered) — see <c>RowStateToBackgroundConverter</c>.</summary>
    public bool ShowRowBackground => IsSelected || IsHovered;
    public bool AreActionsVisible { get => _areActionsVisible; set => SetProperty(ref _areActionsVisible, value); }
    public AgentPresence Presence
    {
        get => _presence;
        set
        {
            if (!SetProperty(ref _presence, value)) return;
            OnPropertyChanged(nameof(IsOnline));
            OnPropertyChanged(nameof(IsOfflineIconVisible));
            OnPropertyChanged(nameof(PresenceLabel));
        }
    }
}
