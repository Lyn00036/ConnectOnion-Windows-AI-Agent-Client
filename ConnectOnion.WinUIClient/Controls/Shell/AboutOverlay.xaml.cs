using System;
using ConnectOnion.WinUIClient.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace ConnectOnion.WinUIClient.Controls;

/// <summary>
/// In-app "About ConnectOnion" modal. Mirrors the <see cref="KeyboardShortcutsDialog"/> /
/// <see cref="SettingsOverlay"/> pattern — a window-filling overlay (not an OS window) with a
/// dimmed backdrop, a centered card, Esc / backdrop / close-button / OK dismissal, and focus
/// restored to the opener on close. It is a single persistent instance hosted by MainWindow, so
/// re-invoking the menu item re-focuses this one dialog rather than opening a second About.
/// Purely a UI layer: showing it neither touches page state nor pauses background runs.
/// </summary>
public sealed partial class AboutOverlay : UserControl
{
    private FrameworkElement? _focusReturnTarget;

    public event EventHandler? CloseRequested;

    /// <summary>Resolved at construction; the version can't change while the process runs.</summary>
    public string VersionText { get; } = AppVersionService.VersionText;

    public string CopyrightText { get; } = AppVersionService.CopyrightText;

    public AboutOverlay()
    {
        InitializeComponent();
        SizeChanged += (_, e) => UpdateModalSize(e.NewSize.Width, e.NewSize.Height);
    }

    /// <summary>Whether the dialog is currently shown (used to keep it single-instance).</summary>
    public bool IsOpen => Visibility == Visibility.Visible;

    public void Show(FrameworkElement? focusReturnTarget)
    {
        // Already open: keep the one instance, just move focus back into it.
        if (IsOpen)
        {
            DispatcherQueue.TryEnqueue(() => OkButton.Focus(FocusState.Programmatic));
            return;
        }

        _focusReturnTarget = focusReturnTarget;

        Visibility = Visibility.Visible;
        IsHitTestVisible = true;
        UpdateModalSize(ActualWidth, ActualHeight);
        DispatcherQueue.TryEnqueue(() => OkButton.Focus(FocusState.Programmatic));
    }

    public void Hide()
    {
        if (Visibility != Visibility.Visible) return;

        Visibility = Visibility.Collapsed;
        IsHitTestVisible = false;
        _focusReturnTarget?.Focus(FocusState.Programmatic);
        _focusReturnTarget = null;
    }

    /// <summary>Keeps the card inside the window: width shrinks on narrow windows, height is
    /// capped at ~80% so the content scrolls internally instead of overflowing off-screen.</summary>
    private void UpdateModalSize(double width, double height)
    {
        if (width <= 0 || height <= 0) return;

        var safeMargin = width >= 700 ? 64d : 40d;
        ModalContainer.Width = Math.Max(0, Math.Min(400, width - safeMargin));
        ModalContainer.MaxHeight = Math.Max(0, Math.Min(390, height * 0.8));
    }

    private void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void CloseButton_Click(object sender, RoutedEventArgs e) => RequestClose();

    private void OkButton_Click(object sender, RoutedEventArgs e) => RequestClose();

    private void Backdrop_Tapped(object sender, TappedRoutedEventArgs e) => RequestClose();

    private void ModalContainer_Tapped(object sender, TappedRoutedEventArgs e) => e.Handled = true;

    private void OverlayRoot_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape) return;
        e.Handled = true;
        RequestClose();
    }
}
