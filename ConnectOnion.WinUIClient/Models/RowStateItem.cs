using ConnectOnion.WinUIClient.Common;

namespace ConnectOnion.WinUIClient.Models;

/// <summary>
/// Shared selection/hover/action-visibility state for the desktop sidebar's
/// agent and session rows. A single <c>RowStateToBackground</c>-driven Border
/// per row reads <see cref="ShowRowBackground"/>; hover and keyboard focus both
/// drive <see cref="SetInteractive"/>.
/// </summary>
public abstract class RowStateItem : ObservableObject
{
    private bool _isSelected;
    private bool _isHovered;
    private bool _areActionsVisible;

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

    public bool AreActionsVisible
    {
        get => _areActionsVisible;
        set => SetProperty(ref _areActionsVisible, value);
    }

    /// <summary>True when the row should paint its unified background layer (selected and/or hovered) — see <c>RowStateToBackgroundConverter</c>.</summary>
    public bool ShowRowBackground => IsSelected || IsHovered;

    /// <summary>Enter/leave the interactive (hovered or focused) state: reveal the
    /// row's action buttons and light up its hover background together.</summary>
    public void SetInteractive(bool active)
    {
        AreActionsVisible = active;
        IsHovered = active;
    }
}
