using System;
using System.Diagnostics;
using ConnectOnion.WinUIClient.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace ConnectOnion.WinUIClient;

public sealed partial class MainWindow
{
    private const double ZoomStep = 0.1;
    private const double MinZoom = 0.8;
    private const double MaxZoom = 1.4;
    private const double DefaultZoom = 1.0;
    private const double ZoomPopupMarginFromCaption = 8;
    private const double ZoomPopupTopOffset = 18;
    private const double FindOverlayWidth = 380;
    private const double FindOverlayMinWidth = 280;
    private const double FindOverlayMarginFromCaption = 8;
    private const double FindOverlayTopOffset = 18;
    private static readonly TimeSpan ZoomPopupIdleTimeout = TimeSpan.FromSeconds(3);

    private double _zoomFactor = 1.0;
    private bool _isFullScreen;
    private bool _isUpdatingFindText;
    private Views.IFindHost? _activeFindHost;
    private readonly DispatcherTimer _zoomPopupIdleTimer = new()
    {
        Interval = ZoomPopupIdleTimeout,
    };

    private void RegisterViewMenuAccelerators()
    {
        RootGrid.KeyDown += ViewMenuShortcut_KeyDown;
        _zoomPopupIdleTimer.Tick += ZoomPopupIdleTimer_Tick;
    }

    /// <summary>Called when the window closes: a pending idle tick would hide the zoom popup —
    /// i.e. touch the visual tree — after teardown has started. Stop it before that can happen.</summary>
    private void DetachViewMenu()
    {
        _zoomPopupIdleTimer.Stop();
        _zoomPopupIdleTimer.Tick -= ZoomPopupIdleTimer_Tick;
    }

    private void ToggleSidebarMenu_Click(object sender, RoutedEventArgs e)
        => ToggleSidebarCommand();

    private void OpenTerminal_Click(object sender, RoutedEventArgs e)
        => OpenTerminalCommand();

    private void Find_Click(object sender, RoutedEventArgs e)
        => FindCommand();

    private void BackMenu_Click(object sender, RoutedEventArgs e)
        => BackCommand();

    private void ForwardMenu_Click(object sender, RoutedEventArgs e)
        => ForwardCommand();

    private void ZoomIn_Click(object sender, RoutedEventArgs e)
        => ZoomInCommand();

    private void ZoomOut_Click(object sender, RoutedEventArgs e)
        => ZoomOutCommand();

    private void ActualSize_Click(object sender, RoutedEventArgs e)
        => ShowActualSizeCommand();

    private void ActualSizeZoomIn_Click(object sender, RoutedEventArgs e)
    {
        ZoomInCommand();
        RestartZoomPopupIdleTimer();
    }

    private void ActualSizeZoomOut_Click(object sender, RoutedEventArgs e)
    {
        ZoomOutCommand();
        RestartZoomPopupIdleTimer();
    }

    private void ResetZoom_Click(object sender, RoutedEventArgs e)
    {
        ResetZoomCommand();
        RestartZoomPopupIdleTimer();
    }

    private void ToggleFullScreen_Click(object sender, RoutedEventArgs e)
        => ToggleFullScreenCommand();

