using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ConnectOnion.WinUIClient;

public sealed partial class MainWindow
{
    private const int WmClose = 0x0010;
    private const int WmCommand = 0x0111;
    private const int WmLButtonDown = 0x0201;
    private const int WmLButtonUp = 0x0202;
    private const int WmLButtonDblClk = 0x0203;
    private const int WmRButtonUp = 0x0205;
    private const int NinSelect = 0x0400;
    private const int WmApp = 0x8000;
    private const int TrayCallbackMessage = WmApp + 100;

    private const int SwHide = 0;
    private const int SwShow = 5;
    private const int TrayIconId = 1;
    private const int OpenWindowCommandId = 1001;
    private const int ExitCommandId = 1002;

    private const uint ImageIcon = 1;
    private const uint LrLoadFromFile = 0x0010;
    private const uint LrDefaultSize = 0x0040;
    private const uint MfString = 0x0000;
    private const uint MfSeparator = 0x0800;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmBottomAlign = 0x0020;

    // Window styles for the hidden tray helper window.
    private const uint WsPopUp = 0x80000000;
    private const int HwndMessage = -3;

    private static readonly IntPtr IdiApplication = new(32512);
    private static bool _isExiting;
    private static GCHandle _trayWndProcHandle;

    private SUBCLASSPROC? _subclassProc;
    private IntPtr _trayIconHandle;
    private bool _ownsTrayIconHandle;
    private bool _isTrayIconVisible;
    private IntPtr _trayHelperHwnd;

    private void InitializeTrayBehavior()
    {
        // Create a hidden message-only window solely for receiving tray callbacks.
        // This decouples tray lifecycle from the WinUI HWND, which may be
        // recreated by the framework on hide/show.
        _trayHelperHwnd = CreateTrayHelperWindow();
        if (_trayHelperHwnd == IntPtr.Zero) return;

        _subclassProc = WindowSubclassProc;
        SetWindowSubclass(_hwnd, _subclassProc, UIntPtr.Zero, UIntPtr.Zero);
        _trayIconHandle = LoadTrayIcon(out _ownsTrayIconHandle);
        EnsureTrayIconVisible();
    }

