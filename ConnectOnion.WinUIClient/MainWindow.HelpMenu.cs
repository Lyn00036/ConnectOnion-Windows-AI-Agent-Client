using System;
using System.Runtime.InteropServices;
using ConnectOnion.WinUIClient.Models.Notifications;
using ConnectOnion.WinUIClient.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace ConnectOnion.WinUIClient;

/// <summary>
/// Help menu: keyboard shortcuts overview, the public docs, and the About overlay. Each item owns
/// exactly one command — the docs item only launches the browser, the About item only shows the
/// overlay. Both overlays are single, persistent instances hosted by MainWindow, so re-invoking a
/// menu item re-focuses the open one rather than creating a second.
/// </summary>
public sealed partial class MainWindow
{
    private const string DocsUrl = "https://docs.connectonion.com/";

    /// <summary>VK_OEM_2 — the key that carries '/' on a US layout. Kept only as the fallback for
    /// <see cref="ProducesSlash"/>; do not compare against it directly (see that method).</summary>
    private const int VkOemSlash = 191;

    /// <summary>MapVirtualKey's MAPVK_VK_TO_CHAR: translate a virtual key to the character it
    /// produces under the *current* keyboard layout.</summary>
    private const uint MapVkToChar = 2;

    private void RegisterHelpMenuAccelerators()
        => RootGrid.KeyDown += HelpMenuShortcut_KeyDown;

    private void HelpMenuShortcut_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Handled) return;

        // While a modal overlay is open it owns all input — don't fire background
        // accelerators behind it.
        if (IsModalOverlayOpen) return;

        var hasControl = IsKeyDown(VirtualKey.Control);
        var hasShift = IsKeyDown(VirtualKey.Shift);
        var hasAlt = IsKeyDown(VirtualKey.Menu);

        if (hasControl && hasShift && !hasAlt && ProducesSlash(e.Key))
        {
            e.Handled = true;
            ShowKeyboardShortcutsOverlay(HelpMenuBarItem);
        }
    }

    /// <summary>
    /// True when this key types '/' on the keyboard layout the user actually has. Hard-coding
    /// VK_OEM_2 would only be right for US-style layouts — '/' sits on a different virtual key in
    /// many others (and on some it is a shifted digit), so Ctrl+Shift+/ would simply not exist for
    /// those users. Asking the layout what a key produces is the only portable way to match the
    /// shortcut we actually advertise in the menu.
    /// </summary>
    private static bool ProducesSlash(VirtualKey key)
    {
        // Numpad divide is always '/', whatever the layout.
        if (key == VirtualKey.Divide) return true;

        try
        {
            // The low word holds the character; the high bit flags a dead key, which '/' never is.
            var mapped = MapVirtualKeyW((uint)key, MapVkToChar) & 0x7FFF;
            if (mapped != 0) return (char)mapped == '/';
        }
        catch (EntryPointNotFoundException)
        {
            // Fall through to the positional guess.
        }

        return (int)key == VkOemSlash;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint MapVirtualKeyW(uint uCode, uint uMapType);

    private void KeyboardShortcuts_Click(object sender, RoutedEventArgs e)
        // Return focus to the top-level "Help" menu bar item, not the clicked flyout item —
        // the flyout is gone by the time the dialog closes, so focusing it would no-op.
        => ShowKeyboardShortcutsOverlay(HelpMenuBarItem);

    private void ShowKeyboardShortcutsOverlay(FrameworkElement? opener = null)
        => KeyboardShortcutsDialog.Show(opener);

    private void CloseKeyboardShortcutsOverlay()
        => KeyboardShortcutsDialog.Hide();

    /// <summary>Opens the public docs in the default browser. Goes through
    /// <see cref="AppServices.UriLauncher"/> (rather than the static, unmockable <c>Launcher</c>)
    /// so this path is testable; surfaces a toast rather than failing silently when the shell can't
    /// hand the URI off (no browser registered, blocked by policy).</summary>
    private async void ConnectOnionDocs_Click(object sender, RoutedEventArgs e)
    {
        var launched = await AppServices.UriLauncher.LaunchAsync(new Uri(DocsUrl));

        if (!launched)
        {
            ShowInAppToast(new InAppToastModel(
                "Couldn't open the docs",
                $"Visit {DocsUrl} in your browser.",
                NotificationType.Error,
                AgentId: null,
                ConversationId: null,
                ActionId: null));
        }
    }

    private void AboutConnectOnion_Click(object sender, RoutedEventArgs e)
        => ShowAboutOverlay(HelpMenuBarItem);

    private void ShowAboutOverlay(FrameworkElement? opener = null)
        => AboutOverlay.Show(opener);

    private void CloseAboutOverlay()
        => AboutOverlay.Hide();
}
