using System;
using System.Runtime.InteropServices;

namespace ConnectOnion.WinUIClient;

// Native interop backing the tray behavior in MainWindow.Tray.cs — the hidden
// message-only helper window, window subclassing, Shell_NotifyIcon, and the
// popup menu. Kept in its own partial so the tray *behavior* stays readable
// without the wall of P/Invoke declarations.
public sealed partial class MainWindow
{
    // WinUI 3 has no tray API at all, which is why this file exists: the notification area is
    // pure Win32, so the tray icon needs a real HWND to receive its callback messages and a
    // window procedure to interpret them. The declarations are grouped roughly in the order the
    // tray uses them — window class, icon data, then the imports.
    //
    // Delegate lifetime is the trap here. A managed delegate passed to Win32 is not rooted by
    // the unmanaged side holding a function pointer to it, so if the GC collects it the next
    // message from the shell calls into freed memory and the process dies with no managed
    // stack. The instances therefore live in fields in MainWindow.Tray.cs, not as temporaries
    // at the call site.

    /// <summary>Window procedure for the message-only helper window.</summary>
    private delegate IntPtr WNDPROC(IntPtr hWnd, uint msg, UIntPtr wParam, IntPtr lParam);

    /// <summary>Subclass procedure used to intercept messages on an existing window without
    /// replacing its window proc outright — the supported way to add behavior to a window the
    /// framework owns, since it chains back through <c>DefSubclassProc</c>.</summary>
    private delegate IntPtr SUBCLASSPROC(
        IntPtr hWnd, uint uMsg, UIntPtr wParam, IntPtr lParam,
        UIntPtr uIdSubclass, UIntPtr dwRefData);

    // Sequential layout and Unicode charset are mandatory on every struct below: these are
    // passed by reference straight into Win32, which reads them field-by-field at fixed
    // offsets. Reordering a field, changing a type's width, or letting the runtime pick a
    // layout produces silent memory corruption rather than a compile error.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEXW
    {
        // cbSize must be set to the struct's own size before use — Win32's versioning
        // mechanism, and the call fails if it is left at zero.
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

    /// <summary>The tray icon itself, as the shell sees it. The fixed-size string buffers are
    /// the API's own limits (128 chars of tooltip, 256 of balloon text) and are enforced by the
    /// marshaller — a longer string is truncated, not rejected.</summary>
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

    /// <summary>A genuine C union — both fields occupy the same four bytes, which
    /// <c>LayoutKind.Explicit</c> with a shared <c>FieldOffset(0)</c> reproduces. Which one is
    /// meaningful depends on the message being sent, so writing one necessarily clobbers the
    /// other.</summary>
    [StructLayout(LayoutKind.Explicit)]
    private struct NotifyIconDataUnion
    {
        [FieldOffset(0)] public int uTimeout;
        [FieldOffset(0)] public int uVersion;
    }

    /// <summary>Which fields of <see cref="NotifyIconData"/> the shell should actually read.
    /// Anything not flagged here is ignored no matter what it contains — forgetting a flag is
    /// the usual reason a tooltip or callback appears to be silently dropped.</summary>
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

    // Comctl32 subclassing rather than SetWindowLongPtr(GWLP_WNDPROC): the window belongs to
    // WinUI, and swapping its window proc outright would break any other component doing the
    // same. Every SetWindowSubclass needs a matching RemoveWindowSubclass at teardown, with the
    // *same* delegate instance and id, or the shell keeps calling into a dead window.
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

    // SetForegroundWindow before TrackPopupMenu is a documented requirement, not a nicety:
    // without it the context menu does not dismiss when the user clicks elsewhere and is left
    // stuck on screen.
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    // Returns the selected command id (0 = dismissed) rather than posting a message, because
    // the tray menu is tracked with TPM_RETURNCMD — simpler than routing WM_COMMAND back
    // through the helper window's proc.
    [DllImport("user32.dll", SetLastError = true)]
    private static extern int TrackPopupMenu(
        IntPtr hMenu, uint uFlags, int x, int y,
        int nReserved, IntPtr hWnd, IntPtr prcRect);
}
