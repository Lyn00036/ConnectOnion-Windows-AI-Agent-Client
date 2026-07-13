using System;
using ConnectOnion.WinUIClient.Models.Notifications;
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

    /// <summary>VK_OEM_2 — the '/' key on a US layout. Not in the VirtualKey enum, so it is
    /// compared numerically, the same way ViewMenu handles '='/'-'.</summary>
    private const int VkOemSlash = 191;

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

        if (hasControl && hasShift && !hasAlt && (int)e.Key == VkOemSlash)
        {
            e.Handled = true;
            ShowKeyboardShortcutsOverlay(HelpMenuBarItem);
        }
    }

    private void KeyboardShortcuts_Click(object sender, RoutedEventArgs e)
        // Return focus to the top-level "Help" menu bar item, not the clicked flyout item —
        // the flyout is gone by the time the dialog closes, so focusing it would no-op.
        => ShowKeyboardShortcutsOverlay(HelpMenuBarItem);

    private void ShowKeyboardShortcutsOverlay(FrameworkElement? opener = null)
        => KeyboardShortcutsDialog.Show(opener);

    private void CloseKeyboardShortcutsOverlay()
        => KeyboardShortcutsDialog.Hide();

    /// <summary>Opens the public docs in the default browser. Surfaces a toast rather than
    /// failing silently when the shell can't hand the URI off (no browser registered, policy).</summary>
    private async void ConnectOnionDocs_Click(object sender, RoutedEventArgs e)
    {
        bool launched;
        try
        {
            launched = await Launcher.LaunchUriAsync(new Uri(DocsUrl));
        }
        catch
        {
            launched = false;
        }

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
