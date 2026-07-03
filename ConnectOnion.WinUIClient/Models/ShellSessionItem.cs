using ConnectOnion.WinUIClient.Common;

namespace ConnectOnion.WinUIClient.Models;

/// <summary>
/// Lightweight projection for a conversation row in the desktop sidebar.
/// </summary>
public sealed class ShellSessionItem : ObservableObject
{
    private bool _areActionsVisible;

    public string Id { get; set; } = "";
    public string Title { get; set; } = "";

    public bool AreActionsVisible { get => _areActionsVisible; set => SetProperty(ref _areActionsVisible, value); }
}