    private void ViewMenuShortcut_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Handled) return;
        if (IsModalOverlayOpen) return;

        var id = MatchShortcut(e);
        if (id is null) return;

        // Full screen and Find are the two that stay live while the caret is in a text box;
        // the rest would fight the editor for keys it legitimately owns, so they stand down.
        // (This split predates rebinding and is about focus, not about which keys are bound.)
        if (id is not (KeyboardShortcutCatalog.Ids.ToggleFullScreen or KeyboardShortcutCatalog.Ids.Find)
            && IsEditableInputFocused())
        {
            return;
        }

        switch (id)
        {
            case KeyboardShortcutCatalog.Ids.ToggleFullScreen:
                ToggleFullScreenCommand();
                break;
            case KeyboardShortcutCatalog.Ids.Find:
                FindCommand();
                break;
            case KeyboardShortcutCatalog.Ids.ToggleSidebar:
                ToggleSidebarCommand();
                break;
            case KeyboardShortcutCatalog.Ids.OpenTerminal:
                OpenTerminalCommand();
                break;
            case KeyboardShortcutCatalog.Ids.GoBack:
                BackCommand();
                break;
            case KeyboardShortcutCatalog.Ids.GoForward:
                ForwardCommand();
                break;
            case KeyboardShortcutCatalog.Ids.ZoomIn:
                ZoomInCommand();
                break;
            case KeyboardShortcutCatalog.Ids.ZoomOut:
                ZoomOutCommand();
                break;
            default:
                return;   // a shortcut another handler owns
        }

        e.Handled = true;
    }

    private void OpenTerminalCommand()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "wt.exe",
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                UseShellExecute = true,
            });
        }
        catch
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                UseShellExecute = true,
            });
        }
    }

    private void FindCommand()
    {
        if (ContentFrame.Content is not Views.IFindHost findHost) return;

        AttachFindHost(findHost);
        UpdateFindOverlayPosition();
        FindOverlayPanel.Visibility = Visibility.Visible;
        findHost.OpenFind();
        findHost.SetFindQuery(FindTextBox.Text);
        UpdateFindOverlayState();

        DispatcherQueue.TryEnqueue(() =>
        {
            FindTextBox.Focus(FocusState.Programmatic);
            FindTextBox.SelectAll();
        });
    }

    private void ZoomInCommand()
        => SetZoom(Math.Min(MaxZoom, _zoomFactor + ZoomStep));

    private void ZoomOutCommand()
        => SetZoom(Math.Max(MinZoom, _zoomFactor - ZoomStep));

    private void ResetZoomCommand()
        => SetZoom(DefaultZoom);

    private void ShowActualSizeCommand()
    {
        UpdateZoomPopupPosition();
        UpdateZoomPercentText();
        ZoomPopupPanel.Visibility = Visibility.Visible;
        RestartZoomPopupIdleTimer();
    }

    private void SetZoom(double zoomFactor)
    {
        _zoomFactor = Math.Round(zoomFactor, 2);
        UpdateZoomPercentText();
        MainContentArea.RenderTransformOrigin = new Windows.Foundation.Point(0, 0);
        MainContentArea.RenderTransform = new ScaleTransform
        {
            ScaleX = _zoomFactor,
            ScaleY = _zoomFactor,
        };
    }

    private void UpdateZoomPercentText()
    {
        ZoomPercentText.Text = $"{_zoomFactor:P0}";
        ResetZoomButton.IsEnabled = Math.Abs(_zoomFactor - DefaultZoom) > 0.001;
    }

    private void UpdateZoomPopupPosition()
    {
        var captionWidth = CaptionButtonsColumn.ActualWidth > 0
            ? CaptionButtonsColumn.ActualWidth
            : 144;

        ZoomPopupPanel.Margin = new Thickness(
            0,
            ZoomPopupTopOffset,
            captionWidth + ZoomPopupMarginFromCaption,
            0);
    }

    private void UpdateFindOverlayPosition()
    {
        var captionWidth = CaptionButtonsColumn.ActualWidth > 0
            ? CaptionButtonsColumn.ActualWidth
            : 144;
        var availableWidth = Math.Max(FindOverlayMinWidth, RootGrid.ActualWidth - captionWidth - 16);

        FindOverlayPanel.Width = Math.Min(FindOverlayWidth, availableWidth);
        FindOverlayPanel.Margin = new Thickness(
            0,
            FindOverlayTopOffset,
            captionWidth + FindOverlayMarginFromCaption,
            0);
    }

    private void AttachFindHost(Views.IFindHost findHost)
    {
        if (ReferenceEquals(_activeFindHost, findHost)) return;

        if (_activeFindHost is not null)
            _activeFindHost.FindStateChanged -= FindHost_FindStateChanged;

        _activeFindHost = findHost;
        _activeFindHost.FindStateChanged += FindHost_FindStateChanged;
    }

    private void FindHost_FindStateChanged(object? sender, EventArgs e)
        => DispatcherQueue.TryEnqueue(UpdateFindOverlayState);

    private void UpdateFindOverlayState()
    {
        var host = _activeFindHost;
        var hasMatches = host?.HasFindMatches == true;

        FindCounterText.Text = host?.FindStatusText ?? "0 / 0 results";
        PreviousFindButton.IsEnabled = hasMatches;
        NextFindButton.IsEnabled = hasMatches;
    }

    private void CloseFindOverlay()
    {
        FindOverlayPanel.Visibility = Visibility.Collapsed;
        _activeFindHost?.CloseFind();
        _isUpdatingFindText = true;
        FindTextBox.Text = "";
        _isUpdatingFindText = false;
        UpdateFindOverlayState();
    }

    private void FindTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingFindText) return;
        _activeFindHost?.SetFindQuery(FindTextBox.Text);
    }

    private void FindTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            if ((Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0)
                _activeFindHost?.SelectPreviousFindMatch();
            else
                _activeFindHost?.SelectNextFindMatch();

            e.Handled = true;
            return;
        }

        if (e.Key == VirtualKey.Escape)
        {
            CloseFindOverlay();
            e.Handled = true;
        }
    }

    private void PreviousFind_Click(object sender, RoutedEventArgs e)
        => _activeFindHost?.SelectPreviousFindMatch();

    private void NextFind_Click(object sender, RoutedEventArgs e)
        => _activeFindHost?.SelectNextFindMatch();

    private void CloseFind_Click(object sender, RoutedEventArgs e)
        => CloseFindOverlay();

    private void RestartZoomPopupIdleTimer()
    {
        if (ZoomPopupPanel.Visibility != Visibility.Visible) return;

        _zoomPopupIdleTimer.Stop();
        _zoomPopupIdleTimer.Start();
    }

    private void ZoomPopupIdleTimer_Tick(object? sender, object e)
    {
        _zoomPopupIdleTimer.Stop();
        ZoomPopupPanel.Visibility = Visibility.Collapsed;
    }

    private void ToggleFullScreenCommand()
    {
        _isFullScreen = !_isFullScreen;
        _appWindow.SetPresenter(_isFullScreen
            ? AppWindowPresenterKind.FullScreen
            : AppWindowPresenterKind.Default);
    }

    private bool IsEditableInputFocused()
    {
        var focused = FocusManager.GetFocusedElement(RootGrid.XamlRoot) as DependencyObject;

        while (focused is not null)
        {
            if (focused is TextBox or RichEditBox or PasswordBox or AutoSuggestBox or NumberBox)
                return true;

            focused = VisualTreeHelper.GetParent(focused);
        }

        return false;
    }
}
