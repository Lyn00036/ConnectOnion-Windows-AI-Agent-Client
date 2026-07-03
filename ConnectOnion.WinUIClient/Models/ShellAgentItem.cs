using System.Collections.ObjectModel;
using ConnectOnion.WinUIClient.Common;

namespace ConnectOnion.WinUIClient.Models;

/// <summary>
/// Lightweight projection for the desktop shell's agent -> session navigation.
/// </summary>
public sealed class ShellAgentItem : ObservableObject
{
    private bool _isExpanded;
    private bool _isSelected;
    private bool _areActionsVisible;

    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Initial => string.IsNullOrWhiteSpace(Name) ? "?" : Name[..1].ToUpperInvariant();
    public int SessionCount => Sessions.Count;
    public ObservableCollection<ShellSessionItem> Sessions { get; } = new();

    public double ChevronAngle => IsExpanded ? 90 : 0;

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (!SetProperty(ref _isExpanded, value)) return;
            OnPropertyChanged(nameof(ChevronAngle));
        }
    }
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
    public bool AreActionsVisible { get => _areActionsVisible; set => SetProperty(ref _areActionsVisible, value); }
}
