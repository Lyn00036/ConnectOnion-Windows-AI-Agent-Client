using System;
using System.IO;
using ConnectOnion.WinUIClient.Data;
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
    private readonly IntPtr _hwnd;
    private readonly SessionRepository _sessionRepository;
    private int _windowServicesDetached;
    private bool _isSidebarVisible = true;

    public MainWindow()
    {
        InitializeComponent();
        _sessionRepository = AppServices.Sessions;
        TrackWindowLifetime();
        InitializeNotificationPresence();
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _appWindow = GetAppWindow(_hwnd);
        ConfigureCustomTitleBar();
        InitializeTrayBehavior();
        ShellSidebar.NavigationRequested += NavigateTo;
        ShellSidebar.SettingsRequested += ShowSettingsOverlay;
        ShellSidebar.AddAgentRequested += ShowAddAgentOnHome;
        SettingsOverlay.CloseRequested += (_, _) => CloseSettingsOverlay();
        KeyboardShortcutsDialog.CloseRequested += (_, _) => CloseKeyboardShortcutsOverlay();
        AboutOverlay.CloseRequested += (_, _) => CloseAboutOverlay();
        ContentFrame.Navigated += async (_, _) =>
        {
            try
            {
                ShellSidebar.SetCurrentPage(ContentFrame.CurrentSourcePageType);
                UpdateNavigationButtons();
                CloseFindOverlay();
                await ShellSidebar.RefreshAsync();
            }
            catch
            {
                // Best-effort UI refresh; a failed sidebar update should not
                // crash the process.
            }
        };
        ThemeService.ThemeApplied += ApplyTitleBarTheme;
        _sessionRepository.SessionsChanged += OnSessionsChanged;
        Closed += (_, _) =>
        {
            DisposeTrayBehavior();
            DetachWindowServices();
        };
        ThemeService.RegisterRoot(RootGrid);
        _ = ApplyStartupThemeAsync();
        RootGrid.Loaded += async (_, _) =>
        {
            try
            {
                await ShellSidebar.RefreshAsync();
                NavigateTo(typeof(HomePage));
                UpdateNavigationButtons();
            }
            catch
            {
                // Initialization is best-effort; a failure here should not
                // prevent the window from being shown.
            }
            finally
            {
                // UI is ready — replay any notification click buffered at cold start, and report an
                // identity that was reset before this window existed to show it.
                AppServices.NotificationActivation.MarkReady();
                ReportIdentityResetIfAny();
            }
        };
    }

    /// <summary>
    /// Stops window callbacks from touching DI-owned services — or the visual tree — during final
    /// teardown. The dispatcher keeps pumping briefly after <c>Closed</c>, so anything still armed
    /// (a timer tick, an animation continuation) can run against a tree the framework is already
    /// tearing down; that surfaces as an access violation inside Microsoft.UI.Xaml.dll, which no
    /// managed catch can see. Disarm the window's own actives here.
    /// </summary>
    private void DetachWindowServices()
    {
        if (System.Threading.Interlocked.Exchange(ref _windowServicesDetached, 1) != 0) return;

        ThemeService.ThemeApplied -= ApplyTitleBarTheme;
        _sessionRepository.SessionsChanged -= OnSessionsChanged;
        DetachNotificationPresence();
        DetachShortcutHints();
        DetachViewMenu();
        InAppNotifications.Shutdown();

        // Pages disarm their own timers in Unloaded, which is not guaranteed to fire on window
        // close — so the page currently on screen is asked directly. Wrapped because a page that
        // throws while disarming must not abort the rest of this teardown; the alternative is
        // leaving the notification host and window hooks attached.
        //
        // The outcome is logged rather than left silent because this is the one step whose
        // failure mode is invisible: skipping it does not throw, it leaves a timer armed, and the
        // crash it eventually causes is a native access violation with no managed stack and no
        // connection back to here. The log line is the only evidence that the disarm ran at all.
        var pageName = ContentFrame.Content?.GetType().Name ?? "none";
        try
        {
            if (ContentFrame.Content is Views.IShutdownDisarmable page)
            {
                page.DisarmForShutdown();
                Serilog.Log.Information("Window teardown: disarmed page {Page}", pageName);
            }
            else
            {
                Serilog.Log.Information(
                    "Window teardown: page {Page} has nothing to disarm", pageName);
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Page {Page} did not disarm cleanly during shutdown", pageName);
        }
    }

    private static AppWindow GetAppWindow(IntPtr hwnd)
    {
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        return AppWindow.GetFromWindowId(windowId);
    }

    private void ConfigureCustomTitleBar()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarDragRegion);
        _appWindow.Title = "ConnectOnion";
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app-icon.ico");
        if (File.Exists(iconPath))
        {
            _appWindow.SetIcon(iconPath);
        }
        ApplyTitleBarTheme(ElementTheme.Light);
    }

    private void RootGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplySidebarWidth(e.NewSize.Width);
        UpdateZoomPopupPosition();
        UpdateFindOverlayPosition();
    }

    private void ApplySidebarWidth(double windowWidth)
    {
        if (!_isSidebarVisible)
        {
            ShellSidebarColumn.Width = new GridLength(0);
            return;
        }

        ShellSidebarColumn.Width = windowWidth switch
        {
            < 720 => new GridLength(208),
            < 980 => new GridLength(232),
            _ => new GridLength(264),
        };
    }

    private void SidebarToggle_Click(object sender, RoutedEventArgs e)
        => ToggleSidebarCommand();

    private void ToggleSidebarCommand()
    {
        _isSidebarVisible = !_isSidebarVisible;
        ShellSidebar.Visibility = _isSidebarVisible ? Visibility.Visible : Visibility.Collapsed;
        ApplySidebarWidth(RootGrid.ActualWidth);
        SidebarToggleButton.Opacity = _isSidebarVisible ? 1 : 0.65;
    }

    private void Back_Click(object sender, RoutedEventArgs e)
        => BackCommand();

    private void BackCommand()
    {
        if (ContentFrame.CanGoBack)
        {
            ContentFrame.GoBack();
        }
    }

    private void Forward_Click(object sender, RoutedEventArgs e)
        => ForwardCommand();

    private void ForwardCommand()
    {
        if (ContentFrame.CanGoForward)
        {
            ContentFrame.GoForward();
        }
    }

    /// <summary>True while any full-window modal (settings, keyboard shortcuts, or About) is
    /// showing. The dimmed backdrop already blocks the mouse; the global menu accelerators bubble
    /// up through RootGrid regardless of hit-testing, so they must additionally bail on this to
    /// keep a modal from acting on the content behind it.</summary>
    private bool IsModalOverlayOpen
        => SettingsOverlay.IsOpen || KeyboardShortcutsDialog.IsOpen || AboutOverlay.IsOpen;

    private void UpdateNavigationButtons()
    {
        BackButton.IsEnabled = ContentFrame.CanGoBack;
        ForwardButton.IsEnabled = ContentFrame.CanGoForward;

        // Disabled Back/Forward keep the title bar's own (transparent) background — see
        // their inline ButtonBackgroundDisabled override in XAML — so the muted arrow color
        // is the only disabled cue.
        BackIcon.Foreground = NavIconBrush(ContentFrame.CanGoBack);
        ForwardIcon.Foreground = NavIconBrush(ContentFrame.CanGoForward);
    }

    private static Microsoft.UI.Xaml.Media.Brush NavIconBrush(bool isEnabled)
        => (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
            isEnabled ? "TextSecondaryBrush" : "TextDisabledBrush"];

    private void OnSessionsChanged()
        => DispatcherQueue.TryEnqueue(async () =>
        {
            try { await ShellSidebar.RefreshAsync(); }
            catch { /* Best-effort refresh on session change. */ }
        });

    private void ApplyTitleBarTheme(ElementTheme theme)
    {
        var titleBar = _appWindow.TitleBar;
        titleBar.BackgroundColor = Colors.Transparent;
        titleBar.InactiveBackgroundColor = Colors.Transparent;
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;

        var isDark = theme == ElementTheme.Dark;
        var textPrimary = ThemeService.GetColor("TextPrimaryColor", isDark);
        var textSecondary = ThemeService.GetColor("TextSecondaryColor", isDark);

        titleBar.ForegroundColor = textPrimary;
        titleBar.InactiveForegroundColor = textSecondary;
        titleBar.ButtonForegroundColor = textPrimary;
        titleBar.ButtonInactiveForegroundColor = textSecondary;
        titleBar.ButtonHoverBackgroundColor = ThemeService.GetColor("SurfaceHoverColor", isDark);
        titleBar.ButtonHoverForegroundColor = textPrimary;
        titleBar.ButtonPressedBackgroundColor = ThemeService.GetColor("SurfacePressedColor", isDark);
        titleBar.ButtonPressedForegroundColor = textPrimary;
    }

    private async System.Threading.Tasks.Task ApplyStartupThemeAsync()
    {
        var prefs = await AppServices.Preferences.LoadAsync();
        ThemeService.Apply(prefs.Theme);
    }

    /// <summary>Two-parameter shape kept because it is what <c>ShellSidebar.NavigationRequested</c>
    /// (an <c>Action&lt;Type, bool&gt;</c>) binds to — a method with an extra optional parameter
    /// does not convert to that delegate.</summary>
    private void NavigateTo(Type page, bool forceReload = false)
        => NavigateTo(page, forceReload, parameter: null);

    /// <param name="parameter">Optional navigation parameter, read by the target page in
    /// <c>OnNavigatedTo</c>. Only reaches a page that is actually navigated to — the
    /// <see cref="Views.IReloadablePage"/> fast path below reloads the live page instead, and a
    /// page using this must therefore not depend on it arriving on every call.</param>
    private async void NavigateTo(Type page, bool forceReload, object? parameter)
    {
        // Chat → chat (or agent → agent) is a change of selection, not a change of page. Navigating
        // would discard the live page and build another — XAML tree, composer + Win2D canvas, view
        // model, bindings — on every click. Hand the switch to the page already on screen (see
        // Views.IReloadablePage) and do by hand the two things the Navigated handler would have.
        if (forceReload
            && ContentFrame.CurrentSourcePageType == page
            && ContentFrame.Content is Views.IReloadablePage reloadable)
        {
            CloseFindOverlay();
            try { await reloadable.ReloadAsync(); }
            catch { /* a failed reload leaves the page on its previous selection */ }
            try { await ShellSidebar.RefreshAsync(); }
            catch { /* best-effort sidebar refresh */ }
            return;
        }

        if (forceReload || ContentFrame.CurrentSourcePageType != page)
        {
            ContentFrame.Navigate(page, parameter);

            // Navigate pushes the old page onto the BackStack. When
            // force-reloading, remove it so the old instance (with its
            // ViewModel, WebSocket, and bindings) can be garbage collected.
            if (forceReload && ContentFrame.BackStack.Count > 0)
                ContentFrame.BackStack.RemoveAt(ContentFrame.BackStack.Count - 1);
        }
    }

    /// <summary>
    /// Answers the sidebar's "Add agent" button: go to the landing page with the form already
    /// open and scrolled to.
    ///
    /// <para><c>forceReload</c> is what makes this work when the user is already on HomePage —
    /// without it <c>NavigateTo</c> would see the page type is unchanged and do nothing, so the
    /// button would appear dead exactly when the target is already on screen. HomePage does not
    /// implement <see cref="Views.IReloadablePage"/>, so a force-reload is always a real
    /// navigation and the parameter always arrives.</para>
    /// </summary>
    private void ShowAddAgentOnHome()
        => NavigateTo(typeof(HomePage), forceReload: true, parameter: HomePage.OpenAddAgentParameter);

    private void ShowSettingsOverlay(FrameworkElement? opener = null)
        => SettingsOverlay.Show(opener);

    private void CloseSettingsOverlay()
        => SettingsOverlay.Hide();
}
