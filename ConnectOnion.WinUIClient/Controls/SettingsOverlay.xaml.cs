using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace ConnectOnion.WinUIClient.Controls;

public sealed partial class SettingsOverlay : UserControl
{
    private FrameworkElement? _focusReturnTarget;
    public event EventHandler? CloseRequested;

    public SettingsOverlay()
    {
        InitializeComponent();
        SizeChanged += (_, e) => UpdateModalSize(e.NewSize.Width, e.NewSize.Height);
    }

    /// <summary>Whether the settings modal is currently shown.</summary>
    public bool IsOpen => Visibility == Visibility.Visible;

    public void Show(FrameworkElement? focusReturnTarget)
    {
        _focusReturnTarget = focusReturnTarget;
        GeneralNav.IsChecked = true;
        CategoryTitle.Text = "General";
        SearchBox.Text = string.Empty;
        SettingsContent.SelectCategory("General");
        Visibility = Visibility.Visible;
        IsHitTestVisible = true;
        UpdateModalSize(ActualWidth, ActualHeight);
        DispatcherQueue.TryEnqueue(() => SearchBox.Focus(FocusState.Programmatic));
    }

    public void Hide()
    {
        if (Visibility != Visibility.Visible) return;
        Visibility = Visibility.Collapsed;
        IsHitTestVisible = false;
        _focusReturnTarget?.Focus(FocusState.Programmatic);
        _focusReturnTarget = null;
    }

    private void UpdateModalSize(double width, double height)
    {
        if (width <= 0 || height <= 0) return;
        var safeMargin = width >= 860 ? 64d : 48d;
        ModalContainer.Width = Math.Max(0, Math.Min(1100, Math.Min(width * 0.82, width - safeMargin)));
        ModalContainer.Height = Math.Max(0, Math.Min(820, Math.Min(height * 0.86, height - safeMargin)));
    }

    private void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
    private void CloseButton_Click(object sender, RoutedEventArgs e) => RequestClose();
    private void Backdrop_Tapped(object sender, TappedRoutedEventArgs e) => RequestClose();
    private void ModalContainer_Tapped(object sender, TappedRoutedEventArgs e) => e.Handled = true;
    private void OverlayRoot_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape) return;
        e.Handled = true;
        RequestClose();
    }
    private void GeneralNav_Click(object sender, RoutedEventArgs e) => SelectCategory("General");
    private void NotificationsNav_Click(object sender, RoutedEventArgs e) => SelectCategory("Notifications");
    private void AudioNav_Click(object sender, RoutedEventArgs e) => SelectCategory("Audio");
    private void IdentityNav_Click(object sender, RoutedEventArgs e) => SelectCategory("Identity");
    private void SelectCategory(string category)
    {
        CategoryTitle.Text = category;
        var isIdentity = category == "Identity";
        SettingsContent.Visibility = isIdentity ? Visibility.Collapsed : Visibility.Visible;
        IdentityScrollViewer.Visibility = isIdentity ? Visibility.Visible : Visibility.Collapsed;
        if (!isIdentity)
            SettingsContent.SelectCategory(category);
    }
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => SettingsContent.Filter(SearchBox.Text);
}
