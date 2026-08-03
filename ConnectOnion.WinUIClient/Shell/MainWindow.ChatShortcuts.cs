using ConnectOnion.WinUIClient.Services;
using ConnectOnion.WinUIClient.Views;
using Microsoft.UI.Xaml.Input;

namespace ConnectOnion.WinUIClient;

public sealed partial class MainWindow
{
    private void RegisterChatAccelerators()
        => RootGrid.KeyDown += ChatShortcut_KeyDown;

    /// <summary>
    /// Dispatches shortcuts that only make sense while a conversation is visible. This handler
    /// intentionally remains active while the composer text box has focus: the mode shortcut
    /// uses modifiers and does not conflict with typing or the composer's Enter behavior.
    /// </summary>
    private async void ChatShortcut_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Handled || IsModalOverlayOpen) return;
        if (MatchShortcut(e) != KeyboardShortcutCatalog.Ids.CycleChatMode) return;
        if (ContentFrame.Content is not ChatPage page) return;

        e.Handled = true;
        await page.CycleApprovalModeAsync();
    }
}
