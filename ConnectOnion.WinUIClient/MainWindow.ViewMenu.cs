using System;
using System.Diagnostics;
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
    private const double ZoomPopupTopOffset = 36;
    private static readonly TimeSpan ZoomPopupIdleTimeout = TimeSpan.FromSeconds(3);

    private double _zoomFactor = 1.0;
    private bool _isFullScreen;
    private readonly DispatcherTimer _zoomPopupIdleTimer = new()
    {
        Interval = ZoomPopupIdleTimeout,
    };

    private void RegisterViewMenuAccelerators()
    {
        RootGrid.KeyDown += ViewMenuShortcut_KeyDown;
        _zoomPopupIdleTimer.Tick += ZoomPopupIdleTimer_Tick;
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

        var hasControl = IsKeyDown(VirtualKey.Control);
        var hasShift = IsKeyDown(VirtualKey.Shift);
        var hasAlt = IsKeyDown(VirtualKey.Menu);

        if (!hasControl && !hasShift && !hasAlt && e.Key == VirtualKey.F11)
        {
            ToggleFullScreenCommand();
            e.Handled = true;
            return;
        }

        if (hasControl && !hasShift && !hasAlt && e.Key == VirtualKey.F)
        {
            FindCommand();
            e.Handled = true;
            return;
        }

        if (!hasControl || hasAlt || IsEditableInputFocused()) return;

        if (!hasShift && e.Key == VirtualKey.B)
        {
            ToggleSidebarCommand();
            e.Handled = true;
            return;
        }

        if (!hasShift && (int)e.Key == 192)
        {
            OpenTerminalCommand();
            e.Handled = true;
            return;
        }

        if (!hasShift && (int)e.Key == 219)
        {
            BackCommand();
            e.Handled = true;
            return;
        }

        if (!hasShift && (int)e.Key == 221)
        {
            ForwardCommand();
            e.Handled = true;
            return;
        }

        if (hasShift && ((int)e.Key == 187 || e.Key == VirtualKey.Add))
        {
            ZoomInCommand();
            e.Handled = true;
            return;
        }

        if (!hasShift && ((int)e.Key == 189 || e.Key == VirtualKey.Subtract))
        {
            ZoomOutCommand();
            e.Handled = true;
        }
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
        if (ContentFrame.Content is Views.IFindHost findHost)
        {
            findHost.OpenFind();
        }
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
        ZoomPopup.IsOpen = false;
        ZoomPopup.IsOpen = true;
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
        var popupWidth = ZoomPopupPanel.Width;
        var captionWidth = CaptionButtonsColumn.ActualWidth > 0
            ? CaptionButtonsColumn.ActualWidth
            : 144;

        ZoomPopup.HorizontalOffset = Math.Max(
            8,
            RootGrid.ActualWidth - captionWidth - popupWidth - ZoomPopupMarginFromCaption);
        ZoomPopup.VerticalOffset = ZoomPopupTopOffset;
    }

    private void RestartZoomPopupIdleTimer()
    {
        if (!ZoomPopup.IsOpen) return;

        _zoomPopupIdleTimer.Stop();
        _zoomPopupIdleTimer.Start();
    }

    private void ZoomPopupIdleTimer_Tick(object? sender, object e)
    {
        _zoomPopupIdleTimer.Stop();
        ZoomPopup.IsOpen = false;
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
