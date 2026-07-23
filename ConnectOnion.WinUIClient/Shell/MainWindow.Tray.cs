using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ConnectOnion.WinUIClient;

public sealed partial class MainWindow
{
    private const int WmClose = 0x0010;
    private const int WmSize = 0x0005;
    private const int SizeRestored = 0;
    private const int SizeMinimized = 1;
    private const int SizeMaximized = 2;

    private const int SwHide = 0;
    private const int SwShow = 5;
    private const int SwRestore = 9;
    private static bool _isExiting;

    private SUBCLASSPROC? _subclassProc;
    private bool _isTrayDisposed;

    private void InitializeTrayBehavior()
    {
        // H.NotifyIcon owns the notification-area icon, callback window, icon handle,
        // context menu, and shell-restart recovery. This remaining subclass only observes
        // the real app window for close-to-tray and notification-presence semantics.
        _subclassProc = WindowSubclassProc;
        if (!SetWindowSubclass(_hwnd, _subclassProc, UIntPtr.Zero, UIntPtr.Zero))
        {
            Serilog.Log.Warning("Could not attach close-to-tray window message observer");
        }

        try
        {
            // Keep the process fully active while background agent turns are running.
            // H.NotifyIcon defaults this call to Windows 11 Efficiency Mode, which is not
            // appropriate for an app maintaining live WebSocket work.
            TrayIcon.ForceCreate(enablesEfficiencyMode: false);
        }
        catch (Exception ex)
        {
            // The XAML control will retry creation when it is loaded. A tray failure must
            // not prevent the primary window from opening.
            Serilog.Log.Warning(ex, "Could not create the system tray icon immediately");
        }
    }

    private IntPtr WindowSubclassProc(
        IntPtr hwnd,
        uint message,
        UIntPtr wParam,
        IntPtr lParam,
        UIntPtr subclassId,
        UIntPtr refData)
    {
        try
        {
            // Track taskbar minimize/restore. WinUI's Window.Activated does not
            // reliably fire on minimize, so without this the notification layer
            // still thinks this window is actively showing the conversation and
            // suppresses (or in-app-routes) replies that arrive while minimized.
            // WM_SIZE always fires, so key visibility off it.
            if (message == WmSize)
            {
                var sizeType = unchecked((int)wParam.ToUInt64()) & 0xffff;
                if (sizeType == SizeMinimized)
                    _windowPresence?.SetVisible(this, false);
                else if (sizeType is SizeRestored or SizeMaximized)
                    _windowPresence?.SetVisible(this, true);
                return DefSubclassProc(hwnd, message, wParam, lParam);
            }

            if (message == WmClose && !_isExiting)
            {
                _ = DispatcherQueue.TryEnqueue(async () =>
                {
                    try
                    {
                        if (await ShowCloseDialogAsync())
                            MinimizeToTray();
                    }
                    catch
                    {
                        // If the dialog or minimize fails (e.g. XamlRoot
                        // unavailable during teardown), let the close proceed
                        // through the default handler so the window can still
                        // be dismissed.
                        DefSubclassProc(hwnd, message, wParam, lParam);
                    }
                });
                return IntPtr.Zero;
            }

            return DefSubclassProc(hwnd, message, wParam, lParam);
        }
        catch
        {
            return DefSubclassProc(hwnd, message, wParam, lParam);
        }
    }

    private async System.Threading.Tasks.Task<bool> ShowCloseDialogAsync()
    {
        // If the XamlRoot is not available (e.g. window is already tearing
        // down), skip the dialog and proceed with a normal close.
        if (Content.XamlRoot is null)
            return false;

        var dialog = new ContentDialog
        {
            Title = "ConnectOnion",
            Content = "Do you want to minimize to the tray or exit the application?",
            PrimaryButtonText = "Minimize to tray",
            SecondaryButtonText = "Exit",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
        };

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Secondary)
        {
            ExitApplication();
            return false;
        }

        // Primary = minimize, Close/Cancel = do nothing (return true for minimize)
        return result == ContentDialogResult.Primary;
    }

    private void MinimizeToTray()
    {
        if (_isExiting || _isTrayDisposed) return;

        // Logged because "hidden to tray" and "exited" are indistinguishable in a log otherwise —
        // the process simply stops writing. A user reporting "the app closed itself" is answered
        // by whether this line or the exit sequence came last.
        Serilog.Log.Information("Window hidden to tray; app still running");
        ShowWindow(_hwnd, SwHide);
        _windowPresence?.SetVisible(this, false);
    }

    private void RestoreFromTray()
    {
        if (_isExiting || _isTrayDisposed) return;

        ShowWindow(_hwnd, SwShow);
        SetForegroundWindow(_hwnd);
        Activate();
        _windowPresence?.SetVisible(this, true);
    }

    /// <summary>Restores the window from a minimized or hidden (tray) state and brings it
    /// to the foreground. Used when a second app launch is redirected to this — the only —
    /// instance: it must wake the existing window, never create a new one. Safe against
    /// teardown (a redirect arriving while the app is closing) and a temporarily
    /// unavailable window handle.</summary>
    public void BringToForeground()
    {
        if (_isExiting || _hwnd == IntPtr.Zero) return;

        // SW_RESTORE un-minimizes and unhides in one call; SW_SHOW guarantees the window
        // is visible even when it was hidden to the tray via SW_HIDE.
        ShowWindow(_hwnd, SwRestore);
        ShowWindow(_hwnd, SwShow);
        SetForegroundWindow(_hwnd);
        try { Activate(); }
        catch { /* Activate can throw during teardown; best-effort. */ }
        if (!_isTrayDisposed) _windowPresence?.SetVisible(this, true);
    }

    private async void ExitApplication()
    {
        // The single funnel for a real exit (close dialog's Exit, tray menu Exit, File > Exit).
        // Logged at entry so the teardown lines that follow — window detach, then the phase
        // timings from App.ShutdownAsync — can be read as one sequence, and so a log that simply
        // stops without this line is recognisable as a crash rather than a clean exit.
        Serilog.Log.Information("Exit requested; tearing down window");
        _isExiting = true;
        // Single window: this instance owns the only tray behavior to dispose.
        DisposeTrayBehavior();
        // Application.Exit closes the HWND after the host has stopped. Remove callbacks first so
        // late Activated/Closed events never resolve services from a disposed IServiceProvider.
        DetachWindowServices();

        // Persist while the host and its services are still alive. Application.Exit raises
        // Closed only after ShutdownAsync has disposed the provider; SaveWindowPlacementAsync is
        // idempotent, so that later Closed callback becomes a harmless no-op.
        await SaveWindowPlacementAsync();

        // Real exit (as opposed to minimize-to-tray): cancel in-flight turns, let them
        // persist recoverable state, and release the sockets — all bounded so exit never
        // hangs. ShutdownAsync self-limits; the extra cap is a hard backstop.
        await ((App)Application.Current).ShutdownAsync();

        Application.Current.Exit();
    }

    private void DisposeTrayBehavior()
    {
        if (_isTrayDisposed) return;

        _isTrayDisposed = true;
        TrayIcon.Dispose();

        if (_subclassProc is not null)
        {
            RemoveWindowSubclass(_hwnd, _subclassProc, UIntPtr.Zero);
            _subclassProc = null;
        }
    }
}
