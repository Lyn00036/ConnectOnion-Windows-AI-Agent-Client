using CommunityToolkit.Mvvm.ComponentModel;
using ConnectOnion.WinUIClient.Common;

namespace ConnectOnion.WinUIClient.Models;

/// <summary>
/// Lightweight projection for a conversation row in the desktop sidebar.
/// </summary>
public sealed partial class ShellSessionItem : RowStateItem
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    /// <summary>Raw owning-agent id; <see cref="AgentDisplayName"/> is what the pinned row shows.</summary>
    public string AgentName { get; set; } = "";
    public string AgentDisplayName => FriendlyAgentName.From(AgentName);
    public string PinToolTip => IsPinned ? "Unpin chat" : "Pin chat";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PinToolTip))]
    public partial bool IsPinned { get; set; }
}
