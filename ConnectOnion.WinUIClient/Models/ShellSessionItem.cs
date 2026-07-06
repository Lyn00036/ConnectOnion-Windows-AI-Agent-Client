using ConnectOnion.WinUIClient.Common;

namespace ConnectOnion.WinUIClient.Models;

/// <summary>
/// Lightweight projection for a conversation row in the desktop sidebar.
/// </summary>
public sealed class ShellSessionItem : ObservableObject
{
    private bool _areActionsVisible;
    private bool _isSelected;
    private bool _isPinned;

    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string AgentName { get; set; } = "";
    public string PinToolTip => IsPinned ? "Unpin chat" : "Pin chat";

    public bool AreActionsVisible { get => _areActionsVisible; set => SetProperty(ref _areActionsVisible, value); }
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
    public bool IsPinned
    {
        get => _isPinned;
        set
        {
            if (!SetProperty(ref _isPinned, value)) return;
            OnPropertyChanged(nameof(PinToolTip));
        }
    }
}
