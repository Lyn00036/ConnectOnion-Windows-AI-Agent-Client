using CommunityToolkit.Mvvm.ComponentModel;

namespace ConnectOnion.WinUIClient.Models;

/// <summary>
/// Lightweight projection for a conversation row in the desktop sidebar.
/// </summary>
public sealed partial class ShellSessionItem : RowStateItem
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string AgentName { get; set; } = "";
    public string PinToolTip => IsPinned ? "Unpin chat" : "Pin chat";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PinToolTip))]
    public partial bool IsPinned { get; set; }
}
