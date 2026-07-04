using System;
using ConnectOnion.WinUIClient.Models;
using ConnectOnion.WinUIClient.Services;
using ConnectOnion.WinUIClient.Views;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.UI;

namespace ConnectOnion.WinUIClient;

/// <summary>
/// Shell window with a custom WinUI title bar and a content frame.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly AppWindow _appWindow;

    public MainWindow()
    {
        InitializeComponent();
        _appWindow = GetAppWindow();
        ConfigureCustomTitleBar();
        ShellSidebar.NavigationRequested += NavigateTo;
        ContentFrame.Navigated += async (_, _) => await ShellSidebar.RefreshAsync();
        ThemeService.ThemeApplied += ApplyTitleBarTheme;
        Closed += (_, _) => ThemeService.ThemeApplied -= ApplyTitleBarTheme;
        ThemeService.RegisterRoot(RootGrid);
        _ = ApplyStartupThemeAsync();
        RootGrid.Loaded += async (_, _) =>
        {
            await ShellSidebar.RefreshAsync();
            NavigateTo(typeof(HomePage));
        };
    }

    private AppWindow GetAppWindow()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        return AppWindow.GetFromWindowId(windowId);
    }

    private void ConfigureCustomTitleBar()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarDragRegion);
        _appWindow.Title = "ConnectOnion";
        _appWindow.SetIcon("Assets\\icon.ico");
        ApplyTitleBarTheme(ThemeMode.Light);
    }

    private void RootGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ShellSidebarColumn.Width = e.NewSize.Width switch
        {
            < 720 => new GridLength(220),
            < 980 => new GridLength(248),
            _ => new GridLength(286),
        };
    }

    private void ApplyTitleBarTheme(ThemeMode theme)
    {
        var titleBar = _appWindow.TitleBar;
        titleBar.BackgroundColor = Colors.Transparent;
        titleBar.InactiveBackgroundColor = Colors.Transparent;
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;

        if (theme == ThemeMode.Dark)
        {
            titleBar.ForegroundColor = Color.FromArgb(255, 245, 245, 247);
            titleBar.InactiveForegroundColor = Color.FromArgb(255, 146, 146, 154);
            titleBar.ButtonForegroundColor = Color.FromArgb(255, 245, 245, 247);
            titleBar.ButtonInactiveForegroundColor = Color.FromArgb(255, 146, 146, 154);
            titleBar.ButtonHoverBackgroundColor = Color.FromArgb(255, 50, 50, 56);
            titleBar.ButtonHoverForegroundColor = Color.FromArgb(255, 255, 255, 255);
            titleBar.ButtonPressedBackgroundColor = Color.FromArgb(255, 68, 68, 76);
            titleBar.ButtonPressedForegroundColor = Color.FromArgb(255, 255, 255, 255);
            return;
        }

        titleBar.ForegroundColor = Color.FromArgb(255, 48, 49, 54);
        titleBar.InactiveForegroundColor = Color.FromArgb(255, 120, 120, 126);
        titleBar.ButtonForegroundColor = Color.FromArgb(255, 48, 49, 54);
        titleBar.ButtonInactiveForegroundColor = Color.FromArgb(255, 120, 120, 126);
        titleBar.ButtonHoverBackgroundColor = Color.FromArgb(255, 232, 232, 234);
        titleBar.ButtonHoverForegroundColor = Color.FromArgb(255, 20, 20, 24);
        titleBar.ButtonPressedBackgroundColor = Color.FromArgb(255, 215, 215, 218);
        titleBar.ButtonPressedForegroundColor = Color.FromArgb(255, 20, 20, 24);
    }

    private async System.Threading.Tasks.Task ApplyStartupThemeAsync()
    {
        var prefs = await AppServices.Preferences.LoadAsync();
        ThemeService.Apply(prefs.Theme);
    }

    private void NavigateTo(Type page, bool forceReload = false)
    {
        if (forceReload || ContentFrame.CurrentSourcePageType != page)
        {
            ContentFrame.Navigate(page);
        }
    }
}