    private static IntPtr CreateTrayHelperWindow()
    {
        var wndProc = new WNDPROC(TrayHelperWndProc);
        _trayWndProcHandle = GCHandle.Alloc(wndProc);

        var wc = new WNDCLASSEXW
        {
            cbSize = Marshal.SizeOf<WNDCLASSEXW>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(wndProc),
            hInstance = GetModuleHandle(null),
            lpszClassName = "ConnectOnionTrayHelper",
        };

        var atom = RegisterClassExW(ref wc);
        if (atom == 0)
        {
            _trayWndProcHandle.Free();
            return IntPtr.Zero;
        }

        return CreateWindowExW(0, "ConnectOnionTrayHelper", "", WsPopUp,
            0, 0, 0, 0, new IntPtr(HwndMessage), IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
    }

    private static IntPtr TrayHelperWndProc(IntPtr hwnd, uint msg, UIntPtr wParam, IntPtr lParam)
    {
        if (msg == TrayCallbackMessage)
        {
            var mouseMessage = (int)(lParam.ToInt64() & 0xFFFF);

            // Left-click (single or double) restores the window.
            if (mouseMessage is WmLButtonDown or WmLButtonUp
                or WmLButtonDblClk or NinSelect)
            {
                foreach (var window in OpenWindows.ToArray())
                {
                    if (window is MainWindow mainWindow)
                    {
                        _ = mainWindow.DispatcherQueue.TryEnqueue(() =>
                            mainWindow.RestoreFromTray());
                    }
                }
                return IntPtr.Zero;
            }

            // Right-click shows the context menu.
            if (mouseMessage == WmRButtonUp)
            {
                foreach (var window in OpenWindows.ToArray())
                {
                    if (window is MainWindow mainWindow)
                    {
                        _ = mainWindow.DispatcherQueue.TryEnqueue(() =>
                            mainWindow.ShowTrayMenu());
                    }
                }
                return IntPtr.Zero;
            }
        }

        if (msg == WmCommand)
        {
            var commandId = unchecked((int)wParam.ToUInt64()) & 0xffff;
            if (commandId == OpenWindowCommandId)
            {
                foreach (var window in OpenWindows.ToArray())
                {
                    if (window is MainWindow mainWindow)
                    {
                        _ = mainWindow.DispatcherQueue.TryEnqueue(() =>
                            mainWindow.RestoreFromTray());
                    }
                }
                return IntPtr.Zero;
            }

            if (commandId == ExitCommandId)
            {
                foreach (var window in OpenWindows.ToArray())
                {
                    if (window is MainWindow mainWindow)
                    {
                        _ = mainWindow.DispatcherQueue.TryEnqueue(
                            mainWindow.ExitApplication);
                    }
                }
                return IntPtr.Zero;
            }
        }

        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private IntPtr WindowSubclassProc(
        IntPtr hwnd,
        uint message,
        UIntPtr wParam,
        IntPtr lParam,
        UIntPtr subclassId,
        UIntPtr refData)
    {
        if (message == WmClose && !_isExiting)
        {
            _ = DispatcherQueue.TryEnqueue(async () =>
            {
                if (await ShowCloseDialogAsync())
                    MinimizeToTray();
            });
            return IntPtr.Zero;
        }

        return DefSubclassProc(hwnd, message, wParam, lParam);
    }

    private async System.Threading.Tasks.Task<bool> ShowCloseDialogAsync()
    {
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
        if (_isExiting) return;

        EnsureTrayIconVisible();
        ShowWindow(_hwnd, SwHide);
    }

    private void RestoreFromTray()
    {
        ShowWindow(_hwnd, SwShow);
        SetForegroundWindow(_hwnd);
        Activate();
    }

    private void ExitApplication()
    {
        _isExiting = true;
        foreach (var window in OpenWindows.ToArray())
        {
            if (window is MainWindow mainWindow)
            {
                mainWindow.DisposeTrayBehavior();
            }
        }

        Application.Current.Exit();
    }

    private void EnsureTrayIconVisible()
    {
        if (_isTrayIconVisible) return;

        var data = CreateNotifyIconData();
        if (Shell_NotifyIcon(NotifyIconMessage.Add, ref data))
        {
            data.Anonymous.uVersion = 4;
            Shell_NotifyIcon(NotifyIconMessage.SetVersion, ref data);
            _isTrayIconVisible = true;
        }
    }

    private void RemoveTrayIcon()
    {
        if (!_isTrayIconVisible) return;

        var data = CreateNotifyIconData();
        Shell_NotifyIcon(NotifyIconMessage.Delete, ref data);
        _isTrayIconVisible = false;
    }

    private NotifyIconData CreateNotifyIconData()
        => new()
        {
            cbSize = Marshal.SizeOf<NotifyIconData>(),
            hWnd = _trayHelperHwnd == IntPtr.Zero ? _hwnd : _trayHelperHwnd,
            uID = TrayIconId,
            uFlags = NotifyIconFlags.Message | NotifyIconFlags.Icon | NotifyIconFlags.Tip,
            uCallbackMessage = TrayCallbackMessage,
            hIcon = _trayIconHandle,
            szTip = "ConnectOnion",
            szInfo = string.Empty,
            szInfoTitle = string.Empty,
        };

    private void ShowTrayMenu()
    {
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) return;

        try
        {
            AppendMenu(menu, MfString, OpenWindowCommandId, "Open Window");
            AppendMenu(menu, MfSeparator, 0, null);
            AppendMenu(menu, MfString, ExitCommandId, "Exit");
            GetCursorPos(out var point);
            SetForegroundWindow(_hwnd);
            TrackPopupMenu(menu, TpmRightButton | TpmBottomAlign, point.X, point.Y, 0, _hwnd, IntPtr.Zero);
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private static IntPtr LoadTrayIcon(out bool ownsHandle)
    {
        ownsHandle = false;
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");
        if (File.Exists(iconPath))
        {
            var handle = LoadImage(IntPtr.Zero, iconPath, ImageIcon, 0, 0, LrLoadFromFile | LrDefaultSize);
            if (handle != IntPtr.Zero)
            {
                ownsHandle = true;
                return handle;
            }
        }

        return LoadIcon(IntPtr.Zero, IdiApplication);
    }

    private void DisposeTrayBehavior()
    {
        RemoveTrayIcon();

        if (_subclassProc is not null)
        {
            RemoveWindowSubclass(_hwnd, _subclassProc, UIntPtr.Zero);
            _subclassProc = null;
        }

        if (_trayHelperHwnd != IntPtr.Zero)
        {
            DestroyWindow(_trayHelperHwnd);
            _trayHelperHwnd = IntPtr.Zero;
        }

        if (_trayWndProcHandle.IsAllocated)
        {
            _trayWndProcHandle.Free();
        }

        if (_trayIconHandle != IntPtr.Zero && _ownsTrayIconHandle)
        {
            DestroyIcon(_trayIconHandle);
        }

        if (_trayIconHandle != IntPtr.Zero)
        {
            _trayIconHandle = IntPtr.Zero;
            _ownsTrayIconHandle = false;
        }
    }

    // --- P/Invoke declarations ---

    private delegate IntPtr WNDPROC(IntPtr hWnd, uint msg, UIntPtr wParam, IntPtr lParam);

    private delegate IntPtr SUBCLASSPROC(
        IntPtr hWnd, uint uMsg, UIntPtr wParam, IntPtr lParam,
        UIntPtr uIdSubclass, UIntPtr dwRefData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEXW
    {
        public int cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public NotifyIconFlags uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public NotifyIconDataUnion Anonymous;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct NotifyIconDataUnion
    {
        [FieldOffset(0)] public int uTimeout;
        [FieldOffset(0)] public int uVersion;
    }

    [Flags]
    private enum NotifyIconFlags
    {
        Message = 0x00000001,
        Icon = 0x00000002,
        Tip = 0x00000004,
    }

    private enum NotifyIconMessage
    {
        Add = 0x00000000,
        Delete = 0x00000002,
        SetVersion = 0x00000004,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassExW(ref WNDCLASSEXW lpwcx);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight, IntPtr hWndParent,
        IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(
        IntPtr hWnd, uint msg, UIntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("Comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(
        IntPtr hWnd, SUBCLASSPROC pfnSubclass,
        UIntPtr uIdSubclass, UIntPtr dwRefData);

    [DllImport("Comctl32.dll", SetLastError = true)]
    private static extern bool RemoveWindowSubclass(
        IntPtr hWnd, SUBCLASSPROC pfnSubclass,
        UIntPtr uIdSubclass);

    [DllImport("Comctl32.dll")]
    private static extern IntPtr DefSubclassProc(
        IntPtr hWnd, uint uMsg, UIntPtr wParam, IntPtr lParam);

    [DllImport("Shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(
        NotifyIconMessage dwMessage, ref NotifyIconData lpData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImage(
        IntPtr hInst, string name, uint type, int cx, int cy, uint fuLoad);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool AppendMenu(
        IntPtr hMenu, uint uFlags, int uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorPos(out Point lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool TrackPopupMenu(
        IntPtr hMenu, uint uFlags, int x, int y,
        int nReserved, IntPtr hWnd, IntPtr prcRect);
}
